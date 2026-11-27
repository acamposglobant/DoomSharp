using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_maputl.c intercept routines and p_map.c P_SlideMove.
/// </summary>
public static partial class MapObject
{
    // ---- intercept globals (p_maputl.c:545-550) ----
    private struct Intercept
    {
        public int Frac;        // fixed
        public bool IsALine;
        public Line? Line;
    
    }

    private const int MaxIntercepts = 128;
    private static readonly Intercept[] _intercepts = new Intercept[MaxIntercepts];
    private static int _interceptP;

    internal struct DivLine
    {
        public int X, Y, Dx, Dy;
    }

    private static DivLine _trace;
    private static bool _earlyOut;


    // slide globals (p_map.c:620-630)
    private static Mobj _slidemo = null!;
    private static int _bestSlideFrac;
    private static Line _bestSlideLine = null!;
    private static int _secondSlideFrac;
    private static Line? _secondSlideLine;
    private static int _tmXMove;
    private static int _tmYMove;

    /// <summary>P_AproxDistance (p_maputl.c:60-70).</summary>
    public static int AproxDistance(int dx, int dy)
    {
        if (dx < 0)
        {
            dx = -dx;
        }
        if (dy < 0)
        {
            dy = -dy;
        }
        return dx + dy - ((dx < dy ? dx : dy) >> 1);
    }

    /// <summary>
    /// P_InterceptVector (p_maputl.c:231-252) — exact >>8 pre-shifts.
    /// </summary>
    internal static int InterceptVector(ref DivLine v2, ref DivLine v1)
    {
        int den = FixedPoint.FixedMul(v1.Dy >> 8, v2.Dx)
                  - FixedPoint.FixedMul(v1.Dx >> 8, v2.Dy);

        if (den == 0)
        {
            return 0;
        }

        int num = FixedPoint.FixedMul((v1.X - v2.X) >> 8, v1.Dy)
                  + FixedPoint.FixedMul((v2.Y - v1.Y) >> 8, v1.Dx);

        return FixedPoint.FixedDiv(num, den);
    }
}

public static partial class MapObject
{
    private const int PtAddLines = 1;
    private const int PtEarlyOut = 4;

    /// <summary>MAPBTOFRAC (p_local.h) — MAPBLOCKSHIFT-FRACBITS.</summary>
    private const int MapBToFrac = GameLevel.MapBlockShift - FixedPoint.FracBits;

    /// <summary>PIT_AddLineIntercepts (p_maputl.c:560-620).</summary>
    private static bool AddLineIntercepts(Line ld)
    {
        int s1, s2;

        // avoid precision problems with two routines
        if (_trace.Dx > FixedPoint.FracUnit * 16
            || _trace.Dy > FixedPoint.FracUnit * 16
            || _trace.Dx < -FixedPoint.FracUnit * 16
            || _trace.Dy < -FixedPoint.FracUnit * 16)
        {
            s1 = PointOnDivlineSide(Level!.Vertexes[ld.V1].X, Level.Vertexes[ld.V1].Y, ref _trace);
            s2 = PointOnDivlineSide(Level.Vertexes[ld.V2].X, Level.Vertexes[ld.V2].Y, ref _trace);
        }
        else
        {
            s1 = PointOnLineSide(_trace.X, _trace.Y, ld);
            s2 = PointOnLineSide(_trace.X + _trace.Dx, _trace.Y + _trace.Dy, ld);
        }

        if (s1 == s2)
        {
            return true;    // line isn't crossed
        }

        // hit the line
        var dl = MakeDivline(ld);
        int frac = InterceptVector(ref _trace, ref dl);

        if (frac < 0)
        {
            return true;    // behind source
        }

        // try to early out the check
        if (_earlyOut
            && frac < FixedPoint.FracUnit
            && ld.BackSector == -1)
        {
            return false;   // stop checking
        }

        if (_interceptP < MaxIntercepts)
        {
            _intercepts[_interceptP].Frac = frac;
            _intercepts[_interceptP].IsALine = true;
            _intercepts[_interceptP].Line = ld;
            _interceptP++;
        }

        return true;
    }

    /// <summary>P_MakeDivline (p_maputl.c:200-210).</summary>
    private static DivLine MakeDivline(Line li)
    {
        DivLine dl;
        dl.X = Level!.Vertexes[li.V1].X;
        dl.Y = Level.Vertexes[li.V1].Y;
        dl.Dx = li.Dx;
        dl.Dy = li.Dy;
        return dl;
    }

    /// <summary>P_PointOnDivlineSide (p_maputl.c:100-140).</summary>
    private static int PointOnDivlineSide(int x, int y, ref DivLine line)
    {
        if (line.Dx == 0)
        {
            return x <= line.X ? 1 : 0;
        }

        if (line.Dy == 0)
        {
            return y <= line.Y ? 0 : 1;
        }

        int dx = x - line.X;
        int dy = y - line.Y;

        // try to quickly resolve the sign
        if ((line.Dy ^ line.Dx ^ x ^ y) < 0)
        {
            return (line.Dy ^ line.Dx ^ x ^ y) < 0 ? 1 : 0;
        }

        int left = FixedPoint.FixedMul(line.Dy >> 8, (x - line.X) >> 8);
        int right = FixedPoint.FixedMul(dy >> 8, line.Dx >> 8);

        return left == right ? 2 : right > left ? 0 : 1;
    }
}

public static partial class MapObject
{
    /// <summary>P_TraverseIntercepts (p_maputl.c:683-730) — selection sort by frac.</summary>
    private static bool TraverseIntercepts(Func<Intercept, bool> trav, int maxFrac)
    {
        int count = _interceptP;

        while (count-- > 0)
        {
            int dist = int.MaxValue;
            int inIndex = -1;
            for (int scan = 0; scan < _interceptP; scan++)
            {
                if (_intercepts[scan].Frac < dist)
                {
                    dist = _intercepts[scan].Frac;
                    inIndex = scan;
                }
            }

            if (dist > maxFrac)
            {
                return true;    // checked everything in range
            }

            if (inIndex < 0 || !trav(_intercepts[inIndex]))
            {
                return false;
            }

            // remove the processed intercept
            _intercepts[inIndex].Frac = int.MaxValue;
        }
        return true;
    }

    /// <summary>P_PathTraverse (p_maputl.c:745-880) — lines only.</summary>
    private static bool PathTraverse(int x1, int y1, int x2, int y2, int flags, Func<Intercept, bool> trav)
    {
        int partial;
        int xIntercept, yIntercept;
        int mapX, mapY;
        int mapXStep, mapYStep;
        int xStep = 0, yStep = 0;

        _earlyOut = (flags & PtEarlyOut) != 0;

        ValidCount++;
        _interceptP = 0;

        if (((x1 - GameLevel.BlockMapOrgX) & (GameLevel.MapBlockSize - 1)) == 0)
        {
            x1 += FixedPoint.FracUnit;  // don't side exactly on a line
        }
        if (((y1 - GameLevel.BlockMapOrgY) & (GameLevel.MapBlockSize - 1)) == 0)
        {
            y1 += FixedPoint.FracUnit;  // don't side exactly on a line
        }

        _trace.X = x1;
        _trace.Y = y1;
        _trace.Dx = x2 - x1;
        _trace.Dy = y2 - y1;

        int lx1 = x1 - GameLevel.BlockMapOrgX;
        int ly1 = y1 - GameLevel.BlockMapOrgY;
        int xt1 = lx1 >> GameLevel.MapBlockShift;
        int yt1 = ly1 >> GameLevel.MapBlockShift;

        int lx2 = x2 - GameLevel.BlockMapOrgX;
        int ly2 = y2 - GameLevel.BlockMapOrgY;
        int xt2 = lx2 >> GameLevel.MapBlockShift;
        int yt2 = ly2 >> GameLevel.MapBlockShift;

        if (xt2 > xt1)
        {
            mapXStep = 1;
            partial = FixedPoint.FracUnit - ((lx1 >> MapBToFrac) & (FixedPoint.FracUnit - 1));
            yStep = FixedPoint.FixedDiv(y2 - y1, Math.Abs(x2 - x1));
        }
        else if (xt2 < xt1)
        {
            mapXStep = -1;
            partial = (lx1 >> MapBToFrac) & (FixedPoint.FracUnit - 1);
            yStep = FixedPoint.FixedDiv(y2 - y1, Math.Abs(x2 - x1));
        }
        else
        {
            mapXStep = 0;
            partial = FixedPoint.FracUnit;
            yStep = 256 * FixedPoint.FracUnit;
        }

        yIntercept = (y1 >> MapBToFrac) + FixedPoint.FixedMul(partial, yStep);

        if (yt2 > yt1)
        {
            mapYStep = 1;
            partial = FixedPoint.FracUnit - ((ly1 >> MapBToFrac) & (FixedPoint.FracUnit - 1));
            xStep = FixedPoint.FixedDiv(x2 - x1, Math.Abs(y2 - y1));
        }
        else if (yt2 < yt1)
        {
            mapYStep = -1;
            partial = (ly1 >> MapBToFrac) & (FixedPoint.FracUnit - 1);
            xStep = FixedPoint.FixedDiv(x2 - x1, Math.Abs(y2 - y1));
        }
        else
        {
            mapYStep = 0;
            partial = FixedPoint.FracUnit;
            xStep = 256 * FixedPoint.FracUnit;
        }
        xIntercept = (x1 >> MapBToFrac) + FixedPoint.FixedMul(partial, xStep);

        // Step through map blocks.
        mapX = xt1;
        mapY = yt1;

        for (int count = 0; count < 64; count++)
        {
            if ((flags & PtAddLines) != 0)
            {
                if (!BlockLinesIterator(mapX, mapY, AddLineIntercepts))
                {
                    return false;   // early out
                }
            }

            if (mapX == xt2 && mapY == yt2)
            {
                break;
            }

            if ((yIntercept >> FixedPoint.FracBits) == mapY)
            {
                yIntercept += yStep;
                mapX += mapXStep;
            }
            else if ((xIntercept >> FixedPoint.FracBits) == mapX)
            {
                xIntercept += xStep;
                mapY += mapYStep;
            }
        }

        // go through the sorted list
        return TraverseIntercepts(trav, FixedPoint.FracUnit);
    }
}

public static partial class MapObject
{
    /// <summary>PTR_SlideTraverse (p_map.c:636-680).</summary>
    private static bool SlideTraverse(Intercept intercept)
    {
        if (!intercept.IsALine)
        {
            throw new DoomFatalException("PTR_SlideTraverse: not a line?");
        }

        var li = _bestSlideLine = intercept.Line!;

        if ((li.Flags & LineDefFlags.TwoSided) == 0)
        {
            if (PointOnLineSide(_slidemo.X, _slidemo.Y, li) != 0)
            {
                // don't hit the back side
                return true;
            }
            return SlideBlocking(intercept);
        }

        // set openrange, opentop, openbottom
        LineOpening(li);

        if (OpenRange < _slidemo.Height)
        {
            return SlideBlocking(intercept);    // doesn't fit
        }

        if (OpenTop - _slidemo.Z < _slidemo.Height)
        {
            return SlideBlocking(intercept);    // mobj is too high
        }

        if (OpenBottom - _slidemo.Z > MaxStepHeight)
        {
            return SlideBlocking(intercept);    // too big a step up
        }

        // this line doesn't block movement
        return true;
    }

    private static bool SlideBlocking(Intercept intercept)
    {
        if (intercept.Frac < _bestSlideFrac)
        {
            _secondSlideFrac = _bestSlideFrac;
            _secondSlideLine = _bestSlideLine;
            _bestSlideFrac = intercept.Frac;
            _bestSlideLine = intercept.Line!;
        }
        return true;    // keep going
    }

    /// <summary>P_HitSlideLine (p_map.c:584-630).</summary>
    private static void HitSlideLine(Line ld)
    {
        if (ld.SlopeType == SlopeType.Horizontal)
        {
            _tmYMove = 0;
            return;
        }

        if (ld.SlopeType == SlopeType.Vertical)
        {
            _tmXMove = 0;
            return;
        }

        int side = PointOnLineSide(_slidemo.X, _slidemo.Y, ld);

        uint lineAngle = RendererMath.PointToAngle2(0, 0, ld.Dx, ld.Dy);

        if (side == 1)
        {
            lineAngle += AngleTables.Ang180;
        }

        uint moveAngle = RendererMath.PointToAngle2(0, 0, _tmXMove, _tmYMove);
        uint deltaAngle = moveAngle - lineAngle;

        if (deltaAngle > AngleTables.Ang180)
        {
            deltaAngle += AngleTables.Ang180;
        }

        lineAngle >>= AngleTables.AngleToFineShift;
        deltaAngle >>= AngleTables.AngleToFineShift;

        int moveLen = AproxDistance(_tmXMove, _tmYMove);
        int newLen = FixedPoint.FixedMul(moveLen, AngleTables.FineCosine[deltaAngle]);

        _tmXMove = FixedPoint.FixedMul(newLen, AngleTables.FineCosine[lineAngle]);
        _tmYMove = FixedPoint.FixedMul(newLen, AngleTables.FineSine[lineAngle]);
    }
}

public static partial class MapObject
{
    /// <summary>P_SlideMove (p_map.c:687-790) — the exact hitcount==3 stairstep fallback.</summary>
    public static void SlideMove(Mobj mo)
    {
        int hitCount = 0;

        _slidemo = mo;

        while (true)
        {
            // retry:
            if (++hitCount == 3)
            {
                break;      // don't loop forever -> stairstep
            }

            // trace along the three leading corners
            int leadX = mo.MomX > 0 ? mo.X + mo.Radius : mo.X - mo.Radius;
            int trailX = mo.MomX > 0 ? mo.X - mo.Radius : mo.X + mo.Radius;
            int leadY = mo.MomY > 0 ? mo.Y + mo.Radius : mo.Y - mo.Radius;
            int trailY = mo.MomY > 0 ? mo.Y - mo.Radius : mo.Y + mo.Radius;

            _bestSlideFrac = FixedPoint.FracUnit + 1;

            PathTraverse(leadX, leadY, leadX + mo.MomX, leadY + mo.MomY, PtAddLines, SlideTraverse);
            PathTraverse(trailX, leadY, trailX + mo.MomX, leadY + mo.MomY, PtAddLines, SlideTraverse);
            PathTraverse(leadX, trailY, leadX + mo.MomX, trailY + mo.MomY, PtAddLines, SlideTraverse);

            // move up to the wall
            if (_bestSlideFrac == FixedPoint.FracUnit + 1)
            {
                break;      // the move must have hit the middle, so stairstep
            }

            // fudge a bit to make sure it doesn't hit
            _bestSlideFrac -= 0x800;
            if (_bestSlideFrac > 0)
            {
                int fudgeX = FixedPoint.FixedMul(mo.MomX, _bestSlideFrac);
                int fudgeY = FixedPoint.FixedMul(mo.MomY, _bestSlideFrac);

                if (!TryMove(mo, mo.X + fudgeX, mo.Y + fudgeY))
                {
                    break;  // -> stairstep
                }
            }

            // Now continue along the wall. First calculate remainder.
            _bestSlideFrac = FixedPoint.FracUnit - (_bestSlideFrac + 0x800);

            if (_bestSlideFrac > FixedPoint.FracUnit)
            {
                _bestSlideFrac = FixedPoint.FracUnit;
            }

            if (_bestSlideFrac <= 0)
            {
                return;
            }

            _tmXMove = FixedPoint.FixedMul(mo.MomX, _bestSlideFrac);
            _tmYMove = FixedPoint.FixedMul(mo.MomY, _bestSlideFrac);

            HitSlideLine(_bestSlideLine);

            mo.MomX = _tmXMove;
            mo.MomY = _tmYMove;

            if (!TryMove(mo, mo.X + _tmXMove, mo.Y + _tmYMove))
            {
                continue;   // retry
            }

            return;
        }

        // stairstep: the move must have hit the middle, so stairstep
        if (!TryMove(mo, mo.X, mo.Y + mo.MomY))
        {
            TryMove(mo, mo.X + mo.MomX, mo.Y);
        }
    }
}

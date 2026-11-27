// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      Renderer geometry, angle and projection math.
//      Migration of Legacy/linuxdoom-1.10/r_main.c utility functions:
//      R_PointOnSide, R_PointOnSegSide, R_PointToAngle,
//      R_PointToAngle2, R_PointToDist, R_ScaleFromGlobalAngle,
//      R_PointInSubsector, R_AddPointToBox.
//      See docs/adr/ADR-004 for tracing; DOOM-030b plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// Renderer geometry / angle math (r_main.c globals and utility
/// functions). All angle arithmetic uses <c>uint</c> (angle_t BAM)
/// semantics, including the two's-complement wraparound of
/// <c>-tantoangle[..]</c> and the <c>ANG90-1-</c> fencepost asymmetry.
/// </summary>
public static class RendererMath
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(RendererMath));

    // --- r_main.c globals owned here (set by ViewSetup / callers) ---

    /// <summary>viewx (r_main.c:75) — set by ViewSetup.SetupFrame or PointToAngle2.</summary>
    public static int ViewX;

    /// <summary>viewy (r_main.c:76).</summary>
    public static int ViewY;

    /// <summary>rw_normalangle (r_segs.c global, read by R_ScaleFromGlobalAngle).</summary>
    public static int RwNormalAngle;

    /// <summary>rw_distance (r_segs.c global, read by R_ScaleFromGlobalAngle).</summary>
    public static int RwDistance;

    /// <summary>detailshift (r_main.c:87) — 0 = high detail, 1 = low.</summary>
    public static int DetailShift;

    /// <summary>projection (r_main.c:66) — set by ViewSetup.ExecuteSetViewSize.</summary>
    public static int Projection;

    /// <summary>viewangle (r_main.c:79) — set by ViewSetup.SetupFrame.</summary>
    public static uint ViewAngle;

    /// <summary>viewz (r_main.c:77) — set by ViewSetup.SetupFrame.</summary>
    public static int ViewZ;

    /// <summary>viewsin (r_main.c:82) — set by ViewSetup.SetupFrame.</summary>
    public static int ViewSin;

    /// <summary>viewcos (r_main.c:82) — set by ViewSetup.SetupFrame.</summary>
    public static int ViewCos;

    /// <summary>viewangleoffset (r_main.c:52).</summary>
    public static int ViewAngleOffset;

    /// <summary>validcount, incremented per check (r_main.c:55).</summary>
    public static int ValidCount = 1;

    /// <summary>framecount (r_main.c:69).</summary>
    public static int FrameCount;

    /// <summary>sscount (r_main.c:71).</summary>
    public static int SsCount;

    /// <summary>linecount (r_main.c:72).</summary>
    public static int LineCount;

    /// <summary>loopcount (r_main.c:73).</summary>
    public static int LoopCount;

    /// <summary>extralight (r_main.c:121).</summary>
    public static int ExtraLight;

    /// <summary>fixedcolormap (r_main.c:58) — null = none (C NULL).</summary>
    public static byte[]? FixedColormap;

    /// <summary>
    /// R_AddPointToBox (r_main.c:139-152): expand a bbox so it
    /// encloses a point. Box layout is BOXLEFT/BOXTOP/BOXRIGHT/BOXBOTTOM
    /// (r_defs.h:60-65).
    /// </summary>
    public static void AddPointToBox(int x, int y, int[] box)
    {
        if (x < box[BBox.Left])
        {
            box[BBox.Left] = x;
        }

        if (x > box[BBox.Right])
        {
            box[BBox.Right] = x;
        }

        if (y < box[BBox.Bottom])
        {
            box[BBox.Bottom] = y;
        }

        if (y > box[BBox.Top])
        {
            box[BBox.Top] = y;
        }
    }

    /// <summary>
    /// R_PointOnSide (r_main.c:162-211): traverse BSP (sub)tree, check
    /// point against partition plane. Returns side 0 (front) or 1 (back).
    /// Preserves the axis fast paths (with their &lt;= boundary bias),
    /// the sign-bit quick reject, and the FixedMul cross-product.
    /// </summary>
    public static int PointOnSide(int x, int y, Node node)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("PointOnSide({X}, {Y}, node dx={Dx} dy={Dy})", x, y, node.Dx, node.Dy);
        }

        if (node.Dx == 0)
        {
            if (x <= node.X)
            {
                return node.Dy > 0 ? 1 : 0;
            }

            return node.Dy < 0 ? 1 : 0;
        }

        if (node.Dy == 0)
        {
            if (y <= node.Y)
            {
                return node.Dx < 0 ? 1 : 0;
            }

            return node.Dx > 0 ? 1 : 0;
        }

        var dx = x - node.X;
        var dy = y - node.Y;

        // Try to quickly decide by looking at sign bits.
        if (((node.Dy ^ node.Dx ^ dx ^ dy) & unchecked((int)0x80000000)) != 0)
        {
            if (((node.Dy ^ dx) & unchecked((int)0x80000000)) != 0)
            {
                // (left is negative)
                return 1;
            }

            return 0;
        }

        var left = FixedPoint.FixedMul(node.Dy >> FixedPoint.FracBits, dx);
        var right = FixedPoint.FixedMul(dy, node.Dx >> FixedPoint.FracBits);

        if (right < left)
        {
            // front side
            return 0;
        }

        // back side
        return 1;
    }

    /// <summary>
    /// R_PointOnSegSide (r_main.c:215-274): same algorithm as
    /// PointOnSide but against a seg's v1/v2 vertex coordinates.
    /// The C reads line->v1->x/y and line->v2->x/y; here the caller
    /// passes the resolved vertex coordinates (LevelData.Vertexes).
    /// </summary>
    public static int PointOnSegSide(int x, int y, int v1x, int v1y, int v2x, int v2y)
    {
        var lx = v1x;
        var ly = v1y;

        var ldx = v2x - lx;
        var ldy = v2y - ly;

        if (ldx == 0)
        {
            if (x <= lx)
            {
                return ldy > 0 ? 1 : 0;
            }

            return ldy < 0 ? 1 : 0;
        }

        if (ldy == 0)
        {
            if (y <= ly)
            {
                return ldx < 0 ? 1 : 0;
            }

            return ldx > 0 ? 1 : 0;
        }

        var dx = x - lx;
        var dy = y - ly;

        // Try to quickly decide by looking at sign bits.
        if (((ldy ^ ldx ^ dx ^ dy) & unchecked((int)0x80000000)) != 0)
        {
            if (((ldy ^ dx) & unchecked((int)0x80000000)) != 0)
            {
                // (left is negative)
                return 1;
            }

            return 0;
        }

        var left = FixedPoint.FixedMul(ldy >> FixedPoint.FracBits, dx);
        var right = FixedPoint.FixedMul(dy, ldx >> FixedPoint.FracBits);

        if (right < left)
        {
            // front side
            return 0;
        }

        // back side
        return 1;
    }

    /// <summary>
    /// R_PointToAngle (r_main.c:292-374): global angle from cartesian
    /// coordinates relative to viewx/viewy. Octant dispatch + SlopeDiv +
    /// tantoangle lookup. angle_t is unsigned BAM: the negative-octant
    /// forms rely on two's-complement wraparound of uint arithmetic.
    /// </summary>
    public static uint PointToAngle(int x, int y)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("PointToAngle({X}, {Y}) view=({ViewX}, {ViewY})", x, y, ViewX, ViewY);
        }

        x -= ViewX;
        y -= ViewY;

        if (x == 0 && y == 0)
        {
            return 0;
        }

        if (x >= 0)
        {
            if (y >= 0)
            {
                if (x > y)
                {
                    // octant 0
                    return AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)y, (uint)x)];
                }

                // octant 1
                return AngleTables.Ang90 - 1u - AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)x, (uint)y)];
            }
            else
            {
                y = -y;

                if (x > y)
                {
                    // octant 8
                    return 0u - AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)y, (uint)x)];
                }
                else
                {
                    // octant 7
                    return AngleTables.Ang270 + AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)x, (uint)y)];
                }
            }
        }
        else
        {
            x = -x;

            if (y >= 0)
            {
                if (x > y)
                {
                    // octant 3
                    return AngleTables.Ang180 - 1u - AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)y, (uint)x)];
                }
                else
                {
                    // octant 2
                    return AngleTables.Ang90 + AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)x, (uint)y)];
                }
            }
            else
            {
                y = -y;

                if (x > y)
                {
                    // octant 4
                    return AngleTables.Ang180 + AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)y, (uint)x)];
                }
                else
                {
                    // octant 5
                    return AngleTables.Ang270 - 1u - AngleTables.TanToAngle[AngleTables.SlopeDiv((uint)x, (uint)y)];
                }
            }
        }
    }

    /// <summary>
    /// R_PointToAngle2 (r_main.c:377-388): sets viewx/viewy then
    /// delegates to PointToAngle. The view-position side effect is
    /// preserved exactly (documented deviation: explicit state write).
    /// </summary>
    public static uint PointToAngle2(int x1, int y1, int x2, int y2)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("PointToAngle2({X1}, {Y1}, {X2}, {Y2})", x1, y1, x2, y2);
        }

        ViewX = x1;
        ViewY = y1;

        return PointToAngle(x2, y2);
    }

    /// <summary>
    /// R_PointToDist (r_main.c:391-418): distance from view position.
    /// Uses tantoangle + finesine; dx/dy are swapped so dx &gt;= dy.
    /// </summary>
    public static int PointToDist(int x, int y)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("PointToDist({X}, {Y}) view=({ViewX}, {ViewY})", x, y, ViewX, ViewY);
        }

        var dx = IntAbs(x - ViewX);
        var dy = IntAbs(y - ViewY);

        if (dy > dx)
        {
            (dx, dy) = (dy, dx);
        }

        var angle = (int)(((uint)AngleTables.TanToAngle[FixedPoint.FixedDiv(dy, dx) >> AngleTables.DBits] + AngleTables.Ang90) >> AngleTables.AngleToFineShift);

        // use as cosine
        return FixedPoint.FixedDiv(dx, AngleTables.FineSine[angle]);
    }

    /// <summary>
    /// R_ScaleFromGlobalAngle (r_main.c:453-502): texture mapping scale
    /// for the current line at the given angle. rw_distance must be
    /// calculated first. Clamps to [256, 64*FRACUNIT]; the
    /// den &gt; num&gt;&gt;16 guard falls through to 64*FRACUNIT.
    /// </summary>
    public static int ScaleFromGlobalAngle(uint visangle)
    {
        int scale;
        int sinea;
        int sineb;
        int num;
        int den;

        // angle_t arithmetic is unsigned; keep the shift unsigned so
        // angles >= 0x80000000 index finesine positively (r_main.c:387).
        var anglea = AngleTables.Ang90 + (visangle - ViewAngle);
        var angleb = AngleTables.Ang90 + (visangle - unchecked((uint)RwNormalAngle));

        // both sines are always positive
        sinea = AngleTables.FineSine[(int)(anglea >> AngleTables.AngleToFineShift)];
        sineb = AngleTables.FineSine[(int)(angleb >> AngleTables.AngleToFineShift)];
        num = FixedPoint.FixedMul(Projection, sineb) << DetailShift;
        den = FixedPoint.FixedMul(RwDistance, sinea);

        if (den > (num >> 16))
        {
            scale = FixedPoint.FixedDiv(num, den);

            if (scale > 64 * FixedPoint.FracUnit)
            {
                scale = 64 * FixedPoint.FracUnit;
            }
            else if (scale < 256)
            {
                scale = 256;
            }
        }
        else
        {
            scale = 64 * FixedPoint.FracUnit;
        }

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("ScaleFromGlobalAngle({VisAngle}) = {Scale}", visangle, scale);
        }

        return scale;
    }

    /// <summary>
    /// R_PointInSubsector (r_main.c:800-823): BSP tree walk from the
    /// root (numnodes-1) using R_PointOnSide; NF_SUBSECTOR marks a
    /// subsector child. Single-subsector maps (numnodes == 0) return
    /// subsectors[0].
    /// </summary>
    public static Subsector PointInSubsector(int x, int y, LevelData level)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("PointInSubsector({X}, {Y}, {NumNodes} nodes)", x, y, level.Nodes.Length);
        }

        if (level.Nodes.Length == 0)
        {
            // single subsector is a special case
            return level.Subsectors[0];
        }

        var nodeNum = level.Nodes.Length - 1;

        while ((nodeNum & NodeFlags.SubSector) == 0)
        {
            var node = level.Nodes[nodeNum];
            var side = PointOnSide(x, y, node);
            nodeNum = node.Children[side];
        }

        return level.Subsectors[nodeNum & ~NodeFlags.SubSector];
    }

    /// <summary>C abs() for int (doomtype.h / r_main.c:402).</summary>
    private static int IntAbs(int value) => value < 0 ? -value : value;
}

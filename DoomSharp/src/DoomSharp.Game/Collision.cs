using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_map.c: movement clipping — P_CheckPosition, P_TryMove,
/// PIT_CheckLine, PIT_CheckThing, P_SlideMove.
/// </summary>
public static partial class MapObject
{
    // ---- p_map.c globals ----
    private static Mobj? _tmThing;
    private static uint _tmFlags;
    private static int _tmX;
    private static int _tmY;
    private static readonly int[] _tmBBox = new int[4];
    private static int _tmFloorZ;
    private static int _tmCeilingZ;
    private static int _tmDropOffZ;
    internal static bool FloatOk;
    private static readonly Line?[] _spechit = new Line?[8];
    private static int _numSpechit;

    // P_LineOpening results (p_maputl.c:294-297).
    public static int OpenTop;
    public static int OpenBottom;
    public static int OpenRange;
    public static int LowFloor;

    /// <summary>MAXRADIUS (p_local.h:52).</summary>
    public const int MaxRadius = 32 * FixedPoint.FracUnit;

    /// <summary>P_LineOpening (p_maputl.c:300-340).</summary>
    public static void LineOpening(Line linedef)
    {
        if (linedef.Sidenum[1] == -1)
        {
            // single sided line
            OpenRange = 0;
            return;
        }

        var front = Level!.Sectors[linedef.FrontSector];
        var back = Level.Sectors[linedef.BackSector];

        OpenTop = front.CeilingHeight < back.CeilingHeight
            ? front.CeilingHeight
            : back.CeilingHeight;

        if (front.FloorHeight > back.FloorHeight)
        {
            OpenBottom = front.FloorHeight;
            LowFloor = back.FloorHeight;
        }
        else
        {
            OpenBottom = back.FloorHeight;
            LowFloor = front.FloorHeight;
        }

        OpenRange = OpenTop - OpenBottom;
    }

    /// <summary>P_CheckPosition (p_map.c:355-440).</summary>
    public static bool CheckPosition(Mobj thing, int x, int y)
    {
        _tmThing = thing;
        _tmFlags = thing.Flags;

        _tmX = x;
        _tmY = y;

        _tmBBox[BBox.Top] = y + thing.Radius;
        _tmBBox[BBox.Bottom] = y - thing.Radius;
        _tmBBox[BBox.Right] = x + thing.Radius;
        _tmBBox[BBox.Left] = x - thing.Radius;

        var newSubSec = RendererMath.PointInSubsector(x, y, Level!);
        CeilingLine = null;

        // The base floor / ceiling is from the subsector that contains
        // the point. Any contacted lines the step closer together will
        // adjust them.
        int ssIndex = Array.IndexOf(Level!.Subsectors, newSubSec);
        int secIndex = SubsectorSector(ssIndex);
        _tmFloorZ = _tmDropOffZ = Level.Sectors[secIndex].FloorHeight;
        _tmCeilingZ = Level.Sectors[secIndex].CeilingHeight;

        ValidCount++;
        _numSpechit = 0;

        if ((_tmFlags & MobjFlags.NoClip) != 0)
        {
            return true;
        }

        // Check things first, possibly picking things up.
        int xl = (_tmBBox[BBox.Left] - GameLevel.BlockMapOrgX - MaxRadius) >> GameLevel.MapBlockShift;
        int xh = (_tmBBox[BBox.Right] - GameLevel.BlockMapOrgX + MaxRadius) >> GameLevel.MapBlockShift;
        int yl = (_tmBBox[BBox.Bottom] - GameLevel.BlockMapOrgY - MaxRadius) >> GameLevel.MapBlockShift;
        int yh = (_tmBBox[BBox.Top] - GameLevel.BlockMapOrgY + MaxRadius) >> GameLevel.MapBlockShift;

        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
            {
                if (!BlockThingsIterator(bx, by, CheckThing))
                {
                    return false;
                }
            }
        }

        // check lines
        xl = (_tmBBox[BBox.Left] - GameLevel.BlockMapOrgX) >> GameLevel.MapBlockShift;
        xh = (_tmBBox[BBox.Right] - GameLevel.BlockMapOrgX) >> GameLevel.MapBlockShift;
        yl = (_tmBBox[BBox.Bottom] - GameLevel.BlockMapOrgY) >> GameLevel.MapBlockShift;
        yh = (_tmBBox[BBox.Top] - GameLevel.BlockMapOrgY) >> GameLevel.MapBlockShift;

        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
            {
                if (!BlockLinesIterator(bx, by, CheckLine))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

public static partial class MapObject
{
    /// <summary>P_TryMove (p_map.c:445-510).</summary>
    public static bool TryMove(Mobj thing, int x, int y)
    {
        int oldX = thing.X;
        int oldY = thing.Y;

        FloatOk = false;
        if (!CheckPosition(thing, x, y))
        {
            return false;       // solid wall or thing
        }

        if ((thing.Flags & MobjFlags.NoClip) == 0)
        {
            if (_tmCeilingZ - _tmFloorZ < thing.Height)
            {
                return false;   // doesn't fit
            }

            FloatOk = true;

            if ((thing.Flags & MobjFlags.Teleport) == 0
                && _tmCeilingZ - thing.Z < thing.Height)
            {
                return false;   // mobj must lower itself to fit
            }

            if ((thing.Flags & MobjFlags.Teleport) == 0
                && _tmFloorZ - thing.Z > MaxStepHeight)
            {
                return false;   // too big a step up
            }

            if ((thing.Flags & (MobjFlags.DropOff | MobjFlags.Float)) == 0
                && _tmFloorZ - _tmDropOffZ > MaxStepHeight)
            {
                return false;   // don't stand over a dropoff
            }
        }

        // the move is ok, so link the thing into its new position
        UnsetThingPosition(thing);

        thing.FloorZ = _tmFloorZ;
        thing.CeilingZ = _tmCeilingZ;
        thing.X = x;
        thing.Y = y;

        SetThingPosition(thing);

        // if any special lines were hit, do the effect
        // (line specials are DOOM-022 scope; the crossed-side check is
        //  preserved structurally but P_CrossSpecialLine is a no-op here)
        if ((thing.Flags & (MobjFlags.Teleport | MobjFlags.NoClip)) == 0)
        {
            while (_numSpechit-- > 0)
            {
                // see if the line was crossed
                var ld = _spechit[_numSpechit];
                if (ld is null)
                {
                    continue;
                }
                int side = PointOnLineSide(thing.X, thing.Y, ld);
                int oldSide = PointOnLineSide(oldX, oldY, ld);
                if (side != oldSide && ld.Special != 0)
                {
                    // P_CrossSpecialLine — DOOM-022 (line specials)
                }
            }
        }

        return true;
    }

    /// <summary>PIT_CheckLine (p_map.c:197-265).</summary>
    private static bool CheckLine(Line ld)
    {
        if (_tmBBox[BBox.Right] <= ld.BBox[BBox.Left]
            || _tmBBox[BBox.Left] >= ld.BBox[BBox.Right]
            || _tmBBox[BBox.Top] <= ld.BBox[BBox.Bottom]
            || _tmBBox[BBox.Bottom] >= ld.BBox[BBox.Top])
        {
            return true;
        }

        if (BoxOnLineSide(_tmBBox, ld) != -1)
        {
            return true;
        }

        // A line has been hit
        if (ld.BackSector == -1)
        {
            return false;       // one sided line
        }

        if ((_tmThing!.Flags & MobjFlags.Missile) == 0)
        {
            if ((ld.Flags & LineDefFlags.Blocking) != 0)
            {
                return false;   // explicitly blocking everything
            }

            if (_tmThing.Player is null && (ld.Flags & LineDefFlags.BlockMonsters) != 0)
            {
                return false;   // block monsters only
            }
        }

        // set openrange, opentop, openbottom
        LineOpening(ld);

        // adjust floor / ceiling heights
        if (OpenTop < _tmCeilingZ)
        {
            _tmCeilingZ = OpenTop;
            CeilingLine = ld;
        }

        if (OpenBottom > _tmFloorZ)
        {
            _tmFloorZ = OpenBottom;
        }

        if (LowFloor < _tmDropOffZ)
        {
            _tmDropOffZ = LowFloor;
        }

        // if contacted a special line, add it to the list
        if (ld.Special != 0 && _numSpechit < _spechit.Length)
        {
            _spechit[_numSpechit++] = ld;
        }

        return true;
    }

    /// <summary>PIT_CheckThing (p_map.c:270-345).</summary>
    private static bool CheckThing(Mobj thing)
    {
        var tmThing = _tmThing!;

        if ((thing.Flags & (MobjFlags.Solid | MobjFlags.Special | MobjFlags.Shootable)) == 0)
        {
            return true;
        }

        int blockDist = thing.Radius + tmThing.Radius;

        if (Math.Abs(thing.X - _tmX) >= blockDist
            || Math.Abs(thing.Y - _tmY) >= blockDist)
        {
            // didn't hit it
            return true;
        }

        // don't clip against self
        if (thing == tmThing)
        {
            return true;
        }

        // check for skulls slamming into things
        if ((tmThing.Flags & MobjFlags.SkullFly) != 0)
        {
            // damage deferred to the combat slice (P_DamageMobj)
            tmThing.Flags &= ~MobjFlags.SkullFly;
            tmThing.MomX = tmThing.MomY = tmThing.MomZ = 0;
            SetMobjState(tmThing, InfoTables.MobjInfo[tmThing.Type].SpawnState);
            return false;       // stop moving
        }

        // missiles can hit other things
        if ((tmThing.Flags & MobjFlags.Missile) != 0)
        {
            // see if it went over / under
            if (tmThing.Z > thing.Z + thing.Height)
            {
                return true;    // overhead
            }
            if (tmThing.Z + tmThing.Height < thing.Z)
            {
                return true;    // underneath
            }

            if (tmThing.Target is not null
                && tmThing.Target.Type == thing.Type)
            {
                // Don't hit same species as originator.
                if (thing == tmThing.Target)
                {
                    return true;
                }

                if (thing.Type != (int)MobjType.Player)
                {
                    // Explode, but do no damage. Let players missile other players.
                    return false;
                }
            }

            if ((thing.Flags & MobjFlags.Shootable) == 0)
            {
                // didn't do any damage
                return (thing.Flags & MobjFlags.Solid) == 0;
            }

            // damage / explode — P_DamageMobj is DOOM-021b (combat)
            return false;
        }

        // check for special pickup
        if ((thing.Flags & MobjFlags.Special) != 0)
        {
            bool solid = (thing.Flags & MobjFlags.Solid) != 0;
            if ((_tmFlags & MobjFlags.Pickup) != 0)
            {
                // can remove thing — P_TouchSpecialThing is DOOM-021b
            }
            return !solid;
        }

        return (thing.Flags & MobjFlags.Solid) == 0;
    }
}

public static partial class MapObject
{
    /// <summary>P_BlockLinesIterator (p_maputl.c:477-510).</summary>
    public static bool BlockLinesIterator(int x, int y, Func<Line, bool> func)
    {
        if (x < 0 || y < 0 || x >= GameLevel.BlockMapWidth || y >= GameLevel.BlockMapHeight)
        {
            return true;
        }

        int offset = (y * GameLevel.BlockMapWidth) + x;
        var bm = Level!.BlockMap!;
        // blockmap = blockmaplump + 4 (p_setup.c) — skip the org/size header.
        offset = bm.Lump[offset + 4];

        for (int i = offset; bm.Lump[i] != -1; i++)
        {
            int lineIndex = bm.Lump[i];
            var ld = Level.Lines[lineIndex];
            if (ld.ValidCount == ValidCount)
            {
                continue;   // line has already been checked
            }
            ld.ValidCount = ValidCount;

            if (!func(ld))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>P_BlockThingsIterator (p_maputl.c:510-535).</summary>
    public static bool BlockThingsIterator(int x, int y, Func<Mobj, bool> func)
    {
        if (x < 0 || y < 0 || x >= GameLevel.BlockMapWidth || y >= GameLevel.BlockMapHeight)
        {
            return true;
        }

        for (var mobj = GameLevel.BlockLinks[(y * GameLevel.BlockMapWidth) + x];
             mobj is not null;
             mobj = mobj.BNext)
        {
            if (!func(mobj))
            {
                return false;
            }
        }
        return true;
    }
}

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_maputl.c: thing position linking (P_SetThingPosition /
/// P_UnsetThingPosition). The C's sector_t.thinglist and blocklinks are
/// game-side arrays in GameLevel; the renderer bridge registers the
/// mobj's MobjVisual in SectorThings.
/// </summary>
public static partial class MapObject
{
    /// <summary>P_SetThingPosition (p_maputl.c:396-455).</summary>
    public static void SetThingPosition(Mobj thing)
    {
        // link into subsector
        var ss = RendererMath.PointInSubsector(thing.X, thing.Y, Level!);
        thing.SubSector = ss;

        int ssIndex = Array.IndexOf(Level!.Subsectors, ss);

        if ((thing.Flags & MobjFlags.NoSector) == 0)
        {
            // invisible things don't go into the sector links
            int secIndex = SubsectorSector(ssIndex);
            thing.SectorIndex = secIndex;

            thing.SPrev = null;
            thing.SNext = GameLevel.SectorThingLists[secIndex];
            if (GameLevel.SectorThingLists[secIndex] is not null)
            {
                GameLevel.SectorThingLists[secIndex]!.SPrev = thing;
            }
            GameLevel.SectorThingLists[secIndex] = thing;

            // renderer bridge: register the visual in the sector list
            Things?.Add(ssIndex, thing.Visual);
        }

        // link into blockmap
        if ((thing.Flags & MobjFlags.NoBlockMap) == 0)
        {
            int blockX = (thing.X - GameLevel.BlockMapOrgX) >> GameLevel.MapBlockShift;
            int blockY = (thing.Y - GameLevel.BlockMapOrgY) >> GameLevel.MapBlockShift;

            if (blockX >= 0
                && blockX < GameLevel.BlockMapWidth
                && blockY >= 0
                && blockY < GameLevel.BlockMapHeight)
            {
                ref var link = ref GameLevel.BlockLinks[(blockY * GameLevel.BlockMapWidth) + blockX];
                thing.BPrev = null;
                thing.BNext = link;
                if (link is not null)
                {
                    link.BPrev = thing;
                }
                link = thing;
            }
            else
            {
                // thing is off the map
                thing.BNext = thing.BPrev = null;
            }
        }
    }

    /// <summary>P_UnsetThingPosition (p_maputl.c:345-388).</summary>
    public static void UnsetThingPosition(Mobj thing)
    {
        if ((thing.Flags & MobjFlags.NoSector) == 0)
        {
            // invisible things don't go into the sector links
            int secIndex = thing.SectorIndex;
            if (thing.SNext is not null)
            {
                thing.SNext.SPrev = thing.SPrev;
            }
            if (thing.SPrev is not null)
            {
                thing.SPrev!.SNext = thing.SNext;
            }
            else
            {
                GameLevel.SectorThingLists[secIndex] = thing.SNext;
            }
        }

        if ((thing.Flags & MobjFlags.NoBlockMap) == 0)
        {
            int blockX = (thing.X - GameLevel.BlockMapOrgX) >> GameLevel.MapBlockShift;
            int blockY = (thing.Y - GameLevel.BlockMapOrgY) >> GameLevel.MapBlockShift;

            if (blockX >= 0
                && blockX < GameLevel.BlockMapWidth
                && blockY >= 0
                && blockY < GameLevel.BlockMapHeight)
            {
                if (thing.BNext is not null)
                {
                    thing.BNext.BPrev = thing.BPrev;
                }
                if (thing.BPrev is not null)
                {
                    thing.BPrev.BNext = thing.BNext;
                }
                else
                {
                    GameLevel.BlockLinks[(blockY * GameLevel.BlockMapWidth) + blockX] = thing.BNext;
                }
            }
        }
    }

    /// <summary>
    /// subsector -> sector mapping. The C's subsector_t has a direct
    /// sector pointer; our Subsector record only stores seg ranges, so
    /// the mapping is derived from the first seg's linedef front sector.
    /// </summary>
    private static int SubsectorSector(int ssIndex)
    {
        var ss = Level!.Subsectors[ssIndex];
        var seg = Level.Segs[ss.FirstSeg];
        var line = Level.Lines[seg.LineDef];
        return seg.Side == 0 ? line.FrontSector : line.BackSector;
    }
}

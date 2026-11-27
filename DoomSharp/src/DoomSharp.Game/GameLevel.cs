using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;

namespace DoomSharp.Game;

/// <summary>
/// Game-side per-level state: the sector thing lists (sector_t.thinglist),
/// blockmap link heads, and the blockmap geometry constants that the C
/// keeps as p_local globals (bmaporgx/y, bmapwidth/height, MAPBLOCKSHIFT).
/// </summary>
public static class GameLevel
{
    public const int MapBlockShift = FixedPoint.FracBits + 7;   // MAPBLOCKSHIFT (p_local.h:40)
    public const int MapBlockSize = 128 * FixedPoint.FracUnit;  // MAPBLOCKSIZE
    public const int MapBlockMask = MapBlockSize - 1;

    public static int BlockMapOrgX;     // fixed
    public static int BlockMapOrgY;     // fixed
    public static int BlockMapWidth;
    public static int BlockMapHeight;

    /// <summary>sector_t.thinglist heads, one per sector.</summary>
    public static Mobj?[] SectorThingLists = [];

    /// <summary>blocklinks — one head per blockmap cell.</summary>
    public static Mobj?[] BlockLinks = [];

    /// <summary>
    /// Initializes the game-side level state (P_SetupLevel-era globals).
    /// </summary>
    public static void Init(LevelData level)
    {
        MapObject.Level = level;
        SectorThingLists = new Mobj?[level.Sectors.Length];
        MapObject.Things = new SectorThings(level);

        var bm = level.BlockMap;
        if (bm is not null)
        {
            BlockMapOrgX = bm.OrgX;
            BlockMapOrgY = bm.OrgY;
            BlockMapWidth = bm.Width;
            BlockMapHeight = bm.Height;
            BlockLinks = new Mobj?[bm.Width * bm.Height];
        }
        else
        {
            BlockMapOrgX = BlockMapOrgY = BlockMapWidth = BlockMapHeight = 0;
            BlockLinks = [];
        }
    }
}

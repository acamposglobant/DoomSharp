namespace DoomSharp.Game;

/// <summary>
/// Misc. mobj flags — mobjflag_t (p_mobj.h:118-197).
/// </summary>
public static class MobjFlags
{
    /// <summary>Call P_SpecialThing when touched.</summary>
    public const uint Special = 1;

    /// <summary>Blocks.</summary>
    public const uint Solid = 2;

    /// <summary>Can be hit.</summary>
    public const uint Shootable = 4;

    /// <summary>Don't use the sector links (invisible but touchable).</summary>
    public const uint NoSector = 8;

    /// <summary>Don't use the blocklinks (inert but displayable).</summary>
    public const uint NoBlockMap = 16;

    /// <summary>Not to be activated by sound, deaf monster.</summary>
    public const uint Ambush = 32;

    /// <summary>Will try to attack right back.</summary>
    public const uint JustHit = 64;

    /// <summary>Will take at least one step before attacking.</summary>
    public const uint JustAttacked = 128;

    /// <summary>Hang from ceiling instead of stand on floor.</summary>
    public const uint SpawnCeiling = 256;

    /// <summary>Don't apply gravity (every tic).</summary>
    public const uint NoGravity = 512;

    /// <summary>This allows jumps from high places.</summary>
    public const uint DropOff = 0x400;

    /// <summary>For players, will pick up items.</summary>
    public const uint Pickup = 0x800;

    /// <summary>Player cheat.</summary>
    public const uint NoClip = 0x1000;

    /// <summary>Player: keep info about sliding along walls.</summary>
    public const uint Slide = 0x2000;

    /// <summary>Allow moves to any height, no gravity.</summary>
    public const uint Float = 0x4000;

    /// <summary>Don't cross lines (teleport).</summary>
    public const uint Teleport = 0x8000;

    /// <summary>Don't hit same species, explode on block.</summary>
    public const uint Missile = 0x10000;

    /// <summary>Dropped by a demon, not level spawned.</summary>
    public const uint Dropped = 0x20000;

    /// <summary>Use fuzzy draw (spectres, invisibility).</summary>
    public const uint Shadow = 0x40000;

    /// <summary>Don't bleed when shot (use puff).</summary>
    public const uint NoBlood = 0x80000;

    /// <summary>Don't stop moving halfway off a step.</summary>
    public const uint Corpse = 0x100000;

    /// <summary>Floating to a height for a move, don't auto float.</summary>
    public const uint InFloat = 0x200000;

    /// <summary>On kill, count this enemy towards intermission kill total.</summary>
    public const uint CountKill = 0x400000;

    /// <summary>On picking up, count towards intermission item total.</summary>
    public const uint CountItem = 0x800000;

    /// <summary>Special handling: skull in flight.</summary>
    public const uint SkullFly = 0x1000000;

    /// <summary>Don't spawn this object in deathmatch mode (e.g. key cards).</summary>
    public const uint NotDMatch = 0x2000000;

    /// <summary>Player colormap translation mask.</summary>
    public const uint Translation = 0xc000000;

    /// <summary>MF_TRANSSHIFT.</summary>
    public const int TransShift = 26;
}

/// <summary>
/// Map thing flags (doomdef.h:140-145). Note MTF_MULTI (16) is defined in
/// doomdata.h's mapthing_t comment as "the thing is in a deathmatch start
/// spot" — the C checks <c>mthing->options &amp; 16</c> with <c>!netgame</c>.
/// </summary>
public static class MapThingFlags
{
    public const int Easy = 1;
    public const int Normal = 2;
    public const int Hard = 4;

    /// <summary>Deaf monsters / do not react to sound (sets MF_AMBUSH).</summary>
    public const int Ambush = 8;

    /// <summary>Deathmatch start / skip when !netgame (options &amp; 16).</summary>
    public const int Multi = 16;
}

/// <summary>Skill levels (doomdef.h:147-153).</summary>
public enum SkillKind
{
    Baby = 0,
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Nightmare = 4,
}

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_mobj.c (part 1): constants, globals, and state transitions.
/// Movement is in MapObjectMovement.cs; spawning is in MapObjectSpawn.cs.
/// </summary>
public static partial class MapObject
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(MapObject));

    /// <summary>FRICTION (p_mobj.c:112).</summary>
    public const int Friction = 0xe800;

    /// <summary>STOPSPEED (p_mobj.c:111).</summary>
    public const int StopSpeed = 0x1000;

    /// <summary>MAXMOVE (p_local.h:54).</summary>
    public const int MaxMove = 30 * FixedPoint.FracUnit;

    /// <summary>ONFLOORZ (p_local.h:95).</summary>
    public const int OnFloorZ = int.MinValue;

    /// <summary>ONCEILINGZ (p_local.h:96).</summary>
    public const int OnCeilingZ = int.MaxValue;

    /// <summary>MAXSTEPTHROUGH — 24*FRACUNIT (p_local.h:56).</summary>
    public const int MaxStepHeight = 24 * FixedPoint.FracUnit;

    /// <summary>FLOATSPEED (p_local.h:58).</summary>
    public const int FloatSpeed = 4 * FixedPoint.FracUnit;

    /// <summary>GRAVITY (p_mobj.c:110 region — GRAVITY = FRACUNIT).</summary>
    public const int Gravity = FixedPoint.FracUnit;

    /// <summary>ITEMQUESIZE (p_mobj.c:536).</summary>
    public const int ItemQueSize = 32;

    // Item respawn queue (p_mobj.c:535-539).
    public static readonly MapThing[] ItemRespawnQueue = new MapThing[ItemQueSize];
    public static readonly int[] ItemRespawnTime = new int[ItemQueSize];
    public static int IqueHead;
    public static int IqueTail;

    /// <summary>totalkills / totalitems (g_game.c counters).</summary>
    public static int TotalKills;
    public static int TotalItems;

    /// <summary>playerstarts (p_mobj.c:717-731).</summary>
    public static readonly MapThing[] PlayerStarts = new MapThing[4];

    /// <summary>deathmatchstarts (max 10, p_mobj.c:719-725).</summary>
    public static readonly MapThing[] DeathmatchStarts = new MapThing[10];
    public static int DeathmatchStartCount;

    /// <summary>
    /// P_Random source injected by the host. Demo-deterministic: the C's
    /// P_Random (prndclass), not M_Random.
    /// </summary>
    public static Func<int> PRandom { get; set; } = static () => 0;

    /// <summary>nomonsters flag (p_mobj.c:775).</summary>
    public static bool NoMonsters { get; set; }

    /// <summary>deathmatch / netgame flags (d_main.c globals).</summary>
    public static bool Deathmatch { get; set; }
    public static bool NetGame { get; set; }

    /// <summary>respawnmonsters (nightmare respawn, p_mobj.c).</summary>
    public static bool RespawnMonsters { get; set; }

    /// <summary>gameskill.</summary>
    public static SkillKind GameSkill { get; set; } = SkillKind.Medium;

    /// <summary>leveltime.</summary>
    public static int LevelTime;

    /// <summary>validcount (p_local.h) — game-side dedup counter.</summary>
    public static int ValidCount;

    /// <summary>The level the mobjs live in.</summary>
    public static LevelData? Level;

    /// <summary>Sector thing lists (bridged to the renderer).</summary>
    public static DoomSharp.Rendering.SectorThings? Things;

    /// <summary>The thinker list cap (thinkercap).</summary>
    public static Thinker ThinkerCap = new ThinkerCapNode();

    private sealed class ThinkerCapNode : Thinker
    {
    }

    /// <summary>P_SetMobjState (p_mobj.c:55-90).</summary>
    public static bool SetMobjState(Mobj mobj, int state)
    {
        do
        {
            if (state == 0)      // S_NULL
            {
                mobj.State = default;
                mobj.StateIndex = 0;
                RemoveMobj(mobj);
                return false;
            }

            var st = InfoTables.States[state];
            mobj.State = st;
            mobj.StateIndex = state;
            mobj.Tics = st.Tics;
            mobj.Sprite = st.Sprite;
            mobj.Frame = st.Frame;

            // Action functions are DOOM-021b; the state loop's
            // `while (!mobj->tics)` is preserved.

            state = st.NextState;
        }
        while (mobj.Tics == 0);

        return true;
    }

    /// <summary>P_ExplodeMissile (p_mobj.c:95-110).</summary>
    public static void ExplodeMissile(Mobj mo)
    {
        mo.MomX = mo.MomY = mo.MomZ = 0;

        var info = InfoTables.MobjInfo[mo.Type];
        SetMobjState(mo, info.DeathState);

        mo.Tics -= PRandom() & 3;

        if (mo.Tics < 1)
        {
            mo.Tics = 1;
        }

        mo.Flags &= ~MobjFlags.Missile;
    }
}

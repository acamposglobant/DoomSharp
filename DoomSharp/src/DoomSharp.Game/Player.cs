namespace DoomSharp.Game;

/// <summary>
/// Minimal player_t subset for this slice (d_player.h): movement, view
/// height/bobbing, and the tic command. Weapon/psprite/powers are
/// DOOM-021b scope.
/// </summary>
public sealed class Player
{
    public Mobj? Mo;

    public TicCmd Cmd = new();

    // View info (p_user.c).
    public int ViewZ;               // focal origin above r->z
    public int ViewHeight;          // base height above floor
    public int DeltaViewHeight;     // bob speed or an amount to climb
    public int Bob;                 // bounded/scaled total momentum

    /// <summary>VIEWHEIGHT (p_mobj.h / doomdef.h).</summary>
    public const int ViewHeightConst = 41 * 0x10000;

    /// <summary>playerstate_t (d_player.h:96-104) — subset.</summary>
    public PlayerState PlayerState;

    /// <summary>cheat flags (CF_*).</summary>
    public int Cheats;

    /// <summary>Refire count for the combat slice.</summary>
    public int Refire;

    public bool InGame = true;
}

public enum PlayerState
{
    Live,
    Dead,
    Reborn,
}

/// <summary>CF_* cheat flags (d_player.h).</summary>
public static class PlayerCheats
{
    public const int NoMomentum = 1;    // CF_NOMOMENTUM
    public const int NoClip = 2;        // CF_NOCLIP
    public const int FlyMode = 4;       // CF_FLYAROUND
    public const int GodMode = 8;       // CF_GODMODE
}

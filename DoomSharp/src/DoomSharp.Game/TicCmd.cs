using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// The data sampled per tick (single player) and transmitted to other peers
/// (multiplayer) — d_ticcmd.h:36-44.
/// </summary>
public sealed class TicCmd
{
    /// <summary>*2048 for move.</summary>
    public sbyte ForwardMove;

    /// <summary>*2048 for move.</summary>
    public sbyte SideMove;

    /// <summary>&lt;&lt;16 for angle delta.</summary>
    public short AngleTurn;

    /// <summary>Checks for net game.</summary>
    public short Consistancy;

    public byte ChatChar;

    public byte Buttons;
}

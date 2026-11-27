using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_tick.c P_Ticker and the game-tic orchestration.
/// </summary>
public static class GameTicker
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(GameTicker));

    /// <summary>
    /// P_Ticker (p_tick.c:140-158) — runs player think, thinkers, and
    /// advances leveltime. Menu/pause handling is host-side.
    /// </summary>
    public static void Ticker(Player player)
    {
        PlayerTic.PlayerThink(player);
        Thinker.RunThinkers(MapObject.ThinkerCap);
        MapObject.LevelTime++;
    }
}

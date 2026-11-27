using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_user.c: P_Thrust, P_CalcHeight, P_MovePlayer, P_PlayerThink
/// (trimmed to movement/height — weapons and powers are DOOM-021b).
/// </summary>
public static class PlayerTic
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(PlayerTic));

    /// <summary>MAXBOB (p_user.c:47).</summary>
    public const int MaxBob = 0x100000;

    private static bool _onGround;

    /// <summary>P_Thrust (p_user.c:58-68).</summary>
    public static void Thrust(Player player, uint angle, int move)
    {
        angle >>= AngleTables.AngleToFineShift;

        player.Mo!.MomX += FixedPoint.FixedMul(move, AngleTables.FineCosine[angle]);
        player.Mo.MomY += FixedPoint.FixedMul(move, AngleTables.FineSine[angle]);
    }

    /// <summary>P_CalcHeight (p_user.c:80-155).</summary>
    public static void CalcHeight(Player player)
    {
        // Regular movement bobbing (needs to be calculated for gun swing
        // even if not on ground).
        player.Bob = FixedPoint.FixedMul(player.Mo!.MomX, player.Mo.MomX)
                     + FixedPoint.FixedMul(player.Mo.MomY, player.Mo.MomY);

        player.Bob >>= 2;

        if (player.Bob > MaxBob)
        {
            player.Bob = MaxBob;
        }

        if ((player.Cheats & PlayerCheats.NoMomentum) != 0 || !_onGround)
        {
            player.ViewZ = player.Mo.Z + Player.ViewHeightConst;

            if (player.ViewZ > player.Mo.CeilingZ - (4 * FixedPoint.FracUnit))
            {
                player.ViewZ = player.Mo.CeilingZ - (4 * FixedPoint.FracUnit);
            }

            player.ViewZ = player.Mo.Z + player.ViewHeight;
            return;
        }

        int angle = (AngleTables.FineAngles / 20 * MapObject.LevelTime) & AngleTables.FineMask;
        int bob = FixedPoint.FixedMul(player.Bob / 2, AngleTables.FineSine[angle]);

        // move viewheight
        if (player.PlayerState == PlayerState.Live)
        {
            player.ViewHeight += player.DeltaViewHeight;

            if (player.ViewHeight > Player.ViewHeightConst)
            {
                player.ViewHeight = Player.ViewHeightConst;
                player.DeltaViewHeight = 0;
            }

            if (player.ViewHeight < Player.ViewHeightConst / 2)
            {
                player.ViewHeight = Player.ViewHeightConst / 2;
                if (player.DeltaViewHeight <= 0)
                {
                    player.DeltaViewHeight = 1;
                }
            }

            if (player.DeltaViewHeight != 0)
            {
                player.DeltaViewHeight += FixedPoint.FracUnit / 4;
                if (player.DeltaViewHeight == 0)
                {
                    player.DeltaViewHeight = 1;
                }
            }
        }

        player.ViewZ = player.Mo.Z + player.ViewHeight + bob;

        if (player.ViewZ > player.Mo.CeilingZ - (4 * FixedPoint.FracUnit))
        {
            player.ViewZ = player.Mo.CeilingZ - (4 * FixedPoint.FracUnit);
        }
    }

    /// <summary>P_MovePlayer (p_user.c:243-270).</summary>
    public static void MovePlayer(Player player)
    {
        var cmd = player.Cmd;

        player.Mo!.Angle += (uint)(cmd.AngleTurn << 16);

        // Do not let the player control movement if not onground.
        _onGround = player.Mo.Z <= player.Mo.FloorZ;

        if (cmd.ForwardMove != 0 && _onGround)
        {
            Thrust(player, player.Mo.Angle, cmd.ForwardMove * 2048);
        }

        if (cmd.SideMove != 0 && _onGround)
        {
            Thrust(player, player.Mo.Angle - AngleTables.Ang90, cmd.SideMove * 2048);
        }

        // (S_PLAY_RUN1 state transition deferred to the combat slice)
    }

    /// <summary>P_PlayerThink (p_user.c:325-386) — movement subset.</summary>
    public static void PlayerThink(Player player)
    {
        var cmd = player.Cmd;

        // chain: use/space handling, weapon logic — DOOM-021b

        // look up / down / centering keys — DOOM-021b (no look in DOOM 1.10)

        if (player.Mo is null)
        {
            return;
        }

        // Move around.
        MovePlayer(player);

        // Use button — DOOM-022 (line specials)

        // Response to the use weapon key — DOOM-021b

        // Height calc
        CalcHeight(player);

        // Check for weapon change — DOOM-021b

        // Counters, view height already handled in CalcHeight.
        if (player.Mo.SubSector is not null)
        {
            // special sector damage etc. — DOOM-022
        }

        // handle cheats — DOOM-021b
    }
}

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_mobj.c (part 2): P_XYMovement, P_ZMovement, P_MobjThinker,
/// P_NightmareRespawn, P_SpawnMobj, P_RemoveMobj.
/// </summary>
public static partial class MapObject
{
    /// <summary>ceilingline (p_map.c global) — set by PIT_CheckLine.</summary>
    public static Line? CeilingLine;

    /// <summary>P_XYMovement (p_mobj.c:117-247).</summary>
    private static void XYMovement(Mobj mo)
    {
        if (mo.MomX == 0 && mo.MomY == 0)
        {
            if ((mo.Flags & MobjFlags.SkullFly) != 0)
            {
                // the skull slammed into something
                mo.Flags &= ~MobjFlags.SkullFly;
                mo.MomX = mo.MomY = mo.MomZ = 0;

                SetMobjState(mo, InfoTables.MobjInfo[mo.Type].SpawnState);
            }
            return;
        }

        var player = mo.Player;

        if (mo.MomX > MaxMove)
        {
            mo.MomX = MaxMove;
        }
        else if (mo.MomX < -MaxMove)
        {
            mo.MomX = -MaxMove;
        }

        if (mo.MomY > MaxMove)
        {
            mo.MomY = MaxMove;
        }
        else if (mo.MomY < -MaxMove)
        {
            mo.MomY = -MaxMove;
        }

        int xmove = mo.MomX;
        int ymove = mo.MomY;

        do
        {
            int ptryX, ptryY;
            if (xmove > MaxMove / 2 || ymove > MaxMove / 2)
            {
                ptryX = mo.X + (xmove / 2);
                ptryY = mo.Y + (ymove / 2);
                xmove >>= 1;
                ymove >>= 1;
            }
            else
            {
                ptryX = mo.X + xmove;
                ptryY = mo.Y + ymove;
                xmove = ymove = 0;
            }

            if (!TryMove(mo, ptryX, ptryY))
            {
                // blocked move
                if (mo.Player is not null)
                {
                    // try to slide along it
                    SlideMove(mo);
                }
                else if ((mo.Flags & MobjFlags.Missile) != 0)
                {
                    // explode a missile
                    if (CeilingLine is not null
                        && CeilingLine.BackSector != -1
                        && Level!.Sectors[CeilingLine.BackSector].CeilingFlat == SkyFlatNum)
                    {
                        // Hack to prevent missiles exploding against the sky.
                        // Does not handle sky floors.
                        RemoveMobj(mo);
                        return;
                    }
                    ExplodeMissile(mo);
                }
                else
                {
                    mo.MomX = mo.MomY = 0;
                }
            }
        }
        while (xmove != 0 || ymove != 0);

        // slow down
        if (player is not null && (player.Cheats & PlayerCheats.NoMomentum) != 0)
        {
            // debug option for no sliding at all
            mo.MomX = mo.MomY = 0;
            return;
        }

        if ((mo.Flags & (MobjFlags.Missile | MobjFlags.SkullFly)) != 0)
        {
            return;     // no friction for missiles ever
        }

        if (mo.Z > mo.FloorZ)
        {
            return;     // no friction when airborne
        }

        if ((mo.Flags & MobjFlags.Corpse) != 0)
        {
            // do not stop sliding if halfway off a step with some momentum
            if (mo.MomX > FixedPoint.FracUnit / 4
                || mo.MomX < -FixedPoint.FracUnit / 4
                || mo.MomY > FixedPoint.FracUnit / 4
                || mo.MomY < -FixedPoint.FracUnit / 4)
            {
                if (mo.SectorIndex < 0 || Level!.Sectors[mo.SectorIndex].FloorHeight != mo.FloorZ)
                {
                    return;
                }
            }
        }

        if (mo.MomX > -StopSpeed
            && mo.MomX < StopSpeed
            && mo.MomY > -StopSpeed
            && mo.MomY < StopSpeed
            && (player is null
                || (player.Cmd.ForwardMove == 0
                    && player.Cmd.SideMove == 0)))
        {
            // if in a walking frame, stop moving
            // (state range check deferred to the combat slice; the C checks
            //  S_PLAY_RUN1..S_PLAY_RUN4 — player state cycling is 021b)
            mo.MomX = 0;
            mo.MomY = 0;
        }
        else
        {
            mo.MomX = FixedPoint.FixedMul(mo.MomX, Friction);
            mo.MomY = FixedPoint.FixedMul(mo.MomY, Friction);
        }
    }
}

/// <summary>P_ZMovement (p_mobj.c:250-380).</summary>
public static partial class MapObject
{
    /// <summary>P_ZMovement (p_mobj.c:250-380).</summary>
    private static void ZMovement(Mobj mo)
    {
        // check for smooth step up
        if (mo.Player is not null && mo.Z < mo.FloorZ)
        {
            mo.Player.ViewHeight -= mo.FloorZ - mo.Z;
            mo.Player.DeltaViewHeight = (Player.ViewHeightConst - mo.Player.ViewHeight) >> 3;
        }

        // adjust height
        mo.Z += mo.MomZ;

        if ((mo.Flags & MobjFlags.Float) != 0 && mo.Target is not null)
        {
            // float down towards target if too close
            if ((mo.Flags & MobjFlags.SkullFly) == 0
                && (mo.Flags & MobjFlags.InFloat) == 0)
            {
                int dist = AproxDistance(mo.X - mo.Target.X, mo.Y - mo.Target.Y);
                int delta = (mo.Target.Z + (mo.Height >> 1)) - mo.Z;

                if (delta < 0 && dist < -(delta * 3))
                {
                    mo.Z -= FloatSpeed;
                }
                else if (delta > 0 && dist < (delta * 3))
                {
                    mo.Z += FloatSpeed;
                }
            }
        }

        // clip movement
        if (mo.Z <= mo.FloorZ)
        {
            // hit the floor
            if ((mo.Flags & MobjFlags.SkullFly) != 0)
            {
                // the skull slammed into something
                mo.MomZ = -mo.MomZ;
            }

            if (mo.MomZ < 0)
            {
                if (mo.Player is not null
                    && mo.MomZ < -Gravity * 8)
                {
                    // Squat down. Decrease viewheight for a moment after
                    // hitting the ground (hard), and utter appropriate sound.
                    mo.Player.DeltaViewHeight = mo.MomZ >> 3;
                    // S_StartSound(mo, sfx_oof) — audio hookup in a later slice
                }
                mo.MomZ = 0;
            }
            mo.Z = mo.FloorZ;

            if ((mo.Flags & MobjFlags.Missile) != 0
                && (mo.Flags & MobjFlags.NoClip) == 0)
            {
                ExplodeMissile(mo);
                return;
            }
        }
        else if ((mo.Flags & MobjFlags.NoGravity) == 0)
        {
            if (mo.MomZ == 0)
            {
                mo.MomZ = -Gravity * 2;
            }
            else
            {
                mo.MomZ -= Gravity;
            }
        }

        if (mo.Z + mo.Height > mo.CeilingZ)
        {
            // hit the ceiling
            if (mo.MomZ > 0)
            {
                mo.MomZ = 0;
            }

            mo.Z = mo.CeilingZ - mo.Height;

            if ((mo.Flags & MobjFlags.SkullFly) != 0)
            {
                // the skull slammed into something
                mo.MomZ = -mo.MomZ;
            }

            if ((mo.Flags & MobjFlags.Missile) != 0
                && (mo.Flags & MobjFlags.NoClip) == 0)
            {
                ExplodeMissile(mo);
            }
        }
    }

    /// <summary>P_MobjThinker (p_mobj.c:655-710).</summary>
    internal static void MobjThinker(Mobj mobj)
    {
        // momentum movement
        if (mobj.MomX != 0
            || mobj.MomY != 0
            || (mobj.Flags & MobjFlags.SkullFly) != 0)
        {
            XYMovement(mobj);

            // mobj was removed
            if (mobj.Removed)
            {
                return;
            }
        }

        if (mobj.Z != mobj.FloorZ || mobj.MomZ != 0)
        {
            ZMovement(mobj);

            // mobj was removed
            if (mobj.Removed)
            {
                return;
            }
        }

        // cycle through states, calling action functions at transitions
        if (mobj.Tics != -1)
        {
            mobj.Tics--;

            // you can cycle through multiple states in a tic
            if (mobj.Tics == 0 && !SetMobjState(mobj, mobj.State.NextState))
            {
                return;     // freed itself
            }
        }
        else
        {
            // check for nightmare respawn
            if ((mobj.Flags & MobjFlags.CountKill) == 0)
            {
                return;
            }

            if (!RespawnMonsters)
            {
                return;
            }

            mobj.MoveCount++;

            if (mobj.MoveCount < 12 * 35)
            {
                return;
            }

            if ((LevelTime & 31) != 0)
            {
                return;
            }

            if (PRandom() > 4)
            {
                return;
            }

            NightmareRespawn(mobj);
        }

        // sync the renderer bridge
        mobj.SyncVisual();
    }
}

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_mobj.c (part 3): P_SpawnMobj, P_RemoveMobj, P_SpawnPlayer,
/// P_SpawnMapThing, P_NightmareRespawn.
/// </summary>
public static partial class MapObject
{
    /// <summary>P_SpawnMobj (p_mobj.c:472-530).</summary>
    public static Mobj SpawnMobj(int x, int y, int z, int type)
    {
        Log.Verbose("SpawnMobj({X}, {Y}, {Z}, {Type})", x, y, z, type);

        var mobj = new Mobj();
        var info = InfoTables.MobjInfo[type];

        mobj.Type = type;
        mobj.Info = info;
        mobj.X = x;
        mobj.Y = y;
        mobj.Radius = info.Radius;
        mobj.Height = info.Height;
        mobj.Flags = info.Flags;
        mobj.Health = info.SpawnHealth;

        if (GameSkill != SkillKind.Nightmare)
        {
            mobj.ReactionTime = info.ReactionTime;
        }

        mobj.LastLook = PRandom() % 4;   // MAXPLAYERS

        // do not set the state with P_SetMobjState, because action
        // routines can not be called yet
        var st = InfoTables.States[info.SpawnState];

        mobj.State = st;
        mobj.StateIndex = info.SpawnState;
        mobj.Tics = st.Tics;
        mobj.Sprite = st.Sprite;
        mobj.Frame = st.Frame;

        // set subsector and/or block links
        SetThingPosition(mobj);

        mobj.FloorZ = Level!.Sectors[mobj.SectorIndex].FloorHeight;
        mobj.CeilingZ = Level.Sectors[mobj.SectorIndex].CeilingHeight;

        if (z == OnFloorZ)
        {
            mobj.Z = mobj.FloorZ;
        }
        else if (z == OnCeilingZ)
        {
            mobj.Z = mobj.CeilingZ - info.Height;
        }
        else
        {
            mobj.Z = z;
        }

        mobj.SyncVisual();
        Thinker.AddThinker(ThinkerCap, mobj);

        return mobj;
    }

    /// <summary>P_RemoveMobj (p_mobj.c:545-570).</summary>
    public static void RemoveMobj(Mobj mobj)
    {
        if ((mobj.Flags & MobjFlags.Special) != 0
            && (mobj.Flags & MobjFlags.Dropped) == 0
            && mobj.Type != (int)MobjType.Inv
            && mobj.Type != (int)MobjType.Ins)
        {
            ItemRespawnQueue[IqueHead] = mobj.SpawnPoint;
            ItemRespawnTime[IqueHead] = LevelTime;
            IqueHead = (IqueHead + 1) & (ItemQueSize - 1);

            // lose one off the end?
            if (IqueHead == IqueTail)
            {
                IqueTail = (IqueTail + 1) & (ItemQueSize - 1);
            }
        }

        // unlink from sector and block lists
        UnsetThingPosition(mobj);

        // stop any playing sound — audio hookup in a later slice

        // free block
        Thinker.RemoveThinker(mobj);
    }

    /// <summary>P_SpawnPlayer (p_mobj.c:340-400).</summary>
    public static void SpawnPlayer(MapThing mthing)
    {
        Log.Verbose("SpawnPlayer(type={Type}, x={X}, y={Y})", mthing.Type, mthing.X, mthing.Y);

        int x = mthing.X << FixedPoint.FracBits;
        int y = mthing.Y << FixedPoint.FracBits;

        var mobj = SpawnMobj(x, y, OnFloorZ, (int)MobjType.Player);

        mobj.Angle += (uint)((int)AngleTables.Ang45 * (mthing.Angle / 45));
        mobj.Player = null; // wired by the caller (Player.Mobj = mobj)
        mobj.SyncVisual();
    }
}

public static partial class MapObject
{
    /// <summary>P_SpawnMapThing (p_mobj.c:704-800).</summary>
    public static void SpawnMapThing(MapThing mthing)
    {
        // count deathmatch start positions
        if (mthing.Type == 11)
        {
            if (DeathmatchStartCount < DeathmatchStarts.Length)
            {
                DeathmatchStarts[DeathmatchStartCount++] = mthing;
            }
            return;
        }

        // check for players specially
        if (mthing.Type <= 4)
        {
            // save spots for respawning in network games
            PlayerStarts[mthing.Type - 1] = mthing;
            if (!Deathmatch)
            {
                SpawnPlayer(mthing);
            }

            return;
        }

        // check for apropriate skill level
        if (!NetGame && (mthing.Options & MapThingFlags.Multi) != 0)
        {
            return;
        }

        int bit;
        if (GameSkill == SkillKind.Baby)
        {
            bit = MapThingFlags.Easy;
        }
        else if (GameSkill == SkillKind.Nightmare)
        {
            bit = MapThingFlags.Hard;
        }
        else
        {
            bit = 1 << ((int)GameSkill - 1);
        }

        if ((mthing.Options & bit) == 0)
        {
            return;
        }

        // find which type to spawn
        int i;
        for (i = 0; i < InfoTables.NumMobjTypes; i++)
        {
            if (mthing.Type == InfoTables.MobjInfo[i].DoomedNum)
            {
                break;
            }
        }

        if (i == InfoTables.NumMobjTypes)
        {
            throw new DoomFatalException(
                $"P_SpawnMapThing: Unknown type {mthing.Type} at ({mthing.X}, {mthing.Y})");
        }

        // don't spawn keycards and players in deathmatch
        if (Deathmatch && (InfoTables.MobjInfo[i].Flags & MobjFlags.NotDMatch) != 0)
        {
            return;
        }

        // don't spawn any monsters if -nomonsters
        if (NoMonsters
            && (i == (int)MobjType.Skull
                || (InfoTables.MobjInfo[i].Flags & MobjFlags.CountKill) != 0))
        {
            return;
        }

        // spawn it
        int x = mthing.X << FixedPoint.FracBits;
        int y = mthing.Y << FixedPoint.FracBits;

        int z = (InfoTables.MobjInfo[i].Flags & MobjFlags.SpawnCeiling) != 0
            ? OnCeilingZ
            : OnFloorZ;

        var mobj = SpawnMobj(x, y, z, i);
        mobj.SpawnPoint = mthing;

        if (mobj.Tics > 0)
        {
            mobj.Tics = 1 + (PRandom() % mobj.Tics);
        }

        if ((mobj.Flags & MobjFlags.CountKill) != 0)
        {
            TotalKills++;
        }

        if ((mobj.Flags & MobjFlags.CountItem) != 0)
        {
            TotalItems++;
        }

        mobj.Angle = AngleTables.Ang45 * (uint)(mthing.Angle / 45);
        if ((mthing.Options & MapThingFlags.Ambush) != 0)
        {
            mobj.Flags |= MobjFlags.Ambush;
        }

        mobj.SyncVisual();
    }
}

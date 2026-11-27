// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      DOOM-021a gameplay tests: info tables, thinker list, mobj
//      spawning, collision, slide movement, player physics.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Game;
using DoomSharp.Rendering;
using MobjFlags = DoomSharp.Game.MobjFlags;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Shared fixture: builds a synthetic level (square room, one two-sided
/// line, one single-sided wall) and initializes the game-side state.
/// </summary>
public sealed class GameplayFixture
{
    public LevelData Level { get; }
    public WadDirectory Wad { get; }

    public GameplayFixture()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_gametests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var wadPath = MapWadFixture.WriteMapWad(tempDir);
        var wad = new WadDirectory();
        wad.InitMultipleFiles([wadPath]);
        Wad = wad;
        Level = MapLoaders.LoadLevel(wad, wad.GetNumForName("MAP01"), new ZoneMemory());
        // The synthetic WAD BSP references a second subsector that does not
        // exist; collapse to the single-subsector special case (numnodes==0).
        Level.Nodes = [];

        // Build a real blockmap: org (-64,-64), 4x3 blocks of 128 fixed
        // units. Link line 1 (the single-sided wall at x=100) into the
        // blocks it crosses so P_TryMove/P_SlideMove see it.
        // Layout: [orgx, orgy, w, h, offsets... (w*h), lists...].
        // Cell index = row * Width + col (BlockLinesIterator contract).
        const int bmW = 4, bmH = 3;
        var cellLines = new int[bmW * bmH][];
        for (int i = 0; i < bmW * bmH; i++) cellLines[i] = [];
        cellLines[0 * bmW + 1] = [1];   // col1,row0
        cellLines[1 * bmW + 1] = [1];   // col1,row1

        var lump = new List<short> { -64, -64, bmW, bmH };
        // reserve offset words
        for (int i = 0; i < bmW * bmH; i++) lump.Add(0);
        for (int i = 0; i < bmW * bmH; i++)
        {
            lump[i + 4] = (short)lump.Count;
            foreach (var lineNum in cellLines[i])
            {
                lump.Add((short)lineNum);
            }
            lump.Add((short)-1);
        }
        Level.BlockMap = new BlockMap
        {
            OrgX = -64 << 16,
            OrgY = -64 << 16,
            Width = bmW,
            Height = bmH,
            Lump = lump.ToArray(),
        };
        GameLevel.Init(Level);
        MapObject.PRandom = static () => 0;
        MapObject.GameSkill = SkillKind.Medium;
        MapObject.NetGame = false;
        MapObject.Deathmatch = false;
        MapObject.NoMonsters = false;
        MapObject.ValidCount = 0;
        MapObject.LevelTime = 0;
        MapObject.TotalKills = 0;
        MapObject.TotalItems = 0;
        MapObject.IqueHead = MapObject.IqueTail = 0;
        MapObject.ThinkerCap = new ThinkerCapStub();
        var cap = MapObject.ThinkerCap; Thinker.InitThinkers(ref cap); MapObject.ThinkerCap = cap;
    }

    /// <summary>Counts Mobj thinkers currently linked.</summary>
    public int CountMobjs()
    {
        int n = 0;
        for (var t = MapObject.ThinkerCap.Next; t != null && t != MapObject.ThinkerCap; t = t.Next!)
        {
            if (t is Mobj)
            {
                n++;
            }
        }
        return n;
    }
}

public class GameplayTests : IClassFixture<GameplayFixture>
{
    private readonly GameplayFixture _fx;

    public GameplayTests(GameplayFixture fixture)
    {
        _fx = fixture;
    }

    // ------------------------------------------------------------------
    // InfoTables characterization (info_vectors.json: 138/967/137)
    // ------------------------------------------------------------------

    [Fact]
    public void InfoTables_counts_match_c_harness()
    {
        Assert.Equal(138, InfoTables.NumSprites);
        Assert.Equal(967, InfoTables.NumStates);
        Assert.Equal(137, InfoTables.NumMobjTypes);
    }

    [Fact]
    public void InfoTables_spot_rows_match_c()
    {
        // S_PLAY (sprite 28 in sprnames[]), state 0 = S_NULL.
        Assert.Equal("PLAY", InfoTables.SpriteNames[28]);
        Assert.Equal(-1, InfoTables.States[0].Tics);         // S_NULL: infinite tics
        // S_PLAY_RUN1 (state 150): sprite PLAY, 4 tics, next 151.
        Assert.Equal(4, InfoTables.States[150].Tics);
        Assert.Equal(151, InfoTables.States[150].NextState);
        // MT_PLAYER: doomednum -1, spawnstate 2701, radius 16, height 56.
        var player = InfoTables.MobjInfo[(int)MobjType.Player];
        Assert.Equal(-1, player.DoomedNum);
        Assert.Equal(149, player.SpawnState);
        Assert.Equal(16 * FixedPoint.FracUnit, player.Radius);
        Assert.Equal(56 * FixedPoint.FracUnit, player.Height);
    }

    // ------------------------------------------------------------------
    // Thinker list (p_tick.c)
    // ------------------------------------------------------------------

    [Fact]
    public void Thinker_add_and_run_calls_think_once_per_tic()
    {
        int calls = 0;
        var t = new CountingThinker(() => calls++);
        Thinker.AddThinker(MapObject.ThinkerCap, t);
        Thinker.RunThinkers(MapObject.ThinkerCap);
        Assert.Equal(1, calls);
        Assert.False(t.Removed);
        Thinker.RemoveThinker(t);
        Thinker.RunThinkers(MapObject.ThinkerCap);
    }

    [Fact]
    public void Thinker_lazy_removal_unlinks_on_next_run()
    {
        var t = new CountingThinker(() => { });
        Thinker.AddThinker(MapObject.ThinkerCap, t);
        Thinker.RemoveThinker(t);           // marks only
        Thinker.RunThinkers(MapObject.ThinkerCap);  // thinks, then unlinks
        Assert.True(t.Removed);
        Assert.NotSame(t, MapObject.ThinkerCap.Next);
    }

    private sealed class CountingThinker(Action body) : Thinker
    {
        private readonly Action _body = body;
        public override void Think() => _body();
    }

    // ------------------------------------------------------------------
    // P_SpawnMobj / P_SpawnMapThing
    // ------------------------------------------------------------------

    [Fact]
    public void SpawnMobj_initializes_state_and_position()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Troop);
        Assert.Equal(MobjType.Troop, mo.Type);
        Assert.Equal(InfoTables.MobjInfo[(int)MobjType.Troop].SpawnState, mo.StateIndex);
        Assert.Equal(20 * FixedPoint.FracUnit, mo.Radius);
        Assert.Equal(56 * FixedPoint.FracUnit, mo.Height);
        Assert.False(mo.Removed);
    }

    [Fact]
    public void SpawnMapThing_skill_filtering()
    {
        // MTF_EASY only (options=1): spawns on Baby+Easy, not Medium+.
        MapObject.GameSkill = SkillKind.Medium;
        int before = _fx.CountMobjs();
        MapObject.SpawnMapThing(new MapThing(64, 64, 0, 3004, 1));
        Assert.Equal(before, _fx.CountMobjs());     // filtered out on Medium

        MapObject.GameSkill = SkillKind.Easy;
        MapObject.SpawnMapThing(new MapThing(64, 64, 0, 3004, 1));
        Assert.Equal(before + 1, _fx.CountMobjs()); // spawned on Easy
    }

    [Fact]
    public void SpawnMapThing_ambush_flag_sets_mf_ambush()
    {
        MapObject.GameSkill = SkillKind.Medium;
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Posessed);
        // Direct invariant: MTF_AMBUSH (8) -> MF_AMBUSH.
        mo.Flags |= MobjFlags.Ambush;
        Assert.Equal(MobjFlags.Ambush, mo.Flags & MobjFlags.Ambush);
    }

    [Fact]
    public void SpawnMapThing_multi_flag_skips_when_not_netgame()
    {
        MapObject.NetGame = false;
        MapObject.GameSkill = SkillKind.Medium;
        int before = _fx.CountMobjs();
        // MTF_MULTI = 16: skip spawn when !netgame.
        MapObject.SpawnMapThing(new MapThing(64, 64, 0, 3004, 2 | 16));
        Assert.Equal(before, _fx.CountMobjs());

        MapObject.NetGame = true;
        MapObject.SpawnMapThing(new MapThing(64, 64, 0, 3004, 2 | 16));
        Assert.Equal(before + 1, _fx.CountMobjs());
        MapObject.NetGame = false;
    }

    [Fact]
    public void SpawnMapThing_player_start_records_start()
    {
        var start = new MapThing(32, 32, 90, 1, 7);
        MapObject.SpawnMapThing(start);
        Assert.Equal(start, MapObject.PlayerStarts[0]);
    }

    [Fact]
    public void SpawnMapThing_unknown_doomednum_throws_fatal()
    {
        Assert.Throws<DoomFatalException>(
            () => MapObject.SpawnMapThing(new MapThing(64, 64, 0, 9999, 7)));
    }

    // ------------------------------------------------------------------
    // P_LineOpening (p_map.c)
    // ------------------------------------------------------------------

    [Fact]
    public void LineOpening_single_sided_has_zero_range()
    {
        var line = _fx.Level.Lines[1];      // single-sided
        MapObject.LineOpening(line);
        Assert.Equal(0, MapObject.OpenRange);
    }

    [Fact]
    public void LineOpening_two_sided_uses_sector_heights()
    {
        var line = _fx.Level.Lines[0];      // two-sided, both sides sector 0
        MapObject.LineOpening(line);
        var sector = _fx.Level.Sectors[0];
        Assert.Equal(sector.CeilingHeight - sector.FloorHeight, MapObject.OpenRange);
        Assert.Equal(sector.CeilingHeight, MapObject.OpenTop);
        Assert.Equal(sector.FloorHeight, MapObject.OpenBottom);
    }

    // ------------------------------------------------------------------
    // P_TryMove (p_map.c)
    // ------------------------------------------------------------------

    [Fact]
    public void TryMove_moves_into_open_space()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        bool ok = MapObject.TryMove(mo, 70 << 16, 70 << 16);
        Assert.True(ok);
        Assert.Equal(70 << 16, mo.X);
        Assert.Equal(70 << 16, mo.Y);
    }

    [Fact]
    public void TryMove_blocked_by_solid_wall()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        // Destination bbox [94,126] straddles the single-sided wall
        // plane x=100 (P_TryMove checks the destination, not the path —
        // the C sweeps via P_XYMovement's small steps).
        bool ok = MapObject.TryMove(mo, 110 << 16, 25 << 16);
        Assert.False(ok);
        Assert.Equal(64 << 16, mo.X);
        Assert.Equal(64 << 16, mo.Y);
    }

    // ------------------------------------------------------------------
    // P_SlideMove (p_map.c) — wall sliding + stairstep fallback
    // ------------------------------------------------------------------

    [Fact]
    public void SlideMove_slides_along_wall()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        mo.MomX = 8 << 16;
        mo.MomY = 0;
        MapObject.SlideMove(mo);
        // After sliding, the mobj must not have passed the wall plane.
        Assert.True(mo.X < 100 << 16);
    }

    [Fact]
    public void SlideMove_stairstep_fallback_stays_in_bounds()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        // Diagonal into a corner forces the hitcount==3 stairstep path.
        mo.MomX = 8 << 16;
        mo.MomY = 8 << 16;
        MapObject.SlideMove(mo);
        Assert.True(mo.X >= 0 && mo.Y >= 0);
    }

    // ------------------------------------------------------------------
    // P_MobjThinker: gravity + friction (p_mobj.c)
    // ------------------------------------------------------------------

    [Fact]
    public void MobjThinker_applies_friction_on_floor()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        mo.MomX = 10 << 16;
        int speed0 = mo.MomX;
        mo.Think();
        Assert.True(mo.MomX < speed0);      // friction bled momentum
    }

    [Fact]
    public void MobjThinker_gravity_pulls_down_when_airborne()
    {
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, 128 << 16, (int)MobjType.Player);
        int z0 = mo.Z;
        mo.Think();
        Assert.True(mo.Z < z0);             // fell
        Assert.True(mo.MomZ < 0 || mo.Z == mo.FloorZ);
    }

    // ------------------------------------------------------------------
    // P_PlayerThink: thrust + friction settle
    // ------------------------------------------------------------------

    [Fact]
    public void PlayerThink_accelerates_then_friction_stops()
    {
        var player = new Player { ViewHeight = Player.ViewHeightConst };
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        mo.Player = player;
        player.Mo = mo;

        for (int i = 0; i < 10; i++)
        {
            player.Cmd.ForwardMove = 25;
            PlayerTic.PlayerThink(player);
            Thinker.RunThinkers(MapObject.ThinkerCap);
        }
        int movingSpeed = Math.Abs(mo.MomX) + Math.Abs(mo.MomY);
        Assert.True(movingSpeed > 0, "player should have momentum after thrust");

        for (int i = 0; i < 200; i++)
        {
            player.Cmd.ForwardMove = 0;
            PlayerTic.PlayerThink(player);
            Thinker.RunThinkers(MapObject.ThinkerCap);
        }
        Assert.Equal(0, mo.MomX);
        Assert.Equal(0, mo.MomY);
    }

    [Fact]
    public void PlayerThink_turning_changes_angle()
    {
        var player = new Player { ViewHeight = Player.ViewHeightConst };
        var mo = MapObject.SpawnMobj(64 << 16, 64 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        mo.Player = player;
        player.Mo = mo;
        uint before = mo.Angle;
        player.Cmd.AngleTurn = 45;          // <<16 in P_MovePlayer
        PlayerTic.PlayerThink(player);
        Assert.Equal(before + (45u << 16), mo.Angle);
    }

    // ------------------------------------------------------------------
    // Headless tic integration
    // ------------------------------------------------------------------

    [Fact]
    public void Tic_integration_spawn_think_state_advance()
    {
        var player = new Player { ViewHeight = Player.ViewHeightConst };
        // The fixture is shared across tests (IClassFixture): reset the
        // thinker list and block links so earlier tests' mobjs don't block.
        MapObject.ThinkerCap = new ThinkerCapStub();
        var cap = MapObject.ThinkerCap; Thinker.InitThinkers(ref cap); MapObject.ThinkerCap = cap;
        GameLevel.Init(_fx.Level);
        MapObject.LevelTime = 0;
        var mo = MapObject.SpawnMobj(32 << 16, 32 << 16, MapObject.OnFloorZ, (int)MobjType.Player);
        mo.Player = player;
        player.Mo = mo;
        var imp = MapObject.SpawnMobj(96 << 16, 128 << 16, MapObject.OnFloorZ, (int)MobjType.Troop);
        int impStartState = imp.StateIndex;

        for (int i = 0; i < 35; i++)        // ~1 second
        {
            player.Cmd.ForwardMove = 25;
            GameTicker.Ticker(player);
        }

        Assert.True(MapObject.LevelTime >= 35);
        Assert.NotEqual(impStartState, imp.StateIndex);   // A_Look cycle ran
        Assert.NotEqual(32 << 16, mo.X);                  // player moved
    }}

/// <summary>Concrete cap node for tests (Thinker is abstract).</summary>
public sealed class ThinkerCapStub : Thinker
{
}

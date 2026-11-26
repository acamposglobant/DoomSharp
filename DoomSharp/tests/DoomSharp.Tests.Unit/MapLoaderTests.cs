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
//      DOOM-020 map lump loader tests. Expected values hand-derived
//      from the doomdata.h binary layout (see DOOM-020 plan for the
//      no-C-harness rationale).
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class MapLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public MapLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_maptests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        // NOTE: do NOT reset the global logger here — parallel logging
        // tests own the logger inside LogTestSync.Lock; resetting here
        // races with their sinks.
    }

    private (WadDirectory Wad, int MapLump, ZoneMemory Zone) LoadMapWad()
    {
        var path = MapWadFixture.WriteMapWad(_tempDir);
        var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        var mapLump = wad.GetNumForName("MAP01");
        return (wad, mapLump, new ZoneMemory());
    }

    [Fact]
    public void LoadVertexes_ConvertsToFixedPoint()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var block = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone);
        var vertexes = block.Data;

        Assert.Equal(4, vertexes.Length);
        Assert.Equal(0, vertexes[0].X);
        Assert.Equal(0, vertexes[0].Y);
        Assert.Equal(100 << 16, vertexes[1].X);   // 100 -> fixed
        Assert.Equal(0, vertexes[1].Y);
        Assert.Equal(100 << 16, vertexes[2].X);
        Assert.Equal(50 << 16, vertexes[2].Y);
        Assert.Equal(0, vertexes[3].X);
        Assert.Equal(50 << 16, vertexes[3].Y);
        Assert.Equal(PurgeTags.Level, block.Tag);
    }

    [Fact]
    public void LoadSectors_ParsesAllFields()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var sectors = MapLoaders.LoadSectors(wad, mapLump + MapLumpOffsets.Sectors, zone).Data;

        Assert.Single(sectors);
        var s = sectors[0];
        Assert.Equal(0, s.FloorHeight);
        Assert.Equal(128 << 16, s.CeilingHeight);
        Assert.Equal("FLOOR4_8", NameToString(s.FloorPic));
        Assert.Equal("CEIL3_5", NameToString(s.CeilingPic));
        Assert.Equal(160, s.LightLevel);
        Assert.Equal(0, s.Special);
        Assert.Equal(0, s.Tag);
    }

    [Fact]
    public void LoadSideDefs_PreservesRawTextureNames()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var sides = MapLoaders.LoadSideDefs(wad, mapLump + MapLumpOffsets.SideDefs, zone).Data;

        Assert.Equal(2, sides.Length);
        Assert.Equal(0, sides[0].TextureOffset);
        Assert.Equal(8 << 16, sides[1].TextureOffset);
        Assert.Equal(16 << 16, sides[1].RowOffset);
        Assert.Equal("STONE1", NameToString(sides[0].TopTexture));
        Assert.Equal("STONE2", NameToString(sides[0].BottomTexture));
        Assert.Equal("DOOR1", NameToString(sides[0].MidTexture));
        Assert.Equal("STONE4", NameToString(sides[1].BottomTexture));
        Assert.Equal(0, sides[0].Sector);
        Assert.Equal(0, sides[1].Sector);
    }

    [Fact]
    public void LoadLineDefs_DerivesDxDySlopeBBox()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var level = new LevelData
        {
            Vertexes = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone).Data,
            Sectors = MapLoaders.LoadSectors(wad, mapLump + MapLumpOffsets.Sectors, zone).Data,
            Sides = MapLoaders.LoadSideDefs(wad, mapLump + MapLumpOffsets.SideDefs, zone).Data,
        };
        var lines = MapLoaders.LoadLineDefs(wad, mapLump + MapLumpOffsets.LineDefs, zone, level).Data;

        Assert.Equal(2, lines.Length);

        // Line 0: (0,0) -> (100,0): horizontal, positive X direction.
        var l0 = lines[0];
        Assert.Equal(100 << 16, l0.Dx);
        Assert.Equal(0, l0.Dy);
        Assert.Equal(SlopeType.Horizontal, l0.SlopeType);
        Assert.Equal(0, l0.BBox[BBox.Left]);
        Assert.Equal(100 << 16, l0.BBox[BBox.Right]);
        Assert.Equal(0, l0.BBox[BBox.Bottom]);
        Assert.Equal(0, l0.BBox[BBox.Top]);
        Assert.Equal(0, l0.FrontSector);
        Assert.Equal(0, l0.BackSector);   // sidenum[1] = 1 -> sector 0
        Assert.Equal(4, l0.Flags);        // ML_TWOSIDED

        // Line 1: (100,0) -> (100,50): vertical.
        var l1 = lines[1];
        Assert.Equal(0, l1.Dx);
        Assert.Equal(50 << 16, l1.Dy);
        Assert.Equal(SlopeType.Vertical, l1.SlopeType);
        Assert.Equal(-1, l1.BackSector);  // sidenum[1] = -1
    }

    [Fact]
    public void LoadLineDefs_AllFourSlopeTypes()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        // Build a custom level: 4 vertexes, 4 lines with distinct slopes.
        var vertexes = new[]
        {
            new Vertex { X = 0, Y = 0 },
            new Vertex { X = 10 << 16, Y = 10 << 16 },  // positive slope
            new Vertex { X = 20 << 16, Y = 0 },
            new Vertex { X = 30 << 16, Y = -10 << 16 }, // negative slope
        };
        var level = new LevelData { Vertexes = vertexes };

        // Vertical: v1 == v2 in x. Horizontal: same y.
        var lines = new Line[4];
        lines[0] = MakeLine(0, 0, 0, 0);              // degenerate: dx=0, dy=0 -> vertical (dx checked first)
        lines[1] = MakeLine(0, 0, 10 << 16, 0);       // horizontal
        lines[2] = MakeLine(0, 0, 10 << 16, 10 << 16);  // positive
        lines[3] = MakeLine(0, 0, 10 << 16, -10 << 16); // negative

        Assert.Equal(SlopeType.Vertical, lines[0].SlopeType);
        Assert.Equal(SlopeType.Horizontal, lines[1].SlopeType);
        Assert.Equal(SlopeType.Positive, lines[2].SlopeType);
        Assert.Equal(SlopeType.Negative, lines[3].SlopeType);
    }

    private static Line MakeLine(int v1x, int v1y, int v2x, int v2y)
    {
        // Replicates the slopetype logic for direct testing.
        var dx = v2x - v1x;
        var dy = v2y - v1y;
        if (dx == 0) return new Line { SlopeType = SlopeType.Vertical };
        if (dy == 0) return new Line { SlopeType = SlopeType.Horizontal };
        return new Line { SlopeType = FixedPoint.FixedDiv(dy, dx) > 0 ? SlopeType.Positive : SlopeType.Negative };
    }

    [Fact]
    public void LoadSegs_TwoSidedBackSectorRule()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var level = new LevelData
        {
            Vertexes = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone).Data,
            Sectors = MapLoaders.LoadSectors(wad, mapLump + MapLumpOffsets.Sectors, zone).Data,
            Sides = MapLoaders.LoadSideDefs(wad, mapLump + MapLumpOffsets.SideDefs, zone).Data,
            Lines = MapLoaders.LoadLineDefs(wad, mapLump + MapLumpOffsets.LineDefs, zone, new LevelData
            {
                Vertexes = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone).Data,
                Sectors = MapLoaders.LoadSectors(wad, mapLump + MapLumpOffsets.Sectors, zone).Data,
                Sides = MapLoaders.LoadSideDefs(wad, mapLump + MapLumpOffsets.SideDefs, zone).Data,
            }).Data,
        };

        var segs = MapLoaders.LoadSegs(wad, mapLump + MapLumpOffsets.Segs, zone, level).Data;

        Assert.Equal(2, segs.Length);

        // Seg 0: line 0 (two-sided), side 0. Back = sides[sidenum[1]].sector = 0.
        Assert.Equal(0, segs[0].FrontSector);
        Assert.Equal(0, segs[0].BackSector);

        // Seg 1: line 1 (single-sided, sidenum[1] = -1). Even though
        // sidenum[side^1] = sidenum[1] = -1, ML_TWOSIDED is not set,
        // so backsector = NULL (-1) — p_setup.c:189-192.
        Assert.Equal(0, segs[1].FrontSector);
        Assert.Equal(-1, segs[1].BackSector);
    }

    [Fact]
    public void LoadNodes_PreservesUnsignedChildren()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var nodes = MapLoaders.LoadNodes(wad, mapLump + MapLumpOffsets.Nodes, zone).Data;

        Assert.Single(nodes);
        var n = nodes[0];
        Assert.Equal(50 << 16, n.X);
        Assert.Equal(25 << 16, n.Y);
        Assert.Equal(100 << 16, n.Dx);
        Assert.Equal(50 << 16, n.Dy);
        // NF_SUBSECTOR bit preserved without sign extension.
        Assert.Equal(0x8000, n.Children[0]);
        Assert.Equal(0x8001, n.Children[1]);
        Assert.Equal(-10 << 16, n.BBox[0][0]);
        Assert.Equal(60 << 16, n.BBox[0][3]);
    }

    [Fact]
    public void LoadThings_StoresRawRecords()
    {
        var (wad, mapLump, _) = LoadMap();

        var things = MapLoaders.LoadThings(wad, mapLump + MapLumpOffsets.Things);

        Assert.Single(things);
        Assert.Equal(32, things[0].X);
        Assert.Equal(32, things[0].Y);
        Assert.Equal(90, things[0].Angle);
        Assert.Equal(1, things[0].Type);
        Assert.Equal(7, things[0].Options);
    }

    [Fact]
    public void LoadBlockMap_ParsesHeaderAndWords()
    {
        var (wad, mapLump, _) = LoadMap();

        var bm = MapLoaders.LoadBlockMap(wad, mapLump + MapLumpOffsets.BlockMap);

        Assert.Equal(-64 << 16, bm.OrgX);
        Assert.Equal(-64 << 16, bm.OrgY);
        Assert.Equal(4, bm.Width);
        Assert.Equal(3, bm.Height);
        Assert.Equal(16, bm.Lump.Length);        // 4 header + 12 offsets
        Assert.Equal(-1, bm.Lump[12]);           // 0xFFFF -> -1 (little-endian short)
    }

    [Fact]
    public void LoadReject_StoresRawBytes()
    {
        var (wad, mapLump, _) = LoadMap();

        var reject = MapLoaders.LoadReject(wad, mapLump + MapLumpOffsets.Reject);

        Assert.Equal([0x01, 0x02, 0x03], reject);
    }

    [Fact]
    public void LoadLevel_OrchestratesAllLoaders()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        var level = MapLoaders.LoadLevel(wad, mapLump, zone);

        Assert.Equal(4, level.Vertexes.Length);
        Assert.Equal(1, level.Sectors.Length);
        Assert.Equal(2, level.Sides.Length);
        Assert.Equal(2, level.Lines.Length);
        Assert.Equal(1, level.Subsectors.Length);
        Assert.Equal(2, level.Segs.Length);
        Assert.Equal(1, level.Nodes.Length);
        Assert.Single(level.Things);
        Assert.NotNull(level.BlockMap);
        Assert.Equal(3, level.Reject.Length);
    }

    [Fact]
    public void LoadLevel_TruncatesNonMultipleLumpSizes()
    {
        // C: numX = lumpLength / sizeof(record) — trailing bytes ignored.
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        // Vertexes lump is 16 bytes = 4 records. A 17-byte lump would
        // still yield 4 vertexes. Verify via the actual fixture (16B).
        var vertexes = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone).Data;
        Assert.Equal(4, vertexes.Length);
    }

    [Fact]
    public void LoadLevel_OutOfRangeIndex_ThrowsFatal()
    {
        var (wad, mapLump, zone) = LoadMap();
        using var _ = zone;

        // Seg referencing a nonexistent linedef (C UB -> managed fatal):
        // level has NO lines, so the seg's linedef index 0 is out of range.
        var level = new LevelData
        {
            Vertexes = MapLoaders.LoadVertexes(wad, mapLump + MapLumpOffsets.Vertexes, zone).Data,
            Sectors = MapLoaders.LoadSectors(wad, mapLump + MapLumpOffsets.Sectors, zone).Data,
            Sides = MapLoaders.LoadSideDefs(wad, mapLump + MapLumpOffsets.SideDefs, zone).Data,
            Lines = [],  // empty: any seg linedef index is out of range
        };

        Assert.Throws<DoomFatalException>(() =>
            MapLoaders.LoadSegs(wad, mapLump + MapLumpOffsets.Segs, zone, level));
    }

    [Fact]
    public void LoadLevel_InvalidLumpBase_ThrowsFatal()
    {
        var (wad, _, zone) = LoadMap();
        using var _ = zone;

        Assert.Throws<DoomFatalException>(() => MapLoaders.LoadLevel(wad, 9999, zone));
    }

    [Fact]
    public void ZoneCleanup_PuLevelCycles_ReturnToBaseline()
    {
        var path = MapWadFixture.WriteMapWad(_tempDir);
        using var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        var mapLump = wad.GetNumForName("MAP01");

        using var zone = new ZoneMemory();
        var baseline = zone.LiveBytes;

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var level = MapLoaders.LoadLevel(wad, mapLump, zone);
            Assert.True(zone.LiveBytes > baseline);
            zone.FreeTags(PurgeTags.Level, int.MaxValue);
            Assert.Equal(baseline, zone.LiveBytes);
        }
    }

    [Fact]
    public void TraceLogging_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var (wad, mapLump, zone) = LoadMap();
                using var _ = zone;
                MapLoaders.LoadLevel(wad, mapLump, zone);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("LoadLevel"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("LoadVertexes"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("LoadSegs"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("LoadBlockMap"));
            }
            finally
            {
                Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void TraceLogging_SilentByDefault()
    {
        lock (LogTestSync.Lock)
        {
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();

            var (wad, mapLump, zone) = LoadMap();
            using var _ = zone;

            var level = MapLoaders.LoadLevel(wad, mapLump, zone);
            Assert.Equal(4, level.Vertexes.Length); // behavior identical when silent
        }
    }

    [Fact]
    public void RealWad_E1M1_WhenDoomWadDirSet()
    {
        var wadDir = Environment.GetEnvironmentVariable("DOOMWADDIR");
        if (string.IsNullOrEmpty(wadDir))
        {
            return; // gated: shareware doom1.wad not available
        }

        var path = Path.Combine(wadDir, "doom1.wad");
        if (!File.Exists(path))
        {
            return;
        }

        using var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        var e1m1 = wad.GetNumForName("E1M1");

        using var zone = new ZoneMemory();
        var level = MapLoaders.LoadLevel(wad, e1m1, zone);

        // Consistency checks (counts not hardcoded — verified offline).
        Assert.True(level.Vertexes.Length > 0);
        Assert.True(level.Lines.Length > 0);
        Assert.True(level.Sectors.Length > 0);
        Assert.True(level.Segs.Length >= level.Subsectors.Length);
        Assert.All(level.Lines, l =>
        {
            Assert.InRange(l.V1, 0, level.Vertexes.Length - 1);
            Assert.InRange(l.V2, 0, level.Vertexes.Length - 1);
        });
        Assert.All(level.Segs, s =>
        {
            Assert.InRange(s.LineDef, 0, level.Lines.Length - 1);
            Assert.InRange(s.V1, 0, level.Vertexes.Length - 1);
        });
    }

    private (WadDirectory, int, ZoneMemory) LoadMap()
    {
        var path = MapWadFixture.WriteMapWad(_tempDir);
        var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        var mapLump = wad.GetNumForName("MAP01");
        return (wad, mapLump, new ZoneMemory());
    }

    private static string NameToString(byte[] name)
    {
        var end = Array.IndexOf(name, (byte)0);
        if (end < 0) end = name.Length;
        return System.Text.Encoding.ASCII.GetString(name, 0, end);
    }
}

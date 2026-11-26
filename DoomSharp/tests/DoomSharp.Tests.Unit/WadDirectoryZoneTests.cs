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
//      Tests for the WadDirectory zone integration (DOOM-005):
//      the W_CacheLumpNum tag lifecycle (w_wad.c:475-500) — cache
//      miss allocates through the zone, hit retags, purge invalidates
//      the slot so the lump is re-read from file.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class WadDirectoryZoneTests : IDisposable
{
    private readonly string _tempDir;

    public WadDirectoryZoneTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_zonewadtests", Guid.NewGuid().ToString("N"));
        WadTestFixtures.WriteFixtures(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void CacheLumpNum_WithZone_MissAllocatesWithCallerTag()
    {
        using var zone = new ZoneMemory();
        using var dir = new WadDirectory(zone);
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        var data = dir.CacheLumpNum(dir.GetNumForName("E1M1"), PurgeTags.Level);

        Assert.Equal(WadTestFixtures.E1M1Data, data);
        Assert.Equal(1, zone.LiveBlocks);
    }

    [Fact]
    public void CacheLumpNum_HitRetags_LikeOriginalC()
    {
        // w_wad.c:496 — Z_ChangeTag(lumpcache[lump], tag) on cache hit.
        using var zone = new ZoneMemory();
        using var dir = new WadDirectory(zone);
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);
        var lump = dir.GetNumForName("E1M1");

        var first = dir.CacheLumpNum(lump, PurgeTags.Level);
        var second = dir.CacheLumpNum(lump, PurgeTags.Cache);

        Assert.Same(first, second);
    }

    [Fact]
    public void PurgeAndReload_RoundTrip_ReReadsFromDisk()
    {
        // The lumpcache[lump] write-back (w_wad.c:490): after the zone
        // frees the block, the slot is cleared and the next access
        // re-reads the lump — fresh instance, identical bytes.
        using var zone = new ZoneMemory();
        using var dir = new WadDirectory(zone);
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);
        var lump = dir.GetNumForName("E1M1");

        var first = dir.CacheLumpNum(lump, PurgeTags.Level);
        first[0] = 0xFF; // mutate to prove the reload is a fresh read

        // Level exit frees PU_LEVEL-tagged cached lumps (p_setup.c:613).
        zone.FreeTags(PurgeTags.Level, int.MaxValue);

        var second = dir.CacheLumpNum(lump, PurgeTags.Level);
        Assert.NotSame(first, second);
        Assert.Equal(WadTestFixtures.E1M1Data, second); // re-read from file
        Assert.Equal(8, second.Length);
    }

    [Fact]
    public void CacheLumpNum_PurgeLevelTag_WithoutZoneOwner_IsFatal()
    {
        // A purgable tag requires an owner; the per-lump owner is
        // always provided by the directory, so this path cannot fatal
        // through CacheLumpNum — but a direct zone call must.
        using var zone = new ZoneMemory();
        Assert.Throws<DoomFatalException>(
            () => zone.Allocate(new byte[10], PurgeTags.Cache));
    }

    [Fact]
    public void ZoneTracked_LevelExit_InvalidatesLevelTaggedLumps()
    {
        // p_setup.c:474,659 — blockmap/reject cached with PU_LEVEL are
        // freed at level exit; PU_CACHE lumps survive.
        using var zone = new ZoneMemory();
        using var dir = new WadDirectory(zone);
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        var e1m1 = dir.GetNumForName("E1M1");
        var map01 = dir.GetNumForName("MAP01");

        var levelLump = dir.CacheLumpNum(e1m1, PurgeTags.Level);
        var cacheLump = dir.CacheLumpNum(map01, PurgeTags.Cache);

        zone.FreeTags(PurgeTags.Level, PurgeTags.PurgeLevel - 1);

        // PU_LEVEL lump slot cleared; PU_CACHE lump still cached.
        var reloaded = dir.CacheLumpNum(e1m1, PurgeTags.Level);
        Assert.NotSame(levelLump, reloaded);
        Assert.Equal(WadTestFixtures.E1M1Data, reloaded);
        Assert.Same(cacheLump, dir.CacheLumpNum(map01, PurgeTags.Cache));
    }

    [Fact]
    public void ZoneTracked_RepeatedCycles_BoundedLiveBytes()
    {
        using var zone = new ZoneMemory();
        using var dir = new WadDirectory(zone);
        dir.InitMultipleFiles([Path.Combine(_tempDir, "test_iwad.wad")]);

        for (var cycle = 0; cycle < 50; cycle++)
        {
            var e1m1 = dir.CacheLumpNum(dir.GetNumForName("E1M1"), PurgeTags.Level);
            var map01 = dir.CacheLumpNum(dir.GetNumForName("MAP01"), PurgeTags.Level);
            Assert.Equal(8, e1m1.Length);

            zone.FreeTags(PurgeTags.Level, int.MaxValue);
            Assert.Equal(0, zone.LiveBytes);
        }
    }
}

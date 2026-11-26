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
//      Tests for ZoneMemory (DOOM-005, z_zone.c migration).
//      Characterization outcomes come from the C harness
//      (tools/charact/zonedump.c over the UNMODIFIED z_zone.c), dumped
//      to tools/charact/zone_vectors.json.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class ZoneMemoryTests
{
    // ---------- characterization: matches C dump (zone_vectors.json) ----------

    [Fact]
    public void FreeTags_LevelToPurgeLevelMinus1_FreesOnlyLevelTag()
    {
        // C dump "after_freetags_level": LEVEL block freed,
        // STATIC and CACHE blocks untouched.
        var zone = new ZoneMemory();
        var a = zone.Allocate(new byte[100], PurgeTags.Static);
        var b = zone.Allocate(new byte[200], PurgeTags.Level);
        var c = zone.Allocate(new byte[300], PurgeTags.Cache, new StubUser());

        zone.FreeTags(PurgeTags.Level, PurgeTags.PurgeLevel - 1);

        Assert.True(a.InUse);
        Assert.False(b.InUse);
        Assert.True(c.InUse);
    }

    [Fact]
    public void FreeTags_LevelToMaxInt_AlsoFreesCache()
    {
        // C dump "after_freetags_maxint": CACHE block freed too
        // (p_setup.c:613 — Z_FreeTags(PU_LEVEL, MAXINT)).
        var zone = new ZoneMemory();
        var a = zone.Allocate(new byte[100], PurgeTags.Static);
        var b = zone.Allocate(new byte[200], PurgeTags.Level);
        var c = zone.Allocate(new byte[300], PurgeTags.Cache, new StubUser());

        zone.FreeTags(PurgeTags.Level, int.MaxValue);

        Assert.True(a.InUse);
        Assert.False(b.InUse);
        Assert.False(c.InUse);
    }

    [Fact]
    public void ChangeTag_DemotesOwnedStaticToCache()
    {
        // C dump "after_changetag": owned STATIC block retagged to 101.
        var zone = new ZoneMemory();
        var owner = new StubUser();
        var a = zone.Allocate(new byte[100], PurgeTags.Static, owner);

        zone.ChangeTag(a, PurgeTags.Cache);

        Assert.Equal(PurgeTags.Cache, a.Tag);
        Assert.True(a.InUse);
    }

    [Fact]
    public void ChangeTag_UnownedToPurgable_IsFatal_LikeOriginalC()
    {
        // C harness caught this: z_zone.c:440 fatals when retagging an
        // unowned block (user sentinel < 0x100) to a purgable tag.
        var zone = new ZoneMemory();
        var a = zone.Allocate(new byte[100], PurgeTags.Static); // no owner

        Assert.Throws<DoomFatalException>(() => zone.ChangeTag(a, PurgeTags.Cache));
    }

    // ---------- fatal paths (all fatal=true in C dump) ----------

    [Fact]
    public void Exhaustion_WithCap_IsFatal_WithOriginalMessage()
    {
        // C dump: exhaust fatal=true ("Z_Malloc: failed on allocation of %i bytes")
        var zone = new ZoneMemory(4096);
        var ex = Assert.Throws<DoomFatalException>(
            () => zone.Allocate(new byte[1024 * 1024], PurgeTags.Static));
        Assert.Contains("failed on allocation of", ex.Message);
    }

    [Fact]
    public void PurgableWithoutOwner_IsFatal_LikeOriginalC()
    {
        // C dump: purgable_no_owner fatal=true (z_zone.c:275-276).
        var zone = new ZoneMemory();
        Assert.Throws<DoomFatalException>(
            () => zone.Allocate(new byte[64], PurgeTags.Cache));
    }

    [Fact]
    public void DoubleFree_IsFatal_LikeOriginalC()
    {
        // C dump: double_free fatal=true (ZONEID check, z_zone.c:129).
        var zone = new ZoneMemory();
        var block = zone.Allocate(new byte[64], PurgeTags.Static);
        zone.Free(block);

        Assert.Throws<DoomFatalException>(() => zone.Free(block));
    }

    [Fact]
    public void ChangeTag_OnFreedBlock_IsFatal_LikeOriginalC()
    {
        // C dump: changetag_freed fatal=true (z_zone.c:437).
        var zone = new ZoneMemory();
        var block = zone.Allocate(new byte[64], PurgeTags.Static);
        zone.Free(block);

        Assert.Throws<DoomFatalException>(() => zone.ChangeTag(block, PurgeTags.Cache));
    }

    [Fact]
    public void ChangeTag_ToPurgableWithoutOwner_IsFatal_LikeOriginalC()
    {
        // C dump: changetag_no_owner fatal=true (z_zone.c:440-441).
        var zone = new ZoneMemory();
        var block = zone.Allocate(new byte[64], PurgeTags.Static);

        Assert.Throws<DoomFatalException>(() => zone.ChangeTag(block, PurgeTags.Cache));
    }

    // ---------- owner write-back (the void** user mechanism) ----------

    [Fact]
    public void OnFreed_InvokedOnce_WithBlockReference()
    {
        var zone = new ZoneMemory();
        var owner = new StubUser();
        var block = zone.Allocate(new byte[64], PurgeTags.Static, owner);

        zone.Free(block);

        Assert.Equal(1, owner.FreedCount);
        Assert.Same(block, owner.LastFreed);
        Assert.False(block.InUse);
        Assert.Null(block.Owner); // owner detached after write-back
    }

    [Fact]
    public void FreeTags_InvokesOnFreed_InListOrder()
    {
        // z_zone.c:304-317 — Z_FreeTags iterates the block list in order.
        var zone = new ZoneMemory();
        var owner1 = new StubUser();
        var owner2 = new StubUser();
        var owner3 = new StubUser();
        var b1 = zone.Allocate(new byte[10], PurgeTags.Level, owner1);
        var b2 = zone.Allocate(new byte[20], PurgeTags.Level, owner2);
        var b3 = zone.Allocate(new byte[30], PurgeTags.Level, owner3);

        zone.FreeTags(PurgeTags.Level, PurgeTags.Level);

        Assert.Equal(1, owner1.FreedCount);
        Assert.Equal(1, owner2.FreedCount);
        Assert.Equal(1, owner3.FreedCount);
        // List order = allocation order in the managed model.
        Assert.Same(b1, owner1.LastFreed);
        Assert.Same(b2, owner2.LastFreed);
        Assert.Same(b3, owner3.LastFreed);
    }

    // ---------- bounded memory across level cycles ----------

    [Fact]
    public void LevelCycles_ReturnLiveBytesToBaseline()
    {
        var zone = new ZoneMemory();
        var baseline = zone.LiveBytes;

        for (var cycle = 0; cycle < 100; cycle++)
        {
            // Simulate a level load: level data + cached lumps.
            for (var i = 0; i < 10; i++)
            {
                zone.Allocate(new byte[4096], PurgeTags.Level);
            }
            for (var i = 0; i < 20; i++)
            {
                zone.Allocate(new byte[8192], PurgeTags.Cache, new StubUser());
            }

            Assert.True(zone.LiveBytes > baseline, "level data should be live");

            // Level exit (p_setup.c:613): frees level data AND cache.
            zone.FreeTags(PurgeTags.Level, int.MaxValue);

            Assert.Equal(baseline, zone.LiveBytes);
            Assert.Equal(0, zone.LiveBlocks);
        }
    }

    [Fact]
    public void LevelExit_KeepingCache_PreservesCacheBlocks()
    {
        // p_setup.c:618 — Z_FreeTags(PU_LEVEL, PU_PURGELEVEL-1) keeps cache.
        var zone = new ZoneMemory();
        var levelBlock = zone.Allocate(new byte[100], PurgeTags.Level);
        var cacheBlock = zone.Allocate(new byte[200], PurgeTags.Cache, new StubUser());

        zone.FreeTags(PurgeTags.Level, PurgeTags.PurgeLevel - 1);

        Assert.False(levelBlock.InUse);
        Assert.True(cacheBlock.InUse);
    }

    [Fact]
    public void Cap_ReproducesExhaustionAcrossCycles()
    {
        // With a cap, repeated level cycles must not leak: each cycle
        // must fit within the cap after FreeTags.
        var zone = new ZoneMemory(64 * 1024);
        var owner = new StubUser();

        for (var cycle = 0; cycle < 10; cycle++)
        {
            var blocks = new List<ZoneBlock<byte[]>>();
            for (var i = 0; i < 8; i++)
            {
                blocks.Add(zone.Allocate(new byte[4096], PurgeTags.Level));
            }
            zone.Allocate(new byte[8192], PurgeTags.Cache, owner);

            zone.FreeTags(PurgeTags.Level, int.MaxValue);
            Assert.Equal(0, zone.LiveBytes);
        }
    }

    [Fact]
    public void CheckHeap_ValidatesAccounting()
    {
        var zone = new ZoneMemory();
        zone.Allocate(new byte[100], PurgeTags.Static);
        zone.CheckHeap(); // no throw

        zone.FreeTags(PurgeTags.Static, PurgeTags.Static);
        zone.CheckHeap(); // no throw after frees
    }

    [Fact]
    public void UnboundedZone_AcceptsLargeAllocations()
    {
        // ADR-003: default zone is unbounded — modern PWADs not constrained.
        var zone = new ZoneMemory();
        var big = zone.Allocate(new byte[32 * 1024 * 1024], PurgeTags.Level);
        Assert.True(big.InUse);
        Assert.Equal(32 * 1024 * 1024, big.Size);
    }

    private sealed class StubUser : IZoneUser
    {
        public int FreedCount { get; private set; }
        public ZoneBlock? LastFreed { get; private set; }

        public void OnFreed(ZoneBlock block)
        {
            FreedCount++;
            LastFreed = block;
        }
    }
}

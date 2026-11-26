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
//      Zone memory allocation — managed semantic replacement.
//      Migration of Legacy/linuxdoom-1.10/z_zone.c / z_zone.h.
//      See docs/adr/ADR-003-zone-allocator-replacement.md.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Core;

/// <summary>
/// Purge tags (z_zone.h:36-44). Original numeric values preserved:
/// tags &lt; PurgeLevel are not freed until explicitly released;
/// tags &gt;= PurgeLevel are purgable.
/// </summary>
public static class PurgeTags
{
    /// <summary>Static entire execution time (PU_STATIC).</summary>
    public const int Static = 1;

    /// <summary>Static while playing (PU_SOUND).</summary>
    public const int Sound = 2;

    /// <summary>Static while playing (PU_MUSIC).</summary>
    public const int Music = 3;

    /// <summary>Anything else Dave wants static (PU_DAVE).</summary>
    public const int Dave = 4;

    /// <summary>Static until level exited (PU_LEVEL).</summary>
    public const int Level = 50;

    /// <summary>A special thinker in a level (PU_LEVSPEC).</summary>
    public const int LevSpec = 51;

    /// <summary>Tags &gt;= this are purgable whenever needed (PU_PURGELEVEL).</summary>
    public const int PurgeLevel = 100;

    /// <summary>Purgable cache (PU_CACHE).</summary>
    public const int Cache = 101;
}

/// <summary>
/// Owner write-back callback, replacing the original <c>void** user</c>
/// pointer write-back and its zeroing on free (z_zone.c:138,271).
/// Implementations clear their reference so cached data is re-read
/// after a purge (e.g. lumpcache[lump] invalidation, w_wad.c:490).
/// </summary>
public interface IZoneUser
{
    /// <summary>Called exactly once when the block is freed.</summary>
    void OnFreed(ZoneBlock block);
}

/// <summary>
/// A zone-tracked allocation. Replaces <c>memblock_t</c>
/// (z_zone.h:58-66); identity replaces the ZONEID pointer arithmetic.
/// </summary>
public class ZoneBlock
{
    private protected ZoneBlock(int size, int tag, IZoneUser? owner)
    {
        Size = size;
        Tag = tag;
        Owner = owner;
    }

    /// <summary>Allocation size in bytes (memblock_t.size).</summary>
    public int Size { get; }

    /// <summary>Purge tag (memblock_t.tag).</summary>
    public int Tag { get; internal set; }

    /// <summary>Owner receiving the OnFreed write-back (memblock_t.user).</summary>
    public IZoneUser? Owner { get; internal set; }

    /// <summary>False once freed (memblock_t.user == NULL).</summary>
    public bool InUse { get; internal set; } = true;

    /// <summary>The managed payload attached to this block.</summary>
    public object? Data { get; internal set; }
}

/// <summary>
/// A zone-tracked allocation with a typed payload, replacing the
/// <c>ptr = Z_Malloc(size, tag, &amp;holder)</c> pattern where the caller
/// keeps a typed pointer to the data.
/// </summary>
public sealed class ZoneBlock<T> : ZoneBlock where T : class
{
    internal ZoneBlock(T data, int size, int tag, IZoneUser? owner)
        : base(size, tag, owner)
    {
        Data = data;
    }

    /// <summary>The typed managed payload (the allocated data itself).</summary>
    public new T Data { get; internal set; }
}

/// <summary>
/// Managed, tag-tracked memory zone. Semantic replacement of the
/// original zone allocator (z_zone.c): same tags, same ownership
/// write-back, same tag-scoped freeing — without simulating the fixed
/// arena. See ADR-003.
/// </summary>
public sealed class ZoneMemory : IDisposable
{
    private readonly List<ZoneBlock> _blocks = [];
    private readonly long? _maxBytes;
    private long _liveBytes;

    /// <summary>Creates an unbounded zone (GC-managed heap).</summary>
    public ZoneMemory()
    {
    }

    /// <summary>
    /// Creates a zone with an optional byte cap reproducing the
    /// original zone-exhaustion fatal (z_zone.c:220). Opt-in per ADR-003.
    /// </summary>
    public ZoneMemory(long maxBytes)
    {
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// Bytes currently in use with tags below PurgeLevel — the
    /// Z_FreeMemory analog restricted to non-purgable blocks
    /// (z_zone.c:451-466 counts free + purgable as "available").
    /// </summary>
    public long LiveBytes => _liveBytes;

    /// <summary>Number of in-use blocks (diagnostics/tests).</summary>
    public int LiveBlocks { get; private set; }

    /// <summary>
    /// Z_Malloc (z_zone.c:183-289): allocates a block with the given
    /// tag and optional owner, attaching the typed payload.
    /// Fatal if a purgable tag has no owner (z_zone.c:275-276) or, when
    /// a cap is set, on exhaustion (z_zone.c:220).
    /// </summary>
    public ZoneBlock<T> Allocate<T>(T data, int tag, IZoneUser? owner = null) where T : class
    {
        if (tag >= PurgeTags.PurgeLevel && owner == null)
        {
            throw new DoomFatalException(
                "Z_Malloc: an owner is required for purgable blocks");
        }

        var size = EstimateSize(data);

        if (_maxBytes is { } cap && _liveBytes + size > cap)
        {
            throw new DoomFatalException(
                $"Z_Malloc: failed on allocation of {size} bytes");
        }

        var block = new ZoneBlock<T>(data, size, tag, owner);
        _blocks.Add(block);
        _liveBytes += size;
        LiveBlocks++;
        return block;
    }

    /// <summary>
    /// Z_Free (z_zone.c:122-172): frees a block and invokes the owner's
    /// OnFreed write-back. Fatal on double free / unknown block
    /// (replaces the ZONEID check, z_zone.c:129-130).
    /// </summary>
    public void Free(ZoneBlock block)
    {
        if (block == null || !block.InUse || !_blocks.Contains(block))
        {
            throw new DoomFatalException("Z_Free: freed a pointer without ZONEID");
        }

        FreeCore(block);
    }

    /// <summary>
    /// Z_FreeTags (z_zone.c:296-318): frees every in-use block with
    /// lowtag &lt;= tag &lt;= hightag, invoking OnFreed in list order.
    /// </summary>
    public void FreeTags(int lowTag, int highTag)
    {
        // Snapshot: FreeCore mutates the list (z_zone.c:308-309 —
        // "get link before freeing").
        foreach (var block in _blocks.ToArray())
        {
            if (block.InUse && block.Tag >= lowTag && block.Tag <= highTag)
            {
                FreeCore(block);
            }
        }
    }

    /// <summary>
    /// Z_ChangeTag2 (z_zone.c:428-444): retags an in-use block.
    /// Fatal on freed/unknown block (ZONEID check) and on purgable
    /// retag without owner (z_zone.c:440-441).
    /// </summary>
    public void ChangeTag(ZoneBlock block, int newTag)
    {
        if (block == null || !block.InUse || !_blocks.Contains(block))
        {
            throw new DoomFatalException("Z_ChangeTag: freed a pointer without ZONEID");
        }

        if (newTag >= PurgeTags.PurgeLevel && block.Owner == null)
        {
            throw new DoomFatalException(
                "Z_ChangeTag: an owner is required for purgable blocks");
        }

        block.Tag = newTag;
    }

    /// <summary>
    /// Z_CheckHeap (z_zone.c:399-420): validates zone invariants.
    /// In the managed model the list cannot break links, so this
    /// verifies the invariants that remain meaningful: no double-free
    /// bookkeeping errors and consistent live-byte accounting.
    /// </summary>
    public void CheckHeap()
    {
        long total = 0;
        var live = 0;
        foreach (var block in _blocks)
        {
            if (block.InUse)
            {
                total += block.Size;
                live++;
            }
        }

        if (total != _liveBytes || live != LiveBlocks)
        {
            throw new DoomFatalException("Z_CheckHeap: zone bookkeeping inconsistent");
        }
    }

    private void FreeCore(ZoneBlock block)
    {
        block.InUse = false;
        _liveBytes -= block.Size;
        LiveBlocks--;

        // Owner write-back (z_zone.c:132-139: *block->user = 0).
        // The OnFreed callback is the user-side invalidation; the
        // payload reference itself is left alone (GC-managed lifetime,
        // mirroring C where freed memory persists until reused).
        block.Owner?.OnFreed(block);
        block.Owner = null;
    }

    private static int EstimateSize<T>(T data) where T : class
    {
        return data switch
        {
            byte[] a => a.Length,
            Array a => Buffer.ByteLength(a),
            string s => s.Length * 2,
            _ => 64, // opaque object: nominal tracking size
        };
    }

    /// <summary>
    /// Frees all remaining blocks (invoking OnFreed write-backs) and
    /// detaches the zone. Blocks must not be used afterwards.
    /// </summary>
    public void Dispose()
    {
        FreeTags(0, int.MaxValue);
        _blocks.Clear();
    }
}

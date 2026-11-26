# ADR-003 — Zone allocator semantic replacement

- **Status:** Accepted
- **Date:** 2026-11-26
- **Deciders:** Project owner (approved DOOM-005 plan)
- **Scope:** All engine memory allocation with purge tags

## Context

The original engine manages all game memory through a custom zone
allocator (`z_zone.c`, 468 lines) over a fixed ~6 MB arena obtained from
`I_ZoneBase` (`z_zone.c:98`):

- Blocks are `memblock_t` nodes in a doubly-linked list
  (`z_zone.h:58-66`): `size`, `user`, `tag`, `id` (ZONEID `0x1d4a11`),
  `next`, `prev`.
- `Z_Malloc` (`z_zone.c:183-289`) is a rover-based first-fit scan that
  **evicts purgable blocks (`tag >= PU_PURGELEVEL`) encountered during
  the scan**, splits blocks with `MINFRAGMENT=64`, and calls
  `I_Error("Z_Malloc: failed on allocation of %i bytes")` when the whole
  list is scanned without success (`z_zone.c:220`).
- The `user` field is a `void**` write-back: the zone stores the data
  pointer into the caller's variable (`z_zone.c:271`) and **zeroes it on
  free** (`z_zone.c:138`). This is how `lumpcache[lump]` self-invalidates
  when a cached lump is purged (`w_wad.c:490`).
- Unowned blocks use the sentinel `user = (void*)2` (`z_zone.c:279`);
  the `user > 0x100` test (`z_zone.c:132`) distinguishes real pointers
  from sentinels.
- `Z_FreeTags(lowtag, hightag)` (`z_zone.c:296-318`) frees every in-use
  block with `lowtag <= tag <= hightag`. Level exit uses
  `Z_FreeTags(PU_LEVEL, MAXINT)` (also purge cache) or
  `Z_FreeTags(PU_LEVEL, PU_PURGELEVEL-1)` (keep cache) — `p_setup.c:613,618`.
- `Z_ChangeTag2` (`z_zone.c:428-444`) retags with ZONEID and
  owner-required checks; heavily used to demote graphics/sound to
  `PU_CACHE` (st_stuff, wi_stuff, s_sound, r_data).
- Tags (`z_zone.h:36-44`): `PU_STATIC=1, PU_SOUND=2, PU_MUSIC=3,
  PU_DAVE=4, PU_LEVEL=50, PU_LEVSPEC=51, PU_PURGELEVEL=100, PU_CACHE=101`.

## Decision

Replace the arena allocator with a **managed, tag-tracked ownership
model** (`DoomSharp.Core.ZoneMemory`). The GC heap replaces the fixed
arena; the *semantics* (tags, ownership, tag-scoped freeing, purge
lifecycle) are preserved exactly.

1. **Tags**: `PurgeTags` constants preserve the original numeric values
   (`Static=1, Sound=2, Music=3, Dave=4, Level=50, LevSpec=51,
   PurgeLevel=100, Cache=101`). Tag-range logic (`< PurgeLevel` vs
   `>= PurgeLevel`) is identical.
2. **Blocks**: `ZoneBlock` carries `Tag`, `Size`, `Owner`, `InUse`, and
   a typed `Data` payload reference. `ZoneBlock<T>` attaches the managed
   object (lump buffer, map structure) directly to the zone-tracked
   block, replacing the `void*` + header-pointer-arithmetic pattern.
3. **Owner write-back**: `IZoneUser.OnFreed(ZoneBlock)` replaces the
   `void** user` write-back / `*user = 0` invalidation
   (`z_zone.c:138,271`). `WadDirectory` implements it to clear the
   lump's cache slot so the next `CacheLumpNum` re-reads from file —
   the original purge/reload round-trip.
4. **No fixed heap simulation by default**: `ZoneMemory()` is unbounded
   (GC-managed). An opt-in `ZoneMemory(long maxBytes)` cap reproduces
   the zone-exhaustion fatal for testing; modern PWADs are not
   artificially constrained.
5. **Eviction during allocation scan is not simulated** (no pressure in
   an unbounded heap). The same observable outcomes are produced by
   `FreeTags` at level exit; with a cap set, exhaustion is fatal with
   the original message text.
6. **Fatal checks preserved as `DoomFatalException`** (ADR-002):
   - free/change-tag of an already-freed or unknown block (replaces the
     ZONEID checks, `z_zone.c:129,437`)
   - purgable allocation/retag without owner (`z_zone.c:276,441`)
   - exhaustion with cap set (`z_zone.c:220`, original message)
   - `CheckHeap` invariant violations (`z_zone.c:412-418`)
7. **Obsolete, not ported**: pointer-arithmetic header peeking, the
   `user > 0x100` sentinel test, rover/merge/MINFRAGMENT bookkeeping,
   `Z_DumpHeap`/`Z_FileDumpHeap` (debug printf tools; replaced by
   `CheckHeap` + `LiveBytes`).

## WadDirectory integration

`WadDirectory` gains an optional zone; `CacheLumpNum(lump, tag)` mirrors
`w_wad.c:475-500`:

- **Miss**: allocate through the zone with the caller's tag and a
  per-lump owner whose `OnFreed` clears that lump's cache slot (the
  `&lumpcache[lump]` write-back) → next access re-reads from file.
- **Hit**: `ChangeTag(block, tag)` — the original hit path
  (`w_wad.c:496`).
- **Level exit**: `FreeTags(PurgeTags.Level, int.MaxValue)` frees
  `PU_LEVEL`-tagged cached lumps (blockmap `p_setup.c:474`, reject
  `p_setup.c:659`) and invalidates their slots; `PU_CACHE` lumps
  survive unless the MAXINT variant is used.

This resolves the DOOM-004 documented deviation (parameterless cache).

## Consequences

- Tag semantics, ownership, and purge/reload behavior are preserved and
  testable; memory pressure behavior is intentionally not simulated
  except under an explicit cap.
- Engine code must hold `ZoneBlock<T>` (not raw arrays) to keep
  tag-tracking alive; the GC cannot collect a live block's payload.
- `OnFreed` must be invoked exactly once per block, in list order for
  `FreeTags` (matching `Z_FreeTags` iteration, `z_zone.c:304-317`).

## Alternatives considered

- **Simulate the full 6 MB arena with rover/merging:** rejected —
  replicates C pointer arithmetic with no observable benefit; block
  layout is not part of game behavior.
- **Rely on GC alone (no tags):** rejected — `PU_LEVEL` vs `PU_CACHE`
  lifetime semantics are observable (level exit frees level data but
  keeps cached graphics) and drive memory behavior.
- **Mandatory 6 MB cap:** rejected by project owner — modern PWADs
  exceed the original zone; cap stays opt-in.

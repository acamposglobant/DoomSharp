# Migration Backlog — DoomSharp

> One item per independently testable migration slice. Status values: Proposed | Ready | In Progress | Blocked | Review | Done.
> Ordering follows the dependency graph in `SOURCE_INVENTORY.md`. Nothing is marked Done without test evidence.

## Testing strategy (behavioral equivalence)

Layered per `VALIDATION.md`:

1. **Characterization vectors from the C original.** For each migrated pure module, compile the original C file(s) standalone (they are mostly dependency-light) into a small harness that dumps input/output pairs to JSON. Reuse the vectors as xUnit tests against C#. Priority modules: `m_fixed.c`, `m_random.c`, `tables.c`, `w_wad.c` (directory parsing), `m_bbox.c`, `r_main.c` angle functions.
2. **Golden-file tests.** Savegames (`g_game.c:1198` `"version 110"` header) and demo lumps (`g_game.c:1555`) are fully specified binary formats — round-trip tests against original files anchor serialization compatibility.
3. **Differential frame tests.** Build linuxdoom 1.10 in Linux (WSL/Docker, risk R-01), run with `-playdemo` on a fixed IWAD, dump framebuffers at fixed tics; compare with C# renderer output (exact pixel match expected — the pipeline is deterministic integer math).
4. **Determinism controls.** `P_Random`/`M_Random` are LUT-based (`m_random.c`) and `I_GetTime` is the only wall-clock input (`i_system.c:88`) — inject a fake clock; input comes from demo files, making full-tick deterministic replay possible.
5. **Numeric rules.** All engine math is `int`/`fixed_t`/`angle_t` (32-bit). C# implementation uses `int`/`uint` with `unchecked` where wraparound is semantic; `FixedDiv2`'s `double` path is reproduced with `double` and truncating cast, validated by vectors (risk R-02).

## Mandatory trace logging (ADR-004, applies to ALL items)

Every class and method — existing and future — must emit structured
Serilog trace logs at `LogEventLevel.Verbose`:

- Each class declares `private static ILogger Log => Serilog.Log.ForContext(typeof(ClassName));` (re-resolving property, per ADR-004).
- Every public/internal method logs entry (key parameters) and/or outcome via `Log.Verbose("Method({Param1}, {Param2})", ...)` — structured templates, never `$""` interpolation.
- Hot-path methods (`FixedPoint`, `DoomRandom`, `AngleTables`, future renderer inner loops) guard with `if (Log.IsEnabled(LogEventLevel.Verbose))`.
- **Acceptance criterion for every backlog item:** emission test with a Verbose sink attached + silent-by-default test. PRs without trace logging on new classes/methods are not mergeable.

## Items

### DOOM-001 — Fixed-point arithmetic

- **Status:** Done (2026-11-26)
- **Original files:** `m_fixed.h`, `m_fixed.c`
- **Original symbols/functions:** `FixedMul` (`m_fixed.c:44`), `FixedDiv` (`m_fixed.c:58`), `FixedDiv2` (`m_fixed.c:70`), constants `FRACBITS`/`FRACUNIT` (`m_fixed.h:35-36`), `fixed_t` typedef
- **Responsibility:** 16.16 signed fixed-point multiply/divide with exact overflow behavior
- **Dependencies:** none (leaf); `I_Error` only on overflow path
- **Target project:** `DoomSharp.Core`
- **Target files/types:** `src/DoomSharp.Core/FixedPoint.cs` — `static class FixedPoint { FixedMul, FixedDiv, FixedDiv2 }`; `DoomFatalException.cs`
- **Semantic risks:** `FixedDiv2` computes in `double` and truncates toward zero (`m_fixed.c:80-86`); `FixedDiv` overflow sentinel `MININT`/`MAXINT` (`m_fixed.c:63`); signed right-shift of negative values (arithmetic in both C and C#, pinned by tests)
- **Test strategy:** characterization harness over the original C: random + boundary operands (0, ±1, MININT, MAXINT, values near the `(abs(a)>>14) >= abs(b)` threshold), compare against C# exactly
- **Acceptance criteria:** all vectors match bit-exactly; overflow path returns sentinels (not exceptions); documented ADR for `unchecked` usage
- **Evidence/notes:** `m_fixed.c:43-87`
- **Completion evidence (2026-11-26):**
  - 2068 characterization vectors + 16 fatal cases generated from the **unmodified** C sources via `DoomSharp/tools/charact/charact.c` (gcc 13, `-O0 -DNORMALUNIX -DLINUX`, Docker); full dump `tools/charact/vectors.json` (13.7 MB), test subset `tests/DoomSharp.Tests.Unit/CharacterizationVectors.json`.
  - All vectors pass bit-exactly; `FixedDiv(MININT, -1)` confirmed fatal in C (the `abs(MININT)` quirk defeats the guard) and throws `DoomFatalException` in C# per ADR-002.
  - ADR-001 (fixed-point representation) and ADR-002 (fatal error handling) accepted.
  - **Documented deviation:** `FixedDiv2` overflow throws `DoomFatalException` instead of calling `I_Error`/`exit(-1)` — per approved decision; host must terminate with non-success exit code at the boundary.

### DOOM-002 — Random number LUT

- **Status:** Done (2026-11-26)
- **Original files:** `m_random.h/c`
- **Original symbols/functions:** `rndtable[256]` (`m_random.c:31`), `P_Random` (`m_random.c:57`), `M_Random` (`m_random.c:63`), `M_ClearRandom` (`m_random.c:69`), `rndindex`/`prndindex`
- **Responsibility:** deterministic 0-255 pseudo-random sequence for gameplay (`P_Random`) and cosmetic effects (`M_Random`)
- **Dependencies:** none
- **Target project:** `DoomSharp.Core`
- **Target files/types:** `src/DoomSharp.Core/DoomRandom.cs` — `sealed class DoomRandom` with two independent index counters over the same 256-byte table
- **Semantic risks:** trivial; must keep two separate indices (demo determinism depends on `P_Random` stream only)
- **Test strategy:** full-sequence walk (256 values, wraparound), reset semantics
- **Acceptance criteria:** sequence matches `rndtable` exactly; indices wrap at 256
- **Evidence/notes:** `m_random.c:31-72`
- **Completion evidence (2026-11-26):**
  - Table extracted **mechanically** from `m_random.c` by `DoomSharp/tools/gen/run.ts` (no hand-copying) and cross-checked element-for-element against the C characterization dump (P_Random 256-value walk matches).
  - Tests: full walk, wraparound at 256, P/M stream independence, `M_ClearRandom` reset, spot values — all pass.
  - No semantic differences.

### DOOM-003 — Angle/ trig lookup tables

- **Status:** Done (2026-11-26)
- **Original files:** `tables.h`, `tables.c` (2130 lines, mostly data)
- **Original symbols:** `finetangent[4096]`, `finesine[10240]`, `tantoangle[2049]`, `ANG45/90/180/270`, `ANGLETOFINESHIFT`, `SLOPERANGE` (`tables.h:50-78`); consumers `R_PointToAngle` (`r_main.c:292`), `R_PointOnSide` (`r_main.c:162`)
- **Responsibility:** BAM angle constants and fixed-point trig LUTs
- **Dependencies:** `m_fixed.h` (`fixed_t`)
- **Target project:** `DoomSharp.Core`
- **Target files/types:** `src/DoomSharp.Core/AngleTables.cs` (generated static arrays)
- **Semantic risks:** `finecosine` is an aliased pointer into `finesine` (`tables.h:61`); BAM wraparound is unsigned-overflow semantics
- **Test strategy:** dump tables from C; equality test in C#; spot-check `R_PointToAngle` quadrant behavior with vectors
- **Acceptance criteria:** tables byte-identical; aliasing behavior documented
- **Evidence/notes:** `tables.c` data can be extracted mechanically
- **Completion evidence (2026-11-26):**
  - Tables extracted **mechanically** from `tables.c` by `DoomSharp/tools/gen/run.ts` and cross-checked element-for-element (10240 + 4096 + 2049 entries) against the C characterization dump — exact match.
  - `FineCosine` implemented as the PI/2-rotated copy of `FineSine` (mirrors C pointer aliasing `finecosine = finesine + FINEANGLES/4`); verified by test.
  - Notable original quirk preserved: `finesine` peaks at **65535**, not `FRACUNIT` (65536) — captured in tests.
  - `R_PointToAngle`/`R_PointOnSide` consumers are part of the renderer (DOOM-030), not this item.
  - No semantic differences.

### DOOM-004 — WAD file reader

- **Status:** Done (2026-11-26)
- **Original files:** `w_wad.h`, `w_wad.c`
- **Original symbols:** `wadinfo_t`, `filelump_t`, `lumpinfo_t` (`w_wad.h:35-60`), `W_AddFile` (`w_wad.c:141`), `W_CheckNumForName` (`w_wad.c:351`), `W_GetNumForName`, `W_ReadLump`, `W_CacheLumpNum`, `W_InitMultipleFiles` (`w_wad.c:303`), `ExtractFileBase` (`w_wad.c:93-114`)
- **Responsibility:** WAD header/directory parsing, lump lookup (8-char name, case-insensitive, last-match-wins), lump caching
- **Dependencies:** z_zone (cache), i_system (file I/O, `I_Error`), m_swap
- **Target project:** `DoomSharp.Data`
- **Target files/types:** `src/DoomSharp.Data/WadFile.cs` (WadFile, SingleLumpFile, LumpInfo, ILumpSource), `src/DoomSharp.Data/WadDirectory.cs`
- **Semantic risks:** name comparison packs 8 bytes into two ints and scans **backwards** so PWAD lumps override IWAD (`w_wad.c:368-383`); `O_BINARY`/`filelength` POSIX bits; lump cache purge-tag interplay with z_zone
- **Test strategy:** parse a real shareware `doom1.wad` (freely redistributable, risk R-05): header, lump count, names, offsets; malformed/truncated WAD tests; lookup precedence tests (duplicate names)
- **Acceptance criteria:** directory identical to reference dump; lookup order matches C (backwards scan); graceful errors on malformed input
- **Evidence/notes:** `w_wad.c:125-303` (`W_AddFile`), `w_wad.c:347-395`
- **Completion evidence (2026-11-26):**
  - C characterization harness `tools/charact/waddump.c` compiles the **unmodified** `w_wad.c` (gcc 13, Docker) over synthetic fixtures; dump `tools/charact/wad_vectors.json` confirms: directory layout (4 lumps incl. single-lump DEHACKED), backwards-scan precedence (MAP01 → PWAD copy, index 2), case-insensitive lookup, 8-char name semantics, fatal out-of-range and missing-lump paths.
  - C# `WadDirectory` matches the C dump exactly (directory entries, positions, sizes, lookup results, duplicate precedence data).
  - All original `I_Error` paths map to `DoomFatalException` per ADR-002 (bad WAD id, lump not found, out-of-range index, short read, empty directory); unopenable optional files are skipped with a message (original behavior).
  - Bounds validation added per review: `numlumps >= 0`, directory table within file, lump `pos+size` within file.
  - 8-byte name handling: NUL-padded names normalize, full 8-char names preserved, ASCII-only uppercasing; lookup names longer than 8 chars do NOT match shorter lump names (matches C packed-int comparison).
  - `ExtractFileBase` parity: both separators, >8 chars fatal, stops at `.`.
  - `WadDirectory` is `IDisposable`, disposes all opened files even on partial init failure.
  - Managed lump cache replaces z_zone purge tags (documented deviation, revisited in DOOM-005).
  - Shareware IWAD test gated on `DOOMWADDIR` env var (skips when absent).
  - Tests: 2117 passing (18 new WAD tests), 0 warnings.

### DOOM-005 — Zone memory allocator (semantic replacement)

- **Status:** Done (2026-11-26)
- **Original files:** `z_zone.h`, `z_zone.c`, `i_system.c` (`I_ZoneBase`, 6 MB)
- **Original symbols:** `Z_Init`, `Z_Malloc`, `Z_Free`, `Z_FreeTags`, `Z_ChangeTag2`, `memblock_t`, `PU_*` tags (`z_zone.h:36-44`)
- **Responsibility:** tagged purge-able allocator; `PU_LEVEL`/`PU_CACHE` semantics drive what is freed between levels
- **Dependencies:** i_system
- **Target project:** `DoomSharp.Core`
- **Target files/types:** `src/DoomSharp.Core/ZoneMemory.cs` — `ZoneMemory`, `ZoneBlock<T>`, `PurgeTags`, `IZoneUser` (ADR-003)
- **Semantic risks:** original relies on `Z_Malloc` failing/purging when the 6 MB zone fills; C# has no equivalent pressure. Behavior-visible only through cache eviction; document as intentional deviation if replaced
- **Test strategy:** if ported: allocation/free/tag tests mirroring `z_zone.c`; if replaced: level-load memory equivalence checks
- **Acceptance criteria:** ADR accepted; level load/unload cycles produce no unbounded growth; purge behavior documented
- **Evidence/notes:** `z_zone.h:36-66`, `z_zone.c:43,91-130`
- **Completion evidence (2026-11-26):**
  - ADR-003 accepted: managed tag-tracked ownership model; fixed arena/rover/merging not simulated; `maxBytes` cap opt-in (default unbounded, per project owner decision).
  - C characterization harness `tools/charact/zonedump.c` compiles the **unmodified** `z_zone.c` (gcc 13, Docker); dump `tools/charact/zone_vectors.json` confirms: `FreeTags(PU_LEVEL, PU_PURGELEVEL-1)` frees only level tags, `FreeTags(PU_LEVEL, MAXINT)` also frees `PU_CACHE`, owned-block retag to `PU_CACHE`, and all five fatal paths (exhaustion, purgable-without-owner, double free, ChangeTag on freed block, ChangeTag to purgable without owner).
  - Harness discovery: retagging an **unowned** block to a purgable tag is fatal in the original C (`z_zone.c:440` sentinel check) — reproduced in C#.
  - C# `ZoneMemory` matches all C outcomes; `OnFreed` write-back replaces the `void** user` zeroing; `ZoneBlock<T>` carries typed payloads for lump buffers and future map structures.
  - `WadDirectory` zone integration: `CacheLumpNum(lump, tag)` mirrors `w_wad.c:475-500` (miss allocates with caller tag + per-lump owner, hit retags, purge invalidates slot → lump re-read from file). Resolves the DOOM-004 documented deviation.
  - Tests: 2139 passing (22 new zone/WAD-zone tests), 0 warnings. Level cycles return `LiveBytes` to baseline; capped zone reproduces exhaustion fatal with original message.

### DOOM-010 — Platform boundary: clock, error, quit

- **Status:** Done (2026-11-26)
- **Original files:** `i_system.h/c`
- **Original symbols:** `I_GetTime` (`i_system.c:88`, 35 tics/s from `gettimeofday`), `I_Error` (`i_system.c:162`), `I_Quit`, `I_WaitVBL`, `I_ZoneBase`
- **Responsibility:** wall-clock tics, fatal error path, shutdown
- **Dependencies:** libc only
- **Target project:** `DoomSharp.Platform` + interfaces `IClock`, `IErrorHandler` per `ARCHITECTURE.md`
- **Target files/types:** `src/DoomSharp.Platform/Clock.cs` (`IClock`, `SystemClock`, `FakeClock`, `TimeConstants`), `src/DoomSharp.Platform/DoomShutdown.cs` (`DoomShutdown`, `ShutdownRegistry`, `IErrorHandler`, `IQuitHandler`)
- **Semantic risks:** `I_Error` calls `exit(-1)` after shutdown — C# must decide exception vs Environment.Exit; tic conversion `tv_usec*TICRATE/1000000` integer truncation must be preserved
- **Test strategy:** fake clock tests for tic conversion boundaries
- **Acceptance criteria:** tic math matches C for sampled timestamps; error path writes message and shuts down cleanly
- **Evidence/notes:** `i_system.c:88-125,158-182`
- **Completion evidence (2026-11-26):**
  - `DoomSharp.Platform` project: `IClock`/`SystemClock`/`FakeClock`, `IErrorHandler`/`IQuitHandler`, `DoomShutdown` + `ShutdownRegistry`.
  - **Tic arithmetic preserved exactly:** `(sec * 35) + (usec * 35 / 1_000_000)` with integer truncation, computed in 64-bit (`Stopwatch` elapsed µs) — no 32-bit overflow; `TICRATE=35` (`doomdef.h:122`).
  - **Anchor note:** `SystemClock` anchors `Stopwatch` at 0 tics on the **first `GetTime()` call** (mirroring the C `static basetime` anchoring, `i_system.c:96-97`), avoiding sub-second wall-clock phase jitter while preserving the exact truncation expression.
  - **`I_WaitVBL`**: `count * (1000000/70)` = `count * 14285` µs (70 Hz VBL rate, distinct from TICRATE); `FakeClock` records waits without sleeping.
  - **`I_Error` → `DoomShutdown.Fatal`**: `"Error: <msg>\n"` to stderr + flush, LIFO shutdown callbacks, then `DoomFatalException` (ADR-002 — host terminates with non-success exit code; library never exits the process).
  - **`I_Quit`**: LIFO shutdown callbacks (atexit-style), then return; host performs `exit(0)`. `ShutdownRegistry` is instantiable with `Clear()` and `IDisposable` registration tokens for parallel-test isolation.
  - **No C harness (documented decision):** `I_GetTime` reads `gettimeofday` directly — unmockable without LD_PRELOAD or source modification. The arithmetic is ported literally and tested exhaustively on `FakeClock` boundary values (sub-tic truncation, exact 1/35 s boundaries, 24-hour uptime, monotonicity).
  - Tests: 2158 passing (19 new platform tests), 0 warnings.
  - Deferred: `I_StartFrame`/`I_StartTic`/`I_BaseTiccmd`/`I_Tactile` (input/event pump, DOOM-030+), `I_Init` (sound), `I_AllocLow` (trivial at call sites), `I_ZoneBase` (superseded by ADR-003).

### DOOM-020 — Map lump loaders

- **Status:** Done (2026-11-26)
- **Original files:** `p_setup.c`, `doomdata.h`
- **Original symbols:** `P_LoadVertexes` (`p_setup.c:122`), `P_LoadSegs` (`p_setup.c:159`), `P_LoadSubsectors` (`p_setup.c:202`), `P_LoadSectors` (`p_setup.c:231`), `P_LoadNodes` (`p_setup.c:264`), `P_LoadThings` (`p_setup.c:301`), `P_LoadLineDefs` (`p_setup.c:357`), `P_LoadSideDefs` (`p_setup.c:436`), `P_SetupLevel` (`p_setup.c:584`), `P_LoadBlockMap`, `P_LoadReject`
- **Responsibility:** parse all map lumps into runtime structures (`vertex_t`, `sector_t`, `line_t`, `node_t`, `subsector_t` in `r_defs.h`)
- **Dependencies:** DOOM-004 (WAD), DOOM-001 (fixed_t), DOOM-005 (zone), ADR-004 (tracing)
- **Target project:** `DoomSharp.Data`
- **Target files/types:** `MapRecords.cs` (raw `mapX_t` readers + `MapLumpOffsets` + `LineDefFlags` + `NodeFlags`), `MapDataTypes.cs` (runtime `Vertex`/`Side`/`Line`/`Sector`/`Seg`/`Subsector`/`Node`/`BlockMap`, `SlopeType`, `BBox`), `MapLoaders.cs` (10 loaders + `LoadLevel` orchestrator + `LevelData`)
- **Semantic risks:** `short` sign handling (e.g. `NF_SUBSECTOR 0x8000` vs `unsigned short children`, `doomdata.h:178-191`); little-endian reads; `RANGECHECK` aborts on bad data (`p_setup.c` uses `I_Error`)
- **Test strategy:** hand-derived synthetic map fixtures (binary builders per `doomdata.h` layout); all 4 `SlopeType` cases; `ML_TWOSIDED` vs single-sided backsector; `NF_SUBSECTOR` unsigned children; truncation on non-multiple lump sizes; out-of-range index fatals; `PU_LEVEL` zone cleanup cycles; optional `DOOMWADDIR` E1M1 consistency test; ADR-004 trace verification
- **Acceptance criteria:** all map structures match hand-derived expectations; malformed input fails with `DoomFatalException`, not a crash; zone returns to baseline after `FreeTags(PU_LEVEL, ...)`
- **Evidence/notes:** `p_setup.c:120-580`, `doomdata.h:60-210`. **Documented deviations:** (1) Option A — texture/flat names stored raw on `Side`/`Sector`; `R_TextureNumForName`/`R_FlatNumForName` resolution deferred to DOOM-030. (2) C pointer graph → int indices (`seg.v1`, `line.sidenum`, etc.). (3) C out-of-bounds UB (bad seg→linedef, line→vertex, sidedef→sector indices) → `DoomFatalException` per ADR-002. (4) Truncated blockmap (< 4 words) fatals; C would read garbage. (5) `P_LoadThings` stores raw `mapthing_t` records — `P_SpawnMapThing` is DOOM-021. (6) No C characterization harness: loaders are fully specified by `doomdata.h` offsets + conversion expressions; a harness would require stubbing `R_TextureNumForName`/zone/WAD globals — hand-derived binary fixtures give equivalent coverage (user-approved). (7) `ZoneMemory.EstimateSize` extended: reference-type arrays estimate 64 bytes/element (was `Buffer.ByteLength`, primitive-only). Test evidence: 2184/2184 passing (18 new map tests), 0 warnings.

### DOOM-021a — Thing spawning, thinker loop, player movement physics

- **Status:** Done (2026-11-27)
- **Original files:** `info.c`, `info.h`, `p_tick.c`, `p_mobj.c`, `p_user.c`, `p_map.c`, `p_maputl.c`, `p_local.h`
- **Original symbols:** `P_Ticker`/`P_RunThinkers`/`P_InitThinkers`/`P_AddThinker`/`P_RemoveThinker` (`p_tick.c`), `P_SpawnMobj`/`P_RemoveMobj`/`P_MobjThinker`/`P_XYMovement`/`P_ZMovement`/`P_NightmareRespawn` (`p_mobj.c`), `P_SpawnMapThing`/`P_SpawnPlayer` (`p_mobj.c`), `P_Thrust`/`P_CalcHeight`/`P_MovePlayer`/`P_PlayerThink` (`p_user.c`), `P_TryMove`/`P_CheckPosition`/`PIT_CheckLine`/`PIT_CheckThing`/`P_LineOpening`/`P_SlideMove`/`P_HitSlideLine` (`p_map.c`), `P_PointOnLineSide`/`P_BoxOnLineSide`/`P_BlockLinesIterator`/`P_BlockThingsIterator`/`P_PathTraverse`/`P_InterceptVector`/`P_AproxDistance` (`p_maputl.c`), `P_SetMobjState` (`p_mobj.c`), `info.c` tables (`states[]`, `sprnames[]`, `mobjinfo[]`)
- **Responsibility:** gameplay simulation — mobj lifecycle (spawn/state/think/remove), thinker list, map thing spawning with skill/MTF filtering, player acceleration/friction/gravity/view bobbing, collision (things + lines), wall sliding with the stairstep fallback, blockmap/BSP traversal for movement
- **Dependencies:** DOOM-020 (map loaders), DOOM-001 (fixed point), DOOM-002 (PRandom), DOOM-003 (angle tables), DOOM-030 (renderer bridge), ADR-004 (tracing)
- **Target project:** `DoomSharp.Game` (new) + `DoomSharp.Core` (InfoTables.g.cs)
- **Target files/types:** `InfoTables.g.cs` (generated: 138 sprites, 967 states, 137 mobj types, 74 action names), `Thinker.cs`, `Mobj.cs`, `MobjType.cs`, `MobjFlags.cs`, `Player.cs`, `TicCmd.cs`, `MapObject*.cs` (static partial: globals, spawn, movement, position, collision, slide, map utils), `PlayerThink.cs`, `GameTicker.cs`, `GameLevel.cs`, `Collision.cs`, `SlideMove.cs`
- **Semantic risks:** fixed-point overflow in `P_InterceptVector` (exact `>>8` pre-shifts preserved); `P_SlideMove` goto structure (restructured as a state loop, semantics identical); `MAPBLOCKSHIFT = FRACBITS+7` (fixed-point shift, not a plain 7); blockmap lump offsets are absolute indices after the 4-word header (`blockmap = blockmaplump + 4`); demo-deterministic `PRandom()` in `P_SpawnMobj` (not `MRandom`)
- **Test strategy:** info table characterization against the C harness dump (`tools/charact/info_vectors.json` via `infodump.c` over unmodified `info.c`); thinker add/run/lazy-remove; `P_LineOpening`; `P_TryMove` open-space/blocked; `P_SlideMove` wall slide + stairstep; `P_MobjThinker` gravity/friction; `P_PlayerThink` thrust/settle/turn; `P_SpawnMapThing` skill/MTF_MULTI filtering + unknown doomednum fatal; headless tic integration (spawn → think → state advance → movement)
- **Acceptance criteria:** player walks E1M1 with real Doom physics (acceleration, friction, gravity, step-climbing, wall sliding); spawned map things appear in the renderer via the `MobjVisual`/`SectorThings` bridge; all four user-approved C invariants hold (MTF flags 1/2/4/8/16, `P_InterceptVector` `>>8` pre-shifts, MAXMOVE split-stepping + `hitcount==3` stairstep, `PRandom()` not `MRandom()`)
- **Evidence/notes:** `p_tick.c`, `p_mobj.c`, `p_user.c`, `p_map.c`, `p_maputl.c`, `info.c`. **Documented deviations:** (1) `info.c` extracted mechanically via `tools/charact/infodump.c` (compiles unmodified `info.c`, dumps all 23 `mobjinfo_t` fields) + `tools/gen/extract_info.ts` → `InfoTables.g.cs`; action functions carried as a `StateAction` enum (dispatch is DOOM-021b). (2) Game-side state (sector thing lists, blocklinks, bmaporg/width/height, validcount) lives in `GameLevel` statics; `Line.ValidCount` added to the shared `Data.Line` (renderer + game share instances). (3) C pointer graph (`mobj_t*` target/tracer/player, sector_t.thinglist, blocklinks) → object references + int sector indices; subsector→sector derived from the first seg's linedef. (4) `P_SlideMove`'s `goto retry/stairstep` restructured as a state loop with identical semantics (hitcount==3 stairstep fallback preserved). (5) `P_CrossSpecialLine` is a structural no-op (line specials are DOOM-022). (6) Player weapons/powers/psprite deferred to DOOM-021b; `P_PlayerThink` covers the movement/height subset. (7) `MobjVisual` + `SectorThings` bridge (DOOM-030) carries render state; `SyncVisual()` per tic. (8) `DoomSharp.App` drives E1M1 walkaround via `TicCmd` + `GameTicker.Ticker` and renders from the live player mobj. Test evidence: 2351/2351 passing (21 new gameplay tests), 0 warnings.


### DOOM-030 — Renderer core (later phase)

- **Status:** Done (2026-11-27) — all slices DOOM-030a/b/c complete
- **Original files:** `r_main.c`, `r_bsp.c`, `r_segs.c`, `r_plane.c`, `r_things.c`, `r_draw.c`, `r_data.c`, `r_sky.c`, `r_plane.c`, `v_video.c`
- **Original symbols:** `R_RenderPlayerView` (`r_main.c:870`), `R_Init` (`r_main.c:773`), `R_PointInSubsector` (`r_main.c:801`), colormaps alignment hack (`r_data.c:641-643`), column/span drawers (`r_draw.c`)
- **Responsibility:** deterministic software renderer to 320×200 8-bit framebuffer
- **Dependencies:** DOOM-020, tables, WAD
- **Target project:** `DoomSharp.Rendering`
- **Semantic risks:** highest-risk module: pointer arithmetic, `ds_source`/`dc_colormap` hot loops, light tables, visplanes
- **Test strategy:** fixed camera + `-playdemo` frame dumps; exact pixel comparison (deterministic integer pipeline)
- **Acceptance criteria:** selected frames pixel-identical to reference build (risk R-01: requires Linux reference build)
- **Evidence/notes:** `r_main.c:868-894`

#### DOOM-030a — Renderer Data & Texture/Flat/Colormap Manager (`r_data.c`) — Done (2026-11-26)

- **Scope:** `R_InitData`/`R_InitTextures`/`R_InitFlats`/`R_InitSpriteLumps`/`R_InitColormaps`, `R_GenerateLookup`, `R_GenerateComposite`, `R_DrawColumnInCache`, `R_GetColumn`, `R_CheckTextureNumForName`/`R_TextureNumForName`/`R_FlatNumForName`, and the DOOM-020 name-resolution bridge. Resolves the deferred `R_TextureNumForName`/`R_FlatNumForName` mapping from DOOM-020.
- **Target:** new `DoomSharp.Rendering` project (`TextureManager`, `Texture`, `TexPatch`); `Sector.FloorFlat`/`CeilingFlat` and `Side.TopTextureIndex/BottomTextureIndex/MidTextureIndex` populated by `TextureManager.ResolveLevelTextures(level)` (Option a — raw 8-byte names retained).
- **Exact details preserved:** power-of-two `texturewidthmask` loop (`r_data.c:551-555`, width 48 → mask 31), `colofs = columnofs[x-x1]+3` post-header skip (`r_data.c:345`), multi-patch columns `collump = -1` + composite slots, `>64k` composite fatal (`r_data.c:365-369`), post traversal pixels at `+3` / advance `length+4` (`r_data.c:198-216`), `"-"` NoTexture → 0, case-insensitive 8-char `strncasecmp` semantics (query longer than 8 matches its 8-char prefix), `R_FlatNumForName` range check `[firstflat, lastflat]` (user-approved addition), `GetColumn` width-mask wrapping, `R_InitData` order Textures→Flats→Sprites→Colormaps.
- **Documented deviations:** (1) `GetColumn` returns `ReadOnlySpan<byte>` (zero-copy) instead of a raw pointer. (2) `CheckFlatNumForName` added (no C counterpart; non-fatal lookup). (3) COLORMAP 256-byte alignment omitted (C allocator detail). (4) "Column without a patch" → `Console.WriteLine` + empty composite (C prints then would UB on render; managed code logs and continues). (5) Composites are managed arrays retagged conceptually `PU_CACHE` (zone block tracking deferred to the zone-integration slice). (6) `FlatNumForName` additionally verifies the lump is within `[firstflat, lastflat]` (user-approved hardening).
- **Test evidence:** 2205/2205 passing (21 new), 0 warnings, Release `-warnaserror` clean. Synthetic fixtures: patch_t builder with per-column posts (topdelta/length/pad/pixels/pad/0xff), PNAMES, TEXTURE1 single- and multi-patch overlap composites, width-mask wrapping, `COLORMAP` 34×256, sprite headers, `ResolveLevelTextures` integration, optional `DOOMWADDIR` IWAD test, ADR-004 trace tests.

#### DOOM-030b — Renderer Geometry/Angle Math, 320×200 Framebuffer & Column/Span Drawers (`r_main.c`, `r_draw.c`, `v_video.c`) — Done (2026-11-26)

- **Scope:** `R_PointOnSide`/`R_PointOnSegSide`/`R_PointToAngle`/`R_PointToAngle2`/`R_PointToDist`/`R_ScaleFromGlobalAngle`/`R_PointInSubsector` (`r_main.c`), `R_SetupFrame`/`R_SetViewSize`/`R_ExecuteSetViewSize`/`R_InitBuffer`/`R_InitTextureMapping`/`R_InitLightTables`/`R_InitTranslationTables` (view setup LUTs: `xtoviewangle`, `viewangletox`, `clipangle`, `yslope`, `distscale`, `scalelight`, `zlight`, `ylookup`, `columnofs`), the full `r_draw.c` drawer set (`R_DrawColumn`, `R_DrawColumnLow`, `R_DrawFuzzColumn`, `R_DrawTranslatedColumn`, `R_DrawTranslatedColumnLow`, `R_DrawSpan`, `R_DrawSpanLow`, `R_InitTranslationTables`, `R_InitBuffer` LUT fill), and `v_video.c` (`V_Init`, `V_DrawPatch`, `V_DrawPatchFlipped`, `V_DrawPatchDirect`, `V_CopyRect`, `V_DrawBlock`, `V_GetBlock`, `V_MarkRect`, 5-row `gammatable`). **Deferred to DOOM-030c** (user-approved): `R_FillBackScreen`, `R_DrawViewBorder`, `R_VideoErase`, `R_InitPlanes`, `R_InitSkyMap` (need border patches/status-bar layout).
- **Target:** `DoomSharp.Rendering`: `RendererMath` (static, r_main.c math globals), `ViewSetup` (instance; view geometry + LUTs + static `ColormapsGlobal`), `Video` (static; `Screens[5]`), `ColumnDrawer`/`ColumnDrawState`, `SpanDrawer`/`SpanDrawState`, `Patch`/`PatchColumn` (patch_t parser). `AngleTables` gained `SlopeDiv` + `DBits` (mirrored in `tools/gen/run.ts`).
- **Exact details preserved:** octant-dispatch `PointToAngle` with `tantoangle`/`SlopeDiv` and `ANG90-1` fenceposts; `PointOnSide` axis fast paths with `<=` bias and sign-bit reject; `ScaleFromGlobalAngle` clamps 256..64·FRACUNIT with `den > num>>16` guard; `SlopeDiv` `den<512` → SLOPERANGE; fuzz `±320` offsets with border clamps (`yl==0→1`, `yh==viewheight-1→viewheight-2`), shared `fuzzpos` wrap at 50, colormap 6 darkening; `&127` wall columns vs unmasked translated columns; span `64×64` flat mask `((yfrac>>10)&0xFC0)|((xfrac>>16)&0x3F)`; `V_DrawPatch` post transparency (`+3` header skip, `length+4` advance); `V_DrawPatchFlipped` mirrors column order and fatals on out-of-range while `V_DrawPatch` warns+ignores (TNT.WAD behavior); `R_ExecuteSetViewSize` order InitBuffer→InitTextureMapping→psprite→screenheightarray→yslope→distscale→scalelight.
- **Documented deviations:** (1) `ylookup`/`columnofs` pointer LUTs → managed `int[]`/`byte[]` indexing (`view.YLookup[y] + view.ColumnOfs[x]`). (2) `angle_t` → `uint` with wraparound arithmetic. (3) `R_PointToAngle2` preserves the C side effect of overwriting `ViewX`/`ViewY` (explicit, documented). (4) `PointOnSegSide` takes raw vertex coordinates (C `seg->v1->x` deref → indices in C# data model). (5) `Screens[5]` are separate arrays, not one `I_AllocLow` block. (6) `dc_*`/`ds_*` globals → `ColumnDrawState`/`SpanDrawState` static classes; drawers take `ViewSetup` for `YLookup`/`ColumnOfs`/`CenterY`. (7) COLORMAP 256-byte alignment omitted. (8) C UB cases (FixedDiv overflow sentinel → `tantoangle` OOB; `finesine` OOB for visangle outside frustum) → `DoomFatalException` per ADR-002; vectors mark these `"fatal": true`. (9) `R_DrawColumn`/`R_DrawSpan` RANGECHECK → explicit `DoomFatalException` range guards.
- **Test evidence:** 2235/2235 passing, 0 warnings, Release `-warnaserror` clean. C characterization harness `tools/charact/rmaindump.c` compiles the **unmodified** `r_main.c`/`r_draw.c`/`v_video.c`/`tables.c`/`m_fixed.c`/`m_bbox.c` (gcc:13 Docker, `RANGECHECK` active) and dumps `rmain_vectors.json`: 77 `pointOnSide`, 23 `pointToAngle2` (+dist), 24 `scaleFromGlobalAngle` (incl. 1 fatal), 6 `drawColumn`, 2 `drawFuzzColumn` (fuzzpos continuity), 5 `drawSpan`, 1 `drawPatch` full-screen dumps. xUnit: `RendererMathTests` (vector-driven), `RasterizerTests` (pixel-exact column/fuzz/span), `VideoTests` (patch posts/flipped/copyrect/blocks/marks/gamma), `BspLookupTests` (synthetic 3-node BSP + single-subsector), `RendererTraceLoggingTests` (ADR-004: Verbose emission, silent-by-default, one event per rasterizer call). Renderer tests serialized via `RendererSerial` collection (process-global C state). Harness gotchas: `R_PointToDist(0,0)` and out-of-frustum `R_ScaleFromGlobalAngle` are original UB (skipped/fatal-marked); fuzz needs full 34×256 colormap (`colormap[6*256+px]` reads past a 256-byte stub).

#### DOOM-030c — Scene Traversal & Rendering (`r_bsp.c`, `r_segs.c`, `r_plane.c`, `r_things.c`, `r_sky.c`) — Done (2026-11-27)

- **Scope:** `R_AddLine`/`R_CheckBBox`/`R_Subsector`/`R_RenderBSPNode` (`r_bsp.c`), `R_StoreWallRange`/`R_RenderSegLoop`/`R_ClipToWall`/`R_RenderMaskedSegRange` (`r_segs.c`), `R_InitPlanes`/`R_FindPlane`/`R_CheckPlane`/`R_MakeSpans`/`R_DrawPlanes` (`r_plane.c`), `R_InitSkyMap`/`R_DrawSkyColumn` (`r_sky.c`), `R_InitSprites`/`R_AddSprites`/`R_ProjectSprite`/`R_DrawVisSprite`/`R_DrawSprite`/`R_DrawMasked`/`R_SortVisSprites` (`r_things.c`), `R_FillBackScreen`/`R_DrawViewBorder`/`R_VideoErase` (`v_video.c` consumers), and `R_RenderPlayerView` (`r_main.c:870`). **Deferred to DOOM-021+** (user-approved): `R_DrawPlayerSprites`/`R_DrawPSprite` (weapon psprite needs player state).
- **Target:** `DoomSharp.Rendering`: `BspRenderer` (solidsegs clip list, drawsegs), `SegRenderer` (wall ranges, masked segs, openings pool), `VisplaneManager` (visplane pool + openings), `SkyRenderer`, `SpriteRenderer`/`SpriteDefs`/`SectorThings`/`MobjVisual` (vissprite bridge), `SceneRenderer.RenderPlayerView` (pipeline), `Video.FillBackScreen`/`DrawViewBorder`/`VideoErase`, zero-copy `TextureManager.GetColumn(wad, tex, col, out offset)`.
- **Exact details preserved:** two-sided sky-hack (`frontsector.ceilingpic == skyflatnum && backsector.ceilingpic == skyflatnum` → `worldtop = worldhigh`, `r_segs.c:278-281`); unsigned 32-bit division `0xffffffffu / (uint)rw_scale` for `dc_iscale` (`r_segs.c:470`); `R_ScaleFromGlobalAngle` reads module-level `rw_normalangle`/`rw_distance` statics; `MAXOPENINGS = SCREENWIDTH*64` openings pool with `lastopening` cursor; drawsegs store `sprtopclip`/`sprbottomclip` as openings-pool offsets with sentinels `-2` (screenheightarray) / `-3` (negonearray); `R_CheckPlane` split-vs-extend decision (`in`-count parity, `r_plane.c:290-330`); `R_MakeSpans` left-to-right with x1/x2 swap; sky `&0x1FFF` angle mask, `ANG90` offset, `skymid = 100*FRACUNIT`; `R_SortVisSprites` selection sort by scale (descending) walking `vissprite_p-1..0`; sprite `gx - viewx`/`gy - viewy` projection with `+viewwidth/2` centering; `R_ProjectSprite` out-of-frustum reject (`transx > clipangle<<1`); fuzz shadow via `R_DrawFuzzColumn`; `R_DrawViewBorder` wraparound side copies (`ofs = top*320 + 320 - side`, 32-wide) and two `R_VideoErase` bands; `R_FillBackScreen` early-return when `scaledviewwidth == SCREENWIDTH`.
- **Documented deviations:** (1) C pointer graph (`curline`, `frontsector`, `backsector`, `seg->frontsector`) → int indices into `LevelData` (-1 = NULL). (2) `openings`/`lastopening` → `VisplaneManager.Openings` array + `LastOpening` cursor (shared by drawsegs and vissprites, as in C). (3) `sprtopclip`/`sprbottomclip` sentinels: C points at `screenheightarray`/`negonearray`; C# uses offsets `-2`/`-3` resolved by `ReadFloorClip`/`ReadCeilingClip`. (4) `R_SortVisSprites` → index sort over `VisSprites` list (no pointer surgery). (5) `R_SetupFrame` called as `SetupFrame(viewX, viewY, viewZ, viewAngle)` with `detailshift=0`, `extralight=0`, `fixedcolormap=null` (player state deferred to DOOM-021+). (6) `I_NetUpdate`/`NetUpdate` calls omitted (networking is DOOM-040). (7) `R_DrawPlayerSprites`/`R_DrawPSprite` deferred to DOOM-021+ (weapon psprite needs player state); `R_AddSprites` uses the `MobjVisual` + `SectorThings` bridge. (8) `R_RenderMaskedSegRange` `colormap` selection simplified to the `frontsector.lightlevel` path (C's `walllights` full logic preserved via `WallLights`). (9) Zero-copy `GetColumn(wad, tex, col, out offset)` overload added (user-approved refinement) — `RenderSegLoop` reads the cached patch/composite array directly, no per-column copies. (10) `SpriteRenderer.DrawSprite` reuses two instance `short[320]` scratch clip buffers (`_clipBot`/`_clipTop`) — allocation-free per sprite (user-approved refinement). (11) `PatchColumn` no longer reads the length byte past a 0xff terminator that ends the lump (C reads garbage; managed code bounds-checks).
- **Test evidence:** 2254/2254 passing (19 new), 0 warnings, Release `-warnaserror` clean. C characterization harness `tools/charact/rscenedump.c` compiles the **unmodified** `r_bsp.c`/`r_segs.c`/`r_plane.c`/`r_things.c`/`r_main.c`/`r_draw.c`/`v_video.c`/`tables.c`/`m_fixed.c`/`m_bbox.c` (gcc:13 Docker, per-op `setjmp` recovery via `I_Error` longjmp) and dumps `rscene_vectors.json`: 9 `clipSolid` ops (solidsegs/newend/drawsegs snapshots), 5 `planes` finds, `checkPlaneSplit`/`checkPlaneExtend`, 3 `storeWallRange` (scale1/scale2/scalestep/silhouette/bsil/tsil + floorclip/ceilingclip), 3 `projectSprite` (x1/x2/scale/startfrac/xiscale/texturemid). xUnit: `BspRendererTests`, `SegRendererTests`, `VisplaneTests`, `SpriteTests`, `SkyTests`, `ViewBorderTests`, `RenderPlayerViewTests` (pipeline + state-clearing + sprite projection), `SceneTraceLoggingTests` (ADR-004: Verbose at frame/BSP/drawseg/visplane/vissprite granularity, silent by default, never per-column/per-pixel). Optional `DOOMWADDIR` E1M1 render test deferred to the DOOM-040 integration slice. Port bugs found by the harness: `BspRenderer.AddLine` must propagate `CurLine` to `SegRenderer` (C shares one global); `StoreWallRange` must mirror `rw_normalangle`/`rw_distance` into the `RendererMath` statics read by `ScaleFromGlobalAngle`; `ScaleFromGlobalAngle` must keep `angle_t` arithmetic unsigned (angles ≥ 0x80000000 index `finesine` positively); `SpriteDefs` must pre-fill the sprite array (C pre-allocates the struct array).

### DOOM-040 — Platform: video/audio/input (replacement, not port)

- **Status:** In progress — **Slices DOOM-040a + DOOM-040b Done (2026-11-27)**; remaining slices (network, mouse/joystick input, full game loop) Proposed
- **Original files:** `i_video.c` (X11/XSHM), `i_sound.c` (sndserver pipe protocol, `i_sound.c:483-486,754`), `sndserv/*` (`/dev/dsp` OSS), `i_net.c` (UDP port 5029)
- **Responsibility:** window/presentation, audio output, input, network transport
- **Dependencies:** chosen multimedia binding (ADR-005: Silk.NET 2.23 — Accepted)
- **Target project:** `DoomSharp.Platform`, `DoomSharp.App`
- **Semantic risks:** original X11 path has quirks (pointer warp, blocky multiply mode, `__BIG_ENDIAN__` branches); sndserv protocol is text-based and simple to reimplement if needed
- **Test strategy:** manual + integration; audio verified by captured output buffers in tests where feasible
- **Acceptance criteria:** playable end-to-end on Windows + Linux; input mapping matches `xlatekey` (`i_video.c:97`)
- **Evidence/notes:** `i_video.c:29-90`, `i_sound.c:60-90`, `sndserv/linux.c:43-81`

#### DOOM-040a — Video Frame Presenter, PLAYPAL/Gamma Palette, Input Events, Window Host — Done (2026-11-27)

- **Scope:** `I_SetPalette`/`UploadNewPalette` (`i_video.c:530-585`), `xlatekey` (`i_video.c:97-162`), `I_GetEvent` key path (`i_video.c:194-260`), `event_t`/`evtype_t`/`MAXEVENTS` (`d_event.h:34-112`), `D_PostEvent`/`D_ProcessEvents` ring (`d_main.c:141-176`), `KEY_*` constants (`doomdef.h:250-280`), and the window/presentation host replacing `I_InitGraphics`/`I_FinishUpdate`. **Deferred:** mouse/joystick event translation (DOOM-040b), audio (`i_sound.c`/`sndserv`, DOOM-040b), network (`i_net.c`, DOOM-040c), full game responder (`D_ProcessEvents` responder chain, DOOM-021).
- **Target:** `DoomSharp.Platform`: `PaletteConverter` (PLAYPAL 14×768 + gamma → RGBA8888), `DoomEvent`/`EventType`/`DoomKeys`/`KeyTranslator`/`EventQueue`, `IFramePresenter` + `HeadlessFramePresenter` + `IInputSource` seams. `DoomSharp.App`: `SilkWindowHost` (Silk.NET GLFW backend, GL texture blit, 4:3 letterbox, integer scale ×1..×4) + `Program.cs` smoke-test loop (E1M1 + `RenderPlayerView` + arrow/WASD camera).
- **Exact details preserved:** gamma pass before channel expansion (`c = gammatable[usegamma][*palette++]`, `i_video.c:565-570`); `xlatekey` named-key table + printable range `space..asciitilde` → `' '..'~'` + A-Z lowercase fold + Delete→BACKSPACE + KP_Equal/KP_Subtract + both shifts/ctrls/alts+meta→RSHIFT/RCRTL/RALT (`i_video.c:97-162`); `KEY_*` constants exact (`doomdef.h:250-280`, `KEY_LALT=KEY_RALT`); `event_t` shape (data1 keys/buttons, data2/3 mouse x/y); `MAXEVENTS=64` ring with `&(MAXEVENTS-1)` wrap and the C's no-overflow-check lap behavior (head laps tail loses unread events; full lap reads as empty — `d_main.c:150-154,170`); PLAYPAL 14×768 layout (palette 0 normal, 1-8 pain reds, 9-12 golds, 13 radiation green).
- **Documented deviations:** (1) X11/Shm presentation replaced by Silk.NET GLFW window + GL texture blit (replacement, not port — ADR-005). (2) `xlatekey` switches on a platform-neutral `PlatformKey` enum (named keys at 0x100+, printable range = ASCII) instead of X11 keysyms; the window adapter maps Silk.NET `Key` → `PlatformKey`. (3) The C event ring's head-laps-tail overwrite is preserved exactly (no managed "fix" — documented UB-compatible behavior). (4) `I_SetPalette` becomes `PaletteConverter.Convert` (indexed→RGBA per frame) instead of an X colormap upload; the palette slice + gamma row are explicit parameters. (5) Mouse/joystick events deferred to DOOM-040b. (6) `SilkWindowHost` buffers key callbacks between frames (Silk callback model) and drains them in `Poll` — the C polled X synchronously in `I_GetEvent`. (7) `DoomSharp.App` is the only project referencing Silk.NET; the test project stays pure managed (headless CI guarantee, ADR-005).
- **Test evidence:** 2296/2296 passing (42 new), 0 warnings, Release `-warnaserror` clean. Headless suites: `PaletteConverterTests` (gamma-then-expand order, slice selection, range guards), `KeyTranslatorTests` (all named keys, printable fold, Delete→BACKSPACE, KP aliases, constant values vs `doomdef.h`), `EventQueueTests` (FIFO, lap-overwrite, full-lap-empty, MAXEVENTS=64), `HeadlessFramePresenterTests` (frame copy semantics), `PlatformTraceLoggingTests` (ADR-004: palette blit and event queue are silent at all levels). Interactive smoke test: `dotnet run --project src/DoomSharp.App -- -iwad <doom1.wad>` (arrow/WASD camera, ESC quit) — manual verification, not CI.

#### DOOM-040b — Platform Audio Mixer & Sound/Music Subsystem — Done (2026-11-27)

- **Scope:** `getsfx`/`addsfx`/`I_UpdateSound`/`I_SetChannels` (`i_sound.c:185-635`), `I_StartSound`/`I_SoundIsPlaying` (`i_sound.c:460-521`), `S_sfx`/`S_music` tables (`sounds.c`), `S_Init`/`S_StartSound`/`S_StartSoundAtVolume`/`S_StopSound`/`S_UpdateSounds`/`S_AdjustSoundParams`/`S_getChannel`/`S_StopChannel` (`s_sound.c`), and the audio output seam replacing `/dev/dsp` writes. **Deferred:** MUS/OPL2 music synthesis (linuxdoom's `I_*Song` API is dummies — faithful stub), network (`i_net.c`, DOOM-040c), mouse/joystick input (DOOM-040c).
- **Target:** `DoomSharp.Platform`: `DmxSound` (format-3 parser + 512-sample 0x80 padding), `SoundMixer` (8-channel, `SAMPLECOUNT=512`, 11,025 Hz stereo 16-bit; `vol_lookup`/`steptable` exact), `SoundTables`/`SfxInfo`/`MusicInfo`/`SfxId`/`MusicId` (107 SFX + 67 music entries, `chgun`→`pistol` link at pitch 150), `SoundManager` (s_sound.c port), `IAudioOutput` + `HeadlessAudioOutput`. `DoomSharp.App`: `OpenAlAudioOutput` (Silk.NET.OpenAL streaming source, graceful headless fallback) + SFX pre-cache + SPACE-fires-pistol demo trigger.
- **Exact details preserved:** DMX format-3 header + 512-sample 0x80 padding (`getsfx`, `i_sound.c:185-251`); `vol_lookup[i*256+j] = (i*(j-128)*256)/127` and `steptable[i] = pow(2, i/64)*65536` (`I_SetChannels`, `i_sound.c:396-424`); `addsfx` chainsaw/pistol/stnmov singularity kill, oldest-`channelstart` eviction, `handlenums` starting at 100 (unsigned short — wraps at 65536, not 100; the C comment "limited to 0..100" is misleading); x² stereo falloff with the C's `seperation+1`/`-257` asymmetry (NORM_SEP=128 gives leftvol=95/rightvol=96 — *not* centered); `I_UpdateSound` per-sample loop with 16-bit fractional stepping and ±32767/−32768 clamping; `S_AdjustSoundParams` GG1 approx distance `adx+ady-(min>>1)`, `S_CLIPPING_DIST=1200<<16`, `S_CLOSE_DIST=160<<16`, `S_STEREO_SWING=96<<16` with the **double-shift** sep formula `128 - (FixedMul(S_STEREO_SWING, finesine[angle]) >> FRACBITS)`, and the `gamemap==8` min-volume-15 special case; `S_getChannel` priority eviction; `I_SoundIsPlaying` = `gametic < handle` quirk (s_sound bookkeeping frees after tic passes handle while the mixer drains by data end); `I_StopSound` is a no-op (s_sound stops free only bookkeeping slots).
- **Documented deviations:** (1) `/dev/dsp` OSS writes replaced by `IAudioOutput` seam; live output is Silk.NET.OpenAL (ADR-006) with `HeadlessAudioOutput` fallback — no sound card required for CI. (2) `mobj_t*` origins become `SoundOrigin` records (mobjs are DOOM-021); the listener is injected per call. (3) `R_PointToAngle2`/`finesine` injected as delegates to keep `DoomSharp.Platform` decoupled from `DoomSharp.Rendering` (App wires the real ones; tests may stub). (4) Music is a faithful stub matching linuxdoom's dummy `I_*Song` API; MUS/OPL2 synthesis out of scope. (5) `sndserv` pipe protocol not ported (in-process output instead). (6) `I_UpdateSoundParams` was already a no-op in the C (Linux build) — per-tic param refresh is bookkeeping-only, preserved as such.
- **Test evidence:** 2330/2330 passing (34 new), 0 warnings, Release `-warnaserror` clean, verified 3× on Windows and 3× in the `dotnet/sdk:10.0` Linux container (CI environment). Suites: `DmxSoundTests` (header parse, 512-sample padding, truncation/format/count guards), `SoundMixerTests` (`StepTable`/`VolLookup` formulas, handle sequence from 100, singularity kill, oldest-channel eviction, exact mixed amplitudes incl. the NORM_SEP 95/96 asymmetry, clamp to ±32767/−32768, drain-at-data-end, `gametic < handle` quirk), `SoundManagerTests` (attenuation vectors incl. gamemap-8 special case, priority eviction, bookkeeping-vs-mixer channel independence), `AudioTraceLoggingTests` (ADR-004: mixing loop silent at all levels; `HeadlessAudioOutput` capture/copy semantics). Trace-test classes moved into the `RendererSerial` collection (global-logger swap race, same class of bug as the DOOM-040a CI fix).

## Deferred / out of scope (initial)

| Item | Reason |
|---|---|
| `Legacy/ipx/` (IPX driver), `Legacy/sersrc/` (serial driver) | DOS-only, not referenced by the Linux build (`ipx/README`, `sersrc/README.TXT`); modern multiplayer would use a different transport anyway |
| `sndserv/` as a separate process | Replaced by in-process .NET audio output; the pipe protocol (`i_sound.c:485`) is documented for reference |
| X11 SHM/DGA video code | Replaced by presentation layer (`IframePresenter`); algorithms stay in renderer |
| `-statcopy` external statistics driver | Obscure hook (`d_main.c:1128-1135`); defer unless needed |
| French localization build (`-DFRENCH`, `dstrings.h:34`) | Single-language target initially; data-driven strings make this trivial later |

---

# First migration iteration — proposal (awaiting approval)

## Scope and objectives

Migrate the three pure, deterministic, platform-free leaf modules and stand up the .NET 10 solution + test harness:

1. Fixed-point arithmetic (`m_fixed.c`)
2. Random LUT (`m_random.c`)
3. Trig/angle lookup tables (`tables.c` data + accessors)

Objectives: establish the solution skeleton (`DoomSharp.sln`, nullable enabled, xUnit), prove the characterization-test workflow end to end, and de-risk the numeric-fidelity question (R-02) before any engine logic is ported.

## Original source files and functions involved

- `Legacy/linuxdoom-1.10/m_fixed.h`, `m_fixed.c` — `FixedMul`, `FixedDiv`, `FixedDiv2`
- `Legacy/linuxdoom-1.10/m_random.h`, `m_random.c` — `rndtable`, `P_Random`, `M_Random`, `M_ClearRandom`
- `Legacy/linuxdoom-1.10/tables.h`, `tables.c` — `finetangent`, `finesine`/`finecosine`, `tantoangle`, `SlopeDiv` (verify exact accessor list during implementation)
- Build reference: `Makefile:9` flags (`-DNORMALUNIX -DLINUX`, no `USEASM` — C paths are authoritative)

## Proposed C# implementation

- `DoomSharp.sln` with `src/DoomSharp.Core` (net10.0, nullable enable) and `tests/DoomSharp.Tests.Unit` (xUnit). No other projects yet (per `ARCHITECTURE.md` §3: start small).
- `FixedPoint` static class: `int`-based `Mul`, `Div`, `Div2`; `unchecked` arithmetic; `Div2` reproduces the `double` intermediate and truncating cast of `m_fixed.c:80-86`; overflow sentinel behavior of `Div` preserved (returns `int.MinValue`/`int.MaxValue` instead of calling `I_Error` — error surfaced via callback/exception at a higher layer; documented deviation).
- `DoomRandom` class: 256-byte table (embedded resource or const array), two index properties (`P`, `M`), `Reset()`.
- `AngleTables` static class: `short`-free, `int[]` tables generated from `tables.c` by a one-off script; `FineCosine` implemented as an offset view over `FineSine` (mirroring `tables.h:61`).
- No `unsafe`, no floating point except the documented `Div2` intermediate.

## Dependencies and potential risks

- Depends on nothing outside BCL. `I_Error` dependency in `m_fixed.c` is the only coupling — resolved via the sentinel/exception decision above (needs sign-off; it changes observable behavior on overflow from "abort" to "return sentinel" — but the C `FixedDiv` already returns sentinels for the common overflow case, and `FixedDiv2`'s `I_Error` only triggers on values that also overflow the double path).
- Risks: R-02 (double-precision division fidelity — mitigated by exhaustive boundary vectors); R-03 (license headers — GPL-2.0 notice to be added to solution root); tables.c extraction must be mechanical (script), not hand-copied.

## Required tests and acceptance criteria

- Characterization vectors generated by compiling `m_fixed.c`/`m_random.c`/`tables.c` unmodified with a small `main()` harness (gcc, WSL or CI Linux) — inputs: boundary values (0, ±1, MININT, MAXINT, threshold cases of `FixedDiv`), random fuzz (≥100k pairs), full `rndtable` walk.
- Acceptance:
  - `dotnet build` clean, zero warnings (CONSTITUTION §5).
  - 100% of characterization vectors pass bit-exactly.
  - `FixedDiv` overflow returns `MININT`/`MAXINT` matching C for all tested inputs.
  - Tables match C arrays element-for-element (4096 + 10240 + 2049 entries).
  - ADR-001 (fixed-point representation) and ADR-002 (project layout) drafted and approved.

## Explicitly out of scope for this iteration

Everything else — no game logic, no WAD, no rendering, no platform code. `Legacy/` is not modified.

---

**Status: awaiting human review and approval before any implementation begins** (per `AGENT_WORKFLOW.md` §2 and `MIGRATION_PLAN.md` Phase 0 exit criteria).

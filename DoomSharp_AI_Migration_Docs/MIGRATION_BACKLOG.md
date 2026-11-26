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

- **Status:** Proposed
- **Original files:** `p_setup.c`, `doomdata.h`
- **Original symbols:** `P_LoadVertexes` (`p_setup.c:122`), `P_LoadSegs` (`p_setup.c:159`), `P_LoadSubsectors` (`p_setup.c:202`), `P_LoadSectors` (`p_setup.c:231`), `P_LoadNodes` (`p_setup.c:264`), `P_LoadThings` (`p_setup.c:301`), `P_LoadLineDefs` (`p_setup.c:357`), `P_LoadSideDefs` (`p_setup.c:436`), `P_SetupLevel` (`p_setup.c:584`), `P_LoadBlockMap`, `P_LoadReject`
- **Responsibility:** parse all map lumps into runtime structures (`vertex_t`, `sector_t`, `line_t`, `node_t`, `subsector_t` in `r_defs.h`)
- **Dependencies:** DOOM-004 (WAD), DOOM-001 (fixed_t), z_zone
- **Target project:** `DoomSharp.Data` (parsing) + `DoomSharp.Core` (runtime types)
- **Target files/types:** `MapLoader.cs`, record types for `Vertex`, `LineDef`, `SideDef`, `Sector`, `Seg`, `Subsector`, `Node`
- **Semantic risks:** `short` sign handling (e.g. `NF_SUBSECTOR 0x8000` vs `unsigned short children`, `doomdata.h:178-191`); little-endian reads; `RANGECHECK` aborts on bad data (`p_setup.c` uses `I_Error`)
- **Test strategy:** parse E1M1 from shareware IWAD; assert counts and sampled field values against C-harness dump; malformed-lump negative tests
- **Acceptance criteria:** all map structures match reference dump; malformed input fails with context, not crash
- **Evidence/notes:** `p_setup.c:120-580`, `doomdata.h:60-210`

### DOOM-030 — Renderer core (later phase)

- **Status:** Proposed (blocked by DOOM-020 map state)
- **Original files:** `r_main.c`, `r_bsp.c`, `r_segs.c`, `r_plane.c`, `r_things.c`, `r_draw.c`, `r_data.c`, `r_sky.c`, `r_plane.c`, `v_video.c`
- **Original symbols:** `R_RenderPlayerView` (`r_main.c:870`), `R_Init` (`r_main.c:773`), `R_PointInSubsector` (`r_main.c:801`), colormaps alignment hack (`r_data.c:641-643`), column/span drawers (`r_draw.c`)
- **Responsibility:** deterministic software renderer to 320×200 8-bit framebuffer
- **Dependencies:** DOOM-020, tables, WAD
- **Target project:** `DoomSharp.Rendering`
- **Semantic risks:** highest-risk module: pointer arithmetic, `ds_source`/`dc_colormap` hot loops, light tables, visplanes
- **Test strategy:** fixed camera + `-playdemo` frame dumps; exact pixel comparison (deterministic integer pipeline)
- **Acceptance criteria:** selected frames pixel-identical to reference build (risk R-01: requires Linux reference build)
- **Evidence/notes:** `r_main.c:868-894`

### DOOM-040 — Platform: video/audio/input (replacement, not port)

- **Status:** Proposed
- **Original files:** `i_video.c` (X11/XSHM), `i_sound.c` (sndserver pipe protocol, `i_sound.c:483-486,754`), `sndserv/*` (`/dev/dsp` OSS), `i_net.c` (UDP port 5029)
- **Responsibility:** window/presentation, audio output, input, network transport
- **Dependencies:** chosen multimedia binding (SDL2# or similar — ADR required per `ARCHITECTURE.md` §4)
- **Target project:** `DoomSharp.Platform`, `DoomSharp.Audio`
- **Semantic risks:** original X11 path has quirks (pointer warp, blocky multiply mode, `__BIG_ENDIAN__` branches); sndserv protocol is text-based and simple to reimplement if needed
- **Test strategy:** manual + integration; audio verified by captured output buffers in tests where feasible
- **Acceptance criteria:** playable end-to-end on Windows + Linux; input mapping matches `xlatekey` (`i_video.c:97`)
- **Evidence/notes:** `i_video.c:29-90`, `i_sound.c:60-90`, `sndserv/linux.c:43-81`

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

# Source Inventory — DOOM (Legacy)

> Completed by the agent after inspecting `C:\projects\DOOM\Legacy` (Phase 0 reconnaissance, no code written).
> Every factual claim below cites a file path and, where possible, a symbol. Items marked **[Hypothesis]** or **[Unverified]** are not established facts.

## Repository identity

| Field | Value | Evidence |
|---|---|---|
| Repository | `https://github.com/id-Software/DOOM.git` (git remote of this checkout) | git status header; `Legacy/README.TXT` signed "John Carmack 12-23-97" |
| Source version/release | **Linux Doom v1.10** source release (the December 1997 id Software GitHub drop) | Directory name `Legacy/linuxdoom-1.10/`; `doomdef.h:33` `enum { VERSION = 110 };`; `Legacy/README.TXT` (Carmack, 12-23-97) |
| Commit/hash | `a77dfb9` (current checkout, branch `master`) | git status context |
| License | **GNU General Public License v2** — Copyright (c) ZeniMax Media Inc. | `Legacy/LICENSE.TXT` (full GPL-2.0 text, 341 lines); `Legacy/README.TXT:80-81` |
| Original platform | Linux (X11), 32-bit x86; "this code only compiles and runs on linux" | `Legacy/README.TXT:12-14`; `Makefile:9-11` (`-DNORMALUNIX -DLINUX`, `-lXext -lX11 -lnsl -lm`) |
| Compiler/toolchain | `gcc`, C89-era C with some GCC extensions (`__GNUG__` pragmas) | `Makefile:7-9`; `#ifdef __GNUG__` in `m_fixed.h:27`, `w_wad.c:46`, `i_system.c:43` |
| Build status | **Not built in this environment** (Windows host; source targets Linux/X11). Not attempted — see Risks R-01 | `Makefile` targets `linux/linuxxdoom`; X11/SHM headers in `i_video.c:29-36` |
| Runtime requirements | X11 display (MIT-SHM or DGA), `/dev/dsp`-capable sound via external `sndserver` process, `$HOME` env var, `DOOMWADDIR` env var | `i_video.c:29-36`; `i_sound.c:64-65,754` (`popen("./sndserver ")`); `d_main.c:583-585` (`getenv("DOOMWADDIR")`, `I_Error("Please set $HOME...")`) |
| Data files required to run | A commercial DOOM IWAD: `doom1.wad` / `doom.wad` / `doomu.wad` / `doom2.wad` / `doom2f.wad` / `plutonia.wad` / `tnt.wad` (not included in repo) | `d_main.c:563-640` `IdentifyVersion()`; `Legacy/README.TXT:3` "You still need real DOOM data" |

**License notes (verified):**
- The engine source is GPL-2.0 (`Legacy/LICENSE.TXT`). The DoomSharp C# port is therefore a derivative work and must be distributed under GPL-2.0 with preserved notices.
- Per-file headers still say "DOOM Source Code License" (e.g. `doomtype.h:9`), but the top-level `LICENSE.TXT` and `README.TXT:80-81` state GPL-2.0. The GPL file is the operative license for this checkout. **[Unresolved]**: the header/license mismatch is a known quirk of the 1997 release; treat GPL-2.0 as authoritative (it is the license text actually shipped).
- Game data (IWAD) licensing is separate and commercial; do not commit IWADs (CONSTITUTION §6).

## Top-level structure

| Path | Purpose | Relevant files/symbols | Confidence |
|---|---|---|---|
| `Legacy/linuxdoom-1.10/` | The DOOM engine itself: 62 `.c` + 62 `.h` files, ~54,640 lines total (`wc -l *.c *.h`) | `i_main.c:main()` → `D_DoomMain()` | Verified |
| `Legacy/sndserv/` | Separate sound server process (`sndserver`), talks to the engine over a pipe | `Makefile:16` target `linux/sndserver`; `soundsrv.c`, `linux.c` (`/dev/dsp` via `ioctl`, lines 43-81); `README.sndserv` | Verified |
| `Legacy/ipx/` | DOS IPX network driver (`ipxsetup`, `doomnet`) — DOS-only, not part of the Linux build | `ipx/README`; `IPXSETUP.C` | Verified |
| `Legacy/sersrc/` | DOS serial/modem network driver (`sersetup`) — DOS-only | `sersrc/README.TXT` "source for the DOOM serial / modem driver" | Verified |
| `Legacy/linuxdoom-1.10/CVS/` | Leftover CVS metadata (`Entries`, `Repository`, `Root`) | — | Verified |
| `Legacy/linuxdoom-1.10/FILES`, `FILES2` | Historical file listings/annotations from the original tree (FILES lists files **not present** in this checkout, e.g. `i_x.c`, `i_ibm.c`, `fpfunc.S`, `sndserver.c`) | `FILES` vs actual directory | Verified |
| `Legacy/linuxdoom-1.10/ChangeLog` | Bernd Kreimeier's cleanup log, Dec 1997 (e.g. SNDSERV made default, `doomu.wad` rename, TNT patch hack) | `ChangeLog:6-59` | Verified |

Note: `FILES`/`FILES2` describe an earlier/parallel tree and do **not** match the actual checkout (e.g. `i_x.c` listed in FILES but the shipped file is `i_video.c` with rcsid `$Id: i_x.c...`). Use the actual directory listing, not FILES.

## Module inventory

The Makefile object list (`Makefile:17-78`) is the authoritative module list for the engine binary. Grouped by subsystem:

### Core / shared
| Module | Files | Responsibilities | Dependencies | Migration risk |
|---|---|---|---|---|
| Types & constants | `doomtype.h`, `doomdef.h`, `doomstat.h/c`, `d_event.h`, `d_ticcmd.h`, `d_think.h`, `d_player.h` | `byte`, `boolean` enum, `fixed_t` (int), `angle_t` (unsigned), `GameMode_t`, key constants, `TICRATE=35` (`doomdef.h:122`), global game state | everything | Low-Med (enum/typedef mapping) |
| Fixed-point math | `m_fixed.h/c` | `FixedMul` (64-bit shift, `m_fixed.c:48`), `FixedDiv` (overflow guard, `m_fixed.c:62-64`), `FixedDiv2` (**uses `double`**, `m_fixed.c:80-86`) | tables, i_system (`I_Error`) | **High** — float-based division must be reproduced bit-exactly |
| Lookup tables | `tables.h/c` (2130 lines) | `finetangent[4096]`, `finesine[10240]`, `tantoangle[2049]`, BAM constants `ANG45=0x20000000` etc. (`tables.h:50-78`) | m_fixed | Low (data) |
| Random | `m_random.h/c` | 256-entry `rndtable` LUT; `P_Random`/`M_Random` index counters (`m_random.c:31-72`) | — | Low |
| Zone allocator | `z_zone.h/c` | Tagged doubly-linked block allocator, `ZONEID 0x1d4a11` (`z_zone.c:43`), `memblock_t` (`z_zone.h:58-66`), 6 MB heap (`i_system.c:51,71-80`) | i_system | Med — replace with GC/managed but preserve purge-tag semantics for cache behavior |
| WAD I/O | `w_wad.h/c` | `wadinfo_t`/`filelump_t` (`w_wad.h:35-53`), `W_AddFile`, `W_CheckNumForName` (name packed into 2 ints, `w_wad.c:351-390`), `W_CacheLumpNum`, lump cache | z_zone, m_swap, i_system | Med — packed-int name compare + endian |
| Swap | `m_swap.h/c` | Big-endian conversion macros (WAD format is little-endian; x86 native) | — | Low |

### Game (simulation)
| Module | Files | Responsibilities | Dependencies | Migration risk |
|---|---|---|---|---|
| Main/game flow | `d_main.h/c`, `d_net.h/c`, `d_items.h/c`, `dstrings.h/c`, `d_englsh.h`, `d_french.h` | `D_DoomMain()` (`d_main.c:796`), `D_DoomLoop()` (`d_main.c:354`), `IdentifyVersion()` (`d_main.c:563`), IWAD detection, demo loop, netgame sync (`TryRunTics`, `d_net.c:636`) | nearly all | High (global state) |
| Game logic | `g_game.h/c` | `G_Ticker` (`g_game.c:605`), `G_BuildTiccmd` (`g_game.c:237`), savegames (`VERSIONSIZE 16`, `g_game.c:1198`), demos (`*demo_p++ = VERSION`, `g_game.c:1555`) | p_*, hu, st, am, d_main | High |
| Map data | `doomdata.h` | On-disk map structs: all `short` fields (`doomdata.h:62-210`), `mapnode_t.children` unsigned short with `NF_SUBSECTOR 0x8000` (`doomdata.h:178-191`) | — | Low |
| Play/simulation | `p_local.h`, `p_tick.c` (thinker list `thinkercap`, `p_tick.c:47`), `p_mobj.c` (`P_XYMovement:114`, `P_ZMovement:246`, `P_MobjThinker:415`), `p_user.c`, `p_enemy.c` (`A_Look:604`, `A_Chase:672`), `p_map.c` (`P_CheckPosition:375`, `P_TryMove:451`, `P_UseLines:1130`, `P_RadiusAttack:1206`), `p_maputl.c` (`P_BlockLinesIterator:472`, `P_BlockThingsIterator:513`, `P_PathTraverse:743`), `p_sight.c` (`P_CheckSight:300`), `p_inter.c`, `p_pspr.c`, `p_setup.c` (`P_LoadVertexes:122` … `P_SetupLevel:584`), `p_saveg.c`, `p_spec.c/h`, `p_doors.c`, `p_ceilng.c`, `p_floor.c`, `p_plats.c`, `p_lights.c`, `p_switch.c`, `p_telept.c`, `p_enemy.c` | Movement, collision, AI, line-of-sight, specials (doors/plats/lights/switches), savegames | z_zone, w_wad, info, sounds, r_data (circular with renderer) | **High** — function-pointer thinkers, global coupling |
| State tables | `info.h/c` (4670 lines) | `state_t states[NUMSTATES]`, `mobjinfo_t mobjinfo[NUMMOBJTYPES]` (`info.h:1158,1333`); `state_t.action` is a **function pointer union** (`d_think.h:40-52`) | doomdata | Med — action function pointers → delegate/switch |
| Sound orchestration | `s_sound.h/c`, `sounds.h/c` | `S_sfx[]`/`S_music[]` tables, `S_StartSoundAtVolume` (`s_sound.c:255`), positional sound | i_sound, w_wad, z_zone | Med |
| Status bar / HUD / automap / intermission / finale / wipe | `st_lib.c`, `st_stuff.c`, `hu_lib.c`, `hu_stuff.c`, `am_map.c`, `wi_stuff.c`, `f_finale.c`, `f_wipe.c` | UI drawing & state machines | v_video, d_event | Med |
| Menus | `m_menu.c`, `m_argv.c`, `m_bbox.c`, `m_cheat.c`, `m_misc.c` (defaults file `.doomrc`, `M_LoadDefaults` `m_misc.c:340`) | Menu, cmdline, bbox, cheats, config I/O | v_video, g_game | Med |

### Renderer
| Module | Files | Responsibilities | Dependencies | Migration risk |
|---|---|---|---|---|
| Refresh core | `r_main.c` (`R_RenderPlayerView:870`, `R_PointOnSide:162`, `R_PointToAngle:292`, `R_PointInSubsector:801`), `r_bsp.c`, `r_segs.c`, `r_plane.c`, `r_sky.c`, `r_things.c`, `r_draw.c` (span/column drawers, `MAXWIDTH 1120/MAXHEIGHT 832`, `r_draw.c:47-48`), `r_data.c` (textures, `colormaps` alignment hack `r_data.c:641-643`), `r_sky.c`, `r_defs.h`, `r_local.h`, `r_state.h` | BSP-walk software renderer, 320×200 8-bit paletted framebuffer (`SCREENWIDTH/HEIGHT`, `doomdef.h:110-112`) | w_wad, z_zone, tables | **High** — pointer arithmetic, fixed-point, light tables |
| Video util | `v_video.c` | `V_DrawPatch` (with the TNT "exceeds LFB" abort→return hack, ChangeLog:34-41), palette ops | i_video | Med |

### Platform interface (I_*)
| Module | Files | Responsibilities | Dependencies | Migration risk |
|---|---|---|---|---|
| System | `i_system.h/c` | `I_GetTime` (gettimeofday → 1/70s tics, `i_system.c:88-100`), `I_Error` (`i_system.c:162-183`), `I_Quit`, `I_ZoneBase` (6 MB malloc), `I_WaitVBL` | libc | High — replace with .NET abstractions |
| Video | `i_video.h/c` (1051 lines) | X11 + MIT-SHM window, 8-bit pseudocolor palette, keyboard/mouse via XEvent (`xlatekey`, `i_video.c:97`) | X11, XShm | High — replace with SDL/modern binding |
| Sound | `i_sound.h/c` | Spawns `./sndserver` via `popen` (`i_sound.c:738-754`), text protocol `p%2.2x%2.2x%2.2x%2.2x\n` (`i_sound.c:485`) | sndserv binary | High — replace with managed audio |
| Network | `i_net.h/c` | UDP sockets, port `IPPORT_USERRESERVED+0x1d` = 5029 (`i_net.c:76`), local `ntohl` macro redefinition (`i_net.c:55-66`) | BSD sockets | High — defer multiplayer |
| Entry | `i_main.c` | `main()` → sets `myargc/myargv`, calls `D_DoomMain()` (`i_main.c:34-45`) | d_main | Low |

### Auxiliary tools (out of engine scope)
| Path | Purpose | Notes |
|---|---|---|
| `sndserv/` | Sound server: reads WAD sounds, mixes, writes `/dev/dsp` (`linux.c:79`) | Linux-only; responsibility replaced by .NET audio output |
| `ipx/`, `sersrc/` | DOS-only multiplayer drivers | Not referenced by the Linux build; defer/ignore |

## Build and platform assumptions

- **Build scripts:** `Legacy/linuxdoom-1.10/Makefile` (engine → `linux/linuxxdoom`), `Legacy/sndserv/Makefile` (→ `linux/sndserver`).
- **Compiler flags:** `CFLAGS=-g -Wall -DNORMALUNIX -DLINUX # -DUSEASM` (`Makefile:9`). `USEASM` is **disabled** — the C versions of all functions are compiled; no `.S` assembly is in this tree (the `fpfunc.S`/`tmap.S` listed in `FILES` are absent). Verified by grep: no `USEASM` references in any `.c/.h`.
- **Libraries:** `-lXext -lX11 -lnsl -lm` (`Makefile:11`). No other external libraries. `sndserv` needs only `-lm` and OSS `/dev/dsp`.
- **Conditional compilation (complete list, verified by grep):** `NORMALUNIX`, `LINUX`, `SNDSERV` (defined in `doomdef.h:84`), `SNDINTR` (off), `RANGECHECK` (defined, `doomdef.h:75`), `FRENCH` (strings, `dstrings.h:34`), `__BIG_ENDIAN__` guards in `i_video.c:403,448`, `#if 0`/`#if 1` dead code in `m_fixed.c:74`, `g_game.c:537`, `d_net.c:529`. No assembly, no DOS code paths in the Linux build.
- **Platform APIs:** X11/Xlib/XSHM (`i_video.c:32-36`), BSD sockets (`i_net.c:30-36`), `gettimeofday` (`i_system.c:95`), `popen` (`i_sound.c:754`), Linux OSS `linux/soundcard.h` (`i_sound.c:45`, `sndserv/linux.c:43`), `sys/shm.h`, `alloca.h` (`w_wad.c:37`).
- **Endianness/packing:** x86 little-endian assumed; `m_swap.h` provides long/short swap macros used by WAD code; `i_video.c` has `__BIG_ENDIAN__` branches for the X11 image path. Structs are read field-by-field from lumps (e.g. `P_LoadVertexes`, `p_setup.c:122`), not by raw struct casting — good for porting.
- **Build status:** not attempted on this Windows host; requires Linux + X11 headers + gcc. **[Unverified]** whether it still compiles with modern gcc (known issues: `errnos.h` include in `i_video.c:49` is nonstandard; `-Wall` warnings expected).

## Numeric and data representation

- **Fixed point:** 32-bit signed, 16.16 — `FRACBITS 16`, `FRACUNIT 1<<16`, `typedef int fixed_t` (`m_fixed.h:35-38`).
  - `FixedMul`: `((long long)a * b) >> 16` (`m_fixed.c:48`) — arithmetic shift of signed 64-bit product.
  - `FixedDiv`: overflow guard `(abs(a)>>14) >= abs(b)` → returns `MININT`/`MAXINT` by sign (`m_fixed.c:62-64`).
  - `FixedDiv2`: **computed in `double` then cast to int** (`m_fixed.c:80-86`) — C cast truncates toward zero; C# `(int)` cast also truncates toward zero, but the double arithmetic must be reproduced exactly (it is, for these magnitudes, under IEEE 754). Overflow → `I_Error("FixedDiv: divide by zero")`.
- **Angles:** `typedef unsigned angle_t` (`tables.h:78`); Binary Angle Measure, full turn = 2^32. `ANG90 = 0x40000000` (`tables.h:69`). `ANGLETOFINESHIFT 19` maps BAM → LUT index (`tables.h:55`).
- **Integer widths:** `byte` = unsigned char; `boolean` = enum; most counters `int`; map on-disk fields `short`/`unsigned short` (`doomdata.h`). `long` is assumed 32-bit (`MINLONG 0x80000000`, `doomtype.h:55`) — LP64/IL32 assumption; C# `int` maps cleanly, `long` must **not** be used where C `long` appears.
- **Overflow/wraparound:** relied upon in places (e.g. angle arithmetic wraps modulo 2^32 by unsigned overflow; `P_Random` masks `&0xff`, `m_random.c:59`). C# needs `unchecked` contexts where wraparound is semantic.
- **Struct layout:** runtime structs contain pointers (e.g. `sector_t.thinglist`, `z_zone.h` `memblock_t`) — layout is irrelevant for migration since disk data is parsed field-by-field.
- **Binary formats:** WAD header `wadinfo_t {char[4]; int; int}` and directory `filelump_t {int; int; char[8]}` (`w_wad.h:35-53`); savegames tagged `"version 110"` in a 16-byte field (`g_game.c:1215`, `VERSIONSIZE` `g_game.c:1198`); demos start with `VERSION` byte (`g_game.c:1555`).
- **Timing/tick model:** 35 tics/sec (`TICRATE`, `doomdef.h:122`); `I_GetTime` returns tics from wall clock (`i_system.c:88-100`); `TryRunTics` (`d_net.c:636`) runs 0..n tics per frame with `BACKUPTICS 12` ring buffers (`d_net.h:49`); `singletics` debug mode runs one tic per frame (`d_main.c:377-388`).

## Runtime flow (with evidence)

1. `main()` (`i_main.c:34`) sets `myargc/myargv`, calls `D_DoomMain()`.
2. `D_DoomMain` (`d_main.c:796`): `FindResponseFile` → `IdentifyVersion` (IWAD search in `$DOOMWADDIR`, `d_main.c:563-640`) → parse parms (`-nomonsters`, `-warp`, `-deathmatch`, …) → `V_Init` → `M_LoadDefaults` (`.doomrc`) → `Z_Init` → `W_InitMultipleFiles(wadfiles)` → shareware/PWAD checks → `M_Init` → `R_Init` (`r_main.c:773`: textures, tables, planes, light tables, sky, translations) → `P_Init` → `I_Init` (sound) → `D_CheckNetGame` → `S_Init` → `HU_Init` → `ST_Init` → demo/record parms → `D_DoomLoop()`.
3. `D_DoomLoop` (`d_main.c:354-415`): infinite loop — `I_StartFrame` → `TryRunTics()` (network-synced tic execution; runs `D_ProcessEvents`, `G_BuildTiccmd`, `M_Ticker`, `G_Ticker`, `P_Ticker` inside `G_Ticker`'s thinkers) → `S_UpdateSounds` → `D_Display` → sound submit.
4. Simulation: `G_Ticker` (`g_game.c:605`) → `P_Ticker` (`p_tick.c:130`) → `P_RunThinkers` (`p_tick.c:101`) dispatches `thinker_t.function.acp1` function pointers (`d_think.h:44-56`).
5. Rendering: `D_Display` → `R_RenderPlayerView` (`r_main.c:870`: `R_BSP` → walls `R_StoreWallRange`/`r_segs.c` → planes `R_DrawPlanes` → masked `R_DrawMasked`).
6. Audio: engine writes ASCII commands to the `sndserver` pipe (`i_sound.c:483-486`); sndserv mixes and writes `/dev/dsp`.
7. Exit: only via `I_Quit` (`i_system.c:115-125`) or `I_Error` (`i_system.c:162-182`, `exit(-1)`).

## Risks and unknowns

| ID | Question/risk | Evidence | Impact | How to resolve |
|---|---|---|---|---|
| R-01 | Original cannot be built on this Windows host (X11/Linux only) | `Makefile:9-11`, `i_video.c` X11 includes | No local reference executable for differential testing | Build in WSL/Docker/Linux CI; or use a known-good linuxdoom 1.10 binary in a controlled environment |
| R-02 | `FixedDiv2` uses `double` math | `m_fixed.c:80-86` | Bit-exact division semantics must be validated | Characterization tests over boundary values; compare C vs C# outputs |
| R-03 | Header comments say "DOOM Source Code License" but shipped license is GPL-2.0 | `doomtype.h:9` vs `LICENSE.TXT` | Licensing clarity for derived work | Treat GPL-2.0 as authoritative (shipped license file); note in ADR |
| R-04 | `FILES`/`FILES2` do not match the actual tree | `FILES` lists `i_x.c`, `fpfunc.S`, `tmap.S` — absent here | Documentation drift; don't trust FILES | Use actual directory + Makefile as truth |
| R-05 | IWAD availability for testing | `IdentifyVersion` requires doom*.wad | Cannot run engine without commercial data | Use shareware `doom1.wad` (freely redistributable) for tests; document source |
| R-06 | `strcasecmp`/`alloca.h`/`popen` POSIX dependencies in ported-logic files (`w_wad.c:67`, `w_wad.c:37`, `i_sound.c:754`) | grep | Minor; these are boundary code | Replace at platform boundary |
| R-07 | Demo/savegame compatibility depends on exact `VERSION 110` serialization | `g_game.c:1198-1291,1555` | Behavioral anchor for tests | Golden-file tests using original save/demo formats |
| R-08 | `i_video.c:49` includes nonstandard `<errnos.h>` | `i_video.c:49` | Original may not compile unmodified today | Patch locally in reference build; document |

## Recommended first migration slice

See `MIGRATION_BACKLOG.md` items DOOM-001…003. Summary: pure, deterministic, zero-platform leaf modules first — fixed-point math, random LUT, angle tables — with characterization tests generated from the C originals.

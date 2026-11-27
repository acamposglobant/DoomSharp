# ADR-005: Silk.NET (SDL3 backend) as the platform binding for video/input

- Status: Accepted
- Date: 2026-11-27

## Context

DOOM-040 replaces the original X11/Shm video path (`i_video.c`), the OSS
sndserver audio path (`i_sound.c`, `sndserv/`), and the UDP network transport
(`i_net.c`) with managed platform adapters. `ARCHITECTURE.md` §4 requires the
multimedia library to stay out of engine rules and rendering algorithms, and
§9 requires an ADR for the binding choice. Slice DOOM-040a needs: a 320×200
indexed-color frame presenter, keyboard/mouse input translated to
`event_t`/Doom key codes, and headless CI testability (no display server in
unit tests).

## Decision

Use **Silk.NET** (v2.23.x) as the multimedia binding:

- `Silk.NET.Windowing` — window creation + swapchain-free blit surface
  (Vulkan/GL not needed; we blit an RGBA buffer via `SDL3` textures through
  `Silk.NET.SDL` or present via the windowing abstraction's raw surface).
- `Silk.NET.Input` — keyboard/mouse abstractions with stable `Key` enums.
- Later slices: `Silk.NET.OpenAL` (audio) and `Silk.NET.GLFW`/SDL joystick
  support.

Rationale vs. alternatives:

| Option | Verdict |
|---|---|
| **Silk.NET 2.23** | MIT, .NET Foundation, actively regenerated against upstream SDL/GLFW, netstandard2.0 (works on .NET 10), Windows+Linux+macOS. Chosen. |
| SDL2-CS | MIT but effectively unmaintained; hand-maintained bindings; SDL2 only. Rejected. |
| Veldrid | Rendering-focused; window/input story thinner. Rejected. |
| Raw P/Invoke to SDL | Maximum control but re-implements what Silk.NET already generates and tests. Rejected. |

## Headless testability (hard requirement)

All unit tests must run without a display server. The binding is therefore
**confined to a thin adapter assembly**; everything testable is pure managed
code in `DoomSharp.Platform` with no native dependencies:

- `IFramePresenter` — interface implemented by `SilkWindowPresenter`
  (adapter) and `HeadlessFramePresenter` (test double that records frames).
- `PaletteConverter` — pure: PLAYPAL 768-byte palette + `Video.GammaTable`
  → RGBA8888 buffer. No windowing types.
- `KeyTranslator` — pure: platform `Key` → Doom key code (`xlatekey` port).
- `EventQueue` — pure: `MAXEVENTS=64` FIFO ring (`D_PostEvent`/`D_PopEvent`
  semantics from `d_event.c`).
- `DoomSharp.App` references the Silk.NET packages; the unit-test project
  does **not**, so `dotnet test` never loads native SDL binaries.

## Consequences

- Windows + Linux presentation from one code path; integer scaling
  (×1/×2/×3/×4) and aspect correction handled by the presenter, not the
  renderer.
- The renderer keeps producing `Video.Screens[0]` (320×200 indexed); the
  palette→RGBA conversion is the only format bridge.
- If Silk.NET's SDL3 backend proves unstable on a target OS, the
  `IFramePresenter`/`IInputSource` seams allow swapping the backend without
  touching engine code (recorded as a follow-up ADR if exercised).

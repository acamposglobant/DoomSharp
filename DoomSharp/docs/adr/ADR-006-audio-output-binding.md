# ADR-006: Audio Output Binding — Silk.NET.OpenAL with Headless Seam

- **Status:** Accepted
- **Date:** 2026-11-27
- **Deciders:** Migration team
- **Related:** ADR-005 (Silk.NET platform binding), DOOM-040b

## Context

DOOM-040b replaces the Linux DOOM sound path (`i_sound.c` writing 11,025 Hz
stereo signed-16-bit PCM to `/dev/dsp` via OSS ioctl, plus the optional
`sndserver` pipe protocol) with a managed software mixer. The mixer itself is
pure computation (portable, headless-testable), but the final PCM submission
needs a real audio device on Windows and Linux, while all unit tests must run
headless in CI with no sound card (same constraint as ADR-005).

## Decision

Use **Silk.NET.OpenAL 2.23** as the audio output binding in `DoomSharp.App`,
behind a new `IAudioOutput` seam in `DoomSharp.Platform`:

- `DoomSharp.Platform` (pure managed): `SoundMixer` (8-channel DMX mixer),
  `DmxSound` (format-3 lump parser + 512-sample padding), `SoundManager`
  (`s_sound.c` port), `SoundTables` (`sounds.c` tables), `IAudioOutput`,
  `HeadlessAudioOutput` (captures submitted buffers for deterministic tests).
- `DoomSharp.App` (only project referencing Silk.NET.OpenAL): `OpenAlAudioOutput`
  — 11,025 Hz stereo 16-bit streaming source; **fails gracefully** (logs a
  warning, falls back to `HeadlessAudioOutput`) when no audio device/context is
  available, so the app still runs on headless machines.

## Alternatives considered

| Option | Verdict |
| --- | --- |
| **Silk.NET.OpenAL** (chosen) | Same vendor/major version as the windowing stack (ADR-005); OpenAL Soft ships Windows/Linux; simple streaming model matches `I_SubmitSound` write semantics; graceful headless fallback is trivial. |
| SDL2 audio via Silk.NET.SDL | Viable, but mixes two native backends in one app; OpenAL is the smaller delta. |
| Raw WASAPI/ALSA P/Invoke | Maximum fidelity to `/dev/dsp` semantics, but two OS-specific implementations and no CI story. |
| NAudio / PortAudioSharp | Windows-centric or wrapper-maintenance risk; no advantage over OpenAL here. |

## Headless testability

The mixer, DMX parser, and `SoundManager` never touch a device: they push
finished PCM buffers into `IAudioOutput`. `HeadlessAudioOutput` records every
`Submit` call (buffer copies + submit count), so mixing, attenuation, and
channel-eviction tests are fully deterministic in CI. Only `DoomSharp.App`
references OpenAL, mirroring the ADR-005 isolation pattern.

## Consequences

- Music remains a faithful stub: linuxdoom's `I_*Song` API is already a no-op
  set of dummies (no music on Linux DOOM 1.10), so `IMusicPlayer` mirrors that
  contract; MUS/OPL2 synthesis stays out of scope (documented deviation).
- The `gametic < handle` quirk in `I_SoundIsPlaying` is preserved exactly and
  documented (it makes `S_UpdateSounds` free its bookkeeping channels once
  `gametic` exceeds the handle value, while the mixer itself drains channels by
  data end — characterization tests pin this behavior).

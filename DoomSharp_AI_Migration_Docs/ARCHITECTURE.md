# DoomSharp — Target Architecture (Initial Proposal)

## 1. Status

This is a proposed starting architecture, not a claim about the exact structure of the checked-out source. The agent must inspect `Legacy` and map original modules to these boundaries. Adjustments require an Architecture Decision Record (ADR).

## 2. Goals

- Preserve original engine behavior.
- Separate engine logic from operating-system and multimedia integration.
- Make core logic testable without opening a game window.
- Keep the migration incremental.
- Avoid introducing abstractions that make the port harder to compare with the original.

## 3. Suggested solution layout

```text
DoomSharp/
  DoomSharp.sln
  src/
    DoomSharp.Core/
    DoomSharp.Data/
    DoomSharp.Rendering/
    DoomSharp.Audio/
    DoomSharp.Platform/
    DoomSharp.App/
  tests/
    DoomSharp.Tests.Unit/
    DoomSharp.Tests.Compatibility/
  docs/
    adr/
    migration/
```

The agent must validate whether all these projects are justified. It is acceptable to begin with fewer projects and split only when boundaries become clear.

### Responsibilities

- `DoomSharp.Core`: engine state, game tick, gameplay rules, deterministic logic.
- `DoomSharp.Data`: WAD parsing, resource directory, map and lump representations.
- `DoomSharp.Rendering`: software rendering and rendering-specific data structures.
- `DoomSharp.Audio`: sound/music abstractions and playback orchestration.
- `DoomSharp.Platform`: keyboard, timing, filesystem, window and multimedia adapters.
- `DoomSharp.App`: composition root and executable entry point.
- `DoomSharp.Tests.Unit`: fast tests for isolated algorithms and data structures.
- `DoomSharp.Tests.Compatibility`: tests comparing C# behavior with reference outputs or original engine behavior.

## 4. Platform and multimedia

A multimedia library such as SDL2 may be used for window creation, input, audio output, and presentation. It must not become the owner of engine rules or rendering algorithms.

Before selecting a .NET SDL binding, inspect current package health, supported .NET versions, licensing, native runtime requirements, and Fedora/Windows support. Record the decision in an ADR.

Keep platform-specific code behind narrow interfaces, for example:
- `IInputSource`
- `IClock`
- `IAudioOutput`
- `IFramePresenter`
- `IFileSystem` (only if filesystem isolation is useful)

Do not create interfaces for every class. Introduce seams where they improve testability or isolate platform APIs.

## 5. Numeric fidelity

The agent must inspect the original numeric model before implementing math:
- integer widths and signedness;
- fixed-point representation and scale;
- overflow and wraparound behavior;
- angle representation and lookup tables;
- rounding and truncation rules;
- deterministic tick/update order.

Use explicit-width types (`byte`, `short`, `int`, `uint`, `long`, etc.) where they match the source. Use `unchecked` only where wraparound is intentional and documented. Do not assume C# arithmetic exactly matches the source compiler's C behavior.

## 6. Data and file formats

The agent must identify the actual formats and structures used by the checked-out version, including WAD directory handling, map lumps, resource lookup, and any platform-specific data.

Do not infer format details from memory when the source can establish them. Add malformed-input tests for parsers.

## 7. Rendering

The renderer should initially preserve the original rendering approach and output characteristics. Do not replace it with Unity, Unreal, MonoGame, or a modern 3D engine as a shortcut.

A presentation layer may display the engine's generated framebuffer. Keep the renderer's algorithms independent from the windowing library.

## 8. Error handling

- Validate external data at parsing boundaries.
- Preserve original error behavior where compatibility requires it, but avoid process crashes for malformed untrusted input when a safe failure is feasible.
- Do not swallow exceptions.
- Include context in errors (file/lump/offset/subsystem) without logging sensitive or irrelevant data.

## 9. Architecture decisions

Create an ADR for decisions such as:
- SDL binding and version;
- project boundaries;
- fixed-point representation;
- asset/test-data strategy;
- threading and timing model;
- whether any original subsystem is intentionally replaced rather than ported.

ADR template:

```markdown
# ADR-NNN: Title

- Status: Proposed | Accepted | Superseded
- Date:
- Context:
- Decision:
- Alternatives:
- Consequences:
- Compatibility impact:
- Evidence:
```

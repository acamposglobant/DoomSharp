# ADR-004 — Serilog trace logging

- **Status:** Accepted
- **Date:** 2026-11-26
- **Deciders:** Project owner (cross-cutting requirement)
- **Scope:** Every class and method in all DoomSharp projects, all future tasks

## Context

The migration requires structured, low-level trace logging for
diagnosing engine behavior and comparing it against the original C.
The project owner mandated: **every class and method must emit
structured trace logs at `LogEventLevel.Verbose` using Serilog.**

Serilog was chosen (core `Serilog` package only — no sinks in library
code; hosts/tests attach sinks).

## Decision

### Mandatory pattern

Every class declares a **re-resolving** logger property (NOT a
`static readonly` field — a captured field binds to the logger at type
initialization and never sees later `Log.Logger` replacement, which
breaks test isolation and runtime reconfiguration):

```csharp
private static ILogger Log => Serilog.Log.ForContext(typeof(ClassName));
// static classes use the same typeof() form (no generic type argument)
```

`Serilog.Log.ForContext` resolves through the **current** static logger
on every access, so sink changes propagate. With the default silent
logger this is allocation-free (`SilentLogger.ForContext` returns
itself); with a real logger attached it allocates a context wrapper —
acceptable because `Verbose` tracing is a diagnostic mode.

Every public/internal method logs entry (with key parameters) and/or
exit/outcome at `Log.Verbose(...)` using **structured message
templates** — property names in braces, arguments passed separately:

```csharp
Log.Verbose("WadDirectory.InitMultipleFiles({Count} files)", files.Count());
```

**Never** `$"..."` string interpolation in log calls — interpolation
defeats structured logging (values are baked into the message instead
of becoming queryable properties) and allocates even when the level is
disabled.

### Hot-path performance guard

High-frequency math/LUT methods — `FixedPoint`, `DoomRandom`,
`AngleTables`, and future renderer inner loops — MUST guard trace calls:

```csharp
if (Log.IsEnabled(LogEventLevel.Verbose))
{
    Log.Verbose("FixedMul({A}, {B}) = {Result}", a, b, result);
}
```

so disabled trace logging incurs zero allocations and negligible
overhead during unit tests and gameplay.

### Default silence

No sink is configured by library code. With no sink attached, Serilog's
default static logger is silent — zero output, negligible overhead
(the `IsEnabled` guard short-circuits). Tests attach a sink only when
asserting on trace output.

### Test requirements (every task)

- **Emission test:** with a `Verbose`-level sink attached, trace events
  are emitted for the module's public methods.
- **Silent-by-default test:** with no sink (or `Fatal` minimum level),
  operations produce no log output and behave identically.

## Consequences

- All subsequent backlog items must include trace logging on every new
  class/method; review checklist item.
- `Log.ForContext<T>()` gives source-context properties for filtering.
- The `IsEnabled` guard is mandatory in hot paths; reviewers must
  reject unguarded `Log.Verbose` in `FixedPoint`, `DoomRandom`,
  `AngleTables`, and future renderer inner loops.

## Alternatives considered

- **`Microsoft.Extensions.Logging` abstractions:** heavier dependency
  chain; Serilog core is sufficient and the owner specified it.
- **`$""` interpolation in log calls:** rejected — destroys structured
  properties and allocates unconditionally.
- **`Debug.WriteLine`/`Trace`:** no structured properties, no levels,
  no sink abstraction.

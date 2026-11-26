# ADR-002 — Fatal error handling (DoomFatalException)

- **Status:** Accepted
- **Date:** 2026-11-26
- **Deciders:** Project owner (explicit decision in Phase 1 authorization)
- **Scope:** All engine code where the original calls `I_Error`

## Context

The original engine aborts the process on unrecoverable errors via
`I_Error` (`i_system.c:162-183`): it prints the message to stderr, performs
shutdown (demo flush, network quit, graphics shutdown), and calls
`exit(-1)`.

`FixedDiv2` calls `I_Error("FixedDiv: divide by zero")` when the double
result of the division overflows the 32-bit int range (`m_fixed.c:84-85`).
This is reachable in normal gameplay conditions (e.g. `FixedDiv(MININT, -1)`
— the C `abs(MININT)` quirk defeats the `FixedDiv` guard, see ADR-001).

The Phase 1 authorization explicitly decided:

> Do not replace the original fatal error behavior with a sentinel value.
> Represent the fatal condition in C# using a dedicated exception, such as
> `DoomFatalException`. At the application boundary, this exception must
> result in a non-successful process termination, preserving the original
> fatal-error semantics. Unit tests must be able to verify this condition
> without terminating the test runner.

## Decision

1. Engine code throws `DoomSharp.Core.DoomFatalException` wherever the
   original calls `I_Error`. The exception message preserves the original
   format string output (e.g. `"FixedDiv: divide by zero"`).
2. `DoomFatalException` is `sealed` and derives from `Exception` — it is
   not a `SystemException` subclass, so generic handlers that catch
   `SystemException` will not silently swallow it.
3. **Application boundary contract:** the host application's top-level
   handler must catch `DoomFatalException` and terminate the process with a
   non-success exit code (mirroring `I_Error`'s `exit(-1)`), after
   performing the equivalent of the original shutdown sequence. This
   contract will be implemented by the future host project and documented
   there.
4. Unit tests assert `Assert.Throws<DoomFatalException>(...)` — the test
   runner is never terminated.

## Consequences

- Fatal conditions are testable without killing the test process.
- The engine library itself never calls `Environment.Exit` — termination
  policy belongs to the host, keeping `DoomSharp.Core` platform-independent.
- Any future migration of code calling `I_Error` must throw
  `DoomFatalException` with the original message text; this is a review
  checklist item for every backlog item.

## Alternatives considered

- **Sentinel return values:** rejected by the project owner — silently
  continuing after a fatal condition would diverge from original behavior.
- **`Environment.Exit` directly in engine code:** rejected — makes the
  library untestable and platform-coupled.
- **`ApplicationException` / custom hierarchy:** unnecessary; a single
  sealed type is sufficient and unambiguous.

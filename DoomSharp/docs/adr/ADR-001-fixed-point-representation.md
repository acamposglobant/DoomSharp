# ADR-001 — Fixed-point representation

- **Status:** Accepted
- **Date:** 2026-11-26
- **Deciders:** Project owner (approved Phase 1 authorization), migration agent
- **Scope:** All numeric engine code in DoomSharp

## Context

The original linuxdoom-1.10 engine represents all fractional quantities as
32-bit signed fixed point with 16 fractional bits:

- `typedef int fixed_t;` with `FRACBITS 16`, `FRACUNIT (1<<16)` (`m_fixed.h:35-38`)
- `FixedMul`: `((long long)a * (long long)b) >> FRACBITS` (`m_fixed.c:48`)
- `FixedDiv`: guard `(abs(a)>>14) >= abs(b)` → `MININT`/`MAXINT` sentinel by sign, else `FixedDiv2` (`m_fixed.c:58-65`)
- `FixedDiv2`: computed in `double`, truncated to `int` by C cast; out-of-range → `I_Error` (`m_fixed.c:80-86`)

Angles use Binary Angle Measurement: `typedef unsigned angle_t`, full turn = 2³² (`tables.h:78`).

C# `int`/`long`/`double` are all IEEE-754/two's-complement compatible with the
C behavior **provided overflow is not checked** — .NET default arithmetic in
`checked` contexts (or `/checked` build flag) would throw where C wraps.

## Decision

1. `fixed_t` maps to C# `int`; `angle_t` maps to C# `uint`.
2. All engine arithmetic that can overflow is written inside `unchecked`
   blocks (or the project compiles with `<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>`, the default).
3. `FixedMul` uses `(int)(((long)a * b) >> 16)` — the 64-bit product and
   arithmetic right shift reproduce C exactly, including floor behavior for
   negative results.
4. `FixedDiv` reproduces the C guard **including the `abs(MININT)` quirk**:
   in C, `abs(int.MinValue)` returns `int.MinValue` (still negative), so the
   signed comparison can pass through to `FixedDiv2`. The C# port uses the
   same signed negation (`a < 0 ? -a : a`), not `Math.Abs`, to preserve this.
5. `FixedDiv2` computes `((double)a / (double)b) * 65536.0` in `double` and
   casts to `int` (truncation toward zero, identical to C). The overflow
   check `c >= 2147483648.0 || c < -2147483648.0` matches `m_fixed.c:84`.
6. No `float`, no `decimal`, no `Math` library calls in migrated numeric code.

## Consequences

- Bit-exact equivalence with the original, proven by 2068 characterization
  vectors + 16 fatal cases generated from the unmodified C sources
  (`tools/charact/vectors.json`).
- `unchecked` is mandatory in engine code; reviewers must not "fix" it.
- The `abs(MININT)` quirk means `FixedDiv(int.MinValue, -1)` reaches the
  fatal path — documented and tested (see ADR-002).

## Alternatives considered

- **`double` everywhere / modernize to floating point:** rejected — changes
  observable behavior (rendering, movement, AI all depend on exact fixed-point).
- **`Math.DivRem` / checked arithmetic:** rejected — different overflow and
  rounding semantics.
- **`Int128` for FixedMul:** unnecessary; `long` suffices and matches C's
  `long long` exactly.

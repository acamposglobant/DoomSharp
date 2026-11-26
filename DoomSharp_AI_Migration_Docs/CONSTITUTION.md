# DoomSharp Project Constitution

## 1. Mission

Create a C# implementation of the DOOM engine based on the source code located at `C:\projects\DOOM\Legacy`.

The purpose is both:
- to produce a functioning, understandable C# engine; and
- to evaluate AI agents on real-world software comprehension, legacy-code migration, architectural reasoning, and behavioral preservation.

This is a migration and learning project, not a license to redesign the engine arbitrarily.

## 2. Non-negotiable principles

1. **Behavior before redesign.** Preserve original behavior unless a documented decision explicitly approves a deviation.
2. **Evidence before assumptions.** Inspect the actual source. Never invent modules, APIs, algorithms, file formats, or build steps.
3. **Small vertical slices.** Each migration unit must compile, have tests or a documented validation method, and be reviewable.
4. **Traceability.** Every migrated subsystem must map to original source files, functions, data structures, and relevant tests.
5. **No blind translation.** Translate semantics and intent, not C syntax line by line.
6. **No silent omissions.** Unsupported, platform-specific, or unclear behavior must be recorded as a blocker or explicit limitation.
7. **No premature optimization.** First establish correctness and compatibility. Optimize only with measurements and regression tests.
8. **Readable idiomatic C#.** Use clear types and encapsulation, but do not distort original semantics for stylistic purity.
9. **Deterministic tests where possible.** Time, randomness, input, filesystem, and audio/video dependencies should be isolated behind controllable boundaries.
10. **Human approval for architectural changes.** Agents may propose decisions but must not silently change approved architecture.

## 3. Source-of-truth hierarchy

When sources disagree, use this order:

1. Executable behavior of the original engine, where it can be built and run.
2. Original source code in `C:\projects\DOOM\Legacy`.
3. Original project documentation, comments, and build scripts.
4. Established DOOM format/engine documentation, used as supporting evidence.
5. Agent inference, which must always be labelled as inference.

The agent must identify the exact source revision and license before substantial implementation.

## 4. Required workflow

For each subsystem:

1. Inspect and document the original implementation.
2. Identify public/internal contracts, data structures, numeric assumptions, and side effects.
3. Write a migration design.
4. Define characterization tests or golden/reference checks.
5. Implement the smallest useful slice in C#.
6. Build and run tests.
7. Compare behavior with the original where practical.
8. Update documentation and migration status.
9. Submit a concise summary with evidence and known gaps.

## 5. Coding rules

- Target .NET 10 unless a documented compatibility constraint requires otherwise.
- Enable nullable reference types.
- Treat compiler warnings as issues to resolve; do not suppress warnings globally.
- Use explicit, meaningful types for engine concepts.
- Avoid `dynamic` except where justified and documented.
- Avoid reflection-based magic in core engine logic.
- Avoid unnecessary NuGet dependencies.
- Do not introduce an ECS, dependency injection framework, game engine, or large abstraction layer without a demonstrated need.
- Keep platform integration at the boundary.
- Preserve fixed-width integer behavior and overflow semantics where the original depends on them.
- Do not replace fixed-point arithmetic with floating point without a written decision and comparative tests.
- Do not use `unsafe` unless required for a measured, documented reason.
- Do not port platform-specific code blindly. Identify its responsibility and replace it with a suitable .NET/platform abstraction.

## 6. Dependency and licensing rules

- Record every third-party dependency and its license.
- Verify the source project's license from files in the actual checkout.
- Preserve required copyright and license notices.
- Do not copy commercial DOOM game data into the repository.
- Treat engine source licensing and game asset licensing as separate matters.
- Use freely distributable compatible game data for testing where possible, and document its source and license.

## 7. Definition of done

A migration task is complete only when:
- the original implementation and its role are documented;
- the C# implementation exists and is included in the solution;
- the project builds;
- relevant tests pass;
- behavior differences are documented;
- no unexplained TODOs or placeholder implementations remain in the migrated scope;
- documentation and backlog are updated.

A successful build alone does not mean a subsystem is correctly migrated.

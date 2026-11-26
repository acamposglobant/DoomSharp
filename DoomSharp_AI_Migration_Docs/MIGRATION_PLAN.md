# DoomSharp — Migration Plan

## Phase 0 — Source reconnaissance (no implementation)

- [ ] Confirm repository identity, version, commit/revision, and license.
- [ ] Read README, license, build scripts, makefiles, and platform documentation.
- [ ] Identify all source files and classify them by subsystem.
- [ ] Identify compiler assumptions, platform APIs, assembly, macros, and conditional compilation.
- [ ] Identify external libraries and required tools.
- [ ] Determine whether the original can be built in the current environment.
- [ ] Determine how to run a reference build and which compatible IWAD/PWAD is available.
- [ ] Produce `SOURCE_INVENTORY.md`, `MIGRATION_BACKLOG.md`, and initial ADRs.

**Exit criteria:** source inventory reviewed by the human; no major unknown is hidden.

## Phase 1 — Target skeleton

- [ ] Create .NET 10 solution and minimal executable.
- [ ] Configure nullable reference types and analyzers.
- [ ] Configure unit and compatibility test projects.
- [ ] Add CI/build instructions.
- [ ] Add a minimal application loop only after source timing/platform behavior is understood.

**Exit criteria:** clean build and test execution on the primary development OS.

## Phase 2 — Data and foundational algorithms

Migrate in dependency order based on the source inventory. Candidate areas (verify against actual source):
- primitive types and constants;
- fixed-point math and angle helpers;
- byte/stream utilities;
- WAD/resource loading;
- map and geometry data structures.

**Exit criteria:** parser and algorithm tests cover valid, boundary, and malformed inputs.

## Phase 3 — Core simulation

Migrate the deterministic engine state and update logic in small slices:
- game state and tick progression;
- world/map state;
- player state and movement;
- collision and movement rules;
- entities, thinkers, and gameplay interactions.

The exact order must follow the dependency graph discovered in Phase 0.

**Exit criteria:** deterministic test scenarios produce stable, documented results.

## Phase 4 — Rendering

- [ ] Identify original renderer pipeline and data dependencies.
- [ ] Port low-level math and rendering data structures.
- [ ] Port rendering algorithms in dependency order.
- [ ] Produce a framebuffer independent of the platform presentation layer.
- [ ] Compare selected frames against reference captures where possible.

**Exit criteria:** known test scenes render consistently and differences are explained.

## Phase 5 — Input, audio, and platform integration

- [ ] Port input mapping and event semantics.
- [ ] Integrate the selected multimedia binding.
- [ ] Port sound/music orchestration.
- [ ] Validate timing, input latency, and resource lifecycle.

**Exit criteria:** application can run an end-to-end playable scenario.

## Phase 6 — Compatibility and completion

- [ ] Run the full test suite.
- [ ] Compare original and migrated behavior using repeatable scenarios.
- [ ] Document intentional deviations and unsupported features.
- [ ] Profile before optimizing.
- [ ] Update license notices and third-party attributions.
- [ ] Produce a final migration report.

## Prioritization

Use this order when choosing work:
1. Foundational code required by many modules.
2. Pure deterministic logic with easy tests.
3. Parsers and data structures.
4. Core simulation.
5. Rendering.
6. Platform, audio, and polish.

Do not assume this order is final until the source dependency graph has been inspected.

## Backlog item format

Each item in `MIGRATION_BACKLOG.md` must include:
- ID and title;
- original files and symbols;
- responsibility and dependencies;
- target project/files;
- semantic risks;
- tests and validation;
- acceptance criteria;
- status: Proposed | Ready | In Progress | Blocked | Review | Done.

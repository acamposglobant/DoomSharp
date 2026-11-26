# DoomSharp — AI-assisted migration documentation

This folder contains the project instructions and working documents for migrating the DOOM source code in:

`C:\projects\DOOM\Legacy`

The intended target is a maintainable C# implementation, initially targeting **.NET 10**. The migration must preserve observable behavior before attempting redesigns or optimizations.

## How to use these documents

1. Copy this entire documentation folder into `C:\projects\DOOM\` (next to `Legacy`), or copy the Markdown files into a `docs` folder in your new repository.
2. Give the agent the documents in this order:
   - `CONSTITUTION.md`
   - `MIGRATION_PLAN.md`
   - `ARCHITECTURE.md`
   - `VALIDATION.md`
   - `AGENT_WORKFLOW.md`
3. Ask the agent to inspect `Legacy` and produce an inventory before writing or changing code.
4. Review and approve the inventory and the first architecture decisions.
5. Migrate in small, independently testable slices. Do not ask an agent to translate the entire source tree in one pass.

## Initial assumptions

- Source directory: `C:\projects\DOOM\Legacy`
- Target language: C#
- Target framework: .NET 10
- Target solution name: `DoomSharp`
- Target solution directory (suggested): `C:\projects\DOOM\DoomSharp`
- The original source is treated as the behavioral reference.
- The source repository is expected to be the id Software Linux Doom source release, not necessarily the original DOS build. The agent must verify the actual checkout, version, license, build instructions, and platform-specific files before planning detailed work.
- A compatible IWAD/PWAD is required to run the engine. Do not assume game data is included in the source repository.

## First prompt to give the agent

> Read `CONSTITUTION.md`, `MIGRATION_PLAN.md`, `ARCHITECTURE.md`, `VALIDATION.md`, and `AGENT_WORKFLOW.md`. Do not modify source files yet. Inspect `C:\projects\DOOM\Legacy`, identify the exact source version and license, map the modules and build system, identify platform-specific code and external dependencies, and produce `SOURCE_INVENTORY.md` plus an initial `MIGRATION_BACKLOG.md`. Cite concrete file paths and symbols for every finding. Flag uncertainties rather than guessing.

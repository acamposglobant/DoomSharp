# DoomSharp — AI Agent Workflow

## 1. Operating rules

The agent must follow `CONSTITUTION.md`. It must inspect the actual source under `C:\projects\DOOM\Legacy` and must not assume the source layout from online examples.

Do not make broad changes without a task-specific plan. Do not modify `Legacy` unless explicitly asked; it is the immutable reference copy.

## 2. First task: reconnaissance only

The first assignment is analysis, not migration.

Deliver:
1. `SOURCE_INVENTORY.md`
2. `MIGRATION_BACKLOG.md`
3. dependency/module map;
4. build and run instructions for the original source;
5. list of unresolved questions and risks;
6. proposed first migration slice.

Every factual statement must include a source file path and, where possible, symbol/function names. Separate verified facts from hypotheses.

## 3. Task execution loop

For every backlog item:

1. Read the constitution, architecture, validation strategy, and relevant ADRs.
2. Inspect all original files involved, including callers and callees.
3. Summarize the original behavior and dependencies.
4. Propose the C# design and tests.
5. Wait for human approval if the task changes an architectural decision or has high semantic risk.
6. Implement only the agreed scope.
7. Build and run relevant tests.
8. Review the diff for unrelated changes and accidental edits to `Legacy`.
9. Update backlog and migration documentation.
10. Report what changed, commands run, test results, gaps, and next step.

## 4. Pull request / commit discipline

- One logical subsystem or vertical slice per PR.
- Keep diffs focused.
- Do not combine formatting-only changes with semantic migration.
- Do not claim tests passed unless they were actually run.
- Do not fabricate test output, benchmark results, or source references.
- Include a migration mapping in the PR description.

Suggested PR template:

```markdown
## Scope
## Original source mapping
## Behavior preserved
## Implementation summary
## Tests added
## Validation commands and results
## Known differences / risks
## Follow-up work
```

## 5. Agent response format

At the end of each task, report:

- **Status:** Done | Partial | Blocked
- **Scope:** what was migrated
- **Source evidence:** original paths and symbols
- **Target files:** C# paths and types
- **Tests:** exact commands and results
- **Behavioral differences:** list or "none observed" (not "none" unless proven)
- **Risks / unknowns**
- **Next recommended task**

## 6. Stop conditions

Stop and ask for human input when:
- source license or provenance is unclear;
- original behavior cannot be determined;
- a proposed change alters engine semantics;
- a dependency requires a licensing or distribution decision;
- the original source cannot be built and no alternative reference exists;
- a task would require large-scale restructuring beyond the approved architecture.

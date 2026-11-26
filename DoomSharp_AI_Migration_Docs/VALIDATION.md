# DoomSharp — Validation and Behavioral Equivalence

## 1. Objective

The goal is not merely to compile translated code. The goal is to establish that the C# implementation preserves the original engine's observable behavior within explicitly defined tolerances.

## 2. Validation layers

### A. Static and build validation
- Build the original using its documented build process where feasible.
- Build the C# solution in Release and Debug.
- Treat warnings as review items.
- Run formatters and analyzers consistently.

### B. Unit tests
Test pure functions and data structures independently:
- fixed-point operations;
- angle/math helpers;
- binary readers;
- resource directory lookup;
- geometry and collision primitives;
- state transitions.

### C. Characterization tests
Before migrating a function, capture representative inputs and outputs from the original implementation. Reuse those vectors against C#.

Include:
- normal inputs;
- minimum/maximum values;
- negative values;
- overflow boundaries;
- invalid/truncated data;
- ordering-sensitive cases.

### D. Differential tests
Where the original executable can be built, run the same deterministic scenario in both implementations and compare:
- computed values;
- state transitions;
- parsed resource metadata;
- framebuffer output;
- event/tick progression.

Document any non-deterministic inputs and control them where possible.

### E. End-to-end tests
Validate:
- startup and shutdown;
- loading compatible game data;
- entering a level;
- player movement and collision;
- rendering;
- sound and input;
- clean handling of missing or invalid data.

## 3. Numeric behavior

Pay special attention to C versus C# differences:
- integer promotion;
- signed overflow;
- shifts of signed values;
- division and remainder for negative operands;
- narrowing conversions;
- byte order;
- struct packing and alignment;
- fixed-point rounding and saturation/wraparound.

Never change numeric types or arithmetic behavior without tests.

## 4. Rendering comparisons

Use a stable test map, fixed player position/orientation, fixed tick count, and fixed rendering settings.

Compare:
- exact pixel equality when expected;
- otherwise image difference metrics with a documented threshold;
- geometry/depth outputs where available.

Do not dismiss visual differences as harmless without identifying their cause.

## 5. Performance

Correctness comes first. Record a baseline before optimization:
- frame time;
- update/tick time;
- memory use;
- load time.

Compare equivalent workloads and builds. Do not compare Debug against Release.

## 6. Test data and assets

The engine source and game data are separate. Use compatible data that can legally be used for testing. Document the origin and license of every test asset. Do not commit commercial game data unless the repository's rights and distribution terms explicitly permit it.

## 7. Migration report

For each completed subsystem, record:
- original source files/functions;
- C# files/classes;
- tests added;
- commands executed;
- test results;
- known behavioral differences;
- unresolved risks.

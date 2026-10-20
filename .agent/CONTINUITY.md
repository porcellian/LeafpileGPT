# Continuity

## [PLANS]
- 2026-10-20T04:40Z [USER] Implement the accepted 20-task Z-machine interpreter plan; complete one task per PR and wait for manual merge before starting the next.

## [DECISIONS]
- 2026-10-20T04:40Z [USER] Use Avalonia and target macOS and Windows only; use .NET 10 and xUnit.
- 2026-10-20T04:40Z [USER] Keep story files local; automated tests and CI use generated fixtures.

## [PROGRESS]
- 2026-10-20T04:54Z [USER] PR #1 was merged as `8d12864`.
- 2026-10-20T05:03Z [CODE] Making task numbers explicit in `TASKS.md` and preserving user edits to `AGENTS.md` and `README.md` on `feature/1-explicit-task-numbering`.
- 2026-10-20T05:17Z [USER] PR #2 was merged; `origin/main` is at `13e524e` [TOOL].
- 2026-10-20T05:21Z [CODE] Implemented Task 02 on `feature/2-story-loading`; Release build, formatting, whitespace check, and all 36 generated tests pass.
- 2026-10-20T05:27Z [USER] PR #3 was merged. Task 02 is complete; Task 03 has not started at the user's request to stand by.
- 2026-10-20T14:34Z [CODE] Implemented Task 03 on `feature/3-machine-state`; Release build, formatter, whitespace check, and all 75 generated tests pass. PR review pending.
- 2026-10-20T14:45Z [USER] PR #4 was merged.
- 2026-10-20T14:50Z [CODE] Implemented and reviewed Task 04 on `feature/4-routines-stacks`; Release build has zero warnings/errors, formatter and whitespace checks pass, and all 95 generated tests pass. PR review pending.
- 2026-10-20T15:09Z [USER] PR #5 was merged; this supersedes the Task 04 PR review status above.
- 2026-10-20T15:09Z [CODE] Started Task 05 on `feature/5-ztext-character-conversion`; packed text and character conversion services are in progress.
- 2026-10-20T15:16Z [CODE] Completed and reviewed Task 05 on `feature/5-ztext-character-conversion`; Release build has zero warnings/errors, formatter and whitespace checks pass, and all 131 generated tests pass. PR review pending; this supersedes the Task 05 in-progress status above.
- 2026-10-20T15:19Z [TOOL] Re-ran the Release build, formatter, staged whitespace check, and full suite after final code review; the build and checks pass with all 131 tests passing.
- 2026-10-20T15:38Z [USER] PR #6 was merged; this supersedes the Task 05 PR review status above.
- 2026-10-20T15:38Z [CODE] Completed Task 06 on `feature/6-instruction-decoder`; the decoder and regression suite are ready for PR review.
- 2026-10-20T15:39Z [TOOL] Final Task 06 build and formatter pass; the full generated suite passes with all 154 tests.
- 2026-10-20T15:48Z [USER] PR #7 was merged; this supersedes the Task 06 PR review status above.
- 2026-10-20T15:48Z [CODE] Started Task 07 on `feature/7-control-flow-opcodes` from `main` at `fe94611`.
- 2026-10-20T16:00Z [CODE] Completed and reviewed Task 07 on `feature/7-control-flow-opcodes`; control-flow execution is implemented and ready for PR review.
- 2026-10-20T16:00Z [TOOL] Task 07 Release build succeeded with zero warnings/errors, formatter and whitespace checks pass, and all 164 generated tests pass.
- 2026-10-20T16:10Z [USER] PR #8 was merged; this supersedes the Task 07 PR review status above.
- 2026-10-20T16:10Z [CODE] Started Task 08 on `feature/8-arithmetic-memory-system-opcodes` from merged `main` at `7b42175`.
- 2026-10-20T16:20Z [CODE] Completed and reviewed Task 08 on `feature/8-arithmetic-memory-system-opcodes`; arithmetic, variable, memory/table, random, shift, and stack cleanup opcodes are ready for PR review.
- 2026-10-20T16:20Z [TOOL] Task 08 Release build succeeded with zero warnings/errors, formatter and whitespace checks pass, and all 187 generated tests pass.

## [DISCOVERIES]
- 2026-10-20T04:40Z [TOOL] The repository initially had no C# projects or continuity file; local story candidates cover V3–V6.
- 2026-10-20T04:50Z [TOOL] Avalonia build telemetry must be disabled in this restricted workspace with `AVALONIA_TELEMETRY_OPTOUT=1`; CI uses the same setting.
- 2026-10-20T15:38Z [TOOL] `call_vs2` and `call_vn2` place both operand-type bytes before all operand values; generated tests verify eight-operand decoding and invalid omitted-type ordering.

## [OUTCOMES]
- 2026-10-20T04:50Z [CODE] Added the .NET 10 solution, Avalonia desktop shell, xUnit test, macOS/Windows CI, task list, and project instructions. Release build and the setup test pass.
- 2026-10-20T05:21Z [CODE] Added standalone and Blorb Z-code loading with header/memory validation and clear errors; tested generated V1–V8 stories and malformed inputs. Local stories remain untracked.
- 2026-10-20T14:34Z [CODE] Added bounded dynamic/static memory, packed and word address decoding, signed arithmetic, deterministic/random RNG modes, program counter and restart state; tested boundaries and version-specific behavior.
- 2026-10-20T14:50Z [CODE] Added version-aware routine entry/return, locals and argument tracking, evaluation stack frames, catch/throw, and V6 user stacks; covered V1–V8 generated fixtures.
- 2026-10-20T15:16Z [CODE] Added packed Z-text decoding and encoding, versioned shifts, abbreviations, custom alphabets, dictionary truncation, ZSCII/Unicode translation, invalid-character replacement, and restart-aware text services; covered V1–V8 with generated fixtures.
- 2026-10-20T15:38Z [CODE] Added instruction decoding for long, short, variable, and extended forms; opcode legality and operand counts vary by version, with typed operands, stores, branches, and inline `print`/`print_ret` strings. Build and formatting pass; all 153 generated tests pass.
- 2026-10-20T15:39Z [TOOL] Final Task 06 verification supersedes the prior count: Release build succeeded with zero warnings/errors, formatter and whitespace checks passed, and all 154 tests passed.
- 2026-10-20T16:00Z [CODE] Added the execution framework for conditional branches, calls/returns, save/restore service flow, restart/quit, verification, scan-table, and argument/catch/throw control instructions, with generated tests for key version differences.
- 2026-10-20T16:20Z [CODE] Added signed arithmetic with 16-bit wrapping, indirect variable stack-top access, bounded byte/word arrays, overlap-aware `copy_table`, random opcode seeding/reset, shifts, and V6 `pop_stack` execution.

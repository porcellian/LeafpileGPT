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
- 2026-10-20T16:25Z [USER] PR #9 was merged; this supersedes the Task 08 PR review status above.
- 2026-10-20T16:25Z [CODE] Started Task 09 on `feature/9-object-property-opcodes` from merged `main` at `017c2f2`.
- 2026-10-20T16:56Z [CODE] Completed and reviewed Task 09 on `feature/9-object-property-opcodes`; versioned object/property operations and their opcode handlers are ready for PR review.
- 2026-10-20T16:56Z [TOOL] Task 09 Release build succeeded with zero warnings/errors, formatter and whitespace checks pass, and all 207 generated tests pass.
- 2026-10-20T17:56Z [USER] PR #10 was merged; this supersedes the Task 09 PR review status above.
- 2026-10-20T17:56Z [CODE] Started Task 10 on `feature/10-dictionary-token-text-opcodes` from merged `main` at `ec6d519`.
- 2026-10-20T18:07Z [CODE] Completed and reviewed Task 10 on `feature/10-dictionary-token-text-opcodes`; dictionary, tokenization, text encoding, Unicode capability, and scan-table coverage is ready for PR review.
- 2026-10-20T18:07Z [TOOL] Task 10 Release build succeeded with zero warnings/errors, formatter and whitespace checks pass, and all 235 generated tests pass.
- 2026-10-20T18:40Z [USER] PR #11 was merged; Task 10 is complete and Task 11 may proceed.
- 2026-10-20T18:40Z [CODE] Completed Task 11 on `feature/11-input-output-streams-files`; host input/output contracts and stream execution are ready for PR review.
- 2026-10-20T18:40Z [TOOL] Task 11 Release build succeeded with zero warnings/errors, format and whitespace checks pass, and all 256 generated tests pass.
- 2026-10-20T18:50Z [USER] PR #12 was merged; Task 11 is complete and Task 12 may proceed.
- 2026-10-20T18:50Z [CODE] Completed Task 12 on `feature/12-v1-v5-screen-model`; the versioned text-screen model and opcode execution are ready for PR review.
- 2026-10-20T18:50Z [TOOL] Initial Task 12 verification passed with 271 tests; this was superseded by final verification below.
- 2026-10-20T18:56Z [TOOL] Final Task 12 build and review passed with 274 tests; superseded by the additional status-before-read regression below.
- 2026-10-20T18:57Z [TOOL] Final Task 12 Release build succeeded with zero warnings/errors, formatting and whitespace checks passed, and all 275 generated tests passed. Full diff review found no remaining findings.
- 2026-10-20T19:21Z [USER] PR #13 was merged; Task 13 may proceed.
- 2026-10-20T19:21Z [CODE] Completed Task 13 on `feature/13-v6-v8-screen-model`; V6 windows, controls, formatted output, input capability reporting, and V7/V8 opcode boundaries are ready for PR review.
- 2026-10-20T19:21Z [TOOL] Task 13 Release build succeeded with zero warnings/errors, formatter and whitespace checks passed, and all 284 generated tests passed.
- 2026-10-20T19:24Z [CODE] Final review found V7/V8 fixed-pitch and word-wrap behavior still gated to V5; corrected it, superseding the earlier Task 13 completion/verification entries.
- 2026-10-20T19:24Z [TOOL] Final Task 13 Release build succeeded with zero warnings/errors, formatting and whitespace checks passed, and all 285 generated tests passed.

## [DISCOVERIES]
- 2026-10-20T04:40Z [TOOL] The repository initially had no C# projects or continuity file; local story candidates cover V3–V6.
- 2026-10-20T04:50Z [TOOL] Avalonia build telemetry must be disabled in this restricted workspace with `AVALONIA_TELEMETRY_OPTOUT=1`; CI uses the same setting.
- 2026-10-20T15:38Z [TOOL] `call_vs2` and `call_vn2` place both operand-type bytes before all operand values; generated tests verify eight-operand decoding and invalid omitted-type ordering.
- 2026-10-20T16:56Z [TOOL] Generated executor tests exposed that 1OP `get_parent` and `get_prop_len` require store bytes; decoder metadata now describes those stores, with regression coverage.
- 2026-10-20T18:07Z [TOOL] The standard distinguishes zero-terminated V1–V4 text buffers from length-prefixed V5+ buffers; parse positions are offsets into the respective buffer. Generated tests verify both layouts.
- 2026-10-20T18:40Z [TOOL] Timed input uses an interpreter-thread callback; interrupt output must be sent to the screen while input remains pending. Host capability flags allow unsupported external streams to warn and fall back cleanly.
- 2026-10-20T18:50Z [TOOL] V5 screen dimensions occupy interpreter-owned header bytes `$20–$27`; opcode fixtures using those addresses as ordinary memory must be moved to safe dynamic-memory addresses.
- 2026-10-20T18:56Z [TOOL] The text screen stores a row/column grid per window so cursor moves, erase-line, and scrolling update the actual visible cells rather than only tracking cursor metadata.
- 2026-10-20T19:21Z [TOOL] V6 `get_cursor` has no store byte; generated execution coverage exposed and corrected the decoder metadata. V6-only window/screen operand forms are rejected in V7/V8, which retain the V5 two-window model. `set_font` remains available in V5, V7 and V8, but uses single-window state there.
- 2026-10-20T19:24Z [TOOL] V7/V8 use the V5 window, wrapping, and font model; tests exercise `set_font` and fixed-pitch capability across versions 5, 7, and 8.

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
- 2026-10-20T16:56Z [CODE] Added V1–V3 and V4+ object layouts, attribute access/mutation, object-tree traversal and safe insertion/removal, short-name decoding, property defaults and parsing, lookup/address/length/iteration/update operations, and executor support for object/property opcode families. Generated fixtures cover versioned entries, headers, long lengths, malformed ordering/pointers, mutation invariants, and opcode execution.
- 2026-10-20T18:07Z [CODE] Added version-aware dictionary headers and sorted/unsorted lookup, separator-aware lexical analysis into bounded parse tables, `tokenise` and `encode_text`, Unicode input/output capability checks, and generated V1–V8 text-buffer tests plus V4/V5 `scan_table` word/byte cases.
- 2026-10-20T18:40Z [CODE] Added host-provided line/key input, timed interrupt execution, function-key terminators, V1–V8 buffer and parse behavior, input/output stream selection, nested stream 3 capture, transcript/command streams, routed text output, `print_table`, `buffer_mode`, and suggested filename normalization. Generated tests cover V1–V8 input, timers, stream nesting, filename rules, and unsupported command-file input. Quetzal save-file encoding remains Task 17.
- 2026-10-20T18:57Z [CODE] Added V1–V5 screen state and opcode support for status lines, dimensions/resizing, V3–V5 upper/lower windows, cursor operations, erase behavior, text styles, V5 colors, and lower-window word buffering. Generated screen tests cover versions 1–5, including status refresh before input; all 275 tests pass.
- 2026-10-20T19:21Z [CODE] Added V6's eight pixel-coordinate windows, per-window geometry/cursors/styles/colors/fonts/attributes/properties, newline interrupt routines, buffer_screen, formatted stream output and print_form. Unsupported picture, sound, mouse, menu, and transparency features are reported through capability flags; mouse reads return an empty state and picture/menu queries fail cleanly. V7/V8 retain V5 screen behavior. All 284 tests pass.
- 2026-10-20T19:24Z [CODE] Final Task 13 implementation includes V6 window controls and formatted output, capability reporting for unavailable devices/resources, plus V7/V8 V5-model behavior. Supersedes the 19:21 test count; all 285 generated tests pass.

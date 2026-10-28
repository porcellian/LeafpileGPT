# LeafpileGPT Continuity

## [PLANS]
- 2026-10-28T02:34Z [USER] Implement the approved V1–V8 C# interpreter/Avalonia plan in one task branch and PR at a time; stop for user review/merge after each PR.
- 2026-10-28T02:49Z [TOOL] Task 1 PR #1 merged; Task 2 is complete locally on `feature/2-solution-build-pipeline` and awaiting PR creation/review.
- 2026-10-28T02:51Z [TOOL] Supersedes prior Task 2 status: PR #2 is open at https://github.com/porcellian/LeafpileGPT/pull/2; CI is running.
- 2026-10-28T03:03Z [TOOL] PR #2 merged; Task 3 is in progress on `feature/3-story-file-validation`.
- 2026-10-28T03:12Z [TOOL] Task 3 is committed as `70fae26`; PR #3 is open at https://github.com/porcellian/LeafpileGPT/pull/3. Await user review/merge before Task 4.
- 2026-10-28T03:14Z [TOOL] PR #3 merged to main as `bb84a63`.
- 2026-10-28T03:19Z [USER] Add Story File Info as Task 30 in `TASKS.md`; defer implementation until that task is reached. Walkthrough tasks are renumbered to 31–65.
- 2026-10-28T03:25Z [TOOL] PR #4 merged to main as `a1410f5`.
- 2026-10-28T03:33Z [USER] Add an unnumbered Glulx future-task placeholder after the current task roadmap; keep detailed breakdown deferred until specs are supplied in `reference/`.
- 2026-10-28T03:35Z [TOOL] PR #5 merged to main as `8108953`.
- 2026-10-28T03:41Z [USER] Include both headless route replay and terminal play in the roadmap; place replay in Task 27 and terminal play in a separate console host task, text-and-keyboard only.
- 2026-10-28T03:44Z [TOOL] PR #6 merged to main as `98911f0`.
- 2026-10-28T03:47Z [USER] Move terminal play from numbered Task 66 to an unnumbered entry in Future Tasks before Glulx; preserve Task 27 headless replay.
- 2026-10-28T03:49Z [TOOL] PR #7 merged to main as `04d9903`; Task 4 started on `feature/4-memory-address-translation`.
- 2026-10-28T04:11Z [USER] PR #8 is merged; Task 5 proceeds on `feature/5-instruction-decoder`.
- 2026-10-28T04:20Z [USER] PR #9 is merged as `0913bf7`; Task 6 proceeds on `feature/6-execution-state-calls` from merged main.
- 2026-10-28T04:30Z [USER] PR #10 is merged; proceed with Task 7 on `feature/7-arithmetic-vm-operations`.
- 2026-10-28T14:04Z [USER] Supersedes prior Task 8 pull-request status: PR #12 is merged; Task 9 proceeds from updated `origin/main` on `feature/9-objects-and-properties`.

## [DECISIONS]
- 2026-10-28T02:34Z [USER] Target .NET 10 LTS and Avalonia 12; xUnit; Windows/macOS/Linux.
- 2026-10-28T02:34Z [USER] Keep story/manual/hint files, routes, and raw traces local; commit original walkthrough PDFs. Use four runtime error policies, default first-per-type; logs omit player commands/story output.
- 2026-10-28T02:42Z [TOOL] Central package versions pin Avalonia 12.1.4 and xUnit v3 4.0.2; SDK selection starts at 10.0.100 and rolls forward within the .NET 10 feature band.
- 2026-10-28T04:25Z [CODE] Default the combined routine-frame and evaluation stack budget to 32,768 words; reject configurations below the standard's 1,024-word minimum.
- 2026-10-28T04:25Z [CODE] Represent catch tokens as active call-stack indices; throw removes the indexed routine and deeper frames, then stores its return value in the target caller.
- 2026-10-28T04:34Z [CODE] Task 7 executes decoded opcodes; variable-reference operands use their encoded variable number, while value operands are read through execution state.
- 2026-10-28T14:04Z [CODE] Task 9 object support handles version-specific entries and property encodings; attribute 48 accesses in V4+ are safely ignored to accommodate the documented Sherlock bug.

## [PROGRESS]
- 2026-10-28T02:34Z [TOOL] Repository has no solution, `TASKS.md`, or prior continuity file; existing `.gitignore`, `AGENTS.md`, and `specs/` are untracked and are included in Task 1.
- 2026-10-28T02:49Z [TOOL] Created the Visual Studio solution, Avalonia desktop shell, core and xUnit projects, central package versions, editor configuration, and three-OS CI workflow. Restore, Release build (zero warnings/errors), xUnit (1 passed), and formatting verification passed.
- 2026-10-28T02:53Z [TOOL] PR #2 Windows CI reported ENDOFLINE errors because checkout produced CRLF for C# files while `.editorconfig` requires LF; added `.gitattributes` to enforce LF checkout for `*.cs`. Windows rerun pending.
- 2026-10-28T03:03Z [TOOL] Started Task 3 after PR #2 merged. Reviewed Z-machine Standard 1.1 sections 1.1, 1.2, 11.1, 14 verify, and Appendix B for file limits, header fields, scaled length, checksum scope, and legacy V3 behavior.
- 2026-10-28T03:07Z [TOOL] Implemented `StoryFile.Load/Parse`, immutable `StoryHeader`, version-scaled lengths, checksum validation, dynamic/static/high memory checks, V6 main-routine resolution, and 27 synthetic-fixture tests. Local-only probe successfully loaded all 37 available story files without adding story dependencies to CI.
- 2026-10-28T03:10Z [TOOL] Supersedes prior test count: expanded `StoryHeader` to expose version-gated display/interpreter/address fields and boundary coverage; formatting, Release build (0 warnings/errors), and all 47 xUnit tests pass. Repeated local probe loaded all 37/37 story files.
- 2026-10-28T03:11Z [TOOL] Supersedes prior Task 3 verification: after adding tests for maximum file lengths, empty high memory, and V6 packed address zero with a nonzero offset, formatting and Release build pass and all 49 xUnit tests pass. Final local probe loaded 37/37 story files.
- 2026-10-28T03:12Z [TOOL] Opened PR #3 for Task 3 after local validation; cross-platform CI is pending.
- 2026-10-28T03:19Z [TOOL] Started the docs-only Story File Info task-list update on `feature/30-story-file-info`; reverted all feature code after the user clarified implementation should wait.
- 2026-10-28T03:33Z [TOOL] Added the Glulx placeholder under Future Tasks on `docs/future-glulx-support`; Tasks 1–65 remain numbered as before.
- 2026-10-28T03:41Z [TOOL] Updated Task 27 for headless route replay, added Task 66 for the separate text-and-keyboard console host, and updated the Glulx prerequisite on `docs/headless-replay-terminal-play`.
- 2026-10-28T03:47Z [TOOL] Moved terminal play to Future Tasks ahead of Glulx; numbered roadmap remains Tasks 1–65 on `docs/move-terminal-play-to-future`.
- 2026-10-28T03:59Z [TOOL] Implemented `ZMachineMemory` and V1–V8 address translation with 31 new xUnit cases; all 80 tests pass, Release build has zero warnings/errors, format verification passes, and the local Zork I memory smoke check succeeds.
- 2026-10-28T04:01Z [TOOL] Opened PR #8 for Task 4 at https://github.com/porcellian/LeafpileGPT/pull/8; cross-platform CI is queued.
- 2026-10-28T04:12Z [TOOL] Task 5 adds a decoder and complete 119-opcode metadata table; formatting verification, Release build (0 warnings/errors), and all 106 xUnit tests pass locally.
- 2026-10-28T04:14Z [TOOL] Opened PR #9 for Task 5 at https://github.com/porcellian/LeafpileGPT/pull/9; waiting for review and merge before Task 6.
- 2026-10-28T04:26Z [TOOL] Task 6 adds execution state, routine frames, variables, catch/throw, and V6 stacks. Formatting verification, Release build (0 warnings/errors), and all 151 tests pass locally.
- 2026-10-28T04:25Z [TOOL] Opened PR #10 for Task 6 at https://github.com/porcellian/LeafpileGPT/pull/10; waiting for review and merge before Task 7.
- 2026-10-28T04:35Z [USER] Supersedes prior PR #10 status: PR #10 merged; Task 7 is active on `feature/7-arithmetic-vm-operations`.
- 2026-10-28T04:35Z [TOOL] Task 7 implements arithmetic, comparisons, shifts, random modes, variable/array/table access, control flow, routine and stack opcodes, and fail-fast dispatch for deferred opcodes. Formatting and Release build pass; 177 xUnit tests pass.
- 2026-10-28T04:38Z [TOOL] Final Task 7 review confirmed division-by-zero halts execution, unsupported opcodes preserve the faulting PC, and generated tests cover these failure paths. Task checkbox and spec coverage matrix are updated; commit and PR #11 remain.
- 2026-10-28T04:40Z [TOOL] Task 7 committed as `9c91d62`, pushed to `feature/7-arithmetic-vm-operations`, and opened as PR #11 at https://github.com/porcellian/LeafpileGPT/pull/11. Await review and merge before Task 8.
- 2026-10-28T04:42Z [USER] Supersedes prior PR #11 status: PR #11 is merged. Task 8 is active on `feature/8-text-character-encoding` from updated main.
- 2026-10-28T04:50Z [TOOL] Task 8 adds V1–V8 packed text decoding, abbreviations, alphabet shifts/custom tables, ZSCII/Unicode maps, and fixed-width dictionary encoding. Formatting and Release build pass; all 220 xUnit tests pass locally.
- 2026-10-28T04:52Z [TOOL] Supersedes prior Task 8 verification: added V6/V7 packed-string offsets, V8 scale, additional V4–V8 shift coverage, and invalid abbreviation-table checks. Formatting and Release build pass with zero warnings/errors; all 231 tests pass. Task 8 review and PR creation remain.
- 2026-10-28T14:04Z [TOOL] Task 9 implements the object/property model and associated opcodes; formatting and Release build pass with zero warnings/errors, and all 284 generated xUnit tests pass. Full diff review and PR #13 remain.
- 2026-10-28T14:07Z [TOOL] Supersedes prior Task 9 review status: full staged diff reviewed; commit `09cd324` pushed and PR #13 opened. Windows, macOS, and Ubuntu CI jobs are queued. Wait for review/merge before Task 10.

## [DISCOVERIES]
- 2026-10-28T02:34Z [CODE] `.gitignore` excludes `/stories/*`, `/reference/gui-design.png`, and `.agent/regression-routes/`; keep those local.
- 2026-10-28T02:42Z [TOOL] Avalonia template package installation was blocked by write access to the user-wide template cache; equivalent minimal project files were authored in the repository. NuGet restore succeeded with elevated network access.
- 2026-10-28T02:49Z [TOOL] .NET 10's `dotnet test` must select Microsoft.Testing.Platform for xUnit v3 4.x; `global.json` now selects MTP and the test project builds as an executable.
- 2026-10-28T02:53Z [TOOL] Windows checkout line endings can differ from the contributor's local worktree; `.gitattributes` now enforces the formatting policy at checkout.
- 2026-10-28T03:07Z [TOOL] The Z-machine standard permits dictionary tables in static memory; loader validates the dictionary header against the full story range while requiring globals and object defaults to remain in dynamic memory.
- 2026-10-28T03:10Z [TOOL] Header file-length scales are 2 bytes for V1–V3, 4 for V4–V5, and 8 for V6–V8; maximum encoded lengths and missing legacy V3 length/checksum cases have generated tests.
- 2026-10-28T03:11Z [TOOL] An empty high-memory region at EOF is valid, as is V6 packed main address zero when the routine offset resolves it into the story; both cases have regression tests.
- 2026-10-28T04:11Z [CODE] `call_vs2` and `call_vn2` are the only instructions using a second operand-type byte; omitted types must remain omitted across both bytes.
- 2026-10-28T04:13Z [CODE] Inline Z-text termination is bit 15 of each word, the high bit of its first byte; the second byte's high bit does not terminate the string.
- 2026-10-28T04:20Z [CODE] V6 user-stack word slots grow downward from the last slot: push writes at `table + 2 * spareSlots`, then decrements spare slots; pull increments first, then reads that slot. The standard deliberately does not require an underflow guard.
- 2026-10-28T04:35Z [CODE] je is the one 2OP opcode that accepts two to four operands; metadata was corrected so decoder and executor accept all of its comparison operands.
- 2026-10-28T04:38Z [CODE] Division by zero halts the execution state; unsupported-opcode dispatch restores the faulting PC before raising a contextual exception.
- 2026-10-28T04:50Z [CODE] Abbreviation markers select one of three 32-entry groups; Unicode translation table address is extension word 3. The supplied PDF's V1 A2 row includes a leading space before digits.
- 2026-10-28T04:52Z [TOOL] Reviewed the supplied PDF page image for the V1 A2 alphabet row; the leading space is present, correcting an ambiguity in extracted text.
- 2026-10-28T14:04Z [CODE] V4+ `get_prop_len` identifies extended properties from the byte immediately before data (bit 7 set), then reads the first size byte two positions back; extended length zero means 64 bytes.

## [OUTCOMES]
- 2026-10-28T02:49Z [TOOL] Task 1 baseline was merged in PR #1. Task 2 scaffold and local checks are complete; PR #2 has not yet been created.
- 2026-10-28T02:51Z [TOOL] Supersedes prior Task 2 PR status: Task 2 is committed as `610d0e6` with an additional continuity update, and PR #2 is open for review. Do not start Task 3 before the user merges PR #2.
- 2026-10-28T02:53Z [TOOL] PR #2 received a follow-up LF checkout fix; rerun CI and keep the PR open for review. Do not start Task 3 before merge.
- 2026-10-28T02:58Z [TOOL] Supersedes prior CI-pending status: after commit `4324671`, formatting, build, and tests passed on Windows, macOS, and Linux. PR #2 remains open for user review and merge.
- 2026-10-28T03:07Z [TOOL] PR #2 was merged. Task 3 implementation on `feature/3-story-file-validation` passes formatting, Release build (0 warnings/errors), 27 xUnit tests, and a local-only probe of 37 story files. Task 3 awaits PR review; do not begin Task 4 before merge.
- 2026-10-28T03:10Z [TOOL] Supersedes prior Task 3 test count; implementation now passes 47 xUnit tests and the local probe remains 37/37. Task 3 awaits PR review; do not begin Task 4 before merge.
- 2026-10-28T03:11Z [TOOL] Supersedes prior Task 3 verification: 49 xUnit tests and formatting/build pass; the 37-file local probe passes. Task 3 awaits PR creation/review; do not begin Task 4 before merge.
- 2026-10-28T03:12Z [TOOL] Supersedes prior PR state: PR #3 is open for review after commit `70fae26`; do not start Task 4 before merge.
- 2026-10-28T03:19Z [TOOL] Supersedes prior PR #3 state: PR #3 merged as `bb84a63`. The Story File Info task is documented but remains unchecked and unimplemented.
- 2026-10-28T03:33Z [TOOL] PR #4 merged as `a1410f5`; the Glulx future task is now documented but remains unchecked pending the supplied specs and a later spec-derived breakdown.
- 2026-10-28T03:41Z [TOOL] PR #5 merged as `8108953`; headless replay and terminal play are roadmap-only changes on a new docs branch.
- 2026-10-28T03:47Z [TOOL] PR #6 merged as `98911f0`. Terminal play is an unnumbered future item; it does not extend the 1–65 numbered roadmap.
- 2026-10-28T03:59Z [TOOL] Task 4 implementation is complete locally and ready for PR; no completed story walkthroughs exist to replay.
- 2026-10-28T04:01Z [TOOL] Supersedes prior Task 4 state: PR #8 is open for review; do not begin Task 5 until it is merged.
- 2026-10-28T04:14Z [TOOL] Supersedes prior Task 4 PR status: PR #8 is merged. Task 5 is committed as `6efb985`, with PR #9 open for review. Do not begin Task 6 until the user confirms merge.
- 2026-10-28T04:20Z [USER] Supersedes prior Task 5 PR status: PR #9 is merged as `0913bf7`. Task 6 is implemented on `feature/6-execution-state-calls`; final full review, commit, and PR creation remain.
- 2026-10-28T04:25Z [TOOL] Supersedes prior Task 6 status: committed as `c0f1c36`; PR #10 is open for review. Do not begin Task 7 until the user confirms merge.
- 2026-10-28T04:35Z [TOOL] Supersedes prior Task 6 PR status: PR #10 merged. Task 7 is implemented and checked locally; full diff review, commit, and PR #11 remain.
- 2026-10-28T04:40Z [TOOL] Supersedes prior Task 7 status: PR #11 is open for review after commit `9c91d62`; do not begin Task 8 until the user confirms merge.
- 2026-10-28T04:42Z [USER] Supersedes prior PR #11 status: PR #11 merged. Task 8 is active on `feature/8-text-character-encoding`.
- 2026-10-28T04:50Z [TOOL] Task 8 implementation and generated tests are locally complete; documentation review, commit, and PR #12 remain.
- 2026-10-28T04:52Z [TOOL] Task 8 full diff review and local checks are complete; 231 generated xUnit tests pass. Awaiting conventional commit, push, and PR #12.
- 2026-10-28T04:54Z [TOOL] Task 8 committed as `eefca70`, pushed to `feature/8-text-character-encoding`, and opened as PR #12 at https://github.com/porcellian/LeafpileGPT/pull/12. Await review and merge before Task 9.
- 2026-10-28T04:55Z [TOOL] PR #12 CI passed on macOS, Ubuntu, and Windows. GitHub annotations only report existing workflow notices for Node.js 20 action deprecation and ubuntu-latest image migration; no task build/test warnings or failures.

- 2026-10-28T14:04Z [TOOL] Supersedes prior Task 8 PR status: PR #12 is merged to main. Task 9 checks and review are in progress; no Task 9 commit or PR exists yet.
- 2026-10-28T14:07Z [TOOL] Task 9 is committed as `09cd324` on `feature/9-objects-and-properties`; PR #13 is open at https://github.com/porcellian/LeafpileGPT/pull/13. CI is queued; stop for user review and merge before Task 10.

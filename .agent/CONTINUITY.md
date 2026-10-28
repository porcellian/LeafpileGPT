# LeafpileGPT Continuity

## [PLANS]
- 2026-10-28T02:34Z [USER] Implement the approved V1–V8 C# interpreter/Avalonia plan in one task branch and PR at a time; stop for user review/merge after each PR.
- 2026-10-28T02:49Z [TOOL] Task 1 PR #1 merged; Task 2 is complete locally on `feature/2-solution-build-pipeline` and awaiting PR creation/review.
- 2026-10-28T02:51Z [TOOL] Supersedes prior Task 2 status: PR #2 is open at https://github.com/porcellian/LeafpileGPT/pull/2; CI is running.
- 2026-10-28T03:03Z [TOOL] PR #2 merged; Task 3 is in progress on `feature/3-story-file-validation`.
- 2026-10-28T03:12Z [TOOL] Task 3 is committed as `70fae26`; PR #3 is open at https://github.com/porcellian/LeafpileGPT/pull/3. Await user review/merge before Task 4.

## [DECISIONS]
- 2026-10-28T02:34Z [USER] Target .NET 10 LTS and Avalonia 12; xUnit; Windows/macOS/Linux.
- 2026-10-28T02:34Z [USER] Keep story/manual/hint files, routes, and raw traces local; commit original walkthrough PDFs. Use four runtime error policies, default first-per-type; logs omit player commands/story output.
- 2026-10-28T02:42Z [TOOL] Central package versions pin Avalonia 12.1.4 and xUnit v3 4.0.2; SDK selection starts at 10.0.100 and rolls forward within the .NET 10 feature band.

## [PROGRESS]
- 2026-10-28T02:34Z [TOOL] Repository has no solution, `TASKS.md`, or prior continuity file; existing `.gitignore`, `AGENTS.md`, and `specs/` are untracked and are included in Task 1.
- 2026-10-28T02:49Z [TOOL] Created the Visual Studio solution, Avalonia desktop shell, core and xUnit projects, central package versions, editor configuration, and three-OS CI workflow. Restore, Release build (zero warnings/errors), xUnit (1 passed), and formatting verification passed.
- 2026-10-28T02:53Z [TOOL] PR #2 Windows CI reported ENDOFLINE errors because checkout produced CRLF for C# files while `.editorconfig` requires LF; added `.gitattributes` to enforce LF checkout for `*.cs`. Windows rerun pending.
- 2026-10-28T03:03Z [TOOL] Started Task 3 after PR #2 merged. Reviewed Z-machine Standard 1.1 sections 1.1, 1.2, 11.1, 14 verify, and Appendix B for file limits, header fields, scaled length, checksum scope, and legacy V3 behavior.
- 2026-10-28T03:07Z [TOOL] Implemented `StoryFile.Load/Parse`, immutable `StoryHeader`, version-scaled lengths, checksum validation, dynamic/static/high memory checks, V6 main-routine resolution, and 27 synthetic-fixture tests. Local-only probe successfully loaded all 37 available story files without adding story dependencies to CI.
- 2026-10-28T03:10Z [TOOL] Supersedes prior test count: expanded `StoryHeader` to expose version-gated display/interpreter/address fields and boundary coverage; formatting, Release build (0 warnings/errors), and all 47 xUnit tests pass. Repeated local probe loaded all 37/37 story files.
- 2026-10-28T03:11Z [TOOL] Supersedes prior Task 3 verification: after adding tests for maximum file lengths, empty high memory, and V6 packed address zero with a nonzero offset, formatting and Release build pass and all 49 xUnit tests pass. Final local probe loaded 37/37 story files.
- 2026-10-28T03:12Z [TOOL] Opened PR #3 for Task 3 after local validation; cross-platform CI is pending.

## [DISCOVERIES]
- 2026-10-28T02:34Z [CODE] `.gitignore` excludes `/stories/*`, `/reference/gui-design.png`, and `.agent/regression-routes/`; keep those local.
- 2026-10-28T02:42Z [TOOL] Avalonia template package installation was blocked by write access to the user-wide template cache; equivalent minimal project files were authored in the repository. NuGet restore succeeded with elevated network access.
- 2026-10-28T02:49Z [TOOL] .NET 10's `dotnet test` must select Microsoft.Testing.Platform for xUnit v3 4.x; `global.json` now selects MTP and the test project builds as an executable.
- 2026-10-28T02:53Z [TOOL] Windows checkout line endings can differ from the contributor's local worktree; `.gitattributes` now enforces the formatting policy at checkout.
- 2026-10-28T03:07Z [TOOL] The Z-machine standard permits dictionary tables in static memory; loader validates the dictionary header against the full story range while requiring globals and object defaults to remain in dynamic memory.
- 2026-10-28T03:10Z [TOOL] Header file-length scales are 2 bytes for V1–V3, 4 for V4–V5, and 8 for V6–V8; maximum encoded lengths and missing legacy V3 length/checksum cases have generated tests.
- 2026-10-28T03:11Z [TOOL] An empty high-memory region at EOF is valid, as is V6 packed main address zero when the routine offset resolves it into the story; both cases have regression tests.

## [OUTCOMES]
- 2026-10-28T02:49Z [TOOL] Task 1 baseline was merged in PR #1. Task 2 scaffold and local checks are complete; PR #2 has not yet been created.
- 2026-10-28T02:51Z [TOOL] Supersedes prior Task 2 PR status: Task 2 is committed as `610d0e6` with an additional continuity update, and PR #2 is open for review. Do not start Task 3 before the user merges PR #2.
- 2026-10-28T02:53Z [TOOL] PR #2 received a follow-up LF checkout fix; rerun CI and keep the PR open for review. Do not start Task 3 before merge.
- 2026-10-28T02:58Z [TOOL] Supersedes prior CI-pending status: after commit `4324671`, formatting, build, and tests passed on Windows, macOS, and Linux. PR #2 remains open for user review and merge.
- 2026-10-28T03:07Z [TOOL] PR #2 was merged. Task 3 implementation on `feature/3-story-file-validation` passes formatting, Release build (0 warnings/errors), 27 xUnit tests, and a local-only probe of 37 story files. Task 3 awaits PR review; do not begin Task 4 before merge.
- 2026-10-28T03:10Z [TOOL] Supersedes prior Task 3 test count; implementation now passes 47 xUnit tests and the local probe remains 37/37. Task 3 awaits PR review; do not begin Task 4 before merge.
- 2026-10-28T03:11Z [TOOL] Supersedes prior Task 3 verification: 49 xUnit tests and formatting/build pass; the 37-file local probe passes. Task 3 awaits PR creation/review; do not begin Task 4 before merge.
- 2026-10-28T03:12Z [TOOL] Supersedes prior PR state: PR #3 is open for review after commit `70fae26`; do not start Task 4 before merge.

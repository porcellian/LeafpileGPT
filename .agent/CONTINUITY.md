# LeafpileGPT Continuity

## [PLANS]
- 2026-10-28T02:34Z [USER] Implement the approved V1–V8 C# interpreter/Avalonia plan in one task branch and PR at a time; stop for user review/merge after each PR.
- 2026-10-28T02:49Z [TOOL] Task 1 PR #1 merged; Task 2 is complete locally on `feature/2-solution-build-pipeline` and awaiting PR creation/review.

## [DECISIONS]
- 2026-10-28T02:34Z [USER] Target .NET 10 LTS and Avalonia 12; xUnit; Windows/macOS/Linux.
- 2026-10-28T02:34Z [USER] Keep story/manual/hint files, routes, and raw traces local; commit original walkthrough PDFs. Use four runtime error policies, default first-per-type; logs omit player commands/story output.
- 2026-10-28T02:42Z [TOOL] Central package versions pin Avalonia 12.1.4 and xUnit v3 4.0.2; SDK selection starts at 10.0.100 and rolls forward within the .NET 10 feature band.

## [PROGRESS]
- 2026-10-28T02:34Z [TOOL] Repository has no solution, `TASKS.md`, or prior continuity file; existing `.gitignore`, `AGENTS.md`, and `specs/` are untracked and are included in Task 1.
- 2026-10-28T02:49Z [TOOL] Created the Visual Studio solution, Avalonia desktop shell, core and xUnit projects, central package versions, editor configuration, and three-OS CI workflow. Restore, Release build (zero warnings/errors), xUnit (1 passed), and formatting verification passed.

## [DISCOVERIES]
- 2026-10-28T02:34Z [CODE] `.gitignore` excludes `/stories/*`, `/reference/gui-design.png`, and `.agent/regression-routes/`; keep those local.
- 2026-10-28T02:42Z [TOOL] Avalonia template package installation was blocked by write access to the user-wide template cache; equivalent minimal project files were authored in the repository. NuGet restore succeeded with elevated network access.
- 2026-10-28T02:49Z [TOOL] .NET 10's `dotnet test` must select Microsoft.Testing.Platform for xUnit v3 4.x; `global.json` now selects MTP and the test project builds as an executable.

## [OUTCOMES]
- 2026-10-28T02:49Z [TOOL] Task 1 baseline was merged in PR #1. Task 2 scaffold and local checks are complete; PR #2 has not yet been created.

# LeafpileGPT

An experimental C# desktop Z-machine VM implemented entirely by ChatGPT. Based on the Z-machine Standards Document v1.1. The project targets Z-machine versions 1–8 and the Quetzal save-file and Blorb resource formats. Authored by GPT-6 Luna, cleaned up by a human.

## Projects

- `src/Leafpile.Core` contains the interpreter engine and format support.
- `src/Leafpile.Desktop` contains the Avalonia desktop application for macOS and
  Windows.
- `tests/Leafpile.Tests` contains xUnit tests. Tests use generated fixtures and
  do not require local story files.

The core currently supports story loading, bounded machine memory, arithmetic,
routine and stack state, and version-aware packed Z-text conversion. Text support
includes abbreviations, custom alphabets, dictionary-form encoding, ZSCII, and
story-specific Unicode translation tables. The instruction decoder handles all
four instruction forms, typed operands, version-specific opcode layouts, stores,
branches, and inline print strings. A control-flow executor now handles
conditional branches, routine calls and returns, stack-frame catch/throw,
versioned save/restore control flow through a host service, restart, verification,
and related routine opcodes. Arithmetic, object-table operations, and save-file
encoding are implemented in later tasks.

## Requirements

- .NET 10 SDK
- macOS or Windows for running the desktop application

## Build and test

```sh
dotnet restore LeafpileGPT.sln
dotnet build LeafpileGPT.sln --configuration Release --no-restore
dotnet test LeafpileGPT.sln --configuration Release --no-build
```

Run the desktop application with:

```sh
dotnet run --project src/Leafpile.Desktop
```

See [TASKS.md](TASKS.md) for implementation status and the per-task PR workflow.
Local story files under `stories/` are excluded from Git and are not needed by
builds or tests.

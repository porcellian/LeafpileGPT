# LeafpileGPT

LeafpileGPT is a C# desktop Z-machine interpreter. The project targets Z-machine
versions 1–8 and the Quetzal save-file and Blorb resource formats.

## Projects

- `src/Leafpile.Core` contains the interpreter engine and format support.
- `src/Leafpile.Desktop` contains the Avalonia desktop application for macOS and
  Windows.
- `tests/Leafpile.Tests` contains xUnit tests. Tests use generated fixtures and
  do not require local story files.

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

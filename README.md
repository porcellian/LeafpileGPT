# LeafpileGPT

LeafpileGPT is a cross-platform C# Z-machine interpreter and desktop application. The project targets Z-machine versions 1–8 and the Quetzal save-file and Blorb resource formats.

## Development

Requirements: .NET 10 SDK. Open `LeafpileGPT.sln` in Visual Studio or build from the repository root:

```sh
dotnet restore LeafpileGPT.sln
dotnet build LeafpileGPT.sln
dotnet test LeafpileGPT.sln
dotnet format LeafpileGPT.sln --verify-no-changes
```

Run the desktop shell with:

```sh
dotnet run --project src/LeafpileGPT.Desktop
```

The GitHub Actions workflow restores, formats, builds, and tests the solution on Windows, macOS, and Linux. Automated tests use generated or in-memory fixtures and do not require local story files.

## Per-user data

The core resolves user storage through .NET's `LocalApplicationData` special folder. It creates `LeafpileGPT/Save/` for Quetzal files and `LeafpileGPT/Preferences/` for versioned JSON preferences. Play Game, Save, and Restore remember separate last-used directories; Save and Restore start in the app's `Save/` folder. The desktop shell will connect these preferences to its file dialogs in Task 24.

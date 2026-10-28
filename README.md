# LeafpileGPT

LeafpileGPT is a cross-platform C# Z-machine interpreter and desktop application. The project targets Z-machine versions 1–8 and the Quetzal save-file and Blorb resource formats.

Authored by GPT-6 Luna, cleaned up by a human.

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

Open a story directly in the desktop app by passing its path (quote paths containing spaces):

```sh
dotnet run --project src/LeafpileGPT.Desktop -- stories/zork1.z3
```

The File menu also provides Play Game, Save, Restore, Restart, and Exit. The app remembers separate picker directories for story selection, saves, and restores. Standalone Z-code stories and Blorb containers with a Z-code executable can be loaded. The desktop display uses a blue story area, fixed-width Courier-style text, a location/score/moves strip, inline command input after a blinking `>` prompt, upward scrolling output, and a separate app-status bar. Task 26 connects live VM sessions to keyboard input, screen output and operations, Quetzal save/restore, Blorb picture resources, and transcript stream files under app-data `LeafpileGPT/Transcripts/`. Sound requests reach the desktop host and are reported in the status bar; the desktop currently has no audio playback backend.

The GitHub Actions workflow restores, formats, builds, and tests the solution on Windows, macOS, and Linux. Automated tests use generated or in-memory fixtures and do not require local story files.

## Per-user data

The core resolves user storage through .NET's `LocalApplicationData` special folder. It creates `LeafpileGPT/Save/` for Quetzal files and `LeafpileGPT/Preferences/` for versioned JSON preferences. Play Game, Save, and Restore remember separate last-used directories; Save and Restore start in the app's `Save/` folder.

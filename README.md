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

## Headless route replay

`LeafpileGPT.Replay` replays a JSON route through the same Core decoder and executor without initializing Avalonia. Route story paths are resolved relative to the route file. For example, save a local route as `.agent/regression-routes/zork1.json`:

```json
{
  "schemaVersion": 1,
  "storyPath": "../../stories/zork1.z3",
  "commands": ["open mailbox", "take leaflet", "read leaflet"],
  "keyPresses": [],
  "randomSeed": 1,
  "maximumInstructions": 1000000,
  "expectedCompletionStatus": "AwaitingInput"
}
```

Run it and optionally write one JSON object per executed instruction to a trace file:

```sh
dotnet run --project src/LeafpileGPT.Replay -- .agent/regression-routes/zork1.json --trace zork1.jsonl
```

Trace paths are resolved relative to the route file.

After adding more local route files, replay the entire directory in stable filename order:

```sh
dotnet run --project src/LeafpileGPT.Replay -- --all .agent/regression-routes
```

This emits one JSON result per route and returns a nonzero exit code if any route fails.

The command writes a JSON completion result to standard output, including captured story text, its SHA-256, final VM state hash, and an opcode inventory with occurrence counts, first execution sequence, and first PC. A route can require an expected completion status and/or output hash; a mismatch, VM fault, or instruction-limit stop returns a nonzero exit code. Routes and traces remain local and ignored; CI uses generated story fixtures instead.

## Per-user data

The core resolves user storage through .NET's `LocalApplicationData` special folder. It creates `LeafpileGPT/Save/` for Quetzal files and `LeafpileGPT/Preferences/` for versioned JSON preferences. Play Game, Save, and Restore remember separate last-used directories; Save and Restore start in the app's `Save/` folder.

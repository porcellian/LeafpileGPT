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
and related routine opcodes. It also executes signed and bitwise arithmetic,
indirect variable operations, memory and table access, random-number control,
and logical/arithmetic shifts. Object operations now support versioned object
tables, attributes, tree mutation, short names, and versioned property headers,
defaults, lookup, iteration, and updates. Dictionary search supports sorted
story dictionaries and unsorted user dictionaries; text parsing handles word
separators and versioned text/parse buffers. `tokenise`, `encode_text`,
`check_unicode`, and versioned `scan_table` execution are covered. The executor
supports host-provided line and character input, timed-input callbacks, terminating
characters, command input and recording streams, transcript and screen selection,
nested memory output streams, standardized suggested filenames, and streamed text
output. The V1–V5 screen model tracks versioned header dimensions, V1–V3 status
updates, split-window behavior, cursor positions, screen clearing, text styles,
V5 colors, and lower-window word wrapping and buffering. V6 adds eight pixel
windows, per-window geometry, cursor, style, color, font, margins, attributes and
properties, scrolling, screen buffering, and formatted output streams. Font 3 is
available for character graphics. The core dispatches sound effects through the
`IZMachineSound` host contract, including volume, repeats, stop/finish operations,
and completion routines; actual resource lookup and playback are supplied by a
host. Blorb containers expose indexed resources, validated metadata, image bytes,
dimensions, and scaling information through the loaded story. V6 draw and erase
operations are available to a display host. Sound capability bits are set or
cleared from host support. Mouse, menu, and transparency capabilities remain
unavailable. Versions 7 and 8 retain the V5 text-window model. Quetzal support writes compressed CMem
or optional UMem saves and restores either encoding for V1–V8. It validates story
identity and IFZS chunk bounds, restores versioned call frames and stack values,
resumes at the original save instruction's success continuation, and supports
ANNO and IntD chunks. `QuetzalSaveRestoreService` uses the core
`IZMachineSaveFileAccess` boundary for host-selected files and includes
in-memory `save_undo` / `restore_undo` support. The Avalonia desktop host now
provides Quetzal save and restore file pickers, story open and restart commands,
keyboard text and character input, renderer-neutral display updates, Blorb image
rendering, and runtime error reporting. It uses a Courier-style font with a blue
story area and gray V1–V3 status bar. Timed input, transcript and command files,
and sound playback remain unavailable in the desktop host and are reported via
the capability flags supported by the core.

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

To open a story file as the application starts, pass its path after `--`:

```sh
dotnet run --project src/Leafpile.Desktop -- stories/zork1.z3
```

Pass one standalone Z-code or Blorb story path. Relative paths are resolved
from the current working directory; quote paths containing spaces. Without a
path, the application starts normally and a story can be opened from the File
menu.

See [TASKS.md](TASKS.md) for implementation status and the per-task PR workflow.
Local story files under `stories/` are excluded from Git and are not needed by
builds or tests.

## Local story route replay

`tools/Leafpile.RouteReplay` replays a JSON command route against a local story,
checks story identity and score/location checkpoints, and reports the executed
opcode inventory. An optional JSONL trace records each instruction. Routes and
traces under `.agent/regression-routes/` are local regression data and stay out
of Git; automated tests use generated stories.

```sh
dotnet run --project tools/Leafpile.RouteReplay -c Release -- \
  stories/zork1.z3 .agent/regression-routes/zork1-maximum-score.json \
  .agent/regression-routes/zork1-opcode-trace.jsonl
```

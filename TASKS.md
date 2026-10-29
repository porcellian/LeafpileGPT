# LeafpileGPT Implementation Tasks

Each numbered task is a reviewable unit on its own branch, with one conventional commit and one pull request. After opening a PR, stop for review and merge before beginning the next task. Do not auto-merge. Every code task includes relevant xUnit coverage, formatting/build checks, and a full diff review. Track specification mapping in [SPEC-COVERAGE.md](SPEC-COVERAGE.md).

## Interpreter, desktop app, and diagnostics

- [x] **1. Establish project baseline and requirements checklist.** Track project guidance and supplied standards; create this task list, the spec-to-code-to-test matrix, and `.agent/CONTINUITY.md`.
- [x] **2. Create the Visual Studio solution and build pipeline.** Add a `.sln`, core, Avalonia desktop, and xUnit projects; configure formatting, builds, and tests on Windows, macOS, and Linux.
- [x] **3. Load and validate story files.** Parse and validate V1–V8 headers, lengths, checksums, memory limits, and malformed-file errors.
- [x] **4. Implement memory and address translation.** Support dynamic/static/high memory, protected writes, byte/word/packed addresses, and V1–V8 packed-address rules.
- [x] **5. Decode instructions.** Implement instruction forms, operands, branches, stores, inline text, and version-gated metadata for all standard opcodes.
- [x] **6. Implement execution state and calls.** Add stacks, locals, globals, call frames, argument counts, catch/throw, and V6 main-routine and user-stack behavior.
- [x] **7. Implement arithmetic and VM operations.** Add signed 16-bit arithmetic and comparisons, shifts, random modes, variable and array access, table copying/search, stack and routine operations, branches, returns, and fail-fast dispatch for opcodes assigned to later tasks.
- [x] **8. Implement text and character encoding.** Decode packed Z-text for V1–V8 with version-specific shifts, abbreviations, custom alphabets, ZSCII/Unicode translation tables, and bounded malformed-text handling; encode fixed-width dictionary words with version-specific padding and safe fallback for unrepresentable input.
- [x] **9. Implement objects and properties.** Support version-specific layouts, attributes, object trees, property tables, defaults, and related opcodes.
- [x] **10. Implement dictionaries and command parsing.** Add separators, sorted and unsorted dictionaries, tokenization, parse buffers, `encode_text`, and V1–V8 input-buffer behavior.
- [x] **11. Implement output streams.** Add screen, transcript, nested memory, and command-recording streams, buffering, stream/header synchronization, and standard text output opcodes.
- [x] **12. Implement keyboard input and timed reads.** Support asynchronous `read`/`read_char`, terminating characters, keyboard and command-file input streams, line editing and filtering, timed routine callbacks, and defined ZSCII input.
- [x] **13. Implement auxiliary file operations.** Read length-prefixed story filenames, normalize to safe uppercase 8.3 names, provide directory-rooted auxiliary file reads and writes, and distinguish missing files from reportable I/O failures.
- [x] **14. Implement V1–V5 screen behavior.** Model scrolling and MORE prompts, V3–V5 split windows and cursors, status lines, erase operations, text styles, V5 colors, and versioned display dimensions.
- [x] **15. Implement V6 windows and screen properties.** Add eight windows with independent cursors, geometry, margins, styles, colors, wrapping, scrolling, buffering, and readable/writable window properties.
- [x] **16. Implement fonts and character graphics.** Add fixed-pitch behavior, font capability reporting, Courier-style text, and the standard 8×8 character graphics font.
- [x] **17. Implement mouse, menus, and asynchronous events.** Add host-neutral mouse state and V6 mouse-window constraints, V6 menu creation/removal and input data, timed-input routine integration, V6 newline-countdown callbacks, sound effects and completion callbacks, and capability flags.
- [x] **18. Implement Blorb container parsing.** Parse IFF `FORM IFRS`, resource indexes, chunk bounds and padding, Z-code executable chunks, metadata, and resource descriptions; skip unknown chunks.
- [x] **19. Implement Blorb images and display hints.** Support PNG, JPEG, rectangles, picture lookup, scaling, palettes, adaptive palettes, frontispiece and release metadata, and V6 picture operations.
- [x] **20. Implement Blorb audio.** Support AIFF, Ogg Vorbis, MOD-family formats, legacy SONG resources, sample/music channels, volume, looping, and callbacks.
- [x] **21. Implement the Quetzal codec.** Read and write IFZS, IFhd, CMem, UMem, and Stks; handle V6 and non-V6 frames, dummy frames, PC rules, padding, annotations, IntD, and malformed or unknown chunks.
- [x] **22. Integrate save, restore, restart, and undo.** Apply version-specific save behavior, story identity checks, state restoration, preserved header flags, Quetzal files, and in-memory undo.
- [x] **23. Add app-data paths and persistent preferences.** Store saves under `LeafpileGPT/Save/` and preferences under `LeafpileGPT/Preferences/`. Persist separate last-used directories for Play Game, Save, and Restore; seed Save and Restore from the default app-data Save folder.
- [x] **24. Build app startup, command-line story launch, and File menu.** Accept `LeafpileGPT <story-path>` to open the story in the Avalonia GUI; handle paths with spaces and invalid files clearly. With no argument, start without a loaded game. Add File items in this order: Play Game..., a Save/Restore group with Save... and Restore..., then Restart and Exit.
- [x] **25. Build the game display.** Match the blue-and-white fixed-width reference; show location left and score/moves right below the menus; place the prompt at the lower left and scroll text upward; add a separate bottom app-status bar.
- [x] **26. Integrate the app, VM, and resources.** Connect story sessions to input, output, screen, transcript, saves, images, and sound requests for standalone and Blorb-packaged games. Report audio requests when no playback backend is available.
- [x] **27. Complete conformance and replay tooling.** Close the spec matrix; build deterministic route replay through the same VM in a headless host without starting Avalonia, capturing game output and a machine-readable completion result; build opcode tracing; add generated V1–V8 fixtures and xUnit coverage; configure cross-platform builds and local story checks that CI does not require.
- [x] **28. Add robust error handling and logging.** Implement four runtime error policies: suppress user-facing reports, report first instance per error type, report every occurrence, and stop on any error; default to first-per-type. Show detailed dialogs for blocking/fatal failures and recoverable warnings in the status bar and Tools > View Errors. Include copyable game identity, PC, opcode, exception, and stack details. Write structured JSONL logs to app-data `LeafpileGPT/Logs/`, rolling at 10 MB per file, retaining 30 days, and capping total storage at 100 MB. Omit player commands and story output from logs by default. Logging failure must not crash the game. A missing file for an in-game restore returns failure to the story as specified.
- [x] **29. Add the View Instructions debugging window.** Add Tools > View Instructions. Open a read-only, continuously updating, scrollable window beside the main window. Record from game start and group each user command with subsequent opcodes. Show sequence, PC, form, opcode, mnemonic, and useful decoded operands/results. Add Save Audit, Clear History, Previous, Current, and Next on one right-aligned toolbar row, with Previous/Current/Next in that order. Previous and Next navigate command groups; Current selects the active or latest group and resumes following live output. Clear History clears the active unsaved trace without changing game state or deleting saved audits. Save Audit opens a dialog defaulting to `LeafpileGPT/Audits/` and exports a readable PDF and structured JSONL with the selected base filename. Use compact session storage and a virtualized view.
- [x] **30. Add the Story File Info window.** Add Tools > Story File Info, disabled when no game is loaded. Open one read-only, resizable modeless window showing source identity and SHA-256, story version/release/serial, optional IFID and Blorb metadata, file size and checksum status, memory and execution layout, decoded version-specific header fields, original and current runtime header values, and the raw 64-byte header. Refresh the open window when the loaded story changes. Use an immutable snapshot interface and generated xUnit fixtures.

## Maximum-score story walkthroughs

For each game, use the story and same-basename manual/hint files supplied for that run. Play from a fresh game to maximum score and ending without deaths. Create an original, concise, professionally formatted PDF in `walkthroughs/` and commit it with the updated checkbox. Include an opcode table with form, opcode, mnemonic, total count, first execution sequence, and first PC. Save a deterministic route and machine-readable opcode trace under `.agent/regression-routes/`; keep these local and ignored. Record first sequence as a 1-based instruction execution index and PCs as hexadecimal byte addresses. If source code changes during a run, replay every previously completed maximum-score route from a fresh game before closing the task; verify score, ending, and deaths, and fix defects with regression tests.

- [ ] **31. Zork I: The Great Underground Empire**
- [ ] **32. Zork II: The Wizard of Frobozz**
- [ ] **33. Deadline**
- [ ] **34. Zork III: The Dungeon Master**
- [ ] **35. Starcross**
- [ ] **36. Suspended**
- [ ] **37. The Witness**
- [ ] **38. Planetfall**
- [ ] **39. Enchanter**
- [ ] **40. Infidel**
- [ ] **41. Sorcerer**
- [ ] **42. Seastalker**
- [ ] **43. Cutthroats**
- [ ] **44. The Hitchhiker's Guide to the Galaxy**
- [ ] **45. Suspect**
- [ ] **46. Wishbringer**
- [ ] **47. A Mind Forever Voyaging**
- [ ] **48. Spellbreaker**
- [ ] **49. Ballyhoo**
- [ ] **50. Trinity**
- [ ] **51. Leather Goddesses of Phobos**
- [ ] **52. Moonmist**
- [ ] **53. Hollywood Hijinx**
- [ ] **54. Bureaucracy**
- [ ] **55. Stationfall**
- [ ] **56. The Lurking Horror**
- [ ] **57. Nord and Bert Couldn't Make Head or Tail of It**
- [ ] **58. Plundered Hearts**
- [ ] **59. Beyond Zork**
- [ ] **60. Border Zone**
- [ ] **61. Sherlock: The Riddle of the Crown Jewels**
- [ ] **62. Zork Zero: The Revenge of Megaboz**
- [ ] **63. James Clavell's Shogun**
- [ ] **64. Journey: The Quest Begins**
- [ ] **65. Arthur: The Quest for Excalibur**

## Completion checks

- Add xUnit coverage with implementation tasks. Use generated deterministic fixtures in CI; CI must not depend on local story, manual, or hint files.
- Test CLI startup with valid paths, paths containing spaces, invalid/missing paths, and no argument. Test persisted Play Game, Save, and Restore directories across launches.
- Test all runtime error policies, dialog/status behavior, View Errors history, log rotation/retention, command-text omission, disk/logging failures, and in-game restore failure behavior.
- Test instruction grouping, sequence/PC accuracy, live updates, navigation, clear semantics, and PDF/JSONL exports. Verify tracing does not alter VM behavior.
- Build and test on Windows, macOS, and Linux; run applicable formatting, build, and xUnit checks for every code change.
- For every task, update its checkbox in the PR, review the entire diff, open the PR, then wait for review and merge.

## Future Tasks

- [ ] **Add a Story Hex Viewer.** Add Tools > Story Hex Viewer, disabled when no story is loaded, as a read-only, resizable window styled like Story File Info. Let players inspect the exact original story file, current runtime memory, and for Blorb launches the embedded Z-code executable and complete Blorb container. Display offsets, hexadecimal bytes, and ASCII text; support go-to-offset, byte search, and side-by-side original-story/runtime comparison with changed bytes highlighted. Virtualize rows and read bytes on demand so large sources remain responsive. Provide immutable byte snapshots or bounded read-only ranges, capturing runtime memory under the session lock; the UI must not read VM memory directly. Cover standalone and padded stories, Blorb sources, navigation/search, changed-byte comparison, story refresh, no-story menu state, and unchanged VM state with generated xUnit fixtures. Keep this as a future task; implementation is deferred.
- [ ] **Add terminal play mode.** Create a separate `LeafpileGPT.Cli` console host that shares the VM/session core, accepts a story path, reads player input from standard input, and writes game text to standard output without initializing Avalonia. Support text and keyboard play, paths with spaces, invalid-story errors, and clean end-of-input handling. Advertise text-only capabilities; graphics and audio remain available through the desktop app. Preserve the existing desktop command-line behavior that opens the GUI.
- [ ] **Support playable Glulx games.** After Tasks 1–65 are complete and the Glulx and Glk specifications are added to `reference/`, review them and define a spec-derived task breakdown. Implement Glulx as a separate VM engine in the shared app, with the Glk I/O needed to play games through Avalonia. Extend story detection and Blorb launch support as required by the specifications.

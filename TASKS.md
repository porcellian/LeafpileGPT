# LeafpileGPT Implementation Tasks

Each numbered task is a reviewable unit on its own branch, with one conventional commit and one pull request. After opening a PR, stop for review and merge before beginning the next task. Do not auto-merge. Every code task includes relevant xUnit coverage, formatting/build checks, and a full diff review. Track specification mapping in [SPEC-COVERAGE.md](SPEC-COVERAGE.md).

## Interpreter, desktop app, and diagnostics

- [x] **1. Establish project baseline and requirements checklist.** Track project guidance and supplied standards; create this task list, the spec-to-code-to-test matrix, and `.agent/CONTINUITY.md`.
- [x] **2. Create the Visual Studio solution and build pipeline.** Add a `.sln`, core, Avalonia desktop, and xUnit projects; configure formatting, builds, and tests on Windows, macOS, and Linux.
- [x] **3. Load and validate story files.** Parse and validate V1–V8 headers, lengths, checksums, memory limits, and malformed-file errors.
- [ ] **4. Implement memory and address translation.** Support dynamic/static/high memory, protected writes, byte/word/packed addresses, and V1–V8 packed-address rules.
- [ ] **5. Decode instructions.** Implement instruction forms, operands, branches, stores, inline text, and version-gated metadata for all standard opcodes.
- [ ] **6. Implement execution state and calls.** Add stacks, locals, globals, call frames, argument counts, catch/throw, and V6 main-routine and user-stack behavior.
- [ ] **7. Implement arithmetic and VM operations.** Cover signed arithmetic, shifts, comparisons, random modes, memory/table operations, control flow, and remaining standard opcodes.
- [ ] **8. Implement text and character encoding.** Add Z-characters, abbreviations, alphabets, ZSCII, Unicode tables, dictionary encoding, and safe invalid-character handling.
- [ ] **9. Implement objects and properties.** Support version-specific layouts, attributes, object trees, property tables, defaults, and related opcodes.
- [ ] **10. Implement dictionaries and command parsing.** Add separators, sorted and unsorted dictionaries, tokenization, parse buffers, and V1–V8 input-buffer behavior.
- [ ] **11. Implement output streams.** Add screen, transcript, nested memory, and command-recording streams, buffering, and stream/header synchronization.
- [ ] **12. Implement keyboard input and timed reads.** Support `read`, `read_char`, terminating characters, input streams, editing, timed callbacks, and defined ZSCII input.
- [ ] **13. Implement auxiliary file operations.** Add filename sanitization, supported external file operations, and specified error behavior.
- [ ] **14. Implement V1–V5 screen behavior.** Add scrolling, split windows, cursor and erase behavior, status lines, text styles, colors, dimensions, and MORE prompts.
- [ ] **15. Implement V6 windows and screen properties.** Add eight windows, independent cursors, geometry, margins, styles, colors, wrapping, scrolling, buffering, and window properties.
- [ ] **16. Implement fonts and character graphics.** Add fixed-pitch behavior, font capability reporting, Courier-style text, and character graphics.
- [ ] **17. Implement mouse, menus, and asynchronous events.** Add mouse support, V6 menus and mouse windows, timed-input routines, newline interrupts, and sound callbacks.
- [ ] **18. Implement Blorb container parsing.** Parse IFF `FORM IFRS`, resource indexes, chunk bounds and padding, Z-code executable chunks, metadata, and resource descriptions; skip unknown chunks.
- [ ] **19. Implement Blorb images and display hints.** Support PNG, JPEG, rectangles, picture lookup, scaling, palettes, adaptive palettes, frontispiece and release metadata, and V6 picture operations.
- [ ] **20. Implement Blorb audio.** Support AIFF, Ogg Vorbis, MOD-family formats, legacy SONG resources, sample/music channels, volume, looping, and callbacks.
- [ ] **21. Implement the Quetzal codec.** Read and write IFZS, IFhd, CMem, UMem, and Stks; handle V6 and non-V6 frames, dummy frames, PC rules, padding, annotations, IntD, and malformed or unknown chunks.
- [ ] **22. Integrate save, restore, restart, and undo.** Apply version-specific save behavior, story identity checks, state restoration, preserved header flags, Quetzal files, and in-memory undo.
- [ ] **23. Add app-data paths and persistent preferences.** Store saves under `LeafpileGPT/Save/` and preferences under `LeafpileGPT/Preferences/`. Persist separate last-used directories for Play Game, Save, and Restore; seed Save and Restore from the default app-data Save folder.
- [ ] **24. Build app startup, command-line story launch, and File menu.** Accept `LeafpileGPT <story-path>` to open the story in the Avalonia GUI; handle paths with spaces and invalid files clearly. With no argument, start without a loaded game. Add File items in this order: Play Game..., a Save/Restore group with Save... and Restore..., then Restart and Exit.
- [ ] **25. Build the game display.** Match the blue-and-white fixed-width reference; show location left and score/moves right below the menus; place the prompt at the lower left and scroll text upward; add a separate bottom app-status bar.
- [ ] **26. Integrate the app, VM, and resources.** Connect story sessions to input, output, screen, transcript, saves, images, and sound for standalone and Blorb-packaged games.
- [ ] **27. Complete conformance and replay tooling.** Close the spec matrix; build deterministic route replay and opcode tracing; add generated V1–V8 fixtures and xUnit coverage; configure cross-platform builds and local story checks that CI does not require.
- [ ] **28. Add robust error handling and logging.** Implement four runtime error policies: suppress user-facing reports, report first instance per error type, report every occurrence, and stop on any error; default to first-per-type. Show detailed dialogs for blocking/fatal failures and recoverable warnings in the status bar and Tools > View Errors. Include copyable game identity, PC, opcode, exception, and stack details. Write structured JSONL logs to app-data `LeafpileGPT/Logs/`, rolling at 10 MB per file, retaining 30 days, and capping total storage at 100 MB. Omit player commands and story output from logs by default. Logging failure must not crash the game. A missing file for an in-game restore returns failure to the story as specified.
- [ ] **29. Add the View Instructions debugging window.** Add Tools > View Instructions. Open a read-only, continuously updating, scrollable window beside the main window. Record from game start and group each user command with subsequent opcodes. Show sequence, PC, form, opcode, mnemonic, and useful decoded operands/results. Add Save Audit, Clear History, Previous, Current, and Next on one right-aligned toolbar row, with Previous/Current/Next in that order. Previous and Next navigate command groups; Current selects the active or latest group and resumes following live output. Clear History clears the active unsaved trace without changing game state or deleting saved audits. Save Audit opens a dialog defaulting to `LeafpileGPT/Audits/` and exports a readable PDF and structured JSONL with the selected base filename. Use compact session storage and a virtualized view.

## Maximum-score story walkthroughs

For each game, use the story and same-basename manual/hint files supplied for that run. Play from a fresh game to maximum score and ending without deaths. Create an original, concise, professionally formatted PDF in `walkthroughs/` and commit it with the updated checkbox. Include an opcode table with form, opcode, mnemonic, total count, first execution sequence, and first PC. Save a deterministic route and machine-readable opcode trace under `.agent/regression-routes/`; keep these local and ignored. Record first sequence as a 1-based instruction execution index and PCs as hexadecimal byte addresses. If source code changes during a run, replay every previously completed maximum-score route from a fresh game before closing the task; verify score, ending, and deaths, and fix defects with regression tests.

- [ ] **30. Zork I: The Great Underground Empire**
- [ ] **31. Zork II: The Wizard of Frobozz**
- [ ] **32. Deadline**
- [ ] **33. Zork III: The Dungeon Master**
- [ ] **34. Starcross**
- [ ] **35. Suspended**
- [ ] **36. The Witness**
- [ ] **37. Planetfall**
- [ ] **38. Enchanter**
- [ ] **39. Infidel**
- [ ] **40. Sorcerer**
- [ ] **41. Seastalker**
- [ ] **42. Cutthroats**
- [ ] **43. The Hitchhiker's Guide to the Galaxy**
- [ ] **44. Suspect**
- [ ] **45. Wishbringer**
- [ ] **46. A Mind Forever Voyaging**
- [ ] **47. Spellbreaker**
- [ ] **48. Ballyhoo**
- [ ] **49. Trinity**
- [ ] **50. Leather Goddesses of Phobos**
- [ ] **51. Moonmist**
- [ ] **52. Hollywood Hijinx**
- [ ] **53. Bureaucracy**
- [ ] **54. Stationfall**
- [ ] **55. The Lurking Horror**
- [ ] **56. Nord and Bert Couldn't Make Head or Tail of It**
- [ ] **57. Plundered Hearts**
- [ ] **58. Beyond Zork**
- [ ] **59. Border Zone**
- [ ] **60. Sherlock: The Riddle of the Crown Jewels**
- [ ] **61. Zork Zero: The Revenge of Megaboz**
- [ ] **62. James Clavell's Shogun**
- [ ] **63. Journey: The Quest Begins**
- [ ] **64. Arthur: The Quest for Excalibur**

## Completion checks

- Add xUnit coverage with implementation tasks. Use generated deterministic fixtures in CI; CI must not depend on local story, manual, or hint files.
- Test CLI startup with valid paths, paths containing spaces, invalid/missing paths, and no argument. Test persisted Play Game, Save, and Restore directories across launches.
- Test all runtime error policies, dialog/status behavior, View Errors history, log rotation/retention, command-text omission, disk/logging failures, and in-game restore failure behavior.
- Test instruction grouping, sequence/PC accuracy, live updates, navigation, clear semantics, and PDF/JSONL exports. Verify tracing does not alter VM behavior.
- Build and test on Windows, macOS, and Linux; run applicable formatting, build, and xUnit checks for every code change.
- For every task, update its checkbox in the PR, review the entire diff, open the PR, then wait for review and merge.

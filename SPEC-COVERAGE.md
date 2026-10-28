# Specification Coverage Matrix

This matrix maps the supplied standards to planned implementation areas and acceptance tests. Update implementation and test references as tasks land. No standard requirement is complete until its behavior and edge cases have automated coverage or a documented manual acceptance test.

## Z-Machine Standard 1.1

| Standard area | Implementation tasks | Coverage / acceptance evidence |
|---|---:|---|
| 1. Memory map and addresses | 3–4 | `StoryFile` validates dynamic/static/high boundaries, story size limits, and table bounds. `ZMachineMemory` classifies regions, bounds game-visible and VM-internal reads, and protects static/high memory from writes. `ZMachineAddressing` converts byte, word, and V1–V8 packed addresses, including separate V6/V7 routine and string offsets. Generated xUnit fixtures cover boundaries, overlap, overflow, and write protection. |
| 2. Numbers and arithmetic | 7 | ZMachineOpcodeExecutor applies signed 16-bit division, remainder, and comparisons; wraps arithmetic to 16 bits; rejects divide-by-zero and invalid shifts; and handles random, seeded, and random-mode reset behavior. Generated xUnit cases cover signed results, overflow, bounds, and repeatable sequences. |
| 3. Text and character encoding | 8, 10 | `ZMachineTextCodec` decodes packed Z-text and inline words, V1–V2 shifts/locks, V3+ abbreviations, ZSCII escapes, and story-defined alphabet tables. `ZsciiCodec` maps default/custom Unicode tables, version-specific output characters, supported input codes, and invalid codes safely. Dictionary words are lowercased, encoded without abbreviations, padded/truncated to six or nine Z-characters, and packed with the terminal bit. `ZMachineDictionary` and `ZMachineTextBuffer` use that encoding for lookup, lexical analysis, and V1–V8 input buffers. Generated xUnit fixtures cover all versions, malformed bounds/terminators, custom tables, abbreviation expansion, Unicode escapes, tokenization, lookup order, and truncation. |
| 4. Instruction encoding | 5–7 | `ZMachineInstructionDecoder` parses long, short, variable, and extended forms; large/small/variable operands; the double type byte for `call_vs2`/`call_vn2`; store and branch data (including both on one instruction); signed branch offsets and return offsets; and inline string boundaries. `ZMachineOpcodeTable` describes all 119 standard opcodes with version-specific aliases, operand counts, store/branch flags, and inline-text metadata. Generated xUnit fixtures cover V1–V8, truncation, invalid operand ordering, and illegal opcode/version combinations. |
| 5. Routine encoding | 3–6 | `ZMachineExecutionState` reads routine headers and V1–V4 local defaults, zero-initializes V5+ locals, copies arguments into locals, and starts V6 from its packed main routine. Generated fixtures cover packed calls, local-header bounds, and main-routine offsets. |
| 6. Game state, storage, routine calls | 6, 22 | `ZMachineExecutionState` provides bounded private evaluation/call stacks, local and global variables, version-specific argument limits, return-value storage, per-routine stack boundaries, catch/throw unwinding, and V6 user stacks. Generated xUnit tests cover overflow/underflow, zero-address calls, argument counts, stack isolation, and user-stack spare-slot behavior. Task 22 adds persistent save/restart semantics. |
| 7. Output streams and file handling | 11–13, 22 | `ZMachineOutputStreams` routes screen and transcript output, supports V3+ selection with Flags 2 synchronization, nested stream 3 memory tables (up to depth 16), stream 4 command and key recording, V1–V5 input echo, buffering mode, ZSCII/Unicode conversion, and V6 stream-3 width reporting. Task 13 adds length-prefixed filename decoding, portable 8.3 sanitization, and a host-rooted auxiliary file store; missing reads return null while other I/O failures carry operation/name context. Task 22 connects the store to save/restore. Generated V1–V8 fixtures cover routing, selection, nested tables, bounds, flags, buffer mode, input echo, and output opcodes; generated filesystem tests cover filename edge cases, containment, missing files, and I/O failures. |
| 8. Screen model | 14–16, 25 | V1–V5 windows/status; V6 eight-window model, text styles, colors, geometry, MORE behavior. |
| 9. Sound effects | 17, 20 | Capability flags, bleeps, samples/music channels, repeats, callbacks, prepare/stop/finish. |
| 10. Input streams and devices | 12, 17 | `ZMachineInputHandler` handles asynchronous line and key reads through a host-neutral input provider, stream 0 keyboard and stream 1 command selection, editing/filtering through defined ZSCII, V1–V3 status-line requests, V5+ terminating-character tables and wildcards, input echo/command recording, and periodic timed routines that can resume input or terminate with code 0. Generated V1–V8 fixtures cover buffer layouts, append behavior, parse suppression, termination, defined key codes, callback cadence, timeout clearing, and opcode dispatch. Task 17 adds mouse and menu events. |
| 11. Header | 3, 14–17, 23, 28 | `StoryHeader` parses V1–V8 base fields, version-scaled file length, checksum, V6 main-routine address, and memory bounds; xUnit fixtures cover all versions, legacy V3 omissions, truncation, and checksum mismatch. Later tasks add initialization, capabilities, extension-table behavior, and Rst fields. |
| 12. Object table | 9 | `ZMachineObjectTable` reads V1–V3 byte links/32 attributes and V4+ word links/48 attributes, applies the Sherlock attribute-48 compatibility behavior, inserts/removes objects, decodes ordered property lists and short names, and reads/writes properties and version-specific defaults. Generated V1–V8 xUnit fixtures cover all object/property opcode paths, one/two/extended (including 64-byte) property data, malformed ranges/order, default values, tree mutation, and attribute bit order. |
| 13. Dictionary and lexical analysis | 10 | `ZMachineDictionary` validates separators and entry widths, searches sorted tables with binary search and negative-count user tables linearly, splits spaces and standalone separators, and writes bounded four-byte parse records. `ZMachineTextBuffer` reads/writes the distinct V1–V4 and V5–V8 text-buffer forms, including lowercase input, truncation, retained partial input, and V5+ terminator results. Generated fixtures cover every story version, dictionary order modes, parse capacities and unknown-word preservation, input continuation, and malformed ranges. |
| 14. Complete opcode table | 5–12, 14–17 | `ZMachineOpcodeTable` and decoder provide version/form metadata for all standard opcodes. Tasks 7 and 9–12 dispatch arithmetic, comparison, variable, array/table, object/property, dictionary, routine, stack, control-flow, text output, and asynchronous input operations; later tasks add screen and resource opcode behavior. I/O opcodes route through VM-owned stream and input abstractions. Unsupported operations fail with opcode and PC context. xUnit covers execution effects, stores, branches, and defined errors. |
| 15. Opcode dictionary | 5–17 | Per-opcode semantics cross-checked against the standard descriptions. |
| 16. Font 3 and character graphics | 16 | Fixed-pitch 8x8 glyph behavior and reported capability. |
| Appendix A. Errors and debugging | 28 | Four configurable error policies, context-rich diagnostics, stack trace, invalid control-character filtering, responsive execution. |
| Appendix B. Conventional header contents | 3, 23, 28 | Header compatibility and interpreter-owned fields. |
| Appendix C. Resources | 18–20 | Resource availability and supported media behavior. |

### Opcode inventory

The decoder and tests must account for every standard opcode, including version-specific reuse of opcode slots:

- **2OP:** `je`, `jl`, `jg`, `dec_chk`, `inc_chk`, `jin`, `test`, `or`, `and`, `test_attr`, `set_attr`, `clear_attr`, `store`, `insert_obj`, `loadw`, `loadb`, `get_prop`, `get_prop_addr`, `get_next_prop`, `add`, `sub`, `mul`, `div`, `mod`, `call_2s`, `call_2n`, `set_colour`, `throw`.
- **1OP:** `jz`, `get_sibling`, `get_child`, `get_parent`, `get_prop_len`, `inc`, `dec`, `print_addr`, `call_1s`, `remove_obj`, `print_obj`, `ret`, `jump`, `print_paddr`, `load`, `not` / `call_1n`.
- **0OP:** `rtrue`, `rfalse`, `print`, `print_ret`, `nop`, `save`, `restore`, `restart`, `ret_popped`, `pop` / `catch`, `quit`, `new_line`, `show_status`, `verify`, `piracy`, `extended` prefix.
- **VAR:** `call` / `call_vs`, `storew`, `storeb`, `put_prop`, `sread` / `aread`, `print_char`, `print_num`, `random`, `push`, `pull`, `split_window`, `set_window`, `call_vs2`, `erase_window`, `erase_line`, `set_cursor`, `get_cursor`, `set_text_style`, `buffer_mode`, `output_stream`, `input_stream`, `sound_effect`, `read_char`, `scan_table`, `not`, `call_vn`, `call_vn2`, `tokenise`, `encode_text`, `copy_table`, `print_table`, `check_arg_count`.
- **EXT:** `save`, `restore`, `log_shift`, `art_shift`, `set_font`, `draw_picture`, `picture_data`, `erase_picture`, `set_margins`, `save_undo`, `restore_undo`, `print_unicode`, `check_unicode`, `set_true_colour`, `move_window`, `window_size`, `window_style`, `get_wind_prop`, `scroll_window`, `pop_stack`, `read_mouse`, `mouse_window`, `push_stack`, `put_wind_prop`, `print_form`, `make_menu`, `picture_table`, `buffer_screen`.

## Quetzal 1.4

| Standard area | Implementation tasks | Coverage / acceptance evidence |
|---|---:|---|
| 1–2. Byte order, IFF, IFZS structure | 21 | Big-endian parsing, FORM/chunk lengths, even-byte padding, unknown/duplicate chunks. |
| 3. Dynamic memory | 21–22 | CMem XOR/run-length codec, UMem exact size, short/long and malformed runs. |
| 4. Stacks | 6, 21–22 | Oldest-first frames, locals/evaluation stack, dummy non-V6 frame, V6 frame behavior. |
| 5. Story identity and saved PC | 3, 21–22 | IFhd release/serial/checksum validation and version-specific save PC. |
| 6. Catch/throw and sound/picture notes | 6, 17, 21–22 | Frame-count cookie and resource identifiers. |
| 7. AUTH/(c)/ANNO/IntD | 21 | Optional metadata parsing, flags/copy rules, and safe ignoring of foreign data. |
| 8. IFF rules | 18, 21 | Four-character IDs, chunk bounds, padding, unknown chunks, duplicate handling. |

## Blorb 2.0.4

| Standard area / chunk | Implementation tasks | Coverage / acceptance evidence |
|---|---:|---|
| IFF FORM IFRS, RIdx, resource usages | 18 | Index offsets, order, validation, resource-number lookup, malformed file handling. |
| Pict: PNG, JPEG, Rect | 19 | Decode, dimensions, unsupported/placeholder behavior, picture-data queries. |
| Snd: AIFF, OGGV, MOD, SONG | 20 | Decode/playback capabilities, sample/music channel independence, repeat and stop. |
| Data TEXT/BINA and Exec ZCOD | 18 | Parse/index resources and load embedded Z-code; reject inconsistent launch inputs. |
| IFhd, Plte, Fspc, RDes, IFmd, RelN | 18–19 | Identity validation, palette, cover, accessible descriptions, metadata, release. |
| Reso, APal, Loop | 19–20 | Image scaling/palette rules and V3 sound looping. |
| AUTH, (c), ANNO, deprecated SNam | 18 | Optional user-level metadata; unknown and deprecated chunks do not break loading. |
| Compatibility and media channels | 17–20 | Z-machine feature flags and app behavior when optional resources are absent. |

## Cross-cutting quality checks

- Generated synthetic fixtures cover all eight story versions and do not rely on local licensed stories.
- Implemented Task 7 operations have generated xUnit coverage for signed results, variable references, stack effects, bounds/write protection, overlap behavior, branches, stores, and unsupported-opcode errors. Decoder coverage includes the four-operand je form.
- Task 8 text codecs have generated xUnit coverage for V1–V8 alphabet behavior, abbreviations, custom alphabet and Unicode tables, ZSCII validity, dictionary word widths, padding/escape truncation, and malformed text.
- Task 9 object/property support has generated V1–V8 fixtures for byte/word object links, attribute ordering and bounds, insertion/removal, default and declared properties, short names, dynamic-memory property tables, property sizes and addresses, opcode stores/branches, and malformed property lists.
- Task 10 dictionary and input-buffer support has generated V1–V8 fixtures for six/nine-Z-character lookup, sorted and unsorted dictionary counts, standalone separators, parse limits and skip-unknown behavior, `tokenise`/`encode_text`, legacy terminators, lowercase input, and V5+ continuation/terminator results.
- Task 11 output streams have generated V1–V8 fixtures for stream 1/2 routing and header synchronization, nested stream 3 tables and Unicode conversion, the 16-level nesting cap, stream 4 command/key capture, version-specific input echo, V4/V5 buffering, V6 width reporting, and print/output_stream opcodes.
- Task 12 input has generated V1–V8 fixtures for legacy and modern buffers, appended text, lowercase and defined-character filtering, dictionaries/parse buffers, CR/LF and function-key terminators, wildcard terminators, line editing, stream selection and command recording, read_char, timed callbacks/redraw, timeout clearing, and async executor dispatch.
- Task 13 auxiliary-file tests use generated names and temporary directories to cover uppercase 8.3 normalization, invalid-character filtering, length-prefixed story-memory bounds, directory-rooted reads/writes, missing-file results, and reportable storage failures.
- Malformed input tests cover truncated story, IFF, Blorb, and Quetzal data with bounded, actionable diagnostics.
- CI builds/tests Windows, macOS, and Linux; local story runs are optional and documented separately.

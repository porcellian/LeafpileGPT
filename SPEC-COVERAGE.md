# Specification Coverage Matrix

This matrix maps the supplied standards to planned implementation areas and acceptance tests. Update implementation and test references as tasks land. No standard requirement is complete until its behavior and edge cases have automated coverage or a documented manual acceptance test.

## Z-Machine Standard 1.1

| Standard area | Implementation tasks | Coverage / acceptance evidence |
|---|---:|---|
| 1. Memory map and addresses | 3–4 | `StoryFile` validates dynamic/static/high boundaries, story size limits, and table bounds. `ZMachineMemory` classifies regions, bounds game-visible and VM-internal reads, and protects static/high memory from writes. `ZMachineAddressing` converts byte, word, and V1–V8 packed addresses, including separate V6/V7 routine and string offsets. Generated xUnit fixtures cover boundaries, overlap, overflow, and write protection. |
| 2. Numbers and arithmetic | 7 | Signed arithmetic/comparisons; 16-bit wrapping; divide-by-zero; deterministic and random modes. |
| 3. Text and character encoding | 8, 10 | Z-character decoding, shifts, abbreviations, custom alphabets, Unicode/ZSCII tables, incomplete sequences, dictionary truncation. |
| 4. Instruction encoding | 5–7 | `ZMachineInstructionDecoder` parses long, short, variable, and extended forms; large/small/variable operands; the double type byte for `call_vs2`/`call_vn2`; store and branch data (including both on one instruction); signed branch offsets and return offsets; and inline string boundaries. `ZMachineOpcodeTable` describes all 119 standard opcodes with version-specific aliases, operand counts, store/branch flags, and inline-text metadata. Generated xUnit fixtures cover V1–V8, truncation, invalid operand ordering, and illegal opcode/version combinations. |
| 5. Routine encoding | 3–6 | Routine headers, local defaults, initial execution, V6 main routine, packed-call targets. |
| 6. Game state, storage, routine calls | 6, 22 | Stack/local/global state, user stacks, argument counts, catch/throw, save/restart semantics. |
| 7. Output streams and file handling | 11–13, 22 | Stream selection/nesting, buffering, Unicode stream conversions, filename rules, external file outcomes. |
| 8. Screen model | 14–16, 25 | V1–V5 windows/status; V6 eight-window model, text styles, colors, geometry, MORE behavior. |
| 9. Sound effects | 17, 20 | Capability flags, bleeps, samples/music channels, repeats, callbacks, prepare/stop/finish. |
| 10. Input streams and devices | 12, 17 | Command and key input, stream 1 files, timed input, terminating characters, mouse and menu events. |
| 11. Header | 3, 14–17, 23, 28 | `StoryHeader` parses V1–V8 base fields, version-scaled file length, checksum, V6 main-routine address, and memory bounds; xUnit fixtures cover all versions, legacy V3 omissions, truncation, and checksum mismatch. Later tasks add initialization, capabilities, extension-table behavior, and Rst fields. |
| 12. Object table | 9 | V1–V3 and V4+ layouts, attributes, object tree, property defaults and variable-length properties. |
| 13. Dictionary and lexical analysis | 10 | Separators, V1–V3/V4+ entry widths, sorted/unsorted dictionaries, token and parse buffers. |
| 14. Complete opcode table | 5–7, 14–17 | Per-opcode dispatch, version/operand-form combinations, store/branch effects, negative/error cases. |
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
- Every implemented opcode has tests for decoding, version availability, state effects, and branch/store behavior where applicable.
- Malformed input tests cover truncated story, IFF, Blorb, and Quetzal data with bounded, actionable diagnostics.
- CI builds/tests Windows, macOS, and Linux; local story runs are optional and documented separately.

namespace LeafpileGPT.Core;

/// <summary>The standard opcode inventory and version gates from Z-Machine Standard 1.1, section 14.</summary>
public static class ZMachineOpcodeTable
{
    private static readonly IReadOnlyDictionary<(OpcodeClass Class, byte Number), OpcodeDefinition[]> Definitions = Build();

    public static bool TryGet(OpcodeClass opcodeClass, byte number, byte version, out OpcodeDefinition definition)
    {
        if (Definitions.TryGetValue((opcodeClass, number), out var alternatives))
        {
            foreach (var candidate in alternatives)
            {
                if (candidate.SupportsVersion(version))
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null!;
        return false;
    }

    public static IReadOnlyCollection<OpcodeDefinition> AllDefinitions => Definitions.Values.SelectMany(items => items).ToArray();

    private static IReadOnlyDictionary<(OpcodeClass, byte), OpcodeDefinition[]> Build()
    {
        var all = new List<OpcodeDefinition>();
        void Add(OpcodeClass c, byte n, string name, int min = 1, int max = 8, int minArgs = 0, int maxArgs = 0,
            bool store = false, bool branch = false, bool inline = false, bool ignored = false) =>
            all.Add(new(c, n, name, (byte)min, (byte)max, (byte)minArgs, (byte)maxArgs, store, branch, inline, ignored));

        void Range(OpcodeClass c, int first, string[] names, int min = 1, int max = 8,
            int minArgs = 0, int maxArgs = 8, Func<int, bool>? stores = null, Func<int, bool>? branches = null)
        {
            for (var i = 0; i < names.Length; i++)
            {
                Add(c, (byte)(first + i), names[i], min, max, minArgs, maxArgs, stores?.Invoke(i) ?? false, branches?.Invoke(i) ?? false);
            }
        }

        Range(OpcodeClass.TwoOperand, 1,
            ["je", "jl", "jg", "dec_chk", "inc_chk", "jin", "test", "or", "and", "test_attr", "set_attr", "clear_attr", "store", "insert_obj", "loadw", "loadb", "get_prop", "get_prop_addr", "get_next_prop", "add", "sub", "mul", "div", "mod"],
            minArgs: 2, maxArgs: 2,
            stores: i => i is 7 or 8 or >= 14 and <= 23,
            branches: i => i is <= 6 or 9);
        Add(OpcodeClass.TwoOperand, 25, "call_2s", 4, 8, 2, 2, store: true);
        Add(OpcodeClass.TwoOperand, 26, "call_2n", 5, 8, 2, 2);
        Add(OpcodeClass.TwoOperand, 27, "set_colour", 5, 5, 2, 2);
        Add(OpcodeClass.TwoOperand, 27, "set_colour", 6, 8, 3, 3);
        Add(OpcodeClass.TwoOperand, 28, "throw", 5, 8, 2, 2);

        Range(OpcodeClass.OneOperand, 0,
            ["jz", "get_sibling", "get_child", "get_parent", "get_prop_len", "inc", "dec", "print_addr"],
            minArgs: 1, maxArgs: 1,
            stores: i => i is 1 or 2 or 3 or 4,
            branches: i => i is 0 or 1 or 2);
        Add(OpcodeClass.OneOperand, 8, "call_1s", 4, 8, 1, 1, store: true);
        Range(OpcodeClass.OneOperand, 9, ["remove_obj", "print_obj", "ret", "jump", "print_paddr", "load"],
            minArgs: 1, maxArgs: 1, stores: i => i == 5);
        Add(OpcodeClass.OneOperand, 15, "not", 1, 4, 1, 1, store: true);
        Add(OpcodeClass.OneOperand, 15, "call_1n", 5, 8, 1, 1);

        Add(OpcodeClass.ZeroOperand, 0, "rtrue");
        Add(OpcodeClass.ZeroOperand, 1, "rfalse");
        Add(OpcodeClass.ZeroOperand, 2, "print", inline: true);
        Add(OpcodeClass.ZeroOperand, 3, "print_ret", inline: true);
        Add(OpcodeClass.ZeroOperand, 4, "nop");
        Add(OpcodeClass.ZeroOperand, 5, "save", 1, 3, branch: true);
        Add(OpcodeClass.ZeroOperand, 5, "save", 4, 4, store: true);
        Add(OpcodeClass.ZeroOperand, 6, "restore", 1, 3, branch: true);
        Add(OpcodeClass.ZeroOperand, 6, "restore", 4, 4, store: true);
        Add(OpcodeClass.ZeroOperand, 7, "restart");
        Add(OpcodeClass.ZeroOperand, 8, "ret_popped");
        Add(OpcodeClass.ZeroOperand, 9, "pop", 1, 4);
        Add(OpcodeClass.ZeroOperand, 9, "catch", 5, 8, store: true);
        Add(OpcodeClass.ZeroOperand, 10, "quit");
        Add(OpcodeClass.ZeroOperand, 11, "new_line");
        Add(OpcodeClass.ZeroOperand, 12, "show_status", 3, 3);
        Add(OpcodeClass.ZeroOperand, 13, "verify", branch: true);
        Add(OpcodeClass.ZeroOperand, 15, "piracy", 5, 8, branch: true);

        Add(OpcodeClass.Variable, 0, "call", 1, 3, 1, 4, store: true);
        Add(OpcodeClass.Variable, 0, "call_vs", 4, 8, 1, 4, store: true);
        Range(OpcodeClass.Variable, 1, ["storew", "storeb", "put_prop"], minArgs: 3, maxArgs: 3);
        Add(OpcodeClass.Variable, 4, "sread", 1, 4, 2, 4);
        Add(OpcodeClass.Variable, 4, "aread", 5, 8, 2, 4, store: true);
        Add(OpcodeClass.Variable, 5, "print_char", minArgs: 1, maxArgs: 1);
        Add(OpcodeClass.Variable, 6, "print_num", minArgs: 1, maxArgs: 1);
        Add(OpcodeClass.Variable, 7, "random", store: true, minArgs: 1, maxArgs: 1);
        Add(OpcodeClass.Variable, 8, "push", minArgs: 1, maxArgs: 1);
        Add(OpcodeClass.Variable, 9, "pull", 1, 5, 1, 1);
        Add(OpcodeClass.Variable, 9, "pull", 6, 8, 1, 1, store: true);
        Add(OpcodeClass.Variable, 10, "split_window", 3, 8, 1, 1);
        Add(OpcodeClass.Variable, 11, "set_window", 3, 8, 1, 1);
        Add(OpcodeClass.Variable, 12, "call_vs2", 4, 8, 1, 8, store: true);
        Add(OpcodeClass.Variable, 13, "erase_window", 4, 8, 1, 1);
        Add(OpcodeClass.Variable, 14, "erase_line", 4, 5, 1, 1);
        Add(OpcodeClass.Variable, 14, "erase_line", 6, 8, 1, 1);
        Add(OpcodeClass.Variable, 15, "set_cursor", 4, 5, 2, 2);
        Add(OpcodeClass.Variable, 15, "set_cursor", 6, 8, 2, 3);
        Add(OpcodeClass.Variable, 16, "get_cursor", 4, 8, 1, 1);
        Add(OpcodeClass.Variable, 17, "set_text_style", 4, 8, 1, 1);
        Add(OpcodeClass.Variable, 18, "buffer_mode", 4, 8, 1, 1);
        Add(OpcodeClass.Variable, 19, "output_stream", 3, 4, 1, 1);
        Add(OpcodeClass.Variable, 19, "output_stream", 5, 5, 1, 2);
        Add(OpcodeClass.Variable, 19, "output_stream", 6, 8, 1, 3);
        Add(OpcodeClass.Variable, 20, "input_stream", 3, 8, 1, 1);
        Add(OpcodeClass.Variable, 21, "sound_effect", 3, 3, 2, 4);
        Add(OpcodeClass.Variable, 21, "sound_effect", 5, 8, 2, 4);
        Add(OpcodeClass.Variable, 22, "read_char", 4, 8, 1, 3, store: true);
        Add(OpcodeClass.Variable, 23, "scan_table", 4, 8, 3, 4, store: true, branch: true);
        Add(OpcodeClass.Variable, 24, "not", 5, 8, 1, 1, store: true);
        Add(OpcodeClass.Variable, 25, "call_vn", 5, 8, 1, 4);
        Add(OpcodeClass.Variable, 26, "call_vn2", 5, 8, 1, 8);
        Add(OpcodeClass.Variable, 27, "tokenise", 5, 8, 2, 4);
        Add(OpcodeClass.Variable, 28, "encode_text", 5, 8, 4, 4);
        Add(OpcodeClass.Variable, 29, "copy_table", 5, 8, 3, 3);
        Add(OpcodeClass.Variable, 30, "print_table", 5, 8, 2, 4);
        Add(OpcodeClass.Variable, 31, "check_arg_count", 5, 8, 1, 1, branch: true);

        Add(OpcodeClass.Extended, 0, "save", 5, 8, 3, 4, store: true);
        Add(OpcodeClass.Extended, 1, "restore", 5, 8, 3, 4, store: true);
        Add(OpcodeClass.Extended, 2, "log_shift", 5, 8, 2, 2, store: true);
        Add(OpcodeClass.Extended, 3, "art_shift", 5, 8, 2, 2, store: true);
        Add(OpcodeClass.Extended, 4, "set_font", 5, 5, 1, 1, store: true);
        Add(OpcodeClass.Extended, 4, "set_font", 6, 8, 1, 2, store: true);
        Add(OpcodeClass.Extended, 5, "draw_picture", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 6, "picture_data", 6, 8, 2, 2, branch: true);
        Add(OpcodeClass.Extended, 7, "erase_picture", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 8, "set_margins", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 9, "save_undo", 5, 8, 0, 0, store: true);
        Add(OpcodeClass.Extended, 10, "restore_undo", 5, 8, 0, 0, store: true);
        Add(OpcodeClass.Extended, 11, "print_unicode", 5, 8, 1, 1);
        Add(OpcodeClass.Extended, 12, "check_unicode", 5, 8, 1, 1, store: true);
        Add(OpcodeClass.Extended, 13, "set_true_colour", 5, 5, 2, 2);
        Add(OpcodeClass.Extended, 13, "set_true_colour", 6, 8, 2, 3);
        Add(OpcodeClass.Extended, 16, "move_window", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 17, "window_size", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 18, "window_style", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 19, "get_wind_prop", 6, 8, 2, 2, store: true);
        Add(OpcodeClass.Extended, 20, "scroll_window", 6, 8, 2, 2);
        Add(OpcodeClass.Extended, 21, "pop_stack", 6, 8, 1, 2);
        Add(OpcodeClass.Extended, 22, "read_mouse", 6, 8, 1, 1);
        Add(OpcodeClass.Extended, 23, "mouse_window", 6, 8, 1, 1);
        Add(OpcodeClass.Extended, 24, "push_stack", 6, 8, 2, 2, branch: true);
        Add(OpcodeClass.Extended, 25, "put_wind_prop", 6, 8, 3, 3);
        Add(OpcodeClass.Extended, 26, "print_form", 6, 8, 1, 1);
        Add(OpcodeClass.Extended, 27, "make_menu", 6, 8, 2, 2, branch: true);
        Add(OpcodeClass.Extended, 28, "picture_table", 6, 8, 1, 1);
        Add(OpcodeClass.Extended, 29, "buffer_screen", 6, 8, 1, 1, store: true);

        var result = new Dictionary<(OpcodeClass, byte), OpcodeDefinition[]>();
        foreach (var group in all.GroupBy(item => (item.Class, item.Number)))
        {
            result.Add(group.Key, group.ToArray());
        }

        return result;
    }
}

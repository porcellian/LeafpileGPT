namespace Leafpile.Core.Machine;

/// <summary>Provides the standard mnemonic for a decoded opcode.</summary>
public static class ZMachineOpcodeNames
{
    private static readonly IReadOnlyDictionary<ZMachineOpcodeClass, string[]> Names = new Dictionary<ZMachineOpcodeClass, string[]>
    {
        [ZMachineOpcodeClass.TwoOperand] = ["", "je", "jl", "jg", "dec_chk", "inc_chk", "jin", "test", "or", "and", "test_attr", "set_attr", "clear_attr", "store", "insert_obj", "loadw", "loadb", "get_prop", "get_prop_addr", "get_next_prop", "add", "sub", "mul", "div", "mod", "call_2s", "call_2n", "set_colour", "throw"],
        [ZMachineOpcodeClass.OneOperand] = ["jz", "get_sibling", "get_child", "get_parent", "get_prop_len", "inc", "dec", "print_addr", "call_1s", "remove_obj", "print_obj", "ret", "jump", "print_paddr", "load", "not"],
        [ZMachineOpcodeClass.ZeroOperand] = ["rtrue", "rfalse", "print", "print_ret", "nop", "save", "restore", "restart", "ret_popped", "pop", "quit", "new_line", "show_status", "verify", "", "piracy"],
        [ZMachineOpcodeClass.Variable] = ["call_vs", "storew", "storeb", "put_prop", "read", "print_char", "print_num", "random", "push", "pull", "split_window", "set_window", "call_vs2", "erase_window", "erase_line", "set_cursor", "get_cursor", "set_text_style", "buffer_mode", "output_stream", "input_stream", "sound_effect", "read_char", "scan_table", "not", "call_vn", "call_vn2", "tokenise", "encode_text", "copy_table", "print_table", "check_arg_count"],
        [ZMachineOpcodeClass.Extended] = ["save", "restore", "log_shift", "art_shift", "set_font", "draw_picture", "picture_data", "erase_picture", "set_margins", "save_undo", "restore_undo", "print_unicode", "check_unicode", "set_true_colour", "move_window", "window_size", "window_style", "get_wind_prop", "scroll_window", "pop_stack", "read_mouse", "mouse_window", "push_stack", "put_wind_prop", "print_form", "make_menu", "picture_table"]
    };

    public static string GetMnemonic(ZMachineOpcodeClass opcodeClass, int opcode) =>
        Names.TryGetValue(opcodeClass, out var names) && opcode >= 0 && opcode < names.Length && names[opcode].Length > 0
            ? names[opcode]
            : "unknown";
}

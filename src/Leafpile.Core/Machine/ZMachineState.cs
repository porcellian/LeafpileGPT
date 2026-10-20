using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public sealed class ZMachineState
{
    private readonly LoadedStory _story;

    public ZMachineState(LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);

        _story = story;
        Memory = new ZMachineMemory(story);
        Characters = new ZMachineCharacterSet(Memory, story.Header);
        Text = new ZMachineTextCodec(Memory, story.Header, Characters);
        Instructions = new ZMachineInstructionDecoder(Memory, story.Header, Text);
        CallStack = new ZMachineCallStack(Memory, story.Header);
        Random = new ZMachineRandom();
    }

    public StoryHeader Header => _story.Header;

    public ZMachineMemory Memory { get; private set; }

    public ZMachineCharacterSet Characters { get; private set; }

    public ZMachineTextCodec Text { get; private set; }

    public ZMachineInstructionDecoder Instructions { get; private set; }

    public ZMachineCallStack CallStack { get; private set; }

    public ZMachineRandom Random { get; }

    public int ProgramCounter => CallStack.ProgramCounter;

    public byte ReadHeaderByte(ushort address)
    {
        if (address >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "A header byte address must be within 0x00–0x3f.");
        }

        return Memory.ReadByte(address);
    }

    public ushort ReadHeaderWord(ushort address)
    {
        if (address >= 63)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "A header word must begin within 0x00–0x3e.");
        }

        return Memory.ReadWord(address);
    }

    public void SetProgramCounter(int address)
    {
        CallStack.SetProgramCounter(address);
    }

    public void AdvanceProgramCounter(int byteCount) => CallStack.AdvanceProgramCounter(byteCount);

    public ushort ReadVariableValue(byte variableNumber)
    {
        if (variableNumber == 0)
        {
            return CallStack.Pop();
        }

        if (variableNumber < 16)
        {
            return CallStack.GetLocal(variableNumber);
        }

        var address = checked((ushort)(Header.GlobalVariablesAddress + ((variableNumber - 16) * sizeof(ushort))));
        return Memory.ReadWord(address);
    }

    public ushort ReadIndirectVariable(byte variableNumber) => variableNumber == 0
        ? CallStack.Peek()
        : ReadVariableValue(variableNumber);

    public void WriteVariable(byte variableNumber, ushort value)
    {
        if (variableNumber == 0)
        {
            CallStack.Push(value);
            return;
        }

        if (variableNumber < 16)
        {
            CallStack.SetLocal(variableNumber, value);
            return;
        }

        var address = checked((ushort)(Header.GlobalVariablesAddress + ((variableNumber - 16) * sizeof(ushort))));
        Memory.WriteWord(address, value);
    }

    public void WriteIndirectVariable(byte variableNumber, ushort value)
    {
        if (variableNumber == 0)
        {
            CallStack.ReplaceTop(value);
            return;
        }

        WriteVariable(variableNumber, value);
    }

    public void Restart()
    {
        const ushort PreservedFlags2 = 0x0003;
        var preservedFlags = (ushort)(Memory.ReadWord(0x10) & PreservedFlags2);
        Memory = new ZMachineMemory(_story);
        Memory.WriteWord(0x10, (ushort)((Memory.ReadWord(0x10) & ~PreservedFlags2) | preservedFlags));
        Characters = new ZMachineCharacterSet(Memory, Header);
        Text = new ZMachineTextCodec(Memory, Header, Characters);
        Instructions = new ZMachineInstructionDecoder(Memory, Header, Text);
        CallStack = new ZMachineCallStack(Memory, Header);
        Random.ResetToRandom();
    }
}

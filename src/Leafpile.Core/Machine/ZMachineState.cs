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
        CallStack = new ZMachineCallStack(Memory, story.Header);
        Random = new ZMachineRandom();
    }

    public StoryHeader Header => _story.Header;

    public ZMachineMemory Memory { get; private set; }

    public ZMachineCharacterSet Characters { get; private set; }

    public ZMachineTextCodec Text { get; private set; }

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

    public void Restart()
    {
        Memory = new ZMachineMemory(_story);
        Characters = new ZMachineCharacterSet(Memory, Header);
        Text = new ZMachineTextCodec(Memory, Header, Characters);
        CallStack = new ZMachineCallStack(Memory, Header);
        Random.ResetToRandom();
    }
}

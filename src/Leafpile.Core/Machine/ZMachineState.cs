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
        ProgramCounter = story.Header.InitialProgramAddress;
        Random = new ZMachineRandom();
    }

    public StoryHeader Header => _story.Header;

    public ZMachineMemory Memory { get; private set; }

    public ZMachineRandom Random { get; }

    public int ProgramCounter { get; private set; }

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
        if ((uint)address >= (uint)Memory.StoryLength)
        {
            throw new ZMachineRuntimeException($"Program counter 0x{address:x} is outside the {Memory.StoryLength}-byte story.");
        }

        ProgramCounter = address;
    }

    public void AdvanceProgramCounter(int byteCount)
    {
        var target = (long)ProgramCounter + byteCount;
        if (target < 0 || target >= Memory.StoryLength)
        {
            throw new ZMachineRuntimeException($"Program counter advance to 0x{target:x} is outside the {Memory.StoryLength}-byte story.");
        }

        ProgramCounter = (int)target;
    }

    public void Restart()
    {
        Memory = new ZMachineMemory(_story);
        ProgramCounter = Header.InitialProgramAddress;
        Random.ResetToRandom();
    }
}

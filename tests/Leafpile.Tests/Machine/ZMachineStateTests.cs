using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineStateTests
{
    [Fact]
    public void InitializesProgramCounterFromLoadedHeader()
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));

        Assert.Equal(0x200, state.ProgramCounter);
        Assert.Equal(5, state.ReadHeaderByte(0));
        Assert.Equal(0x0300, state.ReadHeaderWord(0x0e));
    }

    [Fact]
    public void TracksDynamicHeaderFlagsThroughMemory()
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));
        state.Memory.WriteByte(1, 0x28);
        state.Memory.WriteWord(0x10, 0x8001);

        Assert.Equal(0x28, state.ReadHeaderByte(1));
        Assert.Equal(0x8001, state.ReadHeaderWord(0x10));
    }

    [Fact]
    public void AdvancesAndSetsProgramCounterWithinStoryBounds()
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));
        state.AdvanceProgramCounter(3);
        Assert.Equal(0x203, state.ProgramCounter);
        state.AdvanceProgramCounter(-1);
        Assert.Equal(0x202, state.ProgramCounter);

        Assert.Throws<ZMachineRuntimeException>(() => state.SetProgramCounter(state.Memory.StoryLength));
        Assert.Throws<ZMachineRuntimeException>(() => state.AdvanceProgramCounter(int.MaxValue));
    }

    [Fact]
    public void RestartRestoresInitialMemoryPcAndRandomMode()
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));
        state.Memory.WriteByte(0x80, 0xab);
        state.SetProgramCounter(0x220);
        state.Random.Next(unchecked((ushort)-10));

        state.Restart();

        Assert.Equal(0, state.Memory.ReadByte(0x80));
        Assert.Equal(0x200, state.ProgramCounter);
        Assert.False(state.Random.IsPredictable);
    }

    [Fact]
    public void RejectsHeaderAccessOutsideTheHeader()
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));

        Assert.Throws<ArgumentOutOfRangeException>(() => state.ReadHeaderByte(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.ReadHeaderWord(63));
    }
}

internal static class MachineStoryFactory
{
    public static byte[] Create(byte version, int length = 2048)
    {
        var data = new byte[length];
        data[0] = version;
        WriteWord(data, 0x04, 0x0300);
        WriteWord(data, 0x06, version == 6 ? (ushort)0x80 : (ushort)0x0200);
        WriteWord(data, 0x08, 0x0300);
        WriteWord(data, 0x0a, 0x0040);
        WriteWord(data, 0x0c, 0x0040);
        WriteWord(data, 0x0e, 0x0300);
        if (version >= 2)
        {
            WriteWord(data, 0x18, 0x0100);
        }

        if (version >= 3)
        {
            var multiplier = version <= 3 ? 2 : version <= 5 ? 4 : 8;
            WriteWord(data, 0x1a, (ushort)(length / multiplier));
            WriteWord(data, 0x1c, 0x1234);
        }

        if (version == 6)
        {
            data[0x200] = 0;
        }

        return data;
    }

    public static ushort WriteRoutine(byte[] data, byte version, int address, byte localCount, params ushort[] localDefaults)
    {
        data[address] = localCount;
        if (version <= 4)
        {
            for (var index = 0; index < localCount; index++)
            {
                var value = index < localDefaults.Length ? localDefaults[index] : (ushort)0;
                WriteWord(data, address + 1 + (index * sizeof(ushort)), value);
            }
        }

        return version switch
        {
            <= 3 => checked((ushort)(address / 2)),
            <= 5 => checked((ushort)(address / 4)),
            <= 7 => checked((ushort)(address / 4)),
            8 => checked((ushort)(address / 8)),
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
    }

    private static void WriteWord(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }
}

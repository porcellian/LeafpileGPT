using System.Buffers.Binary;
using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public sealed class ZMachineMemory
{
    private readonly byte[] _data;

    public ZMachineMemory(LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);

        _data = story.Data.ToArray();
        DynamicMemoryLength = story.Header.StaticMemoryBase;
        StaticMemoryLimitExclusive = Math.Min(_data.Length, 0x10000);
    }

    public int StoryLength => _data.Length;

    public int DynamicMemoryLength { get; }

    public int StaticMemoryLimitExclusive { get; }

    public byte ReadByte(ushort address)
    {
        ValidateAddress(address, sizeof(byte));
        return _data[address];
    }

    public ushort ReadWord(ushort address)
    {
        ValidateAddress(address, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(address, sizeof(ushort)));
    }

    public void WriteByte(ushort address, byte value)
    {
        ValidateDynamicAddress(address, sizeof(byte));
        _data[address] = value;
    }

    public void WriteWord(ushort address, ushort value)
    {
        ValidateDynamicAddress(address, sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(_data.AsSpan(address, sizeof(ushort)), value);
    }

    public void ValidateDynamicRange(ushort address, int width)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A dynamic memory range must have a positive width.");
        }

        ValidateDynamicAddress(address, width);
    }

    public void ValidateReadableRange(int address, int width)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A readable memory range must have a positive width.");
        }

        var end = (long)address + width;
        if (address < 0 || address > ushort.MaxValue || end > StaticMemoryLimitExclusive)
        {
            throw new ZMachineRuntimeException($"Memory read at 0x{address:x} with width {width} is outside addressable dynamic and static memory.");
        }
    }

    public byte ReadInstructionByte(int address)
    {
        if ((uint)address >= (uint)_data.Length)
        {
            throw new ZMachineRuntimeException($"Instruction address 0x{address:x} is outside the {StoryLength}-byte story.");
        }

        return _data[address];
    }

    public ReadOnlyMemory<byte> GetStoryImage() => _data.ToArray();

    private void ValidateAddress(ushort address, int width)
    {
        var end = address + width;
        if (end > StaticMemoryLimitExclusive)
        {
            throw new ZMachineRuntimeException($"Memory read at 0x{address:x4} with width {width} is outside addressable dynamic and static memory.");
        }
    }

    private void ValidateDynamicAddress(ushort address, int width)
    {
        var end = address + width;
        if (end > DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException($"Memory access at 0x{address:x4} with width {width} is outside dynamic memory (0x0000–0x{DynamicMemoryLength - 1:x4}).");
        }
    }
}

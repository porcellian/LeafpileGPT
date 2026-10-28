namespace LeafpileGPT.Core;

using System.Buffers.Binary;

public enum ZMachineMemoryRegion
{
    Dynamic,
    Static,
    StaticAndHigh,
    High
}

/// <summary>A story memory image with version-aware regions and write protection.</summary>
public sealed class ZMachineMemory
{
    private readonly byte[] _bytes;
    private readonly byte[] _originalBytes;

    public ZMachineMemory(StoryFile story)
    {
        ArgumentNullException.ThrowIfNull(story);
        _bytes = story.Bytes.ToArray();
        _originalBytes = _bytes.ToArray();
        Header = story.Header;
    }

    public StoryHeader Header { get; }

    public int Length => _bytes.Length;

    public ushort DynamicMemoryEndExclusive => Header.StaticMemoryBase;

    public ushort StaticMemoryBase => Header.StaticMemoryBase;

    public ushort HighMemoryBase => Header.HighMemoryBase;

    public ZMachineMemoryRegion GetRegion(uint byteAddress)
    {
        ValidateStoryRange(byteAddress, 1);
        if (byteAddress < StaticMemoryBase)
        {
            return ZMachineMemoryRegion.Dynamic;
        }

        if (byteAddress >= HighMemoryBase)
        {
            return byteAddress <= ushort.MaxValue
                ? ZMachineMemoryRegion.StaticAndHigh
                : ZMachineMemoryRegion.High;
        }

        return ZMachineMemoryRegion.Static;
    }

    public void ValidateReadableRange(uint byteAddress, int size) => ValidateDirectRange(byteAddress, size);

    public void ValidateWritableRange(uint byteAddress, int size) => ValidateDynamicRange(byteAddress, size);

    /// <summary>Reads a byte using a game-visible 16-bit byte address.</summary>
    public byte ReadByte(ushort byteAddress)
    {
        ValidateStoryRange(byteAddress, 1);
        return _bytes[byteAddress];
    }

    /// <summary>Reads a big-endian word using a game-visible 16-bit byte address.</summary>
    public ushort ReadWord(ushort byteAddress)
    {
        ValidateDirectRange(byteAddress, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(byteAddress, sizeof(ushort)));
    }

    /// <summary>Writes a byte only when the target is in dynamic memory.</summary>
    public void WriteByte(ushort byteAddress, byte value)
    {
        ValidateDynamicRange(byteAddress, 1);
        _bytes[byteAddress] = value;
    }

    /// <summary>Writes a big-endian word only when both target bytes are in dynamic memory.</summary>
    public void WriteWord(ushort byteAddress, ushort value)
    {
        ValidateDynamicRange(byteAddress, sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(byteAddress, sizeof(ushort)), value);
    }

    /// <summary>Returns a copy of dynamic memory for a Quetzal save.</summary>
    public byte[] CaptureDynamicMemory() => _bytes.AsSpan(0, StaticMemoryBase).ToArray();

    /// <summary>Restores exactly the dynamic-memory region, leaving static and high memory untouched.</summary>
    public void RestoreDynamicMemory(ReadOnlySpan<byte> dynamicMemory)
    {
        if (dynamicMemory.Length != StaticMemoryBase)
        {
            throw new ArgumentException(
                $"Dynamic memory must contain exactly {StaticMemoryBase} bytes.", nameof(dynamicMemory));
        }

        dynamicMemory.CopyTo(_bytes);
    }

    /// <summary>Restores the complete original story image for a restart.</summary>
    public void RestoreOriginalStory() => _originalBytes.CopyTo(_bytes, 0);

    /// <summary>
    /// Reads a byte by the VM's full internal byte address, including high memory. Game memory
    /// opcodes should use <see cref="ReadByte(ushort)"/> instead.
    /// </summary>
    public byte ReadStoryByte(uint byteAddress)
    {
        ValidateStoryRange(byteAddress, 1);
        return _bytes[checked((int)byteAddress)];
    }

    /// <summary>Reads a big-endian word by the VM's full internal byte address, including high memory.</summary>
    public ushort ReadStoryWord(uint byteAddress)
    {
        ValidateStoryRange(byteAddress, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(checked((int)byteAddress), sizeof(ushort)));
    }

    private void ValidateDynamicRange(uint address, int size)
    {
        ValidateDirectRange(address, size);
        var endExclusive = (ulong)address + (uint)size;
        if (endExclusive > StaticMemoryBase)
        {
            throw new ZMachineMemoryAccessException(address, "write", "Writes are allowed only in dynamic memory.");
        }
    }

    private void ValidateDirectRange(uint address, int size)
    {
        if (address > ushort.MaxValue || (ulong)address + (uint)size > (ulong)ushort.MaxValue + 1)
        {
            throw new ZMachineMemoryAccessException(address, "access", "Game-visible byte addresses are limited to 16 bits.");
        }

        ValidateStoryRange(address, size);
    }

    private void ValidateStoryRange(uint address, int size)
    {
        if (size < 0 || (ulong)address + (uint)size > (ulong)_bytes.Length)
        {
            throw new ZMachineMemoryAccessException(address, "access", "Address range lies outside the loaded story image.");
        }
    }
}

public sealed class ZMachineMemoryAccessException : Exception
{
    public ZMachineMemoryAccessException(uint address, string operation, string reason)
        : base($"Cannot {operation} Z-machine memory at 0x{address:X}: {reason}")
    {
        Address = address;
        Operation = operation;
    }

    public uint Address { get; }

    public string Operation { get; }
}

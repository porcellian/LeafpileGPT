namespace LeafpileGPT.Core;

/// <summary>Converts Z-machine byte, word, and packed addresses to byte addresses.</summary>
public static class ZMachineAddressing
{
    public static uint ToByteAddress(ushort byteAddress) => byteAddress;

    public static uint WordToByteAddress(ushort wordAddress) => (uint)wordAddress * 2;

    public static uint PackedToByteAddress(ushort packedAddress, StoryHeader header, PackedAddressKind kind)
    {
        ArgumentNullException.ThrowIfNull(header);
        return PackedToByteAddress(packedAddress, header.Version, kind, header.RoutinesOffset, header.StringsOffset);
    }

    public static uint PackedToByteAddress(
        ushort packedAddress,
        byte version,
        PackedAddressKind kind,
        ushort? routinesOffset = null,
        ushort? stringsOffset = null)
    {
        var scale = version switch
        {
            >= 1 and <= 3 => 2u,
            >= 4 and <= 5 => 4u,
            >= 6 and <= 7 => 4u,
            8 => 8u,
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Z-machine version must be from 1 through 8.")
        };

        var byteAddress = packedAddress * scale;
        if (version is 6 or 7)
        {
            var offset = kind switch
            {
                PackedAddressKind.Routine => routinesOffset,
                PackedAddressKind.String => stringsOffset,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown packed-address kind.")
            };

            if (offset is null)
            {
                var parameterName = kind == PackedAddressKind.Routine
                    ? nameof(routinesOffset)
                    : nameof(stringsOffset);
                throw new ArgumentException($"Version {version} packed addresses require the applicable header offset.", parameterName);
            }

            byteAddress += (uint)offset.Value * 8;
        }
        else if (kind is not PackedAddressKind.Routine and not PackedAddressKind.String)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown packed-address kind.");
        }

        return byteAddress;
    }

    /// <summary>Gets the byte address where execution begins, resolving the V6 packed main routine.</summary>
    public static uint InitialProgramCounterByteAddress(StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.UsesPackedMainRoutine
            ? PackedToByteAddress(header.InitialProgramCounter, header, PackedAddressKind.Routine)
            : ToByteAddress(header.InitialProgramCounter);
    }
}

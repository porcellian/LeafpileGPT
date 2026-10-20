using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public enum PackedAddressKind
{
    Routine,
    String
}

public static class ZMachineAddressing
{
    public static int DecodePackedAddress(ushort packedAddress, StoryHeader header, PackedAddressKind kind)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Version switch
        {
            <= 3 => packedAddress * 2,
            <= 5 => packedAddress * 4,
            <= 7 => (packedAddress * 4) + (kind == PackedAddressKind.Routine ? header.RoutinesOffset : header.StringsOffset) * 8,
            8 => packedAddress * 8,
            _ => throw new ArgumentOutOfRangeException(nameof(header), header.Version, "The story version must be between 1 and 8.")
        };
    }

    public static int DecodeWordAddress(ushort wordAddress) => wordAddress * 2;
}

using System.Buffers.Binary;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineAddressingTests
{
    [Theory]
    [InlineData(1, 64, 64)]
    [InlineData(2, 64, 64)]
    [InlineData(3, 64, 64)]
    [InlineData(4, 128, 128)]
    [InlineData(5, 128, 128)]
    [InlineData(6, 144, 208)]
    [InlineData(7, 144, 208)]
    [InlineData(8, 256, 256)]
    public void DecodesPackedRoutineAndStringAddresses(byte version, int routineExpected, int stringExpected)
    {
        var story = MachineStoryFactory.Create(version);
        WriteWord(story, 0x28, 2);
        WriteWord(story, 0x2a, 10);
        var header = StoryLoader.Load(story).Header;

        Assert.Equal(routineExpected, ZMachineAddressing.DecodePackedAddress(0x20, header, PackedAddressKind.Routine));
        Assert.Equal(stringExpected, ZMachineAddressing.DecodePackedAddress(0x20, header, PackedAddressKind.String));
    }

    [Fact]
    public void ConvertsWordAddressToByteAddress()
    {
        Assert.Equal(0x2468, ZMachineAddressing.DecodeWordAddress(0x1234));
    }

    private static void WriteWord(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, sizeof(ushort)), value);
}

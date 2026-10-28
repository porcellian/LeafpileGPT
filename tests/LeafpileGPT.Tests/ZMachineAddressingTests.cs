using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public class ZMachineAddressingTests
{
    [Theory]
    [InlineData(1, 123, PackedAddressKind.Routine, 0, 0, 246)]
    [InlineData(2, 123, PackedAddressKind.String, 0, 0, 246)]
    [InlineData(3, 123, PackedAddressKind.Routine, 0, 0, 246)]
    [InlineData(4, 123, PackedAddressKind.String, 0, 0, 492)]
    [InlineData(5, 123, PackedAddressKind.Routine, 0, 0, 492)]
    [InlineData(6, 123, PackedAddressKind.Routine, 5, 9, 532)]
    [InlineData(6, 123, PackedAddressKind.String, 5, 9, 564)]
    [InlineData(7, 123, PackedAddressKind.Routine, 5, 9, 532)]
    [InlineData(7, 123, PackedAddressKind.String, 5, 9, 564)]
    [InlineData(8, 123, PackedAddressKind.Routine, 0, 0, 984)]
    [InlineData(8, 123, PackedAddressKind.String, 0, 0, 984)]
    public void TranslatesPackedAddressesUsingVersionRules(
        byte version,
        ushort packedAddress,
        PackedAddressKind kind,
        ushort routinesOffset,
        ushort stringsOffset,
        uint expected)
    {
        var actual = ZMachineAddressing.PackedToByteAddress(
            packedAddress,
            version,
            kind,
            version is 6 or 7 ? routinesOffset : null,
            version is 6 or 7 ? stringsOffset : null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConvertsByteAndWordAddressesWithoutNarrowingResults()
    {
        Assert.Equal(0xFFFFu, ZMachineAddressing.ToByteAddress(0xFFFF));
        Assert.Equal(0x10000u, ZMachineAddressing.WordToByteAddress(0x8000));
        Assert.Equal(0x1FFFEu, ZMachineAddressing.WordToByteAddress(0xFFFF));
    }

    [Fact]
    public void ResolvesVersionSixInitialProgramCounterAsPackedRoutineAddress()
    {
        var data = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(data, 0x06, 200);
        StoryFixtureBuilder.WriteWord(data, 0x28, 5);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));
        var header = StoryFile.Parse(data).Header;

        Assert.Equal(840u, ZMachineAddressing.InitialProgramCounterByteAddress(header));
    }

    [Theory]
    [InlineData(6, PackedAddressKind.Routine, 532)]
    [InlineData(6, PackedAddressKind.String, 564)]
    [InlineData(7, PackedAddressKind.Routine, 532)]
    [InlineData(7, PackedAddressKind.String, 564)]
    public void UsesOffsetsFromVersionSixAndSevenStoryHeaders(byte version, PackedAddressKind kind, uint expected)
    {
        var data = StoryFixtureBuilder.Create(version);
        StoryFixtureBuilder.WriteWord(data, 0x28, 5);
        StoryFixtureBuilder.WriteWord(data, 0x2A, 9);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));
        var header = StoryFile.Parse(data).Header;

        Assert.Equal(expected, ZMachineAddressing.PackedToByteAddress(123, header, kind));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void KeepsNonVersionSixInitialProgramCounterAsByteAddress(byte version)
    {
        var header = StoryFile.Parse(StoryFixtureBuilder.Create(version)).Header;

        Assert.Equal(850u, ZMachineAddressing.InitialProgramCounterByteAddress(header));
    }

    [Fact]
    public void RequiresVersionSixAndSevenPackedAddressOffsets()
    {
        var routineException = Assert.Throws<ArgumentException>(
            () => ZMachineAddressing.PackedToByteAddress(1, 6, PackedAddressKind.Routine));
        var stringException = Assert.Throws<ArgumentException>(
            () => ZMachineAddressing.PackedToByteAddress(1, 7, PackedAddressKind.String));

        Assert.Equal("routinesOffset", routineException.ParamName);
        Assert.Equal("stringsOffset", stringException.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsUnsupportedMachineVersion(byte version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ZMachineAddressing.PackedToByteAddress(1, version, PackedAddressKind.Routine));
    }
}

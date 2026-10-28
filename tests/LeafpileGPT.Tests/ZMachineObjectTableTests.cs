namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineObjectTableTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ReadsVersionSpecificObjectTreeAndMovesObjects(byte version)
    {
        var state = CreateState(version);
        var objects = new ZMachineObjectTable(state.Memory);

        Assert.Equal((ushort)1, objects.GetParent(2));
        Assert.Equal((ushort)3, objects.GetSibling(2));
        Assert.Equal((ushort)2, objects.GetChild(1));
        Assert.True(objects.IsChildOf(3, 1));
        Assert.False(objects.IsChildOf(1, 3));

        objects.RemoveObject(2);
        Assert.Equal((ushort)3, objects.GetChild(1));
        Assert.Equal((ushort)0, objects.GetParent(2));
        Assert.Equal((ushort)0, objects.GetSibling(2));

        objects.InsertObject(3, 2);
        Assert.Equal((ushort)3, objects.GetChild(2));
        Assert.Equal((ushort)2, objects.GetParent(3));
        Assert.Equal((ushort)0, objects.GetSibling(3));
        Assert.Equal((ushort)0, objects.GetChild(1));
    }

    [Theory]
    [InlineData(1, 31)]
    [InlineData(2, 31)]
    [InlineData(3, 31)]
    [InlineData(4, 47)]
    [InlineData(5, 47)]
    [InlineData(6, 47)]
    [InlineData(7, 47)]
    [InlineData(8, 47)]
    public void AttributesAreStoredMostSignificantBitFirstAndRespectVersionLayout(byte version, ushort lastAttribute)
    {
        var state = CreateState(version);
        var objects = new ZMachineObjectTable(state.Memory);

        objects.SetAttribute(1, 0, true);
        objects.SetAttribute(1, lastAttribute, true);
        Assert.True(objects.TestAttribute(1, 0));
        Assert.True(objects.TestAttribute(1, lastAttribute));

        objects.SetAttribute(1, 0, false);
        Assert.False(objects.TestAttribute(1, 0));
        Assert.Equal(version <= 3 ? 32 : 48, objects.AttributeCount);
        Assert.False(objects.TestAttribute(1, (ushort)(lastAttribute + 1)));
    }

    [Fact]
    public void OutOfRangeSherlockAttributeDoesNotCorruptVersionFourPlusLinks()
    {
        var state = CreateState(5);
        var objects = new ZMachineObjectTable(state.Memory);

        objects.SetAttribute(1, 48, true);
        objects.SetAttribute(1, 48, false);

        Assert.False(objects.TestAttribute(1, 48));
        Assert.Equal((ushort)2, objects.GetChild(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ReadsDefaultsAddressesAndOrderedProperties(byte version)
    {
        var state = CreateState(version);
        var objects = new ZMachineObjectTable(state.Memory);

        Assert.Equal((ushort)0xCAFE, objects.GetProperty(1, 4));
        Assert.Equal((ushort)502, objects.GetPropertyAddress(1, 10));
        Assert.Equal(10, objects.GetNextProperty(1, 0));
        Assert.Equal(5, objects.GetNextProperty(1, 10));
        Assert.Equal(0, objects.GetNextProperty(1, 5));
        Assert.Equal((ushort)0, objects.GetPropertyAddress(1, 4));
    }

    [Fact]
    public void ReadsAndWritesOneAndTwoBytePropertiesAndIgnoresHighBitsForByteWrite()
    {
        var state = CreateState(5);
        var objects = new ZMachineObjectTable(state.Memory);

        objects.SetProperty(1, 10, 0xABCD);
        objects.SetProperty(1, 5, 0x1234);

        Assert.Equal((ushort)0xABCD, objects.GetProperty(1, 10));
        Assert.Equal((ushort)0x34, objects.GetProperty(1, 5));
        Assert.Equal((byte)0x34, state.Memory.ReadByte(505));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 2)]
    public void GetsPropertyLengthFromVersionSpecificSizeHeaders(byte version, int expectedLength)
    {
        var state = CreateState(version);
        var objects = new ZMachineObjectTable(state.Memory);

        Assert.Equal(expectedLength, objects.GetPropertyLength(502));
        Assert.Equal(1, objects.GetPropertyLength(505));
    }

    [Fact]
    public void PropertyTablesMayBeLocatedAnywhereInDynamicMemory()
    {
        var bytes = CreateStoryBytes(5);
        Array.Copy(bytes, 500, bytes, 220, 7);
        SetObject(bytes, 5, 1, 0, 0, 0, 220);
        UpdateChecksum(bytes);
        var objects = new ZMachineObjectTable(new ZMachineMemory(StoryFile.Parse(bytes)));

        Assert.Equal((ushort)0xABCD, objects.GetProperty(1, 10));
        Assert.Equal(2, objects.GetPropertyLength(222));
    }

    [Fact]
    public void SupportsExtendedPropertySizesAndPropertyLengthLookup()
    {
        var state = CreateState(5, extendedPropertyLength: 64);
        var objects = new ZMachineObjectTable(state.Memory);

        Assert.Equal(64, objects.GetPropertyLength(503));
        Assert.Equal(0, objects.GetPropertyLength(0));
        state.Memory.WriteByte(599, 0);
        Assert.Throws<ZMachineExecutionException>(() => objects.GetPropertyLength(600));
    }

    [Fact]
    public void RejectsMissingNextPropertyAndOversizedGetOrPutProperty()
    {
        var state = CreateState(5, extendedPropertyLength: 3);
        var objects = new ZMachineObjectTable(state.Memory);

        Assert.Throws<ZMachineExecutionException>(() => objects.GetNextProperty(1, 4));
        Assert.Throws<ZMachineExecutionException>(() => objects.GetProperty(1, 10));
        Assert.Throws<ZMachineExecutionException>(() => objects.SetProperty(1, 10, 9));
    }

    [Fact]
    public void DecodesObjectShortNameWithinDeclaredNameWords()
    {
        var bytes = CreateStoryBytes(5);
        bytes[540] = 1;
        StoryFixtureBuilder.WriteWord(bytes, 541, 0x98A5); // terminal packed Z-string "a"
        bytes[543] = 0;
        SetObject(bytes, 5, 1, 0, 0, 0, 540);
        UpdateChecksum(bytes);
        var objects = new ZMachineObjectTable(new ZMachineMemory(StoryFile.Parse(bytes)));

        Assert.Equal("a", objects.GetShortName(1));
    }

    [Theory]
    [InlineData("jin")]
    [InlineData("test_attr")]
    [InlineData("set_attr")]
    [InlineData("clear_attr")]
    [InlineData("insert_obj")]
    [InlineData("get_sibling")]
    [InlineData("get_child")]
    [InlineData("get_parent")]
    [InlineData("get_prop")]
    [InlineData("get_prop_addr")]
    [InlineData("get_next_prop")]
    [InlineData("get_prop_len")]
    [InlineData("put_prop")]
    [InlineData("remove_obj")]
    public void ObjectOpcodesExecuteThroughTheOpcodeExecutor(string mnemonic)
    {
        var state = CreateState(5);
        var executor = new ZMachineOpcodeExecutor();
        DecodedOperand[] operands = mnemonic switch
        {
            "jin" or "test_attr" or "set_attr" or "clear_attr" or "insert_obj" => [Constant(2), Constant(mnemonic == "jin" ? (ushort)1 : (ushort)0)],
            "get_sibling" or "get_child" or "get_parent" or "remove_obj" => [Constant(1)],
            "get_prop" or "get_prop_addr" or "get_next_prop" => [Constant(1), Constant(10)],
            "get_prop_len" => [Constant(502)],
            "put_prop" => [Constant(1), Constant(10), Constant(0x4321)],
            _ => throw new InvalidOperationException()
        };
        var storesResult = mnemonic is "get_sibling" or "get_child" or "get_parent" or "get_prop" or "get_prop_addr" or "get_next_prop" or "get_prop_len";
        var branches = mnemonic is "jin" or "test_attr" or "get_sibling" or "get_child";
        var definition = new OpcodeDefinition(OpcodeClass.TwoOperand, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), storesResult, branches);
        var instruction = new DecodedInstruction(850, 851, InstructionForm.Extended, OpcodeClass.TwoOperand, 0,
            definition, operands.ToImmutableArray(), storesResult ? (byte)16 : null,
            branches ? new DecodedBranch(true, 2, 853, null) : null, ImmutableArray<byte>.Empty);

        executor.Execute(instruction, state);

        switch (mnemonic)
        {
            case "jin":
            case "get_child":
                Assert.Equal((uint)853, state.ProgramCounter);
                break;
            case "get_sibling":
            case "test_attr":
                Assert.Equal((uint)851, state.ProgramCounter);
                break;
            case "get_parent":
            case "get_prop":
            case "get_prop_addr":
            case "get_next_prop":
            case "get_prop_len":
                Assert.Equal((ushort)(mnemonic switch
                {
                    "get_parent" => 0,
                    "get_prop" => 0xABCD,
                    "get_prop_addr" => 502,
                    "get_next_prop" => 5,
                    _ => 2
                }), state.Memory.ReadWord(64));
                break;
            case "set_attr":
                Assert.True(new ZMachineObjectTable(state.Memory).TestAttribute(2, 0));
                break;
            case "clear_attr":
                Assert.False(new ZMachineObjectTable(state.Memory).TestAttribute(2, 0));
                break;
            case "put_prop":
                Assert.Equal((ushort)0x4321, state.Memory.ReadWord(502));
                break;
        }
    }

    [Fact]
    public void PrintObjectEmitsItsShortNameAndRequiresOutputCallback()
    {
        var state = CreateState(5, includeName: true);
        var definition = new OpcodeDefinition(OpcodeClass.OneOperand, 10, "print_obj", 1, 8, 1, 1);
        var instruction = new DecodedInstruction(850, 851, InstructionForm.Short, OpcodeClass.OneOperand, 10,
            definition, [Constant(1)], null, null, ImmutableArray<byte>.Empty);
        var output = new List<string>();

        new ZMachineOpcodeExecutor(output.Add).Execute(instruction, state);

        Assert.Equal(new[] { "a" }, output);
        var noOutputState = CreateState(5, includeName: true);
        Assert.Throws<ZMachineExecutionException>(() => new ZMachineOpcodeExecutor().Execute(instruction, noOutputState));
    }

    private static ZMachineExecutionState CreateState(byte version, int extendedPropertyLength = 0, bool includeName = false)
    {
        var bytes = CreateStoryBytes(version, extendedPropertyLength, includeName);
        if (version == 6)
        {
            bytes[800] = 0;
        }

        SetObject(bytes, version, 1, 0, 0, 2, includeName ? (ushort)540 : (ushort)500);
        SetObject(bytes, version, 2, 1, 3, 0, 520);
        SetObject(bytes, version, 3, 1, 0, 0, 520);
        if (includeName)
        {
            bytes[540] = 1;
            StoryFixtureBuilder.WriteWord(bytes, 541, 0x98A5);
            bytes[543] = 0;
        }

        UpdateChecksum(bytes);
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static byte[] CreateStoryBytes(byte version, int extendedPropertyLength = 0, bool includeName = false)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        var objectStart = 300 + (version <= 3 ? 62 : 126);
        Array.Clear(bytes, objectStart, 600 - objectStart);
        StoryFixtureBuilder.WriteWord(bytes, 306, 0xCAFE);
        StoryFixtureBuilder.WriteWord(bytes, 308, 0xCAFE);
        if (includeName)
        {
            bytes[540] = 1;
            StoryFixtureBuilder.WriteWord(bytes, 541, 0x98A5);
            bytes[543] = 0;
        }

        var tableAddress = includeName ? 550 : 500;
        bytes[tableAddress] = 0;
        var cursor = tableAddress + 1;
        if (version <= 3)
        {
            bytes[cursor++] = 0x2A; // property 10, length 2
            StoryFixtureBuilder.WriteWord(bytes, cursor, 0xABCD);
            cursor += 2;
            bytes[cursor++] = 0x05; // property 5, one byte
            bytes[cursor++] = 0x34;
        }
        else if (extendedPropertyLength > 0)
        {
            bytes[cursor++] = 0x80 | 10;
            bytes[cursor++] = (byte)(0x80 | (extendedPropertyLength == 64 ? 0 : extendedPropertyLength));
            cursor += extendedPropertyLength;
            bytes[cursor++] = 0x45; // property 5, one byte
            bytes[cursor++] = 0x34;
        }
        else
        {
            bytes[cursor++] = 0x4A; // property 10, two bytes
            StoryFixtureBuilder.WriteWord(bytes, cursor, 0xABCD);
            cursor += 2;
            bytes[cursor++] = 0x05; // property 5, one byte
            bytes[cursor++] = 0x34;
        }

        bytes[cursor] = 0;
        return bytes;
    }

    private static void SetObject(byte[] bytes, byte version, ushort objectNumber, ushort parent, ushort sibling,
        ushort child, ushort propertyTable)
    {
        var entry = 300 + (version <= 3 ? 62 : 126) + (objectNumber - 1) * (version <= 3 ? 9 : 14);
        if (version <= 3)
        {
            bytes[entry + 4] = (byte)parent;
            bytes[entry + 5] = (byte)sibling;
            bytes[entry + 6] = (byte)child;
            StoryFixtureBuilder.WriteWord(bytes, entry + 7, propertyTable);
        }
        else
        {
            StoryFixtureBuilder.WriteWord(bytes, entry + 6, parent);
            StoryFixtureBuilder.WriteWord(bytes, entry + 8, sibling);
            StoryFixtureBuilder.WriteWord(bytes, entry + 10, child);
            StoryFixtureBuilder.WriteWord(bytes, entry + 12, propertyTable);
        }
    }

    private static void UpdateChecksum(byte[] bytes) =>
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);
}

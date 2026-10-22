using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineObjectTableTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ReadsVersionedLinksAndAttributeBits(byte version)
    {
        var state = CreatePopulatedState(version);
        var table = new ZMachineObjectTable(state);
        var lastAttribute = version <= 3 ? (ushort)31 : (ushort)47;

        Assert.Equal(0, table.GetParent(1));
        Assert.Equal(2, table.GetChild(1));
        Assert.Equal(3, table.GetSibling(2));
        Assert.True(table.IsChild(2, 1));
        Assert.False(table.IsChild(0, 1));
        Assert.True(table.HasAttribute(1, 0));
        Assert.True(table.HasAttribute(1, lastAttribute));
        Assert.False(table.HasAttribute(2, 0));
    }

    [Fact]
    public void ReadsDefaultsAndV1ThroughV3PropertiesAndUpdatesOneAndTwoByteValues()
    {
        var state = CreatePopulatedState(3);
        var table = new ZMachineObjectTable(state);

        Assert.Equal(0x0077, table.GetProperty(1, 31));
        Assert.Equal(0x1234, table.GetProperty(1, 5));
        Assert.Equal(0xbeef, table.GetProperty(1, 3));
        Assert.Equal(0x104, table.GetPropertyAddress(1, 5));
        Assert.Equal(0, table.GetPropertyAddress(1, 3));
        Assert.Equal(1, table.GetPropertyLength(0x102));
        Assert.Equal(2, table.GetPropertyLength(0x104));
        Assert.Equal(0, table.GetPropertyLength(0));
        Assert.Equal(31, table.GetNextProperty(1, 0));
        Assert.Equal(5, table.GetNextProperty(1, 31));
        Assert.Equal(0, table.GetNextProperty(1, 5));

        table.SetProperty(1, 31, 0x12ff);
        table.SetProperty(1, 5, 0xabcd);

        Assert.Equal(0xff, state.Memory.ReadByte(0x102));
        Assert.Equal(0xabcd, state.Memory.ReadWord(0x104));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void ParsesV4PlusLongPropertyHeadersAndZeroLengthAs64(byte version)
    {
        var state = CreatePopulatedState(version);
        var table = new ZMachineObjectTable(state);

        Assert.Equal(64, table.GetPropertyLength(0x183));
        Assert.Equal(0x183, table.GetPropertyAddress(1, 63));
        Assert.Equal(0x1122, table.GetProperty(1, 2));
        Assert.Equal(63, table.GetNextProperty(1, 0));
        Assert.Equal(2, table.GetNextProperty(1, 63));
        Assert.Equal(0, table.GetNextProperty(1, 2));

        table.SetProperty(1, 2, 0xabcd);

        Assert.Equal(0xabcd, state.Memory.ReadWord(0x1c4));
        Assert.Throws<ZMachineRuntimeException>(() => table.GetProperty(1, 63));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void AttributeMutationUsesVersionSpecificRangesAndBitOrder(byte version)
    {
        var state = CreatePopulatedState(version);
        var table = new ZMachineObjectTable(state);
        var lastAttribute = version <= 3 ? (ushort)31 : (ushort)47;

        table.ClearAttribute(1, 0);
        table.ClearAttribute(1, lastAttribute);
        Assert.False(table.HasAttribute(1, 0));
        Assert.False(table.HasAttribute(1, lastAttribute));

        table.SetAttribute(1, 0);
        table.SetAttribute(1, lastAttribute);
        Assert.True(table.HasAttribute(1, 0));
        Assert.True(table.HasAttribute(1, lastAttribute));

        Assert.Throws<ZMachineRuntimeException>(() => table.SetAttribute(1, (ushort)(lastAttribute + 1)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void RemoveAndInsertMaintainParentSiblingAndChildLinks(byte version)
    {
        var state = CreatePopulatedState(version);
        var table = new ZMachineObjectTable(state);

        table.RemoveObject(2);
        Assert.Equal(3, table.GetChild(1));
        Assert.Equal(0, table.GetParent(2));
        Assert.Equal(0, table.GetSibling(2));

        table.InsertObject(2, 1);
        Assert.Equal(2, table.GetChild(1));
        Assert.Equal(3, table.GetSibling(2));
        Assert.Equal(1, table.GetParent(2));
        Assert.Equal(1, table.GetParent(3));

        Assert.Throws<ZMachineRuntimeException>(() => table.InsertObject(1, 2));
    }

    [Fact]
    public void RejectsMissingPropertiesAndInvalidNumbers()
    {
        var table = new ZMachineObjectTable(CreatePopulatedState(5));

        Assert.Throws<ZMachineRuntimeException>(() => table.SetProperty(1, 4, 1));
        Assert.Throws<ZMachineRuntimeException>(() => table.GetProperty(1, 64));
        Assert.Throws<ZMachineRuntimeException>(() => table.GetProperty(1, 0));
        Assert.Throws<ZMachineRuntimeException>(() => table.RemoveObject(0));
    }

    [Fact]
    public void IgnoresAttributeMutationsOnNullObjectForInfocomCompatibility()
    {
        var table = new ZMachineObjectTable(CreatePopulatedState(3));

        table.SetAttribute(0, 22);
        table.ClearAttribute(0, 22);

        Assert.False(table.HasAttribute(0, 22));
        Assert.Throws<ZMachineRuntimeException>(() => table.SetAttribute(0, 32));
    }

    [Fact]
    public void ReadsNullObjectPropertiesAsDefaultsWithoutAnAddress()
    {
        var table = new ZMachineObjectTable(CreatePopulatedState(5));

        Assert.Equal(0xbeef, table.GetProperty(0, 3));
        Assert.Equal(0, table.GetPropertyAddress(0, 3));
    }

    [Fact]
    public void RejectsMalformedPropertyOrderAndInvalidTablePointers()
    {
        var state = CreatePopulatedState(5);
        state.Memory.WriteByte(0x181, 0x02);
        state.Memory.WriteByte(0x182, 1);
        state.Memory.WriteByte(0x183, 0x03);
        state.Memory.WriteByte(0x184, 2);
        state.Memory.WriteByte(0x185, 0);
        var table = new ZMachineObjectTable(state);

        Assert.Throws<ZMachineRuntimeException>(() => table.GetNextProperty(1, 0));
        Assert.Throws<ZMachineRuntimeException>(() => table.GetPropertyLength(0x2ff));

        state.Memory.WriteWord((ushort)(ObjectEntryAddress(5, 1) + 12), 0);
        Assert.Throws<ZMachineRuntimeException>(() => table.GetChild(1));
    }

    [Fact]
    public void UsesFirstDefinitionWhenInfocomV3StoryRepeatsProperty31()
    {
        var state = CreatePopulatedState(3);
        state.Memory.WriteByte(0x101, 0x1f); // property 31, one byte
        state.Memory.WriteByte(0x102, 0x11);
        state.Memory.WriteByte(0x103, 0x9f); // duplicate property 31, length 5
        for (var index = 0; index < 5; index++) state.Memory.WriteByte((ushort)(0x104 + index), (byte)(0x20 + index));
        state.Memory.WriteByte(0x109, 0x05); // lower-numbered property
        state.Memory.WriteByte(0x10a, 0x22);
        state.Memory.WriteByte(0x10b, 0);
        var table = new ZMachineObjectTable(state);

        Assert.Equal(0x11, table.GetProperty(1, 31));
        Assert.Equal(31, table.GetNextProperty(1, 0));
        Assert.Equal(5, table.GetNextProperty(1, 31));
        Assert.Equal(0, table.GetNextProperty(1, 5));
    }

    [Fact]
    public void UsesFirstDefinitionWhenInfocomV3StoryRepeatsAnotherProperty()
    {
        var state = CreatePopulatedState(3);
        state.Memory.WriteByte(0x101, 0x08); // property 8, one byte
        state.Memory.WriteByte(0x102, 0x33);
        state.Memory.WriteByte(0x103, 0x08); // duplicate property 8, one byte
        state.Memory.WriteByte(0x104, 0x44);
        state.Memory.WriteByte(0x105, 0x05); // lower-numbered property
        state.Memory.WriteByte(0x106, 0x55);
        state.Memory.WriteByte(0x107, 0);
        var table = new ZMachineObjectTable(state);

        Assert.Equal(0x33, table.GetProperty(1, 8));
        Assert.Equal(8, table.GetNextProperty(1, 0));
        Assert.Equal(5, table.GetNextProperty(1, 8));
        Assert.Equal(0, table.GetNextProperty(1, 5));
    }

    [Fact]
    public void RejectsAscendingPropertyNumbersAfterAllowingV3Duplicates()
    {
        var state = CreatePopulatedState(3);
        state.Memory.WriteByte(0x101, 0x05); // property 5
        state.Memory.WriteByte(0x102, 0x33);
        state.Memory.WriteByte(0x103, 0x08); // invalid increase to property 8
        state.Memory.WriteByte(0x104, 0x44);
        state.Memory.WriteByte(0x105, 0);
        var table = new ZMachineObjectTable(state);

        var exception = Assert.Throws<ZMachineRuntimeException>(() => table.GetProperty(1, 5));

        Assert.Contains("not in descending numerical order", exception.Message);
    }

    [Fact]
    public void ExecutesObjectAndPropertyOpcodesThroughTheDefaultObjectService()
    {
        var story = CreatePopulatedStory(3);
        var code = new byte[]
        {
            0x93, 1, 0x10,            // get_parent 1 -> g0
            0xd1, 0x5f, 1, 5, 0x11,   // get_prop 1 5 -> g1
            0xe3, 0x57, 1, 5, 0x56,   // put_prop 1 5 0x56
            0x93, 1, 0x10,            // get_parent 1 -> g0
            0xd2, 0x5f, 1, 5, 0x12,   // get_prop_addr 1 5 -> g2
            0xd3, 0x5f, 1, 0, 0x13,   // get_next_prop 1 0 -> g3
            0x84, 0x01, 0x04, 0x14    // get_prop_len 0x0104 -> g4
        };
        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal(0, state.ReadVariableValue(16));
        Assert.Equal(0x1234, state.ReadVariableValue(17));
        Assert.Equal(0x56, state.Memory.ReadWord(0x104));
        Assert.Equal(0x104, state.ReadVariableValue(18));
        Assert.Equal(31, state.ReadVariableValue(19));
        Assert.Equal(2, state.ReadVariableValue(20));
    }

    private static ZMachineState CreatePopulatedState(byte version) => new(StoryLoader.Load(CreatePopulatedStory(version)));

    private static byte[] CreatePopulatedStory(byte version)
    {
        var story = MachineStoryFactory.Create(version);
        var propertyTableAddress = version <= 3 ? (ushort)0x100 : (ushort)0x180;
        WriteObject(story, version, 1, propertyTableAddress, parent: 0, sibling: 0, child: 2);
        WriteObject(story, version, 2, 0x120, parent: 1, sibling: 3, child: 0);
        WriteObject(story, version, 3, 0x140, parent: 1, sibling: 0, child: 0);
        var object1 = ObjectEntryAddress(version, 1);
        story[object1] = 0x80;
        story[object1 + (version <= 3 ? 3 : 5)] = 0x01;

        WriteWord(story, 0x44, 0xbeef);
        WriteWord(story, 0x40 + ((version <= 3 ? 31 : 63) * 2) - 2, 0x1234);
        story[0x100] = 0;
        if (version <= 3)
        {
            story[0x101] = 0x1f;
            story[0x102] = 0x77;
            story[0x103] = 0x25;
            story[0x104] = 0x12;
            story[0x105] = 0x34;
            story[0x106] = 0;
            story[0x120] = 0;
            story[0x121] = 0;
            story[0x140] = 0;
            story[0x141] = 0;
        }
        else
        {
            story[0x181] = 0xbf;
            story[0x182] = 0x80;
            for (var index = 0; index < 64; index++)
            {
                story[0x183 + index] = (byte)index;
            }

            story[0x1c3] = 0x42;
            story[0x1c4] = 0x11;
            story[0x1c5] = 0x22;
            story[0x1c6] = 0;
            story[0x120] = 0;
            story[0x121] = 0;
            story[0x140] = 0;
            story[0x141] = 0;
        }

        return story;
    }

    private static void WriteObject(byte[] story, byte version, ushort number, ushort properties, ushort parent, ushort sibling, ushort child)
    {
        var entry = ObjectEntryAddress(version, number);
        var linksAddress = entry + (version <= 3 ? 4 : 6);
        if (version <= 3)
        {
            story[linksAddress] = (byte)parent;
            story[linksAddress + 1] = (byte)sibling;
            story[linksAddress + 2] = (byte)child;
            WriteWord(story, linksAddress + 3, properties);
        }
        else
        {
            WriteWord(story, linksAddress, parent);
            WriteWord(story, linksAddress + 2, sibling);
            WriteWord(story, linksAddress + 4, child);
            WriteWord(story, linksAddress + 6, properties);
        }
    }

    private static int ObjectEntryAddress(byte version, ushort number) =>
        0x40 + ((version <= 3 ? 31 : 63) * sizeof(ushort)) + ((number - 1) * (version <= 3 ? 9 : 14));

    private static void WriteWord(byte[] data, int address, ushort value)
    {
        data[address] = (byte)(value >> 8);
        data[address + 1] = (byte)value;
    }
}

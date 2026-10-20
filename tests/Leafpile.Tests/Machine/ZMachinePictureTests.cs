using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachinePictureTests
{
    [Fact]
    public void PictureDataReportsAvailableCountReleaseDimensionsAndBranchesCorrectly()
    {
        var state = CreateState();
        var executor = new ZMachineOpcodeExecutor(state);
        state.Memory.WriteWord(0x280, 0xaaaa);
        state.Memory.WriteWord(0x282, 0xbbbb);

        executor.Execute(PictureDataInstruction(2, 0x280, branchOnTrue: true));

        Assert.Equal(50, state.Memory.ReadWord(0x280));
        Assert.Equal(100, state.Memory.ReadWord(0x282));
        Assert.Equal(0x208, state.ProgramCounter);

        state.SetProgramCounter(0x201);
        executor.Execute(PictureDataInstruction(0, 0x280, branchOnTrue: true));
        Assert.Equal(3, state.Memory.ReadWord(0x280));
        Assert.Equal(0x1234, state.Memory.ReadWord(0x282));
        Assert.Equal(0x208, state.ProgramCounter);

        state.SetProgramCounter(0x201);
        var untouched = state.Memory.ReadWord(0x300);
        executor.Execute(PictureDataInstruction(200, 0x300, branchOnTrue: true));
        Assert.Equal(untouched, state.Memory.ReadWord(0x300));
        Assert.Equal(0x206, state.ProgramCounter);
    }

    [Fact]
    public void PictureQueriesSetV6PictureCapabilityOnlyWhenBlorbHasImages()
    {
        var withPictures = CreateState();
        var withoutPictures = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(6)));

        Assert.NotEqual(0, withPictures.Memory.ReadByte(1) & 0x02);
        Assert.Equal(0, withoutPictures.Memory.ReadByte(1) & 0x02);
        Assert.Equal(3, withPictures.Screen.AvailablePictureCount);
        Assert.Equal(0, withoutPictures.Screen.AvailablePictureCount);
    }

    [Fact]
    public void ExtendedPictureOpcodesDispatchDrawAndEraseToTheScreenHost()
    {
        var state = CreateState();
        var executor = new ZMachineOpcodeExecutor(state);
        var changes = new List<ZMachinePictureOperation>();
        state.Screen.PictureChanged += changes.Add;

        executor.Execute(PictureInstruction(5, 2, 3, 4));
        state.SetProgramCounter(0x201);
        executor.Execute(PictureInstruction(7, 2, 3, 4));

        Assert.Equal(2, changes.Count);
        Assert.False(changes[0].Erase);
        Assert.True(changes[1].Erase);
        Assert.Equal((4, 3), (changes[0].X, changes[0].Y));
        Assert.Empty(state.Screen.DisplayedPictures);
    }

    [Fact]
    public void DrawAndEraseProduceWindowRelativeRendererOperationsAndAdaptivePalette()
    {
        var state = CreateState();
        var screen = state.Screen;
        var events = new List<ZMachinePictureOperation>();
        screen.PictureChanged += events.Add;
        screen.SelectWindow(2);
        screen.MoveWindow(2, 5, 10);
        screen.ResizeWindow(2, 100, 200);
        screen.SetCursorV6(2, 20, 30);

        screen.DrawPicture(2, 0, 0);

        var drawn = Assert.Single(events);
        Assert.False(drawn.Erase);
        Assert.Equal(2, drawn.Window);
        Assert.Equal(39, drawn.X);
        Assert.Equal(24, drawn.Y);
        Assert.Equal(new BlorbPictureSize(100, 50), drawn.DisplaySize);
        Assert.Single(screen.DisplayedPictures);
        Assert.Equal(new[] { new BlorbColor(20, 40, 60), new BlorbColor(80, 100, 120) }, screen.CurrentAdaptivePalette);

        screen.DrawPicture(9, 70, 100);
        var adaptive = events[1];
        Assert.True(adaptive.Picture.UsesAdaptivePalette);
        Assert.Equal(screen.CurrentAdaptivePalette, adaptive.PaletteOverride);
        Assert.Equal(2, screen.DisplayedPictures.Count);

        screen.DrawPicture(2, 20, 30, erase: true);
        Assert.True(events[2].Erase);
        Assert.Single(screen.DisplayedPictures);
    }

    [Fact]
    public void DrawPictureRejectsMissingResourcesAndRectanglesButAllowsRectangleErase()
    {
        var state = CreateState();
        var changes = new List<ZMachinePictureOperation>();
        state.Screen.PictureChanged += changes.Add;

        Assert.Throws<ZMachineRuntimeException>(() => state.Screen.DrawPicture(404, 1, 1));
        Assert.Throws<ZMachineRuntimeException>(() => state.Screen.DrawPicture(15, 1, 1));
        state.Screen.DrawPicture(15, 1, 1, erase: true);
        Assert.True(Assert.Single(changes).Erase);
    }

    [Fact]
    public void ErasingWindowsRemovesTheirPicturesAndNotifiesTheDisplayHost()
    {
        var state = CreateState();
        var windowsCleared = new List<int?>();
        state.Screen.PictureAreaCleared += windowsCleared.Add;
        state.Screen.DrawPicture(2, 10, 10);

        state.Screen.EraseWindow(0);

        Assert.Empty(state.Screen.DisplayedPictures);
        Assert.Equal(new int?[] { 0 }, windowsCleared);

        state.Screen.DrawPicture(2, 10, 10);
        state.Screen.EraseWindow(-2);
        Assert.Empty(state.Screen.DisplayedPictures);
        Assert.Equal(new int?[] { 0, null }, windowsCleared);
    }

    private static ZMachineState CreateState()
    {
        var story = MachineStoryFactory.Create(6);
        var blorb = BlorbFixture.Build(
        [
            new("Exec", 0, "ZCOD", story),
            new("Pict", 2, "PNG ", Png(200, 100, [new BlorbColor(20, 40, 60), new BlorbColor(80, 100, 120)], indexed: true)),
            new("Pict", 9, "PNG ", Png(30, 15, [new BlorbColor(1, 2, 3)], indexed: true)),
            new("Pict", 15, "Rect", UInt32Bytes(12, 7))
        ],
        [
            ("RelN", new byte[] { 0x12, 0x34 }),
            ("APal", UInt32Bytes(9)),
            ("Reso", UInt32Bytes(640, 400, 0, 0, 0, 0, 2, 1, 2, 0, 0, 0, 0))
        ]);
        return new ZMachineState(StoryLoader.Load(blorb));
    }

    private static ZMachineInstruction PictureDataInstruction(uint number, ushort table, bool branchOnTrue) =>
        new(0x201, 0x206, ZMachineInstructionForm.Extended, ZMachineOpcodeClass.Extended, 6, 2,
            [new(ZMachineOperandType.LargeConstant, checked((ushort)number)), new(ZMachineOperandType.LargeConstant, table)],
            null, new ZMachineBranch(branchOnTrue, 4), null);

    private static ZMachineInstruction PictureInstruction(int opcode, ushort picture, ushort y, ushort x) =>
        new(0x201, 0x206, ZMachineInstructionForm.Extended, ZMachineOpcodeClass.Extended, opcode, 3,
            [new(ZMachineOperandType.LargeConstant, picture), new(ZMachineOperandType.LargeConstant, y), new(ZMachineOperandType.LargeConstant, x)],
            null, null, null);

    private static byte[] Png(uint width, uint height, IReadOnlyList<BlorbColor> colors, bool indexed = false)
    {
        var result = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = indexed ? (byte)3 : (byte)2;
        AddPngChunk(result, "IHDR", header);
        if (indexed || colors.Count > 0)
        {
            AddPngChunk(result, "PLTE", colors.SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray());
        }

        AddPngChunk(result, "IDAT", [0, 0]);
        AddPngChunk(result, "IEND", []);
        return result.ToArray();
    }

    private static byte[] UInt32Bytes(params uint[] values)
    {
        var result = new List<byte>();
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            result.AddRange(bytes.ToArray());
        }

        return result.ToArray();
    }

    private static void AddPngChunk(List<byte> target, string id, byte[] data)
    {
        target.AddRange(UInt32Bytes(checked((uint)data.Length)));
        target.AddRange(Encoding.ASCII.GetBytes(id));
        target.AddRange(data);
        target.AddRange(UInt32Bytes(0));
    }

    private static class BlorbFixture
    {
        public static byte[] Build(IReadOnlyList<(string Usage, uint Number, string Id, byte[] Data)> resources, IReadOnlyList<(string Id, byte[] Data)> optional)
        {
            var indexLength = 4 + (resources.Count * 12);
            var resourceStart = 20 + indexLength;
            var offsets = new int[resources.Count];
            var cursor = resourceStart;
            for (var index = 0; index < resources.Count; index++)
            {
                offsets[index] = cursor;
                cursor += 8 + resources[index].Data.Length + (resources[index].Data.Length & 1);
            }

            foreach (var chunk in optional)
            {
                cursor += 8 + chunk.Data.Length + (chunk.Data.Length & 1);
            }

            var result = new byte[cursor];
            WriteFourCc(result, 0, "FORM");
            WriteUInt32(result, 4, (uint)(result.Length - 8));
            WriteFourCc(result, 8, "IFRS");
            WriteFourCc(result, 12, "RIdx");
            WriteUInt32(result, 16, (uint)indexLength);
            WriteUInt32(result, 20, (uint)resources.Count);
            for (var index = 0; index < resources.Count; index++)
            {
                var entry = 24 + (index * 12);
                WriteFourCc(result, entry, resources[index].Usage);
                WriteUInt32(result, entry + 4, resources[index].Number);
                WriteUInt32(result, entry + 8, (uint)offsets[index]);
            }

            cursor = resourceStart;
            foreach (var resource in resources)
            {
                cursor = WriteChunk(result, cursor, resource.Id, resource.Data);
            }

            foreach (var chunk in optional)
            {
                cursor = WriteChunk(result, cursor, chunk.Id, chunk.Data);
            }

            return result;
        }

        private static int WriteChunk(byte[] target, int offset, string id, byte[] data)
        {
            WriteFourCc(target, offset, id);
            WriteUInt32(target, offset + 4, (uint)data.Length);
            data.CopyTo(target, offset + 8);
            var end = offset + 8 + data.Length;
            return end + (data.Length & 1);
        }

        private static void WriteFourCc(byte[] target, int offset, string text) => Encoding.ASCII.GetBytes(text).CopyTo(target, offset);

        private static void WriteUInt32(byte[] target, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset, 4), value);
    }
}

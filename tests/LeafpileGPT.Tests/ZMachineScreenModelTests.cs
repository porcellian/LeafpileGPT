namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineScreenModelTests
{
    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 25)]
    [InlineData(3, 25)]
    [InlineData(4, 25)]
    [InlineData(5, 1)]
    public void StartsWithVersionSpecificLowerWindowCursor(byte version, int row)
    {
        var screen = CreateState(version).OutputStreams.Screen;

        Assert.Equal(new ZMachineCursorPosition(row, 1), screen.GetCursor(0));
        Assert.Equal(80, screen.WidthInCharacters);
        Assert.Equal(25, screen.HeightInLines);
    }

    [Fact]
    public void WritesInterpreterCapabilitiesAndVersionedDimensionsToHeader()
    {
        var v3 = CreateState(3);
        var v3Screen = v3.OutputStreams.Screen;
        Assert.NotEqual((byte)0, v3.Memory.ReadByte(1) & 0x20);
        Assert.NotEqual((byte)0, v3.Memory.ReadByte(1) & 0x10);
        v3Screen.StatusLineWriter = _ => { };
        Assert.Equal((byte)0, (byte)(v3.Memory.ReadByte(1) & 0x10));
        v3Screen.SupportsUpperWindow = false;
        Assert.Equal((byte)0, (byte)(v3.Memory.ReadByte(1) & 0x20));

        var v5 = CreateState(5);
        v5.OutputStreams.Screen.SetDimensions(60, 18, 480, 288);
        Assert.Equal((byte)18, v5.Memory.ReadByte(0x20));
        Assert.Equal((byte)60, v5.Memory.ReadByte(0x21));
        Assert.Equal((ushort)480, v5.Memory.ReadWord(0x22));
        Assert.Equal((ushort)288, v5.Memory.ReadWord(0x24));
        Assert.NotEqual((byte)0, v5.Memory.ReadByte(1) & 0x01);
    }

    [Fact]
    public void V3SplitClearsTheUpperWindowAndSelectingItResetsItsCursor()
    {
        var screen = CreateState(3).OutputStreams.Screen;
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;

        screen.SplitWindow(4);
        screen.SetWindow(1);
        screen.WriteText("sonar", null);

        Assert.Equal(4, screen.UpperWindowHeight);
        Assert.Equal(new ZMachineCursorPosition(1, 6), screen.CurrentCursor);
        Assert.Contains(operations, item => item.Kind == ZMachineScreenOperationKind.ClearWindow && item.Window == 1);
        Assert.Contains(operations, item => item.Kind == ZMachineScreenOperationKind.SelectWindow && item.Window == 1);
        Assert.Throws<ZMachineExecutionException>(() => screen.SplitWindow(25));
    }

    [Fact]
    public void V4SplitKeepsLowerCursorOnBottomAndV5MovesItBelowTheSplit()
    {
        var v4 = CreateState(4).OutputStreams.Screen;
        v4.SetDimensions(80, 10);
        v4.SplitWindow(3);
        Assert.Equal(new ZMachineCursorPosition(10, 1), v4.GetCursor(0));
        v4.SetWindow(1);
        v4.SetCursor(2, 8);
        Assert.Equal(new ZMachineCursorPosition(2, 8), v4.CurrentCursor);
        v4.EraseWindow(1);
        Assert.Equal(new ZMachineCursorPosition(1, 1), v4.GetCursor(1));
        v4.EraseWindow(0);
        Assert.Equal(new ZMachineCursorPosition(10, 1), v4.GetCursor(0));

        var v5 = CreateState(5).OutputStreams.Screen;
        v5.SetDimensions(80, 10);
        v5.SplitWindow(3);
        Assert.Equal(new ZMachineCursorPosition(4, 1), v5.GetCursor(0));
    }

    [Fact]
    public void V4LowerWindowScrollsAndKeepsItsCursorOnTheBottomLine()
    {
        var screen = CreateState(4).OutputStreams.Screen;
        screen.SetDimensions(3, 2);
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;

        screen.WriteText("a\nb\n", null);

        Assert.Equal(new ZMachineCursorPosition(2, 1), screen.GetCursor(0));
        Assert.Equal(2, operations.Count(operation => operation.Kind == ZMachineScreenOperationKind.ScrollWindow));
    }

    [Fact]
    public void V5CursorGetEraseStyleColoursAndBufferingAreModeled()
    {
        var state = CreateState(5);
        var screen = state.OutputStreams.Screen;
        screen.SetDimensions(10, 5, 80, 80);
        screen.SplitWindow(2);
        screen.SetWindow(1);
        screen.SetCursor(2, 4);
        screen.StoreCursorPosition(400);
        Assert.Equal((ushort)2, state.Memory.ReadWord(400));
        Assert.Equal((ushort)4, state.Memory.ReadWord(402));

        screen.SetTextStyle(5);
        Assert.Equal(ZMachineTextStyle.ReverseVideo | ZMachineTextStyle.Italic, screen.TextStyle);
        screen.SetTextStyle(0);
        Assert.Equal(ZMachineTextStyle.Roman, screen.TextStyle);
        screen.SetColours(3, 4);
        Assert.Equal((byte)3, screen.ForegroundColour);
        Assert.Equal((byte)4, screen.BackgroundColour);
        Assert.Equal((byte)4, state.Memory.ReadByte(0x2C));
        Assert.Equal((byte)3, state.Memory.ReadByte(0x2D));
        screen.SetColours(1, 1);
        Assert.Equal((byte)9, screen.ForegroundColour);
        Assert.Equal((byte)2, screen.BackgroundColour);
        Assert.Throws<ZMachineExecutionException>(() => screen.SetColours(10, 2));

        state.OutputStreams.SetBuffering(false);
        Assert.False(screen.IsBufferingEnabled);
        screen.SetWindow(1);
        Assert.False(screen.IsBufferingEnabled);
        screen.SetWindow(0);
        state.OutputStreams.SetBuffering(true);
        Assert.True(screen.IsBufferingEnabled);

        screen.EraseWindow(-2);
        Assert.Equal(2, screen.UpperWindowHeight);
        screen.EraseLine(1);
        screen.EraseWindow(-1);
        Assert.Equal((short)0, screen.SelectedWindow);
        Assert.Equal(0, screen.UpperWindowHeight);
        Assert.Equal(new ZMachineCursorPosition(1, 1), screen.GetCursor(0));
    }

    [Fact]
    public void StatusLineUsesGlobalLocationScoreAndTimeBeforeReadOnlyWhenRequested()
    {
        var state = CreateState(3);
        WriteObjectName(state.Memory, "hall");
        state.Memory.WriteWord(64, 1);
        state.Memory.WriteWord(66, unchecked((ushort)-3));
        state.Memory.WriteWord(68, 17);
        var statusLines = new List<ZMachineStatusLine>();
        state.OutputStreams.Screen.StatusLineWriter = statusLines.Add;

        state.OutputStreams.Screen.ShowStatusLine(state);
        Assert.Equal(new ZMachineStatusLine("hall", "Score: -3 Moves: 17", false), statusLines[^1]);

        state.Memory.WriteByte(1, (byte)(state.Memory.ReadByte(1) | 0x02));
        state.Memory.WriteWord(66, 4);
        state.Memory.WriteWord(68, 5);
        state.OutputStreams.Screen.ShowStatusLine(state);
        Assert.Equal(new ZMachineStatusLine("hall", "04:05", true), statusLines[^1]);
    }

    [Fact]
    public async Task MorePromptPausesSubsequentScreenTextUntilAcknowledged()
    {
        var screen = CreateState(5).OutputStreams.Screen;
        screen.SetDimensions(3, 2, 3, 2);
        var output = new List<string>();
        var operations = new List<ZMachineScreenOperation>();
        var eventOrder = new List<string>();
        screen.OperationWriter = operation =>
        {
            operations.Add(operation);
            if (operation.Kind == ZMachineScreenOperationKind.MorePrompt)
            {
                eventOrder.Add("more");
            }
        };
        screen.WriteText("a\nb\nlater", text =>
        {
            output.Add(text);
            eventOrder.Add("text");
        });
        Assert.True(screen.IsWaitingForMore);
        Assert.Equal(new[] { "a\nb\n" }, output);
        Assert.Contains(operations, item => item.Kind == ZMachineScreenOperationKind.MorePrompt);
        Assert.Equal(new[] { "text", "more" }, eventOrder);

        await screen.WaitForMoreAsync(_ => ValueTask.FromResult((ushort)32), TestContext.Current.CancellationToken);

        Assert.False(screen.IsWaitingForMore);
        Assert.Equal(new[] { "a\nb\n", "later" }, output);
        Assert.Contains(operations, item => item.Kind == ZMachineScreenOperationKind.MorePromptCleared);
    }

    [Fact]
    public void ScreenOpcodesDispatchThroughTheExecutor()
    {
        var state = CreateState(5);
        state.OutputStreams.Screen.SetDimensions(80, 10);
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("split_window", [Constant(3)]), state);
        executor.Execute(Instruction("set_window", [Constant(1)]), state);
        executor.Execute(Instruction("set_cursor", [Constant(2), Constant(4)]), state);
        executor.Execute(Instruction("get_cursor", [Constant(400)]), state);

        Assert.Equal((ushort)2, state.Memory.ReadWord(400));
        Assert.Equal((ushort)4, state.Memory.ReadWord(402));
    }

    private static ZMachineExecutionState CreateState(byte version)
    {
        var bytes = CreateStory(version);
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static byte[] CreateStory(byte version) => StoryFixtureBuilder.Create(version);

    private static void WriteObjectName(ZMachineMemory memory, string name)
    {
        var entryAddress = 300 + (31 * 2);
        memory.WriteWord(checked((ushort)(entryAddress + 7)), 500);
        var packedName = new ZMachineTextCodec(memory).EncodeDictionaryWord(name);
        memory.WriteByte(500, checked((byte)(packedName.Length / 2)));
        for (var index = 0; index < packedName.Length; index++)
        {
            memory.WriteByte(checked((ushort)(501 + index)), packedName[index]);
        }

        memory.WriteByte(checked((ushort)(501 + packedName.Length)), 0);
    }

    private static DecodedInstruction Instruction(string mnemonic, DecodedOperand[] operands)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Variable, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length));
        return new DecodedInstruction(850, 851, InstructionForm.Variable, OpcodeClass.Variable, 0,
            definition, operands.ToImmutableArray(), null, null, ImmutableArray<byte>.Empty);
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);
}

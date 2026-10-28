namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineOutputStreamsTests
{
    [Fact]
    public void ScreenAndTranscriptCanBeSelectedIndependentlyAndSynchronizeFlags2()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        var transcript = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        state.OutputStreams.TranscriptWriter = transcript.Add;

        state.OutputStreams.SelectStream(2);
        state.OutputStreams.WriteText("both");
        state.OutputStreams.SelectStream(-1);
        state.OutputStreams.WriteText("transcript");
        state.OutputStreams.SelectStream(1);
        state.OutputStreams.SelectStream(-2);
        state.OutputStreams.WriteText("screen");

        Assert.Equal(new[] { "both", "screen" }, screen);
        Assert.Equal(new[] { "both", "transcript" }, transcript);
        Assert.Equal((ushort)0, state.Memory.ReadWord(0x10));
        Assert.False(state.OutputStreams.TranscriptSelected);
    }

    [Fact]
    public void DirectFlags2ChangesAreAppliedBeforeTheNextOutput()
    {
        var state = CreateState(3);
        var transcript = new List<string>();
        state.OutputStreams.TranscriptWriter = transcript.Add;

        state.Memory.WriteWord(0x10, 1);
        state.OutputStreams.WriteText("enabled");
        state.Memory.WriteWord(0x10, 0);
        state.OutputStreams.WriteText("disabled");

        Assert.Equal(new[] { "enabled" }, transcript);
    }

    [Fact]
    public void MemoryStreamsSuppressOtherStreamsAndSupportNestedTables()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        state.OutputStreams.SelectStream(3, 400);
        state.OutputStreams.WriteText("A\n");
        state.OutputStreams.SelectStream(3, 500);
        state.OutputStreams.WriteText("é😀");

        Assert.Equal(2, state.OutputStreams.MemoryStreamDepth);
        Assert.Equal((ushort)500, state.OutputStreams.CurrentMemoryTableAddress);
        state.OutputStreams.SelectStream(-3);
        state.OutputStreams.WriteText("B");
        state.OutputStreams.SelectStream(-3);

        Assert.Equal(0, state.OutputStreams.MemoryStreamDepth);
        Assert.Equal((ushort)3, state.Memory.ReadWord(400));
        Assert.Equal((byte)'A', state.Memory.ReadByte(402));
        Assert.Equal((byte)13, state.Memory.ReadByte(403));
        Assert.Equal((byte)'B', state.Memory.ReadByte(404));
        Assert.Equal((ushort)2, state.Memory.ReadWord(500));
        Assert.InRange(state.Memory.ReadByte(502), (byte)155, (byte)251);
        Assert.Equal((byte)'?', state.Memory.ReadByte(503));
        Assert.Empty(screen);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void MemoryStreamIsAvailableAcrossVersionsThreeThroughEight(byte version)
    {
        var state = CreateState(version);
        state.OutputStreams.SelectStream(3, 400);
        state.OutputStreams.WriteText("ok");
        state.OutputStreams.SelectStream(-3);

        Assert.Equal((ushort)2, state.Memory.ReadWord(400));
        Assert.Equal((byte)'o', state.Memory.ReadByte(402));
        Assert.Equal((byte)'k', state.Memory.ReadByte(403));
    }

    [Fact]
    public void MemoryStreamNestingIsLimitedToSixteenAndMustBeClosed()
    {
        var state = CreateState(5);
        for (var index = 0; index < 16; index++)
        {
            state.OutputStreams.SelectStream(3, checked((ushort)(400 + (index * 4))));
        }

        Assert.Throws<ZMachineExecutionException>(() => state.OutputStreams.SelectStream(3, 500));
        for (var index = 0; index < 16; index++)
        {
            state.OutputStreams.SelectStream(-3);
        }

        Assert.Throws<ZMachineExecutionException>(() => state.OutputStreams.SelectStream(-3));
    }

    [Fact]
    public void MemoryStreamValidatesTableAndOutputAgainstDynamicMemory()
    {
        var state = CreateState(5);

        Assert.Throws<ZMachineMemoryAccessException>(() => state.OutputStreams.SelectStream(3, 699));
        state.OutputStreams.SelectStream(3, 698);

        Assert.Throws<ZMachineMemoryAccessException>(() => state.OutputStreams.WriteText("x"));
    }

    [Fact]
    public void StreamFourRecordsWholeCommandsAndIndividualKeypressesOnly()
    {
        var state = CreateState(3);
        var screen = new List<string>();
        var commands = new List<string>();
        var keys = new List<byte>();
        state.OutputStreams.ScreenWriter = screen.Add;
        state.OutputStreams.CommandWriter = commands.Add;
        state.OutputStreams.KeyPressWriter = keys.Add;

        state.OutputStreams.SelectStream(4);
        state.OutputStreams.WriteText("story text");
        state.OutputStreams.RecordCommand("look");
        state.OutputStreams.RecordKeyPress(13);
        state.OutputStreams.SelectStream(-4);
        state.OutputStreams.RecordCommand("ignored");

        Assert.Equal(new[] { "story text" }, screen);
        Assert.Equal(new[] { "look\n" }, commands);
        Assert.Equal(new byte[] { 13 }, keys);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void BufferModeChangesAreReportedForVersionsFourAndFive(byte version)
    {
        var state = CreateState(version);
        var changes = new List<bool>();
        state.OutputStreams.BufferingChanged += changes.Add;

        state.OutputStreams.SetBuffering(false);
        state.OutputStreams.SetBuffering(false);
        state.OutputStreams.SetBuffering(true);

        Assert.Equal(new[] { false, true }, changes);
        Assert.True(state.OutputStreams.BufferingEnabled);
    }

    [Fact]
    public void VersionSixMemoryStreamStoresPrintedWidthUnitsAtHeaderOffsetThirty()
    {
        var state = CreateState(6);
        state.OutputStreams.MeasureCharacterWidth = codePoint => codePoint == 'ç' ? 2 : 1;
        state.OutputStreams.SelectStream(3, 400);
        state.OutputStreams.WriteText("ab\nç");
        state.OutputStreams.SelectStream(-3);

        Assert.Equal((ushort)4, state.Memory.ReadWord(0x30));
    }

    [Fact]
    public void InputEchoUsesVersionSpecificTranscriptRulesAndYieldsToMemoryStream()
    {
        var versionFive = CreateState(5);
        var transcript = new List<string>();
        versionFive.OutputStreams.TranscriptWriter = transcript.Add;
        versionFive.OutputStreams.SelectStream(2);
        versionFive.OutputStreams.EchoInput("look");
        Assert.Equal(new[] { "look" }, transcript);

        var versionSix = CreateState(6);
        var versionSixTranscript = new List<string>();
        versionSix.OutputStreams.TranscriptWriter = versionSixTranscript.Add;
        versionSix.OutputStreams.SelectStream(2);
        versionSix.OutputStreams.EchoInput("look");
        Assert.Empty(versionSixTranscript);

        versionFive.OutputStreams.SelectStream(3, 400);
        versionFive.OutputStreams.EchoInput("hidden");
        versionFive.OutputStreams.SelectStream(-3);
        Assert.Equal((ushort)0, versionFive.Memory.ReadWord(400));
    }

    [Fact]
    public void OutputOpcodesRouteTextNumbersCharactersAndStreams()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("print_num", [Constant(ushort.MaxValue)]), state);
        executor.Execute(Instruction("print_char", [Constant('!')]), state);
        executor.Execute(Instruction("new_line", []), state);
        executor.Execute(Instruction("print_unicode", [Constant(0x263A)]), state);
        executor.Execute(Instruction("buffer_mode", [Constant(0)]), state);
        executor.Execute(Instruction("output_stream", [Constant(3), Constant(500)]), state);
        executor.Execute(Instruction("print_char", [Constant('X')]), state);
        executor.Execute(Instruction("output_stream", [Constant(0xFFFD)]), state);

        Assert.Equal(new[] { "-1", "!", "\n", "☺" }, screen);
        Assert.False(state.OutputStreams.BufferingEnabled);
        Assert.Equal((ushort)1, state.Memory.ReadWord(500));
        Assert.Equal((byte)'X', state.Memory.ReadByte(502));
    }

    [Fact]
    public void PrintAddressAndPackedAddressDecodeTerminatedStrings()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        var executor = new ZMachineOpcodeExecutor();
        var codec = new ZMachineTextCodec(state.Memory);
        var encoded = codec.EncodeDictionaryWord("door");
        for (var index = 0; index < encoded.Length; index++)
        {
            state.Memory.WriteByte(checked((ushort)(550 + index)), encoded[index]);
            state.Memory.WriteByte(checked((ushort)(600 + index)), encoded[index]);
        }

        executor.Execute(Instruction("print_addr", [Constant(550)]), state);
        executor.Execute(Instruction("print_paddr", [Constant(150)]), state);

        Assert.Equal("doordoor", string.Concat(screen));
    }

    [Fact]
    public void NullZsciiHasNoEffectInEverySelectedStream()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        state.OutputStreams.SelectStream(2);
        state.OutputStreams.TranscriptWriter = screen.Add;
        state.OutputStreams.SelectStream(3, 500);
        state.OutputStreams.WriteZsciiCharacter(0, new ZMachineTextCodec(state.Memory).Zscii);
        state.OutputStreams.SelectStream(-3);

        Assert.Empty(screen);
        Assert.Equal((ushort)0, state.Memory.ReadWord(500));
    }

    [Fact]
    public void PrintOpcodeDecodesInlineTextAndPrintTableWritesRows()
    {
        var state = CreateState(5);
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        var executor = new ZMachineOpcodeExecutor();
        var packedText = new ZMachineTextCodec(state.Memory).EncodeDictionaryWord("hi");
        executor.Execute(Instruction("print", [], inlineText: packedText), state);
        state.Memory.WriteByte(400, (byte)'A');
        state.Memory.WriteByte(401, (byte)'B');
        state.Memory.WriteByte(402, (byte)'C');
        state.Memory.WriteByte(403, (byte)'D');
        executor.Execute(Instruction("print_table", [Constant(400), Constant(2), Constant(2)]), state);

        Assert.Equal("hiAB\nCD", string.Concat(screen));
    }

    [Fact]
    public void OutputStreamOpcodeRejectsVersionOneAndTwoStreams()
    {
        var state = CreateState(2);

        Assert.Throws<ZMachineExecutionException>(() => state.OutputStreams.SelectStream(3, 400));
        Assert.Throws<ZMachineExecutionException>(() => state.OutputStreams.SelectStream(2));
    }

    private static ZMachineExecutionState CreateState(byte version)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        StoryFixtureBuilder.WriteWord(bytes, 0x10, 0);
        if (version == 6)
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x06, 150);
            bytes[600] = 0;
        }

        if (version >= 3)
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        }

        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static DecodedInstruction Instruction(
        string mnemonic,
        DecodedOperand[] operands,
        ImmutableArray<byte> inlineText = default)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Variable, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), HasInlineText: !inlineText.IsDefaultOrEmpty);
        return new DecodedInstruction(850, 851, InstructionForm.Variable, OpcodeClass.Variable, 0,
            definition, operands.ToImmutableArray(), null, null,
            inlineText.IsDefault ? ImmutableArray<byte>.Empty : inlineText);
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);
}

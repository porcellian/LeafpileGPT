using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineScreenTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 25, 80)]
    [InlineData(5, 25, 80)]
    public void PublishesCharacterAndUnitDimensionsInVersionedHeaderFields(byte version, byte rows, byte columns)
    {
        var state = CreateState(version);

        Assert.Equal(rows, state.ReadHeaderByte(0x20));
        Assert.Equal(columns, state.ReadHeaderByte(0x21));
        if (version == 5)
        {
            Assert.Equal(640, state.ReadHeaderWord(0x22));
            Assert.Equal(400, state.ReadHeaderWord(0x24));
            Assert.Equal(8, state.ReadHeaderByte(0x26));
            Assert.Equal(16, state.ReadHeaderByte(0x27));
        }
    }

    [Theory]
    [InlineData(3, 0x20)]
    [InlineData(4, 0x10)]
    [InlineData(5, 0x11)]
    public void AdvertisesOnlyImplementedVersionedScreenCapabilities(byte version, byte expectedFlags)
    {
        var state = CreateState(version);

        Assert.Equal(expectedFlags, (byte)(state.ReadHeaderByte(1) & 0x31));
    }

    [Fact]
    public void ShowStatusUsesScoreAndMovesAndOnlyUpdatesWhenRequested()
    {
        var state = CreateState(3, 0xbc);
        state.WriteVariable(17, 42);
        state.WriteVariable(18, 7);

        Assert.Equal(string.Empty, state.Screen.StatusLine);
        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Contains("Score: 42", state.Screen.StatusLine);
        Assert.Contains("Moves: 7", state.Screen.StatusLine);
        Assert.Equal("Score: 42  Moves: 7", state.Screen.StatusValues);
    }

    [Fact]
    public void ShowStatusUsesTimeForVersionThreeTimeGames()
    {
        var story = MachineStoryFactory.Create(3);
        story[1] = 0x02;
        story[0x200] = 0xbc;
        var state = new ZMachineState(StoryLoader.Load(story));
        state.WriteVariable(17, 16);
        state.WriteVariable(18, 5);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Contains("16:05", state.Screen.StatusLine);
        Assert.DoesNotContain("Score", state.Screen.StatusLine);
        Assert.Equal("16:05", state.Screen.StatusValues);
    }

    [Fact]
    public void VersionThreeRefreshesStatusImmediatelyBeforeReadingInput()
    {
        var story = MachineStoryFactory.Create(3);
        story[0x80] = 10;
        story[0x90] = 1;
        story[0x300] = 0;
        story[0x301] = 4;
        story[0x302] = 0;
        story[0x303] = 0;
        new byte[] { 0xe4, 0x5f, 0x80, 0x90 }.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        state.WriteVariable(17, 3);
        state.WriteVariable(18, 4);
        var host = new StatusObservingHost(state.Screen);

        new ZMachineOpcodeExecutor(state, inputOutput: host).ExecuteNext();

        Assert.Contains("Score: 3", host.StatusObservedBeforeRead);
        Assert.Contains("Moves: 4", host.StatusObservedBeforeRead);
    }

    [Fact]
    public void VersionThreeSplitClearsUpperWindowAndWindowSelectionResetsItsCursor()
    {
        var state = CreateState(3, 0xea, 0x7f, 3, 0xeb, 0x7f, 1);
        state.Screen.Write("old upper text");
        state.Screen.SelectWindow(1);
        state.Screen.Write("ignored old text");
        state.SetProgramCounter(0x200);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal(3, state.Screen.UpperWindowHeight);
        Assert.Equal(string.Empty, state.Screen.UpperWindowText);
        Assert.Equal(0, state.Screen.CursorRow);
        Assert.Equal(0, state.Screen.CursorColumn);
    }

    [Fact]
    public void VersionFourAndFiveScreenOpcodesTrackCursorStylesColoursAndEraseRules()
    {
        var state = CreateState(5,
            0xea, 0x7f, 4,       // split_window 4
            0xeb, 0x7f, 1,       // set_window 1
            0xef, 0x5f, 2, 9,    // set_cursor 2 9
            0xf1, 0x7f, 0x0a,    // set_text_style fixed pitch + bold
            0xdb, 0x5f, 6, 9,    // set_colour blue on white
            0xee, 0x7f, 1);      // erase_line 1
        var executor = new ZMachineOpcodeExecutor(state);

        for (var index = 0; index < 6; index++)
        {
            executor.ExecuteNext();
        }

        Assert.Equal(1, state.Screen.SelectedWindow);
        Assert.Equal(1, state.Screen.CursorRow);
        Assert.Equal(8, state.Screen.CursorColumn);
        Assert.Equal(10, state.Screen.TextStyle);
        Assert.True(state.Screen.UsesFixedPitch);
        Assert.Equal(0, state.ReadHeaderWord(0x10) & 0x02);
        Assert.Equal(6, state.Screen.Foreground);
        Assert.Equal(9, state.Screen.Background);
    }

    [Fact]
    public void GetCursorStoresOneBasedRowAndColumnInTheSuppliedArray()
    {
        var state = CreateState(4,
            0xea, 0x7f, 2,
            0xeb, 0x7f, 1,
            0xef, 0x5f, 2, 9,
            0xf0, 0x7f, 0x90);
        var executor = new ZMachineOpcodeExecutor(state);

        for (var index = 0; index < 4; index++)
        {
            executor.ExecuteNext();
        }

        Assert.Equal(2, state.Memory.ReadWord(0x90));
        Assert.Equal(9, state.Memory.ReadWord(0x92));
    }

    [Fact]
    public void CursorPositionControlsWhereUpperWindowTextIsDrawn()
    {
        var state = CreateState(4,
            0xea, 0x7f, 2,
            0xeb, 0x7f, 1,
            0xef, 0x5f, 2, 3,
            0xe5, 0x7f, (byte)'X');
        var executor = new ZMachineOpcodeExecutor(state);

        for (var index = 0; index < 4; index++)
        {
            executor.ExecuteNext();
        }

        Assert.Equal("\n  X", state.Screen.UpperWindowText);
        Assert.Equal(1, state.Screen.CursorRow);
        Assert.Equal(3, state.Screen.CursorColumn);
    }

    [Fact]
    public void VersionOneAndTwoScrollTextUpFromTheBottomOfTheScreen()
    {
        var state = CreateState(1);
        state.Screen.SetDimensions(4, 2, 4, 2);

        state.Screen.Write("A\nB\nC");

        Assert.Equal("B\nC", state.Screen.LowerWindowText);
        Assert.Equal(1, state.Screen.CursorRow);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void BufferedWordWrapDoesNotCarrySpacesToTheStartOfTheNextLine(byte version)
    {
        var state = CreateState(version);
        state.Screen.SetDimensions(5, 3, version == 5 ? 40 : 5, version == 5 ? 48 : 3);

        state.Screen.Write("12345  x");

        Assert.EndsWith("12345\nx", state.Screen.LowerWindowText);
    }

    [Fact]
    public void VersionSixBufferedWordWrapDoesNotCarrySpacesToTheNextLine()
    {
        var state = CreateState(6);
        state.Screen.SetDimensions(5, 3, 40, 48);

        state.Screen.Write("12345  x");

        Assert.Equal(8, state.Screen.CursorColumn);
    }

    [Fact]
    public void BufferedWordWrapPreservesIntentionalIndentAfterExplicitNewline()
    {
        var state = CreateState(3);

        state.Screen.Write("a\n  x");

        Assert.EndsWith("a\n  x", state.Screen.LowerWindowText);
    }

    [Fact]
    public void EraseLineAppliesToTheCurrentLowerWindowWithoutMovingItsCursor()
    {
        var state = CreateState(5, 0xe5, 0x7f, (byte)'X', 0xee, 0x7f, 1);
        var executor = new ZMachineOpcodeExecutor(state);
        executor.ExecuteNext();
        var cursorColumn = state.Screen.CursorColumn;

        executor.ExecuteNext();

        Assert.EndsWith("X", state.Screen.LowerWindowText);
        Assert.Equal(cursorColumn, state.Screen.CursorColumn);
    }

    [Fact]
    public void ResizingUpdatesTheVersionFiveHeaderAndRejectsInvalidDimensions()
    {
        var state = CreateState(5);

        state.Screen.SetDimensions(100, 40, 1000, 640);

        Assert.Equal(100, state.ReadHeaderByte(0x21));
        Assert.Equal(40, state.ReadHeaderByte(0x20));
        Assert.Equal(1000, state.ReadHeaderWord(0x22));
        Assert.Equal(640, state.ReadHeaderWord(0x24));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Screen.SetDimensions(0, 40, 1000, 640));
    }

    [Fact]
    public void ScreenOutputIsReflectedInTheSelectedWindowAndBufferModeIsTracked()
    {
        var state = CreateState(4, 0xf2, 0x7f, 0, 0xe5, 0x7f, (byte)'A');

        var executor = new ZMachineOpcodeExecutor(state);
        executor.ExecuteNext();
        Assert.False(state.Screen.LowerBufferingEnabled);
        executor.ExecuteNext();

        Assert.EndsWith("A", state.Screen.LowerWindowText);
    }

    [Fact]
    public void VersionSixPublishesEightIndependentPixelWindowsAndUsesCursorOpcodes()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x200] = 0;
        new byte[]
        {
            0xeb, 0x7f, 4,             // set_window 4
            0xbe, 17, 0x57, 4, 100, 100, // window_size 4 100 100
            0xef, 0x57, 3, 8, 4,       // set_cursor 3 8 4
            0xf0, 0x7f, 0x90           // get_cursor $90
        }.CopyTo(story, 0x201);
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        for (var index = 0; index < 3; index++) executor.ExecuteNext();
        var getCursor = state.Instructions.Decode(state.ProgramCounter);
        Assert.Equal(0x20f, state.ProgramCounter);
        Assert.Equal(16, getCursor.Opcode);
        Assert.Equal(0x90, getCursor.Operands[0].EncodedValue);
        Assert.Null(getCursor.StoreVariable);
        executor.ExecuteNext();

        Assert.Equal(4, state.Screen.SelectedWindow);
        Assert.Equal((3, 8), state.Screen.GetV6Cursor());
        Assert.Equal(16, state.ReadHeaderByte(0x26));
        Assert.Equal(8, state.ReadHeaderByte(0x27));
        Assert.Equal(3, state.Memory.ReadWord(0x90));
        Assert.Equal(8, state.Memory.ReadWord(0x92));
        Assert.Equal(new[] { 1, 1, 400, 640 }, state.Screen.V6WindowBounds(0));
        Assert.Equal(new[] { 1, 1, 0, 640 }, state.Screen.V6WindowBounds(1));
        Assert.Equal(new[] { 1, 1, 0, 0 }, state.Screen.V6WindowBounds(7));
        Assert.Equal(641, state.Screen.GetWindowProperty(1, 5));
    }

    [Fact]
    public void VersionSixWindowMutationsAndPropertiesFollowTheStandard()
    {
        var state = CreateState(6);
        var screen = state.Screen;

        screen.SplitWindow(120);
        screen.MoveWindow(3, 10, 20);
        screen.ResizeWindow(3, 80, 100);
        screen.SetCursorV6(3, 70, 90);
        screen.ResizeWindow(3, 60, 80);
        screen.SelectWindow(3);
        screen.SetWindowStyle(3, 0b1011, 0);
        screen.SetMargins(4, 5, 3);
        screen.PutWindowProperty(3, 9, 12);
        screen.PutWindowProperty(3, 15, unchecked((ushort)-2));

        Assert.Equal(new[] { 10, 20, 60, 80 }, screen.V6WindowBounds(3));
        Assert.Equal((1, 5), screen.GetV6Cursor());
        Assert.Equal(0b1011, screen.GetWindowProperty(3, 14));
        Assert.Equal(12, screen.GetWindowProperty(3, 9));
        Assert.Equal(unchecked((ushort)-2), screen.GetWindowProperty(3, 15));
        Assert.False(screen.CopyTextToTranscript);
        screen.SetWindowStyle(3, 4, 1);
        Assert.True(screen.CopyTextToTranscript);
        Assert.Throws<ZMachineRuntimeException>(() => screen.PutWindowProperty(3, 16, 1));

        screen.SetCursorV6(7, 1, 1);
        Assert.Equal(1, screen.GetWindowProperty(7, 4));
        Assert.Equal(1, screen.GetWindowProperty(7, 5));

        screen.EraseWindow(-1);
        Assert.Equal(0, screen.SelectedWindow);
        Assert.Equal(new[] { 1, 1, 400, 640 }, screen.V6WindowBounds(0));
    }

    [Fact]
    public void VersionSixFontColourBufferAndUnavailableDeviceCapabilitiesAreReported()
    {
        var state = CreateState(6);
        var screen = state.Screen;
        Assert.Equal(0, state.ReadHeaderWord(0x10) & ((1 << 3) | (1 << 5) | (1 << 8)));
        Assert.Equal(0, state.ReadHeaderByte(1) & 0x22);

        Assert.Equal(1, screen.SetFont(3, 0xfffd));
        Assert.Equal(3, screen.GetWindowProperty(0, 12));
        Assert.Equal(0, screen.SetFont(2, 0xfffd));
        screen.SetV6Colour(0x1234, 0xfffc, 0xfffd, trueColour: true);
        Assert.Equal(0x1234, screen.GetWindowProperty(0, 16));
        Assert.Equal(0x7fff, screen.GetWindowProperty(0, 17));

        Assert.Equal(0, screen.SetBufferScreen(1));
        Assert.Equal(1, screen.SetBufferScreen(0));
        Assert.Equal(0, screen.SetBufferScreen(ushort.MaxValue));
        screen.SetMouseWindow(ushort.MaxValue);
        Assert.Equal(-1, screen.MouseWindow);
        Assert.Throws<ZMachineRuntimeException>(() => screen.SetMouseWindow(8));
    }

    [Fact]
    public void SetFontRemainsAvailableInVersionsFiveSevenAndEight()
    {
        foreach (var version in new byte[] { 5, 7, 8 })
        {
            var screen = CreateState(version).Screen;

            Assert.Equal(1, screen.SetFont(4, 0xfffd));
            Assert.True(screen.UsesFixedPitch);
            Assert.Equal(4, screen.SetFont(0, 0xfffd));
            Assert.Equal(0, screen.SetFont(2, 0xfffd));
        }
    }

    [Fact]
    public void VersionSixClearsUnsupportedTransparencyInTheHeaderExtension()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x36] = 0x01;
        story[0x37] = 0x60;
        story[0x160] = 0;
        story[0x161] = 4;
        story[0x168] = 0;
        story[0x169] = 1;

        var state = new ZMachineState(StoryLoader.Load(story));

        Assert.Equal(0, state.Memory.ReadWord(0x168) & 1);
    }

    [Fact]
    public void VersionSixFormattedMemoryStreamStoresLinesForPrintForm()
    {
        var state = CreateState(6);
        var streams = new ZMachineOutputStreams(state, host: null);

        streams.SelectOutput(3, 0x180, -16);
        streams.Write("ABC");
        streams.SelectOutput(-3);

        Assert.Equal(2, state.Memory.ReadWord(0x180));
        Assert.Equal((byte)'A', state.Memory.ReadByte(0x182));
        Assert.Equal((byte)'B', state.Memory.ReadByte(0x183));
        Assert.Equal(1, state.Memory.ReadWord(0x184));
        Assert.Equal((byte)'C', state.Memory.ReadByte(0x186));
        Assert.Equal(0, state.Memory.ReadWord(0x187));
    }

    [Fact]
    public void VersionSixPrintFormRendersFormattedOutputStreamLines()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x200] = 0;
        new byte[] { 0xbe, 26, 0x7f, 0x80 }.CopyTo(story, 0x201); // print_form $80
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteWord(0x80, 2);
        state.Memory.WriteByte(0x82, (byte)'A');
        state.Memory.WriteByte(0x83, (byte)'B');
        state.Memory.WriteWord(0x84, 1);
        state.Memory.WriteByte(0x86, (byte)'C');
        state.Memory.WriteWord(0x87, 0);

        var result = new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal("AB\nC", result.Output);
    }

    [Fact]
    public void VersionSixWindowNewlineInterruptRunsThePackedRoutineBeforePrintingContinues()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x200] = 0;
        new byte[] { 0xe5, 0x7f, 13 }.CopyTo(story, 0x201); // print_char newline
        story[0x240] = 0;
        new byte[] { 0x4d, 16, 42, 0xb0 }.CopyTo(story, 0x241); // store global 16, 42; rtrue
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Screen.PutWindowProperty(0, 8, 0x90);
        state.Screen.PutWindowProperty(0, 9, 1);
        state.Screen.PutWindowProperty(0, 15, 1);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(42, state.ReadVariableValue(16));
        Assert.Equal(1, state.Screen.GetWindowProperty(0, 15));
    }

    private static ZMachineState CreateState(byte version, params byte[] instructions)
    {
        var story = MachineStoryFactory.Create(version);
        instructions.CopyTo(story, 0x200);
        return new ZMachineState(StoryLoader.Load(story));
    }

    private sealed class StatusObservingHost(ZMachineScreen screen) : IZMachineInputOutput
    {
        public bool SupportsTimedInput => false;

        public bool SupportsCommandInput => true;

        public bool SupportsTranscript => false;

        public bool SupportsCommandRecording => false;

        public string StatusObservedBeforeRead { get; private set; } = string.Empty;

        public ZMachineInputResponse ReadInput(ZMachineInputRequest request, Func<bool>? timedInterrupt)
        {
            StatusObservedBeforeRead = screen.StatusLine;
            return new ZMachineInputResponse(string.Empty, 13);
        }

        public void WriteScreenOutput(string text)
        {
        }

        public void WriteTranscript(string text)
        {
        }

        public void WriteCommand(string text)
        {
        }

        public void WriteKeypress(byte zsciiCode)
        {
        }

        public void ReportFileAccessUnavailable(string feature)
        {
        }
    }
}

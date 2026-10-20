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

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
    public void V5TrueColoursSelectTheNearestSupportedStandardColours()
    {
        var state = CreateState(5);
        state.OutputStreams.Screen.SetTrueColours(0x001D, 0x0340);

        Assert.Equal((byte)3, state.OutputStreams.Screen.ForegroundColour);
        Assert.Equal((byte)4, state.OutputStreams.Screen.BackgroundColour);
        Assert.Equal((byte)4, state.Memory.ReadByte(0x2C));
        Assert.Equal((byte)3, state.Memory.ReadByte(0x2D));
    }

    [Fact]
    public void V5FontSelectionReportsAvailabilityAndFixedPitchBehavior()
    {
        var state = CreateFontState(5);
        var screen = state.OutputStreams.Screen;
        Assert.Equal((ushort)0x0008, (ushort)(state.Memory.ReadWord(0x10) & 0x0008));
        Assert.False(screen.RequiresFixedPitch);

        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;
        operations.Clear();
        Assert.Equal((ushort)1, screen.SetFont(ZMachineFontCatalog.CourierFont));
        Assert.Equal(ZMachineFontCatalog.CourierFont, screen.CurrentFont);
        Assert.True(screen.RequiresFixedPitch);
        Assert.Equal(ZMachineScreenOperationKind.SetFont, operations[^1].Kind);
        Assert.True(operations[^1].FixedPitch);

        Assert.Equal(ZMachineFontCatalog.CourierFont, screen.SetFont(0));
        Assert.Equal((ushort)0, screen.SetFont(ZMachineFontCatalog.PictureFont));
        Assert.Equal(ZMachineFontCatalog.CourierFont, screen.CurrentFont);
        Assert.Equal(ZMachineFontCatalog.CourierFont, screen.SetFont(ZMachineFontCatalog.CharacterGraphicsFont));
        Assert.Equal(ZMachineFontCatalog.CharacterGraphicsFont, screen.CurrentFont);
        Assert.Equal((byte)1, state.Memory.ReadByte(0x26));
        Assert.Equal((byte)1, state.Memory.ReadByte(0x27));

        screen.SetFont(ZMachineFontCatalog.NormalFont);
        screen.SetTextStyle((ushort)ZMachineTextStyle.FixedPitch);
        Assert.True(screen.RequiresFixedPitch);
        screen.SetTextStyle((ushort)ZMachineTextStyle.Roman);
        Assert.False(screen.RequiresFixedPitch);
        state.Memory.WriteWord(0x10, 0x000A);
        Assert.True(screen.RequiresFixedPitch);
        state.Memory.WriteWord(0x10, 0x0008);
        Assert.False(screen.RequiresFixedPitch);
    }

    [Fact]
    public void V6FontsCanBeMeasuredAndSelectedOnAnUnselectedWindow()
    {
        var state = CreateV6State();
        var screen = state.OutputStreams.Screen;
        screen.ResizeWindow(5, 400, 640);
        Assert.Equal((ushort)0, (ushort)(state.Memory.ReadWord(0x10) & 0x0008));
        var oldFont = screen.SetFont(ZMachineFontCatalog.CharacterGraphicsFont, 5);

        Assert.Equal((ushort)1, oldFont);
        Assert.Equal(ZMachineFontCatalog.NormalFont, screen.CurrentFont);
        Assert.Equal(ZMachineFontCatalog.CharacterGraphicsFont, screen.GetV6Window(5).FontNumber);
        Assert.Equal((ushort)0x0808, screen.GetV6Window(5).FontSize);
        Assert.Equal((short)50, screen.GetV6Window(5).LineCount);
        Assert.Equal((ushort)ZMachineFontCatalog.CharacterGraphicsFont,
            screen.SetFont(ZMachineFontCatalog.CourierFont, 5));
        Assert.Equal((ushort)0x1008, screen.GetV6Window(5).FontSize);
        Assert.Equal((short)25, screen.GetV6Window(5).LineCount);
        Assert.Equal((ushort)0, screen.SetFont(ZMachineFontCatalog.PictureFont, 5));
        Assert.Equal(ZMachineFontCatalog.CourierFont, screen.GetV6Window(5).FontNumber);
        Assert.True(screen.GetV6Window(5).CursorVisible);
        Assert.Throws<ZMachineExecutionException>(() => screen.SetFont(ZMachineFontCatalog.NormalFont, 8));
    }

    [Fact]
    public void SetFontOpcodeStoresPreviousFontAndUsesOptionalV6WindowOperand()
    {
        var v5 = CreateFontState(5);
        new ZMachineOpcodeExecutor().Execute(
            ExtendedInstruction("set_font", 4, [Constant(ZMachineFontCatalog.CourierFont)], store: 16), v5);
        Assert.Equal((ushort)ZMachineFontCatalog.NormalFont, v5.ReadVariable(16));
        Assert.Equal(ZMachineFontCatalog.CourierFont, v5.OutputStreams.Screen.CurrentFont);

        var v6 = CreateV6State();
        new ZMachineOpcodeExecutor().Execute(
            ExtendedInstruction("set_font", 4,
                [Constant(ZMachineFontCatalog.CharacterGraphicsFont), Constant(5)], store: 16), v6);
        Assert.Equal((ushort)ZMachineFontCatalog.NormalFont, v6.ReadVariable(16));
        Assert.Equal(ZMachineFontCatalog.CharacterGraphicsFont, v6.OutputStreams.Screen.GetV6Window(5).FontNumber);

        new ZMachineOpcodeExecutor().Execute(
            ExtendedInstruction("set_font", 4, [Constant(ZMachineFontCatalog.CourierFont)], store: 17), v6);
        Assert.Equal((ushort)ZMachineFontCatalog.NormalFont, v6.ReadVariable(17));
        Assert.Equal(ZMachineFontCatalog.CourierFont, v6.OutputStreams.Screen.CurrentFont);
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
    public async Task MorePromptWaitsForOutputBeyondAFullPage()
    {
        var screen = CreateState(5).OutputStreams.Screen;
        screen.SetDimensions(80, 2, 80, 2);
        var output = new List<string>();
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;

        screen.WriteText("a\nb\n", output.Add);

        Assert.False(screen.IsWaitingForMore);
        Assert.DoesNotContain(operations, operation => operation.Kind == ZMachineScreenOperationKind.MorePrompt);
        Assert.Equal(new[] { "a\nb\n" }, output);

        screen.WriteText("next page", output.Add);

        Assert.True(screen.IsWaitingForMore);
        Assert.Contains(operations, operation => operation.Kind == ZMachineScreenOperationKind.MorePrompt);
        Assert.Equal(new[] { "a\nb\n" }, output);

        await screen.WaitForMoreAsync(_ => ValueTask.FromResult((ushort)32), TestContext.Current.CancellationToken);

        Assert.False(screen.IsWaitingForMore);
        Assert.Equal(new[] { "a\nb\n", "next page" }, output);
    }

    [Fact]
    public void MorePageStartsFreshWhenThePlayerSubmitsANewCommand()
    {
        var screen = CreateState(5).OutputStreams.Screen;
        screen.SetDimensions(80, 4);
        screen.SetMorePageLineLimit(3);

        screen.WriteText("previous command\nmore previous output\n", null);
        screen.ResetMorePageForCommand();
        screen.WriteText("first response line\nsecond response line\n", null);

        Assert.False(screen.IsWaitingForMore);

        screen.WriteText("third response line\n", null);
        Assert.False(screen.IsWaitingForMore);

        screen.WriteText("fourth response line", null);

        Assert.True(screen.IsWaitingForMore);
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

    [Fact]
    public void V6InitializesEightIndependentWindowsAndVersionedHeaderValues()
    {
        var state = CreateV6State();
        var screen = state.OutputStreams.Screen;
        screen.SetDimensions(80, 25, 640, 400);

        var main = screen.GetV6Window(0);
        var status = screen.GetV6Window(1);
        var auxiliary = screen.GetV6Window(7);
        Assert.Equal(new ZMachineV6WindowSnapshot(0, 1, 1, 400, 640, 1, 1, 0, 0, 0, 0,
            ZMachineTextStyle.Roman, 0x0209, 1, 0x1008, 0x0E, 25, 0x7FFF, 0, true), main);
        Assert.Equal((ushort)0, status.Height);
        Assert.Equal((ushort)640, status.Width);
        Assert.Equal((ushort)0x08, auxiliary.Attributes);
        Assert.Equal((ushort)640, state.Memory.ReadWord(0x22));
        Assert.Equal((ushort)400, state.Memory.ReadWord(0x24));
        Assert.Equal((byte)8, state.Memory.ReadByte(0x27));
        Assert.Equal((byte)16, state.Memory.ReadByte(0x26));
        Assert.Equal((short)0, screen.SelectedWindow);
    }

    [Fact]
    public void V6SplitWindowTilesWindowsZeroAndOneAndWindowSelectionIsIndependent()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.SetDimensions(80, 25, 640, 400);
        screen.SplitWindow(96);

        Assert.Equal((ushort)96, screen.GetWindowProperty(1, 2));
        Assert.Equal((ushort)1, screen.GetWindowProperty(1, 0));
        Assert.Equal((ushort)97, screen.GetWindowProperty(0, 0));
        Assert.Equal((ushort)304, screen.GetWindowProperty(0, 2));

        screen.SetWindow(3);
        screen.ResizeWindow(3, 100, 200);
        screen.SetV6Cursor(12, 40);
        screen.SetWindow(-3);
        Assert.Equal((short)3, screen.SelectedWindow);
        Assert.Equal(new ZMachineV6WindowSnapshot(3, 1, 1, 100, 200, 12, 40, 0, 0, 0, 0,
            ZMachineTextStyle.Roman, 0x0209, 1, 0x1008, 0x08, 0, 0x7FFF, 0, true), screen.GetV6Window(3));
        Assert.Equal(new ZMachineCursorPosition(1, 1), screen.GetCursor(4));
        Assert.Throws<ZMachineExecutionException>(() => screen.SetWindow(8));
    }

    [Fact]
    public void V6ScreenResizePreservesSplitTilingAndClampsTopWindow()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.SetDimensions(80, 25, 640, 400);
        screen.SplitWindow(96);

        screen.SetDimensions(80, 30, 640, 480);
        Assert.Equal((ushort)96, screen.GetWindowProperty(1, 2));
        Assert.Equal((ushort)97, screen.GetWindowProperty(0, 0));
        Assert.Equal((ushort)384, screen.GetWindowProperty(0, 2));

        screen.PutWindowProperty(0, 15, 12);
        screen.SetDimensions(80, 5, 640, 80);
        Assert.Equal((ushort)80, screen.GetWindowProperty(1, 2));
        Assert.Equal((ushort)81, screen.GetWindowProperty(0, 0));
        Assert.Equal((ushort)0, screen.GetWindowProperty(0, 2));
        Assert.Equal((short)12, screen.GetV6Window(0).LineCount);
    }

    [Fact]
    public void V6GeometryMarginsCursorAndVisibilityFollowWindowRules()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.ResizeWindow(2, 40, 80);
        screen.SetMargins(2, 3, 2);
        screen.SetV6Cursor(5, 1, 2);
        Assert.Equal(new ZMachineCursorPosition(5, 3), screen.GetCursor(2));

        screen.MoveWindow(2, 10, 20);
        Assert.Equal((ushort)10, screen.GetWindowProperty(2, 0));
        Assert.Equal((ushort)20, screen.GetWindowProperty(2, 1));
        screen.ResizeWindow(2, 3, 10);
        Assert.Equal(new ZMachineCursorPosition(1, 3), screen.GetCursor(2));

        screen.SetV6Cursor(ushort.MaxValue, 0, 2);
        Assert.False(screen.GetV6Window(2).CursorVisible);
        screen.SetV6Cursor(ushort.MaxValue - 1, 0, 2);
        Assert.True(screen.GetV6Window(2).CursorVisible);
        Assert.Throws<ZMachineExecutionException>(() => screen.SetV6Cursor(6, 1, 2));
    }

    [Fact]
    public void V6WindowStylePropertiesAndTranscriptAttributeAreIndependent()
    {
        var state = CreateV6State();
        var screen = state.OutputStreams.Screen;
        screen.ResizeWindow(2, 20, 40);
        screen.ChangeWindowStyle(2, 0x05, 0);
        screen.ChangeWindowStyle(2, 0x02, 1);
        screen.ChangeWindowStyle(2, 0x01, 2);
        Assert.Equal((ushort)0x06, screen.GetWindowProperty(2, 14));
        Assert.Equal((ushort)0x06, screen.GetV6Window(2).Attributes);

        screen.PutWindowProperty(2, 8, 1234);
        screen.PutWindowProperty(2, 9, 2);
        screen.PutWindowProperty(2, 15, unchecked((ushort)-999));
        Assert.Equal((ushort)1234, screen.GetWindowProperty(2, 8));
        Assert.Equal((ushort)2, screen.GetWindowProperty(2, 9));
        Assert.Equal((short)-999, screen.GetV6Window(2).LineCount);
        Assert.Throws<ZMachineExecutionException>(() => screen.PutWindowProperty(2, 16, 1));
        Assert.Throws<ZMachineExecutionException>(() => screen.GetWindowProperty(2, 18));

        screen.ChangeWindowStyle(2, 0x04, 2);
        var transcript = new List<string>();
        state.OutputStreams.TranscriptWriter = transcript.Add;
        state.OutputStreams.SelectStream(2);
        screen.SetWindow(2);
        state.OutputStreams.WriteText("hidden");
        Assert.Empty(transcript);
        screen.ChangeWindowStyle(2, 0x04, 1);
        state.OutputStreams.WriteText("shown");
        Assert.Equal(new[] { "shown" }, transcript);
    }

    [Fact]
    public void V6ColoursArePerWindowAndExposeStandardAndTrueColourProperties()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.SetColours(5, 6, 3);
        Assert.Equal((ushort)0x0605, screen.GetWindowProperty(3, 11));
        Assert.Equal((ushort)0x03BD, screen.GetWindowProperty(3, 16));
        Assert.Equal((ushort)0x59A0, screen.GetWindowProperty(3, 17));
        Assert.Equal((ushort)0x0209, screen.GetWindowProperty(0, 11));

        screen.SetTrueColours(0x1234, 0x2345, 3);
        Assert.Equal((ushort)0x1234, screen.GetWindowProperty(3, 16));
        Assert.Equal((ushort)0x2345, screen.GetWindowProperty(3, 17));
        Assert.Equal((byte)16, (byte)(screen.GetWindowProperty(3, 11) & 0xFF));
        screen.SetColours(9, 15, 3);
        Assert.Equal((ushort)0xFFFC, screen.GetWindowProperty(3, 17));
        Assert.Throws<ZMachineExecutionException>(() => screen.SetColours(15, 2, 3));
        Assert.Throws<ZMachineExecutionException>(() => screen.ScrollWindow(3, 1));
        Assert.Throws<ZMachineExecutionException>(() => screen.SetTrueColours(0xFFFC, 0x0000, 3));
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;
        screen.EraseWindow(3);
        Assert.DoesNotContain(operations, operation => operation.Kind == ZMachineScreenOperationKind.ClearWindow);
    }

    [Fact]
    public void V6EraseAllStillUnSplitsAndSelectsWindowZeroWithTransparentBackground()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.SetDimensions(80, 25, 640, 400);
        screen.SplitWindow(40);
        screen.ChangeWindowStyle(0, 0x02, 2);
        screen.SetColours(9, 15, 0);
        Assert.Equal((ushort)40, screen.GetWindowProperty(1, 2));
        Assert.Equal((ushort)0xFFFC, screen.GetV6Window(0).TrueBackgroundColour);
        screen.SetWindow(3);
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;
        operations.Clear();

        screen.EraseWindow(-1);

        Assert.Equal((short)0, screen.SelectedWindow);
        Assert.Equal((ushort)0, screen.GetWindowProperty(1, 2));
        Assert.Equal((ushort)400, screen.GetWindowProperty(0, 2));
        Assert.DoesNotContain(operations, operation => operation.Kind == ZMachineScreenOperationKind.ClearScreen);
    }

    [Fact]
    public void V6WrappingUsesMarginsAndBufferedWordWrapThenScrollsWhenEnabled()
    {
        var state = CreateV6State();
        var screen = state.OutputStreams.Screen;
        screen.ResizeWindow(2, 32, 32);
        screen.ChangeWindowStyle(2, 0x0F, 0);
        screen.PutWindowProperty(2, 15, unchecked((ushort)-999));
        screen.SetWindow(2);
        var output = new List<string>();
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;
        state.OutputStreams.ScreenWriter = output.Add;

        state.OutputStreams.WriteText("ab cd");
        Assert.Equal(new[] { "ab", "cd" }, output);
        Assert.Equal(new ZMachineCursorPosition(17, 17), screen.GetCursor(2));

        state.OutputStreams.WriteText("\n\n");
        Assert.Contains(operations, operation => operation.Kind == ZMachineScreenOperationKind.ScrollWindow && operation.Window == 2);
    }

    [Fact]
    public void V6MovedWindowKeepsCursorRelativeAndScrollsAtItsOwnBottomEdge()
    {
        var screen = CreateV6State().OutputStreams.Screen;
        screen.ResizeWindow(2, 2, 20);
        screen.MoveWindow(2, 10, 30);
        screen.PutWindowProperty(2, 13, 0x0101);
        screen.ChangeWindowStyle(2, 0x02, 1);
        screen.PutWindowProperty(2, 15, unchecked((ushort)-999));
        screen.SetWindow(2);
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;

        screen.WriteText("first\nsecond\n", null);

        Assert.Equal(new ZMachineCursorPosition(2, 1), screen.CurrentCursor);
        Assert.Contains(operations, operation => operation.Kind == ZMachineScreenOperationKind.ScrollWindow
            && operation.Window == 2 && operation.Value == 1);
    }

    [Fact]
    public async Task V6LineCountDrivesMorePromptAndWindowBufferScreenFlushes()
    {
        var state = CreateV6State();
        var screen = state.OutputStreams.Screen;
        screen.ResizeWindow(2, 32, 80);
        screen.ChangeWindowStyle(2, 0x03, 0);
        screen.PutWindowProperty(2, 15, 1);
        screen.SetWindow(2);
        var output = new List<string>();
        var operations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = operations.Add;
        state.OutputStreams.ScreenWriter = output.Add;
        state.OutputStreams.WriteText("first\nsecond");

        Assert.True(screen.IsWaitingForMore);
        Assert.Equal(new[] { "first\n" }, output);
        Assert.Equal((ushort)0, screen.BufferScreen(1));
        Assert.Equal((ushort)1, screen.BufferScreen(ushort.MaxValue));
        Assert.Contains(operations, operation => operation.Kind == ZMachineScreenOperationKind.BufferScreen);
        Assert.Contains(operations, operation => operation.Kind == ZMachineScreenOperationKind.FlushDisplay);

        await screen.WaitForMoreAsync(_ => ValueTask.FromResult((ushort)32), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "first\n", "second" }, output);
        Assert.False(screen.IsWaitingForMore);
    }

    [Fact]
    public void V6ScreenOpcodesDispatchThroughTheExecutor()
    {
        var state = CreateV6State();
        var executor = new ZMachineOpcodeExecutor();
        executor.Execute(ExtendedInstruction("move_window", 16, [Constant(3), Constant(7), Constant(9)]), state);
        executor.Execute(ExtendedInstruction("window_size", 17, [Constant(3), Constant(30), Constant(40)]), state);
        executor.Execute(ExtendedInstruction("set_margins", 8, [Constant(3), Constant(4), Constant(3)]), state);
        executor.Execute(ExtendedInstruction("window_style", 18, [Constant(3), Constant(1), Constant(0)]), state);
        executor.Execute(ExtendedInstruction("put_wind_prop", 25, [Constant(3), Constant(15), Constant(12)]), state);
        executor.Execute(ExtendedInstruction("get_wind_prop", 19, [Constant(3), Constant(15)], store: 16), state);
        executor.Execute(ExtendedInstruction("scroll_window", 20, [Constant(3), Constant(2)]), state);
        executor.Execute(ExtendedInstruction("buffer_screen", 29, [Constant(1)], store: 17), state);
        executor.Execute(ExtendedInstruction("split_window", 10, [Constant(20)]), state);
        executor.Execute(ExtendedInstruction("set_window", 11, [Constant(3)]), state);
        executor.Execute(ExtendedInstruction("set_cursor", 15, [Constant(2), Constant(6), Constant(3)]), state);
        executor.Execute(ExtendedInstruction("get_cursor", 16, [Constant(400)]), state);
        executor.Execute(ExtendedInstruction("set_text_style", 17, [Constant(2)]), state);
        executor.Execute(ExtendedInstruction("set_colour", 27, [Constant(4), Constant(5), Constant(3)]), state);
        executor.Execute(ExtendedInstruction("set_true_colour", 13, [Constant(0x1234), Constant(0x2345), Constant(3)]), state);
        executor.Execute(ExtendedInstruction("set_cursor", 15, [Constant(ushort.MaxValue)]), state);
        Assert.False(state.OutputStreams.Screen.GetV6Window(3).CursorVisible);
        executor.Execute(ExtendedInstruction("set_cursor", 15, [Constant(ushort.MaxValue - 1)]), state);
        executor.Execute(ExtendedInstruction("erase_line", 14, [Constant(1)]), state);
        executor.Execute(ExtendedInstruction("erase_window", 13, [Constant(3)]), state);

        Assert.Equal((ushort)7, state.OutputStreams.Screen.GetWindowProperty(3, 0));
        Assert.Equal((ushort)30, state.OutputStreams.Screen.GetWindowProperty(3, 2));
        Assert.Equal((ushort)3, state.OutputStreams.Screen.GetWindowProperty(3, 6));
        Assert.Equal((ushort)0x01, state.OutputStreams.Screen.GetWindowProperty(3, 14));
        Assert.Equal((ushort)12, state.ReadVariable(16));
        Assert.Equal((ushort)0, state.ReadVariable(17));
        Assert.Equal((ushort)2, state.Memory.ReadWord(400));
        Assert.Equal((ushort)6, state.Memory.ReadWord(402));
        Assert.Equal((ushort)20, state.OutputStreams.Screen.GetWindowProperty(1, 2));
        Assert.Equal(new ZMachineCursorPosition(1, 1), state.OutputStreams.Screen.GetCursor(3));
        Assert.True(state.OutputStreams.Screen.GetV6Window(3).CursorVisible);
    }

    [Fact]
    public void V6WindowStyleOpcodeAllowsItsOptionalOperationOperand()
    {
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Extended, 18, 6, out var definition));
        Assert.Equal((byte)2, definition.MinimumOperands);
        Assert.Equal((byte)3, definition.MaximumOperands);
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Variable, 15, 6, out var cursorDefinition));
        Assert.Equal((byte)1, cursorDefinition.MinimumOperands);

        var state = CreateV6State();
        state.OutputStreams.Screen.ChangeWindowStyle(2, 1, 0);
        new ZMachineOpcodeExecutor().Execute(ExtendedInstruction("window_style", 18,
            [Constant(2), Constant(2)]), state);
        Assert.Equal((ushort)2, state.OutputStreams.Screen.GetWindowProperty(2, 14));
    }

    private static ZMachineExecutionState CreateState(byte version)
    {
        var bytes = CreateStory(version);
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static ZMachineExecutionState CreateV6State()
    {
        var bytes = CreateStory(6);
        bytes[800] = 0;
        bytes[801] = 0xAA;
        bytes[0x26] = 16;
        bytes[0x27] = 8;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static ZMachineExecutionState CreateFontState(byte version)
    {
        var bytes = CreateStory(version);
        StoryFixtureBuilder.WriteWord(bytes, 0x10, 0);
        bytes[0x26] = 1;
        bytes[0x27] = 1;
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

    private static DecodedInstruction ExtendedInstruction(
        string mnemonic,
        byte opcode,
        DecodedOperand[] operands,
        byte? store = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Extended, opcode, mnemonic, 6, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), StoresResult: store is not null);
        return new DecodedInstruction(850, 851, InstructionForm.Extended, OpcodeClass.Extended, opcode,
            definition, operands.ToImmutableArray(), store, null, ImmutableArray<byte>.Empty);
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);
}

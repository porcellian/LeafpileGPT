using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class GameDisplayModelTests
{
    [Fact]
    public void StartsWithEmptyTranscriptAndDefaultStatus()
    {
        var model = new GameDisplayModel();

        Assert.Equal(string.Empty, model.Transcript);
        Assert.Equal(string.Empty, model.Location);
        Assert.Equal("Score: 0  Moves: 0", model.RightStatus);
        Assert.False(model.HasStoryLoaded);
        Assert.False(model.IsStatusStripVisible);
    }

    [Fact]
    public void AppendsOutputInOrderAndCanClearIt()
    {
        var model = new GameDisplayModel();
        model.AppendOutput("You are standing outside.\n");
        model.AppendOutput("There is a mailbox here.");
        model.SplitWindow(1);
        model.AppendOutput("stale upper text", window: 1);

        Assert.Equal("You are standing outside.\nThere is a mailbox here.", model.Transcript);

        model.ClearOutput();

        Assert.Equal(string.Empty, model.Transcript);
        Assert.Equal(string.Empty, model.UpperWindowText);
        Assert.False(model.IsUpperWindowVisible);
        Assert.Equal(0, model.UpperWindowHeight);
    }

    [Fact]
    public void DisplaysLocationAndRightAlignedStatusText()
    {
        var model = new GameDisplayModel();

        model.SetStoryVersion(3);
        model.SetStatusLine("West of House", "Score: 3  Moves: 12");
        model.SetStoryLoaded(loaded: true);

        Assert.Equal("West of House", model.Location);
        Assert.Equal("Score: 3  Moves: 12", model.RightStatus);
        Assert.True(model.HasStoryLoaded);
        Assert.True(model.IsStatusStripVisible);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    public void StatusStripVisibilityMatchesStandardStatusLineVersions(int version, bool expected)
    {
        var model = new GameDisplayModel();

        model.SetStoryVersion(version);
        model.SetStoryLoaded(loaded: true);

        Assert.Equal(expected, model.IsStatusStripVisible);
    }

    [Fact]
    public void StatusStripVisibilityUpdatesWhenStoryVersionChanges()
    {
        var model = new GameDisplayModel();
        model.SetStoryLoaded(loaded: true);
        model.SetStoryVersion(3);
        Assert.True(model.IsStatusStripVisible);

        model.SetStoryVersion(4);
        Assert.False(model.IsStatusStripVisible);

        model.SetStoryVersion(1);
        Assert.True(model.IsStatusStripVisible);

        model.SetStoryLoaded(loaded: false);
        Assert.False(model.IsStatusStripVisible);
    }

    [Fact]
    public void ResetPresentationClearsStorySpecificScreenState()
    {
        var model = new GameDisplayModel();
        model.AppendOutput("stale");
        model.AppendOutput("upper", window: 1);
        model.SetStatusLine("Old Room", "Score: 9  Moves: 15");
        model.ShowMorePrompt(visible: true);
        Assert.True(model.IsMorePromptVisible);
        Assert.Equal("[MORE]", model.PromptText);
        model.SetSelectedWindow(1);
        model.SplitWindow(2);
        model.SetColours(0x0608);
        model.InputText = "stale command";
        var presentationResetRaised = false;
        model.PresentationReset += () => presentationResetRaised = true;

        model.ResetPresentation();

        Assert.Equal(string.Empty, model.Transcript);
        Assert.Equal(string.Empty, model.UpperWindowText);
        Assert.False(model.IsUpperWindowVisible);
        Assert.Equal(0, model.UpperWindowHeight);
        Assert.Equal(string.Empty, model.Location);
        Assert.Equal("Score: 0  Moves: 0", model.RightStatus);
        Assert.False(model.HasStoryLoaded);
        Assert.False(model.IsStatusStripVisible);
        Assert.Equal(">", model.PromptText);
        Assert.False(model.IsMorePromptVisible);
        Assert.Equal((short)0, model.SelectedWindow);
        Assert.Equal(string.Empty, model.InputText);
        Assert.True(presentationResetRaised);
    }

    [Fact]
    public void ResetAfterRestorePreservesV4StatusLabelsAndClearsLowerOutput()
    {
        var model = new GameDisplayModel();
        model.SetStoryLoaded(loaded: true);
        model.SetStoryVersion(4);
        model.SplitWindow(4);
        model.AppendOutput(
            "Mode: Simulation Mode\nLocation: Parkview Apartments\nTime: 11:49am\nDate: 3/12/2041",
            window: 1);
        model.AppendOutput("old lower output");
        model.InputText = "Restore";

        model.ResetAfterRestore();

        Assert.Equal(
            "Mode: Simulation Mode\nLocation: Parkview Apartments\nTime: 11:49am\nDate: 3/12/2041",
            model.UpperWindowText);
        Assert.Equal("", model.Transcript);
        Assert.Equal(4, model.UpperWindowHeight);
        Assert.True(model.IsUpperWindowVisible);
        Assert.True(model.HasStoryLoaded);
        Assert.Equal("", model.InputText);
        Assert.Equal(">", model.PromptText);
    }

    [Fact]
    public void SplitWindowTracksDeclaredUpperWindowRows()
    {
        var model = new GameDisplayModel();

        model.SplitWindow(2);

        Assert.True(model.IsUpperWindowVisible);
        Assert.Equal(2, model.UpperWindowHeight);

        model.SplitWindow(0);

        Assert.False(model.IsUpperWindowVisible);
        Assert.Equal(0, model.UpperWindowHeight);
    }

    [Fact]
    public void UpperWindowCursorAndEraseOperationsReplaceOldStatusText()
    {
        var model = new GameDisplayModel();
        model.SetUpperWindowWidth(40);
        model.SplitWindow(2);
        model.SetSelectedWindow(1);
        model.AppendOutput("Communications Mode", window: 1);

        model.MoveCursor(1, 1, 1);
        model.EraseLine(1, 1, 1);
        model.AppendOutput("Mode", window: 1);

        Assert.Equal("Mode", model.UpperWindowText);
    }

    [Fact]
    public void UpperWindowCursorWritesToRequestedRowAndColumn()
    {
        var model = new GameDisplayModel();
        model.SetUpperWindowWidth(40);
        model.SplitWindow(2);
        model.MoveCursor(1, 2, 1);

        model.AppendOutput("3/16/2031", window: 1);

        Assert.Equal("\n3/16/2031", model.UpperWindowText);
    }

    [Fact]
    public void UpperWindowOutputDoesNotCreateRowsBeyondItsSplitHeight()
    {
        var model = new GameDisplayModel();
        model.SplitWindow(1);

        model.AppendOutput("Old status\nNew status", window: 1);

        Assert.Equal("New status", model.UpperWindowText);
    }

    [Fact]
    public void V6UpperWindowKeepsItsExistingTextPresentation()
    {
        var model = new GameDisplayModel();
        model.SetStoryVersion(6);
        model.SplitWindow(200);
        model.AppendOutput("Status", window: 1);

        model.SplitWindow(100);
        model.MoveCursor(1, 2, 3);
        model.EraseLine(1, 1, 1);

        Assert.True(model.IsUpperWindowVisible);
        Assert.Equal("Status", model.UpperWindowText);
    }

    [Fact]
    public void SubmitsNonemptyCommandsAndClearsPrompt()
    {
        var model = new GameDisplayModel();
        string? submitted = null;
        model.CommandSubmitted += command => submitted = command;
        model.InputText = "look mailbox";

        model.SubmitCommand();

        Assert.Equal("look mailbox", submitted);
        Assert.Equal(string.Empty, model.InputText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmitsBlankLinesForVmInput(string input)
    {
        var model = new GameDisplayModel();
        var submissions = 0;
        model.CommandSubmitted += _ => submissions++;
        model.InputText = input;

        model.SubmitCommand();

        Assert.Equal(1, submissions);
        Assert.Equal(string.Empty, model.InputText);
    }
}

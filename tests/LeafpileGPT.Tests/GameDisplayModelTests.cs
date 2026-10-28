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
    }

    [Fact]
    public void AppendsOutputInOrderAndCanClearIt()
    {
        var model = new GameDisplayModel();
        model.AppendOutput("You are standing outside.\n");
        model.AppendOutput("There is a mailbox here.");

        Assert.Equal("You are standing outside.\nThere is a mailbox here.", model.Transcript);

        model.ClearOutput();

        Assert.Equal(string.Empty, model.Transcript);
    }

    [Fact]
    public void DisplaysLocationAndRightAlignedStatusText()
    {
        var model = new GameDisplayModel();

        model.SetStatusLine("West of House", "Score: 3  Moves: 12");

        Assert.Equal("West of House", model.Location);
        Assert.Equal("Score: 3  Moves: 12", model.RightStatus);
    }

    [Fact]
    public void ResetPresentationClearsStorySpecificScreenState()
    {
        var model = new GameDisplayModel();
        model.AppendOutput("stale");
        model.AppendOutput("upper", window: 1);
        model.SetStatusLine("Old Room", "Score: 9  Moves: 15");
        model.ShowMorePrompt(visible: true);
        model.SetSelectedWindow(1);
        model.SetColours(0x0608);
        model.InputText = "stale command";
        var presentationResetRaised = false;
        model.PresentationReset += () => presentationResetRaised = true;

        model.ResetPresentation();

        Assert.Equal(string.Empty, model.Transcript);
        Assert.Equal(string.Empty, model.UpperWindowText);
        Assert.False(model.IsUpperWindowVisible);
        Assert.Equal(string.Empty, model.Location);
        Assert.Equal("Score: 0  Moves: 0", model.RightStatus);
        Assert.Equal(">", model.PromptText);
        Assert.Equal((short)0, model.SelectedWindow);
        Assert.Equal(string.Empty, model.InputText);
        Assert.True(presentationResetRaised);
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

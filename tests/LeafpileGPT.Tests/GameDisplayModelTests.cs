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
    public void DoesNotSubmitBlankCommands(string input)
    {
        var model = new GameDisplayModel();
        var submissions = 0;
        model.CommandSubmitted += _ => submissions++;
        model.InputText = input;

        model.SubmitCommand();

        Assert.Equal(0, submissions);
        Assert.Equal(input, model.InputText);
    }
}

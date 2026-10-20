using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineInputOutputTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ReadStoresLowercaseTextInTheVersionedBufferAndTokenises(byte version)
    {
        var story = MachineStoryFactory.Create(version);
        WriteReadInstruction(story, version, 0x100, 0x150);
        SetEmptyDictionary(story, version);
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteByte(0x100, 8);
        state.Memory.WriteByte(0x150, 4);
        if (version >= 5)
        {
            state.Memory.WriteByte(0x101, 0);
        }

        var host = new FakeHost(new ZMachineInputResponse("LOOK", 13));
        var result = new ZMachineOpcodeExecutor(state, inputOutput: host).ExecuteNext();

        var count = version <= 4 ? 4 : state.Memory.ReadByte(0x101);
        var start = version <= 4 ? 0x101 : 0x102;
        Assert.Equal(4, count);
        Assert.Equal("look", ReadAscii(state, start, 4));
        Assert.Equal(1, state.Memory.ReadByte(0x151));
        Assert.Equal("look\n", result.Output);
        Assert.Empty(host.Commands);
        Assert.False(host.Requests.Single().ReadCharacter);
    }

    [Fact]
    public void V5ReadUsesFunctionKeyTerminatorAndStoresItsCode()
    {
        var story = MachineStoryFactory.Create(5);
        SetEmptyDictionary(story, 5);
        WriteWord(story, 0x2e, 0x180);
        story[0x180] = 129;
        story[0x181] = 0;
        WriteReadInstruction(story, 5, 0x100, 0);
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteByte(0x100, 8);
        state.Memory.WriteByte(0x101, 0);
        var host = new FakeHost(new ZMachineInputResponse("", 129));

        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);
        executor.ExecuteNext();

        Assert.Equal(129, state.ReadVariableValue(16));
        Assert.Contains((byte)129, host.Requests.Single().TerminatingCharacters);
    }

    [Fact]
    public void TimedInputExecutesRoutineAndCanCancelAndEraseTheCommand()
    {
        var story = MachineStoryFactory.Create(4);
        SetEmptyDictionary(story, 4);
        new byte[] { 0xe4, 0x04, 0x01, 0x00, 0x01, 0x50, 1, 0x01, 0x40 }.CopyTo(story, 0x200);
        story[0x500] = 0;
        new byte[] { 0xe5, 0x7f, (byte)'T', 0xb0 }.CopyTo(story, 0x501);
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteByte(0x100, 8);
        state.Memory.WriteByte(0x150, 2);
        var host = new FakeHost(new ZMachineInputResponse("partial", 0, TimedOut: true))
        {
            InvokeTimeoutCallback = true
        };

        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);
        executor.ExecuteNext();

        Assert.True(host.TimeoutCallbackInvoked);
        Assert.Equal((byte)0, state.Memory.ReadByte(0x101));
        Assert.Equal((byte)0, state.Memory.ReadByte(0x102));
        Assert.Empty(host.Commands);
        Assert.Equal("T", host.ScreenOutput);
    }

    [Fact]
    public void ReadCharAcceptsKeyboardInputAndHonorsTheCommandInputSelection()
    {
        var story = MachineStoryFactory.Create(4);
        new byte[] { 0xf3, 0x7f, 4, 0xf4, 0x7f, 1, 0xf6, 0x7f, 1, 0x10 }.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var host = new FakeHost(new ZMachineInputResponse(string.Empty, 129));

        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal((ushort)129, state.ReadVariableValue(16));
        Assert.True(host.Requests.Single().ReadCharacter);
        Assert.True(host.Requests.Single().FromCommandFile);
        Assert.Equal((byte)129, host.Keypresses.Single());
    }

    [Fact]
    public void CommandFileStreamRecordsEachCompletedReadCommand()
    {
        var story = MachineStoryFactory.Create(5);
        new byte[]
        {
            0xf3, 0x7f, 4,
            0xf4, 0x7f, 1,
            0xe4, 0x0f, 0x01, 0x00, 0x00, 0x00, 0x10
        }.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteByte(0x100, 12);
        state.Memory.WriteByte(0x101, 0);
        var host = new FakeHost(new ZMachineInputResponse("Take Lamp", 13));
        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);

        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.True(host.Requests.Single().FromCommandFile);
        Assert.Equal("take lamp", host.Commands.Single());
    }

    [Fact]
    public void UnsupportedCommandInputWarnsAndLeavesKeyboardInputSelected()
    {
        var story = MachineStoryFactory.Create(4);
        new byte[] { 0xf4, 0x7f, 1, 0xf6, 0x7f, 1, 0x10 }.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var host = new FakeHost(new ZMachineInputResponse("x", (byte)'x'))
        {
            SupportsCommandInput = false
        };
        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);

        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.False(host.Requests.Single().FromCommandFile);
        Assert.Contains("command input", host.UnavailableFeatures);
        Assert.Equal((ushort)'x', state.ReadVariableValue(16));
    }

    [Fact]
    public void TimedInputCapabilityFlagAndBufferModeFollowHostAndGameSettings()
    {
        var story = MachineStoryFactory.Create(4);
        story[0x200] = 0xf2;
        story[0x201] = 0x7f;
        story[0x202] = 0;
        var state = new ZMachineState(StoryLoader.Load(story));
        var host = new FakeHost(new ZMachineInputResponse(string.Empty, 13));
        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);

        Assert.Equal(0x80, state.Memory.ReadByte(0x01) & 0x80);
        executor.ExecuteNext();
        Assert.False(executor.OutputBufferingEnabled);

        var unsupportedState = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(4)));
        _ = new ZMachineOpcodeExecutor(unsupportedState, inputOutput: host with { SupportsTimedInput = false });
        Assert.Equal(0, unsupportedState.Memory.ReadByte(0x01) & 0x80);
    }

    [Theory]
    [InlineData("saveGame.sav", "SAVEGAME.SAV")]
    [InlineData("my?bad/name.txt", "MYBADNAM.TXT")]
    [InlineData("!?#", "NULL.AUX")]
    [InlineData("abcdefghijklmnop", "ABCDEFGH.AUX")]
    public void SuggestedFileNamesFollowTheStandardNormalizationRules(string proposedName, string expected)
    {
        Assert.Equal(expected, ZMachineFileName.NormalizeFileName(proposedName));
    }

    [Fact]
    public void ReadsAndNormalizesLengthPrefixedSuggestedFileName()
    {
        var story = MachineStoryFactory.Create(5);
        var name = System.Text.Encoding.ASCII.GetBytes("notes-1.txt");
        story[0x100] = (byte)name.Length;
        name.CopyTo(story, 0x101);
        var state = new ZMachineState(StoryLoader.Load(story));

        Assert.Equal("NOTES1.TXT", ZMachineFileName.ReadSuggestedFileName(state.Memory, 0x100));
    }

    [Fact]
    public void OutputStreamThreeCapturesZsciiAndNestedStreamsResumeOuterTable()
    {
        var story = MachineStoryFactory.Create(5);
        var code = new byte[]
        {
            0xf3, 0x4f, 3, 0x01, 0x20,
            0xe5, 0x7f, (byte)'A',
            0xf3, 0x4f, 3, 0x01, 0x30,
            0xe5, 0x7f, (byte)'B',
            0xf3, 0x3f, 0xff, 0xfd,
            0xe5, 0x7f, (byte)'C',
            0xf3, 0x3f, 0xff, 0xfd
        };
        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        for (var index = 0; index < 7; index++)
        {
            executor.ExecuteNext();
        }

        Assert.Equal(2, state.Memory.ReadWord(0x120));
        Assert.Equal((byte)'A', state.Memory.ReadByte(0x122));
        Assert.Equal((byte)'C', state.Memory.ReadByte(0x123));
        Assert.Equal(1, state.Memory.ReadWord(0x130));
        Assert.Equal((byte)'B', state.Memory.ReadByte(0x132));
    }

    [Fact]
    public void StreamTwoTracksFlagsAndTranscriptWhileStreamFourRecordsCommands()
    {
        var story = MachineStoryFactory.Create(5);
        var code = new byte[]
        {
            0xf3, 0x7f, 2,
            0xf3, 0x7f, 4,
            0xe5, 0x7f, (byte)'x',
            0xf3, 0x3f, 0xff, 0xfe
        };
        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var host = new FakeHost(new ZMachineInputResponse("Go", 13));
        var executor = new ZMachineOpcodeExecutor(state, inputOutput: host);

        executor.ExecuteNext();
        Assert.Equal(1, state.Memory.ReadWord(0x10) & 1);
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal("x", host.Transcript);
        Assert.Empty(host.Commands);
        Assert.Equal(0, state.Memory.ReadWord(0x10) & 1);
    }

    private static void WriteReadInstruction(byte[] story, byte version, ushort textBuffer, ushort parseBuffer)
    {
        if (version == 6)
        {
            WriteWord(story, 0x06, 0x80);
            story[0x200] = 0;
        }

        var instruction = version >= 4
            ? new byte[] { 0xe4, 0x0f, (byte)(textBuffer >> 8), (byte)textBuffer, (byte)(parseBuffer >> 8), (byte)parseBuffer, 0x10 }
            : new byte[] { 0xe4, 0x0f, (byte)(textBuffer >> 8), (byte)textBuffer, (byte)(parseBuffer >> 8), (byte)parseBuffer };
        instruction.CopyTo(story, version == 6 ? 0x201 : 0x200);
    }

    private static void SetEmptyDictionary(byte[] story, byte version)
    {
        WriteWord(story, 0x08, 0x0310);
        story[0x310] = 0;
        story[0x311] = version <= 3 ? (byte)4 : (byte)6;
        story[0x312] = 0;
        story[0x313] = 0;
    }

    private static string ReadAscii(ZMachineState state, int address, int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = state.Memory.ReadByte(checked((ushort)(address + index)));
        }

        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private static void WriteWord(byte[] data, int address, ushort value)
    {
        data[address] = (byte)(value >> 8);
        data[address + 1] = (byte)value;
    }

    private sealed record FakeHost(ZMachineInputResponse response) : IZMachineInputOutput
    {
        public List<ZMachineInputRequest> Requests { get; } = [];

        public List<string> Commands { get; } = [];

        public List<byte> Keypresses { get; } = [];

        public string Transcript { get; private set; } = string.Empty;

        public string ScreenOutput { get; private set; } = string.Empty;

        public bool InvokeTimeoutCallback { get; init; }

        public bool TimeoutCallbackInvoked { get; private set; }

        public bool SupportsTimedInput { get; init; } = true;

        public bool SupportsCommandInput { get; init; } = true;

        public bool SupportsTranscript { get; init; } = true;

        public bool SupportsCommandRecording { get; init; } = true;

        public List<string> UnavailableFeatures { get; } = [];

        public ZMachineInputResponse ReadInput(ZMachineInputRequest request, Func<bool>? timedInterrupt)
        {
            Requests.Add(request);
            if (InvokeTimeoutCallback && timedInterrupt is not null)
            {
                TimeoutCallbackInvoked = true;
                _ = timedInterrupt();
            }

            return response;
        }

        public void WriteScreenOutput(string text) => ScreenOutput += text;

        public void WriteTranscript(string text) => Transcript += text;

        public void WriteCommand(string text) => Commands.Add(text);

        public void WriteKeypress(byte zsciiCode) => Keypresses.Add(zsciiCode);

        public void ReportFileAccessUnavailable(string feature)
        {
            UnavailableFeatures.Add(feature);
        }
    }
}

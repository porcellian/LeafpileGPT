namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineInputHandlerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task LegacyReadStoresLowercaseInputAndTokenizesWithoutAResult(byte version)
    {
        var state = CreateState(version);
        state.Memory.WriteByte(400, 3);
        state.Memory.WriteByte(500, 4);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("LOOK", 10)
        };
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        var handler = new ZMachineInputHandler(state, provider);

        var result = await handler.ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("loo", DecodeText(state, 400));
        Assert.Equal((byte)0, state.Memory.ReadByte(404));
        Assert.Equal((byte)1, state.Memory.ReadByte(501));
        Assert.Equal((byte)3, state.Memory.ReadByte(504));
        Assert.Equal((byte)1, state.Memory.ReadByte(505));
        Assert.Contains("loo", screen);
        Assert.Contains("\n", screen);
        Assert.True(provider.LastLineRequest!.ShowStatusLineBeforeInput == (version <= 3));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ReadAppendsToV5PlusTextNormalizesLineFeedAndStoresTerminator(byte version)
    {
        var state = CreateState(version);
        state.Memory.WriteByte(400, 5);
        state.Memory.WriteByte(401, 2);
        state.Memory.WriteByte(402, (byte)'h');
        state.Memory.WriteByte(403, (byte)'i');
        state.Memory.WriteByte(500, 4);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("THERE", 10)
        };
        var screen = new List<string>();
        state.OutputStreams.ScreenWriter = screen.Add;
        var handler = new ZMachineInputHandler(state, provider);

        var result = await handler.ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)13, result);
        Assert.Equal("hithe", DecodeText(state, 400));
        Assert.Equal((byte)5, state.Memory.ReadByte(401));
        Assert.Equal((ushort)3, provider.LastLineRequest!.MaximumAdditionalCharacters);
        Assert.Equal(new byte[] { 13, 10 }, provider.LastLineRequest.TerminatingCharacters.Take(2));
        Assert.Contains("the", screen);
        Assert.Contains("\n", screen);
    }

    [Fact]
    public async Task V5ReadCanSkipLexicalAnalysisWhenParseAddressIsZero()
    {
        var state = CreateState(5);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("north", 13)
        };

        var result = await new ZMachineInputHandler(state, provider).ReadLineAsync(400, 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)13, result);
        Assert.Equal("north", DecodeText(state, 400));
    }

    [Fact]
    public async Task AcceptedLineStartsFreshMorePageBeforeEchoingPlayerInput()
    {
        var state = CreateState(5);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        var screen = state.OutputStreams.Screen;
        screen.SetDimensions(80, 4);
        screen.SetMorePageLineLimit(3);
        screen.WriteText("previous response\nmore previous response\n", null);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("look", 13)
        };

        await new ZMachineInputHandler(state, provider).ReadLineAsync(
            400,
            0,
            cancellationToken: TestContext.Current.CancellationToken);

        screen.WriteText("first response line\n", null);
        screen.WriteText("second response line", null);

        Assert.False(screen.IsWaitingForMore);
    }

    [Fact]
    public async Task ReadDropsCharactersOutsideTheDefinedInputAndOutputSet()
    {
        var state = CreateState(5);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("A\tB😀C", 13)
        };

        await new ZMachineInputHandler(state, provider).ReadLineAsync(400, 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("abc", DecodeText(state, 400));
    }

    [Fact]
    public async Task V5TerminatingCharacterTableAcceptsListedFunctionKeysAndWildcard()
    {
        var listedState = CreateState(5, includeTerminatingCharacters: true);
        listedState.Memory.WriteByte(400, 8);
        listedState.Memory.WriteByte(401, 0);
        listedState.Memory.WriteByte(500, 1);
        var listedProvider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("north", 129)
        };
        var listedResult = await new ZMachineInputHandler(listedState, listedProvider).ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)129, listedResult);
        Assert.Contains((byte)129, listedProvider.LastLineRequest!.TerminatingCharacters);

        var wildcardState = CreateState(5, includeTerminatingCharacters: true);
        wildcardState.Memory.WriteByte(400, 8);
        wildcardState.Memory.WriteByte(401, 0);
        wildcardState.Memory.WriteByte(500, 1);
        var wildcardProvider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("north", 254)
        };
        var wildcardResult = await new ZMachineInputHandler(wildcardState, wildcardProvider).ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)254, wildcardResult);
    }

    [Fact]
    public void InputLineEditorLowercasesLimitsFiltersAndSupportsBackspace()
    {
        var state = CreateState(6);
        var editor = new ZMachineInputLineEditor(new ZsciiCodec(state.Memory), 3);

        Assert.True(editor.Append('A'));
        Assert.False(editor.Append('\t'));
        Assert.False(editor.Append('\ud83d'));
        Assert.True(editor.Append('B'));
        Assert.True(editor.Backspace());
        Assert.True(editor.Append('C'));
        Assert.True(editor.Append('D'));
        Assert.False(editor.Append('E'));

        Assert.Equal("acd", editor.Text);
    }

    [Fact]
    public async Task InputStreamOpcodeSelectsKeyboardOrCommandFileStream()
    {
        var state = CreateState(5);
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("input_stream", [Constant(1)]), state);
        Assert.Equal((byte)1, state.CurrentInputStream);
        executor.Execute(Instruction("input_stream", [Constant(0)]), state);
        Assert.Equal((byte)0, state.CurrentInputStream);
        await Assert.ThrowsAsync<ZMachineExecutionException>(async () =>
            await Task.Run(() => executor.Execute(Instruction("input_stream", [Constant(2)]), state), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommandInputUsesSelectedStreamAndInputStreamFourRecordsTheLine()
    {
        var state = CreateState(5);
        state.SelectInputStream(1);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        state.Memory.WriteByte(500, 2);
        var lines = new List<string>();
        state.OutputStreams.CommandWriter = lines.Add;
        state.OutputStreams.SelectStream(4);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("WAIT", 13)
        };

        await new ZMachineInputHandler(state, provider).ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)1, provider.LastLineRequest!.InputStream);
        Assert.Equal(new[] { "wait\n" }, lines);
    }

    [Fact]
    public async Task CommandFileInputDoesNotStopForMorePrompts()
    {
        var state = CreateState(5);
        state.OutputStreams.Screen.SetDimensions(4, 2, 4, 2);
        state.OutputStreams.WriteText("a\nb\n");
        Assert.False(state.OutputStreams.Screen.IsWaitingForMore);
        state.OutputStreams.WriteText("next page");
        Assert.True(state.OutputStreams.Screen.IsWaitingForMore);
        state.SelectInputStream(1);
        Assert.False(state.OutputStreams.Screen.IsWaitingForMore);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("look", 13),
            CharacterReader = _ =>
            {
                throw new InvalidOperationException("Command-file reads must not wait for keyboard input.");
            }
        };

        var terminator = await new ZMachineInputHandler(state, provider)
            .ReadLineAsync(400, 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)13, terminator);
        Assert.Equal((byte)1, provider.LastLineRequest!.InputStream);
    }

    [Fact]
    public async Task V3StatusLineUpdatesBeforeKeyboardReadButNotCommandFileRead()
    {
        var state = CreateState(3);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        state.Memory.WriteByte(500, 0);
        var updates = 0;
        state.OutputStreams.Screen.StatusLineWriter = _ => updates++;
        var provider = new FakeInputProvider
        {
            LineResult = new ZMachineLineInputResult("look", 13)
        };
        var input = new ZMachineInputHandler(state, provider);

        await input.ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, updates);

        state.SelectInputStream(1);
        await input.ReadLineAsync(400, 500, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task ReadCharReturnsAndRecordsDefinedZsciiInputIncludingFunctionKeys()
    {
        var state = CreateState(5);
        var keys = new List<byte>();
        state.OutputStreams.KeyPressWriter = keys.Add;
        state.OutputStreams.SelectStream(4);
        var provider = new FakeInputProvider { CharacterResult = 253 };

        var character = await new ZMachineInputHandler(state, provider).ReadCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((ushort)253, character);
        Assert.Equal(new byte[] { 253 }, keys);
    }

    [Fact]
    public async Task ReadCharRejectsUnsupportedDeviceAndUndefinedCodes()
    {
        var state = CreateState(5);
        var provider = new FakeInputProvider { CharacterResult = 255 };
        var handler = new ZMachineInputHandler(state, provider);

        await Assert.ThrowsAsync<ZMachineExecutionException>(async () => await handler.ReadCharacterAsync(2, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ZMachineExecutionException>(async () => await handler.ReadCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TimedReadCallsRoutinePeriodicallyAndContinuesAfterFalse()
    {
        var state = CreateState(5);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        state.Memory.WriteByte(500, 2);
        var provider = new FakeInputProvider
        {
            SupportsTimedInput = true,
            LineReader = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return default;
            }
        };
        var callbacks = new List<ushort>();
        var redraws = 0;
        var handler = new ZMachineInputHandler(
            state,
            provider,
            (routine, _) =>
            {
                callbacks.Add(routine);
                return ValueTask.FromResult(callbacks.Count == 2);
            },
            () => redraws++);

        var result = await handler.ReadLineAsync(400, 500, time: 1, routine: 77, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((byte)0, result);
        Assert.Equal(new ushort[] { 77, 77 }, callbacks);
        Assert.Equal(1, redraws);
        Assert.Equal((byte)0, state.Memory.ReadByte(401));
        Assert.All(Enumerable.Range(402, 8), address => Assert.Equal((byte)0, state.Memory.ReadByte((ushort)address)));
        Assert.Equal(0, state.Memory.ReadByte(501));
        Assert.NotEqual((byte)0, state.Memory.ReadByte(1) & 0x80);
    }

    [Fact]
    public async Task ReadCharTimeoutReturnsZeroAndAdvertisesTimerCapability()
    {
        var state = CreateState(4);
        var provider = new FakeInputProvider
        {
            SupportsTimedInput = true,
            CharacterReader = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
        };
        var handler = new ZMachineInputHandler(state, provider, (_, _) => ValueTask.FromResult(true));

        var result = await handler.ReadCharacterAsync(1, time: 1, routine: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((ushort)0, result);
        Assert.NotEqual((byte)0, state.Memory.ReadByte(1) & 0x80);
    }

    [Fact]
    public async Task AreReadOpcodeStoresTerminatorAndReadOpcodeRequiresAsyncExecution()
    {
        var state = CreateState(5);
        state.Memory.WriteByte(400, 8);
        state.Memory.WriteByte(401, 0);
        state.Memory.WriteByte(500, 2);
        var provider = new FakeInputProvider { LineResult = new ZMachineLineInputResult("go", 13) };
        var executor = new ZMachineOpcodeExecutor();

        await Assert.ThrowsAsync<ZMachineExecutionException>(async () =>
            await Task.Run(() => executor.Execute(Instruction("aread", [Constant(400), Constant(500)], store: 16), state), TestContext.Current.CancellationToken));
        await executor.ExecuteAsync(Instruction("aread", [Constant(400), Constant(500)], store: 16), state, provider, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((ushort)13, state.Memory.ReadWord(64));
        Assert.Equal("go", DecodeText(state, 400));
    }

    private static ZMachineExecutionState CreateState(byte version, bool includeTerminatingCharacters = false)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        StoryFixtureBuilder.WriteWord(bytes, 0x10, 0);
        StoryFixtureBuilder.WriteWord(bytes, 0x2E, 0);
        StoryFixtureBuilder.WriteWord(bytes, 0x34, 0);
        StoryFixtureBuilder.WriteWord(bytes, 0x36, 0);
        bytes[200] = 0;
        bytes[201] = version <= 3 ? (byte)4 : (byte)6;
        StoryFixtureBuilder.WriteWord(bytes, 202, 0);
        if (includeTerminatingCharacters)
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x2E, 230);
            bytes[230] = 129;
            bytes[231] = byte.MaxValue;
            bytes[232] = 0;
        }
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

    private static string DecodeText(ZMachineExecutionState state, ushort address)
    {
        var codec = new ZsciiCodec(state.Memory);
        return string.Concat(ZMachineTextBuffer.ReadText(state.Memory, address).Select(character => codec.Decode(character)));
    }

    private static DecodedInstruction Instruction(string mnemonic, DecodedOperand[] operands, byte? store = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Variable, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), StoresResult: store is not null);
        return new DecodedInstruction(850, 851, InstructionForm.Variable, OpcodeClass.Variable, 0,
            definition, operands.ToImmutableArray(), store, null, ImmutableArray<byte>.Empty);
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);

    private sealed class FakeInputProvider : IZMachineInputProvider
    {
        public bool SupportsTimedInput { get; init; }

        public ZMachineLineInputResult LineResult { get; init; }

        public ushort CharacterResult { get; init; }

        public Func<ZMachineLineInputRequest, CancellationToken, ValueTask<ZMachineLineInputResult>>? LineReader { get; init; }

        public Func<CancellationToken, ValueTask<ushort>>? CharacterReader { get; init; }

        public ZMachineLineInputRequest? LastLineRequest { get; private set; }

        public ValueTask<ZMachineLineInputResult> ReadLineAsync(
            ZMachineLineInputRequest request,
            CancellationToken cancellationToken = default)
        {
            LastLineRequest = request;
            return LineReader is null
                ? ValueTask.FromResult(LineResult)
                : LineReader(request, cancellationToken);
        }

        public ValueTask<ushort> ReadCharacterAsync(CancellationToken cancellationToken = default) =>
            CharacterReader is null ? ValueTask.FromResult(CharacterResult) : CharacterReader(cancellationToken);
    }
}

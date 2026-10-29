namespace LeafpileGPT.Tests;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineEventServicesTests
{
    private const uint CodeAddress = 850;

    [Fact]
    public void StartClearsUnavailableOptionalCapabilitiesAndRetainsSupportedOnes()
    {
        var story = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(story, 0x10, 0x01A0);
        story[0x36] = 0;
        story[0x37] = 0;
        StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        var services = new FakeEventServices { SupportsMouse = true, SupportsSoundEffects = true };

        var state = Start(story, services);

        Assert.Equal((ushort)0x00A0, state.Memory.ReadWord(0x10));
        Assert.Equal((byte)0x20, (byte)(state.Memory.ReadByte(1) & 0x20));
    }

    [Fact]
    public void MouseClickUpdatesCoordinatesOnlyWhenExtensionWordsExist()
    {
        var story = StoryFixtureBuilder.Create(5);
        StoryFixtureBuilder.WriteWord(story, 0x36, 500);
        StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        var state = Start(story, new FakeEventServices());
        state.Memory.WriteWord(500, 2);

        state.RecordMouseClick(31, 47);

        Assert.Equal((ushort)31, state.Memory.ReadWord(502));
        Assert.Equal((ushort)47, state.Memory.ReadWord(504));
        state.Memory.WriteWord(500, 1);
        state.RecordMouseClick(10, 20);
        Assert.Equal((ushort)31, state.Memory.ReadWord(502));
    }

    [Fact]
    public void ReadMouseWritesRealtimeYxButtonsAndMenuWords()
    {
        var state = Start(StoryFixtureBuilder.Create(6), new FakeEventServices
        {
            SupportsMouse = true,
            Mouse = new ZMachineMouseState(12, 34, 0x0105, 0x0302)
        });

        Executor().Execute(Instruction("read_mouse", [Constant(600)]), state);

        Assert.Equal((ushort)12, state.Memory.ReadWord(600));
        Assert.Equal((ushort)34, state.Memory.ReadWord(602));
        Assert.Equal((ushort)0x0105, state.Memory.ReadWord(604));
        Assert.Equal((ushort)0x0302, state.Memory.ReadWord(606));
    }

    [Fact]
    public void ReadMouseWithoutHostSupportReturnsOffscreenAndClearsButtons()
    {
        var state = Start(StoryFixtureBuilder.Create(6), new FakeEventServices());

        Executor().Execute(Instruction("read_mouse", [Constant(600)]), state);

        Assert.Equal(ushort.MaxValue, state.Memory.ReadWord(600));
        Assert.Equal(ushort.MaxValue, state.Memory.ReadWord(602));
        Assert.Equal((ushort)0, state.Memory.ReadWord(604));
        Assert.Equal((ushort)0, state.Memory.ReadWord(606));
    }

    [Fact]
    public void MouseWindowValidatesV6WindowAndDelegatesConstraint()
    {
        var services = new FakeEventServices();
        var state = Start(StoryFixtureBuilder.Create(6), services);
        var executor = Executor();

        executor.Execute(Instruction("mouse_window", [Constant(unchecked((ushort)-1))]), state);

        Assert.Equal((short)-1, state.MouseWindow);
        Assert.Equal((short)-1, services.LastMouseWindow);
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction("mouse_window", [Constant(8)]), state));
    }

    [Fact]
    public void MakeMenuDecodesZsciiPointerTableAndBranchesOnHostResult()
    {
        var story = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(story, 540, 2);
        StoryFixtureBuilder.WriteWord(story, 542, 560);
        StoryFixtureBuilder.WriteWord(story, 544, 570);
        story[560] = (byte)'D'; story[561] = (byte)'o'; story[562] = 0;
        story[570] = (byte)'G'; story[571] = (byte)'o'; story[572] = 0;
        StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        var services = new FakeEventServices { SupportsMenus = true, MenuResult = true };
        var state = Start(story, services);

        Executor().Execute(Instruction("make_menu", [Constant(3), Constant(540)], branch: new DecodedBranch(true, 4, CodeAddress + 4, null)), state);

        Assert.Equal((ushort)3, services.LastMenuNumber);
        Assert.Equal(new[] { "Do", "Go" }, services.LastMenuEntries);
        Assert.Equal(CodeAddress + 4, state.ProgramCounter);
    }

    [Fact]
    public void MakeMenuRemovalPassesAnEmptyMenuAndUnsupportedMenuBranchesFalse()
    {
        var services = new FakeEventServices { SupportsMenus = true, MenuResult = true };
        var state = Start(StoryFixtureBuilder.Create(6), services);
        Executor().Execute(Instruction("make_menu", [Constant(3), Constant(0)], branch: new DecodedBranch(true, 4, CodeAddress + 4, null)), state);
        Assert.Empty(services.LastMenuEntries);
        Assert.Equal(CodeAddress + 4, state.ProgramCounter);

        var unsupported = Start(StoryFixtureBuilder.Create(6), new FakeEventServices());
        Executor().Execute(Instruction("make_menu", [Constant(3), Constant(0)], branch: new DecodedBranch(true, 4, CodeAddress + 4, null)), unsupported);
        Assert.Equal(CodeAddress + 1, unsupported.ProgramCounter);
    }

    [Fact]
    public void MakeMenuBranchesFalseWhenHostCannotCreateRequestedMenu()
    {
        var story = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(story, 540, 0);
        var state = Start(story, new FakeEventServices { SupportsMenus = true, MenuResult = false });

        Executor().Execute(Instruction("make_menu", [Constant(3), Constant(540)], branch: new DecodedBranch(true, 4, CodeAddress + 4, null)), state);

        Assert.Equal(CodeAddress + 1, state.ProgramCounter);
    }

    [Fact]
    public void SoundEffectDecodesActionVolumeRepeatsAndCompletionRoutine()
    {
        var services = new FakeEventServices { SupportsSoundEffects = true };
        var state = Start(StoryFixtureBuilder.Create(6), services);
        var calls = new List<ushort>();
        state.RoutineCallRequested = calls.Add;

        Executor().Execute(Instruction("sound_effect", [Constant(7), Constant(2), Constant(0x0305), Constant(123)]), state);

        Assert.Equal(new ZMachineSoundEffectRequest(7, ZMachineSoundAction.Start, 5, 3, 123), services.LastSoundRequest);
        services.SoundCompletion!.Invoke();
        Assert.Equal(new ushort[] { 123 }, calls);
    }

    [Fact]
    public void SoundEffectPassesBlorbChannelResourceVolumeLoopingAndCompletionToHost()
    {
        var payload = Encoding.ASCII.GetBytes("OggS\0\0\0\0");
        var blorb = BlorbFile.Parse(ZMachineBlorbTests.BlorbFixtureBuilder.Create(
            [new("OGGV", payload, "Snd ", 7)]));
        var services = new FakeEventServices { SupportsSoundEffects = true };
        var state = ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(5)),
            eventServices: services, soundCatalog: blorb.Sounds);
        var calls = new List<ushort>();
        state.RoutineCallRequested = calls.Add;

        Executor().Execute(Instruction("sound_effect", [Constant(7), Constant(2), Constant(0xFF08), Constant(123)]), state);

        var request = services.LastSoundRequest!;
        Assert.Equal(8, request.Volume);
        Assert.Equal(byte.MaxValue, request.Repeats);
        Assert.True(request.LoopIndefinitely);
        Assert.Equal(BlorbSoundChannel.Music, request.Channel);
        Assert.Equal(BlorbSoundFormat.OggVorbis, request.Resource!.Format);
        Assert.Equal(payload, request.Resource.EncodedData.ToArray());
        services.SoundCompletion!.Invoke();
        Assert.Equal(new ushort[] { 123 }, calls);
    }

    [Fact]
    public void VersionThreeSoundRequestUsesBlorbLoopHintAndDefaultPlayOnce()
    {
        var payload = Encoding.ASCII.GetBytes("OggS\0\0\0\0");
        var loop = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(loop, 3);
        BinaryPrimitives.WriteUInt32BigEndian(loop.AsSpan(4), 0);
        var loopingBlorb = BlorbFile.Parse(ZMachineBlorbTests.BlorbFixtureBuilder.Create(
            [new("OGGV", payload, "Snd ", 3)], [new("Loop", loop)]));
        var services = new FakeEventServices { SupportsSoundEffects = true };
        var state = ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(3)),
            eventServices: services, soundCatalog: loopingBlorb.Sounds);

        Executor().Execute(Instruction("sound_effect", [Constant(3), Constant(2)]), state);

        Assert.True(services.LastSoundRequest!.LoopIndefinitely);
        Assert.Equal((byte)0, services.LastSoundRequest.Repeats);
    }

    [Fact]
    public void SoundEffectSupportsBleepsAndRejectsInvalidForms()
    {
        var services = new FakeEventServices();
        var state = Start(StoryFixtureBuilder.Create(5), services);
        var executor = Executor();

        executor.Execute(Instruction("sound_effect", [Constant(1)]), state);

        Assert.Equal(new ZMachineSoundEffectRequest(1, ZMachineSoundAction.Start, 255, 1, 0), services.LastSoundRequest);
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction("sound_effect", [Constant(1), Constant(2)]), state));
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction("sound_effect", [Constant(0), Constant(2)]), state));
    }

    [Fact]
    public void VersionFourSoundEffectCompatibilityBleepReachesTheHost()
    {
        var story = StoryFixtureBuilder.Create(4);
        new byte[] { 0xF5, 0x7F, 0x01 }.CopyTo(story, checked((int)CodeAddress));
        var services = new FakeEventServices { SupportsSoundEffects = true };
        var state = Start(story, services);
        var instruction = new ZMachineInstructionDecoder(state.Memory).Decode(CodeAddress);

        Executor().Execute(instruction, state);

        Assert.Equal(new ZMachineSoundEffectRequest(1, ZMachineSoundAction.Start, byte.MaxValue, 1, 0), services.LastSoundRequest);
    }

    [Fact]
    public void VersionThreeRejectsRepeatCountsAndStoppedSoundsDoNotRequestCompletion()
    {
        var v3Services = new FakeEventServices { SupportsSoundEffects = true };
        var v3 = Start(StoryFixtureBuilder.Create(3), v3Services);
        Executor().Execute(Instruction("sound_effect", [Constant(3), Constant(2)]), v3);
        Assert.Equal((byte)0, v3Services.LastSoundRequest!.Repeats);
        Assert.Throws<ZMachineExecutionException>(() => Executor().Execute(
            Instruction("sound_effect", [Constant(3), Constant(2), Constant(0x0108)]), v3));

        var services = new FakeEventServices { SupportsSoundEffects = true };
        var state = Start(StoryFixtureBuilder.Create(5), services);
        var calls = new List<ushort>();
        state.RoutineCallRequested = calls.Add;
        Executor().Execute(Instruction("sound_effect", [Constant(3), Constant(3), Constant(0x0108), Constant(55)]), state);
        Assert.Null(services.SoundCompletion);
        Assert.Empty(calls);
    }

    [Fact]
    public void NewlineCountdownInvokesGameCallbackBeforeFollowingOutput()
    {
        var state = Start(StoryFixtureBuilder.Create(6), new FakeEventServices());
        var events = new List<string>();
        state.OutputStreams.Screen.PutWindowProperty(0, 8, 321);
        state.OutputStreams.Screen.PutWindowProperty(0, 9, 1);
        state.OutputStreams.Screen.NewlineInterruptHandler = routine => events.Add($"routine:{routine}");

        state.OutputStreams.Screen.WriteText("a\nb", text => events.Add($"text:{text}"));

        Assert.Equal(new[] { "text:a\n", "routine:321", "text:b" }, events);
        Assert.Equal((ushort)0, state.OutputStreams.Screen.GetWindowProperty(0, 9));
    }

    [Fact]
    public void SoundOpcodeMetadataAllowsBleepFormInV3V4CompatibilityAndV5Plus()
    {
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Variable, 21, 3, out var v3));
        Assert.Equal((byte)1, v3.MinimumOperands);
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Variable, 21, 4, out var v4));
        Assert.Equal((byte)1, v4.MinimumOperands);
        Assert.Equal((byte)1, v4.MaximumOperands);
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Variable, 21, 5, out var v5));
        Assert.Equal((byte)1, v5.MinimumOperands);
    }

    private static ZMachineExecutionState Start(byte[] story, FakeEventServices services)
    {
        if (story[0] == 6)
        {
            story[800] = 0;
        }

        StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        return ZMachineExecutionState.Start(StoryFile.Parse(story), eventServices: services);
    }

    private static ZMachineOpcodeExecutor Executor() => new();

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);

    private static DecodedInstruction Instruction(
        string mnemonic,
        DecodedOperand[] operands,
        DecodedBranch? branch = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Extended, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), Branches: branch is not null);
        return new DecodedInstruction(CodeAddress, CodeAddress + 1, InstructionForm.Extended,
            OpcodeClass.Extended, 0, definition, operands.ToImmutableArray(), null, branch, ImmutableArray<byte>.Empty);
    }

    private sealed class FakeEventServices : IZMachineEventServices
    {
        public bool SupportsMouse { get; init; }

        public bool SupportsMenus { get; init; }

        public bool SupportsSoundEffects { get; init; }

        public ZMachineMouseState Mouse { get; init; } = ZMachineMouseState.Offscreen;

        public bool MenuResult { get; init; }

        public short LastMouseWindow { get; private set; }

        public ushort LastMenuNumber { get; private set; }

        public ImmutableArray<string> LastMenuEntries { get; private set; }

        public ZMachineSoundEffectRequest? LastSoundRequest { get; private set; }

        public Action? SoundCompletion { get; private set; }

        public ZMachineMouseState ReadMouse() => Mouse;

        public void SetMouseWindow(short window) => LastMouseWindow = window;

        public bool MakeMenu(ushort number, ImmutableArray<string> entries)
        {
            LastMenuNumber = number;
            LastMenuEntries = entries;
            return MenuResult;
        }

        public void PerformSoundEffect(ZMachineSoundEffectRequest request, Action? completed)
        {
            LastSoundRequest = request;
            SoundCompletion = completed;
        }
    }
}

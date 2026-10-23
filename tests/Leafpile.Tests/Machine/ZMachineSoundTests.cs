using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineSoundTests
{
    [Fact]
    public void BleepAndNoOperandBleepUseImmediateSinglePlayback()
    {
        var sound = new FakeSound();
        var oneOperand = CreateState(5, [0xf5, 0x7f, 2]);
        var noOperands = CreateState(5, [0xf5, 0xff]);
        var executor = new ZMachineOpcodeExecutor(oneOperand, sound: sound);

        executor.ExecuteNext();
        new ZMachineOpcodeExecutor(noOperands, sound: sound).ExecuteNext();

        Assert.Collection(sound.Played,
            request => Assert.Equal(new ZMachineSoundRequest(2, 8, 1, 0), request),
            request => Assert.Equal(new ZMachineSoundRequest(1, 8, 1, 0), request));
        Assert.All(sound.Completions, Assert.Null);
    }

    [Fact]
    public void DispatchesPrepareStartStopAndFinishWithDefaultsAndVolumeRepeatValues()
    {
        var state = CreateState(5,
        [
            0xf5, 0x5f, 3, 1,
            0xf5, 0x53, 3, 2, 0x01, 0xff,
            0xf5, 0x5f, 0, 3,
            0xf5, 0x5f, 0, 4
        ]);
        var sound = new FakeSound();
        var executor = new ZMachineOpcodeExecutor(state, sound: sound);

        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal((ushort)3, Assert.Single(sound.Prepared));
        Assert.Equal(new ZMachineSoundRequest(3, -1, 1, 0), Assert.Single(sound.Played));
        Assert.Equal(new ushort?[] { null }, sound.Stopped);
        Assert.Equal(new ushort?[] { null }, sound.Finished);
    }

    [Fact]
    public void SupportsInfiniteRepeatsAndQueuesNaturalCompletionRoutineOnInterpreterThread()
    {
        var story = MachineStoryFactory.Create(5);
        new byte[] { 0xf5, 0x50, 3, 2, 0xff, 0x05, 0x01, 0x40 }.CopyTo(story, 0x200);
        story[0x500] = 0;
        story[0x501] = 0xb0;
        var state = new ZMachineState(StoryLoader.Load(story));
        var sound = new FakeSound();
        var executor = new ZMachineOpcodeExecutor(state, sound: sound);

        executor.ExecuteNext();
        var completion = Assert.Single(sound.Completions);
        Assert.Equal(new ZMachineSoundRequest(3, 5, null, 0x0140), Assert.Single(sound.Played));
        Assert.Equal(0x208, state.ProgramCounter);

        completion!();
        Assert.Equal(0x208, state.ProgramCounter);
        executor.ProcessSoundCompletions();
        Assert.Equal(0x501, state.ProgramCounter);
        executor.ExecuteNext();
        Assert.Equal(0x208, state.ProgramCounter);
    }

    [Fact]
    public void RunsSoundCompletionRoutinesSequentiallyWithoutNestingThem()
    {
        var story = MachineStoryFactory.Create(5);
        new byte[]
        {
            0xf5, 0x50, 3, 2, 0x01, 0x08, 0x01, 0x40,
            0xf5, 0x50, 4, 2, 0x01, 0x08, 0x01, 0x44
        }.CopyTo(story, 0x200);
        story[0x500] = 0;
        story[0x501] = 0xb0;
        story[0x510] = 0;
        story[0x511] = 0xb0;
        var state = new ZMachineState(StoryLoader.Load(story));
        var sound = new FakeSound();
        var executor = new ZMachineOpcodeExecutor(state, sound: sound);

        executor.ExecuteNext();
        executor.ExecuteNext();
        Assert.Equal((ushort)0x0140, sound.Played[0].CompletionRoutine);
        Assert.Equal((ushort)0x0144, sound.Played[1].CompletionRoutine);
        sound.Completions[0]!();
        sound.Completions[1]!();
        executor.ProcessSoundCompletions();
        Assert.Equal(0x501, state.ProgramCounter);
        Assert.Equal(2, state.CallStack.FrameCount);

        executor.ExecuteNext();
        Assert.Equal(0x210, state.ProgramCounter);
        Assert.Equal(1, state.CallStack.FrameCount);
        executor.ProcessSoundCompletions();
        Assert.Equal(0x511, state.ProgramCounter);
        Assert.Equal(2, state.CallStack.FrameCount);
        executor.ExecuteNext();
        Assert.Equal(0x210, state.ProgramCounter);
        Assert.Equal(1, state.CallStack.FrameCount);
    }

    [Fact]
    public void VersionThreeRequiresRepeatByteZeroAndTreatsThirdOperandAsVolume()
    {
        var state = CreateState(3, [0xf5, 0x53, 3, 2, 0, 6]);
        var sound = new FakeSound();

        new ZMachineOpcodeExecutor(state, sound: sound).ExecuteNext();

        Assert.Equal(new ZMachineSoundRequest(3, 6, 1, 0), Assert.Single(sound.Played));
    }

    [Fact]
    public void VersionFourSoundEffectUsesVersionThreeOperandsAndDoesNotRepeat()
    {
        var state = CreateState(4, [0xf5, 0x53, 3, 2, 0, 6]);
        var sound = new FakeSound();

        new ZMachineOpcodeExecutor(state, sound: sound).ExecuteNext();

        Assert.Equal(new ZMachineSoundRequest(3, 6, 1, 0), Assert.Single(sound.Played));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void UsesVersionCorrectDefaultVolumeAndRepeatCount(byte version)
    {
        var state = CreateState(version, [0xf5, 0x5f, 3, 2]);
        var sound = new FakeSound();

        new ZMachineOpcodeExecutor(state, sound: sound).ExecuteNext();

        Assert.Equal(new ZMachineSoundRequest(3, 8, 1, 0), Assert.Single(sound.Played));
    }

    [Fact]
    public void RejectsInvalidVolumeAndVersionThreeRepeatRequests()
    {
        var badVolume = CreateState(5, [0xf5, 0x53, 3, 2, 0, 0]);
        var badV3Repeats = CreateState(3, [0xf5, 0x53, 3, 2, 1, 6]);
        var badV4Repeats = CreateState(4, [0xf5, 0x53, 3, 2, 1, 6]);
        var sound = new FakeSound();

        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(badVolume, sound: sound).ExecuteNext());
        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(badV3Repeats, sound: sound).ExecuteNext());
        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(badV4Repeats, sound: sound).ExecuteNext());
    }

    [Fact]
    public void ReportsSoundCapabilitiesAndClearsStoryRequestWhenNoProviderExists()
    {
        var unavailable = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(6)));
        _ = new ZMachineOpcodeExecutor(unavailable);
        Assert.Equal(0, unavailable.ReadHeaderByte(1) & 0x20);

        var available = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(6)));
        _ = new ZMachineOpcodeExecutor(available, sound: new FakeSound());
        Assert.NotEqual(0, available.ReadHeaderByte(1) & 0x20);
    }

    [Fact]
    public void ClearsVersionFiveSoundRequestWhenNoProviderExists()
    {
        var story = MachineStoryFactory.Create(5);
        WriteWord(story, 0x10, (ushort)(ReadWord(story, 0x10) | 0x80));
        var state = new ZMachineState(StoryLoader.Load(story));

        _ = new ZMachineOpcodeExecutor(state);

        Assert.Equal(0, state.ReadHeaderWord(0x10) & 0x80);
    }

    private static ZMachineState CreateState(byte version, byte[] instruction)
    {
        var story = MachineStoryFactory.Create(version);
        instruction.CopyTo(story, 0x200);
        return new ZMachineState(StoryLoader.Load(story));
    }

    private static ushort ReadWord(byte[] data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static void WriteWord(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private sealed class FakeSound : IZMachineSound
    {
        public bool SupportsSoundEffects => true;
        public List<ushort> Prepared { get; } = [];
        public List<ZMachineSoundRequest> Played { get; } = [];
        public List<Action?> Completions { get; } = [];
        public List<ushort?> Stopped { get; } = [];
        public List<ushort?> Finished { get; } = [];

        public void Prepare(ushort soundNumber) => Prepared.Add(soundNumber);

        public void Play(ZMachineSoundRequest request, Action? completed)
        {
            Played.Add(request);
            Completions.Add(completed);
        }

        public void Stop(ushort? soundNumber) => Stopped.Add(soundNumber);

        public void Finish(ushort? soundNumber) => Finished.Add(soundNumber);
    }
}

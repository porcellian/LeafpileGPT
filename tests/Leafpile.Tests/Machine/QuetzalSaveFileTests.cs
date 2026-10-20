using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class QuetzalSaveFileTests
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
    public void CompressedAndUncompressedFilesRoundTripMemoryAndStack(byte version)
    {
        var storyImage = MachineStoryFactory.Create(version);
        storyImage[2] = 0x12;
        storyImage[3] = 0x34;
        Encoding.ASCII.GetBytes("260101").CopyTo(storyImage, 0x12);
        var routine = MachineStoryFactory.WriteRoutine(storyImage, version, 0x400, 2, 0x1111, 0x2222);
        var story = StoryLoader.Load(storyImage);
        var state = new ZMachineState(story);
        state.Memory.WriteByte(0x80, 0xa5);
        state.CallStack.Push(0x4567);
        state.CallStack.CallRoutine(routine, [0xabcd], 0x240, storeVariable: 0x10);
        state.CallStack.SetLocal(2, 0xdef0);
        state.CallStack.Push(0x789a);
        state.SetProgramCounter(0x250);
        if (version <= 3)
        {
            state.Memory.WriteByte(0x230, 0xc2);
        }
        else
        {
            state.Memory.WriteByte(0x230, 0x10);
        }

        var compressed = QuetzalSaveFile.Write(state, 0x230, ["At the gate"], new QuetzalInterpreterData("UNIX", "    ", 1, 0, [1, 2, 3]));
        var uncompressed = QuetzalSaveFile.Write(state, 0x230, useCompressedMemory: false);
        Assert.Contains("CMem", Encoding.ASCII.GetString(compressed));
        Assert.Contains("UMem", Encoding.ASCII.GetString(uncompressed));

        var compressedDocument = QuetzalSaveFile.Read(compressed, story);
        var uncompressedDocument = QuetzalSaveFile.Read(uncompressed, story);
        Assert.Equal(state.Memory.GetDynamicMemory(), compressedDocument.DynamicMemory);
        Assert.Equal(state.Memory.GetDynamicMemory(), uncompressedDocument.DynamicMemory);
        Assert.Equal("At the gate", Assert.Single(compressedDocument.Annotations));
        Assert.Equal([1, 2, 3], compressedDocument.InterpreterData!.Data);
        Assert.Equal(2, compressedDocument.StackFrames.Count);

        var restored = new ZMachineState(story);
        QuetzalSaveFile.Restore(restored, compressedDocument);
        Assert.Equal(0xa5, restored.Memory.ReadByte(0x80));
        Assert.Equal(2, restored.CallStack.FrameCount);
        Assert.Equal(0xdef0, restored.CallStack.GetLocal(2));
        Assert.Equal(0x231, restored.ProgramCounter);
        Assert.Equal(0x789a, restored.CallStack.Pop());
        restored.CallStack.ReturnFromRoutine(0);
        Assert.Equal(0x4567, restored.CallStack.Pop());
        Assert.Equal(0x240, restored.ProgramCounter);
        if (version >= 4)
        {
            Assert.Equal(2, restored.Memory.ReadWord(0x40));
        }
    }

    [Fact]
    public void CompressedMemoryAcceptsShortDiffsAndRejectsIncompleteOrOversizedRuns()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        var valid = BuildSave(story, "CMem", [0, 255]);
        var document = QuetzalSaveFile.Read(valid, story);
        Assert.Equal(0x300, document.DynamicMemory.Length);

        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(BuildSave(story, "CMem", [0]), story));
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(BuildSave(story, "CMem", [0, 255, 0, 255, 0, 255, 0, 255]), story));
    }

    [Fact]
    public void ValidatesIdentityAndUncompressedMemoryLength()
    {
        var storyImage = MachineStoryFactory.Create(5);
        var story = StoryLoader.Load(storyImage);
        var state = new ZMachineState(story);
        var save = QuetzalSaveFile.Write(state, 0x230, useCompressedMemory: false);
        storyImage[3] = 0x77;
        var differentStory = StoryLoader.Load(storyImage);

        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(save, differentStory));
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(BuildSave(story, "UMem", [1, 2]), story));
    }

    [Fact]
    public void RejectsInvalidFormChunkBoundsStackFramesAndPadding()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        var valid = QuetzalSaveFile.Write(new ZMachineState(story), 0x230);

        var badFormLength = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(badFormLength.AsSpan(4), 0xffff_ffff);
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(badFormLength, story));

        var badPadding = valid.ToArray();
        var ifhdPadding = 12 + 8 + 13;
        badPadding[ifhdPadding] = 1;
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(badPadding, story));

        var badStack = BuildSave(story, "CMem", [0, 255], [0, 0, 0, 0xe0, 0, 0, 0, 0]);
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Read(badStack, story));
    }

    [Fact]
    public void RejectsSaveResumeAddressesOutsideStoryAndIgnoresUnknownChunks()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        Assert.Throws<QuetzalSaveFileException>(() => QuetzalSaveFile.Write(new ZMachineState(story), story.Header.FileLength));

        var valid = QuetzalSaveFile.Write(new ZMachineState(story), 0x230);
        var unknownChunk = MakeForm(valid.AsSpan(12).ToArray().Concat(MakeChunk("ZZZZ", [9, 8])).ToArray());
        Assert.Equal(0x300, QuetzalSaveFile.Read(unknownChunk, story).DynamicMemory.Length);
    }

    [Fact]
    public void FailedRestoreLeavesCurrentMachineStateUntouched()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        var savedState = new ZMachineState(story);
        savedState.Memory.WriteByte(0x80, 0xaa);
        var document = QuetzalSaveFile.Read(QuetzalSaveFile.Write(savedState, 0x230), story);
        var malformedDocument = document with { StackFrames = [] };
        var targetState = new ZMachineState(story);
        targetState.Memory.WriteByte(0x80, 0x55);
        targetState.SetProgramCounter(0x240);

        Assert.Throws<ZMachineRuntimeException>(() => QuetzalSaveFile.Restore(targetState, malformedDocument));
        Assert.Equal(0x55, targetState.Memory.ReadByte(0x80));
        Assert.Equal(0x240, targetState.ProgramCounter);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SaveRestoreServiceResumesAtTheSavedSaveInstructionSuccessContinuation(byte version)
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(version));
        var files = new FakeSaveFiles();
        var service = new QuetzalSaveRestoreService(files);
        var savedState = new ZMachineState(story);
        savedState.Memory.WriteByte(0x221, version == 3 ? (byte)0xc2 : (byte)0x10);
        var instruction = new ZMachineInstruction(
            0x220,
            0x222,
            ZMachineInstructionForm.Short,
            ZMachineOpcodeClass.ZeroOperand,
            5,
            0,
            [],
            version == 3 ? null : (byte)0x10,
            version == 3 ? new ZMachineBranch(true, 2) : null,
            null,
            version == 3 ? 0x221 : null);

        var saved = service.Save(savedState, instruction, undo: false);
        Assert.True(saved.Succeeded);
        var document = QuetzalSaveFile.Read(files.Data!, story);
        Assert.Equal(0x221, document.ProgramCounter);

        var restoredState = new ZMachineState(story);
        var restored = service.Restore(restoredState, instruction, undo: false);
        Assert.True(restored.ResumedFromSavedState);
        Assert.Equal(0x222, restoredState.ProgramCounter);
        if (version >= 4)
        {
            Assert.Equal((ushort)2, restoredState.Memory.ReadWord(0x40));
        }
    }

    [Fact]
    public void SaveUndoAndRestoreUndoUseTheSameQuetzalStateFormat()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        var service = new QuetzalSaveRestoreService(new FakeSaveFiles());
        var state = new ZMachineState(story);
        var saveInstruction = new ZMachineInstruction(0x220, 0x222, ZMachineInstructionForm.Extended,
            ZMachineOpcodeClass.Extended, 9, 0, [], 0x10, null, null);
        var restoreInstruction = saveInstruction with { Opcode = 10 };
        state.Memory.WriteByte(0x221, 0x10);

        Assert.True(service.Save(state, saveInstruction, undo: true).Succeeded);
        state.Memory.WriteByte(0x80, 0xff);
        var result = service.Restore(state, restoreInstruction, undo: true);

        Assert.True(result.ResumedFromSavedState);
        Assert.Equal(0, state.Memory.ReadByte(0x80));
        Assert.Equal(0x222, state.ProgramCounter);
    }

    [Fact]
    public void CancelledSaveReportsFailureToTheOpcodeCaller()
    {
        var story = StoryLoader.Load(MachineStoryFactory.Create(5));
        var service = new QuetzalSaveRestoreService(new FakeSaveFiles { CanWrite = false });
        var state = new ZMachineState(story);
        state.Memory.WriteByte(0x221, 0x10);
        var instruction = new ZMachineInstruction(0x220, 0x222, ZMachineInstructionForm.Short,
            ZMachineOpcodeClass.ZeroOperand, 5, 0, [], 0x10, null, null);

        var result = service.Save(state, instruction, undo: false);

        Assert.False(result.Succeeded);
        Assert.Equal((ushort)0, result.ReturnValue);
    }

    [Fact]
    public void V3TwoByteBranchSavesItsFirstDescriptorByteAsTheResumeAddress()
    {
        var storyImage = MachineStoryFactory.Create(3);
        storyImage[0x220] = 0xb5;
        storyImage[0x221] = 0x00;
        storyImage[0x222] = 0x10;
        var story = StoryLoader.Load(storyImage);
        var state = new ZMachineState(story);
        var files = new FakeSaveFiles();
        var service = new QuetzalSaveRestoreService(files);
        var instruction = state.Instructions.Decode(0x220);

        Assert.Equal(0x221, instruction.BranchAddress);
        service.Save(state, instruction, undo: false);

        Assert.Equal(0x221, QuetzalSaveFile.Read(files.Data!, story).ProgramCounter);
    }

    private static byte[] BuildSave(LoadedStory story, string memoryChunkId, byte[] memory, byte[]? stackData = null)
    {
        var ifhd = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(ifhd, story.Header.ReleaseNumber);
        Encoding.ASCII.GetBytes(story.Header.SerialNumber).CopyTo(ifhd, 2);
        BinaryPrimitives.WriteUInt16BigEndian(ifhd.AsSpan(8), story.Header.Checksum);
        ifhd[11] = 2;
        ifhd[12] = 0x30;
        var stack = stackData ?? new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(stack.AsSpan(6), 0);
        return MakeForm(MakeChunk("IFhd", ifhd).Concat(MakeChunk(memoryChunkId, memory)).Concat(MakeChunk("Stks", stack)).ToArray());
    }

    private static byte[] MakeForm(byte[] chunks)
    {
        var form = new byte[12 + chunks.Length];
        "FORM"u8.CopyTo(form);
        BinaryPrimitives.WriteUInt32BigEndian(form.AsSpan(4), checked((uint)(form.Length - 8)));
        "IFZS"u8.CopyTo(form.AsSpan(8));
        chunks.CopyTo(form, 12);
        return form;
    }

    private static byte[] MakeChunk(string id, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), checked((uint)data.Length));
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private sealed class FakeSaveFiles : IZMachineSaveFileAccess
    {
        public byte[]? Data { get; private set; }

        public bool CanWrite { get; init; } = true;

        public bool WriteSaveFile(ReadOnlyMemory<byte> saveFile)
        {
            Data = saveFile.ToArray();
            return CanWrite;
        }

        public ReadOnlyMemory<byte>? ReadSaveFile() => Data;
    }
}

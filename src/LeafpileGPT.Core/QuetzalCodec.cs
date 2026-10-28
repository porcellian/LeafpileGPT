namespace LeafpileGPT.Core;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

public enum QuetzalMemoryEncoding
{
    Compressed,
    Uncompressed
}

public sealed record QuetzalStoryIdentity(
    ushort ReleaseNumber,
    ImmutableArray<byte> SerialNumber,
    ushort Checksum,
    uint ProgramCounter,
    ImmutableArray<byte> ExtensionData)
{
    public const int SerialNumberLength = 6;
    public const int ProgramCounterLimit = 0x0100_0000;

    public static QuetzalStoryIdentity FromStory(StoryFile story, uint programCounter)
    {
        ArgumentNullException.ThrowIfNull(story);
        var serial = Encoding.Latin1.GetBytes(story.Header.SerialNumber).ToImmutableArray();
        return new QuetzalStoryIdentity(story.Header.ReleaseNumber, serial,
            CalculateStoryChecksum(story.Bytes.Span), programCounter, ImmutableArray<byte>.Empty);
    }

    public bool MatchesStory(StoryFile story)
    {
        ArgumentNullException.ThrowIfNull(story);
        return ReleaseNumber == story.Header.ReleaseNumber
            && SerialNumber.AsSpan().SequenceEqual(Encoding.Latin1.GetBytes(story.Header.SerialNumber))
            && Checksum == CalculateStoryChecksum(story.Bytes.Span);
    }

    private static ushort CalculateStoryChecksum(ReadOnlySpan<byte> story)
    {
        uint sum = 0;
        for (var index = StoryFile.HeaderLength; index < story.Length; index++)
        {
            sum = (sum + story[index]) & ushort.MaxValue;
        }

        return (ushort)sum;
    }
}

/// <summary>A serialized Quetzal frame, with words in oldest-first order.</summary>
public sealed record QuetzalStackFrame(
    uint ReturnProgramCounter,
    bool DiscardsResult,
    byte StoreVariable,
    byte ArgumentMask,
    ImmutableArray<ushort> Locals,
    ImmutableArray<ushort> EvaluationStack,
    bool IsDummy = false);

/// <summary>An additional IFZS chunk, including chunks unknown to this implementation.</summary>
public sealed record QuetzalChunk(string Type, ImmutableArray<byte> Data);

/// <summary>Optional interpreter-dependent IFZS IntD information.</summary>
public sealed record QuetzalInterpreterData(
    string OperatingSystemId,
    bool SameMachineOnly,
    bool CurrentStateOnly,
    byte ContentsId,
    string InterpreterId,
    ImmutableArray<byte> Data);

/// <summary>An immutable decoded Quetzal image, ready for a caller to apply to a VM.</summary>
public sealed record QuetzalSaveData(
    byte Version,
    QuetzalStoryIdentity Identity,
    ImmutableArray<byte> DynamicMemory,
    ImmutableArray<QuetzalStackFrame> StackFrames,
    ImmutableArray<QuetzalChunk> AdditionalChunks)
{
    public ImmutableArray<QuetzalChunk> Annotations => AdditionalChunks
        .Where(chunk => chunk.Type is "AUTH" or "(c) " or "ANNO")
        .ToImmutableArray();

    public ImmutableArray<QuetzalInterpreterData> InterpreterData => AdditionalChunks
        .Where(chunk => chunk.Type == "IntD")
        .Select(chunk => QuetzalCodec.ParseInterpreterData(chunk.Data.AsSpan()))
        .ToImmutableArray();
}

/// <summary>Reads and writes Quetzal IFZS save images.</summary>
public static class QuetzalCodec
{
    private static readonly ImmutableHashSet<string> CoreChunkTypes =
        ImmutableHashSet.Create(StringComparer.Ordinal, "IFhd", "CMem", "UMem", "Stks");

    public static byte[] Encode(
        QuetzalSaveData save,
        StoryFile story,
        QuetzalMemoryEncoding memoryEncoding = QuetzalMemoryEncoding.Compressed)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(story);
        if (memoryEncoding is not (QuetzalMemoryEncoding.Compressed or QuetzalMemoryEncoding.Uncompressed))
        {
            throw new ArgumentOutOfRangeException(nameof(memoryEncoding), memoryEncoding, "Unknown Quetzal memory encoding.");
        }

        ValidateSaveData(save, story);

        var chunks = new List<QuetzalChunk>
        {
            new("IFhd", EncodeIdentity(save.Identity).ToImmutableArray()),
            new(memoryEncoding == QuetzalMemoryEncoding.Compressed ? "CMem" : "UMem",
                (memoryEncoding == QuetzalMemoryEncoding.Compressed
                    ? EncodeCompressedMemory(save.DynamicMemory.AsSpan(), story.Bytes.Span[..story.Header.StaticMemoryBase])
                    : save.DynamicMemory.ToArray()).ToImmutableArray()),
            new("Stks", EncodeStackFrames(save.Version, save.StackFrames).ToImmutableArray())
        };

        foreach (var chunk in save.AdditionalChunks)
        {
            ValidateChunk(chunk);
            if (CoreChunkTypes.Contains(chunk.Type))
            {
                throw new QuetzalFormatException($"Additional chunk '{chunk.Type}' duplicates a required Quetzal chunk.");
            }

            if (chunk.Type == "IntD")
            {
                _ = ParseInterpreterData(chunk.Data.AsSpan());
            }

            if (chunk.Type is "AUTH" or "(c) " or "ANNO")
            {
                ValidateAsciiAnnotation(chunk);
            }

            chunks.Add(chunk);
        }

        return WriteForm(chunks);
    }

    public static QuetzalSaveData Decode(ReadOnlySpan<byte> bytes, StoryFile story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var chunks = ParseForm(bytes);
        var identityChunk = FirstRequired(chunks, "IFhd");
        var memoryChunk = chunks.FirstOrDefault(chunk => chunk.Type == "CMem")
            ?? chunks.FirstOrDefault(chunk => chunk.Type == "UMem")
            ?? throw new QuetzalFormatException("IFZS is missing both required CMem and UMem chunks.");
        var stackChunk = FirstRequired(chunks, "Stks");

        var identityIndex = chunks.IndexOf(identityChunk);
        var firstMemoryIndex = chunks.IndexOf(chunks.First(chunk => chunk.Type is "CMem" or "UMem"));
        if (identityIndex > firstMemoryIndex || identityIndex > chunks.IndexOf(stackChunk))
        {
            throw new QuetzalFormatException("IFhd must appear before the dynamic-memory and Stks chunks.");
        }

        var identity = DecodeIdentity(identityChunk.Data.AsSpan());
        if (!identity.MatchesStory(story))
        {
            throw new QuetzalStoryMismatchException("The Quetzal save file belongs to a different story (release, serial, or checksum mismatch).");
        }

        if (identity.ProgramCounter >= story.Length)
        {
            throw new QuetzalFormatException($"IFhd program counter 0x{identity.ProgramCounter:X} lies outside the story image.");
        }

        var dynamicMemoryLength = story.Header.StaticMemoryBase;
        var originalDynamicMemory = story.Bytes.Span[..dynamicMemoryLength];
        var dynamicMemory = memoryChunk.Type == "CMem"
            ? DecodeCompressedMemory(memoryChunk.Data.AsSpan(), originalDynamicMemory)
            : DecodeUncompressedMemory(memoryChunk.Data.AsSpan(), dynamicMemoryLength);
        var frames = DecodeStackFrames(stackChunk.Data.AsSpan(), story.Header.Version);
        ValidateFrameProgramCounters(frames, story.Length);
        var additionalChunks = chunks.Where(chunk => !CoreChunkTypes.Contains(chunk.Type)).ToImmutableArray();
        foreach (var chunk in additionalChunks)
        {
            if (chunk.Type == "IntD")
            {
                _ = ParseInterpreterData(chunk.Data.AsSpan());
            }

            if (chunk.Type is "AUTH" or "(c) " or "ANNO")
            {
                ValidateAsciiAnnotation(chunk);
            }
        }

        return new QuetzalSaveData(story.Header.Version, identity, dynamicMemory.ToImmutableArray(), frames, additionalChunks);
    }

    public static byte[] EncodeCompressedMemory(ReadOnlySpan<byte> dynamicMemory, ReadOnlySpan<byte> originalMemory)
    {
        if (dynamicMemory.Length != originalMemory.Length)
        {
            throw new ArgumentException("Dynamic memory and original story memory must have equal lengths.", nameof(originalMemory));
        }

        using var output = new MemoryStream();
        var index = 0;
        while (index < dynamicMemory.Length)
        {
            var difference = (byte)(dynamicMemory[index] ^ originalMemory[index]);
            if (difference != 0)
            {
                output.WriteByte(difference);
                index++;
                continue;
            }

            var runLength = 1;
            while (runLength < 256 && index + runLength < dynamicMemory.Length
                && dynamicMemory[index + runLength] == originalMemory[index + runLength])
            {
                runLength++;
            }

            output.WriteByte(0);
            output.WriteByte(checked((byte)(runLength - 1)));
            index += runLength;
        }

        return output.ToArray();
    }

    public static byte[] DecodeCompressedMemory(ReadOnlySpan<byte> encoded, ReadOnlySpan<byte> originalMemory)
    {
        var decoded = new byte[originalMemory.Length];
        var inputOffset = 0;
        var outputOffset = 0;
        while (inputOffset < encoded.Length)
        {
            var value = encoded[inputOffset++];
            if (value == 0)
            {
                if (inputOffset >= encoded.Length)
                {
                    throw new QuetzalFormatException("CMem ends with an incomplete zero run.");
                }

                var runLength = encoded[inputOffset++] + 1;
                if (outputOffset + runLength > decoded.Length)
                {
                    throw new QuetzalFormatException("CMem expands beyond the story's dynamic-memory length.");
                }

                originalMemory.Slice(outputOffset, runLength).CopyTo(decoded.AsSpan(outputOffset));
                outputOffset += runLength;
                continue;
            }

            if (outputOffset >= decoded.Length)
            {
                throw new QuetzalFormatException("CMem expands beyond the story's dynamic-memory length.");
            }

            decoded[outputOffset] = (byte)(value ^ originalMemory[outputOffset]);
            outputOffset++;
        }

        if (outputOffset < decoded.Length)
        {
            originalMemory[outputOffset..].CopyTo(decoded.AsSpan(outputOffset));
        }

        return decoded;
    }

    public static byte[] DecodeUncompressedMemory(ReadOnlySpan<byte> encoded, int expectedLength)
    {
        if (encoded.Length != expectedLength)
        {
            throw new QuetzalFormatException(
                $"UMem contains {encoded.Length} bytes; exactly {expectedLength} dynamic-memory bytes are required.");
        }

        return encoded.ToArray();
    }

    public static byte[] EncodeStackFrames(byte version, ImmutableArray<QuetzalStackFrame> frames)
    {
        ValidateStackFrames(version, frames);
        using var output = new MemoryStream();
        foreach (var frame in frames)
        {
            WriteUInt24(output, frame.ReturnProgramCounter);
            var flags = (byte)((frame.DiscardsResult ? 0x10 : 0) | frame.Locals.Length);
            output.WriteByte(flags);
            output.WriteByte(frame.StoreVariable);
            output.WriteByte(frame.ArgumentMask);
            WriteWord(output, checked((ushort)frame.EvaluationStack.Length));
            foreach (var local in frame.Locals)
            {
                WriteWord(output, local);
            }

            foreach (var value in frame.EvaluationStack)
            {
                WriteWord(output, value);
            }
        }

        return output.ToArray();
    }

    public static ImmutableArray<QuetzalStackFrame> DecodeStackFrames(ReadOnlySpan<byte> encoded, byte version)
    {
        if (version is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var frames = ImmutableArray.CreateBuilder<QuetzalStackFrame>();
        var offset = 0;
        while (offset < encoded.Length)
        {
            if (encoded.Length - offset < 8)
            {
                throw new QuetzalFormatException($"Stks frame at offset {offset} is shorter than its 8-byte header.");
            }

            var returnPc = ReadUInt24(encoded, offset);
            offset += 3;
            var flags = encoded[offset++];
            if ((flags & 0xE0) != 0)
            {
                throw new QuetzalFormatException($"Stks frame at offset {offset - 1} sets reserved flag bits.");
            }

            var storeVariable = encoded[offset++];
            var argumentMask = encoded[offset++];
            if ((argumentMask & 0x80) != 0)
            {
                throw new QuetzalFormatException($"Stks frame at offset {offset - 1} sets the reserved argument bit.");
            }

            var evaluationCount = ReadWord(encoded, offset);
            offset += sizeof(ushort);
            var localCount = flags & 0x0F;
            var wordCount = checked(localCount + evaluationCount);
            var dataBytes = checked(wordCount * sizeof(ushort));
            if (encoded.Length - offset < dataBytes)
            {
                throw new QuetzalFormatException($"Stks frame at offset {offset - 8} has truncated local or evaluation words.");
            }

            var locals = ImmutableArray.CreateBuilder<ushort>(localCount);
            for (var index = 0; index < localCount; index++)
            {
                locals.Add(ReadWord(encoded, offset));
                offset += sizeof(ushort);
            }

            var evaluation = ImmutableArray.CreateBuilder<ushort>(evaluationCount);
            for (var index = 0; index < evaluationCount; index++)
            {
                evaluation.Add(ReadWord(encoded, offset));
                offset += sizeof(ushort);
            }

            frames.Add(new QuetzalStackFrame(returnPc, (flags & 0x10) != 0, storeVariable,
                argumentMask, locals.ToImmutable(), evaluation.ToImmutable()));
        }

        if (frames.Count == 0)
        {
            throw new QuetzalFormatException("Stks must contain at least one frame.");
        }

        var immutableFrames = frames.ToImmutable();
        return ValidateDummyFrame(immutableFrames, version);
    }

    public static QuetzalChunk CreateInterpreterData(QuetzalInterpreterData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Data.IsDefault)
        {
            throw new ArgumentException("IntD data must be an initialized immutable array.", nameof(data));
        }

        ValidateFourCc(data.OperatingSystemId, nameof(data.OperatingSystemId));
        ValidateFourCc(data.InterpreterId, nameof(data.InterpreterId));
        if (data.OperatingSystemId == "    " && data.InterpreterId == "    ")
        {
            throw new ArgumentException("IntD operating-system and interpreter IDs cannot both be blank.", nameof(data));
        }

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes(data.OperatingSystemId));
        output.WriteByte((byte)((data.SameMachineOnly ? 0x02 : 0) | (data.CurrentStateOnly ? 0x01 : 0)));
        output.WriteByte(data.ContentsId);
        WriteWord(output, 0);
        output.Write(Encoding.ASCII.GetBytes(data.InterpreterId));
        output.Write(data.Data.AsSpan());
        return new QuetzalChunk("IntD", output.ToArray().ToImmutableArray());
    }

    internal static QuetzalInterpreterData ParseInterpreterData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            throw new QuetzalFormatException($"IntD contains {data.Length} bytes; at least 12 are required.");
        }

        var operatingSystem = ReadFourCc(data, 0);
        var flags = data[4];
        var contentsId = data[5];
        var reserved = ReadWord(data, 6);
        var interpreter = ReadFourCc(data, 8);
        ValidateFourCc(operatingSystem, "IntD operating-system ID");
        ValidateFourCc(interpreter, "IntD interpreter ID");
        if ((flags & 0xFC) != 0 || reserved != 0)
        {
            throw new QuetzalFormatException("IntD sets reserved flag bits or a non-zero reserved word.");
        }

        if (operatingSystem == "    " && interpreter == "    ")
        {
            throw new QuetzalFormatException("IntD operating-system and interpreter IDs cannot both be blank.");
        }

        return new QuetzalInterpreterData(operatingSystem, (flags & 0x02) != 0, (flags & 0x01) != 0,
            contentsId, interpreter, data[12..].ToArray().ToImmutableArray());
    }

    private static byte[] EncodeIdentity(QuetzalStoryIdentity identity)
    {
        ValidateIdentity(identity);
        using var output = new MemoryStream();
        WriteWord(output, identity.ReleaseNumber);
        output.Write(identity.SerialNumber.AsSpan());
        WriteWord(output, identity.Checksum);
        WriteUInt24(output, identity.ProgramCounter);
        output.Write(identity.ExtensionData.AsSpan());
        return output.ToArray();
    }

    private static QuetzalStoryIdentity DecodeIdentity(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
        {
            throw new QuetzalFormatException($"IFhd contains {data.Length} bytes; at least 13 are required.");
        }

        return new QuetzalStoryIdentity(ReadWord(data, 0), data.Slice(2, 6).ToArray().ToImmutableArray(),
            ReadWord(data, 8), ReadUInt24(data, 10), data[13..].ToArray().ToImmutableArray());
    }

    private static void ValidateIdentity(QuetzalStoryIdentity identity)
    {
        if (identity.SerialNumber.IsDefault || identity.SerialNumber.Length != QuetzalStoryIdentity.SerialNumberLength)
        {
            throw new QuetzalFormatException("IFhd story serial number must contain exactly six bytes.");
        }

        if (identity.ProgramCounter >= QuetzalStoryIdentity.ProgramCounterLimit)
        {
            throw new QuetzalFormatException("IFhd program counter does not fit its 3-byte field.");
        }

        if (identity.ExtensionData.IsDefault)
        {
            throw new QuetzalFormatException("IFhd extension data must be an initialized immutable array.");
        }
    }

    private static void ValidateSaveData(QuetzalSaveData save, StoryFile story)
    {
        if (save.Version != story.Header.Version)
        {
            throw new QuetzalStoryMismatchException("Save data and story use different Z-machine versions.");
        }

        ValidateIdentity(save.Identity);
        if (!save.Identity.MatchesStory(story))
        {
            throw new QuetzalStoryMismatchException("Save data belongs to a different story.");
        }

        if (save.Identity.ProgramCounter >= story.Length)
        {
            throw new QuetzalFormatException($"IFhd program counter 0x{save.Identity.ProgramCounter:X} lies outside the story image.");
        }

        if (save.DynamicMemory.IsDefault || save.DynamicMemory.Length != story.Header.StaticMemoryBase)
        {
            throw new QuetzalFormatException(
                $"Dynamic memory must contain exactly {story.Header.StaticMemoryBase} bytes.");
        }

        if (save.AdditionalChunks.IsDefault)
        {
            throw new QuetzalFormatException("Additional chunks must be an initialized immutable array.");
        }

        ValidateStackFrames(save.Version, save.StackFrames);
        ValidateFrameProgramCounters(save.StackFrames, story.Length);
    }

    private static void ValidateFrameProgramCounters(ImmutableArray<QuetzalStackFrame> frames, int storyLength)
    {
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index].ReturnProgramCounter >= storyLength)
            {
                throw new QuetzalFormatException(
                    $"Stks frame {index} return PC 0x{frames[index].ReturnProgramCounter:X} lies outside the story image.");
            }
        }
    }

    private static void ValidateStackFrames(byte version, ImmutableArray<QuetzalStackFrame> frames)
    {
        if (version is < 1 or > 8)
        {
            throw new QuetzalFormatException($"Unsupported Z-machine version {version}.");
        }

        if (frames.IsDefaultOrEmpty)
        {
            throw new QuetzalFormatException("Stks must contain at least one frame.");
        }

        if (version == 6)
        {
            if (frames.Any(frame => frame.IsDummy))
            {
                throw new QuetzalFormatException("V6 Stks must not contain a non-V6 dummy frame.");
            }
        }
        else if (!frames[0].IsDummy || frames.Skip(1).Any(frame => frame.IsDummy))
        {
            throw new QuetzalFormatException("Non-V6 Stks must begin with exactly one dummy frame.");
        }

        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            if (frame.ReturnProgramCounter >= QuetzalStoryIdentity.ProgramCounterLimit)
            {
                throw new QuetzalFormatException($"Stks frame {index} return PC does not fit its 3-byte field.");
            }

            if (frame.Locals.IsDefault || frame.Locals.Length > 15)
            {
                throw new QuetzalFormatException($"Stks frame {index} must have an initialized local array of at most 15 words.");
            }

            if (frame.ArgumentMask > 0x7F)
            {
                throw new QuetzalFormatException($"Stks frame {index} argument mask sets its reserved high bit.");
            }

            if (frame.DiscardsResult && frame.StoreVariable != 0)
            {
                throw new QuetzalFormatException($"Stks frame {index} discards its result but has a non-zero store variable.");
            }

            if (frame.EvaluationStack.IsDefault || frame.EvaluationStack.Length > ushort.MaxValue)
            {
                throw new QuetzalFormatException($"Stks frame {index} evaluation stack must fit an initialized 16-bit word count.");
            }

            if (frame.IsDummy && (index != 0 || frame.ReturnProgramCounter != 0 || frame.DiscardsResult
                || frame.StoreVariable != 0 || frame.ArgumentMask != 0 || !frame.Locals.IsEmpty))
            {
                throw new QuetzalFormatException("The non-V6 dummy frame must have zeroed call fields and no locals.");
            }
        }
    }

    private static ImmutableArray<QuetzalStackFrame> ValidateDummyFrame(ImmutableArray<QuetzalStackFrame> frames, byte version)
    {
        var first = frames[0];
        var isDummy = version != 6;
        var withDummy = frames.SetItem(0, first with { IsDummy = isDummy });
        ValidateStackFrames(version, withDummy);
        return withDummy;
    }

    private static ImmutableArray<QuetzalChunk> ParseForm(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            throw new QuetzalFormatException("IFZS FORM header is truncated; at least 12 bytes are required.");
        }

        if (ReadFourCc(bytes, 0) != "FORM" || ReadFourCc(bytes, 8) != "IFZS")
        {
            throw new QuetzalFormatException("Save file must be an IFF FORM IFZS image.");
        }

        var formLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        if ((ulong)formLength + 8 != (ulong)bytes.Length || formLength < 4)
        {
            throw new QuetzalFormatException("FORM length does not match the supplied IFZS bytes.");
        }

        var chunks = ImmutableArray.CreateBuilder<QuetzalChunk>();
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new QuetzalFormatException($"IFF chunk header at offset {offset} is truncated.");
            }

            var type = ReadFourCc(bytes, offset);
            ValidateFourCc(type, $"chunk at offset {offset}");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 4)..]);
            var dataStart = checked(offset + 8);
            var dataEnd = (ulong)dataStart + length;
            var paddedEnd = dataEnd + (length & 1);
            if (paddedEnd > (ulong)bytes.Length)
            {
                throw new QuetzalFormatException($"IFF chunk '{type}' at offset {offset} exceeds the FORM boundary.");
            }

            if ((length & 1) != 0 && bytes[checked((int)dataEnd)] != 0)
            {
                throw new QuetzalFormatException($"IFF chunk '{type}' at offset {offset} has a non-zero padding byte.");
            }

            chunks.Add(new QuetzalChunk(type, bytes.Slice(dataStart, checked((int)length)).ToArray().ToImmutableArray()));
            offset = checked((int)paddedEnd);
        }

        return chunks.ToImmutable();
    }

    private static byte[] WriteForm(IEnumerable<QuetzalChunk> chunks)
    {
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("IFZS"));
        foreach (var chunk in chunks)
        {
            ValidateChunk(chunk);
            WriteFourCc(body, chunk.Type);
            WriteUInt32(body, checked((uint)chunk.Data.Length));
            body.Write(chunk.Data.AsSpan());
            if ((chunk.Data.Length & 1) != 0)
            {
                body.WriteByte(0);
            }
        }

        var formData = body.ToArray();
        using var file = new MemoryStream(checked(formData.Length + 8));
        WriteFourCc(file, "FORM");
        WriteUInt32(file, checked((uint)formData.Length));
        file.Write(formData);
        return file.ToArray();
    }

    private static QuetzalChunk FirstRequired(ImmutableArray<QuetzalChunk> chunks, string type) =>
        chunks.FirstOrDefault(chunk => chunk.Type == type)
        ?? throw new QuetzalFormatException($"IFZS is missing required {type} chunk.");

    private static void ValidateChunk(QuetzalChunk chunk)
    {
        if (chunk.Data.IsDefault)
        {
            throw new QuetzalFormatException($"Chunk '{chunk.Type}' data must be an initialized immutable array.");
        }

        ValidateFourCc(chunk.Type, "chunk ID");
    }

    private static void ValidateAsciiAnnotation(QuetzalChunk chunk)
    {
        if (chunk.Data.Any(value => value is < 0x20 or > 0x7E))
        {
            throw new QuetzalFormatException($"{chunk.Type} annotations must contain printable ASCII bytes only.");
        }
    }

    private static void ValidateFourCc(string value, string context)
    {
        if (value.Length != 4 || value.Any(character => character is < ' ' or > '~'))
        {
            throw new QuetzalFormatException($"{context} must be a four-character printable ASCII identifier.");
        }

        var firstSpace = value.IndexOf(' ');
        if (firstSpace >= 0 && value.AsSpan(firstSpace).IndexOfAnyExcept(' ') >= 0)
        {
            throw new QuetzalFormatException($"{context} has a space before a later non-space character.");
        }
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset) => Encoding.ASCII.GetString(data.Slice(offset, 4));

    private static void WriteFourCc(Stream output, string value) => output.Write(Encoding.ASCII.GetBytes(value));

    private static ushort ReadWord(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, sizeof(ushort)));

    private static void WriteWord(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static uint ReadUInt24(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];

    private static void WriteUInt24(Stream output, uint value)
    {
        if (value >= QuetzalStoryIdentity.ProgramCounterLimit)
        {
            throw new QuetzalFormatException($"Program counter 0x{value:X} does not fit its 3-byte field.");
        }

        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}

public sealed class QuetzalFormatException : Exception
{
    public QuetzalFormatException(string message) : base(message)
    {
    }
}

public sealed class QuetzalStoryMismatchException : Exception
{
    public QuetzalStoryMismatchException(string message) : base(message)
    {
    }
}

using System.Buffers.Binary;
using System.Text;

namespace Leafpile.Core.Loading;

public static class StoryLoader
{
    private const int BlorbHeaderLength = 12;

    public static LoadedStory LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException exception)
        {
            throw new StoryLoadException($"Could not read story file '{path}': {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StoryLoadException($"Could not read story file '{path}': {exception.Message}", exception);
        }

        return Load(data);
    }

    public static LoadedStory Load(ReadOnlyMemory<byte> input)
    {
        if (input.Length < 1)
        {
            throw new StoryLoadException("The story file is empty.");
        }

        var file = input.ToArray();
        if (file.Length >= 4 && HasFourCc(file, 0, "FORM"))
        {
            var story = ReadBlorbExecutable(file);
            return CreateStory(story, StorySourceKind.BlorbExecutable);
        }

        return CreateStory(file, StorySourceKind.ZCodeFile);
    }

    private static LoadedStory CreateStory(byte[] storyBytes, StorySourceKind sourceKind)
    {
        var header = StoryHeader.Parse(storyBytes);
        if (header.FileLength != storyBytes.Length)
        {
            Array.Resize(ref storyBytes, header.FileLength);
        }

        return new LoadedStory(storyBytes, header, sourceKind);
    }

    private static byte[] ReadBlorbExecutable(byte[] file)
    {
        if (file.Length < BlorbHeaderLength)
        {
            throw new StoryLoadException("The Blorb file is shorter than its 12-byte FORM header.");
        }

        if (!HasFourCc(file, 8, "IFRS"))
        {
            var formType = Encoding.ASCII.GetString(file, 8, 4);
            throw new StoryLoadException($"Unsupported FORM type '{formType}'; expected an IFRS Blorb file.");
        }

        var formLength = ReadUInt32(file, 4);
        var formEndLong = 8L + formLength;
        if (formLength < 4 || formEndLong != file.Length)
        {
            throw new StoryLoadException($"Invalid Blorb FORM length {formLength}; the FORM must end at the end of the file.");
        }

        var chunks = new Dictionary<int, BlorbChunk>();
        var cursor = BlorbHeaderLength;
        var chunkNumber = 0;
        var resourceIndex = (BlorbChunk?)null;

        while (cursor < formEndLong)
        {
            if (formEndLong - cursor < 8)
            {
                throw new StoryLoadException($"Blorb chunk header at offset 0x{cursor:x} is truncated.");
            }

            var chunkId = Encoding.ASCII.GetString(file, cursor, 4);
            var chunkLength = ReadUInt32(file, cursor + 4);
            var dataStartLong = cursor + 8L;
            var chunkEndLong = dataStartLong + chunkLength;
            var paddedEndLong = chunkEndLong + (chunkLength & 1);
            if (chunkEndLong > formEndLong || paddedEndLong > formEndLong)
            {
                throw new StoryLoadException($"Blorb chunk '{chunkId}' at offset 0x{cursor:x} extends beyond the IFRS FORM.");
            }

            if ((chunkLength & 1) != 0 && file[(int)chunkEndLong] != 0)
            {
                throw new StoryLoadException($"Blorb chunk '{chunkId}' at offset 0x{cursor:x} has a nonzero IFF pad byte.");
            }

            var chunk = new BlorbChunk(chunkId, (int)cursor, (int)dataStartLong, (int)chunkLength);
            chunks.Add(chunk.Offset, chunk);

            if (chunkId == "RIdx")
            {
                if (resourceIndex is not null)
                {
                    throw new StoryLoadException("The Blorb file contains more than one RIdx resource index.");
                }

                if (chunkNumber != 0)
                {
                    throw new StoryLoadException("The RIdx resource index must be the first chunk in a Blorb file.");
                }

                resourceIndex = chunk;
            }

            cursor = (int)paddedEndLong;
            chunkNumber++;
        }

        if (cursor != formEndLong)
        {
            throw new StoryLoadException("The Blorb chunks do not end at the declared FORM boundary.");
        }

        if (resourceIndex is null)
        {
            throw new StoryLoadException("The Blorb file has no RIdx resource index.");
        }

        var resourceEntries = ReadResourceIndex(file, resourceIndex);
        foreach (var entry in resourceEntries)
        {
            if (!chunks.ContainsKey(entry.Offset))
            {
                throw new StoryLoadException($"Blorb resource '{entry.Usage}' number {entry.Number} refers to no chunk at offset 0x{entry.Offset:x}.");
            }
        }

        var executableEntries = resourceEntries
            .Where(entry => entry.Usage == "Exec")
            .ToArray();

        if (executableEntries.Length == 0)
        {
            throw new StoryLoadException("The Blorb file contains no embedded executable story.");
        }

        if (executableEntries.Length != 1)
        {
            throw new StoryLoadException("The Blorb file contains multiple executable entries; one embedded story is required.");
        }

        var executable = executableEntries[0];
        if (executable.Number != 0)
        {
            throw new StoryLoadException($"The Blorb executable has resource number {executable.Number}; Z-machine executable resources must use number 0.");
        }

        var executableChunk = chunks[executable.Offset];
        if (executableChunk.Id != "ZCOD")
        {
            throw new StoryLoadException($"The Blorb executable chunk is '{executableChunk.Id}', not ZCOD Z-code.");
        }

        return file.AsSpan(executableChunk.DataOffset, executableChunk.Length).ToArray();
    }

    private static IReadOnlyList<BlorbResourceEntry> ReadResourceIndex(byte[] file, BlorbChunk chunk)
    {
        if (chunk.Length < 4)
        {
            throw new StoryLoadException("The Blorb RIdx chunk is too short to contain a resource count.");
        }

        var data = file.AsSpan(chunk.DataOffset, chunk.Length);
        var count = ReadUInt32(data, 0);
        var expectedLength = 4L + (count * 12L);
        if (expectedLength != chunk.Length)
        {
            throw new StoryLoadException($"The Blorb RIdx chunk length is {chunk.Length}, but its {count} entries require {expectedLength} bytes.");
        }

        var entries = new BlorbResourceEntry[count];
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 4 + (index * 12);
            var usage = Encoding.ASCII.GetString(data.Slice(entryOffset, 4));
            var number = ReadUInt32(data, entryOffset + 4);
            var offset = ReadUInt32(data, entryOffset + 8);
            if (offset > int.MaxValue)
            {
                throw new StoryLoadException($"Blorb resource '{usage}' number {number} has an out-of-range chunk offset.");
            }

            entries[index] = new BlorbResourceEntry(usage, number, (int)offset);
        }

        return entries;
    }

    private static bool HasFourCc(byte[] data, int offset, string value) =>
        data.AsSpan(offset, value.Length).SequenceEqual(Encoding.ASCII.GetBytes(value));

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private sealed record BlorbChunk(string Id, int Offset, int DataOffset, int Length);

    private sealed record BlorbResourceEntry(string Usage, uint Number, int Offset);
}

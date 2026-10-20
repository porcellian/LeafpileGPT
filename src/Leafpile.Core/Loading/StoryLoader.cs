namespace Leafpile.Core.Loading;

public static class StoryLoader
{
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
        if (file.Length >= 4 && file.AsSpan(0, 4).SequenceEqual("FORM"u8))
        {
            var blorb = BlorbFile.Parse(file);
            var executable = blorb.SelectZCodeExecutable();
            var story = executable.Chunk.Data.ToArray();
            blorb.ValidateZCodeIdentity(story);
            return CreateStory(story, StorySourceKind.BlorbExecutable, blorb);
        }

        return CreateStory(file, StorySourceKind.ZCodeFile);
    }

    private static LoadedStory CreateStory(byte[] storyBytes, StorySourceKind sourceKind, BlorbFile? blorb = null)
    {
        var header = StoryHeader.Parse(storyBytes);
        if (header.FileLength != storyBytes.Length)
        {
            Array.Resize(ref storyBytes, header.FileLength);
        }

        return new LoadedStory(storyBytes, header, sourceKind, blorb);
    }

}

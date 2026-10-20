namespace Leafpile.Core.Loading;

public sealed class LoadedStory
{
    internal LoadedStory(byte[] data, StoryHeader header, StorySourceKind sourceKind, BlorbFile? blorb = null)
    {
        Data = data;
        Header = header;
        SourceKind = sourceKind;
        Blorb = blorb;
    }

    public ReadOnlyMemory<byte> Data { get; }

    public StoryHeader Header { get; }

    public StorySourceKind SourceKind { get; }

    /// <summary>Gets the parsed resource collection when the story was loaded from Blorb.</summary>
    public BlorbFile? Blorb { get; }
}

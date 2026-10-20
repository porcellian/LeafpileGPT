namespace Leafpile.Core.Loading;

public sealed class LoadedStory
{
    internal LoadedStory(byte[] data, StoryHeader header, StorySourceKind sourceKind)
    {
        Data = data;
        Header = header;
        SourceKind = sourceKind;
    }

    public ReadOnlyMemory<byte> Data { get; }

    public StoryHeader Header { get; }

    public StorySourceKind SourceKind { get; }
}

namespace Leafpile.Core.Loading;

public sealed class StoryLoadException : Exception
{
    public StoryLoadException(string message)
        : base(message)
    {
    }

    public StoryLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace LeafpileGPT.Desktop;

/// <summary>Resolves the optional story path passed to the desktop application.</summary>
public static class DesktopLaunchArguments
{
    /// <summary>
    /// Returns null when no story was supplied. A shell normally passes a quoted path as one
    /// argument; joining multiple arguments also permits an unquoted path containing spaces.
    /// </summary>
    public static string? ResolveStoryPath(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return null;
        }

        if (arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The story path cannot contain an empty argument.", nameof(arguments));
        }

        return Path.GetFullPath(string.Join(' ', arguments));
    }
}

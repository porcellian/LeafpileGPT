namespace LeafpileGPT.Desktop;

/// <summary>Character-cell dimensions measured from the desktop play area's rendered viewport.</summary>
public readonly record struct DesktopScreenMetrics(int WidthInCharacters, int VisibleRows)
{
    public static DesktopScreenMetrics FromViewport(
        double width,
        double height,
        double characterWidth,
        double lineHeight)
    {
        var columns = characterWidth > 0 && double.IsFinite(width)
            ? (int)Math.Floor(Math.Max(0, width) / characterWidth)
            : 1;
        var rows = lineHeight > 0 && double.IsFinite(height)
            ? (int)Math.Floor(Math.Max(0, height) / lineHeight)
            : 1;

        return new DesktopScreenMetrics(Math.Clamp(columns, 1, byte.MaxValue), Math.Clamp(rows, 1, byte.MaxValue));
    }

    public (int HeightInLines, int MorePageLineLimit) ForStoryVersion(int version)
    {
        var screenHeight = Math.Min(byte.MaxValue, VisibleRows + (version == 3 ? 1 : 0));
        var pageLimit = Math.Max(1, VisibleRows - 1);
        return (screenHeight, pageLimit);
    }
}

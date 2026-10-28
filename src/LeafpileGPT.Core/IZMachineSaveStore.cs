namespace LeafpileGPT.Core;

/// <summary>Host storage boundary for a story's Quetzal save file.</summary>
public interface IZMachineSaveStore
{
    /// <summary>Writes a complete IFZS image. Returns false when the user cancels or saving fails gracefully.</summary>
    bool Save(ReadOnlyMemory<byte> ifzsImage, ZMachineSaveOptions options);

    /// <summary>Reads a complete IFZS image, or returns null when no save was selected or found.</summary>
    byte[]? Restore(ZMachineSaveOptions options);
}

/// <summary>Optional V5+ save-area and naming parameters supplied by the game.</summary>
public sealed record ZMachineSaveOptions(
    ushort? TableAddress = null,
    ushort? ByteCount = null,
    string? SuggestedName = null,
    bool? Prompt = null);

using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>A renderer-neutral picture draw or erase request produced by a Version 6 story.</summary>
public sealed record ZMachinePictureOperation(
    bool Erase,
    int Window,
    int X,
    int Y,
    BlorbPicture Picture,
    BlorbPictureSize DisplaySize,
    IReadOnlyList<BlorbColor> PaletteOverride);

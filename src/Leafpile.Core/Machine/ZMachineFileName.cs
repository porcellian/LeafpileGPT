namespace Leafpile.Core.Machine;

/// <summary>Applies the Z-machine Standard's suggested filename rules.</summary>
public static class ZMachineFileName
{
    public static string ReadSuggestedFileName(ZMachineMemory memory, ushort nameAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        memory.ValidateReadableRange(nameAddress, 1);
        var length = memory.ReadByte(nameAddress);
        memory.ValidateReadableRange(nameAddress, length + 1);

        var name = new System.Text.StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            name.Append((char)memory.ReadByte(checked((ushort)(nameAddress + 1 + index))));
        }

        return NormalizeFileName(name.ToString());
    }

    public static string NormalizeFileName(string proposedName)
    {
        ArgumentNullException.ThrowIfNull(proposedName);

        var separator = proposedName.IndexOf('.');
        var baseName = separator >= 0 ? proposedName[..separator] : proposedName;
        var extension = separator >= 0 ? proposedName[(separator + 1)..] : "AUX";
        var normalizedBaseName = new System.Text.StringBuilder(8);
        var normalizedExtension = new System.Text.StringBuilder(3);
        foreach (var character in baseName)
        {
            if (char.IsAsciiLetterOrDigit(character) && normalizedBaseName.Length < 8)
            {
                normalizedBaseName.Append(char.ToUpperInvariant(character));
            }
        }

        foreach (var character in extension)
        {
            if (char.IsAsciiLetterOrDigit(character) && normalizedExtension.Length < 3)
            {
                normalizedExtension.Append(char.ToUpperInvariant(character));
            }
        }

        if (normalizedBaseName.Length == 0)
        {
            normalizedBaseName.Append("NULL");
        }

        return $"{normalizedBaseName}.{normalizedExtension}";
    }
}

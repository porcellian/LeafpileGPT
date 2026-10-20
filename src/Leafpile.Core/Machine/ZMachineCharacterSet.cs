using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public sealed class ZMachineCharacterSet
{
    private const int FirstExtraCharacter = 155;
    private const int LastExtraCharacter = 251;
    private const int UnicodeTableExtensionWord = 3;
    private static readonly string DefaultExtraCharacters =
        "äöüÄÖÜß»«ëïÿËÏáéíóúýÁÉÍÓÚÝàèìòùÀÈÌÒÙâêîôûÂÊÎÔÛåÅøØãñõÃÑÕæÆçÇþðÞÐ£œŒ¡¿";

    private readonly string?[] _unicodeCharacters = new string?[256];
    private readonly Dictionary<char, byte> _zsciiByUnicode = [];
    private readonly StoryHeader _header;

    public ZMachineCharacterSet(ZMachineMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        _header = header;
        InitializeAsciiMappings();

        var extraCharacters = ReadUnicodeTranslationTable(memory, header);
        for (var index = 0; index < extraCharacters.Length; index++)
        {
            var zscii = FirstExtraCharacter + index;
            var character = extraCharacters[index];
            _unicodeCharacters[zscii] = character.ToString();
            _zsciiByUnicode.TryAdd(character, (byte)zscii);
        }
    }

    public string ToUnicodeString(ushort zsciiCode)
    {
        if (zsciiCode >= _unicodeCharacters.Length)
        {
            return "?";
        }

        if (zsciiCode == 0)
        {
            return string.Empty;
        }

        if (zsciiCode == 13)
        {
            return "\n";
        }

        if (_header.Version == 6 && (zsciiCode is 9 or 11))
        {
            return zsciiCode == 9 ? "\t" : "\u2002";
        }

        return _unicodeCharacters[zsciiCode] ?? "?";
    }

    public bool IsDefinedForOutput(ushort zsciiCode)
    {
        if (zsciiCode == 0 || zsciiCode == 13 || zsciiCode is >= 32 and <= 126)
        {
            return true;
        }

        if (_header.Version == 6 && (zsciiCode is 9 or 11))
        {
            return true;
        }

        return zsciiCode is >= FirstExtraCharacter and <= LastExtraCharacter && _unicodeCharacters[zsciiCode] is not null;
    }

    public bool IsDefinedForInput(ushort zsciiCode)
    {
        if (zsciiCode is 8 or 13 or 27 or >= 32 and <= 126 or >= 129 and <= 154)
        {
            return true;
        }

        if ((zsciiCode == 252 && _header.Version == 6) || ((zsciiCode is 253 or 254) && _header.Version >= 5))
        {
            return true;
        }

        return zsciiCode is >= FirstExtraCharacter and <= LastExtraCharacter && _unicodeCharacters[zsciiCode] is not null;
    }

    public bool IsDefinedForTextInput(ushort zsciiCode) =>
        (zsciiCode == 13 || zsciiCode is >= 32 and <= 126 || zsciiCode is >= FirstExtraCharacter and <= LastExtraCharacter) &&
        IsDefinedForInput(zsciiCode) && IsDefinedForOutput(zsciiCode);

    public ushort CheckUnicode(ushort unicodeCharacter)
    {
        if (unicodeCharacter == 8)
        {
            return 0x0002;
        }

        if (unicodeCharacter == 27)
        {
            return 0x0002;
        }

        if (!TryGetZsciiForUnicode((char)unicodeCharacter, out var zsciiCode))
        {
            return 0;
        }

        var result = 0;
        if (IsDefinedForOutput(zsciiCode))
        {
            result |= 0x0001;
        }

        if (IsDefinedForInput(zsciiCode))
        {
            result |= 0x0002;
        }

        return (ushort)result;
    }

    public bool TryGetZsciiForUnicode(char character, out byte zsciiCode)
    {
        if (character == '\r' || character == '\n')
        {
            zsciiCode = 13;
            return true;
        }

        return _zsciiByUnicode.TryGetValue(character, out zsciiCode);
    }

    private void InitializeAsciiMappings()
    {
        _unicodeCharacters[0] = string.Empty;
        _unicodeCharacters[9] = _header.Version == 6 ? "\t" : null;
        _unicodeCharacters[11] = _header.Version == 6 ? "\u2002" : null;
        _unicodeCharacters[13] = "\n";

        for (var code = 32; code <= 126; code++)
        {
            var character = (char)code;
            _unicodeCharacters[code] = character.ToString();
            _zsciiByUnicode.Add(character, (byte)code);
        }
    }

    private static char[] ReadUnicodeTranslationTable(ZMachineMemory memory, StoryHeader header)
    {
        if (header.Version < 5)
        {
            return DefaultExtraCharacters.ToCharArray();
        }

        var extensionAddress = memory.ReadWord(0x36);
        if (extensionAddress == 0)
        {
            return DefaultExtraCharacters.ToCharArray();
        }

        memory.ValidateReadableRange(extensionAddress, sizeof(ushort));
        var extensionWords = memory.ReadWord(extensionAddress);
        if (extensionWords < UnicodeTableExtensionWord)
        {
            return DefaultExtraCharacters.ToCharArray();
        }

        var unicodeAddress = (long)extensionAddress + (sizeof(ushort) * UnicodeTableExtensionWord);
        memory.ValidateReadableRange((int)unicodeAddress, sizeof(ushort));
        var tableAddress = memory.ReadWord((ushort)unicodeAddress);
        if (tableAddress == 0)
        {
            return DefaultExtraCharacters.ToCharArray();
        }

        memory.ValidateReadableRange(tableAddress, sizeof(byte));
        var count = memory.ReadByte(tableAddress);
        if (count > LastExtraCharacter - FirstExtraCharacter + 1)
        {
            throw new ZMachineRuntimeException($"Unicode translation table at 0x{tableAddress:x4} defines {count} characters; the maximum is 97.");
        }

        var tableLength = sizeof(byte) + (count * sizeof(ushort));
        memory.ValidateReadableRange(tableAddress, tableLength);
        var characters = new char[count];
        for (var index = 0; index < count; index++)
        {
            var code = memory.ReadWord((ushort)(tableAddress + 1 + (index * sizeof(ushort))));
            if (code is <= 0x001f or >= 0x007f and <= 0x009f)
            {
                throw new ZMachineRuntimeException($"Unicode translation table at 0x{tableAddress:x4} contains forbidden control character U+{code:X4}.");
            }

            characters[index] = (char)code;
        }

        return characters;
    }
}

namespace LeafpileGPT.Core;

/// <summary>Maps defined ZSCII values to Unicode and accepts defined Unicode keyboard input.</summary>
public sealed class ZsciiCodec
{
    private static readonly char[] DefaultExtraCharacters =
    [
        'ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß', '»', '«', 'ë', 'ï', 'ÿ', 'Ë', 'Ï',
        'á', 'é', 'í', 'ó', 'ú', 'ý', 'Á', 'É', 'Í', 'Ó', 'Ú', 'Ý',
        'à', 'è', 'ì', 'ò', 'ù', 'À', 'È', 'Ì', 'Ò', 'Ù',
        'â', 'ê', 'î', 'ô', 'û', 'Â', 'Ê', 'Î', 'Ô', 'Û',
        'å', 'Å', 'ø', 'Ø', 'ã', 'ñ', 'õ', 'Ã', 'Ñ', 'Õ',
        'æ', 'Æ', 'ç', 'Ç', 'þ', 'ð', 'Þ', 'Ð', '£', 'œ', 'Œ', '¡', '¿'
    ];

    private readonly Dictionary<char, byte> _unicodeToZscii = [];
    private readonly char?[] _zsciiToUnicode = new char?[256];

    public ZsciiCodec(ZMachineMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        Version = memory.Header.Version;
        for (var code = 32; code <= 126; code++)
        {
            Define((byte)code, (char)code);
        }

        if (Version >= 6)
        {
            Define(9, '\t');
            Define(11, '\u2002');
        }

        Define(13, '\n');
        var extraCharacters = ReadExtraCharacters(memory);
        for (var index = 0; index < extraCharacters.Length; index++)
        {
            if (extraCharacters[index] is { } character)
            {
                Define(checked((byte)(155 + index)), character);
            }
        }
    }

    public byte Version { get; }

    /// <summary>Converts a ZSCII character for output; unsupported or input-only codes become U+FFFD.</summary>
    public char Decode(ushort zscii)
    {
        if (zscii == 0)
        {
            return '\0';
        }

        return zscii <= byte.MaxValue
            ? _zsciiToUnicode[zscii] ?? '\uFFFD'
            : '\uFFFD';
    }

    /// <summary>Returns whether the code is defined for keyboard input in this story version.</summary>
    public bool IsDefinedInputCode(byte zscii)
    {
        if (zscii is 8 or 13 or 27 or >= 32 and <= 126 or >= 129 and <= 154)
        {
            return true;
        }

        if (zscii is >= 155 and <= 251)
        {
            return _zsciiToUnicode[zscii] is not null;
        }

        return Version == 6 && zscii == 252
            || Version >= 5 && zscii is 253 or 254;
    }

    /// <summary>Maps one supported Unicode keyboard character to ZSCII.</summary>
    public bool TryEncodeInput(char value, out byte zscii)
    {
        if (value == '\b')
        {
            zscii = 8;
            return true;
        }

        if (value is '\r' or '\n')
        {
            zscii = 13;
            return true;
        }

        if (value == '\u001B')
        {
            zscii = 27;
            return true;
        }

        if (_unicodeToZscii.TryGetValue(value, out zscii))
        {
            return true;
        }

        zscii = 0;
        return false;
    }

    /// <summary>Maps a Unicode character to a ZSCII character defined for output.</summary>
    public bool TryEncodeOutput(char value, out byte zscii) => _unicodeToZscii.TryGetValue(value, out zscii);

    private static char?[] ReadExtraCharacters(ZMachineMemory memory)
    {
        ushort address = 0;
        if (memory.Header.Version >= 5)
        {
            address = ReadHeaderExtensionWord(memory, 3);
        }

        if (address == 0)
        {
            return DefaultExtraCharacters.Select(character => (char?)character).ToArray();
        }

        try
        {
            memory.ValidateReadableRange(address, 1);
            var count = memory.ReadByte(address);
            if (count > 97)
            {
                throw new ZMachineTextFormatException(
                    $"Unicode translation table at 0x{address:X} defines {count} characters; the maximum is 97.");
            }

            var byteCount = 1 + (count * sizeof(ushort));
            memory.ValidateReadableRange(address, byteCount);
            var characters = new char?[count];
            for (var index = 0; index < count; index++)
            {
                var codePoint = memory.ReadWord(checked((ushort)(address + 1 + (index * sizeof(ushort)))));
                characters[index] = IsPrintableBmp(codePoint) ? (char)codePoint : null;
            }

            return characters;
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineTextFormatException($"Invalid Unicode translation table at 0x{address:X}.", exception);
        }
    }

    private static ushort ReadHeaderExtensionWord(ZMachineMemory memory, int oneBasedWordIndex)
    {
        var address = memory.Header.HeaderExtensionTableAddress ?? 0;
        if (address == 0)
        {
            return 0;
        }

        try
        {
            memory.ValidateReadableRange(address, sizeof(ushort));
            var count = memory.ReadWord(address);
            if (count < oneBasedWordIndex)
            {
                return 0;
            }

            var wordAddress = (uint)address + sizeof(ushort) + (uint)(oneBasedWordIndex - 1) * sizeof(ushort);
            memory.ValidateReadableRange(wordAddress, sizeof(ushort));
            return memory.ReadWord(checked((ushort)wordAddress));
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineTextFormatException($"Invalid header extension table at 0x{address:X}.", exception);
        }
    }

    private void Define(byte zscii, char unicode)
    {
        _zsciiToUnicode[zscii] = unicode;
        _unicodeToZscii.TryAdd(unicode, zscii);
    }

    private static bool IsPrintableBmp(ushort codePoint) =>
        codePoint is not (>= 0x0000 and <= 0x001F)
        and not (>= 0x007F and <= 0x009F)
        and not (>= 0xD800 and <= 0xDFFF);
}

public sealed class ZMachineTextFormatException : Exception
{
    public ZMachineTextFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

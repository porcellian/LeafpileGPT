using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public sealed class ZMachineTextCodec
{
    private const int AlphabetTableAddressHeaderWord = 0x34;
    private const int AbbreviationsPerGroup = 32;
    private const string Version1A2 = "0123456789.,!?_#'\"/\\<-:()";
    private static readonly string[] DefaultAlphabets =
    [
        "abcdefghijklmnopqrstuvwxyz",
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "0123456789.,!?_#'\"/\\-:()"
    ];

    private readonly ZMachineMemory _memory;
    private readonly StoryHeader _header;
    private readonly ZMachineCharacterSet _characters;
    private readonly byte[][]? _customAlphabets;

    public ZMachineTextCodec(ZMachineMemory memory, StoryHeader header)
        : this(memory, header, new ZMachineCharacterSet(memory, header))
    {
    }

    public ZMachineTextCodec(ZMachineMemory memory, StoryHeader header, ZMachineCharacterSet characters)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(characters);

        _memory = memory;
        _header = header;
        _characters = characters;
        _customAlphabets = ReadCustomAlphabets(memory, header, characters);
    }

    public string Decode(int byteAddress) => DecodeInternal(byteAddress, allowAbbreviations: true);

    public byte[] EncodeZText(ReadOnlySpan<byte> zsciiText)
    {
        var zcharacters = EncodeCharacters(zsciiText, sourceStart: 0, sourceLength: zsciiText.Length, targetLength: null, lowercase: false);
        return PackZCharacters(zcharacters);
    }

    public byte[] EncodeDictionaryText(ReadOnlySpan<byte> zsciiText, int sourceStart, int sourceLength)
    {
        if (sourceStart < 0 || sourceLength < 0 || sourceStart > zsciiText.Length - sourceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart), "The requested ZSCII text range is outside the source buffer.");
        }

        var targetLength = _header.Version <= 3 ? 6 : 9;
        var zcharacters = EncodeCharacters(zsciiText, sourceStart, sourceLength, targetLength, lowercase: true);
        return PackZCharacters(zcharacters);
    }

    public byte[] EncodeDictionaryText(ReadOnlySpan<byte> zsciiText) =>
        EncodeDictionaryText(zsciiText, sourceStart: 0, sourceLength: zsciiText.Length);

    private string DecodeInternal(int byteAddress, bool allowAbbreviations)
    {
        var result = new StringBuilder();
        var pendingCharacters = new List<int>();
        var address = byteAddress;
        var ended = false;

        while (!ended)
        {
            _memory.ValidateReadableRange(address, sizeof(ushort));
            var packedWord = _memory.ReadWord((ushort)address);
            address += sizeof(ushort);
            ended = (packedWord & 0x8000) != 0;

            var zcharacters = new[]
            {
                (packedWord >> 10) & 0x1f,
                (packedWord >> 5) & 0x1f,
                packedWord & 0x1f
            };
            pendingCharacters.AddRange(zcharacters);
        }

        DecodeWordCharacters(pendingCharacters, allowAbbreviations, result);
        return result.ToString();
    }

    private void DecodeWordCharacters(IReadOnlyList<int> zcharacters, bool allowAbbreviations, StringBuilder result)
    {
        var lockedAlphabet = 0;
        int? shiftedAlphabet = null;

        for (var index = 0; index < zcharacters.Count; index++)
        {
            var zcharacter = zcharacters[index];

            if (_header.Version == 1 && zcharacter == 1)
            {
                result.Append(_characters.ToUnicodeString(13));
                shiftedAlphabet = null;
                continue;
            }

            if ((_header.Version == 2 && zcharacter == 1) || (_header.Version >= 3 && zcharacter is >= 1 and <= 3))
            {
                if (!allowAbbreviations)
                {
                    throw new ZMachineRuntimeException("An abbreviation string cannot contain another abbreviation.");
                }

                if (index + 1 >= zcharacters.Count)
                {
                    break;
                }

                var group = _header.Version == 2 ? 0 : zcharacter - 1;
                var abbreviationIndex = (group * AbbreviationsPerGroup) + zcharacters[++index];
                var tableEntryAddress = _header.AbbreviationsTableAddress + (abbreviationIndex * sizeof(ushort));
                _memory.ValidateReadableRange(tableEntryAddress, sizeof(ushort));
                var packedAddress = _memory.ReadWord((ushort)tableEntryAddress);
                var abbreviationAddress = ZMachineAddressing.DecodeWordAddress(packedAddress);
                result.Append(DecodeInternal(abbreviationAddress, allowAbbreviations: false));
                shiftedAlphabet = null;
                continue;
            }

            if (_header.Version <= 2 && (zcharacter is 2 or 3))
            {
                var currentAlphabet = shiftedAlphabet ?? lockedAlphabet;
                shiftedAlphabet = (currentAlphabet + zcharacter - 1) % 3;
                continue;
            }

            if (_header.Version <= 2 && (zcharacter is 4 or 5))
            {
                var currentAlphabet = shiftedAlphabet ?? lockedAlphabet;
                lockedAlphabet = (currentAlphabet + zcharacter - 3) % 3;
                shiftedAlphabet = null;
                continue;
            }

            if (_header.Version >= 3 && (zcharacter is 4 or 5))
            {
                shiftedAlphabet = zcharacter - 3;
                continue;
            }

            if (zcharacter == 0)
            {
                result.Append(' ');
                shiftedAlphabet = null;
                continue;
            }

            var alphabet = shiftedAlphabet ?? lockedAlphabet;
            if (alphabet == 2 && zcharacter == 6 && _header.Version >= 2)
            {
                if (index + 2 >= zcharacters.Count)
                {
                    if (!allowAbbreviations)
                    {
                        throw new ZMachineRuntimeException("An abbreviation string cannot end with an incomplete ZSCII escape.");
                    }

                    break;
                }

                var zsciiCode = (zcharacters[++index] << 5) | zcharacters[++index];
                result.Append(_characters.ToUnicodeString((ushort)zsciiCode));
                shiftedAlphabet = null;
                continue;
            }

            if (alphabet == 2 && zcharacter == 7 && _header.Version >= 2)
            {
                result.Append(_characters.ToUnicodeString(13));
                shiftedAlphabet = null;
                continue;
            }

            var zscii = GetAlphabetCharacter(alphabet, zcharacter);
            result.Append(_characters.ToUnicodeString(zscii));
            shiftedAlphabet = null;
        }
    }

    private List<int> EncodeCharacters(ReadOnlySpan<byte> zsciiText, int sourceStart, int sourceLength, int? targetLength, bool lowercase)
    {
        var encoded = new List<int>(targetLength ?? (sourceLength * 2));
        var sourceEnd = sourceStart + sourceLength;
        var lockedAlphabet = 0;

        for (var index = sourceStart; index < sourceEnd; index++)
        {
            var zscii = zsciiText[index];
            if (lowercase)
            {
                zscii = LowercaseZscii(zscii);
            }

            if (zscii == 0)
            {
                continue;
            }

            var nextCharacter = index + 1 < sourceEnd ? zsciiText[index + 1] : (byte)0;
            if (lowercase)
            {
                nextCharacter = LowercaseZscii(nextCharacter);
            }

            var characterEncoding = EncodeCharacter(zscii, nextCharacter, ref lockedAlphabet, lowercase);
            var remaining = targetLength.HasValue ? targetLength.Value - encoded.Count : characterEncoding.Length;
            if (targetLength.HasValue && remaining <= 0)
            {
                break;
            }

            var charactersToWrite = targetLength.HasValue
                ? Math.Min(remaining, characterEncoding.Length)
                : characterEncoding.Length;
            for (var characterIndex = 0; characterIndex < charactersToWrite; characterIndex++)
            {
                encoded.Add(characterEncoding[characterIndex]);
            }

            if (charactersToWrite < characterEncoding.Length)
            {
                break;
            }
        }

        if (targetLength.HasValue)
        {
            while (encoded.Count < targetLength.Value)
            {
                encoded.Add(5);
            }
        }
        else
        {
            while (encoded.Count % 3 != 0)
            {
                encoded.Add(5);
            }

            if (encoded.Count == 0)
            {
                encoded.AddRange([5, 5, 5]);
            }
        }

        return encoded;
    }

    private byte LowercaseZscii(byte zscii)
    {
        if (!_characters.IsDefinedForTextInput(zscii))
        {
            return zscii;
        }

        var unicode = _characters.ToUnicodeString(zscii);
        if (unicode.Length == 1 && _characters.TryGetZsciiForUnicode(char.ToLowerInvariant(unicode[0]), out var lowercaseCode))
        {
            return lowercaseCode;
        }

        return zscii;
    }

    private int[] EncodeCharacter(byte zscii, byte nextCharacter, ref int lockedAlphabet, bool dictionary)
    {
        if (zscii == 32)
        {
            return [0];
        }

        if (zscii == 13)
        {
            if (_header.Version == 1)
            {
                return [1];
            }

            return EncodeShiftedCharacter(2, 7, nextCharacter, ref lockedAlphabet, dictionary);
        }

        if (TryFindAlphabetCharacter(zscii, out var alphabet, out var zcharacter))
        {
            if (alphabet == lockedAlphabet)
            {
                return [zcharacter];
            }

            if (_header.Version <= 2 && dictionary && TryFindAlphabetCharacter(nextCharacter, out var nextAlphabet, out _) && nextAlphabet == alphabet)
            {
                var shift = (alphabet - lockedAlphabet + 3) % 3;
                lockedAlphabet = alphabet;
                return [shift + 3, zcharacter];
            }

            return EncodeShiftedCharacter(alphabet, zcharacter, nextCharacter, ref lockedAlphabet, dictionary);
        }

        if (_header.Version >= 2 && _characters.IsDefinedForOutput(zscii))
        {
            return EncodeEscape(zscii, lockedAlphabet);
        }

        if (TryFindAlphabetCharacter((byte)'?', out alphabet, out zcharacter))
        {
            return alphabet == lockedAlphabet ? [zcharacter] : EncodeShiftedCharacter(alphabet, zcharacter, nextCharacter, ref lockedAlphabet, dictionary);
        }

        if (_header.Version >= 2)
        {
            return EncodeEscape((byte)'?', lockedAlphabet);
        }

        throw new ZMachineRuntimeException($"ZSCII character {zscii} cannot be represented in this story's alphabets.");
    }

    private bool TryFindAlphabetCharacter(byte zscii, out int alphabet, out int zcharacter)
    {
        for (alphabet = 0; alphabet < 3; alphabet++)
        {
            if (_customAlphabets is not null)
            {
                var index = Array.IndexOf(_customAlphabets[alphabet], zscii);
                if (index >= 0 && !(alphabet == 2 && _header.Version >= 2 && index < 2))
                {
                    zcharacter = index + 6;
                    return true;
                }

                continue;
            }

            if (alphabet == 2 && _header.Version >= 2)
            {
                var defaultIndex = DefaultAlphabets[alphabet].IndexOf((char)zscii);
                if (defaultIndex >= 0)
                {
                    zcharacter = defaultIndex + 8;
                    if (zcharacter <= 31)
                    {
                        return true;
                    }
                }

                continue;
            }

            var indexInAlphabet = GetDefaultAlphabet(alphabet).IndexOf((char)zscii);
            if (indexInAlphabet >= 0)
            {
                zcharacter = indexInAlphabet + 6;
                return true;
            }
        }

        alphabet = 0;
        zcharacter = 0;
        return false;
    }

    private byte GetAlphabetCharacter(int alphabet, int zcharacter)
    {
        if (_customAlphabets is not null)
        {
            return _customAlphabets[alphabet][zcharacter - 6];
        }

        if (alphabet == 2 && _header.Version >= 2 && zcharacter < 8)
        {
            return 0;
        }

        var defaultZcharacter = alphabet == 2 && _header.Version >= 2 ? zcharacter - 8 : zcharacter - 6;
        var defaultCharacter = GetDefaultAlphabet(alphabet)[defaultZcharacter];
        return (byte)defaultCharacter;
    }

    private int[] EncodeShiftedCharacter(int alphabet, int zcharacter, byte nextCharacter, ref int lockedAlphabet, bool dictionary)
    {
        if (_header.Version <= 2)
        {
            var shift = (alphabet - lockedAlphabet + 3) % 3;
            if (shift == 0)
            {
                return [zcharacter];
            }

            if (dictionary && TryFindAlphabetCharacter(nextCharacter, out var nextAlphabet, out _) && nextAlphabet == alphabet)
            {
                lockedAlphabet = alphabet;
                return [shift + 3, zcharacter];
            }

            return [shift + 1, zcharacter];
        }

        return [alphabet + 3, zcharacter];
    }

    private int GetShiftCharacterFromCurrent(int alphabet, int currentAlphabet)
    {
        if (_header.Version <= 2)
        {
            var shift = (alphabet - currentAlphabet + 3) % 3;
            return shift == 0 ? 0 : shift + 1;
        }

        return alphabet + 3;
    }

    private int[] EncodeEscape(byte zscii, int currentAlphabet)
    {
        var shift = GetShiftCharacterFromCurrent(2, currentAlphabet);
        return shift == 0
            ? [6, zscii >> 5, zscii & 0x1f]
            : [shift, 6, zscii >> 5, zscii & 0x1f];
    }

    private string GetDefaultAlphabet(int alphabet) =>
        alphabet == 2 && _header.Version == 1 ? Version1A2 : DefaultAlphabets[alphabet];

    private static byte[][]? ReadCustomAlphabets(ZMachineMemory memory, StoryHeader header, ZMachineCharacterSet characters)
    {
        if (header.Version < 5)
        {
            return null;
        }

        var address = memory.ReadWord(AlphabetTableAddressHeaderWord);
        if (address == 0)
        {
            return null;
        }

        const int tableLength = 3 * 26;
        memory.ValidateReadableRange(address, tableLength);
        var alphabets = new byte[3][];
        for (var alphabet = 0; alphabet < alphabets.Length; alphabet++)
        {
            alphabets[alphabet] = new byte[26];
            for (var index = 0; index < alphabets[alphabet].Length; index++)
            {
                var zscii = memory.ReadByte((ushort)(address + (alphabet * 26) + index));
                if (alphabet == 2 && index < 2)
                {
                    continue;
                }

                if (!characters.IsDefinedForOutput(zscii))
                {
                    throw new ZMachineRuntimeException($"Alphabet table at 0x{address:x4} contains ZSCII code {zscii}, which is not defined for output.");
                }

                alphabets[alphabet][index] = zscii;
            }
        }

        return alphabets;
    }

    private static byte[] PackZCharacters(IReadOnlyList<int> zcharacters)
    {
        var wordCount = (zcharacters.Count + 2) / 3;
        var bytes = new byte[wordCount * sizeof(ushort)];
        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var characterIndex = wordIndex * 3;
            var first = zcharacters[characterIndex];
            var second = characterIndex + 1 < zcharacters.Count ? zcharacters[characterIndex + 1] : 5;
            var third = characterIndex + 2 < zcharacters.Count ? zcharacters[characterIndex + 2] : 5;
            var packedWord = (ushort)((first << 10) | (second << 5) | third);
            if (wordIndex == wordCount - 1)
            {
                packedWord |= 0x8000;
            }

            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(wordIndex * sizeof(ushort), sizeof(ushort)), packedWord);
        }

        return bytes;
    }
}

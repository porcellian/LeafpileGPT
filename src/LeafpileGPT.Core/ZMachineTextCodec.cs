namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Text;

/// <summary>Decodes packed Z-machine text and encodes fixed-width dictionary words.</summary>
public sealed class ZMachineTextCodec
{
    private const int MaximumDecodedCharacters = 1_048_576;

    private readonly ZMachineMemory _memory;
    private readonly byte[][] _alphabets;
    private readonly ZsciiCodec _zscii;

    public ZMachineTextCodec(ZMachineMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Version = memory.Header.Version;
        _zscii = new ZsciiCodec(memory);
        _alphabets = ReadAlphabets(memory);
    }

    public byte Version { get; }

    /// <summary>Gets the story-specific ZSCII-to-Unicode mapper used for output and keyboard input.</summary>
    public ZsciiCodec Zscii => _zscii;

    /// <summary>Decodes a packed routine/string address using the version-specific string scale.</summary>
    public string DecodePackedString(ushort packedAddress) =>
        DecodeStringAtByteAddress(ZMachineAddressing.PackedToByteAddress(
            packedAddress, _memory.Header, PackedAddressKind.String));

    /// <summary>Decodes a terminated packed Z-string at a byte address.</summary>
    public string DecodeStringAtByteAddress(uint byteAddress) =>
        DecodeStringAtByteAddress(byteAddress, allowAbbreviations: true);

    /// <summary>Decodes a terminated sequence of packed words, such as inline instruction text.</summary>
    public string DecodePackedText(ReadOnlySpan<byte> packedBytes)
    {
        if ((packedBytes.Length & 1) != 0)
        {
            throw new ZMachineTextFormatException("Packed Z-text must contain complete two-byte words.");
        }

        var zCharacters = new List<byte>();
        for (var offset = 0; offset < packedBytes.Length; offset += sizeof(ushort))
        {
            var word = (ushort)((packedBytes[offset] << 8) | packedBytes[offset + 1]);
            zCharacters.Add((byte)((word >> 10) & 0x1F));
            zCharacters.Add((byte)((word >> 5) & 0x1F));
            zCharacters.Add((byte)(word & 0x1F));
            if ((word & 0x8000) != 0)
            {
                return DecodeZCharacters(zCharacters.ToArray());
            }
        }

        throw new ZMachineTextFormatException("Packed Z-text has no terminating word.");
    }

    /// <summary>Decodes individual 5-bit Z-characters, ignoring incomplete trailing constructions.</summary>
    public string DecodeZCharacters(ReadOnlySpan<byte> zCharacters)
    {
        var output = new StringBuilder();
        var currentAlphabet = 0;
        int? shiftedAlphabet = null;
        for (var index = 0; index < zCharacters.Length; index++)
        {
            var zCharacter = zCharacters[index];
            if (zCharacter > 31)
            {
                throw new ZMachineTextFormatException($"Z-character {zCharacter} is outside the five-bit range.");
            }

            if (zCharacter == 0)
            {
                Append(output, ' ');
                shiftedAlphabet = null;
                continue;
            }

            if (TryGetAbbreviationNumber(zCharacter, out var abbreviationNumber))
            {
                if (++index >= zCharacters.Length)
                {
                    break;
                }

                if (_memory.Header.AbbreviationsTableAddress == 0)
                {
                    throw new ZMachineTextFormatException("The text uses abbreviations but the abbreviation table address is zero.");
                }

                if (abbreviationNumber >= 0)
                {
                    var entryNumber = (abbreviationNumber * 32) + zCharacters[index];
                    var abbreviation = DecodeAbbreviation(entryNumber, allowAbbreviations: false);
                    Append(output, abbreviation);
                }

                shiftedAlphabet = null;
                continue;
            }

            if (Version <= 2 && zCharacter is 2 or 3)
            {
                shiftedAlphabet = RotateAlphabet(currentAlphabet, zCharacter == 2 ? 1 : 2);
                continue;
            }

            if (Version <= 2 && zCharacter is 4 or 5)
            {
                currentAlphabet = RotateAlphabet(currentAlphabet, zCharacter == 4 ? 1 : 2);
                shiftedAlphabet = null;
                continue;
            }

            if (Version >= 3 && zCharacter is 4 or 5)
            {
                shiftedAlphabet = zCharacter - 3;
                continue;
            }

            var alphabet = shiftedAlphabet ?? currentAlphabet;
            shiftedAlphabet = null;
            if (zCharacter == 1 && Version == 1)
            {
                Append(output, '\n');
                continue;
            }

            if (alphabet == 2 && zCharacter == 6 && Version >= 2)
            {
                if (index + 2 >= zCharacters.Length)
                {
                    break;
                }

                var escaped = (ushort)((zCharacters[++index] << 5) | zCharacters[++index]);
                var character = _zscii.Decode(escaped);
                if (character != '\0')
                {
                    Append(output, character);
                }

                continue;
            }

            if (alphabet == 2 && zCharacter == 7 && Version >= 2)
            {
                Append(output, '\n');
                continue;
            }

            var code = _alphabets[alphabet][zCharacter - 6];
            var decoded = _zscii.Decode(code);
            if (decoded != '\0')
            {
                Append(output, decoded);
            }
        }

        return output.ToString();
    }

    /// <summary>Encodes player text to the fixed six- or nine-Z-character dictionary representation.</summary>
    public ImmutableArray<byte> EncodeDictionaryWord(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var targetLength = Version <= 3 ? 6 : 9;
        var sourceCharacters = text.Select(character => char.ToLowerInvariant(character)).ToArray();
        var encoded = sourceCharacters.Select(EncodeCharacter).ToArray();
        var zCharacters = new List<byte>(targetLength);
        var currentAlphabet = 0;

        for (var index = 0; index < encoded.Length && zCharacters.Count < targetLength; index++)
        {
            var character = encoded[index];
            if (!character.IsEscape && character.ZCharacter == 0)
            {
                zCharacters.Add(0);
                continue;
            }

            if (character.IsEscape)
            {
                AddShiftToAlphabet(zCharacters, ref currentAlphabet, 2, lockAlphabet: false);
                zCharacters.Add(6); // A2 Z-character 6 introduces a ZSCII escape from Version 2 onward.
                zCharacters.Add((byte)(character.Zscii >> 5));
                zCharacters.Add((byte)(character.Zscii & 0x1F));
                continue;
            }

            if (Version <= 2 && character.Alphabet != currentAlphabet
                && index + 1 < encoded.Length
                && !encoded[index + 1].IsEscape
                && encoded[index + 1].Alphabet == character.Alphabet)
            {
                AddShiftToAlphabet(zCharacters, ref currentAlphabet, character.Alphabet, lockAlphabet: true);
                zCharacters.Add(character.ZCharacter);
                continue;
            }

            AddShiftToAlphabet(zCharacters, ref currentAlphabet, character.Alphabet, lockAlphabet: false);
            zCharacters.Add(character.ZCharacter);
        }

        while (zCharacters.Count < targetLength)
        {
            zCharacters.Add(5);
        }

        if (zCharacters.Count > targetLength)
        {
            zCharacters.RemoveRange(targetLength, zCharacters.Count - targetLength);
        }

        var packed = ImmutableArray.CreateBuilder<byte>(targetLength / 3 * sizeof(ushort));
        for (var index = 0; index < targetLength; index += 3)
        {
            var word = (ushort)((zCharacters[index] << 10)
                | (zCharacters[index + 1] << 5)
                | zCharacters[index + 2]);
            if (index + 3 == targetLength)
            {
                word |= 0x8000;
            }

            packed.Add((byte)(word >> 8));
            packed.Add((byte)word);
        }

        return packed.MoveToImmutable();
    }

    private static byte[][] ReadAlphabets(ZMachineMemory memory)
    {
        var version = memory.Header.Version;
        var punctuation = version == 1
            ? @" 0123456789.,!?_#'""/\<-:()"
            : @" ^0123456789.,!?_#'""/\-:()";
        byte[][] alphabets =
        [
            "abcdefghijklmnopqrstuvwxyz"u8.ToArray(),
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8.ToArray(),
            punctuation.Select(character => (byte)character).ToArray()
        ];

        if (version < 5 || memory.Header.AlphabetTableAddress is not { } address || address == 0)
        {
            return alphabets;
        }

        try
        {
            memory.ValidateReadableRange(address, 78);
            for (var alphabet = 0; alphabet < 3; alphabet++)
            {
                for (var index = 0; index < 26; index++)
                {
                    alphabets[alphabet][index] = memory.ReadByte(checked((ushort)(address + alphabet * 26 + index)));
                }
            }
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineTextFormatException($"Invalid custom alphabet table at 0x{address:X}.", exception);
        }

        return alphabets;
    }

    private bool TryGetAbbreviationNumber(byte zCharacter, out int abbreviationNumber)
    {
        if (zCharacter == 1 && Version == 1)
        {
            abbreviationNumber = -1;
            return false;
        }

        if (Version == 2 && zCharacter == 1 || Version >= 3 && zCharacter is >= 1 and <= 3)
        {
            abbreviationNumber = zCharacter - 1;
            return true;
        }

        abbreviationNumber = -1;
        return false;
    }

    private string DecodeAbbreviation(int abbreviationNumber, bool allowAbbreviations)
    {
        var entryAddress = (uint)_memory.Header.AbbreviationsTableAddress + (uint)abbreviationNumber * sizeof(ushort);
        try
        {
            _memory.ValidateReadableRange(entryAddress, sizeof(ushort));
            var packedAddress = _memory.ReadWord(checked((ushort)entryAddress));
            var byteAddress = ZMachineAddressing.WordToByteAddress(packedAddress);
            return DecodeStringAtByteAddress(byteAddress, allowAbbreviations);
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineTextFormatException(
                $"Invalid abbreviation table entry {abbreviationNumber} at 0x{entryAddress:X}.", exception);
        }
    }

    private string DecodeStringAtByteAddress(uint byteAddress, bool allowAbbreviations)
    {
        var zCharacters = new List<byte>();
        var cursor = byteAddress;
        try
        {
            while (cursor <= (uint)_memory.Length - sizeof(ushort))
            {
                var word = _memory.ReadStoryWord(cursor);
                cursor += sizeof(ushort);
                zCharacters.Add((byte)((word >> 10) & 0x1F));
                zCharacters.Add((byte)((word >> 5) & 0x1F));
                zCharacters.Add((byte)(word & 0x1F));
                if ((word & 0x8000) != 0)
                {
                    return allowAbbreviations
                        ? DecodeZCharacters(zCharacters.ToArray())
                        : DecodeWithoutAbbreviations(zCharacters.ToArray());
                }
            }
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineTextFormatException(
                $"Packed Z-string at byte address 0x{byteAddress:X} extends beyond the story image.", exception);
        }

        throw new ZMachineTextFormatException(
            $"Packed Z-string at byte address 0x{byteAddress:X} has no terminating word.");
    }

    private string DecodeWithoutAbbreviations(ReadOnlySpan<byte> zCharacters)
    {
        if (Version < 2)
        {
            return DecodeZCharacters(zCharacters);
        }

        // An abbreviation string containing an abbreviation is malformed. Consume the two
        // marker characters safely without recursively expanding or looping on hostile input.
        var safe = new List<byte>(zCharacters.Length);
        for (var index = 0; index < zCharacters.Length; index++)
        {
            var character = zCharacters[index];
            if (Version == 2 && character == 1 || Version >= 3 && character is >= 1 and <= 3)
            {
                if (index + 1 < zCharacters.Length)
                {
                    index++;
                }

                continue;
            }

            safe.Add(character);
        }

        return DecodeZCharacters(safe.ToArray());
    }

    private EncodedCharacter EncodeCharacter(char value)
    {
        if (value == ' ')
        {
            return new EncodedCharacter(0, 0, false, 0);
        }

        for (var alphabet = 0; alphabet < _alphabets.Length; alphabet++)
        {
            for (var index = 0; index < 26; index++)
            {
                if (alphabet == 2 && Version >= 2 && index < 2)
                {
                    continue;
                }

                var translated = _zscii.Decode(_alphabets[alphabet][index]);
                if (translated == value)
                {
                    return new EncodedCharacter(alphabet, (byte)(index + 6), false, 0);
                }
            }
        }

        if (Version >= 2 && _zscii.TryEncodeInput(value, out var zscii))
        {
            return new EncodedCharacter(2, 6, true, zscii);
        }

        // Invalid or unrepresentable input is encoded as the standard question mark.
        for (var index = 0; index < 26; index++)
        {
            if (_zscii.Decode(_alphabets[2][index]) == '?')
            {
                return new EncodedCharacter(2, (byte)(index + 6), false, 0);
            }
        }

        return new EncodedCharacter(0, 5, false, 0);
    }

    private void AddShiftToAlphabet(List<byte> zCharacters, ref int currentAlphabet, int targetAlphabet, bool lockAlphabet)
    {
        if (Version >= 3)
        {
            if (targetAlphabet != 0)
            {
                zCharacters.Add((byte)(targetAlphabet + 3));
            }

            return;
        }

        if (targetAlphabet == currentAlphabet)
        {
            return;
        }

        var forward = (targetAlphabet - currentAlphabet + 3) % 3;
        zCharacters.Add((byte)((lockAlphabet ? 4 : 2) + forward - 1));
        if (lockAlphabet)
        {
            currentAlphabet = targetAlphabet;
        }
    }

    private static int RotateAlphabet(int alphabet, int shift) => (alphabet + shift) % 3;

    private static void Append(StringBuilder output, char value)
    {
        if (output.Length >= MaximumDecodedCharacters)
        {
            throw new ZMachineTextFormatException(
                $"Decoded text exceeds the {MaximumDecodedCharacters} character safety limit.");
        }

        output.Append(value);
    }

    private static void Append(StringBuilder output, string value)
    {
        if (value.Length > MaximumDecodedCharacters - output.Length)
        {
            throw new ZMachineTextFormatException(
                $"Decoded text exceeds the {MaximumDecodedCharacters} character safety limit.");
        }

        output.Append(value);
    }

    private readonly record struct EncodedCharacter(int Alphabet, byte ZCharacter, bool IsEscape, ushort Zscii);
}

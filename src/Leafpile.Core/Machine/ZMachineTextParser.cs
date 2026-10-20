using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Splits ZSCII text buffers and writes dictionary tokens to parse buffers.</summary>
public sealed class ZMachineTextParser
{
    private readonly ZMachineMemory _memory;
    private readonly StoryHeader _header;
    private readonly ZMachineDictionary _dictionary;
    private readonly ZMachineTextCodec _text;

    public ZMachineTextParser(ZMachineMemory memory, StoryHeader header, ZMachineDictionary dictionary, ZMachineTextCodec text)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(text);

        _memory = memory;
        _header = header;
        _dictionary = dictionary;
        _text = text;
    }

    public void Tokenise(ushort textBufferAddress, ushort parseBufferAddress, ushort dictionaryAddress, bool skipUnrecognized)
    {
        if (dictionaryAddress == 0)
        {
            dictionaryAddress = _dictionary.StandardDictionaryAddress;
        }

        var (textAddress, textLength) = ReadTextBuffer(textBufferAddress);
        _memory.ValidateDynamicRange(parseBufferAddress, 1);
        var maximumWords = _memory.ReadByte(parseBufferAddress);
        var parseBufferLength = 2 + (maximumWords * 4);
        _memory.ValidateDynamicRange(parseBufferAddress, parseBufferLength);

        var separators = _dictionary.ReadDictionarySeparators(dictionaryAddress);
        var entries = SplitWords(textAddress, textLength, separators);
        var wordCount = Math.Min(maximumWords, entries.Count);
        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var word = entries[wordIndex];
            var encoded = _text.EncodeDictionaryText(word.Bytes);
            var dictionaryEntry = _dictionary.Lookup(dictionaryAddress, encoded);
            if (skipUnrecognized && dictionaryEntry == 0)
            {
                continue;
            }

            var outputAddress = parseBufferAddress + 2 + (wordIndex * 4);
            var textBufferHeaderLength = _header.Version <= 4 ? 1 : 2;
            var textPosition = word.StartOffset + textBufferHeaderLength;
            if (textPosition > byte.MaxValue)
            {
                throw new ZMachineRuntimeException($"Text position {textPosition} cannot be represented in a parse-table entry.");
            }

            _memory.WriteWord(checked((ushort)outputAddress), dictionaryEntry);
            _memory.WriteByte(checked((ushort)(outputAddress + 2)), checked((byte)word.Bytes.Length));
            _memory.WriteByte(
                checked((ushort)(outputAddress + 3)),
                (byte)textPosition);
        }

        _memory.WriteByte(checked((ushort)(parseBufferAddress + 1)), checked((byte)wordCount));
    }

    public byte[] EncodeText(ushort sourceAddress, ushort length, ushort startOffset)
    {
        var address = (long)sourceAddress + startOffset;
        var end = address + length;
        if (address > ushort.MaxValue || end > _memory.DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException("encode_text source range is outside dynamic memory.");
        }

        if (length > 0)
        {
            _memory.ValidateDynamicRange((ushort)address, length);
        }

        var zsciiText = new byte[length];
        for (var index = 0; index < length; index++)
        {
            zsciiText[index] = _memory.ReadByte(checked((ushort)(address + index)));
        }

        return _text.EncodeDictionaryText(zsciiText);
    }

    private (int TextAddress, int TextLength) ReadTextBuffer(ushort bufferAddress)
    {
        if (_header.Version <= 4)
        {
            _memory.ValidateDynamicRange(bufferAddress, 1);
            var maximumLength = _memory.ReadByte(bufferAddress) + 1;
            var legacyTextAddress = bufferAddress + 1;
            _memory.ValidateDynamicRange(checked((ushort)legacyTextAddress), maximumLength + 1);
            var legacyTextLength = 0;
            while (legacyTextLength <= maximumLength && _memory.ReadByte(checked((ushort)(legacyTextAddress + legacyTextLength))) != 0)
            {
                legacyTextLength++;
            }

            if (legacyTextLength > maximumLength)
            {
                throw new ZMachineRuntimeException($"V1–V4 text buffer at 0x{bufferAddress:x4} has no terminator within its declared capacity.");
            }

            return (legacyTextAddress, legacyTextLength);
        }

        _memory.ValidateDynamicRange(bufferAddress, 2);
        var maximumTextLength = _memory.ReadByte(bufferAddress);
        var textBufferHeaderLength = 2;
        var textLength = _memory.ReadByte(checked((ushort)(bufferAddress + 1)));
        if (textLength > maximumTextLength)
        {
            throw new ZMachineRuntimeException($"Text buffer at 0x{bufferAddress:x4} contains {textLength} characters, exceeding its maximum of {maximumTextLength}.");
        }

        var textAddress = bufferAddress + textBufferHeaderLength;
        if (textLength > 0)
        {
            _memory.ValidateDynamicRange(checked((ushort)textAddress), textLength);
        }

        return (textAddress, textLength);
    }

    private List<Word> SplitWords(int textAddress, int textLength, IReadOnlySet<byte> separators)
    {
        var words = new List<Word>();
        var index = 0;
        while (index < textLength)
        {
            var currentAddress = checked((ushort)(textAddress + index));
            var current = _memory.ReadByte(currentAddress);
            if (current == 32)
            {
                index++;
                continue;
            }

            var start = index;
            if (separators.Contains(current))
            {
                words.Add(new Word([current], start));
                index++;
                continue;
            }

            var characters = new List<byte>();
            while (index < textLength)
            {
                current = _memory.ReadByte(checked((ushort)(textAddress + index)));
                if (current == 32 || separators.Contains(current))
                {
                    break;
                }

                characters.Add(current);
                index++;
            }

            words.Add(new Word(characters.ToArray(), start));
        }

        return words;
    }

    private sealed record Word(byte[] Bytes, int StartOffset);
}

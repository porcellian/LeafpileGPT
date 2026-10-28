namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Text;

/// <summary>Looks up encoded dictionary words and writes Z-machine parse tables.</summary>
public sealed class ZMachineDictionary
{
    private readonly ZMachineMemory _memory;
    private readonly ZMachineTextCodec _textCodec;

    public ZMachineDictionary(ZMachineMemory memory, ZMachineTextCodec? textCodec = null)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _textCodec = textCodec ?? new ZMachineTextCodec(memory);
    }

    public ImmutableArray<byte> WordSeparators => ReadTable(0).Separators;

    public byte EntryLength => ReadTable(0).EntryLength;

    /// <summary>Returns the byte address of an encoded word, or zero when it is absent.</summary>
    public ushort FindWord(ReadOnlySpan<byte> zsciiWord, ushort dictionaryAddress = 0)
    {
        var table = ReadTable(dictionaryAddress);
        return FindWord(zsciiWord, table);
    }

    /// <summary>Tokenizes an input buffer and writes the bounded four-byte parse records.</summary>
    public void Tokenise(
        ushort textBufferAddress,
        ushort parseBufferAddress,
        ushort dictionaryAddress = 0,
        bool skipUnrecognizedWords = false)
    {
        if (parseBufferAddress == 0)
        {
            throw new ZMachineExecutionException("tokenise requires a non-zero parse-buffer address.");
        }

        var table = ReadTable(dictionaryAddress);
        var text = ZMachineTextBuffer.ReadText(_memory, textBufferAddress);
        var maximumWords = _memory.ReadByte(parseBufferAddress);
        var parseLength = Math.Max(6, checked(2 + (maximumWords * 4)));
        _memory.ValidateWritableRange(parseBufferAddress, parseLength);

        var wordStartOffset = _memory.Header.Version <= 4 ? 1 : 2;
        var words = SplitWords(text.AsSpan(), table.Separators.ToHashSet(), wordStartOffset);
        var wordCount = Math.Min(words.Count, maximumWords);
        if (words.Take(wordCount).Any(word => word.Position > byte.MaxValue || word.Text.Length > byte.MaxValue))
        {
            throw new ZMachineExecutionException("A parsed word position or length exceeds its one-byte parse-table field.");
        }

        _memory.WriteByte(checked((ushort)(parseBufferAddress + 1)), checked((byte)wordCount));
        for (var index = 0; index < wordCount; index++)
        {
            var word = words[index];
            var dictionaryEntry = FindWord(word.Text.AsSpan(), table);
            if (dictionaryEntry == 0 && skipUnrecognizedWords)
            {
                continue;
            }

            var recordAddress = checked((ushort)(parseBufferAddress + 2 + (index * 4)));
            _memory.WriteWord(recordAddress, dictionaryEntry);
            _memory.WriteByte(checked((ushort)(recordAddress + 2)), checked((byte)word.Text.Length));
            _memory.WriteByte(checked((ushort)(recordAddress + 3)), checked((byte)word.Position));
        }
    }

    /// <summary>Encodes raw ZSCII text as a fixed-width dictionary word in a dynamic-memory array.</summary>
    public void EncodeText(ushort textAddress, ushort length, ushort from, ushort encodedAddress)
    {
        var sourceAddress = (uint)textAddress + from;
        _memory.ValidateReadableRange(sourceAddress, length);
        var zsciiText = new byte[length];
        for (var index = 0; index < length; index++)
        {
            zsciiText[index] = _memory.ReadByte(checked((ushort)(sourceAddress + (uint)index)));
        }

        var encoded = EncodeWord(zsciiText);
        if (encoded.IsEmpty)
        {
            throw new ZMachineExecutionException("encode_text contains a ZSCII value that cannot be encoded as a dictionary word.");
        }

        _memory.ValidateWritableRange(encodedAddress, encoded.Length);
        for (var index = 0; index < encoded.Length; index++)
        {
            _memory.WriteByte(checked((ushort)(encodedAddress + index)), encoded[index]);
        }
    }

    private ushort FindWord(ReadOnlySpan<byte> zsciiWord, DictionaryTable table)
    {
        var key = EncodeWord(zsciiWord);
        if (key.IsEmpty)
        {
            return 0;
        }

        var low = 0;
        var high = table.EntryCount - 1;
        while (low <= high)
        {
            var candidate = table.IsSorted ? low + ((high - low) / 2) : low;
            var entryAddress = table.EntryAddress(candidate);
            var entry = ReadEntryWord(entryAddress, key.Length);
            var comparison = entry.AsSpan().SequenceCompareTo(key.AsSpan());
            if (comparison == 0)
            {
                return checked((ushort)entryAddress);
            }

            if (!table.IsSorted)
            {
                low++;
            }
            else if (comparison < 0)
            {
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }

        return 0;
    }

    private DictionaryTable ReadTable(ushort selectedAddress)
    {
        var address = selectedAddress == 0 ? _memory.Header.DictionaryAddress : selectedAddress;
        if (address == 0)
        {
            throw new ZMachineExecutionException("The story does not define a dictionary table.");
        }

        _memory.ValidateReadableRange(address, 1);
        var separatorCount = _memory.ReadByte(address);
        var headerLength = checked(4 + separatorCount);
        _memory.ValidateReadableRange(address, headerLength);
        var separators = ImmutableArray.CreateBuilder<byte>(separatorCount);
        var zscii = _textCodec.Zscii;
        for (var index = 0; index < separatorCount; index++)
        {
            var separator = _memory.ReadByte(checked((ushort)(address + 1 + index)));
            if (separator is 0 or 32 || !zscii.IsDefinedInputCode(separator) || zscii.Decode(separator) == '\uFFFD')
            {
                throw new ZMachineExecutionException(
                    $"Dictionary at 0x{address:X4} has invalid word separator {separator}.");
            }

            separators.Add(separator);
        }

        var entryLength = _memory.ReadByte(checked((ushort)(address + 1 + separatorCount)));
        var minimumEntryLength = _memory.Header.Version <= 3 ? 4 : 6;
        if (entryLength < minimumEntryLength)
        {
            throw new ZMachineExecutionException(
                $"Dictionary at 0x{address:X4} uses entry length {entryLength}; Version {_memory.Header.Version} requires at least {minimumEntryLength}.");
        }

        var rawCount = _memory.ReadWord(checked((ushort)(address + 2 + separatorCount)));
        var signedCount = unchecked((short)rawCount);
        var entryCount = Math.Abs((int)signedCount);
        var entriesAddress = (uint)address + (uint)headerLength;
        _memory.ValidateReadableRange(entriesAddress, checked(entryCount * entryLength));
        return new DictionaryTable(separators.MoveToImmutable(), entryLength, entryCount, signedCount >= 0, entriesAddress);
    }

    private ImmutableArray<byte> EncodeWord(ReadOnlySpan<byte> zsciiWord)
    {
        var characters = new StringBuilder(zsciiWord.Length);
        foreach (var character in zsciiWord)
        {
            if (!_textCodec.Zscii.IsDefinedInputCode(character))
            {
                return ImmutableArray<byte>.Empty;
            }

            var decoded = _textCodec.Zscii.Decode(character);
            if (decoded == '\uFFFD' || decoded == '\0')
            {
                return ImmutableArray<byte>.Empty;
            }

            characters.Append(decoded);
        }

        return _textCodec.EncodeDictionaryWord(characters.ToString());
    }

    private byte[] ReadEntryWord(uint entryAddress, int encodedLength)
    {
        var bytes = new byte[encodedLength];
        for (var index = 0; index < encodedLength; index++)
        {
            bytes[index] = _memory.ReadByte(checked((ushort)(entryAddress + (uint)index)));
        }

        return bytes;
    }

    private static List<TextWord> SplitWords(ReadOnlySpan<byte> text, HashSet<byte> separators, int startOffset)
    {
        var words = new List<TextWord>();
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == 32)
            {
                index++;
                continue;
            }

            if (separators.Contains(character))
            {
                words.Add(new([character], index + startOffset));
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && text[index] != 32 && !separators.Contains(text[index]))
            {
                index++;
            }

            words.Add(new(text.Slice(start, index - start).ToArray(), start + startOffset));
        }

        return words;
    }

    private sealed class DictionaryTable(
        ImmutableArray<byte> separators,
        byte entryLength,
        int entryCount,
        bool isSorted,
        uint entriesAddress)
    {
        public ImmutableArray<byte> Separators { get; } = separators;

        public byte EntryLength { get; } = entryLength;

        public int EntryCount { get; } = entryCount;

        public bool IsSorted { get; } = isSorted;

        public uint EntryAddress(int index) => entriesAddress + (uint)(index * EntryLength);
    }

    private readonly record struct TextWord(byte[] Text, int Position);
}

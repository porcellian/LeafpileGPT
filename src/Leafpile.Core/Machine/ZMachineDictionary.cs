using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Looks up fixed-width dictionary words in a story or user dictionary table.</summary>
public sealed class ZMachineDictionary
{
    private readonly ZMachineMemory _memory;
    private readonly StoryHeader _header;
    private readonly ZMachineCharacterSet _characters;

    public ZMachineDictionary(ZMachineMemory memory, StoryHeader header, ZMachineCharacterSet characters)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(characters);

        _memory = memory;
        _header = header;
        _characters = characters;
    }

    public ushort Lookup(ushort dictionaryAddress, ReadOnlySpan<byte> encodedWord)
    {
        var dictionary = ReadHeader(dictionaryAddress);
        var expectedKeyLength = _header.Version <= 3 ? 4 : 6;
        if (encodedWord.Length != expectedKeyLength)
        {
            throw new ArgumentException($"A version {_header.Version} dictionary key must contain {expectedKeyLength} bytes.", nameof(encodedWord));
        }

        if (dictionary.EntryCount == 0)
        {
            return 0;
        }

        if (dictionary.IsSorted)
        {
            var low = 0;
            var high = dictionary.EntryCount - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var address = dictionary.EntriesAddress + (middle * dictionary.EntryLength);
                var comparison = CompareEntry(address, encodedWord);
                if (comparison == 0)
                {
                    return checked((ushort)address);
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return 0;
        }

        for (var index = 0; index < dictionary.EntryCount; index++)
        {
            var address = dictionary.EntriesAddress + (index * dictionary.EntryLength);
            if (CompareEntry(address, encodedWord) == 0)
            {
                return checked((ushort)address);
            }
        }

        return 0;
    }

    public ushort Lookup(ushort dictionaryAddress, ReadOnlySpan<byte> zsciiText, int sourceStart, int sourceLength, ZMachineTextCodec text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Lookup(dictionaryAddress, text.EncodeDictionaryText(zsciiText, sourceStart, sourceLength));
    }

    public ushort StandardDictionaryAddress => _header.DictionaryAddress;

    public int GetEntryCount(ushort dictionaryAddress) => ReadHeader(dictionaryAddress).EntryCount;

    public IReadOnlySet<byte> ReadDictionarySeparators(ushort dictionaryAddress) => ReadHeader(dictionaryAddress).Separators;

    private DictionaryHeader ReadHeader(ushort dictionaryAddress)
    {
        _memory.ValidateReadableRange(dictionaryAddress, 1);
        var separatorCount = _memory.ReadByte(dictionaryAddress);
        var headerLength = 1 + separatorCount + 1 + sizeof(ushort);
        _memory.ValidateReadableRange(dictionaryAddress, headerLength);

        var separatorsAddress = dictionaryAddress + 1;
        var separators = new HashSet<byte>();
        for (var index = 0; index < separatorCount; index++)
        {
            var separator = _memory.ReadByte(checked((ushort)(separatorsAddress + index)));
            if (separator == 32 || !_characters.IsDefinedForInput(separator) || !_characters.IsDefinedForOutput(separator))
            {
                throw new ZMachineRuntimeException($"Dictionary 0x{dictionaryAddress:x4} contains invalid word separator code {separator}.");
            }

            separators.Add(separator);
        }

        var entryLengthAddress = dictionaryAddress + 1 + separatorCount;
        var entryLength = _memory.ReadByte(checked((ushort)entryLengthAddress));
        var minimumEntryLength = _header.Version <= 3 ? 4 : 6;
        if (entryLength < minimumEntryLength)
        {
            throw new ZMachineRuntimeException($"Dictionary 0x{dictionaryAddress:x4} has entry length {entryLength}; version {_header.Version} requires at least {minimumEntryLength}.");
        }

        var rawCount = unchecked((short)_memory.ReadWord(checked((ushort)(entryLengthAddress + 1))));
        var count = Math.Abs((int)rawCount);
        var entriesAddress = entryLengthAddress + 1 + sizeof(ushort);
        var entriesEnd = (long)entriesAddress + ((long)count * entryLength);
        if (entriesEnd > _memory.StaticMemoryLimitExclusive)
        {
            throw new ZMachineRuntimeException($"Dictionary 0x{dictionaryAddress:x4} entries extend beyond addressable story memory.");
        }

        return new DictionaryHeader(entriesAddress, entryLength, count, rawCount >= 0, separators);
    }

    private int CompareEntry(int entryAddress, ReadOnlySpan<byte> encodedWord)
    {
        for (var index = 0; index < encodedWord.Length; index++)
        {
            var entryByte = _memory.ReadByte(checked((ushort)(entryAddress + index)));
            var comparison = entryByte.CompareTo(encodedWord[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private readonly record struct DictionaryHeader(
        int EntriesAddress,
        int EntryLength,
        int EntryCount,
        bool IsSorted,
        IReadOnlySet<byte> Separators);
}

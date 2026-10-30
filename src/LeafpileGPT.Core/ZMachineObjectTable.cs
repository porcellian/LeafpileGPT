namespace LeafpileGPT.Core;

/// <summary>Reads and mutates the version-specific object tree and property tables.</summary>
public sealed class ZMachineObjectTable
{
    private readonly ZMachineMemory _memory;
    private readonly byte _version;
    private readonly uint _objectsStart;
    private readonly int _entrySize;
    private readonly int _attributeCount;
    private readonly int _maximumPropertyNumber;

    public ZMachineObjectTable(ZMachineExecutionState state)
        : this((state ?? throw new ArgumentNullException(nameof(state))).Memory)
    {
    }

    public ZMachineObjectTable(ZMachineMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _version = memory.Header.Version;
        _entrySize = _version <= 3 ? 9 : 14;
        _attributeCount = _version <= 3 ? 32 : 48;
        _maximumPropertyNumber = _version <= 3 ? 31 : 63;
        _objectsStart = (uint)memory.Header.ObjectTableAddress + (uint)(_maximumPropertyNumber * sizeof(ushort));
        ValidateDynamicRange(memory.Header.ObjectTableAddress, checked(_maximumPropertyNumber * sizeof(ushort)));
    }

    public int AttributeCount => _attributeCount;

    public int MaximumPropertyNumber => _maximumPropertyNumber;

    public ushort GetParent(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadLink(objectNumber, parent: true);

    public ushort GetSibling(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadLink(objectNumber, sibling: true);

    public ushort GetChild(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadLink(objectNumber, child: true);

    public bool IsChildOf(ushort objectNumber, ushort parentObjectNumber) =>
        objectNumber != 0 && GetParent(objectNumber) == parentObjectNumber;

    public bool TestAttribute(ushort objectNumber, ushort attributeNumber)
    {
        if (objectNumber == 0 || attributeNumber >= _attributeCount)
        {
            return false;
        }

        var (entryAddress, _) = GetObjectEntry(objectNumber);
        var address = entryAddress + (uint)(attributeNumber / 8);
        var mask = 0x80 >> (attributeNumber % 8);
        return (_memory.ReadByte(checked((ushort)address)) & mask) != 0;
    }

    public void SetAttribute(ushort objectNumber, ushort attributeNumber, bool value)
    {
        // Sherlock sets and clears attribute 48 despite the 48-attribute V4+ layout.
        // Treat out-of-range attributes as absent so this known story bug cannot corrupt links.
        if (objectNumber == 0 || attributeNumber >= _attributeCount)
        {
            return;
        }

        var (entryAddress, _) = GetObjectEntry(objectNumber);
        var address = checked((ushort)(entryAddress + attributeNumber / 8));
        var mask = (byte)(0x80 >> (attributeNumber % 8));
        var current = _memory.ReadByte(address);
        _memory.WriteByte(address, value ? (byte)(current | mask) : (byte)(current & ~mask));
    }

    public void RemoveObject(ushort objectNumber)
    {
        if (objectNumber == 0)
        {
            return;
        }

        var parent = GetParent(objectNumber);
        if (parent != 0)
        {
            var child = GetChild(parent);
            ushort previousSibling = 0;
            var visited = new HashSet<ushort>();
            while (child != 0 && child != objectNumber)
            {
                if (!visited.Add(child))
                {
                    throw new ZMachineExecutionException("The object sibling chain contains a cycle.");
                }

                previousSibling = child;
                child = GetSibling(child);
            }

            if (child == objectNumber)
            {
                var nextSibling = GetSibling(objectNumber);
                if (previousSibling == 0)
                {
                    SetLink(parent, nextSibling, child: true);
                }
                else
                {
                    SetLink(previousSibling, nextSibling, sibling: true);
                }
            }
        }

        SetLink(objectNumber, 0, parent: true);
        SetLink(objectNumber, 0, sibling: true);
    }

    public void InsertObject(ushort objectNumber, ushort destinationObjectNumber)
    {
        if (objectNumber == 0)
        {
            return;
        }

        RemoveObject(objectNumber);
        if (destinationObjectNumber == 0)
        {
            return;
        }

        var oldFirstChild = GetChild(destinationObjectNumber);
        SetLink(objectNumber, oldFirstChild, sibling: true);
        SetLink(objectNumber, destinationObjectNumber, parent: true);
        SetLink(destinationObjectNumber, objectNumber, child: true);
    }

    public ushort GetProperty(ushort objectNumber, ushort propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber);
        if (property is null)
        {
            return ReadPropertyDefault(propertyNumber);
        }

        var propertyValue = property.Value.Value;
        if (propertyValue.Length > 2)
        {
            throw new ZMachineExecutionException(
                $"Property {propertyNumber} on object {objectNumber} has length {propertyValue.Length}; get_prop supports at most two bytes.");
        }

        return propertyValue.Length == 1
            ? _memory.ReadByte(checked((ushort)propertyValue.DataAddress))
            : _memory.ReadWord(checked((ushort)propertyValue.DataAddress));
    }

    public ushort GetPropertyAddress(ushort objectNumber, ushort propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber);
        return property is null ? (ushort)0 : checked((ushort)property.Value.Value.DataAddress);
    }

    public int GetNextProperty(ushort objectNumber, ushort propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber, allowZero: true);
        var properties = ReadProperties(objectNumber);
        if (propertyNumber == 0)
        {
            return properties.Count == 0 ? 0 : properties[0].Number;
        }

        var index = properties.FindIndex(property => property.Number == propertyNumber);
        if (index < 0)
        {
            throw new ZMachineExecutionException(
                $"Object {objectNumber} does not provide property {propertyNumber}.");
        }

        return index + 1 < properties.Count ? properties[index + 1].Number : 0;
    }

    public void SetProperty(ushort objectNumber, ushort propertyNumber, ushort value)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber)
            ?? throw new ZMachineExecutionException($"Object {objectNumber} does not provide property {propertyNumber}.");
        var propertyValue = property.Value;
        if (propertyValue.Length > 2)
        {
            throw new ZMachineExecutionException(
                $"Property {propertyNumber} on object {objectNumber} has length {propertyValue.Length}; put_prop supports at most two bytes.");
        }

        if (propertyValue.Length == 1)
        {
            _memory.WriteByte(checked((ushort)propertyValue.DataAddress), (byte)value);
        }
        else
        {
            _memory.WriteWord(checked((ushort)propertyValue.DataAddress), value);
        }
    }

    public int GetPropertyLength(ushort dataAddress)
    {
        if (dataAddress == 0)
        {
            return 0;
        }

        var headerAddress = (uint)dataAddress - 1;
        ValidateDynamicRange(headerAddress, 1);
        var first = _memory.ReadByte(checked((ushort)headerAddress));
        if (_version <= 3)
        {
            if (first == 0 || (first & 0x1F) == 0)
            {
                throw new ZMachineExecutionException($"Property size header at 0x{headerAddress:X4} is invalid.");
            }

            return (first >> 5) + 1;
        }

        if ((first & 0x80) == 0)
        {
            if ((first & 0x3F) == 0)
            {
                throw new ZMachineExecutionException($"Property size header at 0x{headerAddress:X4} has property number zero.");
            }

            return (first & 0x40) == 0 ? 1 : 2;
        }

        if (dataAddress < 2)
        {
            throw new ZMachineExecutionException($"Extended property size header at 0x{headerAddress:X4} is truncated.");
        }

        var extendedHeaderAddress = headerAddress - 1;
        ValidateDynamicRange(extendedHeaderAddress, 2);
        var extendedHeader = _memory.ReadByte(checked((ushort)extendedHeaderAddress));
        if ((extendedHeader & 0x80) == 0 || (extendedHeader & 0x3F) == 0)
        {
            throw new ZMachineExecutionException($"Extended property size header at 0x{extendedHeaderAddress:X4} is invalid.");
        }

        var length = first & 0x3F;
        return length == 0 ? 64 : length;
    }

    public string GetShortName(ushort objectNumber)
    {
        var tableAddress = GetPropertyTableAddress(objectNumber);
        var wordCount = _memory.ReadByte(tableAddress);
        var nameAddress = (uint)tableAddress + 1;
        var nameBytes = checked(wordCount * sizeof(ushort));
        ValidateDynamicRange(nameAddress, nameBytes);
        if (wordCount == 0)
        {
            return string.Empty;
        }

        var packedName = new byte[nameBytes];
        for (var index = 0; index < nameBytes; index++)
        {
            packedName[index] = _memory.ReadByte(checked((ushort)(nameAddress + (uint)index)));
        }

        return new ZMachineTextCodec(_memory).DecodePackedText(packedName);
    }

    private ushort ReadLink(ushort objectNumber, bool parent = false, bool sibling = false, bool child = false)
    {
        var (entryAddress, _) = GetObjectEntry(objectNumber);
        var offset = _version <= 3
            ? 4 + (parent ? 0 : sibling ? 1 : child ? 2 : throw new ArgumentException("A link type is required."))
            : 6 + (parent ? 0 : sibling ? 2 : child ? 4 : throw new ArgumentException("A link type is required."));
        var address = checked((ushort)(entryAddress + (uint)offset));
        return _version <= 3 ? _memory.ReadByte(address) : _memory.ReadWord(address);
    }

    private void SetLink(ushort objectNumber, ushort value, bool parent = false, bool sibling = false, bool child = false)
    {
        var (entryAddress, _) = GetObjectEntry(objectNumber);
        var offset = _version <= 3
            ? 4 + (parent ? 0 : sibling ? 1 : child ? 2 : throw new ArgumentException("A link type is required."))
            : 6 + (parent ? 0 : sibling ? 2 : child ? 4 : throw new ArgumentException("A link type is required."));
        var address = checked((ushort)(entryAddress + (uint)offset));
        if (_version <= 3)
        {
            if (value > byte.MaxValue)
            {
                throw new ZMachineExecutionException($"Object number {value} does not fit the Version 1–3 link fields.");
            }

            _memory.WriteByte(address, (byte)value);
        }
        else
        {
            _memory.WriteWord(address, value);
        }
    }

    private (uint Address, ushort PropertyTableAddress) GetObjectEntry(ushort objectNumber)
    {
        if (objectNumber == 0)
        {
            throw new ZMachineExecutionException("Object zero does not identify an object-table entry.");
        }

        if (_version <= 3 && objectNumber > byte.MaxValue)
        {
            throw new ZMachineExecutionException($"Object number {objectNumber} exceeds the Version 1–3 limit of 255.");
        }

        var address = _objectsStart + ((uint)objectNumber - 1) * (uint)_entrySize;
        ValidateDynamicRange(address, _entrySize);
        var propertyAddress = _memory.ReadWord(checked((ushort)(address + (uint)(_entrySize - sizeof(ushort)))));
        ValidatePropertyTable(propertyAddress);
        return (address, propertyAddress);
    }

    private ushort GetPropertyTableAddress(ushort objectNumber) => GetObjectEntry(objectNumber).PropertyTableAddress;

    private Property? FindProperty(ushort objectNumber, ushort propertyNumber)
    {
        foreach (var property in ReadProperties(objectNumber))
        {
            if (property.Number == propertyNumber)
            {
                return property;
            }
        }

        return null;
    }

    private List<Property> ReadProperties(ushort objectNumber)
    {
        var tableAddress = GetPropertyTableAddress(objectNumber);
        var wordCount = _memory.ReadByte(tableAddress);
        var cursor = (uint)tableAddress + 1 + (uint)wordCount * sizeof(ushort);
        ValidateDynamicRange(tableAddress, checked(1 + wordCount * sizeof(ushort)));
        var properties = new List<Property>();
        var seenNumbers = new HashSet<int>();
        while (true)
        {
            ValidateDynamicRange(cursor, 1);
            var first = _memory.ReadByte(checked((ushort)cursor));
            if (first == 0)
            {
                // Valid story files store properties in descending order, but
                // legacy Infocom data can contain unordered or duplicate
                // entries. Keep the first declared value for each property
                // and expose a canonical sequence to get_next_prop.
                return properties.OrderByDescending(property => property.Number).ToList();
            }

            cursor++;
            int number;
            int length;
            if (_version <= 3)
            {
                number = first & 0x1F;
                length = (first >> 5) + 1;
                if (number == 0)
                {
                    throw new ZMachineExecutionException($"Invalid Version 1–3 property header at 0x{cursor - 1:X4}.");
                }
            }
            else
            {
                number = first & 0x3F;
                if ((first & 0x80) != 0)
                {
                    ValidateDynamicRange(cursor, 1);
                    var second = _memory.ReadByte(checked((ushort)cursor++));
                    if ((second & 0x80) == 0 || number == 0)
                    {
                        throw new ZMachineExecutionException($"Invalid extended property header at 0x{cursor - 2:X4}.");
                    }

                    length = second & 0x3F;
                    if (length == 0)
                    {
                        length = 64;
                    }
                }
                else
                {
                    if (number == 0)
                    {
                        throw new ZMachineExecutionException($"Invalid property number zero at 0x{cursor - 1:X4}.");
                    }

                    length = (first & 0x40) == 0 ? 1 : 2;
                }
            }

            ValidateDynamicRange(cursor, length);
            if (seenNumbers.Add(number))
            {
                properties.Add(new(number, new(cursor, length)));
            }

            cursor += (uint)length;
        }
    }

    private ushort ReadPropertyDefault(ushort propertyNumber)
    {
        var address = (uint)_memory.Header.ObjectTableAddress + ((uint)propertyNumber - 1) * sizeof(ushort);
        ValidateDynamicRange(address, sizeof(ushort));
        return _memory.ReadWord(checked((ushort)address));
    }

    private void ValidatePropertyNumber(ushort propertyNumber, bool allowZero = false)
    {
        if (propertyNumber == 0 && allowZero)
        {
            return;
        }

        if (propertyNumber == 0 || propertyNumber > _maximumPropertyNumber)
        {
            throw new ZMachineExecutionException(
                $"Property number {propertyNumber} is outside the Version {_version} range 1 through {_maximumPropertyNumber}.");
        }
    }

    private void ValidatePropertyTable(ushort tableAddress)
    {
        if (tableAddress == 0)
        {
            throw new ZMachineExecutionException("An object has a zero property-table address.");
        }

        ValidateDynamicRange(tableAddress, 1);
    }

    private void ValidateDynamicRange(uint address, int size)
    {
        if (size < 0 || (ulong)address + (uint)size > _memory.Header.StaticMemoryBase)
        {
            throw new ZMachineExecutionException(
                $"Object table range 0x{address:X}–0x{(ulong)address + (uint)size:X} lies outside dynamic memory.");
        }

        _memory.ValidateReadableRange(address, size);
    }

    private readonly record struct Property(int Number, PropertyValue Value);

    private readonly record struct PropertyValue(uint DataAddress, int Length);
}

using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Reads and updates the versioned object and property tables in dynamic memory.</summary>
public sealed class ZMachineObjectTable : IZMachineObjectQueries
{
    private readonly ZMachineState _state;
    private readonly int _entryStart;
    private readonly int _entrySize;
    private readonly int _attributeByteCount;
    private readonly int _maximumAttribute;
    private readonly int _propertyCount;

    public ZMachineObjectTable(ZMachineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        var version = state.Header.Version;
        _entrySize = version <= 3 ? 9 : 14;
        _attributeByteCount = version <= 3 ? 4 : 6;
        _maximumAttribute = version <= 3 ? 31 : 47;
        _propertyCount = version <= 3 ? 31 : 63;
        _entryStart = state.Header.ObjectTableAddress + (_propertyCount * sizeof(ushort));
    }

    public bool IsChild(ushort child, ushort parent) => child != 0 && GetParent(child) == parent;

    public bool HasAttribute(ushort objectNumber, ushort attribute)
    {
        if (objectNumber == 0)
        {
            return false;
        }

        ValidateAttribute(attribute);
        var entryAddress = GetObjectEntryAddress(objectNumber);
        var attributeAddress = checked((ushort)(entryAddress + (attribute / 8)));
        var mask = 0x80 >> (attribute % 8);
        return (_state.Memory.ReadByte(attributeAddress) & mask) != 0;
    }

    public void SetAttribute(ushort objectNumber, ushort attribute) => UpdateAttribute(objectNumber, attribute, set: true);

    public void ClearAttribute(ushort objectNumber, ushort attribute) => UpdateAttribute(objectNumber, attribute, set: false);

    public ushort GetSibling(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadObjectLink(objectNumber, linkIndex: 1);

    public ushort GetChild(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadObjectLink(objectNumber, linkIndex: 2);

    public ushort GetParent(ushort objectNumber) => objectNumber == 0 ? (ushort)0 : ReadObjectLink(objectNumber, linkIndex: 0);

    public void InsertObject(ushort objectNumber, ushort destination)
    {
        ValidateRequiredObject(objectNumber);
        ValidateRequiredObject(destination);
        if (objectNumber == destination)
        {
            throw new ZMachineRuntimeException("An object cannot be inserted into itself.");
        }

        EnsureNoAncestorCycle(objectNumber, destination);
        RemoveObject(objectNumber);

        _ = GetObjectEntryAddress(destination);
        var oldFirstChild = ReadObjectLink(destination, linkIndex: 2);
        WriteObjectLink(objectNumber, linkIndex: 0, destination);
        WriteObjectLink(objectNumber, linkIndex: 1, oldFirstChild);
        WriteObjectLink(destination, linkIndex: 2, objectNumber);
    }

    public void RemoveObject(ushort objectNumber)
    {
        ValidateRequiredObject(objectNumber);
        var parent = GetParent(objectNumber);
        var sibling = GetSibling(objectNumber);
        if (parent != 0)
        {
            var previous = (ushort)0;
            var current = GetChild(parent);
            var visited = new HashSet<ushort>();
            while (current != 0 && current != objectNumber)
            {
                if (!visited.Add(current))
                {
                    throw new ZMachineRuntimeException($"The child list of object {parent} contains a cycle.");
                }

                previous = current;
                current = GetSibling(current);
            }

            if (current != objectNumber)
            {
                throw new ZMachineRuntimeException($"Object {objectNumber} names parent {parent}, but is absent from that parent's child list.");
            }

            if (previous == 0)
            {
                WriteObjectLink(parent, linkIndex: 2, sibling);
            }
            else
            {
                WriteObjectLink(previous, linkIndex: 1, sibling);
            }
        }

        WriteObjectLink(objectNumber, linkIndex: 0, 0);
        WriteObjectLink(objectNumber, linkIndex: 1, 0);
    }

    public ushort GetProperty(ushort objectNumber, ushort propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber);
        if (property is null)
        {
            var defaultAddress = checked((ushort)(_state.Header.ObjectTableAddress + ((propertyNumber - 1) * sizeof(ushort))));
            return _state.Memory.ReadWord(defaultAddress);
        }

        if (property.Value.Length == 1)
        {
            return _state.Memory.ReadByte(checked((ushort)property.Value.DataAddress));
        }

        if (property.Value.Length == 2)
        {
            return _state.Memory.ReadWord(checked((ushort)property.Value.DataAddress));
        }

        throw new ZMachineRuntimeException($"Property {propertyNumber} of object {objectNumber} has length {property.Value.Length}; get_prop supports only one- or two-byte properties.");
    }

    public ushort GetPropertyAddress(ushort objectNumber, ushort propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber);
        return property is null ? (ushort)0 : checked((ushort)property.Value.DataAddress);
    }

    public ushort GetNextProperty(ushort objectNumber, ushort propertyNumber)
    {
        if (propertyNumber != 0)
        {
            ValidatePropertyNumber(propertyNumber);
        }

        var properties = ReadProperties(objectNumber);
        if (propertyNumber == 0)
        {
            return properties.Count == 0 ? (ushort)0 : properties[0].Number;
        }

        for (var index = 0; index < properties.Count; index++)
        {
            if (properties[index].Number == propertyNumber)
            {
                return index + 1 < properties.Count ? properties[index + 1].Number : (ushort)0;
            }
        }

        throw new ZMachineRuntimeException($"Property {propertyNumber} does not exist on object {objectNumber}.");
    }

    public ushort GetPropertyLength(ushort propertyAddress)
    {
        if (propertyAddress == 0)
        {
            return 0;
        }

        _state.Memory.ValidateDynamicRange(propertyAddress, 1);
        if (_state.Header.Version <= 3)
        {
            if (propertyAddress == 0)
            {
                throw new ZMachineRuntimeException("A property data address has no preceding size byte.");
            }

            var size = _state.Memory.ReadByte((ushort)(propertyAddress - 1));
            if ((size & 0x1f) == 0)
            {
                throw new ZMachineRuntimeException($"Address 0x{propertyAddress:x4} does not follow a valid V1–V3 property header.");
            }

            return (ushort)(((size >> 5) & 0x07) + 1);
        }

        if (propertyAddress >= 2)
        {
            var first = _state.Memory.ReadByte((ushort)(propertyAddress - 2));
            var second = _state.Memory.ReadByte((ushort)(propertyAddress - 1));
            if ((first & 0x80) != 0 && (second & 0x80) != 0)
            {
                var longLength = second & 0x3f;
                return (ushort)(longLength == 0 ? 64 : longLength);
            }
        }

        if (propertyAddress < 1)
        {
            throw new ZMachineRuntimeException("A property data address has no preceding size byte.");
        }

        var shortHeader = _state.Memory.ReadByte((ushort)(propertyAddress - 1));
        if ((shortHeader & 0x80) != 0 || (shortHeader & 0x3f) == 0)
        {
            throw new ZMachineRuntimeException($"Address 0x{propertyAddress:x4} does not follow a valid V4+ property header.");
        }

        return (ushort)((shortHeader & 0x40) != 0 ? 2 : 1);
    }

    public void SetProperty(ushort objectNumber, ushort propertyNumber, ushort value)
    {
        ValidatePropertyNumber(propertyNumber);
        var property = FindProperty(objectNumber, propertyNumber);
        if (property is null)
        {
            throw new ZMachineRuntimeException($"Property {propertyNumber} does not exist on object {objectNumber}.");
        }

        switch (property.Value.Length)
        {
            case 1:
                _state.Memory.WriteByte(checked((ushort)property.Value.DataAddress), (byte)value);
                break;
            case 2:
                _state.Memory.WriteWord(checked((ushort)property.Value.DataAddress), value);
                break;
            default:
                throw new ZMachineRuntimeException($"Property {propertyNumber} of object {objectNumber} has length {property.Value.Length}; put_prop supports only one- or two-byte properties.");
        }
    }

    public string GetShortName(ushort objectNumber)
    {
        var tableAddress = GetPropertyTableAddress(objectNumber);
        var textLength = _state.Memory.ReadByte(tableAddress);
        var textAddress = checked(tableAddress + 1);
        return textLength == 0 ? string.Empty : _state.Text.Decode(textAddress);
    }

    private void UpdateAttribute(ushort objectNumber, ushort attribute, bool set)
    {
        ValidateAttribute(attribute);
        // Object 0 is the null object. The standard recommends configurable
        // handling for illegal object-0 operations; silently ignoring this
        // mutation is the compatibility behavior used by classic Infocom games.
        if (objectNumber == 0)
        {
            return;
        }

        ValidateRequiredObject(objectNumber);
        var entryAddress = GetObjectEntryAddress(objectNumber);
        var address = checked((ushort)(entryAddress + (attribute / 8)));
        var mask = (byte)(0x80 >> (attribute % 8));
        var current = _state.Memory.ReadByte(address);
        _state.Memory.WriteByte(address, set ? (byte)(current | mask) : (byte)(current & ~mask));
    }

    private void ValidateAttribute(ushort attribute)
    {
        if (attribute > _maximumAttribute)
        {
            throw new ZMachineRuntimeException($"Attribute {attribute} is outside the valid range 0–{_maximumAttribute} for version {_state.Header.Version}.");
        }
    }

    private void ValidatePropertyNumber(ushort propertyNumber)
    {
        if (propertyNumber == 0 || propertyNumber > _propertyCount)
        {
            throw new ZMachineRuntimeException($"Property number {propertyNumber} is outside the valid range 1–{_propertyCount} for version {_state.Header.Version}.");
        }
    }

    private void ValidateRequiredObject(ushort objectNumber)
    {
        if (objectNumber == 0)
        {
            throw new ZMachineRuntimeException("Object 0 is the null object and cannot be mutated or queried for properties.");
        }

        _ = GetPropertyTableAddress(objectNumber);
    }

    private int GetObjectEntryAddress(ushort objectNumber)
    {
        if (objectNumber == 0)
        {
            throw new ZMachineRuntimeException("Object 0 is the null object and has no object-table entry.");
        }

        if (_state.Header.Version <= 3 && objectNumber > byte.MaxValue)
        {
            throw new ZMachineRuntimeException($"Object number {objectNumber} exceeds the V1–V3 limit of 255.");
        }

        var address = (long)_entryStart + ((long)(objectNumber - 1) * _entrySize);
        if (address > ushort.MaxValue || address + _entrySize > _state.Memory.DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException($"Object {objectNumber} has no complete entry in dynamic memory.");
        }

        _state.Memory.ValidateDynamicRange((ushort)address, _entrySize);
        return (int)address;
    }

    private ushort ReadObjectLink(ushort objectNumber, int linkIndex)
    {
        var address = GetObjectEntryAddress(objectNumber) + _attributeByteCount + (linkIndex * (_state.Header.Version <= 3 ? 1 : 2));
        ValidateObjectEntryPropertyTable(objectNumber);
        return _state.Header.Version <= 3
            ? _state.Memory.ReadByte(checked((ushort)address))
            : _state.Memory.ReadWord(checked((ushort)address));
    }

    private void WriteObjectLink(ushort objectNumber, int linkIndex, ushort value)
    {
        var address = GetObjectEntryAddress(objectNumber) + _attributeByteCount + (linkIndex * (_state.Header.Version <= 3 ? 1 : 2));
        ValidateObjectEntryPropertyTable(objectNumber);
        if (_state.Header.Version <= 3)
        {
            if (value > byte.MaxValue)
            {
                throw new ZMachineRuntimeException($"Object link value {value} cannot be stored in a V1–V3 object entry.");
            }

            _state.Memory.WriteByte(checked((ushort)address), (byte)value);
        }
        else
        {
            _state.Memory.WriteWord(checked((ushort)address), value);
        }
    }

    private ushort GetPropertyTableAddress(ushort objectNumber)
    {
        var entryAddress = GetObjectEntryAddress(objectNumber);
        var propertyAddress = _state.Memory.ReadWord(checked((ushort)(entryAddress + _entrySize - sizeof(ushort))));
        if (propertyAddress < 64 || propertyAddress >= _state.Memory.DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException($"Object {objectNumber} has an invalid property-table address 0x{propertyAddress:x4}.");
        }

        var textWords = _state.Memory.ReadByte(propertyAddress);
        var propertyStart = (long)propertyAddress + 1 + ((long)textWords * 2);
        if (propertyStart >= _state.Memory.DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException($"Object {objectNumber} has a short name that extends beyond dynamic memory.");
        }

        _state.Memory.ValidateDynamicRange(checked((ushort)propertyAddress), checked((int)(propertyStart - propertyAddress + 1)));
        return propertyAddress;
    }

    private void ValidateObjectEntryPropertyTable(ushort objectNumber) => _ = GetPropertyTableAddress(objectNumber);

    private void EnsureNoAncestorCycle(ushort objectNumber, ushort destination)
    {
        var current = destination;
        var visited = new HashSet<ushort>();
        while (current != 0)
        {
            if (current == objectNumber)
            {
                throw new ZMachineRuntimeException($"Inserting object {objectNumber} into object {destination} would create a parent cycle.");
            }

            if (!visited.Add(current))
            {
                throw new ZMachineRuntimeException("The object parent chain contains a cycle.");
            }

            current = GetParent(current);
        }
    }

    private PropertyEntry? FindProperty(ushort objectNumber, ushort propertyNumber)
    {
        if (objectNumber == 0)
        {
            return null;
        }

        foreach (var property in ReadProperties(objectNumber))
        {
            if (property.Number == propertyNumber)
            {
                return property;
            }

            if (property.Number < propertyNumber)
            {
                break;
            }
        }

        return null;
    }

    private List<PropertyEntry> ReadProperties(ushort objectNumber)
    {
        var tableAddress = GetPropertyTableAddress(objectNumber);
        var textWords = _state.Memory.ReadByte(tableAddress);
        var address = checked(tableAddress + 1 + (textWords * 2));
        var properties = new List<PropertyEntry>();
        var previousNumber = _propertyCount + 1;
        while (true)
        {
            EnsureDynamicAddress(address, 1, $"property header for object {objectNumber}");
            var first = _state.Memory.ReadByte((ushort)address);
            if (first == 0)
            {
                return properties;
            }

            int number;
            int length;
            int headerLength;
            if (_state.Header.Version <= 3)
            {
                number = first & 0x1f;
                length = ((first >> 5) & 0x07) + 1;
                headerLength = 1;
                if (number == 0)
                {
                    throw new ZMachineRuntimeException($"Object {objectNumber} has an illegal V1–V3 property header 0x{first:x2}.");
                }
            }
            else
            {
                number = first & 0x3f;
                if ((first & 0x80) != 0)
                {
                    EnsureDynamicAddress(address + 1, 1, $"second property header byte for object {objectNumber}");
                    var second = _state.Memory.ReadByte((ushort)(address + 1));
                    if ((second & 0x80) == 0 || number == 0)
                    {
                        throw new ZMachineRuntimeException($"Object {objectNumber} has an illegal V4+ long property header at 0x{address:x4}.");
                    }

                    length = second & 0x3f;
                    length = length == 0 ? 64 : length;
                    headerLength = 2;
                }
                else
                {
                    if (number == 0)
                    {
                        throw new ZMachineRuntimeException($"Object {objectNumber} has an illegal V4+ property header 0x{first:x2}.");
                    }

                    length = (first & 0x40) != 0 ? 2 : 1;
                    headerLength = 1;
                }
            }

            var repeatedInfocomV3Property = _state.Header.Version == 3 && number == previousNumber;
            var repeatedLegacyProperty31 = _state.Header.Version < 3 && number == 31 && number == previousNumber;
            if (number > previousNumber ||
                (number == previousNumber && !repeatedInfocomV3Property && !repeatedLegacyProperty31))
            {
                throw new ZMachineRuntimeException($"Object {objectNumber} properties are not in descending numerical order.");
            }

            var dataAddress = address + headerLength;
            EnsureDynamicAddress(dataAddress, length, $"property {number} data for object {objectNumber}");
            // Some Infocom V3 stories contain duplicate property entries. Keep the first
            // (highest-positioned) definition and skip later duplicates. Legacy V1-V2
            // property 31 retains its existing compatibility exception.
            if (number < previousNumber)
            {
                properties.Add(new PropertyEntry((ushort)number, dataAddress, (ushort)length));
            }

            previousNumber = number;
            address = dataAddress + length;
        }
    }

    private void EnsureDynamicAddress(int address, int width, string description)
    {
        if (address < 0 || address > ushort.MaxValue || width <= 0 || (long)address + width > _state.Memory.DynamicMemoryLength)
        {
            throw new ZMachineRuntimeException($"The {description} at 0x{address:x} is outside dynamic memory.");
        }
    }

    private readonly record struct PropertyEntry(ushort Number, int DataAddress, ushort Length);
}

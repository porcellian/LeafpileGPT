namespace Leafpile.Core.Machine;

/// <summary>Object-table operations used by Z-machine opcodes.</summary>
public interface IZMachineObjectQueries
{
    bool IsChild(ushort child, ushort parent);

    bool HasAttribute(ushort objectNumber, ushort attribute);

    void SetAttribute(ushort objectNumber, ushort attribute);

    void ClearAttribute(ushort objectNumber, ushort attribute);

    ushort GetSibling(ushort objectNumber);

    ushort GetChild(ushort objectNumber);

    ushort GetParent(ushort objectNumber);

    void InsertObject(ushort objectNumber, ushort destination);

    void RemoveObject(ushort objectNumber);

    ushort GetProperty(ushort objectNumber, ushort propertyNumber);

    ushort GetPropertyAddress(ushort objectNumber, ushort propertyNumber);

    ushort GetNextProperty(ushort objectNumber, ushort propertyNumber);

    ushort GetPropertyLength(ushort propertyAddress);

    void SetProperty(ushort objectNumber, ushort propertyNumber, ushort value);

    string GetShortName(ushort objectNumber);
}

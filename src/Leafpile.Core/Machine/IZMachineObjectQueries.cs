namespace Leafpile.Core.Machine;

/// <summary>Object-table lookups needed by control-flow opcodes; mutation is implemented in Task 09.</summary>
public interface IZMachineObjectQueries
{
    bool IsChild(ushort child, ushort parent);

    bool HasAttribute(ushort objectNumber, ushort attribute);

    ushort GetSibling(ushort objectNumber);

    ushort GetChild(ushort objectNumber);
}

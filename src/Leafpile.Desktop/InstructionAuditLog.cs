using System.Globalization;
using System.Text;
using Leafpile.Core.Machine;

namespace Leafpile.Desktop;

internal sealed class InstructionAuditLog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private readonly HashSet<int> _endedRuns = [];
    private int _runNumber;

    public int Count
    {
        get { lock (_gate) return _lines.Count; }
    }

    public InstructionAuditSession StartRun(string storyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyName);
        lock (_gate)
        {
            var session = new InstructionAuditSession(++_runNumber);
            _lines.Add(string.Empty);
            _lines.Add($"========== Run {session.RunNumber}: {storyName} | {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ==========");
            _lines.Add("Startup instructions");
            return session;
        }
    }

    public void BeginCommand(InstructionAuditSession session, string command)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            session.Command = Escape(command);
            _lines.Add(string.Empty);
            _lines.Add($"> {session.Command}");
            _lines.Add(string.Empty);
        }
    }

    public void Record(InstructionAuditSession session, ZMachineInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(instruction);
        lock (_gate)
        {
            var operands = string.Join(", ", instruction.Operands.Select(FormatOperand));
            var mnemonic = ZMachineOpcodeNames.GetMnemonic(instruction.OpcodeClass, instruction.Opcode);
            var form = instruction.Form switch
            {
                ZMachineInstructionForm.Variable => "Var",
                ZMachineInstructionForm.Extended => "Ext",
                _ => instruction.Form.ToString()
            };
            var line = string.Create(CultureInfo.InvariantCulture,
                $"#{++session.Sequence:000000}   PC=0x{instruction.Address:X5}   Form={form,-5}   OP={instruction.Opcode,2}   {mnemonic,-15} [{operands}]");
            _lines.Add(line);
        }
    }

    public void MarkRunEnded(InstructionAuditSession session, string reason)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            if (!_endedRuns.Add(session.RunNumber)) return;
            _lines.Add(string.Empty);
            _lines.Add($"[Run {session.RunNumber} ended: {Escape(reason)}]");
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _lines.Add("[Instruction history cleared. Sequence numbers continue within the active run.]");
        }
    }

    public string[] SnapshotFrom(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index > _lines.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _lines.Skip(index).ToArray();
        }
    }

    public string ExportText()
    {
        lock (_gate) return string.Join(Environment.NewLine, _lines) + Environment.NewLine;
    }

    private static string FormatOperand(ZMachineOperand operand)
    {
        var type = operand.Type switch
        {
            ZMachineOperandType.LargeConstant => "lg",
            ZMachineOperandType.SmallConstant => "sm",
            ZMachineOperandType.Variable => "var",
            _ => "unknown"
        };
        return $"{type}:0x{operand.EncodedValue:x4}";
    }

    private static string Escape(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

internal sealed class InstructionAuditSession(int runNumber)
{
    public int RunNumber { get; } = runNumber;
    public int Sequence { get; set; }
    public string? Command { get; set; }
}

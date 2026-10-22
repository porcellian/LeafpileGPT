using Leafpile.Core.Machine;
using Leafpile.Desktop;

namespace Leafpile.Tests.Desktop;

public sealed class InstructionAuditLogTests
{
    [Fact]
    public void AuditRecordsOrderedInstructionDetailsUnderStartupAndCommandHeadings()
    {
        var log = new InstructionAuditLog();
        var session = log.StartRun("fixture.z3");
        log.Record(session, CreateInstruction(0x0200, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));
        log.BeginCommand(session, "look");
        log.Record(session, CreateInstruction(0x0201, ZMachineInstructionForm.Variable, ZMachineOpcodeClass.Variable, 4,
            new ZMachineOperand(ZMachineOperandType.SmallConstant, 10)));

        var text = log.ExportText();

        Assert.Contains("Run 1: fixture.z3", text);
        Assert.Contains("Startup instructions", text);
        Assert.Contains("#000001   PC=0x00200   Form=Short      Opcode= 4   nop              []", text);
        Assert.Contains("#000002   PC=0x00201   Form=Variable   Opcode= 4   read             [small:0x000a]", text);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.None);
        var commandLine = Array.IndexOf(lines, "> look");
        Assert.True(commandLine > 0);
        Assert.Equal(string.Empty, lines[commandLine - 1]);
        Assert.Equal(string.Empty, lines[commandLine + 1]);
    }

    [Fact]
    public void NewRunsHaveIndependentInstructionSequencesAndClearPreservesCurrentSequence()
    {
        var log = new InstructionAuditLog();
        var first = log.StartRun("first.z3");
        log.Record(first, CreateInstruction(0x0200, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));
        var second = log.StartRun("second.z3");
        log.Record(second, CreateInstruction(0x0300, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));

        Assert.Contains("#000001   PC=0x00200", log.ExportText());
        Assert.Contains("#000001   PC=0x00300", log.ExportText());

        log.Clear();
        log.Record(second, CreateInstruction(0x0301, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));

        var clearedText = log.ExportText();
        Assert.DoesNotContain("first.z3", clearedText);
        Assert.Contains("history cleared", clearedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#000002   PC=0x00301", clearedText);
    }

    [Fact]
    public void TextExportIncludesRunEndMarkersAndUsesOneLinePerInstruction()
    {
        var log = new InstructionAuditLog();
        var session = log.StartRun("fixture.z5");
        log.BeginCommand(session, "open mailbox");
        log.Record(session, CreateInstruction(0x0240, ZMachineInstructionForm.Long, ZMachineOpcodeClass.TwoOperand, 1,
            new ZMachineOperand(ZMachineOperandType.SmallConstant, 12),
            new ZMachineOperand(ZMachineOperandType.Variable, 16)));
        log.MarkRunEnded(session, "story halted");

        var lines = log.ExportText().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(lines, line => line == "> open mailbox");
        Assert.Contains(lines, line => line == "#000001   PC=0x00240   Form=Long       Opcode= 1   je               [small:0x000c, variable:0x0010]");
        Assert.DoesNotContain(lines, line => line.Contains("operands=", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "[Run 1 ended: story halted]");
    }

    private static ZMachineInstruction CreateInstruction(
        int address,
        ZMachineInstructionForm form,
        ZMachineOpcodeClass opcodeClass,
        int opcode,
        params ZMachineOperand[] operands) =>
        new(address, address + 1, form, opcodeClass, opcode, operands.Length, operands, null, null, null);
}

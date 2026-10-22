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
        Assert.Contains("Run 1 command: > look", text);
        Assert.Contains("PC=0x0200  Short/ZeroOperand  opcode= 4  nop", text);
        Assert.Contains("PC=0x0201  Variable/Variable  opcode= 4  read  operands=[small:0x000a]", text);
        Assert.Contains("Run=1  Group=Startup  Seq=       1  PC=0x0200", text);
        Assert.Contains("Run=1  Group=> look  Seq=       2  PC=0x0201", text);
    }

    [Fact]
    public void NewRunsHaveIndependentInstructionSequencesAndClearPreservesCurrentSequence()
    {
        var log = new InstructionAuditLog();
        var first = log.StartRun("first.z3");
        log.Record(first, CreateInstruction(0x0200, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));
        var second = log.StartRun("second.z3");
        log.Record(second, CreateInstruction(0x0300, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));

        Assert.Contains("Run=1  Group=Startup  Seq=       1  PC=0x0200", log.ExportText());
        Assert.Contains("Run=2  Group=Startup  Seq=       1  PC=0x0300", log.ExportText());

        log.Clear();
        log.Record(second, CreateInstruction(0x0301, ZMachineInstructionForm.Short, ZMachineOpcodeClass.ZeroOperand, 4));

        var clearedText = log.ExportText();
        Assert.DoesNotContain("first.z3", clearedText);
        Assert.Contains("history cleared", clearedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run=2  Group=Startup  Seq=       2  PC=0x0301", clearedText);
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

        Assert.Contains(lines, line => line == "Run 1 command: > open mailbox");
        Assert.Contains(lines, line => line.Contains("Long/TwoOperand  opcode= 1  je", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("operands=[small:0x000c, variable:0x0010]", StringComparison.Ordinal));
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

using System.Globalization;
using System.Text;
using System.Text.Json;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Exports an instruction audit as a readable PDF and structured JSON Lines file.</summary>
public static class InstructionAuditExporter
{
    private const int PageLineCapacity = 51;
    private const int WrapWidth = 102;

    public static async Task<(string PdfPath, string JsonLinesPath)> ExportAsync(
        string basePath,
        string storyName,
        ZMachineInstructionAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyName);
        ArgumentNullException.ThrowIfNull(audit);

        var fullBasePath = Path.GetFullPath(basePath);
        var directory = Path.GetDirectoryName(fullBasePath)
            ?? throw new ArgumentException("The audit filename must include a parent directory.", nameof(basePath));
        Directory.CreateDirectory(directory);
        var stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(fullBasePath));
        var pdfPath = stem + ".pdf";
        var jsonLinesPath = stem + ".jsonl";
        var groups = audit.GetSnapshot();

        await File.WriteAllBytesAsync(pdfPath, CreatePdf(storyName, groups), cancellationToken).ConfigureAwait(false);
        await using (var stream = new FileStream(jsonLinesPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "command",
                    group = group.Number,
                    command = group.Command,
                    startedAt = group.StartedAt
                })).ConfigureAwait(false);
                foreach (var instruction in group.Instructions)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        type = "instruction",
                        group = group.Number,
                        sequence = instruction.Sequence,
                        programCounter = FormatProgramCounter(instruction.ProgramCounter),
                        form = instruction.Form.ToString(),
                        opcodeClass = instruction.OpcodeClass.ToString(),
                        instruction.Opcode,
                        instruction.Mnemonic,
                        instruction.Operands,
                        instruction.StoreVariable,
                        instruction.Branch,
                        nextProgramCounter = FormatProgramCounter(instruction.NextProgramCounter),
                        programCounterAfter = FormatProgramCounter(instruction.ProgramCounterAfter),
                        isHalted = instruction.IsHalted
                    })).ConfigureAwait(false);
                }
            }
        }

        return (pdfPath, jsonLinesPath);
    }

    private static byte[] CreatePdf(string storyName, IReadOnlyList<ZMachineInstructionAuditGroup> groups)
    {
        var lines = new List<(string Text, bool Heading)>
        {
            ($"Story: {storyName}", true),
            ($"Generated: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}", false),
            ($"Instructions captured: {groups.Sum(group => group.Instructions.Length)}", false),
            (string.Empty, false)
        };
        foreach (var group in groups)
        {
            lines.Add(($"COMMAND {group.Number}: {group.Command}", true));
            lines.Add(($"Started: {group.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}", false));
            foreach (var instruction in group.Instructions)
            {
                var operands = string.Join(", ", instruction.Operands.Select(FormatOperand));
                var suffix = instruction.StoreVariable is { } store ? $" -> store {store:X2}" : string.Empty;
                if (instruction.Branch is { } branch)
                {
                    suffix += branch.TargetAddress is { } target
                        ? $"; branch {(branch.BranchOnTrue ? "true" : "false")} -> 0x{target:X6}"
                        : $"; branch returns {branch.ReturnValue}";
                }

                var result = instruction.IsHalted ? " [halted]" : string.Empty;
                lines.Add(($"{instruction.Sequence,7} {FormatProgramCounter(instruction.ProgramCounter),-8} {instruction.Form}/{instruction.OpcodeClass}:{instruction.Opcode:X2} {instruction.Mnemonic} {operands}{suffix} -> {FormatProgramCounter(instruction.ProgramCounterAfter)}{result}", false));
            }

            lines.Add((string.Empty, false));
        }

        var wrappedLines = lines.SelectMany(line => Wrap(line.Text, WrapWidth)
            .Select((text, index) => (Text: text, Heading: line.Heading && index == 0))).ToArray();
        var pages = wrappedLines.Chunk(PageLineCapacity).ToArray();
        if (pages.Length == 0)
        {
            pages = [[]];
        }

        return BuildPdf(pages);
    }

    private static string FormatProgramCounter(uint address) => $"0x{address:X6}";

    private static string FormatOperand(DecodedOperand operand) => operand.Encoding switch
    {
        OperandEncoding.Variable => $"var[{operand.Value:X2}]",
        OperandEncoding.SmallConstant => $"#{operand.Value:X2}",
        OperandEncoding.LargeConstant => $"#{operand.Value:X4}",
        _ => operand.Value.ToString(CultureInfo.InvariantCulture)
    };

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (value.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var offset = 0;
        while (offset < value.Length)
        {
            var length = Math.Min(width, value.Length - offset);
            if (offset + length < value.Length)
            {
                var breakAt = value.LastIndexOf(' ', offset + length - 1, length);
                if (breakAt > offset)
                {
                    length = breakAt - offset;
                }
            }

            yield return value.Substring(offset, length);
            offset += length;
            while (offset < value.Length && value[offset] == ' ')
            {
                offset++;
            }
        }
    }

    private static byte[] BuildPdf(IReadOnlyList<(string Text, bool Heading)[]> pages)
    {
        var objects = new List<byte[]>();
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageReferences = string.Join(" ", Enumerable.Range(0, pages.Count).Select(index => $"{5 + index * 2} 0 R"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{pageReferences}] /Count {pages.Count} >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold >>"));

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var pageObjectNumber = 5 + pageIndex * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            var content = new StringBuilder("BT\n");
            foreach (var (line, index) in pages[pageIndex].Select((item, index) => (item, index)))
            {
                var font = line.Heading ? "/F2 8 Tf" : "/F1 8 Tf";
                var y = 750 - (index * 13);
                content.Append(font).Append("\n0 0 0 rg\n1 0 0 1 42 ").Append(y)
                    .Append(" Tm (").Append(EscapePdfString(ToAscii(line.Text))).Append(") Tj\n");
            }

            content.Append("ET\nBT\n/F1 8 Tf\n1 0 0 1 42 24 Tm (LeafpileGPT Instruction Audit) Tj\n")
                .Append("1 0 0 1 510 24 Tm (Page ").Append(pageIndex + 1).Append(" of ").Append(pages.Count).Append(") Tj\nET");
            var contentBytes = Ascii(content.ToString());
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectNumber} 0 R >>"));
            objects.Add(Join(
                Ascii($"<< /Length {contentBytes.Length} >>\nstream\n"),
                contentBytes,
                Ascii("\nendstream")));
        }

        using var output = new MemoryStream();
        Write(output, Ascii("%PDF-1.4\n%LeafpileGPT\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            Write(output, Ascii($"{index + 1} 0 obj\n"));
            Write(output, objects[index]);
            Write(output, Ascii("\nendobj\n"));
        }

        var xrefOffset = output.Position;
        Write(output, Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1))
        {
            Write(output, Ascii($"{offset:D10} 00000 n \n"));
        }

        Write(output, Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    private static string ToAscii(string value) => new(value.Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());

    private static string EscapePdfString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] Join(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(array => array.Length)];
        var offset = 0;
        foreach (var array in arrays)
        {
            array.CopyTo(result, offset);
            offset += array.Length;
        }

        return result;
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}

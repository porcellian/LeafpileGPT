using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public sealed record InstructionAuditImportResult(
    ImmutableArray<ZMachineInstructionAuditGroup> Groups,
    bool IgnoredIncompleteTail)
{
    public int InstructionCount => Groups.Sum(group => group.Instructions.Length);
}

/// <summary>Reads the structured JSON Lines format written by instruction audit exports.</summary>
public static class InstructionAuditJsonLinesImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<InstructionAuditImportResult> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(stream, leaveOpen: false, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<InstructionAuditImportResult> ReadAsync(
        Stream stream,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The audit stream must be readable.", nameof(stream));
        }

        var endsWithNewline = await EndsWithNewlineAsync(stream, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, 4096, leaveOpen);
        var groups = new List<MutableGroup>();
        var groupsByNumber = new Dictionary<long, MutableGroup>();
        var lineNumber = 0;
        var ignoredIncompleteTail = false;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                if (lineNumber == 1 && groups.Count == 0)
                {
                    continue;
                }

                throw new InvalidDataException($"Audit JSONL line {lineNumber} is empty.");
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = RequiredString(root, "type", lineNumber);
                if (type == "command")
                {
                    var number = RequiredPositiveInt64(root, "group", lineNumber);
                    if (groupsByNumber.ContainsKey(number))
                    {
                        throw new InvalidDataException($"Audit JSONL line {lineNumber} repeats command group {number}.");
                    }

                    var group = new MutableGroup(
                        number,
                        RequiredString(root, "command", lineNumber),
                        RequiredDateTimeOffset(root, "startedAt", lineNumber));
                    groups.Add(group);
                    groupsByNumber.Add(number, group);
                }
                else if (type == "instruction")
                {
                    var groupNumber = RequiredPositiveInt64(root, "group", lineNumber);
                    if (!groupsByNumber.TryGetValue(groupNumber, out var group))
                    {
                        throw new InvalidDataException($"Audit JSONL line {lineNumber} refers to missing command group {groupNumber}.");
                    }

                    group.Instructions.Add(new ZMachineInstructionAuditEntry(
                        RequiredPositiveInt64(root, "sequence", lineNumber),
                        RequiredAddress(root, "programCounter", lineNumber),
                        RequiredEnum<InstructionForm>(root, "form", lineNumber),
                        RequiredEnum<OpcodeClass>(root, "opcodeClass", lineNumber),
                        RequiredByte(root, "opcode", lineNumber),
                        RequiredString(root, "mnemonic", lineNumber),
                        DeserializeOperands(root, lineNumber),
                        RequiredNullableByte(root, "storeVariable", lineNumber),
                        DeserializeBranch(root, lineNumber),
                        RequiredAddress(root, "nextProgramCounter", lineNumber),
                        RequiredAddress(root, "programCounterAfter", lineNumber),
                        RequiredBoolean(root, "isHalted", lineNumber)));
                }
                else
                {
                    throw new InvalidDataException($"Audit JSONL line {lineNumber} has unsupported record type '{type}'.");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException or OverflowException or ArgumentException)
            {
                var isLastUnterminatedLine = !endsWithNewline && reader.Peek() == -1;
                if (isLastUnterminatedLine)
                {
                    ignoredIncompleteTail = true;
                    break;
                }

                throw new InvalidDataException($"Audit JSONL line {lineNumber} is invalid: {exception.Message}", exception);
            }
        }

        if (groups.Count == 0)
        {
            throw new InvalidDataException("The selected file contains no instruction audit records.");
        }

        var snapshots = groups.Select(group => new ZMachineInstructionAuditGroup(
            group.Number,
            group.Command,
            group.StartedAt,
            group.Instructions.ToImmutableArray())).ToImmutableArray();
        return new InstructionAuditImportResult(snapshots, ignoredIncompleteTail);
    }

    private static async Task<bool> EndsWithNewlineAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return true;
        }

        if (stream.Length == 0)
        {
            stream.Position = 0;
            return true;
        }

        var originalPosition = stream.Position;
        stream.Position = stream.Length - 1;
        var lastByte = new byte[1];
        var bytesRead = await stream.ReadAsync(lastByte, cancellationToken).ConfigureAwait(false);
        stream.Position = originalPosition;
        return bytesRead == 1 && lastByte[0] == (byte)'\n';
    }

    private static string RequiredString(JsonElement element, string name, int lineNumber)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Required string '{name}' is missing.");
        }

        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement element, string name, int lineNumber)
    {
        if (!TryGetProperty(element, name, out var value) || !value.TryGetInt64(out var result))
        {
            throw new InvalidDataException($"Required integer '{name}' is missing.");
        }

        return result;
    }

    private static byte RequiredByte(JsonElement element, string name, int lineNumber)
    {
        var value = RequiredInt64(element, name, lineNumber);
        return value is >= byte.MinValue and <= byte.MaxValue
            ? (byte)value
            : throw new InvalidDataException($"'{name}' is outside the byte range.");
    }

    private static long RequiredPositiveInt64(JsonElement element, string name, int lineNumber)
    {
        var value = RequiredInt64(element, name, lineNumber);
        return value > 0 ? value : throw new InvalidDataException($"'{name}' must be greater than zero.");
    }

    private static byte? RequiredNullableByte(JsonElement element, string name, int lineNumber)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            throw new InvalidDataException($"Required field '{name}' is missing.");
        }

        return value.ValueKind == JsonValueKind.Null ? null : RequiredByte(element, name, lineNumber);
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement element, string name, int lineNumber)
    {
        if (!TryGetProperty(element, name, out var value)
            || value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var result))
        {
            throw new InvalidDataException($"Required timestamp '{name}' is missing or invalid.");
        }

        return result;
    }

    private static T RequiredEnum<T>(JsonElement element, string name, int lineNumber)
        where T : struct, Enum
    {
        var text = RequiredString(element, name, lineNumber);
        return Enum.TryParse<T>(text, ignoreCase: false, out var result) && Enum.IsDefined(result)
            ? result
            : throw new InvalidDataException($"'{name}' has unsupported value '{text}'.");
    }

    private static uint RequiredAddress(JsonElement element, string name, int lineNumber)
    {
        var text = RequiredString(element, name, lineNumber);
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException($"'{name}' is not a valid hexadecimal address.");
    }

    private static ImmutableArray<DecodedOperand> DeserializeOperands(JsonElement element, int lineNumber)
    {
        if (!TryGetProperty(element, "operands", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Required operand array is missing.");
        }

        var operands = JsonSerializer.Deserialize<ImmutableArray<DecodedOperand>>(value.GetRawText(), SerializerOptions);
        if (operands.Any(operand => !Enum.IsDefined(operand.Encoding)))
        {
            throw new InvalidDataException("Operand data contains an unsupported encoding.");
        }

        return operands;
    }

    private static DecodedBranch? DeserializeBranch(JsonElement element, int lineNumber)
    {
        if (!TryGetProperty(element, "branch", out var value))
        {
            throw new InvalidDataException("Required branch field is missing.");
        }

        return value.ValueKind == JsonValueKind.Null
            ? null
            : JsonSerializer.Deserialize<DecodedBranch>(value.GetRawText(), SerializerOptions)
                ?? throw new InvalidDataException("Branch data is invalid.");
    }

    private static bool RequiredBoolean(JsonElement element, string name, int lineNumber)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Required Boolean '{name}' is missing.");
        }

        return value.GetBoolean();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class MutableGroup(long number, string command, DateTimeOffset startedAt)
    {
        public long Number { get; } = number;

        public string Command { get; } = command;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public List<ZMachineInstructionAuditEntry> Instructions { get; } = [];
    }
}

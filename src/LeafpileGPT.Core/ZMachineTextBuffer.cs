namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>Reads and writes the version-specific buffers used by Z-machine line input.</summary>
public static class ZMachineTextBuffer
{
    public static void ClearInput(ZMachineMemory memory, ushort textBufferAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        memory.ValidateReadableRange(textBufferAddress, 1);
        var maximumLength = memory.ReadByte(textBufferAddress);
        var capacity = maximumLength;
        var characterAddress = checked((ushort)(textBufferAddress + (memory.Header.Version <= 4 ? 1 : 2)));
        var requiredLength = Math.Max(3, checked(2 + capacity));
        memory.ValidateWritableRange(textBufferAddress, requiredLength);
        if (memory.Header.Version >= 5)
        {
            memory.WriteByte(checked((ushort)(textBufferAddress + 1)), 0);
        }

        var charactersToClear = memory.Header.Version <= 4 ? capacity + 1 : capacity;
        for (var index = 0; index < charactersToClear; index++)
        {
            memory.WriteByte(checked((ushort)(characterAddress + index)), 0);
        }
    }

    public static ImmutableArray<byte> ReadText(ZMachineMemory memory, ushort textBufferAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        memory.ValidateReadableRange(textBufferAddress, 1);
        var maximumLength = memory.ReadByte(textBufferAddress);
        if (memory.Header.Version <= 4)
        {
            var maximumCharacters = maximumLength;
            var bufferLength = checked(1 + maximumCharacters + 1);
            memory.ValidateReadableRange(textBufferAddress, bufferLength);
            var text = ImmutableArray.CreateBuilder<byte>(maximumCharacters);
            for (var index = 0; index < maximumCharacters; index++)
            {
                var character = memory.ReadByte(checked((ushort)(textBufferAddress + 1 + index)));
                if (character == 0)
                {
                    break;
                }

                text.Add(character);
            }

            return text.ToImmutable();
        }

        memory.ValidateReadableRange(textBufferAddress, 2);
        var actualLength = memory.ReadByte(checked((ushort)(textBufferAddress + 1)));
        if (actualLength > maximumLength)
        {
            throw new ZMachineExecutionException(
                $"Text buffer at 0x{textBufferAddress:X4} contains {actualLength} characters but declares a maximum of {maximumLength}.");
        }

        memory.ValidateReadableRange(textBufferAddress, checked(2 + actualLength));
        var result = ImmutableArray.CreateBuilder<byte>(actualLength);
        for (var index = 0; index < actualLength; index++)
        {
            result.Add(memory.ReadByte(checked((ushort)(textBufferAddress + 2 + index))));
        }

        return result.MoveToImmutable();
    }

    /// <summary>
    /// Writes accepted input in the version's text-buffer format and returns the terminating
    /// character. A line-feed return is normalized to the standard carriage-return code.
    /// </summary>
    public static byte StoreInput(
        ZMachineMemory memory,
        ZsciiCodec zscii,
        ushort textBufferAddress,
        ReadOnlySpan<char> input,
        byte terminatingCharacter = 13)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(zscii);
        memory.ValidateReadableRange(textBufferAddress, memory.Header.Version <= 4 ? 1 : 2);

        var maximumLength = memory.ReadByte(textBufferAddress);
        var versionFiveOrLater = memory.Header.Version >= 5;
        var existingLength = versionFiveOrLater ? memory.ReadByte(checked((ushort)(textBufferAddress + 1))) : 0;
        var capacity = maximumLength;
        if (existingLength > capacity)
        {
            throw new ZMachineExecutionException(
                $"Text buffer at 0x{textBufferAddress:X4} starts with {existingLength} characters but declares a maximum of {capacity}.");
        }

        var bufferLength = Math.Max(3, checked(2 + capacity));
        memory.ValidateWritableRange(textBufferAddress, bufferLength);
        if (versionFiveOrLater && terminatingCharacter != 0 && terminatingCharacter != 10
            && !zscii.IsDefinedInputCode(terminatingCharacter))
        {
            throw new ZMachineExecutionException($"Terminating character {terminatingCharacter} is not defined for ZSCII input.");
        }

        var availableLength = capacity - existingLength;
        var accepted = new List<byte>(Math.Min(input.Length, availableLength));
        for (var index = 0; index < input.Length && accepted.Count < availableLength; index++)
        {
            var lower = char.ToLowerInvariant(input[index]);
            if (!zscii.TryEncodeInput(lower, out var character)
                || !zscii.IsDefinedInputCode(character)
                || character is 0 or 8 or 13 or 27
                || zscii.Decode(character) == '\uFFFD')
            {
                throw new ZMachineExecutionException(
                    $"Input character U+{(int)input[index]:X4} at position {index + 1} is not defined for ZSCII input and output.");
            }

            accepted.Add(character);
        }

        if (versionFiveOrLater)
        {
            var textAddress = checked((ushort)(textBufferAddress + 2 + existingLength));
            for (var index = 0; index < accepted.Count; index++)
            {
                memory.WriteByte(checked((ushort)(textAddress + index)), accepted[index]);
            }

            memory.WriteByte(checked((ushort)(textBufferAddress + 1)), checked((byte)(existingLength + accepted.Count)));
        }
        else
        {
            for (var index = 0; index < accepted.Count; index++)
            {
                memory.WriteByte(checked((ushort)(textBufferAddress + 1 + index)), accepted[index]);
            }

            memory.WriteByte(checked((ushort)(textBufferAddress + 1 + accepted.Count)), 0);
        }

        if (!versionFiveOrLater)
        {
            return 13;
        }

        if (terminatingCharacter == 10)
        {
            return 13;
        }

        return terminatingCharacter;
    }
}

namespace LeafpileGPT.Core;

using System.Text;

/// <summary>Normalizes filenames supplied by a story to the standard's portable 8.3 form.</summary>
public static class ZMachineAuxiliaryFileName
{
    /// <summary>Reads a length-prefixed ASCII filename from story memory and sanitizes it.</summary>
    public static string FromStoryMemory(ZMachineMemory memory, ushort address)
    {
        ArgumentNullException.ThrowIfNull(memory);
        memory.ValidateReadableRange(address, 1);
        var length = memory.ReadByte(address);
        memory.ValidateReadableRange(address, length + 1);

        var name = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            name.Append((char)memory.ReadByte(checked((ushort)(address + 1 + index))));
        }

        return Sanitize(name.ToString());
    }

    /// <summary>
    /// Removes path and non-ASCII filename characters, uppercases the result, limits it to an
    /// 8-character base and 3-character extension, and supplies the standard AUX extension.
    /// </summary>
    public static string Sanitize(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var firstPeriod = fileName.IndexOf('.');
        var baseSource = firstPeriod < 0 ? fileName : fileName[..firstPeriod];
        var extensionSource = firstPeriod < 0 ? null : fileName[(firstPeriod + 1)..];
        var baseName = FilterAsciiAlphaNumeric(baseSource, 8);
        if (baseName.Length == 0)
        {
            baseName = "NULL";
        }

        var extension = extensionSource is null ? "AUX" : FilterAsciiAlphaNumeric(extensionSource, 3);
        return $"{baseName}.{(extension.Length == 0 ? "AUX" : extension)}";
    }

    private static string FilterAsciiAlphaNumeric(string value, int maximumLength)
    {
        var result = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (var character in value)
        {
            if (result.Length == maximumLength)
            {
                break;
            }

            if (character is >= 'a' and <= 'z')
            {
                result.Append((char)(character - ('a' - 'A')));
            }
            else if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }
}

/// <summary>Stores story-requested auxiliary files under a host-selected directory.</summary>
public interface IZMachineAuxiliaryFileStore
{
    /// <summary>Reads a file, returning null only when the requested file does not exist.</summary>
    ValueTask<byte[]?> TryReadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces an auxiliary file.</summary>
    ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken = default);
}

/// <summary>Filesystem-backed auxiliary storage rooted at a directory selected by the host.</summary>
public sealed class FileSystemZMachineAuxiliaryFileStore : IZMachineAuxiliaryFileStore
{
    private readonly string _directoryPath;

    public FileSystemZMachineAuxiliaryFileStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public async ValueTask<byte[]?> TryReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var safeName = ZMachineAuxiliaryFileName.Sanitize(fileName);
        if (File.Exists(_directoryPath))
        {
            throw new ZMachineAuxiliaryFileException(
                "read",
                safeName,
                new IOException("The configured auxiliary-file location is not a directory."));
        }

        try
        {
            return await File.ReadAllBytesAsync(GetFilePath(safeName), cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ZMachineAuxiliaryFileException("read", safeName, exception);
        }
    }

    public async ValueTask WriteAsync(
        string fileName,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken = default)
    {
        var safeName = ZMachineAuxiliaryFileName.Sanitize(fileName);
        try
        {
            Directory.CreateDirectory(_directoryPath);
            await File.WriteAllBytesAsync(GetFilePath(safeName), contents.ToArray(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ZMachineAuxiliaryFileException("write", safeName, exception);
        }
    }

    private string GetFilePath(string safeName)
    {
        return Path.Combine(_directoryPath, safeName);
    }
}

/// <summary>Adds operation and normalized filename context to an auxiliary-file I/O failure.</summary>
public sealed class ZMachineAuxiliaryFileException : Exception
{
    public ZMachineAuxiliaryFileException(string operation, string fileName, Exception innerException)
        : base($"Could not {operation} auxiliary file '{fileName}': {innerException.Message}", innerException)
    {
        Operation = operation;
        FileName = fileName;
    }

    public string Operation { get; }

    public string FileName { get; }
}

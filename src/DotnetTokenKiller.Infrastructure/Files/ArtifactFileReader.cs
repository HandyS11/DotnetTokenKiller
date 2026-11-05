using DotnetTokenKiller.Domain.Files;

namespace DotnetTokenKiller.Infrastructure.Files;

/// <summary>Reads the files a wrapped tool writes beside its output from the local file system.</summary>
public sealed class ArtifactFileReader : IArtifactFileReader
{
    /// <summary>The largest file read; a report beyond it is treated as unreadable rather than loaded whole.</summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <inheritdoc />
    public string? TryReadText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxBytes ? File.ReadAllText(file.FullName) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? FindNewest(string directory, string fileName, DateTimeOffset notBeforeUtc)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fileName);
        try
        {
            var root = new DirectoryInfo(directory);
            if (!root.Exists)
            {
                return null;
            }

            return root.EnumerateFiles(fileName, Recursive)
                .Where(f => f.LastWriteTimeUtc >= notBeforeUtc.UtcDateTime)
                .MaxBy(f => f.LastWriteTimeUtc)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

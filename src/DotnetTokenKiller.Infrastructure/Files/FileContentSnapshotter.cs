using System.Security.Cryptography;
using DotnetTokenKiller.Domain.Files;

namespace DotnetTokenKiller.Infrastructure.Files;

/// <summary>Hashes every regular file under a directory with SHA-256, within a file and byte budget.</summary>
public sealed class FileContentSnapshotter : IFileContentSnapshotter
{
    internal const int DefaultMaxFiles = 20_000;
    internal const long DefaultMaxBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> SkippedDirectories =
        new(["bin", "obj", "node_modules", "artifacts", "StrykerOutput"], StringComparer.Ordinal);

    private readonly int _maxFiles;
    private readonly long _maxBytes;

    /// <summary>Creates a snapshotter with the default limits (20,000 files, 256 MiB).</summary>
    public FileContentSnapshotter()
        : this(DefaultMaxFiles, DefaultMaxBytes)
    {
    }

    internal FileContentSnapshotter(int maxFiles, long maxBytes)
    {
        _maxFiles = maxFiles;
        _maxBytes = maxBytes;
    }

    /// <inheritdoc />
    public FileContentSnapshot? Take(string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(root);
        var files = CollectFiles(rootPath, cancellationToken);
        if (files is null)
        {
            return null;
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryHash(path, out var hash, out var length))
            {
                continue;
            }

            totalBytes += length;
            if (totalBytes > _maxBytes)
            {
                return null;
            }

            hashes[Path.GetRelativePath(rootPath, path).Replace('\\', '/')] = hash;
        }

        return new FileContentSnapshot(rootPath, hashes);
    }

    /// <summary>Lists the regular files worth hashing, or <see langword="null"/> past the file limit.</summary>
    /// <param name="rootPath">The absolute root.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    private List<string>? CollectFiles(string rootPath, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            foreach (var entry in Entries(pending.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry is not DirectoryInfo)
                {
                    files.Add(entry.FullName);
                }
                else if (!entry.Name.StartsWith('.') && !SkippedDirectories.Contains(entry.Name))
                {
                    pending.Push(entry.FullName);
                }

                if (files.Count > _maxFiles)
                {
                    return null;
                }
            }
        }

        return files;
    }

    private static List<FileSystemInfo> Entries(string directory)
    {
        try
        {
            return [.. new DirectoryInfo(directory).EnumerateFileSystemInfos()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool TryHash(string path, out string hash, out long length)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            length = stream.Length;
            hash = Convert.ToHexString(SHA256.HashData(stream));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hash = string.Empty;
            length = 0;
            return false;
        }
    }
}

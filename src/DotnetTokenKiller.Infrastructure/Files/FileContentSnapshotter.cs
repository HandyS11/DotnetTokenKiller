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

    private static readonly string EmptyHash = Convert.ToHexString(SHA256.HashData([]));

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
        foreach (var (path, length) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A zero-length entry is never opened: a named pipe or device reports no size and would block the
            // open, while an empty regular file has a known hash. A file that later gains content still differs.
            if (length == 0)
            {
                hashes[RelativePath(rootPath, path)] = EmptyHash;
            }
            else if (TryHash(path, out var hash))
            {
                hashes[RelativePath(rootPath, path)] = hash;
            }
        }

        return new FileContentSnapshot(rootPath, hashes);
    }

    private static string RelativePath(string rootPath, string path) =>
        Path.GetRelativePath(rootPath, path).Replace('\\', '/');

    /// <summary>
    /// Lists the files worth hashing with their sizes, or <see langword="null"/> past the file or byte limit,
    /// decided from the directory entries alone so an oversized tree is never read.
    /// </summary>
    /// <param name="rootPath">The absolute root.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    private List<(string Path, long Length)>? CollectFiles(string rootPath, CancellationToken cancellationToken)
    {
        var files = new List<(string Path, long Length)>();
        long totalBytes = 0;
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            foreach (var entry in Entries(pending.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSkipped(entry))
                {
                    continue;
                }

                if (entry is FileInfo file)
                {
                    var length = SizeOf(file);
                    totalBytes += length;
                    files.Add((file.FullName, length));
                    if (files.Count > _maxFiles || totalBytes > _maxBytes)
                    {
                        return null;
                    }
                }
                else
                {
                    pending.Push(entry.FullName);
                }
            }
        }

        return files;
    }

    private static bool IsSkipped(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
        || (entry is DirectoryInfo && (entry.Name.StartsWith('.') || SkippedDirectories.Contains(entry.Name)));

    private static long SizeOf(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
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

    private static bool TryHash(string path, out string hash)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            hash = Convert.ToHexString(SHA256.HashData(stream));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hash = string.Empty;
            return false;
        }
    }
}

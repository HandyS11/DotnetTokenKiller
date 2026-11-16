using System.Security.Cryptography;
using DotnetTokenKiller.Domain.Files;

namespace DotnetTokenKiller.Infrastructure.Files;

/// <summary>Hashes the regular files under a directory with SHA-256, within a file and byte budget.</summary>
/// <remarks>
/// In a git work tree it hashes only the files git does not ignore, so generated sites, scratch directories and
/// other ignored output neither cost a read nor push the tree over its budget; elsewhere it walks the directory.
/// Either way it skips build output, dependency and hidden directories, and files the caller leaves out.
/// </remarks>
public sealed class FileContentSnapshotter : IFileContentSnapshotter
{
    internal const int DefaultMaxFiles = 20_000;
    internal const long DefaultMaxBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> SkippedDirectories =
        new(["bin", "obj", "node_modules", "artifacts", "StrykerOutput"], StringComparer.Ordinal);

    private static readonly string EmptyHash = Convert.ToHexString(SHA256.HashData([]));

    private readonly int _maxFiles;
    private readonly long _maxBytes;
    private readonly IGitWorkingTree _git;

    /// <summary>Creates a snapshotter with the default limits (20,000 files, 256 MiB) and the git on <c>PATH</c>.</summary>
    public FileContentSnapshotter()
        : this(new GitWorkingTree())
    {
    }

    /// <summary>Creates a snapshotter with the default limits (20,000 files, 256 MiB).</summary>
    /// <param name="git">Says which files a work tree ignores.</param>
    public FileContentSnapshotter(IGitWorkingTree git)
        : this(DefaultMaxFiles, DefaultMaxBytes, git)
    {
    }

    internal FileContentSnapshotter(int maxFiles, long maxBytes, IGitWorkingTree? git = null)
    {
        _maxFiles = maxFiles;
        _maxBytes = maxBytes;
        _git = git ?? new GitWorkingTree();
    }

    /// <inheritdoc />
    public FileContentSnapshot? Take(string root, Func<string, bool>? includesFile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(root);
        includesFile ??= static _ => true;
        var listed = _git.UnignoredFiles(rootPath, cancellationToken);
        var files = listed is null
            ? CollectFiles(rootPath, includesFile, cancellationToken)
            : ListedFiles(rootPath, listed, includesFile, cancellationToken);
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
    /// Lists the files worth hashing with their sizes among those git does not ignore, or <see langword="null"/>
    /// past the file or byte limit, decided from their sizes alone so an oversized tree is never read. A submodule,
    /// which git lists as its directory, is walked.
    /// </summary>
    /// <param name="rootPath">The absolute root.</param>
    /// <param name="listed">The files under the root that git does not ignore, relative to it.</param>
    /// <param name="includesFile">Whether a file name is worth hashing.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    private List<(string Path, long Length)>? ListedFiles(string rootPath, IReadOnlyList<string> listed,
        Func<string, bool> includesFile, CancellationToken cancellationToken)
    {
        var budget = new FileBudget(_maxFiles, _maxBytes);
        foreach (var relative in listed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segments = relative.TrimEnd('/').Split('/');
            if (segments[..^1].Any(IsSkippedDirectoryName))
            {
                continue;
            }

            var path = Path.GetFullPath(relative, rootPath);
            var directory = new DirectoryInfo(path);
            var fits = directory.Exists && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? IsSkippedDirectoryName(segments[^1]) || Walk(path, includesFile, budget, cancellationToken)
                : !includesFile(segments[^1]) || TryAddListed(new FileInfo(path), budget);
            if (!fits)
            {
                return null;
            }
        }

        return budget.Files;
    }

    /// <summary>
    /// Lists the files worth hashing with their sizes, or <see langword="null"/> past the file or byte limit,
    /// decided from the directory entries alone so an oversized tree is never read.
    /// </summary>
    /// <param name="rootPath">The absolute root.</param>
    /// <param name="includesFile">Whether a file name is worth hashing.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    private List<(string Path, long Length)>? CollectFiles(string rootPath, Func<string, bool> includesFile,
        CancellationToken cancellationToken)
    {
        var budget = new FileBudget(_maxFiles, _maxBytes);
        return Walk(rootPath, includesFile, budget, cancellationToken) ? budget.Files : null;
    }

    /// <summary>Adds the files worth hashing under a directory, and says whether they still fit the limits.</summary>
    /// <param name="directory">The absolute directory to walk.</param>
    /// <param name="includesFile">Whether a file name is worth hashing.</param>
    /// <param name="budget">The files picked so far.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    private static bool Walk(string directory, Func<string, bool> includesFile, FileBudget budget,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            foreach (var entry in Entries(pending.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSkipped(entry))
                {
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    pending.Push(entry.FullName);
                }
                else if (includesFile(file.Name) && !budget.TryAdd(file))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Adds a listed file unless it is gone or a link, and says whether the files still fit.</summary>
    /// <param name="file">A file git listed.</param>
    /// <param name="budget">The files picked so far.</param>
    private static bool TryAddListed(FileInfo file, FileBudget budget) =>
        !file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || budget.TryAdd(file);

    private static bool IsSkipped(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
        || (entry is DirectoryInfo && IsSkippedDirectoryName(entry.Name));

    private static bool IsSkippedDirectoryName(string name) =>
        name.StartsWith('.') || SkippedDirectories.Contains(name);

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

    /// <summary>The files picked so far with their sizes, and whether they still fit the limits.</summary>
    /// <param name="maxFiles">The most files a snapshot hashes.</param>
    /// <param name="maxBytes">The most bytes a snapshot reads.</param>
    private sealed class FileBudget(int maxFiles, long maxBytes)
    {
        private long _totalBytes;

        public List<(string Path, long Length)> Files { get; } = [];

        /// <summary>Adds a file, and says whether the files still fit.</summary>
        /// <param name="file">A regular file.</param>
        public bool TryAdd(FileInfo file)
        {
            var length = SizeOf(file);
            _totalBytes += length;
            Files.Add((file.FullName, length));
            return Files.Count <= maxFiles && _totalBytes <= maxBytes;
        }

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
    }
}

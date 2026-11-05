namespace DotnetTokenKiller.Domain.Files;

/// <summary>The content hashes of the files under a root, taken at one moment.</summary>
public sealed class FileContentSnapshot
{
    private readonly IReadOnlyDictionary<string, string> _hashes;

    /// <summary>Creates a snapshot.</summary>
    /// <param name="root">The directory the paths are relative to.</param>
    /// <param name="hashes">Content hash by path relative to <paramref name="root"/>, <c>/</c>-separated.</param>
    public FileContentSnapshot(string root, IReadOnlyDictionary<string, string> hashes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(hashes);
        Root = root;
        _hashes = hashes;
    }

    /// <summary>The directory the paths are relative to.</summary>
    public string Root { get; }

    /// <summary>The files present in both snapshots whose content differs.</summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <returns>Paths relative to the root, <c>/</c>-separated on every OS, in ordinal order.</returns>
    public IReadOnlyList<string> ChangedSince(FileContentSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(before);
        var changed = new List<string>();
        foreach (var (path, hash) in _hashes)
        {
            if (before._hashes.TryGetValue(path, out var earlier) && !string.Equals(earlier, hash, StringComparison.Ordinal))
            {
                changed.Add(path);
            }
        }

        changed.Sort(StringComparer.Ordinal);
        return changed;
    }
}

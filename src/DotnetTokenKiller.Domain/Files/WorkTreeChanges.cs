namespace DotnetTokenKiller.Domain.Files;

/// <summary>
/// The files the work in progress changed, as <see cref="IGitWorkingTree.ChangedFiles"/> reports them: what a
/// report singles out among a build's warnings, since those are the ones the change may have caused.
/// </summary>
public sealed class WorkTreeChanges
{
    private readonly HashSet<string> _paths;

    /// <summary>Holds <paramref name="paths"/>, each resolved to a full path.</summary>
    /// <param name="paths">The changed files.</param>
    public WorkTreeChanges(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = new HashSet<string>(paths.Select(Path.GetFullPath), Comparer);
    }

    /// <summary>
    /// How paths compare: by case only on Linux, whose file systems tell <c>a.cs</c> from <c>A.cs</c>; Windows and
    /// macOS do not by default, and a compiler may print a path in another case than git does.
    /// </summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>How many files changed.</summary>
    public int Count => _paths.Count;

    /// <summary>Whether any changed file, as a full path, satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The test of a file's full path.</param>
    public bool Any(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _paths.Any(predicate);
    }

    /// <summary>Whether <paramref name="path"/>, resolved to a full path, is one of the changed files.</summary>
    /// <param name="path">A file's path.</param>
    public bool Contains(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length > 0 && _paths.Contains(Path.GetFullPath(path));
    }
}

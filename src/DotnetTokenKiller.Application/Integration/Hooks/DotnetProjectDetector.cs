using System.Collections.Frozen;
using System.Security;
using DotnetTokenKiller.Domain.Files;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Whether a directory is in a .NET project, for the hooks that tell an agent dtk's rules only where it can use them.
/// </summary>
/// <remarks>
/// A hook runs this at every session and subagent start, so it is bounded: the directory and its ancestors are read
/// file by file, and below it at most two levels of directories are read, up to a budget of entries, after which the
/// answer is "not .NET". Nothing it meets throws: an unreadable directory holds no match.
/// </remarks>
internal static class DotnetProjectDetector
{
    /// <summary>How many directory entries below the directory are read before the answer is given up.</summary>
    internal const int DefaultEntryBudget = 2000;

    /// <summary>How many levels of directories below the directory are read.</summary>
    private const int MaxDepth = 2;

    /// <summary>The extensions of solution and project files, which mark a .NET project anywhere they are found.</summary>
    private static readonly FrozenSet<string> ProjectExtensions =
        new[] { ".sln", ".slnx", ".slnf", ".csproj", ".fsproj", ".vbproj" }.ToFrozenSet(WorkTreeChanges.Comparer);

    /// <summary>The files that mark a .NET project in the directory or an ancestor, though not below it.</summary>
    private static readonly FrozenSet<string> AncestorMarkers =
        new[] { "global.json", "Directory.Build.props" }.ToFrozenSet(WorkTreeChanges.Comparer);

    /// <summary>The directories the walk below never enters, beside hidden ones: build output and dependencies.</summary>
    private static readonly FrozenSet<string> SkippedDirectories =
        new[] { "bin", "obj", "node_modules", "artifacts" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>One directory's entries, links not followed, unreadable ones skipped.</summary>
    private static readonly EnumerationOptions EntryOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    /// <summary>What reading one directory's entries found.</summary>
    private enum Scan
    {
        /// <summary>No project file; the walk goes on.</summary>
        Continue = 0,

        /// <summary>A solution or project file.</summary>
        Found = 1,

        /// <summary>The entry budget ran out.</summary>
        Exhausted = 2
    }

    /// <summary>Whether <paramref name="directory"/> is in a .NET project, within <see cref="DefaultEntryBudget"/>.</summary>
    /// <param name="directory">The agent's working directory, as its harness reported it.</param>
    internal static bool IsDotnetProject(string? directory) => IsDotnetProject(directory, DefaultEntryBudget);

    /// <summary>
    /// Whether <paramref name="directory"/> is in a .NET project: it or an ancestor holds a solution or project file,
    /// <c>global.json</c> or <c>Directory.Build.props</c>; or a directory at most two levels below it, outside hidden
    /// directories, <c>bin</c>, <c>obj</c>, <c>node_modules</c> and <c>artifacts</c>, holds a solution or project
    /// file. A directory that is missing, relative or not a directory is in none.
    /// </summary>
    /// <param name="directory">The agent's working directory, as its harness reported it.</param>
    /// <param name="entryBudget">How many entries below <paramref name="directory"/> may be read.</param>
    internal static bool IsDotnetProject(string? directory, int entryBudget)
    {
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var start = new DirectoryInfo(directory);
        return HasMarkerInItselfOrAnAncestor(start) || HasProjectBelow(start, entryBudget);
    }

    private static bool HasMarkerInItselfOrAnAncestor(DirectoryInfo start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            if (HoldsAncestorMarker(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HoldsAncestorMarker(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", EntryOptions)
                .Any(file => IsProjectFile(file.Name) || AncestorMarkers.Contains(file.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the directories below <paramref name="start"/> level by level, the start's own entries counting toward the
    /// budget: its files were read as an ancestor's, so only the files of the levels below it count as a match.
    /// </summary>
    /// <param name="start">The agent's working directory.</param>
    /// <param name="entryBudget">How many entries may be read.</param>
    private static bool HasProjectBelow(DirectoryInfo start, int entryBudget)
    {
        var budget = entryBudget;
        List<DirectoryInfo> level = [start];
        for (var depth = 0; depth <= MaxDepth && level.Count > 0; depth++)
        {
            List<DirectoryInfo> next = [];
            foreach (var directory in level)
            {
                var scan = ScanEntries(directory, readFiles: depth > 0, queueSubdirectories: depth < MaxDepth, ref budget, next);
                if (scan != Scan.Continue)
                {
                    return scan == Scan.Found;
                }
            }

            level = next;
        }

        return false;
    }

    private static Scan ScanEntries(
        DirectoryInfo directory, bool readFiles, bool queueSubdirectories, ref int budget, List<DirectoryInfo> next)
    {
        try
        {
            foreach (var entry in directory.EnumerateFileSystemInfos("*", EntryOptions))
            {
                if (--budget < 0)
                {
                    return Scan.Exhausted;
                }

                if (entry is DirectoryInfo subdirectory)
                {
                    if (queueSubdirectories && !IsSkipped(subdirectory.Name))
                    {
                        next.Add(subdirectory);
                    }
                }
                else if (readFiles && IsProjectFile(entry.Name))
                {
                    return Scan.Found;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Scan.Continue;
        }

        return Scan.Continue;
    }

    private static bool IsProjectFile(string name) => ProjectExtensions.Contains(Path.GetExtension(name));

    private static bool IsSkipped(string name) => name.StartsWith('.') || SkippedDirectories.Contains(name);
}

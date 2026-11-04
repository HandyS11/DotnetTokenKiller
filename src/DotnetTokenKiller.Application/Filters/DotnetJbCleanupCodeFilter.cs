using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet jb cleanupcode</c> output to the files it changed.</summary>
/// <remarks>
/// jb prints a banner, every skipped file and every processed file, changed or not, so the output says nothing about
/// what changed: the changed files come from <see cref="FilterContext.Changes"/>, found by hashing the target's tree
/// before and after the run. The filter counts the skipped and processed files and drops the lists. Under
/// <c>dtk pipe</c> there is no such context and it says so.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed partial class DotnetJbCleanupCodeFilter(string? rootPath = null) : IContextualOutputFilter, ISnapshotFilter
{
    private const int MaxChangedPaths = 30;
    private static readonly string[] ProjectExtensions = [".sln", ".slnx", ".slnf", ".csproj"];

    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <inheritdoc />
    public string SnapshotRoot(IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        foreach (var argument in arguments)
        {
            if (argument.Length == 0 || argument[0] == '-')
            {
                continue;
            }

            var full = Path.GetFullPath(argument, workingDirectory);
            if (ProjectExtensions.Any(e => argument.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                return Path.GetDirectoryName(full) ?? workingDirectory;
            }

            if (Directory.Exists(full))
            {
                return full;
            }
        }

        return workingDirectory;
    }

    /// <summary>Applies the filter to the cleanupcode output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The cleanupcode output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>Applies the filter to the cleanupcode output.</summary>
    /// <param name="strippedOutput">The cleanupcode output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(strippedOutput);
        ArgumentNullException.ThrowIfNull(context);

        var (processed, skipped, kept, loadErrors) = Parse(strippedOutput);

        var sb = new StringBuilder();
        if (exitCode != 0)
        {
            foreach (var line in kept)
            {
                sb.Append(line).Append('\n');
            }

            sb.Append(CultureInfo.InvariantCulture,
                $"✗ dotnet jb cleanupcode failed (exit {exitCode}){FailureSummary(loadErrors, kept)}\n");
            return new FilterResult(sb.ToString());
        }

        var counts = string.Create(CultureInfo.InvariantCulture, $"{processed} processed, {skipped} skipped");
        var changes = context.Changes;
        if (changes is null)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"✓ dotnet jb cleanupcode: {counts} · changed files unknown (piped)\n");
        }
        else if (!changes.Computed)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"✓ dotnet jb cleanupcode: {counts} · changed files not computed ({changes.NotComputedReason})\n");
        }
        else
        {
            AppendChanged(sb, changes.ChangedPaths, counts);
        }

        return new FilterResult(sb.ToString());
    }

    private (int Processed, int Skipped, List<string> Kept, int LoadErrors) Parse(string output)
    {
        var skipped = 0;
        var processed = 0;
        var inProcessed = false;
        var loadErrors = 0;
        var kept = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (inProcessed)
            {
                processed += line.Trim().Length == 0 ? 0 : 1;
            }
            else if (line.StartsWith("Cleaning up using profile", StringComparison.Ordinal))
            {
                inProcessed = true;
            }
            else if (SkippedPattern().IsMatch(line))
            {
                skipped++;
            }
            else if (IsKeptLine(line) && AddDistinct(kept, TextHelpers.ShortenPathsInLine(line.TrimEnd(), RootPath)))
            {
                loadErrors += line.StartsWith("[MsBuild]", StringComparison.Ordinal) ? 1 : 0;
            }
        }

        return (processed, skipped, kept, loadErrors);
    }

    private static bool IsKeptLine(string line) =>
        line.StartsWith("[MsBuild]", StringComparison.Ordinal)
        || line.StartsWith("Warning:", StringComparison.Ordinal)
        || line.StartsWith("No items were found", StringComparison.Ordinal);

    private static bool AddDistinct(List<string> lines, string line)
    {
        if (lines.Contains(line))
        {
            return false;
        }

        lines.Add(line);
        return true;
    }

    private static void AppendChanged(StringBuilder sb, IReadOnlyList<string> paths, string counts)
    {
        if (paths.Count == 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"✓ dotnet jb cleanupcode: no changes ({counts})\n");
            return;
        }

        foreach (var path in paths.Take(MaxChangedPaths))
        {
            sb.Append(path).Append('\n');
        }

        if (paths.Count > MaxChangedPaths)
        {
            sb.Append(CultureInfo.InvariantCulture, $"… {paths.Count - MaxChangedPaths} more (git status)\n");
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"✓ dotnet jb cleanupcode: {paths.Count} file{(paths.Count == 1 ? "" : "s")} changed ({counts})\n");
    }

    private static string FailureSummary(int loadErrors, List<string> kept)
    {
        if (loadErrors > 0)
        {
            return $": {loadErrors} project load error{(loadErrors == 1 ? "" : "s")}";
        }

        return kept.Contains("No items were found to cleanup.") ? ": nothing to clean up" : "";
    }

    [GeneratedRegex(@" - Skipping\.( .*)?$")]
    private static partial Regex SkippedPattern();
}

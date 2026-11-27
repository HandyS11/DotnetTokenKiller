using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet jb cleanupcode</c> output to the files it changed.</summary>
/// <remarks>
/// <para>
/// jb prints a banner, every skipped file and every processed file, changed or not, so the output says nothing about
/// what changed: the changed files come from <see cref="FilterContext.Changes"/>, found by hashing the target's tree
/// before and after the run. The filter counts the skipped and processed files and drops the lists, and the
/// <c>Analyzing file N of M</c> progress lines jb prints while it works. Under <c>dtk pipe</c> there is no such
/// context and it says so. jb prints no list at all below its default verbosity (<c>--verbosity=ERROR</c> prints
/// nothing on success), and then the counts are left out rather than reported as zero.
/// </para>
/// <para>
/// A failed run keeps the <c>[MsBuild]</c>, <c>Warning:</c> and <c>No items were found</c> lines before the
/// <c>Cleaning up using profile</c> header, and every line after it that is not a processed file or progress, so
/// a crash's text stays visible. When it keeps nothing, it returns an empty result, so the pipeline's raw tail shows
/// what happened (jb not restored, say).
/// </para>
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed partial class DotnetJbCleanupCodeFilter(string? rootPath = null) : IContextualOutputFilter, ISnapshotFilter
{
    private const int MaxChangedPaths = 30;
    private const string ProcessingHeader = "Cleaning up using profile";
    private static readonly string[] ProjectExtensions = [".sln", ".slnx", ".slnf", ".csproj"];

    /// <summary>
    /// The extensions of the files ReSharper's code cleanup can rewrite: C#, Visual Basic, XAML, Razor and Web Forms,
    /// HTML, JavaScript and TypeScript, CSS, JSON, XML (project and solution files, resources, run settings, WiX and
    /// the other XML formats of a .NET repository included), C++, T4 and protobuf.
    /// Anything else, sample data, images and binaries above all, it leaves as it is.
    /// </summary>
    private static readonly HashSet<string> CleanedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".vb", ".xaml", ".axaml", ".razor", ".cshtml", ".vbhtml", ".aspx", ".ascx", ".asax",
        ".ashx", ".asmx", ".master", ".skin", ".html", ".htm", ".xhtml", ".js", ".jsx", ".mjs", ".cjs", ".ts",
        ".tsx", ".mts", ".cts", ".css", ".scss", ".less", ".json", ".jsonc", ".xml", ".xsd", ".xsl", ".xslt",
        ".config", ".resx", ".nuspec", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".sqlproj", ".proj", ".props",
        ".targets", ".slnx", ".runsettings", ".ruleset", ".manifest", ".vsct", ".wxs", ".wxi", ".edmx", ".c", ".cc", ".cpp",
        ".cxx", ".h", ".hh", ".hpp", ".hxx", ".inl", ".ixx", ".cppm", ".tt", ".ttinclude", ".t4", ".proto"
    };

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

    /// <inheritdoc />
    public bool CanChange(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return CleanedExtensions.Contains(Path.GetExtension(fileName));
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

        var (processed, skipped, kept, loadErrors, listed) = Parse(strippedOutput);

        var sb = new StringBuilder();
        if (exitCode != 0)
        {
            if (kept.Count == 0)
            {
                return new FilterResult(string.Empty);
            }

            foreach (var line in kept)
            {
                sb.Append(line).Append('\n');
            }

            sb.Append(CultureInfo.InvariantCulture,
                $"✗ dotnet jb cleanupcode failed (exit {exitCode}){FailureSummary(loadErrors, kept)}\n");
            return new FilterResult(sb.ToString());
        }

        var counts = listed
            ? string.Create(CultureInfo.InvariantCulture, $"{processed} processed, {skipped} skipped")
            : null;
        var countsThen = counts is null ? string.Empty : $"{counts} · ";
        var changes = context.Changes;
        if (changes is null)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"✓ dotnet jb cleanupcode: {countsThen}changed files unknown (piped)\n");
        }
        else if (!changes.Computed)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"✓ dotnet jb cleanupcode: {countsThen}changed files not computed ({changes.NotComputedReason})\n");
        }
        else
        {
            AppendChanged(sb, changes.ChangedPaths, counts);
        }

        return new FilterResult(sb.ToString());
    }

    /// <summary>
    /// Counts the processed and skipped files and keeps the lines worth showing on a failure. <c>Listed</c> says
    /// whether jb printed its file list at all: it does under the <c>Cleaning up using profile</c> header, which a
    /// verbosity below the default leaves out.
    /// </summary>
    /// <param name="output">The cleanupcode output.</param>
    private (int Processed, int Skipped, List<string> Kept, int LoadErrors, bool Listed) Parse(string output)
    {
        var skipped = 0;
        var processed = 0;
        var inProcessed = false;
        var loadErrors = 0;
        var kept = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith(ProcessingHeader, StringComparison.Ordinal))
            {
                // jb prints it again before the processed list when it printed progress lines first.
                inProcessed = true;
            }
            else if (inProcessed && IsProcessedEntry(line))
            {
                processed++;
            }
            else if (!inProcessed && SkippedPattern().IsMatch(line))
            {
                skipped++;
            }
            else if ((inProcessed ? !IsProgressOrBlank(line) : IsKeptLine(line))
                     && AddDistinct(kept, TextHelpers.ShortenPathsInLine(line.TrimEnd(), RootPath)))
            {
                loadErrors += line.StartsWith("[MsBuild]", StringComparison.Ordinal) ? 1 : 0;
            }
        }

        return (processed, skipped, kept, loadErrors, inProcessed);
    }

    private static bool IsKeptLine(string line) =>
        line.StartsWith("[MsBuild]", StringComparison.Ordinal)
        || line.StartsWith("Warning:", StringComparison.Ordinal)
        || line.StartsWith("No items were found", StringComparison.Ordinal)
        || line.StartsWith("Unable to find target solution file", StringComparison.Ordinal);

    /// <summary>
    /// Whether a line after the header names a processed file: <c>&lt;folder&gt;\&lt;project&gt;\…</c> as jb
    /// prints it, or a bare path (no blank, a separator).
    /// </summary>
    /// <param name="line">A line after the <c>Cleaning up using profile</c> header.</param>
    private static bool IsProcessedEntry(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('<'))
        {
            return trimmed.Contains(">\\", StringComparison.Ordinal) || trimmed.Contains(">/", StringComparison.Ordinal);
        }

        return trimmed.Length > 0
               && !trimmed.AsSpan().ContainsAny(' ', '\t')
               && trimmed.AsSpan().ContainsAny('\\', '/');
    }

    private static bool IsProgressOrBlank(string line) =>
        line.Trim().Length == 0 || ProgressPattern().IsMatch(line);

    private static bool AddDistinct(List<string> lines, string line)
    {
        if (lines.Contains(line))
        {
            return false;
        }

        lines.Add(line);
        return true;
    }

    private static void AppendChanged(StringBuilder sb, IReadOnlyList<string> paths, string? counts)
    {
        var inParentheses = counts is null ? string.Empty : $" ({counts})";
        if (paths.Count == 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"✓ dotnet jb cleanupcode: no changes{inParentheses}\n");
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
            $"✓ dotnet jb cleanupcode: {paths.Count} file{(paths.Count == 1 ? "" : "s")} changed{inParentheses}\n");
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

    [GeneratedRegex(@"^Analyzing file \d+ of \d+: ")]
    private static partial Regex ProgressPattern();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet stryker</c> output to the mutation score, the counts and the survivors.</summary>
/// <remarks>
/// <para>
/// The score, the <c>Killed</c>/<c>Survived</c>/<c>Timeout</c> block, the no-coverage count and the duration come from
/// the output; the banner, the <c>INF</c> log lines and the hints are dropped, the <c>WRN</c>, <c>ERR</c> and
/// <c>FTL</c> messages are kept once each (but the threshold break's, which the status line states), and the report
/// paths Stryker wraps at 80 columns are joined, the html one (else the json one) named in the status line. Stryker
/// never prints its survivors: they come from the json report at the path its json reporter printed or, for a run dtk
/// started, the newest <c>mutation-report.json</c> written since under Stryker's <c>-O</c>/<c>--output</c> directory
/// or <c>StrykerOutput</c>. Without the progress reporter there is no counts block, and the counts come from the
/// report.
/// </para>
/// <para>
/// A failed initial build is shown as <c>dotnet build</c> shows it, from the unwrapped copy of the MSBuild output in
/// Stryker's <c>ERR</c> entry. Any other failure but a broken threshold returns nothing, so the pipeline's raw-tail
/// fallback shows what went wrong.
/// </para>
/// </remarks>
/// <param name="files">Reads the json report; without it the survivors are not listed.</param>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed partial class DotnetStrykerFilter(IArtifactFileReader? files = null, string? rootPath = null)
    : IContextualOutputFilter
{
    private const int MaxSurvivors = 20;
    private const int MaxNoCoverageFiles = 10;
    private const int MaxReplacementLength = 80;
    private const string ReportFileName = "mutation-report.json";
    private const string DefaultOutputDirectory = "StrykerOutput";
    private const string KilledStatus = "Killed";
    private const string SurvivedStatus = "Survived";
    private const string TimeoutStatus = "Timeout";
    private const string NoCoverageStatus = "NoCoverage";

    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the Stryker output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The Stryker output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>Applies the filter to the Stryker output.</summary>
    /// <param name="strippedOutput">The Stryker output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context: its arguments, working directory and start time.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(strippedOutput);
        ArgumentNullException.ThrowIfNull(context);

        var run = StrykerRun.Parse(strippedOutput, RootPath);
        if (exitCode != 0 && run.BuildOutput is not null)
        {
            return new FilterResult(RenderBuildFailure(run, exitCode));
        }

        if (!run.HasVerdict || (exitCode != 0 && run.BreakThreshold is null))
        {
            return new FilterResult(string.Empty);
        }

        var (mutants, jsonReport) = LoadReport(run, context);
        return new FilterResult(Render(run, mutants, jsonReport, exitCode));
    }

    /// <summary>Reads the json report the run printed, else the newest one a run dtk started wrote.</summary>
    /// <param name="run">The parsed output.</param>
    /// <param name="context">The run's context.</param>
    /// <returns>The report's mutants, or <see langword="null"/>; and whether a json report was named or found.</returns>
    private (IReadOnlyList<StrykerMutant>? Mutants, bool JsonReport) LoadReport(StrykerRun run, FilterContext context)
    {
        var printed = run.ReportPaths.GetValueOrDefault("json");
        var mutants = printed is null ? null : ReadMutants(printed);
        if (mutants is not null || files is null || context.StartedUtc is not { } started)
        {
            return (mutants, printed is not null);
        }

        var found = files.FindNewest(SearchDirectory(context), ReportFileName, started);
        return (found is null ? null : ReadMutants(found), printed is not null || found is not null);
    }

    private IReadOnlyList<StrykerMutant>? ReadMutants(string path) =>
        files?.TryReadText(path) is { } json && StrykerReport.ReadMutants(json) is { Count: > 0 } mutants
            ? mutants
            : null;

    private static string SearchDirectory(FilterContext context)
    {
        string? output = null;
        var arguments = context.Arguments;
        for (var i = 0; i < arguments.Count && arguments[i] != "--"; i++)
        {
            output = OutputAt(arguments, i) ?? output;
        }

        // Stryker writes under <-O>/reports when given -O, else under StrykerOutput/<timestamp>/reports.
        return Path.GetFullPath(output ?? DefaultOutputDirectory,
            context.WorkingDirectory ?? Environment.CurrentDirectory);
    }

    private static string? OutputAt(IReadOnlyList<string> arguments, int i)
    {
        if (arguments[i] is "-O" or "--output")
        {
            return i + 1 < arguments.Count ? arguments[i + 1] : null;
        }

        return arguments[i].StartsWith("--output=", StringComparison.Ordinal) ? arguments[i]["--output=".Length..] : null;
    }

    private string Render(StrykerRun run, IReadOnlyList<StrykerMutant>? mutants, bool jsonReport, int exitCode)
    {
        var counts = run.ResolveCounts(mutants);
        var sb = new StringBuilder();
        foreach (var line in run.Kept)
        {
            sb.Append(line).Append('\n');
        }

        AppendSurvivors(sb, mutants, counts, jsonReport);
        AppendNoCoverage(sb, mutants, counts?.NoCoverage ?? run.NoCoverage ?? 0);
        sb.Append(StatusLine(run, counts, exitCode)).Append('\n');
        return sb.ToString();
    }

    private void AppendSurvivors(StringBuilder sb, IReadOnlyList<StrykerMutant>? mutants, Counts? counts,
        bool jsonReport)
    {
        if (mutants is null)
        {
            if (counts is { Survived: 0 })
            {
                return;
            }

            if (jsonReport)
            {
                sb.Append("survivors: json report not readable\n");
            }
            else if (counts is not null)
            {
                sb.Append("survivors: enable the json reporter to list them here\n");
            }

            return;
        }

        var survivors = mutants
            .Where(m => m.Status == SurvivedStatus)
            .Select(m => (File: Shorten(m.File), Mutant: m))
            .OrderBy(s => s.File, StringComparer.Ordinal)
            .ThenBy(s => s.Mutant.Line)
            .ThenBy(s => s.Mutant.Column)
            .ToList();
        if (survivors.Count == 0)
        {
            return;
        }

        sb.Append(CultureInfo.InvariantCulture, $"Survived ({survivors.Count}):\n");
        foreach (var file in survivors.Take(MaxSurvivors).GroupBy(s => s.File, StringComparer.Ordinal))
        {
            sb.Append("  ").Append(file.Key).Append(": ").AppendJoin(" · ", file.Select(s => Describe(s.Mutant)))
                .Append('\n');
        }

        if (survivors.Count > MaxSurvivors)
        {
            sb.Append(CultureInfo.InvariantCulture, $"… {survivors.Count - MaxSurvivors} more (report)\n");
        }
    }

    private void AppendNoCoverage(StringBuilder sb, IReadOnlyList<StrykerMutant>? mutants, int count)
    {
        if (count == 0)
        {
            return;
        }

        sb.Append(CultureInfo.InvariantCulture, $"No coverage: {count}");
        var byFile = (mutants ?? [])
            .Where(m => m.Status == NoCoverageStatus)
            .GroupBy(m => Shorten(m.File), StringComparer.Ordinal)
            .Select(g => (File: g.Key, Count: g.Count()))
            .OrderByDescending(f => f.Count)
            .ThenBy(f => f.File, StringComparer.Ordinal)
            .ToList();
        if (byFile.Count > 0)
        {
            sb.Append(" (").AppendJoin(", ", byFile.Take(MaxNoCoverageFiles).Select(f =>
                string.Create(CultureInfo.InvariantCulture, $"{f.File} ×{f.Count}")));
            if (byFile.Count > MaxNoCoverageFiles)
            {
                sb.Append(CultureInfo.InvariantCulture, $", … {byFile.Count - MaxNoCoverageFiles} more files");
            }

            sb.Append(')');
        }

        sb.Append('\n');
    }

    private string StatusLine(StrykerRun run, Counts? counts, int exitCode)
    {
        var score = run.Score is null ? "no mutation score" : $"mutation score {run.Score}%";
        var sb = new StringBuilder(exitCode == 0
            ? $"✓ dotnet stryker: {score}"
            : string.Create(CultureInfo.InvariantCulture,
                $"✗ dotnet stryker: {score} below break threshold {run.BreakThreshold}% (exit {exitCode})"));
        if (counts is { } c)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" · {c.Killed} killed, {c.Survived} survived, {c.Timeout} timeout, {c.NoCoverage} no coverage");
        }

        if (run.Duration is not null)
        {
            sb.Append(" (").Append(run.Duration).Append(')');
        }

        if ((run.ReportPaths.GetValueOrDefault("html") ?? run.ReportPaths.GetValueOrDefault("json")) is { } report)
        {
            sb.Append(" · report ").Append(Shorten(report));
        }

        return sb.ToString();
    }

    private string RenderBuildFailure(StrykerRun run, int exitCode)
    {
        var report = MsBuildDiagnosticReport.Parse(run.BuildOutput!, RootPath);
        var sb = new StringBuilder();
        foreach (var line in run.Kept)
        {
            sb.Append(line).Append('\n');
        }

        sb.Append(report.RenderDiagnostics())
            .Append(CultureInfo.InvariantCulture, $"✗ dotnet stryker failed (exit {exitCode}): initial build failed");
        if (report.ErrorTotal > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $" · {report.ErrorTotal} error{(report.ErrorTotal == 1 ? "" : "s")}");
        }

        return sb.Append('\n').ToString();
    }

    /// <summary>A mutant as <c>(line,col) Mutator → replacement</c>, on one line.</summary>
    /// <param name="mutant">The mutant.</param>
    private static string Describe(StrykerMutant mutant)
    {
        const string suffix = " mutation";
        var mutator = mutant.MutatorName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? mutant.MutatorName[..^suffix.Length]
            : mutant.MutatorName;
        var position = string.Create(CultureInfo.InvariantCulture, $"({mutant.Line},{mutant.Column}) {mutator}");
        var replacement = mutant.Replacement is null
            ? string.Empty
            : TextHelpers.Truncate(WhitespacePattern().Replace(mutant.Replacement, " ").Trim(), MaxReplacementLength);
        return replacement.Length == 0 ? position : $"{position} → {replacement}";
    }

    /// <summary>A path relative to the root; one the report or Stryker gives relative is left as it is.</summary>
    /// <param name="path">The path.</param>
    private string Shorten(string path) => Path.IsPathRooted(path) ? TextHelpers.ShortenPath(path, RootPath) : path;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>The mutant counts the status line gives.</summary>
    /// <param name="Killed">Killed mutants.</param>
    /// <param name="Survived">Surviving mutants.</param>
    /// <param name="Timeout">Mutants that timed out.</param>
    /// <param name="NoCoverage">Mutants no test covers.</param>
    private readonly record struct Counts(int Killed, int Survived, int Timeout, int NoCoverage);

    /// <summary>What Stryker's console output says about the run.</summary>
    private sealed partial class StrykerRun
    {
        private const string BreakCrashing = "Final mutation score is below threshold break. Crashing...";
        private const string BuildFailedPrefix = "Initial build failed.";
        private const string BuildResultMarker = "Result:";

        private readonly string _rootPath;
        private readonly Dictionary<string, int> _block = new(StringComparer.Ordinal);

        private StrykerRun(string rootPath) => _rootPath = rootPath;

        /// <summary>The <c>WRN</c>, <c>ERR</c> and <c>FTL</c> messages, once each, paths shortened.</summary>
        public List<string> Kept { get; } = [];

        /// <summary>The report paths, by reporter (<c>json</c>, <c>html</c>), joined and without <c>file://</c>.</summary>
        public Dictionary<string, string> ReportPaths { get; } = new(StringComparer.Ordinal);

        /// <summary>The final mutation score as printed, with a decimal point.</summary>
        public string? Score { get; private set; }

        /// <summary>The threshold of a broken <c>--break-at</c>.</summary>
        public string? BreakThreshold { get; private set; }

        /// <summary>The count of the <c>mutants got status NoCoverage</c> line.</summary>
        public int? NoCoverage { get; private set; }

        /// <summary>The run's duration, formatted.</summary>
        public string? Duration { get; private set; }

        /// <summary>The MSBuild output of a failed initial build, unwrapped.</summary>
        public string? BuildOutput { get; private set; }

        /// <summary>Whether the run ended with a score or with Stryker saying it could not compute one.</summary>
        public bool HasVerdict => Score is not null || NoScore;

        /// <summary>Whether Stryker said it was unable to calculate a mutation score.</summary>
        private bool NoScore { get; set; }

        public static StrykerRun Parse(string output, string rootPath)
        {
            var run = new StrykerRun(rootPath);
            var lines = output.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd('\r');
            }

            var index = 0;
            while (index < lines.Length)
            {
                index = run.Read(lines, index);
            }

            return run;
        }

        /// <summary>The counts from the counts block, else from the report, else none.</summary>
        /// <param name="mutants">The report's mutants, when read.</param>
        public Counts? ResolveCounts(IReadOnlyList<StrykerMutant>? mutants)
        {
            if (_block.Count > 0)
            {
                return new Counts(_block.GetValueOrDefault(KilledStatus), _block.GetValueOrDefault(SurvivedStatus),
                    _block.GetValueOrDefault(TimeoutStatus), NoCoverage ?? 0);
            }

            return mutants is null
                ? null
                : new Counts(mutants.Count(m => m.Status == KilledStatus), mutants.Count(m => m.Status == SurvivedStatus),
                    mutants.Count(m => m.Status == TimeoutStatus), mutants.Count(m => m.Status == NoCoverageStatus));
        }

        /// <summary>Reads the line at <paramref name="index"/> and any it continues on.</summary>
        /// <param name="lines">The output's lines.</param>
        /// <param name="index">The line to read.</param>
        /// <returns>The index of the next line to read.</returns>
        private int Read(string[] lines, int index)
        {
            var line = lines[index];
            var log = LogLinePattern().Match(line);
            if (log.Success)
            {
                return ReadLogEntry(lines, index, log.Groups["level"].Value, log.Groups["message"].Value);
            }

            var header = ReportHeaderPattern().Match(line);
            if (header.Success)
            {
                return ReadReportPath(lines, index + 1, header.Groups["reporter"].Value);
            }

            var count = CountPattern().Match(line);
            if (count.Success)
            {
                _block[count.Groups["status"].Value] = TextHelpers.ParseNumberOrMax(count.Groups["count"].Value);
            }
            else if (BreakPattern().Match(line) is { Success: true } breakAt)
            {
                BreakThreshold = breakAt.Groups["threshold"].Value;
            }

            return index + 1;
        }

        private int ReadLogEntry(string[] lines, int index, string level, string message)
        {
            if (level is "ERR" && message.StartsWith(BuildFailedPrefix, StringComparison.Ordinal))
            {
                return ReadBuildOutput(lines, index, message);
            }

            if (level is "WRN" or "ERR" or "FTL")
            {
                Keep(message);
            }
            else if (level is "INF")
            {
                ReadInfo(message);
            }

            return index + 1;
        }

        private void Keep(string message)
        {
            var line = TextHelpers.ShortenPathsInLine(message.TrimEnd(), _rootPath);
            if (message != BreakCrashing && !Kept.Contains(line))
            {
                Kept.Add(line);
            }
        }

        private void ReadInfo(string message)
        {
            if (ScorePattern().Match(message) is { Success: true } score)
            {
                Score = score.Groups["score"].Value.Replace(',', '.');
            }
            else if (NoCoveragePattern().Match(message) is { Success: true } noCoverage)
            {
                NoCoverage = TextHelpers.ParseNumberOrMax(noCoverage.Groups["count"].Value);
            }
            else if (message.StartsWith("Time Elapsed ", StringComparison.Ordinal))
            {
                Duration = MsBuildDiagnosticReport.FormatElapsed(message) is { Length: > 0 } elapsed ? elapsed : null;
            }
            else if (message.StartsWith("Stryker was unable to calculate a mutation score", StringComparison.Ordinal))
            {
                NoScore = true;
            }
        }

        /// <summary>Joins a report path Stryker wrapped at 80 columns: every line up to a blank or a log line.</summary>
        /// <param name="lines">The output's lines.</param>
        /// <param name="index">The path's first line.</param>
        /// <param name="reporter">The reporter the header names.</param>
        /// <returns>The index of the first line after the path.</returns>
        private int ReadReportPath(string[] lines, int index, string reporter)
        {
            var path = new StringBuilder();
            while (index < lines.Length && lines[index].Length > 0 && !LogLinePattern().IsMatch(lines[index])
                   && !ReportHeaderPattern().IsMatch(lines[index])
                   && !lines[index].StartsWith("You can open it", StringComparison.Ordinal))
            {
                path.Append(lines[index]);
                index++;
            }

            var local = LocalPath(path.ToString().Trim());
            if (local.Length > 0)
            {
                ReportPaths[reporter.ToLowerInvariant()] = local;
            }

            return index;
        }

        /// <summary>A <c>file://</c> URL as a path: <c>file:///a/b</c> is <c>/a/b</c>, <c>file:///C:/a</c> is <c>C:/a</c>.</summary>
        /// <param name="url">The printed URL.</param>
        private static string LocalPath(string url)
        {
            const string scheme = "file://";
            var path = url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? url[scheme.Length..] : url;
            return WindowsDrivePattern().IsMatch(path) ? path[1..] : path;
        }

        /// <summary>Takes the unwrapped MSBuild output of a failed build: the entry's text after <c>Result:</c> and
        /// every line up to the next log line.</summary>
        /// <param name="lines">The output's lines.</param>
        /// <param name="index">The <c>ERR</c> entry's line.</param>
        /// <param name="message">The entry's message.</param>
        /// <returns>The index of the next log line.</returns>
        private int ReadBuildOutput(string[] lines, int index, string message)
        {
            var marker = message.IndexOf(BuildResultMarker, StringComparison.Ordinal);
            var block = new StringBuilder(marker < 0 ? string.Empty : message[(marker + BuildResultMarker.Length)..]);
            index++;
            while (index < lines.Length && !LogLinePattern().IsMatch(lines[index]))
            {
                block.Append('\n').Append(lines[index]);
                index++;
            }

            BuildOutput = block.ToString();
            return index;
        }

        [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} (?<level>[A-Z]{3})\] (?<message>.*)$")]
        private static partial Regex LogLinePattern();

        [GeneratedRegex(@"^Your (?<reporter>\w+) report has been generated at:$")]
        private static partial Regex ReportHeaderPattern();

        [GeneratedRegex(@"^(?<status>Killed|Survived|Timeout):\s+(?<count>\d+)$")]
        private static partial Regex CountPattern();

        [GeneratedRegex(@"^The mutation score is lower than the configured break threshold of (?<threshold>\d+(?:[.,]\d+)?)%\.$")]
        private static partial Regex BreakPattern();

        [GeneratedRegex(@"^The final mutation score is (?<score>\d+(?:[.,]\d+)?) ?%$")]
        private static partial Regex ScorePattern();

        [GeneratedRegex(@"^(?<count>\d+)\s+mutants got status NoCoverage\.")]
        private static partial Regex NoCoveragePattern();

        [GeneratedRegex(@"^/[A-Za-z]:[/\\]")]
        private static partial Regex WindowsDrivePattern();
    }
}

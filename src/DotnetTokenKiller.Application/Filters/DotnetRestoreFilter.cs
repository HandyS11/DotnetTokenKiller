using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetRestoreFilter(string? rootPath = null) : IOutputFilter
{
    private const int MessageMaxLen = 200;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split(["\r\n", "\n"], StringSplitOptions.None);

        var errors = new List<NuGetError>();
        var restoredCount = 0;
        var upToDateCount = 0;
        var allUpToDate = false;
        double totalDurationMs = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // "  Restored /path/Project.csproj (in 123 ms)."
            var restoredMatch = RestoredPattern().Match(line);
            if (restoredMatch.Success)
            {
                restoredCount++;
                if (double.TryParse(restoredMatch.Groups["ms"].Value, CultureInfo.InvariantCulture, out var ms))
                {
                    totalDurationMs += ms;
                }

                continue;
            }

            // "All projects are up-to-date for restore."
            if (AllUpToDatePattern().IsMatch(line))
            {
                allUpToDate = true;
                continue;
            }

            // "3 of 5 projects are up-to-date for restore."
            var partialMatch = PartialUpToDatePattern().Match(line);
            if (partialMatch.Success)
            {
                if (int.TryParse(partialMatch.Groups["count"].Value, CultureInfo.InvariantCulture, out var count))
                {
                    upToDateCount = count;
                }

                continue;
            }

            // "/path/proj.csproj : error NU1101: message" (project path before error code)
            var errorProjFirstMatch = NuGetErrorProjectFirstPattern().Match(line);
            if (errorProjFirstMatch.Success)
            {
                var proj = TextHelpers.ShortenPath(errorProjFirstMatch.Groups["proj"].Value.Trim(), _rootPath);
                errors.Add(new NuGetError(
                    errorProjFirstMatch.Groups["code"].Value,
                    TextHelpers.Truncate(errorProjFirstMatch.Groups["message"].Value.Trim(), MessageMaxLen),
                    proj));
                continue;
            }

            // "error NU1101: message [/path/proj.csproj]" (standard NuGet error format)
            var errorStdMatch = NuGetErrorStandardPattern().Match(line);
            if (errorStdMatch.Success)
            {
                var projRaw = errorStdMatch.Groups["proj"].Value.Trim();
                var proj = string.IsNullOrEmpty(projRaw)
                    ? string.Empty
                    : TextHelpers.ShortenPath(projRaw, _rootPath);
                errors.Add(new NuGetError(
                    errorStdMatch.Groups["code"].Value,
                    TextHelpers.Truncate(errorStdMatch.Groups["message"].Value.Trim(), MessageMaxLen),
                    proj));
            }
        }

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"dotnet restore: {errors.Count} error{(errors.Count == 1 ? "" : "s")}");
            foreach (var e in errors)
            {
                if (string.IsNullOrEmpty(e.Project))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message}");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message} ({e.Project})");
                }
            }

            return sb.ToString();
        }

        var totalProjects = restoredCount + upToDateCount;

        if (totalProjects == 0 && allUpToDate)
        {
            return "✓ dotnet restore (all up-to-date)\n";
        }

        if (totalProjects == 0)
        {
            return string.Empty;
        }

        var elapsed = $"{totalDurationMs / 1000.0:F2}s";
        return $"✓ dotnet restore ({totalProjects} project{(totalProjects == 1 ? "" : "s")}, {elapsed})\n";
    }

    private sealed record NuGetError(string Code, string Message, string Project);

    // "  Restored /path/Project.csproj (in 123 ms)."
    [GeneratedRegex(@"^\s*Restored .+\.[a-z]+proj \(in (?<ms>[\d.]+) ms\)", RegexOptions.IgnoreCase)]
    private static partial Regex RestoredPattern();

    // "All projects are up-to-date for restore."
    [GeneratedRegex(@"All projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex AllUpToDatePattern();

    // "3 of 5 projects are up-to-date for restore."
    [GeneratedRegex(@"(?<count>\d+) of \d+ projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex PartialUpToDatePattern();

    // "/path/proj.csproj : error NU1101: message here"
    [GeneratedRegex(@"^\s*(?<proj>.+?\.[a-z]+proj)\s*:\s*error\s+(?<code>NU\d+):\s+(?<message>.+?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorProjectFirstPattern();

    // "error NU1101: message text [/path/proj.csproj]"
    [GeneratedRegex(@"error\s+(?<code>NU\d+):\s+(?<message>[^\[]+)(?:\s*\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorStandardPattern();
}

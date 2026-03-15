using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetFormatFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFilesShown = 20;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var formattedCount = 0;
        var warningFiles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duration = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var durationMatch = FormatCompletePattern().Match(line);
            if (durationMatch.Success)
            {
                duration = durationMatch.Groups["duration"].Value;
                continue;
            }

            if (FormattedFilePattern().IsMatch(line) || FormattedFilePatternSdk10().IsMatch(line))
            {
                formattedCount++;
                continue;
            }

            var warningMatch = WarningFilePattern().Match(line);
            if (!warningMatch.Success)
                warningMatch = WarningFilePatternSdk10().Match(line);

            if (warningMatch.Success)
            {
                var path = warningMatch.Groups["path"].Value.Trim();
                if (seen.Add(path))
                {
                    warningFiles.Add(path);
                }
            }
        }

        if (warningFiles.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"dotnet format: {warningFiles.Count} file{(warningFiles.Count == 1 ? "" : "s")} need formatting");
            foreach (var file in warningFiles.Count > MaxFilesShown
                         ? warningFiles.GetRange(0, MaxFilesShown)
                         : warningFiles)
            {
                sb.AppendLine(TextHelpers.ShortenPath(file, _rootPath));
            }

            if (warningFiles.Count > MaxFilesShown)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"+{warningFiles.Count - MaxFilesShown} more");
            }

            return sb.ToString();
        }

        if (formattedCount > 0)
        {
            var ctx = BuildContext(formattedCount, duration);
            return $"✓ dotnet format{ctx}\n";
        }

        return "✓ dotnet format (no changes)\n";
    }

    private static string BuildContext(int fileCount, string duration)
    {
        var fileStr = string.Create(CultureInfo.InvariantCulture, $"{fileCount} file{(fileCount == 1 ? "" : "s")}");
        return string.IsNullOrEmpty(duration)
            ? $" ({fileStr})"
            : $" ({fileStr}, {duration})";
    }

    // Matches: "Format complete in 0.21s."
    [GeneratedRegex(@"Format complete in (?<duration>\d+\.\d+s)\.")]
    private static partial Regex FormatCompletePattern();

    // Matches fix-mode lines (older SDK): "/path/to/file.cs formatted."
    [GeneratedRegex(@"formatted\.\s*$")]
    private static partial Regex FormattedFilePattern();

    // Matches fix-mode lines (SDK 10+): "Formatted code file '/path/to/file.cs'."
    [GeneratedRegex(@"^Formatted code file '")]
    private static partial Regex FormattedFilePatternSdk10();

    // Matches check-mode warning lines (older SDK): "/path/to/file.cs - warning IDE0055: ..."
    [GeneratedRegex(@"^\s*(?<path>.+?)\s+-\s+warning\s+")]
    private static partial Regex WarningFilePattern();

    // Matches check-mode diagnostic lines (SDK 10+): "/path/to/file.cs(9,1): error WHITESPACE: ..."
    [GeneratedRegex(@"^(?<path>.+?)\(\d+,\d+\)\s*:\s*(?:error|warning) ")]
    private static partial Regex WarningFilePatternSdk10();
}

using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetRunFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var sb = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (IsPreambleLine(line))
            {
                continue;
            }

            sb.AppendLine(line);
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result)
            ? "✓ dotnet run completed\n"
            : result;
    }

    private static bool IsPreambleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        return line.Contains("MSBuild version", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Determining projects to restore", StringComparison.OrdinalIgnoreCase)
               || line.Contains("All projects are up-to-date for restore", StringComparison.OrdinalIgnoreCase)
               || line.TrimStart().StartsWith("Restored ", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Build started", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Time Elapsed", StringComparison.OrdinalIgnoreCase)
               || BuildCountPattern().IsMatch(line)
               || ProjectOutputPattern().IsMatch(line);
    }

    [GeneratedRegex(@"^\s+\d+ (Warning|Error)\(s\)\s*$", RegexOptions.None, 1000)]
    private static partial Regex BuildCountPattern();

    [GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$", RegexOptions.None, 1000)]
    private static partial Regex ProjectOutputPattern();
}

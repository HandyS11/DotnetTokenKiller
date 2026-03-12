using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetNugetFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var isPush = false;
        var isClear = false;
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Contains("Your package was pushed", StringComparison.OrdinalIgnoreCase))
            {
                isPush = true;
                continue;
            }

            if (line.Contains("resources have been cleared", StringComparison.OrdinalIgnoreCase))
            {
                isClear = true;
                continue;
            }

            if (HttpNoisePattern().IsMatch(line) || PushPreamblePattern().IsMatch(line))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(line);
            }
        }

        if (isPush)
        {
            return "✓ nuget push succeeded\n";
        }

        if (isClear)
        {
            return "✓ nuget locals cleared\n";
        }

        return sb.ToString();
    }

    // Matches NuGet HTTP method lines: "  PUT https://...", "  GET https://...", "  Created https://...", "  OK https://..."
    [GeneratedRegex(@"^\s*(PUT|GET|Created|OK)\s+https?://", RegexOptions.IgnoreCase)]
    private static partial Regex HttpNoisePattern();

    // Matches push preamble: "Pushing Foo.1.0.0.nupkg to 'https://...'"
    [GeneratedRegex(@"^Pushing .+\.nupkg to '", RegexOptions.IgnoreCase)]
    private static partial Regex PushPreamblePattern();
}

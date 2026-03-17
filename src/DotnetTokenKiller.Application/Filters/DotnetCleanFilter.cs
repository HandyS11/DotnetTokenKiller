using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Text;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet clean output to a concise summary.</summary>
public sealed class DotnetCleanFilter : IOutputFilter
{
    /// <summary>Applies the filter to the raw clean output.</summary>
    /// <param name="rawOutput">The raw clean output to filter.</param>
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var failed = Array.Exists(lines,
            l => l.TrimEnd('\r').Contains("FAILED", StringComparison.OrdinalIgnoreCase));
        if (!failed)
        {
            return "✓ dotnet clean\n";
        }

        var sb = new StringBuilder();
        var count = 0;
        foreach (var rawLine in lines)
        {
            if (count >= 5)
            {
                break;
            }

            var line = rawLine.TrimEnd('\r');
            if (!line.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine(line);
            count++;
        }

        return sb.ToString();
    }
}

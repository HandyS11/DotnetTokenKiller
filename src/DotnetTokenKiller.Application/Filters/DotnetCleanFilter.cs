using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Text;

namespace DotnetTokenKiller.Application.Filters;

public sealed class DotnetCleanFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        try
        {
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
        catch
        {
            return rawOutput;
        }
    }
}

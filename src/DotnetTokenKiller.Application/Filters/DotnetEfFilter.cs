using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetEfFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var migrations = new List<string>();
        var applyingCount = 0;
        var isMigrationAdd = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var migMatch = MigrationListPattern().Match(line);
            if (migMatch.Success)
            {
                migrations.Add(migMatch.Groups["name"].Value);
                continue;
            }

            if (ApplyingMigrationPattern().IsMatch(line))
            {
                applyingCount++;
                continue;
            }

            if (line.Contains("To undo this action", StringComparison.OrdinalIgnoreCase))
            {
                isMigrationAdd = true;
            }
        }

        if (isMigrationAdd)
        {
            return "✓ migration added\n";
        }

        if (applyingCount > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"✓ database updated ({applyingCount} migration{(applyingCount == 1 ? "" : "s")} applied)\n");
        }

        if (migrations.Count > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{migrations.Count} migration{(migrations.Count == 1 ? "" : "s")} (latest: {migrations[^1]})\n");
        }

        return "✓ database updated (already up-to-date)\n";
    }

    // Matches migration list lines: "20231001000000_InitialCreate (Applied)" or "20231001000000_InitialCreate"
    [GeneratedRegex(@"^\s*(?<name>\d{14}_[A-Za-z0-9_]+)(\s+\(.*\))?\s*$")]
    private static partial Regex MigrationListPattern();

    // Matches database update lines: "Applying migration 'MigrationName'."
    [GeneratedRegex(@"Applying migration '(?<name>[^']+)'")]
    private static partial Regex ApplyingMigrationPattern();
}

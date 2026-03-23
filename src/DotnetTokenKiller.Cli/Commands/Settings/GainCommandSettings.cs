using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the gain command.</summary>
internal sealed class GainCommandSettings : CommandSettings
{
    /// <summary>Gets the number of history days to include.</summary>
    [CommandOption("--days")]
    [Description("Number of days of history to include (default: 30)")]
    public int Days { get; init; } = 30;

    /// <summary>Gets a value indicating whether to filter by current project.</summary>
    [CommandOption("--project")]
    [Description("Filter by current project directory")]
    public bool Project { get; init; }

    /// <summary>Gets a value indicating whether to output raw JSON.</summary>
    [CommandOption("--json")]
    [Description("Output raw JSON instead of table")]
    public bool Json { get; init; }

    /// <summary>Gets the export format for raw tracking records (e.g. "csv").</summary>
    [CommandOption("--export")]
    [Description("Export raw tracking records in the given format (csv)")]
    public string? Export { get; init; }

    /// <summary>Gets the command name to filter results by (e.g. "build", "test").</summary>
    [CommandOption("--command")]
    [Description("Filter results to a specific command (e.g. build, test, restore, clean)")]
    public string? Command { get; init; }
}

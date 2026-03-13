using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

public sealed class GainCommandSettings : CommandSettings
{
    [CommandOption("--days")]
    [Description("Number of days of history to include (default: 30)")]
    public int Days { get; init; } = 30;

    [CommandOption("--project")]
    [Description("Filter by current project directory")]
    public bool Project { get; init; }

    [CommandOption("--json")]
    [Description("Output raw JSON instead of table")]
    public bool Json { get; init; }
}

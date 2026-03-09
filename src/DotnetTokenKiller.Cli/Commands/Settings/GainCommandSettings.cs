using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

public sealed class GainCommandSettings : CommandSettings
{
    [CommandOption("--days")]
    [Description("Number of days of history to include")]
    public int Days { get; init; } = 7;
}

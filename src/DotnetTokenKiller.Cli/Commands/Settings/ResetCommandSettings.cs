using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

public sealed class ResetCommandSettings : CommandSettings
{
    [CommandOption("-f|--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; init; }
}

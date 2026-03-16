using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the reset command.</summary>
public sealed class ResetCommandSettings : CommandSettings
{
    /// <summary>Gets a value indicating whether to skip the confirmation prompt.</summary>
    [CommandOption("-f|--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; init; }
}

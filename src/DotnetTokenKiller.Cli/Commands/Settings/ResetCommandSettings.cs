using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the reset command.</summary>
internal sealed class ResetCommandSettings : CommandSettings
{
    /// <summary>Gets a value indicating whether to skip the confirmation prompt.</summary>
    [CommandOption("-f|--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; init; }

    /// <summary>Gets a value indicating whether to also remove tee logs and the configuration file.</summary>
    [CommandOption("--all")]
    [Description("Also remove tee logs and configuration file")]
    public bool All { get; init; }
}

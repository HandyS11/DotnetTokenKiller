using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings shared by all integrate subcommands.</summary>
internal sealed class IntegrateCommandSettings : CommandSettings
{
    /// <summary>Gets the target project directory (defaults to the current directory).</summary>
    [CommandOption("-d|--dir")]
    [Description("Target project root directory (default: current directory)")]
    public string? Directory { get; init; }

    /// <summary>Gets a value indicating whether to overwrite existing files.</summary>
    [CommandOption("-f|--force")]
    [Description("Overwrite existing files instead of skipping them")]
    public bool Force { get; init; }
}

using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the config set command.</summary>
internal sealed class ConfigSetCommandSettings : CommandSettings
{
    /// <summary>Gets the configuration key in dot-notation (e.g. <c>tracking.enabled</c>).</summary>
    [CommandArgument(0, "<key>")]
    [Description("Configuration key in dot-notation (e.g. tracking.enabled)")]
    public string Key { get; init; } = string.Empty;

    /// <summary>Gets the new value to assign.</summary>
    [CommandArgument(1, "<value>")]
    [Description("New value to assign (use empty string to reset to default)")]
    public string Value { get; init; } = string.Empty;
}

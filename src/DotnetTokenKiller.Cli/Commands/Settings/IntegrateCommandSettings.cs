using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the integrate command.</summary>
internal sealed class IntegrateCommandSettings : CommandSettings
{
    /// <summary>Gets the AI assistant provider to integrate with.</summary>
    [CommandArgument(0, "<provider>")]
    [Description(
        "AI assistant provider to integrate (claude, copilot, gemini, cursor, windsurf, aider, jetbrains)")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>Gets the target project directory (defaults to the current directory).</summary>
    [CommandOption("-d|--dir")]
    [Description("Target project root directory (default: current directory)")]
    public string? Directory { get; init; }

    /// <summary>Gets a value indicating whether to overwrite existing files.</summary>
    [CommandOption("-f|--force")]
    [Description("Overwrite existing files instead of skipping them")]
    public bool Force { get; init; }
}

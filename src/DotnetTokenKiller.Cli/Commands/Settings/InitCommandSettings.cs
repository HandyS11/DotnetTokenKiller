using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the init command.</summary>
internal sealed class InitCommandSettings : CommandSettings
{
    /// <summary>Gets the AI assistant provider to integrate with.</summary>
    [CommandArgument(0, "<provider>")]
    [Description(
        "AI assistant provider to set up (claude, copilot, copilot-cli, gemini, codex, opencode, antigravity, cursor, windsurf, aider, jetbrains)")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>Gets the target project directory (defaults to the current directory).</summary>
    [CommandOption("-d|--dir")]
    [Description("Target project root directory (default: current directory)")]
    public string? Directory { get; init; }

    /// <summary>Gets a value indicating whether to overwrite existing files.</summary>
    [CommandOption("-f|--force")]
    [Description("Overwrite existing files instead of skipping them")]
    public bool Force { get; init; }

    /// <summary>Gets a value indicating whether to install into the user's home config instead of the project.</summary>
    [CommandOption("-g|--global")]
    [Description("Install into the user's home config (claude, copilot-cli, gemini, codex, opencode, antigravity, aider) instead of a project")]
    public bool Global { get; init; }
}

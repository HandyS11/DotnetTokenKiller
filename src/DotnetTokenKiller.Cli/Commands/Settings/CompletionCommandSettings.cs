using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the completion command.</summary>
internal sealed class CompletionCommandSettings : CommandSettings
{
    /// <summary>Gets the target shell.</summary>
    [CommandArgument(0, "<shell>")]
    [Description("Shell to generate completion for: bash, zsh, fish, powershell")]
    public string Shell { get; init; } = string.Empty;
}

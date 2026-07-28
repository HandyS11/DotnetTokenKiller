using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the <c>log</c> command.</summary>
/// <remarks>
/// Deliberately not derived from <see cref="OutputDisplaySettings"/>: those flags describe how a
/// filtered run presents itself, and none of them apply to retrieving output from disk.
/// </remarks>
internal sealed class LogCommandSettings : CommandSettings
{
    /// <summary>Gets the dotnet subcommand tokens to restrict the search to.</summary>
    [CommandArgument(0, "[subcommand]")]
    [Description("Only show logs for this dotnet subcommand, e.g. build or list package")]
    public string[] Subcommand { get; init; } = [];

    /// <summary>Gets a value indicating whether to list matching logs instead of showing one.</summary>
    [CommandOption("--list")]
    [Description("List the available logs instead of printing one")]
    public bool List { get; init; }

    /// <summary>Gets which of the matches to show, 1-based and newest first.</summary>
    [CommandOption("--index")]
    [Description("Which of the matching logs to show, newest first (default 1)")]
    [DefaultValue(1)]
    public int Index { get; init; } = 1;

    /// <summary>Gets how many trailing lines to print.</summary>
    [CommandOption("--lines")]
    [Description("How many trailing lines to print (default 100)")]
    [DefaultValue(100)]
    public int Lines { get; init; } = 100;

    /// <summary>Gets a value indicating whether to print the whole log.</summary>
    [CommandOption("--full")]
    [Description("Print the whole log instead of a trailing window")]
    public bool Full { get; init; }

    /// <summary>Gets a value indicating whether to include logs from other projects.</summary>
    [CommandOption("--all")]
    [Description("Include logs from every project, not just this directory")]
    public bool All { get; init; }

    /// <inheritdoc/>
    public override ValidationResult Validate()
    {
        if (Lines < 1)
        {
            return ValidationResult.Error("--lines must be 1 or greater.");
        }

        return Index < 1
            ? ValidationResult.Error("--index must be 1 or greater.")
            : ValidationResult.Success();
    }
}

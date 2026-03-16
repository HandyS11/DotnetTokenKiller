using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for dotnet subcommands.</summary>
public class DotnetCommandSettings : CommandSettings
{
    /// <summary>Gets the verbosity level flags.</summary>
    [CommandOption("-v|--verbose")]
    [Description("Increase verbosity (use -v for level 1, -v -v for level 2)")]
    public bool[] Verbose { get; init; } = [];

    /// <summary>Gets additional arguments forwarded to dotnet.</summary>
    [CommandArgument(0, "[args]")]
    [Description("Arguments to forward to the underlying dotnet process")]
    public string[] PositionalArgs { get; init; } = [];
}

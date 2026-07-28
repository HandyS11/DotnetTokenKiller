using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for dotnet subcommands.</summary>
internal sealed class DotnetCommandSettings : OutputDisplaySettings
{
    /// <summary>Gets additional arguments forwarded to dotnet.</summary>
    [CommandArgument(0, "[args]")]
    [Description("Arguments to forward to the underlying dotnet process")]
    public string[] PositionalArgs { get; init; } = [];
}

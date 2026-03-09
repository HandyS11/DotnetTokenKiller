using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotnetTokenKiller.Cli.Commands.Settings;

public class DotnetCommandSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    [Description("Increase verbosity (use -v for level 1, -v -v for level 2)")]
    public bool[] Verbose { get; init; } = [];

    [CommandArgument(0, "[args]")]
    [Description("Arguments to forward to the underlying dotnet process")]
    public string[] PositionalArgs { get; init; } = [];
}

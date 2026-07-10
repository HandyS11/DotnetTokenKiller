using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for dotnet subcommands.</summary>
internal sealed class DotnetCommandSettings : CommandSettings
{
    /// <summary>Gets the verbosity level flags.</summary>
    [CommandOption("-v|--verbose")]
    [Description("Increase verbosity (use -v for level 1, -v -v for level 2)")]
    public bool[] Verbose { get; init; } = [];

    /// <summary>Gets a value indicating whether to print the path to the full log file.</summary>
    [CommandOption("--show-log")]
    [Description("Print the path to the full log file when the output was saved")]
    public bool ShowLog { get; init; }

    /// <summary>Gets a value indicating whether to suppress DTK decorative output.</summary>
    [CommandOption("-q|--quiet")]
    [Description("Suppress all DTK meta-output; forward only the filtered content")]
    public bool Quiet { get; init; }

    /// <summary>Gets additional arguments forwarded to dotnet.</summary>
    [CommandArgument(0, "[args]")]
    [Description("Arguments to forward to the underlying dotnet process")]
    public string[] PositionalArgs { get; init; } = [];
}

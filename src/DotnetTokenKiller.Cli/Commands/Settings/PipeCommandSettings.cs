using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for the <c>pipe</c> command.</summary>
internal sealed class PipeCommandSettings : OutputDisplaySettings
{
    /// <summary>Gets the dotnet subcommand tokens naming the filter to apply.</summary>
    [CommandArgument(0, "<subcommand>")]
    [Description("The dotnet subcommand whose filter to apply, e.g. build or list package")]
    public string[] Subcommand { get; init; } = [];

    /// <summary>Gets the exit code of the command that produced the piped output.</summary>
    /// <remarks>
    /// A shell pipe cannot give dtk the producing command's status, and every filter treats the
    /// exit code as the sole source of the success/failure verdict. Defaulting to 0 means the plain
    /// pipe form is honest only about success; pass this explicitly for an accurate verdict.
    /// </remarks>
    [CommandOption("--exit-code")]
    [Description("Exit code of the command that produced the output (default 0)")]
    [DefaultValue(0)]
    public int ExitCode { get; init; }
}

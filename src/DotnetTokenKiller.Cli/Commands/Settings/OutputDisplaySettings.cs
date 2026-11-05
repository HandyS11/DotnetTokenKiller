using System.ComponentModel;
using DotnetTokenKiller.Application.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Display flags shared by every command that emits filtered output.</summary>
internal abstract class OutputDisplaySettings : CommandSettings
{
    /// <summary>Gets a value indicating whether to echo the resolved command line before running (verbosity level 1).</summary>
    [CommandOption("-v|--verbose")]
    [Description("Echo the resolved command line before running")]
    public bool Verbose { get; init; }

    /// <summary>Gets a value indicating whether to also dump the raw dotnet output and timing (verbosity level 2).</summary>
    [CommandOption("--vv")]
    [Description("Also dump the raw dotnet output and elapsed time")]
    public bool VeryVerbose { get; init; }

    /// <summary>Gets the effective verbosity level: 0 (default), 1 (<c>-v</c>), or 2 (<c>--vv</c>).</summary>
    public int VerbosityLevel => (VeryVerbose, Verbose) switch
    {
        (true, _) => 2,
        (_, true) => 1,
        _ => 0
    };

    /// <summary>Gets a value indicating whether to print the path to the full log file.</summary>
    [CommandOption("--show-log")]
    [Description("Print the path to the full log file when the output was saved")]
    public bool ShowLog { get; init; }

    /// <summary>Gets a value indicating whether to suppress DTK decorative output.</summary>
    [CommandOption("-q|--quiet")]
    [Description("Suppress all DTK meta-output; forward only the filtered content")]
    public bool Quiet { get; init; }

    /// <summary>Gets the path the run's raw output is also written to, or <see langword="null"/> for none.</summary>
    [CommandOption("--tee <PATH>")]
    [Description("Also write the raw output to PATH")]
    public string? TeePath { get; init; }

    /// <summary>Gets the most lines of filtered output to print before the status line, or 0 for no limit.</summary>
    [CommandOption("--max-lines <N>")]
    [Description("Print at most N lines of filtered output (0: no limit)")]
    [DefaultValue(0)]
    public int MaxLines { get; init; }

    /// <inheritdoc/>
    public override ValidationResult Validate() =>
        MaxLines < 0 ? ValidationResult.Error("--max-lines must be 0 or more") : base.Validate();

    /// <summary>Projects these flags onto the Application-layer options record.</summary>
    /// <returns>The equivalent <see cref="OutputOptions"/>.</returns>
    public OutputOptions ToOutputOptions() => new(VerbosityLevel, ShowLog, Quiet, TeePath, MaxLines);
}

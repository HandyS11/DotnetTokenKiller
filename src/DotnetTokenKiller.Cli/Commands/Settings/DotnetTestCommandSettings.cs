using System.ComponentModel;
using DotnetTokenKiller.Domain.Filters;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands.Settings;

/// <summary>Settings for <c>dotnet test</c>.</summary>
internal sealed class DotnetTestCommandSettings : OutputDisplaySettings
{
    /// <summary>Gets additional arguments forwarded to dotnet.</summary>
    [CommandArgument(0, "[args]")]
    [Description("Arguments to forward to the underlying dotnet process")]
    public string[] PositionalArgs { get; init; } = [];

    /// <summary>Gets a value indicating whether to list the test names.</summary>
    [CommandOption("--names")]
    [Description("List the names of the tests that ran")]
    public bool Names { get; init; }

    /// <summary>Gets a value indicating whether to show the tests' own console output.</summary>
    [CommandOption("--show-output")]
    [Description("Show the console output the tests wrote")]
    public bool ShowOutput { get; init; }

    /// <summary>Gets a value indicating whether a run in which no test matched fails.</summary>
    [CommandOption("--fail-on-empty")]
    [Description("Fail when no test matched")]
    public bool FailOnEmpty { get; init; }

    /// <summary>Projects the test flags onto the Domain-layer options record.</summary>
    /// <returns>The equivalent <see cref="TestOptions"/>.</returns>
    public TestOptions ToTestOptions() => new(Names, ShowOutput, FailOnEmpty);
}

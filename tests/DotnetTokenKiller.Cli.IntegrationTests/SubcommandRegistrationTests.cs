using DotnetTokenKiller.Domain;
using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Binds the Spectre command tree to <see cref="DotnetSubcommands"/>. The help snapshots lock the
/// wording of what is registered; this locks the <em>set</em>, so adding a canonical subcommand
/// without registering a command fails here rather than at a user's terminal.
/// </summary>
public sealed class SubcommandRegistrationTests
{
    [Fact]
    public void DotnetBranch_RegistersExactlyTheCanonicalSubcommands()
    {
        var registered = ParseCommandsSection(RunHelp("dotnet", "--help"));

        registered.Should().Equal(
            DotnetSubcommands.Ordered,
            "the dotnet branch must register every canonical subcommand, in canonical order, and nothing else");
    }

    /// <summary>
    /// Extracts the command names from the COMMANDS section of a Spectre help page. The section is
    /// the last block on the page; its entries are indented four spaces and start with the name.
    /// </summary>
    /// <param name="help">Rendered help output.</param>
    private static List<string> ParseCommandsSection(string help)
    {
        var lines = help.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "COMMANDS:");

        start.Should().BeGreaterThan(-1, "the dotnet branch help must have a COMMANDS section");

        return
        [
            .. lines
                .Skip(start + 1)
                .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim().Split(' ')[0]),
        ];
    }

    private static string RunHelp(params string[] args)
    {
        var console = new TestConsole().Width(100);
        var app = new CommandApp();
        app.Configure(config =>
        {
            CliConfigurator.Configure(config, "1.2.3-test");
            config.Settings.Console = console;
        });

        app.Run(args);
        return console.Output;
    }
}

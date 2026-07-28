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
        RunHelp("dotnet", "--help").Should().Contain(
            "COMMANDS:",
            "the dotnet branch help must have a COMMANDS section to read the registrations from");

        var registered = ParseSubcommandTree(["dotnet"]);

        registered.Should().Equal(
            DotnetSubcommands.Ordered,
            "the dotnet branch must register every canonical subcommand, in canonical order, and nothing "
            + "else — a multi-token name such as 'list package' is registered as a nested branch, so it "
            + "only counts as registered once the command inside that branch exists too");
    }

    /// <summary>
    /// Depth-first walk of the Spectre command tree below <paramref name="path"/>, yielding one
    /// space-joined name per leaf command, relative to <paramref name="path"/>. A help page with a
    /// COMMANDS section is a branch and is descended into; one without is a leaf. Walking rather
    /// than reading a single page is what keeps the assertion on whole canonical names: registering
    /// a <c>list</c> branch and forgetting <c>package</c> inside it yields nothing for that branch.
    /// </summary>
    /// <param name="path">Command path to walk below, starting at the application's first level.</param>
    private static List<string> ParseSubcommandTree(string[] path)
    {
        var names = new List<string>();
        foreach (var name in ParseCommandsSection(RunHelp([.. path, "--help"])))
        {
            var nested = ParseSubcommandTree([.. path, name]);
            names.AddRange(nested.Count == 0 ? [name] : nested.Select(child => $"{name} {child}"));
        }

        return names;
    }

    /// <summary>
    /// Extracts the command names from the COMMANDS section of a Spectre help page, or an empty
    /// list when the page has none (a leaf command). The section is the last block on the page;
    /// its entries are indented four spaces and start with the name.
    /// </summary>
    /// <param name="help">Rendered help output.</param>
    private static List<string> ParseCommandsSection(string help)
    {
        var lines = help.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "COMMANDS:");

        if (start < 0)
        {
            return [];
        }

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

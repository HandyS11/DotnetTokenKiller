using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public sealed class CompletionCommandTests
{
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ExecuteAsync_KnownShell_ReturnsZeroAndPrintsScript(string shell)
    {
        var (command, _, writer) = Create();

        var exitCode = await command.RunAsync(new CompletionCommandSettings
        {
            Shell = shell
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("powershell")]
    public async Task ExecuteAsync_Script_OffersEveryDotnetSubcommand(string shell)
    {
        // Anti-drift: the completion lists are generated from
        // CompletionCandidates(DotnetSubcommands.Ordered), so every candidate must appear in every
        // shell script. A multi-token subcommand contributes only its first token (`list package`
        // yields `list`), so iterating the canonical names themselves would assert on text the
        // scripts deliberately never emit — completing the remaining tokens is not implemented.
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = shell
        }, CancellationToken.None);

        var script = writer.ToString();
        foreach (var subcommand in CompletionCommand.CompletionCandidates(DotnetSubcommands.Ordered))
        {
            script.Should().Contain(subcommand, "the {0} completion must offer '{1}'", shell, subcommand);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BashShell_ContainsDtkSubcommands()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "bash"
        }, CancellationToken.None);

        var script = writer.ToString();
        script.Should().Contain("dotnet");
        script.Should().Contain("integrate");
        script.Should().Contain("complete -F _dtk_completion dtk");
    }

    [Fact]
    public async Task ExecuteAsync_ZshShell_ContainsDtkSubcommands()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "zsh"
        }, CancellationToken.None);

        var script = writer.ToString();
        script.Should().Contain("#compdef dtk");
        script.Should().Contain("dotnet");
        script.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_FishShell_ContainsDtkSubcommands()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "fish"
        }, CancellationToken.None);

        var script = writer.ToString();
        script.Should().Contain("complete -c dtk");
        script.Should().Contain("dotnet");
        script.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellShell_ContainsDtkSubcommands()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "powershell"
        }, CancellationToken.None);

        var script = writer.ToString();
        script.Should().Contain("Register-ArgumentCompleter");
        script.Should().Contain("dotnet");
        script.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownShell_ReturnsOneAndPrintsError()
    {
        var (command, console, _) = Create();

        var exitCode = await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "unknownshell"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error");
        console.Output.Should().Contain("unknownshell");
    }

    [Fact]
    public async Task ExecuteAsync_ShellNameCaseInsensitive_ReturnsZero()
    {
        var (command, _, _) = Create();

        var exitCode = await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "BASH"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
    }

    [Fact]
    public void CompletionCandidates_MultiTokenSubcommand_YieldsItsFirstTokenOnly()
    {
        CompletionCommand.CompletionCandidates(["build", "list package"])
            .Should().Equal("build", "list");
    }

    [Fact]
    public void CompletionCandidates_TwoSubcommandsSharingAFirstToken_AreDeduplicated()
    {
        CompletionCommand.CompletionCandidates(["list package", "list reference"])
            .Should().Equal("list");
    }

    [Fact]
    public async Task RunAsync_FishScript_EmitsNoCandidateContainingASpace()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = "fish"
        }, CancellationToken.None);

        var output = writer.ToString();

        // `complete ... -a list package -d '...'` is malformed: fish reads `package` as another
        // argument to `complete`, so the whole completion silently stops working.
        // Some static sections of the fish template pad the candidate with extra trailing spaces
        // for column alignment (e.g. `-a dotnet     -d '...'`); that padding is collapsed by
        // fish's own word-splitting and is harmless, so it is trimmed here before asserting —
        // only a genuine embedded space (a real second token) should fail this check.
        foreach (var line in output.Split('\n').Where(l => l.Contains(" -a ", StringComparison.Ordinal)))
        {
            var candidate = line.Split(" -a ")[1].Split(" -d ")[0].Trim();
            candidate.Should().NotContain(" ", "candidate '{0}' would break the generated script", candidate);
        }
    }

    private static (CompletionCommand command, TestConsole console, StringWriter writer) Create()
    {
        var console = new TestConsole();
        var writer = new StringWriter();
        return (new CompletionCommand(console, writer), console, writer);
    }
}

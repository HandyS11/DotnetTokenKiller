using DotnetTokenKiller.Cli;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
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
        // Anti-drift: the completion lists are generated from ArgumentPreprocessor.KnownSubcommands,
        // so every supported subcommand (including 'format') must appear in every shell script.
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings
        {
            Shell = shell
        }, CancellationToken.None);

        var script = writer.ToString();
        foreach (var subcommand in ArgumentPreprocessor.KnownSubcommands)
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

    private static (CompletionCommand command, TestConsole console, StringWriter writer) Create()
    {
        var console = new TestConsole();
        var writer = new StringWriter();
        return (new CompletionCommand(console, writer), console, writer);
    }
}

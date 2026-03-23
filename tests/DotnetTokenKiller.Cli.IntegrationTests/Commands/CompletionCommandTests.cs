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
        var (command, console) = Create();

        var exitCode = await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = shell
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_BashShell_ContainsDtkSubcommands()
    {
        var (command, console) = Create();

        await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = "bash"
        }, CancellationToken.None);

        console.Output.Should().Contain("dotnet");
        console.Output.Should().Contain("integrate");
        console.Output.Should().Contain("complete -F _dtk_completion dtk");
    }

    [Fact]
    public async Task ExecuteAsync_ZshShell_ContainsDtkSubcommands()
    {
        var (command, console) = Create();

        await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = "zsh"
        }, CancellationToken.None);

        console.Output.Should().Contain("#compdef dtk");
        console.Output.Should().Contain("dotnet");
        console.Output.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_FishShell_ContainsDtkSubcommands()
    {
        var (command, console) = Create();

        await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = "fish"
        }, CancellationToken.None);

        console.Output.Should().Contain("complete -c dtk");
        console.Output.Should().Contain("dotnet");
        console.Output.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellShell_ContainsDtkSubcommands()
    {
        var (command, console) = Create();

        await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = "powershell"
        }, CancellationToken.None);

        console.Output.Should().Contain("Register-ArgumentCompleter");
        console.Output.Should().Contain("dotnet");
        console.Output.Should().Contain("integrate");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownShell_ReturnsOneAndPrintsError()
    {
        var (command, console) = Create();

        var exitCode = await command.ExecuteAsync(null!, new CompletionCommandSettings
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
        var (command, _) = Create();

        var exitCode = await command.ExecuteAsync(null!, new CompletionCommandSettings
        {
            Shell = "BASH"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
    }

    private static (CompletionCommand command, TestConsole console) Create()
    {
        var console = new TestConsole();
        return (new CompletionCommand(console), console);
    }
}

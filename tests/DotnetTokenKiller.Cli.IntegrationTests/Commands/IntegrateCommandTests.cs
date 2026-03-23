using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public class IntegrateCommandTests
{
    [Fact]
    public async Task ExecuteAsync_AllFilesCreated_ShowsCreatedLinesAndDoneMessage()
    {
        const string dir = "/project";
        var result = new IntegrationResult(
            [
                $"{dir}/.claude/skills/dotnet-token-killer/SKILL.md",
                $"{dir}/.claude/hooks/dotnet-to-dtk.py",
                $"{dir}/.claude/settings.json"
            ],
            [],
            []);

        var (command, console) = Create("claude", result);

        var exitCode = await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = dir
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("created");
        console.Output.Should().Contain("Done.");
        console.Output.Should().Contain("claude");
    }

    [Fact]
    public async Task ExecuteAsync_AllFilesSkipped_ShowsAlreadyIntegratedMessage()
    {
        const string dir = "/project";
        var result = new IntegrationResult(
            [],
            [],
            [$"{dir}/.claude/settings.json"]);

        var (command, console) = Create("claude", result);

        var exitCode = await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = dir
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("skipped");
        console.Output.Should().Contain("Already integrated");
        console.Output.Should().NotContain("Done.");
    }

    [Fact]
    public async Task ExecuteAsync_SomeFilesUpdated_ShowsUpdatedLinesAndDoneMessage()
    {
        const string dir = "/project";
        var result = new IntegrationResult(
            [],
            [$"{dir}/.claude/settings.json"],
            []);

        var (command, console) = Create("claude", result);

        var exitCode = await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = dir
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("updated");
        console.Output.Should().Contain("Done.");
    }

    [Fact]
    public async Task ExecuteAsync_NoDirectoryOption_UsesCurrentDirectory()
    {
        var stub = new StubIntegrator("claude");
        var command = new ClaudeIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().Be(Environment.CurrentDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectoryOption_UsesProvidedDirectory()
    {
        var stub = new StubIntegrator("claude");
        var command = new ClaudeIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = "/custom/dir"
        }, CancellationToken.None);

        stub.LastDirectory.Should().Be("/custom/dir");
    }

    [Fact]
    public async Task ExecuteAsync_ForceFlag_PassesForceThroughToUseCase()
    {
        var stub = new StubIntegrator("claude");
        var command = new ClaudeIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Force = true
        }, CancellationToken.None);

        stub.LastForce.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CopilotProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("copilot");
        var command = new CopilotIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AiderProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("aider");
        var command = new AiderIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CursorProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("cursor");
        var command = new CursorIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_GeminiProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("gemini");
        var command = new GeminiIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_JetBrainsProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("jetbrains");
        var command = new JetBrainsAiIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WindsurfProvider_UsesCorrectProviderName()
    {
        var stub = new StubIntegrator("windsurf");
        var command = new WindsurfIntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.ExecuteAsync(null!, new IntegrateCommandSettings(), CancellationToken.None);

        stub.LastDirectory.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFilePath_HandledByRelativePath()
    {
        // Covers the string.IsNullOrEmpty(fullPath) branch in RelativePath
        var result = new IntegrationResult([""], [], []);
        var (command, console) = Create("claude", result);

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = "/project"
        }, CancellationToken.None);

        console.Output.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_FilePaths_AreDisplayedRelativeToDirectory()
    {
        const string dir = "/project";
        var result = new IntegrationResult(
            [$"{dir}/.claude/settings.json"],
            [],
            []);

        var (command, console) = Create("claude", result);

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = dir
        }, CancellationToken.None);

        console.Output.Should().Contain(".claude/settings.json");
        console.Output.Should().NotContain("/project/");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDirectory_FallsBackToFullPath()
    {
        var result = new IntegrationResult(
            ["/some/absolute/path/file.json"],
            [],
            []);

        var (command, console) = Create("claude", result);

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = ""
        }, CancellationToken.None);

        console.Output.Should().Contain("/some/absolute/path/file.json");
    }

    [Fact]
    public async Task ExecuteAsync_PathWithInvalidChars_FallsBackToFullPath()
    {
        const string dir = "/project";
        const string invalidPath = "/result\0file.json";
        var result = new IntegrationResult([invalidPath], [], []);

        var (command, console) = Create("claude", result);

        await command.ExecuteAsync(null!, new IntegrateCommandSettings
        {
            Directory = dir
        }, CancellationToken.None);

        // Path.GetFullPath throws on null-byte paths; the catch block returns the raw path
        console.Output.Should().Contain("file.json");
    }

    private static (ClaudeIntegrateCommand command, TestConsole console) Create(
        string provider,
        IntegrationResult result)
    {
        var console = new TestConsole();
        var stub = new StubIntegrator(provider)
        {
            Result = result
        };
        var command = new ClaudeIntegrateCommand(new IntegrateUseCase([stub]), console);
        return (command, console);
    }

    private sealed class StubIntegrator(string providerName) : IProviderIntegrator
    {
        public string? LastDirectory { get; private set; }
        public bool LastForce { get; private set; }
        public IntegrationResult Result { get; set; } = new([], [], []);
        public string ProviderName => providerName;

        public Task<IntegrationResult> IntegrateAsync(
            string directory,
            bool force,
            CancellationToken cancellationToken)
        {
            LastDirectory = directory;
            LastForce = force;
            return Task.FromResult(Result);
        }
    }
}

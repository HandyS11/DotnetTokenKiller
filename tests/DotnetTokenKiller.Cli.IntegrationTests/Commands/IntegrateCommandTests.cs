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

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
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

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("skipped");
        console.Output.Should().Contain("Already integrated");
        console.Output.Should().NotContain("Done.");
    }

    [Fact]
    public async Task ExecuteAsync_NoForce_SkippedFiles_ShowForceHint()
    {
        const string dir = "/project";
        var result = new IntegrationResult(
            [],
            [],
            [$"{dir}/.claude/settings.json"]);

        var (command, console) = Create("claude", result);

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir,
            Force = false
        }, CancellationToken.None);

        console.Output.Should().Contain("use --force to overwrite");
    }

    [Fact]
    public async Task ExecuteAsync_ForceFlag_SkippedFiles_DoNotShowForceHint()
    {
        // Behavior (a): once --force was already passed, the "use --force to overwrite" hint is
        // never honest — any leftover skip (e.g. an idempotent hook merge) isn't fixed by force.
        const string dir = "/project";
        var result = new IntegrationResult(
            [],
            [],
            [$"{dir}/.claude/settings.json"]);

        var (command, console) = Create("claude", result);

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir,
            Force = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("skipped");
        console.Output.Should().NotContain("use --force to overwrite");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProvider_PrintsErrorAndReturnsExitCodeOne()
    {
        // IntegrateCommand validates settings.Provider against IntegrateUseCase.AvailableProviders
        // before calling into the use case, so an unknown provider is a friendly CLI error rather
        // than an unhandled exception or a raw dictionary-lookup failure.
        var console = new TestConsole();
        var command = new IntegrateCommand(new IntegrateUseCase([]), console);

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error:");
        console.Output.Should().Contain("claude");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProvider_ListsAvailableProviders()
    {
        var console = new TestConsole();
        var command = new IntegrateCommand(
            new IntegrateUseCase(
            [
                new StubIntegrator("claude"),
                new StubIntegrator("copilot")
            ]),
            console);

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "bogus"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error:");
        console.Output.Should().Contain("bogus");
        console.Output.Should().Contain("claude");
        console.Output.Should().Contain("copilot");
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

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
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
        var command = new IntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude"
        }, CancellationToken.None);

        stub.LastDirectory.Should().Be(Environment.CurrentDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_WithDirectoryOption_UsesProvidedDirectory()
    {
        var stub = new StubIntegrator("claude");
        var command = new IntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = "/custom/dir"
        }, CancellationToken.None);

        stub.LastDirectory.Should().Be("/custom/dir");
    }

    [Fact]
    public async Task ExecuteAsync_ForceFlag_PassesForceThroughToUseCase()
    {
        var stub = new StubIntegrator("claude");
        var command = new IntegrateCommand(new IntegrateUseCase([stub]), new TestConsole());

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Force = true
        }, CancellationToken.None);

        stub.LastForce.Should().BeTrue();
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("copilot")]
    [InlineData("gemini")]
    [InlineData("cursor")]
    [InlineData("windsurf")]
    [InlineData("aider")]
    [InlineData("jetbrains")]
    public async Task ExecuteAsync_EveryProviderName_RoutesToMatchingIntegrator(string provider)
    {
        var stub = new StubIntegrator(provider);
        var otherStub = new StubIntegrator($"not-{provider}");
        var command = new IntegrateCommand(new IntegrateUseCase([stub, otherStub]), new TestConsole());

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = provider
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        stub.LastDirectory.Should().NotBeNull();
        otherStub.LastDirectory.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFilePath_HandledByRelativePath()
    {
        // Covers the string.IsNullOrEmpty(fullPath) branch in RelativePath
        var result = new IntegrationResult([""], [], []);
        var (command, console) = Create("claude", result);

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
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

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
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

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
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

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir
        }, CancellationToken.None);

        // Path.GetFullPath throws on null-byte paths; the catch block returns the raw path
        console.Output.Should().Contain("file.json");
    }

    private static (IntegrateCommand command, TestConsole console) Create(
        string provider,
        IntegrationResult result)
    {
        var console = new TestConsole();
        var stub = new StubIntegrator(provider)
        {
            Result = result
        };
        var command = new IntegrateCommand(new IntegrateUseCase([stub]), console);
        return (command, console);
    }

    private sealed class StubIntegrator(string providerName) : IProviderIntegrator
    {
        public string? LastDirectory { get; private set; }
        public bool LastForce { get; private set; }
        public IntegrationResult Result { get; init; } = new([], [], []);
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

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
    public async Task ExecuteAsync_AllFilesSkipped_NoForce_DoesNotClaimAlreadyIntegratedAndMentionsForce()
    {
        // Honest summaries: under skip-unless-force, a skipped file (e.g. a pre-existing
        // .aider.conf.yml without the dtk read: key) might never have been functionally
        // integrated. The CLI cannot distinguish that from a file that already carries dtk's
        // exact managed content, so without --force it must never claim completion.
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
            Force = false
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("skipped");
        console.Output.Should().NotContain("Already integrated");
        console.Output.Should().NotContain("Done.");
        console.Output.Should().Contain("--force");
    }

    [Fact]
    public async Task ExecuteAsync_AllFilesSkipped_WithForce_ShowsAlreadyIntegratedMessage()
    {
        // Under --force, ShouldSkipWrite-based skips can never fire (they require !force), so any
        // remaining skip must come from content-based detection (e.g. the hook command is already
        // registered) — "Already integrated" is honest here.
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
        console.Output.Should().Contain("Already integrated");
        console.Output.Should().NotContain("Done.");
    }

    [Fact]
    public async Task ExecuteAsync_NoForce_SomeCreatedSomeSkipped_DoesNotClaimDoneAndMentionsForce()
    {
        // Reproduces the dishonest "Done." from a pre-existing .aider.conf.yml: the instructions
        // file is newly created, but the conf file (the only functional wiring) is skipped because
        // it pre-existed without --force. Claiming "Done." here is false — the integration does
        // not actually work yet.
        const string dir = "/project";
        var result = new IntegrationResult(
            [$"{dir}/.aider-dtk-instructions.md"],
            [],
            [$"{dir}/.aider.conf.yml"]);

        var (command, console) = Create("aider", result);

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "aider",
            Directory = dir,
            Force = false
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("created");
        console.Output.Should().Contain("skipped");
        console.Output.Should().NotContain("Done.");
        console.Output.Should().Contain("--force");
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

        // Force *merges/appends* the managed section and preserves user content — it never
        // overwrites — so the hint must not claim otherwise.
        console.Output.Should().Contain("use --force to integrate into existing files");
    }

    [Fact]
    public async Task ExecuteAsync_ForceFlag_SkippedFiles_DoNotShowForceHint()
    {
        // Behavior (a): once --force was already passed, the "use --force" hint is never honest —
        // any leftover skip (e.g. an idempotent hook merge) isn't fixed by force.
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
        console.Output.Should().NotContain("use --force to integrate into existing files");
    }

    [Fact]
    public async Task ExecuteAsync_NoProvidersRegistered_PrintsErrorAndReturnsExitCodeOne()
    {
        // With an empty provider registry, every provider name is unknown, so IntegrateCommand's
        // validation against AvailableProviders produces a friendly CLI error and exit 1 rather than
        // an unhandled exception. (A genuinely-unknown provider against a populated registry is
        // covered by ExecuteAsync_UnknownProvider_ListsAvailableProviders.)
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
    public async Task ExecuteAsync_MalformedSettingsJson_PrintsErrorAndReturnsExitCodeOne()
    {
        // IntegratorHelpers.MergeJsonSettingsAsync throws InvalidOperationException on a
        // malformed/non-object settings.json. IntegrateCommand must catch it and turn it into a
        // friendly exit-1 error instead of letting it escape to Spectre's default handler (exit 255).
        var dir = Path.Combine(Path.GetTempPath(), $"dtk-malformed-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(dir, ".claude", "settings.json"), "not json at all {{{");

        try
        {
            var userClaudeDir = Path.Combine(dir, "isolated-home", ".claude");
            var rtkConfigPath = Path.Combine(dir, "isolated-config", "rtk", "config.toml");
            var console = new TestConsole();
            var command = new IntegrateCommand(
                new IntegrateUseCase([new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath))]),
                console);

            var exitCode = await command.RunAsync(new IntegrateCommandSettings
            {
                Provider = "claude",
                Directory = dir
            }, CancellationToken.None);

            exitCode.Should().Be(1);
            console.Output.Should().Contain("Error:");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProviderCasingDiffersFromCanonical_MessagesUseCanonicalCasing()
    {
        // Validation is OrdinalIgnoreCase, so "CLAUDE" resolves to the "claude" integrator — but
        // messages must echo the canonical registered name, not the user's raw casing.
        const string dir = "/project";
        var result = new IntegrationResult(
            [$"{dir}/.claude/settings.json"],
            [],
            []);
        var console = new TestConsole();
        var stub = new StubIntegrator("claude")
        {
            Result = result
        };
        var command = new IntegrateCommand(new IntegrateUseCase([stub]), console);

        var exitCode = await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "CLAUDE",
            Directory = dir
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("integrated with claude");
        console.Output.Should().NotContain("CLAUDE");
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
    public async Task ExecuteAsync_FileOutsideProjectDirectory_ShowsAbsolutePathInsteadOfRelativeWalk()
    {
        // When a rendered file lives outside the project directory (e.g. a global rtk config under
        // a different root), Path.GetRelativePath yields a "../"-prefixed walk. RelativePath renders
        // the absolute, forward-slashed path instead, since that reads better than a deep relative
        // walk to an unrelated root.
        const string dir = "/project";
        const string outsidePath = "/other-root/config/global.toml";
        var result = new IntegrationResult([outsidePath], [], []);

        var (command, console) = Create("claude", result);

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir
        }, CancellationToken.None);

        console.Output.Should().Contain(outsidePath);
        console.Output.Should().NotContain("../");
    }

    [Fact]
    public async Task ExecuteAsync_InProjectFileStartingWithDotDot_RendersRelativeNotAbsolute()
    {
        // A filename that merely begins with ".." (a real file directly under the project root) is
        // NOT outside the project — it must render as the relative "..notes.txt", not an absolute path.
        const string dir = "/project";
        const string inProjectPath = "/project/..notes.txt";
        var result = new IntegrationResult([inProjectPath], [], []);

        var (command, console) = Create("claude", result);

        await command.RunAsync(new IntegrateCommandSettings
        {
            Provider = "claude",
            Directory = dir
        }, CancellationToken.None);

        console.Output.Should().Contain("..notes.txt");
        console.Output.Should().NotContain(inProjectPath); // not rendered as the absolute "/project/..notes.txt"
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

    [Fact]
    public async Task RunAsync_ResultWithNotes_RendersNoteLine()
    {
        var result = new IntegrationResult([], [], [], ["excluded dotnet in rtk config"]);

        var (command, console) = Create("claude", result);

        await command.RunAsync(new IntegrateCommandSettings { Provider = "claude" }, CancellationToken.None);

        console.Output.Should().Contain("excluded dotnet in rtk config");
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

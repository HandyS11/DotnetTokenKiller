using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public sealed class ConfigCommandTests
{
    // ── ConfigShowCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigShow_DisplaysAllKeys()
    {
        var console = new TestConsole();
        var command = new ConfigShowCommand(new StubConfigProvider(), console);

        await command.ExecuteAsync(null!, CancellationToken.None);

        console.Output.Should().Contain("tracking.enabled");
        console.Output.Should().Contain("tracking.retentionDays");
        console.Output.Should().Contain("display.colors");
        console.Output.Should().Contain("tee.mode");
    }

    [Fact]
    public async Task ConfigShow_ReturnsExitCode0()
    {
        var command = new ConfigShowCommand(new StubConfigProvider(), new TestConsole());

        var exitCode = await command.ExecuteAsync(null!, CancellationToken.None);

        exitCode.Should().Be(0);
    }

    // ── ConfigSetCommand ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigSet_ValidKey_PrintsConfirmation_ReturnsZero()
    {
        var console = new TestConsole();
        var command = new ConfigSetCommand(new ConfigSetUseCase(new StubConfigProvider()), console);

        var exitCode = await command.ExecuteAsync(null!, new ConfigSetCommandSettings
        {
            Key = "display.width",
            Value = "100"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("display.width");
        console.Output.Should().Contain("100");
    }

    [Fact]
    public async Task ConfigSet_UnknownKey_PrintsError_ReturnsOne()
    {
        var console = new TestConsole();
        var command = new ConfigSetCommand(new ConfigSetUseCase(new StubConfigProvider()), console);

        var exitCode = await command.ExecuteAsync(null!, new ConfigSetCommandSettings
        {
            Key = "unknown.key",
            Value = "value"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error");
    }

    [Fact]
    public async Task ConfigSet_InvalidValue_PrintsError_ReturnsOne()
    {
        var console = new TestConsole();
        var command = new ConfigSetCommand(new ConfigSetUseCase(new StubConfigProvider()), console);

        var exitCode = await command.ExecuteAsync(null!, new ConfigSetCommandSettings
        {
            Key = "tracking.enabled",
            Value = "notabool"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error");
    }

    [Fact]
    public async Task ConfigShow_AllRowKeysPresentInOutput()
    {
        // Kills statement mutations (removing AddRow calls) and string mutations on row key literals
        var console = new TestConsole();
        var command = new ConfigShowCommand(new StubConfigProvider(), console);

        await command.ExecuteAsync(null!, CancellationToken.None);

        console.Output.Should().Contain("tracking.dbPath");
        console.Output.Should().Contain("tracking.tokenizer");
        console.Output.Should().Contain("display.emoji");
        console.Output.Should().Contain("display.width");
        console.Output.Should().Contain("tee.directory");
        console.Output.Should().Contain("tee.maxFiles");
        console.Output.Should().Contain("tee.maxFileSizeBytes");
        // Column headers
        console.Output.Should().Contain("Key");
        console.Output.Should().Contain("Value");
        // Default config has null DbPath and null Tee.Directory → rendered as "(default)"
        console.Output.Should().Contain("(default)");
    }

    [Fact]
    public async Task ConfigShow_NonNullDbPath_DisplaysActualPathNotDefault()
    {
        // Kills null-coalescing remove-left mutation: config.Tracking.DbPath ?? "(default)" → always "(default)"
        var console = new TestConsole();
        var config = DtkConfig.Default with
        {
            Tracking = DtkConfig.Default.Tracking with
            {
                DbPath = "/custom/path.db"
            }
        };
        var command = new ConfigShowCommand(new StubConfigProvider(config), console);

        await command.ExecuteAsync(null!, CancellationToken.None);

        console.Output.Should().Contain("/custom/path.db");
    }

    private sealed class StubConfigProvider(DtkConfig? config = null) : IConfigProvider
    {
        private DtkConfig Current
        {
            get;
            set;
        } = config ?? DtkConfig.Default;

        public DtkConfig Load()
        {
            return Current;
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Current);
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            Current = config;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

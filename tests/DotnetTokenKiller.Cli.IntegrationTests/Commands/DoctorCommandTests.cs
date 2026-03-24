using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public sealed class DoctorCommandTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"dtk-doctor-cmd-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPassed_ReturnsZeroAndShowsAllPassed()
    {
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(0);

        var exitCode = await command.ExecuteAsync(null!, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("All checks passed");
    }

    [Fact]
    public async Task ExecuteAsync_DotnetCheckFails_ReturnsOneAndShowsSomeFailed()
    {
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(1);

        var exitCode = await command.ExecuteAsync(null!, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Some checks failed");
    }

    [Fact]
    public async Task ExecuteAsync_OutputContainsFourChecks()
    {
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(0);

        await command.ExecuteAsync(null!, CancellationToken.None);

        // Each check has a named label
        console.Output.Should().Contain("dotnet SDK");
        console.Output.Should().Contain("config file");
        console.Output.Should().Contain("tracking database");
        console.Output.Should().Contain("tee directory");
    }

    [Fact]
    public async Task ExecuteAsync_NullDbPathAndTeeDirectory_CallsResolveDefaultMethods()
    {
        // Covers ResolveDefaultDbPath() and ResolveDefaultTeeDir() private methods (lines 55-65)
        // when config returns null for both fields and DTK_DB_PATH is not set
        var savedEnv = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", null);
        try
        {
            var console = new TestConsole();
            var configProvider = new NullPathsConfigProvider();
            var runner = new StubCommandRunner(0);
            var useCase = new DoctorUseCase(runner, configProvider);
            var command = new DoctorCommand(useCase, configProvider, console);

            await command.ExecuteAsync(null!, CancellationToken.None);

            // ResolveDefaultDbPath returns LocalApplicationData/dtk/tracking.db
            // ResolveDefaultTeeDir returns LocalApplicationData/dtk/tee
            // The db directory likely won't exist → check fails, exitCode = 1
            console.Output.Should().Contain("tracking database");
            console.Output.Should().Contain("tee directory");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_DB_PATH", savedEnv);
        }
    }

    private (DoctorCommand command, TestConsole console) Create(int dotnetExitCode)
    {
        var console = new TestConsole();
        var configProvider = new StubConfigProvider(_tempDir);
        var runner = new StubCommandRunner(dotnetExitCode);
        var useCase = new DoctorUseCase(runner, configProvider);
        var command = new DoctorCommand(useCase, configProvider, console);
        return (command, console);
    }

    private sealed class StubConfigProvider(string teeDir) : IConfigProvider
    {
        public DtkConfig Load()
        {
            return DtkConfig.Default with
            {
                Tracking = DtkConfig.Default.Tracking with
                {
                    DbPath = Path.Combine(teeDir, "tracking.db")
                },
                Tee = DtkConfig.Default.Tee with
                {
                    Directory = teeDir
                }
            };
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Load());
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubCommandRunner(int exitCode) : ICommandRunner
    {
        public Task<CommandResult> RunCapturedAsync(
            string command,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResult("10.0.0", string.Empty, exitCode));
        }

        public Task<int> RunPassthroughAsync(
            string command,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(exitCode);
        }
    }

    /// <summary>Config provider returning null for DbPath and Tee.Directory to trigger default path resolution.</summary>
    private sealed class NullPathsConfigProvider : IConfigProvider
    {
        public DtkConfig Load()
        {
            return DtkConfig.Default;
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DtkConfig.Default);
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

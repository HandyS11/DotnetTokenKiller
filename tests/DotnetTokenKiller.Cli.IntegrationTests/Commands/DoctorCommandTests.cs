using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;
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

        var exitCode = await command.RunAsync(CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("All checks passed");
    }

    [Fact]
    public async Task ExecuteAsync_DotnetCheckFails_ReturnsOneAndShowsSomeFailed()
    {
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(1);

        var exitCode = await command.RunAsync(CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Some checks failed");
    }

    [Fact]
    public async Task ExecuteAsync_OutputContainsFourChecks()
    {
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(0);

        await command.RunAsync(CancellationToken.None);

        // Each check has a named label
        console.Output.Should().Contain("dotnet SDK");
        console.Output.Should().Contain("config file");
        console.Output.Should().Contain("tracking database");
        console.Output.Should().Contain("tee directory");
    }

    [Fact]
    public async Task ExecuteAsync_NullConfigPaths_ReportsSharedTrackerAndTeeDefaultPaths()
    {
        // With null config paths and no env override, doctor must report the EXACT default paths
        // the tracker and tee service actually use — a single source of truth, no drift.
        var savedEnv = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", null);
        try
        {
            var console = new TestConsole();
            console.Profile.Width = 400; // avoid wrapping the long absolute paths in the output
            var configProvider = new NullPathsConfigProvider();
            var runner = new StubCommandRunner(0);
            var useCase = new DoctorUseCase(runner, configProvider);
            var command = new DoctorCommand(useCase, configProvider, console);

            await command.RunAsync(CancellationToken.None);

            console.Output.Should().Contain(SqliteTracker.GetDefaultDbPath());
            console.Output.Should().Contain(TeeDirectoryResolver.GetDefault());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_DB_PATH", savedEnv);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DtkTeeDirEnvVar_ReportsTheOverrideTheTeeServiceUses()
    {
        // The tee service honors DTK_TEE_DIR over config/default, so doctor must report that same
        // path — otherwise it would show a tee directory dtk does not actually use at runtime.
        Directory.CreateDirectory(_tempDir);
        var envTeeDir = Path.Combine(_tempDir, "env-tee");
        Directory.CreateDirectory(envTeeDir);

        var savedEnv = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", envTeeDir);
        try
        {
            var console = new TestConsole();
            console.Profile.Width = 400;
            var configProvider = new StubConfigProvider(_tempDir); // config points tee elsewhere
            var runner = new StubCommandRunner(0);
            var useCase = new DoctorUseCase(runner, configProvider);
            var command = new DoctorCommand(useCase, configProvider, console);

            await command.RunAsync(CancellationToken.None);

            // envTeeDir only appears in the output if DTK_TEE_DIR was honored (config points
            // the tee dir at _tempDir instead), so this alone discriminates the fix.
            console.Output.Should().Contain(envTeeDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", savedEnv);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FreshInstall_MissingDbDirectory_PassesAsWillBeCreated()
    {
        // Fresh install: _tempDir is intentionally NOT created, so neither the database directory
        // nor the tee directory exists. Both checks must pass ("will be created") instead of the
        // tracking-database check false-alarming on a brand-new machine.
        var console = new TestConsole();
        console.Profile.Width = 400;
        var configProvider = new StubConfigProvider(_tempDir); // db = _tempDir/tracking.db, tee = _tempDir
        var runner = new StubCommandRunner(0);
        var useCase = new DoctorUseCase(runner, configProvider);
        var command = new DoctorCommand(useCase, configProvider, console);

        var exitCode = await command.RunAsync(CancellationToken.None);

        exitCode.Should().Be(0); // old: missing db directory → check failed → exit 1
        console.Output.Should().Contain("All checks passed");
        console.Output.Should().Contain("will be created");
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPassed_OutputContainsCheckmark()
    {
        // Kills conditional mutations: always ✔ or always ✘ instead of using check.Passed
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(0);

        await command.RunAsync(CancellationToken.None);

        console.Output.Should().Contain("✔");
        console.Output.Should().NotContain("✘");
    }

    [Fact]
    public async Task ExecuteAsync_DotnetCheckFails_OutputContainsCross()
    {
        // Kills conditional mutations: always ✔ or always ✘ instead of using check.Passed
        Directory.CreateDirectory(_tempDir);
        var (command, console) = Create(1);

        await command.RunAsync(CancellationToken.None);

        console.Output.Should().Contain("✘");
    }

    [Fact]
    public async Task ExecuteAsync_DtkDbPathEnvVar_TakesPrecedenceOverConfigDbPath()
    {
        // Kills null-coalescing mutations on the DbPath resolution chain (lines 22-24):
        //   remove-left: DTK_DB_PATH is ignored, config.DbPath is used instead
        //   string mutation: env var name becomes "" so GetEnvironmentVariable always returns null
        // Strategy: env var points to an existing db file (pass), config.DbPath is null (would use default, fail).
        // With original code: env var wins → db exists → tracking database check passes.
        // With remove-left mutation: config.DbPath (null) → default path (non-existent) → check fails.
        Directory.CreateDirectory(_tempDir);
        var envDbPath = Path.Combine(_tempDir, "env-tracking.db");
        await File.WriteAllTextAsync(envDbPath, string.Empty); // create file so check passes

        var savedEnv = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        Environment.SetEnvironmentVariable("DTK_DB_PATH", envDbPath);
        try
        {
            var console = new TestConsole();
            // Config has null DbPath — so env var is the only way to resolve a valid path
            var configProvider = new NullPathsConfigProvider();
            var runner = new StubCommandRunner(0);
            var useCase = new DoctorUseCase(runner, configProvider);
            var command = new DoctorCommand(useCase, configProvider, console);

            await command.RunAsync(CancellationToken.None);

            // DTK_DB_PATH was used (file exists) → tracking database check passes
            console.Output.Should().Contain("✔");
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

        public Task<CommandResult> RunStreamedAsync(string command, IReadOnlyList<string> args,
            TextWriter stdOutSink, TextWriter stdErrSink, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResult(string.Empty, string.Empty, exitCode));
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

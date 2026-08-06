using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class DoctorUseCaseTests : IDisposable
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly DoctorUseCase _sut;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-doctor-test-{Guid.NewGuid()}");

    public DoctorUseCaseTests()
    {
        _sut = new DoctorUseCase(_runner, _configProvider);
        _configProvider.LoadAsync().ReturnsForAnyArgs(DtkConfig.Default);
        _runner.RunCapturedAsync(null!, null!)
            .ReturnsForAnyArgs(new CommandResult("10.0.0", string.Empty, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task RunAsync_DotnetAvailable_DotnetCheckPasses()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(new CommandResult("10.0.100", string.Empty, 0));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Passed.Should().BeTrue();
        dotnetCheck.Message.Should().Contain("10.0.100");
    }

    [Fact]
    public async Task RunAsync_DotnetNotFound_DotnetCheckFails()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(
                new InvalidOperationException("dotnet not found")));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Passed.Should().BeFalse();
        dotnetCheck.Message.Should().Contain("dotnet not found");
    }

    [Fact]
    public async Task RunAsync_DotnetExitsNonZero_DotnetCheckFails()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(new CommandResult(string.Empty, string.Empty, 1));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ConfigLoadsSuccessfully_ConfigCheckPasses()
    {
        _configProvider.LoadAsync().ReturnsForAnyArgs(DtkConfig.Default);

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var configCheck = checks.First(c => c.Name == "config file");
        configCheck.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_DbFileExists_DbCheckPasses()
    {
        var dbPath = Path.Combine(_tempDir, "tracking.db");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(dbPath, string.Empty);

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeTrue();
        dbCheck.Message.Should().Contain(dbPath);
    }

    [Fact]
    public async Task RunAsync_DbFileAbsent_DbCheckPassesWithNotYetCreatedMessage()
    {
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "nonexistent.db");

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeTrue();
        dbCheck.Message.Should().Contain("will be created");
    }

    [Fact]
    public async Task RunAsync_DbDirectoryMissing_DbCheckPassesAsWillBeCreated()
    {
        // A missing database directory is normal on a fresh install — the tracker creates it on
        // first write — so this must pass ("will be created"), not false-alarm.
        var dbPath = Path.Combine(_tempDir, "missing-dir", "tracking.db");

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeTrue();
        dbCheck.Message.Should().Contain("will be created");
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryExists_TeeCheckPasses()
    {
        Directory.CreateDirectory(_tempDir);

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var teeCheck = checks.First(c => c.Name == "tee directory");
        teeCheck.Passed.Should().BeTrue();
        teeCheck.Message.Should().Contain(_tempDir);
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryAbsent_TeeCheckPassesWithWillBeCreatedMessage()
    {
        var nonExistent = Path.Combine(_tempDir, "tee");

        var checks = await _sut.RunAsync("/tmp/test.db", nonExistent);

        var teeCheck = checks.First(c => c.Name == "tee directory");
        teeCheck.Passed.Should().BeTrue();
        teeCheck.Message.Should().Contain("will be created");
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryNotWritable_TeeCheckFails()
    {
        // The check exists to catch exactly this: a tee directory that is present but cannot be
        // written to, which would otherwise show up later as silently missing logs.
        // POSIX permission bits are a no-op on Windows, so this is guarded there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_tempDir);
        var originalMode = File.GetUnixFileMode(_tempDir);
        try
        {
            File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            // Running as root (common in CI containers) ignores the mode entirely, which would make
            // the assertion below meaningless rather than merely skipped.
            var probe = Path.Combine(_tempDir, ".dtk-writability-probe");
            try
            {
                await File.WriteAllTextAsync(probe, string.Empty);
                File.Delete(probe);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // Good: the mode really does block writes for this user.
            }

            var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

            var teeCheck = checks.First(c => c.Name == "tee directory");
            teeCheck.Passed.Should().BeFalse();
            teeCheck.Message.Should().StartWith("Not writable:");
        }
        finally
        {
            File.SetUnixFileMode(_tempDir, originalMode);
        }
    }

    [Fact]
    public async Task RunAsync_ReturnsAllFourChecks()
    {
        Directory.CreateDirectory(_tempDir);

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        checks.Should().HaveCount(4);
    }

    [Fact]
    public async Task RunAsync_ConfigLoadThrows_ConfigCheckFails()
    {
        // Covers CheckConfigAsync catch block (lines 68-70)
        _configProvider.LoadAsync()
            .ReturnsForAnyArgs(Task.FromException<DtkConfig>(new IOException("Config file corrupted")));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var configCheck = checks.First(c => c.Name == "config file");
        configCheck.Passed.Should().BeFalse();
        configCheck.Message.Should().Contain("Config file corrupted");
    }

    [Fact]
    public async Task RunAsync_DbPathWithNoDirectory_PassesAsWillBeCreated()
    {
        // A bare filename with no directory component still resolves to a pending database.
        var checks = await _sut.RunAsync("tracking.db", _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeTrue();
        dbCheck.Message.Should().Contain("will be created");
    }

    [Fact]
    public async Task RunAsync_DotnetExitsNonZero_MessageContainsExitCode()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(new CommandResult(string.Empty, string.Empty, 42));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Message.Should().Contain("exited with code").And.Contain("42");
    }

    [Fact]
    public async Task RunAsync_ConfigLoadsSuccessfully_MessageContainsLoadedSuccessfully()
    {
        _configProvider.LoadAsync().ReturnsForAnyArgs(DtkConfig.Default);

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var configCheck = checks.First(c => c.Name == "config file");
        configCheck.Message.Should().Contain("Loaded successfully");
    }

    [Fact]
    public async Task RunAsync_ConfigLoadThrows_MessageContainsFailedToLoad()
    {
        _configProvider.LoadAsync()
            .ReturnsForAnyArgs(Task.FromException<DtkConfig>(new IOException("boom")));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var configCheck = checks.First(c => c.Name == "config file");
        configCheck.Message.Should().StartWith("Failed to load config:");
    }

    [Fact]
    public async Task RunAsync_DotnetSdkCheckMessage_StartsWithFoundDotnet()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(new CommandResult("9.0.200", string.Empty, 0));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Message.Should().StartWith("Found dotnet ");
    }

    [Fact]
    public async Task RunAsync_DotnetNotFound_MessageStartsWithCouldNotRun()
    {
        _runner.RunCapturedAsync("dotnet", Arg.Any<IReadOnlyList<string>>())
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(
                new InvalidOperationException("not installed")));

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var dotnetCheck = checks.First(c => c.Name == "dotnet SDK");
        dotnetCheck.Message.Should().StartWith("Could not run dotnet:");
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryWritable_ProbeFileIsCleanedUp()
    {
        Directory.CreateDirectory(_tempDir);

        await _sut.RunAsync("/tmp/test.db", _tempDir);

        // After the check, no .dtk-probe-* files should remain
        var probeFiles = Directory.GetFiles(_tempDir, ".dtk-probe-*");
        probeFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryWritable_MessageContainsWritableAt()
    {
        Directory.CreateDirectory(_tempDir);

        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir);

        var teeCheck = checks.First(c => c.Name == "tee directory");
        teeCheck.Message.Should().StartWith("Writable at ");
    }

    [Fact]
    public async Task RunAsync_TeeDirectoryAbsent_MessageContainsDoesNotExistYet()
    {
        var nonExistent = Path.Combine(_tempDir, "tee");

        var checks = await _sut.RunAsync("/tmp/test.db", nonExistent);

        var teeCheck = checks.First(c => c.Name == "tee directory");
        teeCheck.Message.Should().Contain("Does not exist yet");
    }

    [Fact]
    public async Task RunAsync_DbFileExists_MessageStartsWithFoundAt()
    {
        var dbPath = Path.Combine(_tempDir, "tracking.db");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(dbPath, string.Empty);

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Message.Should().StartWith("Found at ");
    }

    [Fact]
    public async Task RunAsync_DbFileAbsent_MessageContainsNoDataYet()
    {
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "nonexistent.db");

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Message.Should().Contain("No data yet");
    }
}

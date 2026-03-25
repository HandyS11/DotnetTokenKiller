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
    public async Task RunAsync_DbDirectoryMissing_DbCheckFails()
    {
        var dbPath = Path.Combine(_tempDir, "missing-dir", "tracking.db");

        var checks = await _sut.RunAsync(dbPath, _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeFalse();
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
    public async Task RunAsync_DbPathWithNoDirectory_SkipsDirectoryExistenceCheck()
    {
        // Path.GetDirectoryName("tracking.db") returns "" → IsNullOrEmpty is true → skip dir check
        // Covers the uncovered branch of the !string.IsNullOrEmpty(dir) condition (line 80)
        var checks = await _sut.RunAsync("tracking.db", _tempDir);

        var dbCheck = checks.First(c => c.Name == "tracking database");
        dbCheck.Passed.Should().BeTrue();
        dbCheck.Message.Should().Contain("will be created");
    }
}

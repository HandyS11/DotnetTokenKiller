using System.Reflection;
using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

/// <summary>
/// Shares the "TeeDirectoryResolver" collection with <see cref="TeeDirectoryResolverTests"/>: both
/// mutate the process-wide DTK_TEE_DIR environment variable, and xUnit only serialises test classes
/// against each other when they are in the same collection — distinct collections still run in
/// parallel by default, so this is what actually prevents the two classes racing on that variable.
/// </summary>
[Collection("TeeDirectoryResolver")]
public sealed class FileTeeServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private FileTeeService CreateSut(TeeConfig? teeConfig = null)
    {
        var config = DtkConfig.Default with
        {
            Tee = teeConfig ?? new TeeConfig()
        };
        return new FileTeeService(new FakeConfigProvider(config), _tempDir);
    }

    private static string LargeOutput(int length = 600)
    {
        return new string('x', length);
    }

    [Fact]
    public void RotateFiles_NonExistentDirectory_ReturnsEarlyWithoutThrowing()
    {
        // Covers RotateFiles early return when dir does not exist (lines 85-86) via reflection
        var method = typeof(FileTeeService)
            .GetMethod("RotateFiles", BindingFlags.NonPublic | BindingFlags.Static)!;

        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-nonexistent-{Guid.NewGuid()}");
        Directory.Exists(nonExistentDir).Should().BeFalse();

        var act = () => method.Invoke(null, [nonExistentDir, 5]);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task DeleteLogsAsync_DeletesLogFiles_LeavesNonLogFiles()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.log"), "log1");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.log"), "log2");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "keep");
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await sut.DeleteLogsAsync();

        Directory.GetFiles(_tempDir, "*.log").Should().BeEmpty();
        File.Exists(Path.Combine(_tempDir, "notes.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteLogsAsync_NonExistentDirectory_DoesNotThrow()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        Directory.Exists(_tempDir).Should().BeFalse();

        var act = () => sut.DeleteLogsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteLogsAsync_ConfigProviderThrows_DoesNotThrow()
    {
        // Covers catch block in DeleteLogsAsync (lines 94-97): exceptions must never surface
        var sut = new FileTeeService(new ThrowingConfigProvider(), _tempDir);

        var act = () => sut.DeleteLogsAsync();

        await act.Should().NotThrowAsync();
    }

    private static TeeLogHeader RunningHeader() => new(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        null,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    [Fact]
    public async Task BeginAsync_WritesTheHeaderBeforeAnyOutputArrives()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await using var session = await sut.BeginAsync("build", RunningHeader());

        var text = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
    }

    [Fact]
    public async Task BeginAsync_OpensASession_EvenInFailuresMode()
    {
        // The exit code is unknown at this point, so the mode cannot be applied yet. Deciding
        // early would mean never writing a log for a run that turns out to fail.
        var sut = CreateSut(new TeeConfig(TeeMode.Failures));

        await using var session = await sut.BeginAsync("build", RunningHeader());

        Directory.GetFiles(_tempDir).Should().ContainSingle();
    }

    [Fact]
    public async Task BeginAsync_ReturnsANullSession_WhenTeeIsOff()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Never));

        await using var session = await sut.BeginAsync("build", RunningHeader());

        session.Should().BeOfType<NullTeeSession>();
        (await session.FinalizeAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task BeginAsync_RotatesOldLogs()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 2));
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "1000_a_build.log"), "old");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "2000_b_build.log"), "old");

        await using var session = await sut.BeginAsync("build", RunningHeader());

        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(2);
    }

    [Fact]
    public async Task BeginAsync_RoundTripsTheStatusRegion_WhenTheHeaderContainsNonAsciiText()
    {
        // The command line and cwd carry multi-byte UTF-8 text, so the char index into the
        // rendered header and its UTF-8 byte offset diverge. BeginAsync must measure the status
        // region's offset in UTF-8 bytes, not UTF-16 chars, or FinalizeAsync's read-back guard
        // rejects the overwrite and the log is stuck reporting "running" forever.
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        var header = new TeeLogHeader(
            "dotnet build café.slnx", "/home/user/projets/café", null, RunSource.Run,
            new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

        var session = await sut.BeginAsync("build", header);
        await session.Writer.WriteLineAsync(LargeOutput().AsMemory(), CancellationToken.None);
        var hint = await session.FinalizeAsync(2);

        hint.Should().NotBeNull();
        var text = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        TeeLogHeader.TryParse(text, out var parsed).Should().BeTrue();
        parsed.Status.Should().Be(TeeLogStatus.Complete);
        parsed.ExitCode.Should().Be(2);
        parsed.CommandLine.Should().Be("dotnet build café.slnx");
        parsed.ProjectPath.Should().Be("/home/user/projets/café");
    }

    private sealed class ThrowingConfigProvider : IConfigProvider
    {
        public DtkConfig Load()
        {
            throw new InvalidOperationException("Simulated config failure");
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated config failure");
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

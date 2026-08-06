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

        var text = await TeeLogFileReader.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
    }

    [Fact]
    public async Task BeginAsync_NormalizesANonNullProvisionalExitCode_ToRunning()
    {
        // A caller that (incorrectly) passes a header with a non-null ExitCode must not get a log
        // stuck reporting Complete before the run has even started: FinalizeAsync's read-back guard
        // expects to find the Running region at the offset it was given, and a header rendered as
        // already Complete means that region is not there, silently disabling tee for the run. This
        // pins BeginAsync's normalization as the fix, rather than trusting every caller to pass null.
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        var header = RunningHeader() with { ExitCode = 7 };

        var session = await sut.BeginAsync("build", header);
        var text = await TeeLogFileReader.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        TeeLogHeader.TryParse(text, out var parsed).Should().BeTrue();
        parsed.Status.Should().Be(TeeLogStatus.Running);
        parsed.ExitCode.Should().BeNull();

        // Kept above the 500-byte minBodyBytes guard so that guard, not this test, decides whether
        // the log survives -- the point under test is the header normalization, not retention.
        await session.Writer.WriteLineAsync(LargeOutput().AsMemory(), CancellationToken.None);
        var hint = await session.FinalizeAsync(3);

        hint.Should().NotBeNull();
        var finalText = await TeeLogFileReader.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        TeeLogHeader.TryParse(finalText, out var finalHeader).Should().BeTrue();
        finalHeader.Status.Should().Be(TeeLogStatus.Complete);
        finalHeader.ExitCode.Should().Be(3);
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

    /// <summary>Builds an old log's filename with a timestamp that reliably sorts before <see cref="RunningHeader"/>.</summary>
    /// <param name="year">The old log's year, distinguishing multiple fixture files from each other.</param>
    /// <param name="suffix">A unique suffix so same-second fixtures do not collide.</param>
    private static string OldLogFileName(int year, string suffix) =>
        TeeLogFileName.Build(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero), suffix, "build");

    [Fact]
    public async Task BeginAsync_DoesNotRotate_BeforeTheRetentionDecisionIsKnown()
    {
        // CRITICAL regression guard: rotating at open (before it is known whether the run's own
        // log will be kept) let a successful run in the default Failures mode evict stored failure
        // logs it was never going to replace. Opening a session must never touch existing files —
        // note BeginAsync still creates its own provisional log (the decision to keep or discard
        // happens later, at FinalizeAsync), so three files are expected here, not two.
        var sut = CreateSut(new TeeConfig(TeeMode.Failures, MaxFiles: 2));
        Directory.CreateDirectory(_tempDir);
        var older = OldLogFileName(2020, "a");
        var newer = OldLogFileName(2021, "b");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, older), "old");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, newer), "old");

        await using var session = await sut.BeginAsync("build", RunningHeader());

        var files = Directory.GetFiles(_tempDir, "*.log").Select(Path.GetFileName).ToList();
        files.Should().HaveCount(3);
        files.Should().Contain([older, newer]);
    }

    [Fact]
    public async Task FinalizeAsync_LeavesPreExistingLogsIntact_WhenARunSucceedsInFailuresMode()
    {
        // Reproduces the CRITICAL scenario exactly: MaxFiles 2, two pre-existing logs, a successful
        // run under the default TeeMode.Failures. The run's own log is discarded by retention, so
        // rotation (now deferred to the keep path) must not run at all — both pre-existing logs
        // must survive.
        var sut = CreateSut(new TeeConfig(TeeMode.Failures, MaxFiles: 2));
        Directory.CreateDirectory(_tempDir);
        var older = OldLogFileName(2020, "a");
        var newer = OldLogFileName(2021, "b");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, older), "old");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, newer), "old");

        var session = await sut.BeginAsync("build", RunningHeader());
        await session.Writer.WriteLineAsync(LargeOutput().AsMemory(), CancellationToken.None);
        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        Directory.GetFiles(_tempDir, "*.log").Select(Path.GetFileName)
            .Should().BeEquivalentTo(older, newer);
    }

    [Fact]
    public async Task FinalizeAsync_RotatesToExactlyMaxFiles_AndKeepsTheNewestLog_WhenTheLogIsKept()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 2));
        Directory.CreateDirectory(_tempDir);
        var oldest = OldLogFileName(2020, "a");
        var older = OldLogFileName(2021, "b");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, oldest), "old");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, older), "old");

        var session = await sut.BeginAsync("build", RunningHeader());
        await session.Writer.WriteLineAsync(LargeOutput().AsMemory(), CancellationToken.None);
        var hint = await session.FinalizeAsync(0);

        hint.Should().NotBeNull();
        const string prefix = "[full output: ";
        var newFileName = Path.GetFileName(hint[prefix.Length..^1]);
        var remaining = Directory.GetFiles(_tempDir, "*.log").Select(Path.GetFileName).ToList();
        remaining.Should().HaveCount(2);
        remaining.Should().Contain(newFileName);
        remaining.Should().Contain(older);
        remaining.Should().NotContain(oldest);
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotRotate_WhenMaxFilesIsZero()
    {
        // Covers RotateFiles' maxFiles <= 0 guard on the keep path: without it, rotation would
        // delete every file, including the one that was just kept.
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFiles: 0));
        Directory.CreateDirectory(_tempDir);
        var older = OldLogFileName(2020, "a");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, older), "old");

        var session = await sut.BeginAsync("build", RunningHeader());
        await session.Writer.WriteLineAsync(LargeOutput().AsMemory(), CancellationToken.None);
        var hint = await session.FinalizeAsync(0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(2);
    }

    [Fact]
    public async Task BeginAsync_WritesLogFileOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await using var session = await sut.BeginAsync("build", RunningHeader());

        var file = Directory.GetFiles(_tempDir).Single();
        File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task BeginAsync_CreatesTeeDirectoryOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var sut = CreateSut(new TeeConfig(TeeMode.Always));

        await using var session = await sut.BeginAsync("build", RunningHeader());

        File.GetUnixFileMode(_tempDir)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task BeginAsync_ClosesTheLogFile_WhenTheHeaderWriteIsCancelled()
    {
        // Cancellation is the one failure that must not degrade to a NullTeeSession — it means the
        // caller is stopping, not that tee broke. The stream is opened before the header is written,
        // and ownership only transfers to the session on success, so this path has to close the file
        // itself; a leaked handle would keep the empty log locked for the rest of the process.
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.BeginAsync("build", RunningHeader(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var log = Directory.GetFiles(_tempDir, "*.log").Should().ContainSingle().Subject;
        new FileInfo(log).Length.Should().Be(0); // the header never made it out
        // Opening with FileShare.None fails if the service leaked its handle (observable on Windows).
        var reopen = () => new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None).Dispose();
        reopen.Should().NotThrow();
    }

    [Fact]
    public async Task BeginAsync_ConfigProviderThrows_ReturnsNullSession()
    {
        // Covers the catch-all block: exceptions raised while opening the log must never surface.
        var sut = new FileTeeService(new ThrowingConfigProvider(), _tempDir);

        var session = await sut.BeginAsync("build", RunningHeader());

        session.Should().BeSameAs(NullTeeSession.Instance);
    }

    [Fact]
    public async Task FinalizeAsync_DiscardsTheLog_WhenTheBodyIsJustUnderTheConfiguredMinBodyBytesGuard()
    {
        // Pins the 500-byte minBodyBytes constant BeginAsync passes to the session: a caller that
        // replaced it with a different hardcoded value would still pass this test's sibling below
        // but fail here, or vice versa.
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        var session = await sut.BeginAsync("build", RunningHeader());
        // WriteLineAsync appends a forced "\n", so 498 chars + 1 byte = 499 bytes: one under 500.
        await session.Writer.WriteLineAsync(new string('x', 498).AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        Directory.GetFiles(_tempDir, "*.log").Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeAsync_KeepsTheLog_WhenTheBodyMeetsTheConfiguredMinBodyBytesGuard()
    {
        var sut = CreateSut(new TeeConfig(TeeMode.Always));
        var session = await sut.BeginAsync("build", RunningHeader());
        // WriteLineAsync appends a forced "\n", so 499 chars + 1 byte = 500 bytes: exactly the guard.
        await session.Writer.WriteLineAsync(new string('x', 499).AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(1);
    }

    [Fact]
    public async Task FinalizeAsync_KeepsALog_WhenMaxFileSizeBytesIsBelowTheDefaultMinBodyBytesGuard()
    {
        // Regression guard: minBodyBytes used to be hardcoded to 500 regardless of a smaller
        // MaxFileSizeBytes. BodyBytesWritten can never exceed MaxFileSizeBytes, so a cap below 500
        // meant the guard could never be met and every log was silently discarded at finalize.
        const long maxBytes = 400L;
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFileSizeBytes: maxBytes));
        var session = await sut.BeginAsync("build", RunningHeader());
        await session.Writer.WriteLineAsync(LargeOutput(5000).AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(1);
    }

    [Fact]
    public async Task BeginAsync_PassesTheConfiguredMaxFileSizeBytesToTheSession()
    {
        // Pins that teeConfig.MaxFileSizeBytes (not a hardcoded constant) reaches the session: a
        // caller that dropped the argument would keep the default 1 MiB budget and never truncate
        // at this tiny configured size. Kept above the 500-byte minBodyBytes guard (also under
        // test elsewhere) so truncation, not that guard, is what this test isolates.
        const long maxBytes = 600L;
        var sut = CreateSut(new TeeConfig(TeeMode.Always, MaxFileSizeBytes: maxBytes));
        var session = await sut.BeginAsync("build", RunningHeader());
        await session.Writer.WriteLineAsync(LargeOutput(5000).AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        var written = await TeeLogFileReader.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
        var body = TeeLogHeader.StripHeader(written);
        ((long)Encoding.UTF8.GetByteCount(body)).Should().BeLessThanOrEqualTo(maxBytes);
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
        var text = await TeeLogFileReader.ReadAllTextAsync(Directory.GetFiles(_tempDir).Single());
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

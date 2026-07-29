using System.Text;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class FileTeeSessionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-session-test-{Guid.NewGuid()}");

    public FileTeeSessionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static TeeLogHeader RunningHeader() => new(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        null,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

    /// <summary>Opens a session over a real file, mirroring what FileTeeService.BeginAsync does.</summary>
    /// <param name="maxBodyBytes">The body's byte budget.</param>
    /// <param name="minBodyBytes">Bodies smaller than this are discarded when the run completes.</param>
    /// <param name="keepOnlyOnFailure">Whether a successful run's log is discarded.</param>
    /// <param name="header">The header to render, or <see langword="null"/> to use <see cref="RunningHeader"/>.</param>
    /// <param name="maxFiles">
    /// Maximum number of tee files to retain once this one is kept or discarded; non-positive
    /// disables rotation. Rotation always targets <see cref="_tempDir"/>.
    /// </param>
    private (FileTeeSession Session, string Path) CreateSut(
        long maxBodyBytes = 1_048_576L,
        long minBodyBytes = 500,
        bool keepOnlyOnFailure = false,
        TeeLogHeader? header = null,
        int maxFiles = 0)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        // ReadWrite on both axes: FinalizeAsync reads the status region back before overwriting
        // it, and FileShare.ReadWrite is what lets a second handle (dtk log, or FileTeeLogStore)
        // open the same file for reading on Windows while this handle is still open for writing.
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        var rendered = (header ?? RunningHeader()).Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var bytes = Encoding.UTF8.GetBytes(rendered);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        var session = new FileTeeSession(stream, path, offset, Encoding.UTF8.GetByteCount(region),
            maxBodyBytes, minBodyBytes, keepOnlyOnFailure, _tempDir, maxFiles);
        return (session, path);
    }

    [Fact]
    public async Task AbandonedSession_LeavesAReadableRunningLog()
    {
        // This is the whole feature. A dtk process killed by SIGKILL never reaches FinalizeAsync,
        // so what this test asserts is exactly what survives a tool-call timeout.
        var (session, path) = CreateSut();

        await session.Writer.WriteLineAsync("first line".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync(CancellationToken.None);

        // Deliberately no FinalizeAsync and no DisposeAsync.
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
        header.ExitCode.Should().BeNull();
        TeeLogHeader.StripHeader(text).Should().Contain("first line");
    }

    [Fact]
    public async Task FinalizeAsync_MarksTheLogComplete_WithoutChangingItsLength()
    {
        var (session, path) = CreateSut(minBodyBytes: 0);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync(CancellationToken.None);
        var lengthBeforeFinalize = new FileInfo(path).Length;

        var hint = await session.FinalizeAsync(3);

        new FileInfo(path).Length.Should().Be(lengthBeforeFinalize);
        hint.Should().Contain(path);
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Complete);
        header.ExitCode.Should().Be(3);
        TeeLogHeader.StripHeader(text).Should().Contain("body");
    }

    [Fact]
    public async Task FinalizeAsync_DeletesTheLog_WhenTheBodyIsBelowTheGuard()
    {
        var (session, path) = CreateSut(minBodyBytes: 500);
        await session.Writer.WriteLineAsync("tiny".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_DeletesTheLog_WhenOnlyFailuresAreKeptAndTheRunSucceeded()
    {
        var (session, path) = CreateSut(minBodyBytes: 0, keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_KeepsTheLog_WhenOnlyFailuresAreKeptAndTheRunFailed()
    {
        var (session, path) = CreateSut(minBodyBytes: 0, keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(1);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_RotatesLeftoverLogs_ToTheCap_WhenThisRunsLogIsDiscarded()
    {
        // Regression guard: round 1 moved rotation onto the keep path only. On the default
        // TeeMode.Failures, a user whose builds all succeed never keeps a log, so RotateFiles
        // never runs at all -- meanwhile abandoned "Running" logs (killed mid-flight) and
        // read-back-guard failures still accumulate on disk with no bound. Rotation must also run
        // on the discard path, after this run's own file is deleted, sweeping those leftovers back
        // down to the cap. This run's own log is already gone by the time rotation runs, so it
        // cannot claim a phantom slot -- only leftovers are being trimmed.
        const int maxFiles = 2;
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"leftover-{i}.log"), "old");
        }

        var (session, path) = CreateSut(minBodyBytes: 0, keepOnlyOnFailure: true, maxFiles: maxFiles);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        File.Exists(path).Should().BeFalse();
        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(maxFiles);
    }

    [Fact]
    public async Task FinalizeAsync_StillReturnsTheHint_WhenRotationFailsToDeleteALeftoverLog()
    {
        if (OperatingSystem.IsWindows())
        {
            // POSIX permission bits (used below to force RotateFiles' File.Delete to fail) are not
            // meaningful on Windows. The scenario this guards against there is a concurrently
            // running dtk process's own open log, which FileTeeService opens without
            // FileShare.Delete, causing File.Delete to throw IOException instead.
            return;
        }

        // Regression guard: RotateFiles used to run inside FinalizeCoreAsync with no try/catch of
        // its own, so any exception it threw (e.g. File.Delete failing) propagated out to
        // FinalizeAsync's outer catch, which returns null. The log itself was written and kept
        // correctly, but the caller -- and so the user -- silently lost the "[full output: ...]"
        // hint pointing at it. Reproduced here by revoking write permission on the tee directory
        // after seeding a leftover log, so RotateFiles' File.Delete throws UnauthorizedAccessException.
        var leftover = Path.Combine(_tempDir, "leftover.log");
        await File.WriteAllTextAsync(leftover, "old");
        var (session, path) = CreateSut(minBodyBytes: 0, maxFiles: 1);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var hint = await session.FinalizeAsync(0);

            hint.Should().NotBeNull();
            hint.Should().Contain(path);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            // Restore write access so Dispose() can clean up _tempDir afterward.
            File.SetUnixFileMode(
                _tempDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Writer_StopsAppending_OnceTheByteCapIsReached()
    {
        var (session, path) = CreateSut(maxBodyBytes: 32, minBodyBytes: 0);

        for (var i = 0; i < 20; i++)
        {
            await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        await session.FinalizeAsync(0);
        var body = TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(32);
    }

    [Fact]
    public async Task Writer_StripsAnsiEscapes()
    {
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("[31mred[0m".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path)).Should().Be("red\n");
    }

    [Fact]
    public async Task Writer_UsesLineFeedEndings()
    {
        // The tee file is read back by dtk log and must not gain CRLF on Windows.
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("a".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("b".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path)).Should().Be("a\nb\n");
    }

    [Fact]
    public async Task Writer_DoesNotThrow_AndLatches_WhenTheUnderlyingStreamFailsToWrite()
    {
        // A sink that throws inside ProcessCommandRunner.PumpAsync would fail the user's build.
        // That is the one way streaming can violate "tee errors never surface", so it is latched.
        // The stream throws on its first write and then recovers, so a second write proves the
        // latch — rather than the stream's continuing failure — is what keeps the sink silent.
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        var stream = new ThrowingFileStream(path) { ThrowOnWrite = true };
        var rendered = RunningHeader().Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var headerBytes = Encoding.UTF8.GetBytes(rendered);
        // Sync on purpose: ThrowingFileStream only overrides the async write path, so writing the
        // header synchronously here (with ThrowOnWrite already set) is what keeps the header
        // itself intact while still failing the session's first real write.
#pragma warning disable CA1849, VSTHRD103, S6966
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Flush();
#pragma warning restore CA1849, VSTHRD103, S6966
        await using var session = new FileTeeSession(
            stream, path, offset, Encoding.UTF8.GetByteCount(region), 1_048_576L, 0, false);

        var act = async () => await session.Writer.WriteLineAsync("first".AsMemory(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // The stream would now succeed, but the latch from the failed write should keep this a
        // no-op rather than letting the sink resume.
        stream.ThrowOnWrite = false;
        await session.Writer.WriteLineAsync("second".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path)).Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsNull_WhenTheSessionIsAlreadyDisposed()
    {
        var (session, _) = CreateSut(minBodyBytes: 0);
        await session.DisposeAsync();

        (await session.FinalizeAsync(0)).Should().BeNull();
    }

    [Fact]
    public async Task FinalizeAsync_RoundTrips_WhenTheHeaderContainsNonAsciiText()
    {
        // The command line and cwd carry multi-byte UTF-8 text, so the char index into the
        // rendered header and its UTF-8 byte offset diverge. This pins the byte-offset convention
        // FinalizeAsync's read-back guard relies on: a caller that measured in chars instead would
        // make the guard reject the overwrite (see the companion "wrong offset" test below).
        var (session, path) = CreateSut(
            minBodyBytes: 0,
            header: new TeeLogHeader(
                "dotnet build café.slnx", "/home/user/projets/café", null, RunSource.Run,
                new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero)));
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(2);

        hint.Should().NotBeNull();
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Complete);
        header.ExitCode.Should().Be(2);
        header.CommandLine.Should().Be("dotnet build café.slnx");
        header.ProjectPath.Should().Be("/home/user/projets/café");
        TeeLogHeader.StripHeader(text).Should().Contain("body");
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsNull_AndDoesNotCorruptTheFile_WhenTheStatusOffsetIsWrong()
    {
        // Simulates a caller that measured statusRegionOffset in UTF-16 chars rather than UTF-8
        // bytes: offset 0 points at "# dtk-log v2", not the status line. The read-back guard must
        // refuse the overwrite instead of clobbering whatever is actually there.
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        var rendered = RunningHeader().Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var bytes = Encoding.UTF8.GetBytes(rendered);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        var session = new FileTeeSession(
            stream, path, statusRegionOffset: 0, Encoding.UTF8.GetByteCount(region), 1_048_576L, 0, false);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        var text = await File.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
        TeeLogHeader.StripHeader(text).Should().Contain("body");
    }

    [Fact]
    public async Task Writer_SerializesConcurrentWrites_ProducingNoInterleavedOrPartialLines()
    {
        // SessionWriter's own doc comment claims it serialises the two output pumps; a single
        // writer thread cannot exercise that claim at all, so this drives two concurrently.
        //
        // On this runtime, FileStream already serialises its own async I/O per instance (verified
        // separately: two "concurrent" large WriteAsync calls to one FileStream take exactly as
        // long, wall-clock, as two sequential ones), so two pumps racing purely on
        // stream.WriteAsync cannot produce byte-level interleaving here regardless of line size or
        // scheduling. What the runtime does NOT protect is SessionWriter's own check-then-act over
        // BodyBytesWritten -- "read remaining, truncate to fit, write, then increment" is four
        // unsynchronized statements. Without the gate, both pumps can read the same stale
        // BodyBytesWritten near the cap, each conclude it still has room for a full line, and both
        // append one -- so the body ends up LARGER than maxBodyBytes even though every individual
        // write is byte-for-byte intact. Reproduced 5/5 trials in isolation with these parameters.
        //
        // The cap is set to an exact multiple of one line's byte size so a correctly-gated run
        // lands on precisely that many bytes with only whole, untruncated lines; a missing gate
        // surfaces as the total overshooting the cap. Lines are still sized past FileStream's
        // internal write buffer and each iteration still yields, so the two loops genuinely
        // overlap instead of one running to completion before the other gets a turn.
        const int lineLength = 8_000;
        const int iterations = 400;
        const int linesAtCap = 100;
        const int maxBodyBytes = linesAtCap * (lineLength + 1);
        var (session, path) = CreateSut(maxBodyBytes: maxBodyBytes, minBodyBytes: 0);
        var lineA = new string('A', lineLength);
        var lineB = new string('B', lineLength);

        async Task WriteLoopAsync(string line)
        {
            for (var i = 0; i < iterations; i++)
            {
                await session.Writer.WriteLineAsync(line.AsMemory(), CancellationToken.None);
                await Task.Yield();
            }
        }

        await Task.WhenAll(WriteLoopAsync(lineA), WriteLoopAsync(lineB));
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path));
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().OnlyContain(line => line == lineA || line == lineB);
        lines.Should().HaveCount(linesAtCap);
        Encoding.UTF8.GetByteCount(body).Should().Be(maxBodyBytes);
    }

    [Fact]
    public async Task Writer_CutsMultiByteContentOnARuneBoundary_WhenTheByteCapIsReached()
    {
        // "é" is 2 bytes in UTF-8; an odd-sized cap forces a cut that cannot land evenly between
        // characters. Decoding a split rune back as UTF-8 (the default, replacement-fallback
        // decoder File.ReadAllTextAsync uses) would surface it as U+FFFD.
        var (session, path) = CreateSut(maxBodyBytes: 33, minBodyBytes: 0);

        await session.Writer.WriteLineAsync(new string('é', 40).AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await File.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(33);
        body.Should().NotContain("�");
    }

    /// <summary>A <see cref="FileStream"/> whose async writes can be made to fail on demand.</summary>
    /// <param name="path">The file to open for reading and writing.</param>
    private sealed class ThrowingFileStream(string path)
        : FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite)
    {
        /// <summary>Whether the next <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> call throws.</summary>
        public bool ThrowOnWrite { get; set; }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ThrowOnWrite ? throw new IOException("Simulated write failure") : base.WriteAsync(buffer, cancellationToken);
    }
}

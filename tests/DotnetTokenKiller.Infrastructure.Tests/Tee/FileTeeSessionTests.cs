using System.Globalization;
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
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // FinalizeAsync_DoesNotThrow_WhenDisposalFailsAfterAWriteFailure deliberately makes
            // disposal fail, which leaks the OS file handle (ThrowingFileStream.DisposeAsync throws
            // instead of ever calling base.DisposeAsync()). On Windows, a directory containing a file
            // with an open handle cannot be unlinked, so cleanup here is best-effort: leaving the
            // temp directory behind is preferable to a spurious IOException failing the test run.
        }
    }

    private const string ProjectPath = "/home/user/projects/MyApp";

    private static readonly DateTimeOffset RunStart = new(2026, 7, 29, 9, 14, 2, TimeSpan.Zero);

    /// <summary>A body at or above the default 500-byte floor: 600 characters plus the line feed.</summary>
    private static readonly string LargeBody = new string('x', 600) + "\n";

    private static TeeLogHeader RunningHeader() => new(
        "dotnet build MyApp.slnx",
        ProjectPath,
        null,
        RunSource.Run,
        RunStart);

    /// <summary>Writes a log as another dtk run would have left it in the tee directory.</summary>
    /// <param name="startedUtc">When that run started; the file name derives from it.</param>
    /// <param name="slug">The run's subcommand slug.</param>
    /// <param name="exitCode">Its exit code, or <see langword="null"/> for a run still in flight.</param>
    /// <param name="body">The log body.</param>
    /// <param name="projectPath">The working directory recorded in its header.</param>
    /// <returns>The log's path.</returns>
    private string SeedLog(
        DateTimeOffset startedUtc,
        string slug,
        int? exitCode,
        string body,
        string projectPath = ProjectPath)
    {
        var header = new TeeLogHeader($"dotnet {slug}", projectPath, exitCode, RunSource.Run, startedUtc);
        var path = Path.Combine(_tempDir, TeeLogFileName.Build(startedUtc, Guid.NewGuid().ToString("N"), slug));
        File.WriteAllText(path, header.Render() + body);
        return path;
    }

    /// <summary>
    /// Extracts a body's true last line, mirroring the two production steps between a raw file body
    /// (what this file works with) and what <c>TeeLogRenderer.IsTruncated</c> actually inspects:
    /// <c>LogViewUseCase.SplitLines</c> trims exactly one trailing "\n" from the raw body before
    /// splitting, and <c>view.Body</c> is then rejoined with no trailing "\n" at all — so the last
    /// line is the text after the last "\n" of the *trimmed* body, not the raw one (whose own
    /// trailing "\n", from the marker's own write, would otherwise make it look empty). Used to run
    /// the real detector (<see cref="TeeTruncationMarker.IsMarkerLine"/>) against exactly what
    /// production would see, rather than a substring <c>Contain</c> check that would pass even if the
    /// marker were glued onto the end of the previous line instead of starting its own.
    /// </summary>
    /// <param name="body">The log body (header already stripped).</param>
    /// <returns>The last line, with no leading or trailing line feed.</returns>
    private static string LastLine(string body)
    {
        var trimmed = body.EndsWith('\n') ? body[..^1] : body;
        var lastNewline = trimmed.LastIndexOf('\n');
        return lastNewline < 0 ? trimmed : trimmed[(lastNewline + 1)..];
    }

    /// <summary>Opens a session over a real file, mirroring what FileTeeService.BeginAsync does.</summary>
    /// <param name="maxBodyBytes">The body's byte budget.</param>
    /// <param name="minBodyBytes">Older logs of the same kind smaller than this are discarded when the run completes.</param>
    /// <param name="keepOnlyOnFailure">Whether older passing logs of the same kind are discarded.</param>
    /// <param name="header">The header to render, or <see langword="null"/> to use <see cref="RunningHeader"/>.</param>
    /// <param name="maxFiles">
    /// Maximum number of tee files to retain once this one is kept; non-positive disables rotation.
    /// Rotation and pruning always target <see cref="_tempDir"/>.
    /// </param>
    /// <param name="slug">The run's subcommand slug, used in its file name and for pruning.</param>
    private (FileTeeSession Session, string Path) CreateSut(
        long maxBodyBytes = 1_048_576L,
        long minBodyBytes = 500,
        bool keepOnlyOnFailure = false,
        TeeLogHeader? header = null,
        int maxFiles = 0,
        string slug = "build")
    {
        header ??= RunningHeader();
        var path = Path.Combine(
            _tempDir, TeeLogFileName.Build(header.TimestampUtc, Guid.NewGuid().ToString("N"), slug));
        // ReadWrite on both axes: FinalizeAsync reads the status region back before overwriting
        // it, and FileShare.ReadWrite is what lets a second handle (dtk log, or FileTeeLogStore)
        // open the same file for reading on Windows while this handle is still open for writing.
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        var rendered = header.Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var bytes = Encoding.UTF8.GetBytes(rendered);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        var session = new FileTeeSession(stream, path, offset, Encoding.UTF8.GetByteCount(region),
            new TeeSessionPolicy(
                maxBodyBytes, minBodyBytes, keepOnlyOnFailure, _tempDir, maxFiles, slug, header.ProjectPath));
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
        var text = await TeeLogFileReader.ReadAllTextAsync(path);
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
        var text = await TeeLogFileReader.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Complete);
        header.ExitCode.Should().Be(3);
        TeeLogHeader.StripHeader(text).Should().Contain("body");
    }

    [Fact]
    public async Task FinalizeAsync_PassingRunInFailuresMode_IsKept()
    {
        // Regression guard (C1): Failures mode used to delete a passing run's log at finalize, so
        // `dtk log` then showed an older failed run — possibly of another command — as if it were
        // the latest. The latest run of each kind is now always kept, whatever its exit code.
        var (session, path) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        File.Exists(path).Should().BeTrue();
        TeeLogHeader.TryParse(await TeeLogFileReader.ReadAllTextAsync(path), out var header).Should().BeTrue();
        header.ExitCode.Should().Be(0);
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
    public async Task FinalizeAsync_SmallestRun_IsKeptDespiteMinimumSize()
    {
        // The 500-byte floor applies to older logs only: dropping the latest run's small log would
        // let `dtk log` surface an older, stale one instead.
        var (session, path) = CreateSut(minBodyBytes: 500, keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync("123456789".AsMemory(), CancellationToken.None); // 10 bytes

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        File.Exists(path).Should().BeTrue();
        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().Be("123456789\n");
    }

    [Fact]
    public async Task FinalizeAsync_PassingRun_ReplacesOlderPassingLogOfSameSlug()
    {
        var older = SeedLog(RunStart.AddMinutes(-5), "test", 0, LargeBody);
        var (session, path) = CreateSut(keepOnlyOnFailure: true, slug: "test");
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        File.Exists(older).Should().BeFalse();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_FailingRun_ReplacesOlderPassingLogOfSameSlug()
    {
        // A failing run supersedes the passing slot too: the latest run of the kind is this one.
        var older = SeedLog(RunStart.AddMinutes(-5), "test", 0, LargeBody);
        var (session, path) = CreateSut(keepOnlyOnFailure: true, slug: "test");
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(1);

        File.Exists(older).Should().BeFalse();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_PassingRun_ReplacesOlderPassingLog_WhenItsOwnHeaderIsOver4KB()
    {
        // A long --filter makes a header longer than the 4 KB pruning reads of each log. The kept
        // log's own project must not depend on reading its header back, or such a run would never
        // replace the passing slot and `dtk log` could still show the older run beside it.
        var older = SeedLog(RunStart.AddMinutes(-5), "test", 0, LargeBody);
        var longCommand = "dotnet test --filter " + string.Join('|', Enumerable.Range(0, 400).Select(i => $"Name~Test{i}"));
        Encoding.UTF8.GetByteCount(longCommand).Should().BeGreaterThan(4096);
        var (session, path) = CreateSut(
            keepOnlyOnFailure: true, header: RunningHeader() with { CommandLine = longCommand }, slug: "test");
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        File.Exists(older).Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_PassingRun_KeepsOlderFailingLogOfSameSlug()
    {
        var older = SeedLog(RunStart.AddMinutes(-5), "test", 1, LargeBody);
        var (session, _) = CreateSut(keepOnlyOnFailure: true, slug: "test");
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(older).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_PassingRun_KeepsLogsOfOtherSlugs()
    {
        // One slot per kind, not one overall: `dtk log build` must still find the passing build
        // after a passing test ran.
        var build = SeedLog(RunStart.AddMinutes(-5), "build", 0, LargeBody);
        var (session, _) = CreateSut(keepOnlyOnFailure: true, slug: "test");
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(build).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_PassingRun_KeepsOlderPassingLogOfAnotherProject()
    {
        // `dtk log` is scoped to the working directory: pruning another project's latest passing
        // run would make `dtk log` there fall back to an older, stale log of that project.
        var otherProject = SeedLog(RunStart.AddMinutes(-5), "build", 0, LargeBody, "/home/user/projects/Other");
        var (session, _) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(otherProject).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotDeleteConcurrentRunningLog()
    {
        // Agents often run builds and tests in parallel against one tee directory. An older run of
        // the same kind that is still in flight has a "running" header and must survive, however
        // small its body is so far — it is another process's open log.
        var (concurrent, concurrentPath) = CreateSut(
            keepOnlyOnFailure: true, header: RunningHeader() with { TimestampUtc = RunStart.AddSeconds(-30) });
        await concurrent.Writer.WriteLineAsync("tiny".AsMemory(), CancellationToken.None);
        await concurrent.Writer.FlushAsync(CancellationToken.None);
        var (session, path) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(concurrentPath).Should().BeTrue();

        // Finishing later, the older run must not take the newer run's log with it either.
        await concurrent.FinalizeAsync(0);
        File.Exists(concurrentPath).Should().BeTrue();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotDeleteANewerLogOfSameSlug()
    {
        // Two parallel runs of one kind can finish out of order: the one that started first must
        // not prune the one that started later and already finished.
        var newer = SeedLog(RunStart.AddMinutes(5), "build", 0, "tiny\n");
        var (session, _) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(newer).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotDeleteAnOlderLogItCannotParse()
    {
        // Not provably a finished passing or undersized run (a header this version cannot read, or
        // a log another process created but has not written its header to yet) — so not ours to drop.
        var unreadable = Path.Combine(
            _tempDir, TeeLogFileName.Build(RunStart.AddMinutes(-5), Guid.NewGuid().ToString("N"), "build"));
        await File.WriteAllTextAsync(unreadable, "# dtk-log v9\n# exit: 0\n---\ntiny\n");
        var empty = Path.Combine(
            _tempDir, TeeLogFileName.Build(RunStart.AddMinutes(-4), Guid.NewGuid().ToString("N"), "build"));
        await File.WriteAllTextAsync(empty, string.Empty);
        var (session, _) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(unreadable).Should().BeTrue();
        File.Exists(empty).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_InAlwaysMode_KeepsOlderPassingLogsOfSameSlug()
    {
        var older = SeedLog(RunStart.AddMinutes(-5), "build", 0, LargeBody);
        var (session, _) = CreateSut(keepOnlyOnFailure: false);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        File.Exists(older).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_StillReturnsTheHint_WhenPruningFailsToDeleteASupersededLog()
    {
        if (OperatingSystem.IsWindows())
        {
            // POSIX permission bits (used below to force the prune's File.Delete to fail) are not
            // meaningful on Windows. The scenario this guards against there is another process
            // holding the superseded log open without FileShare.Delete.
            return;
        }

        var older = SeedLog(RunStart.AddMinutes(-5), "build", 0, LargeBody);
        var (session, path) = CreateSut(keepOnlyOnFailure: true);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var hint = await session.FinalizeAsync(0);

            hint.Should().Contain(path);
            File.Exists(older).Should().BeTrue();
        }
        finally
        {
            // Restore write access so Dispose() can clean up _tempDir afterward.
            File.SetUnixFileMode(
                _tempDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task FinalizeAsync_RotatesLeftoverLogs_ToTheCap_WhenAPassingRunIsKept()
    {
        // A kept passing run counts toward MaxFiles like any other log, and rotation still sweeps
        // leftovers (abandoned "Running" logs from runs killed mid-flight, logs from a read-back-guard
        // failure) back down to the cap, oldest first.
        const int maxFiles = 2;
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempDir, TeeLogFileName.Build(RunStart.AddMinutes(-10 + i), $"leftover{i}", "build")),
                "old");
        }

        var (session, path) = CreateSut(keepOnlyOnFailure: true, maxFiles: maxFiles);
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        Directory.GetFiles(_tempDir, "*.log").Should().HaveCount(maxFiles).And.Contain(path);
    }

    [Fact]
    public async Task FinalizeAsync_NeverRotatesAwayTheLogItJustKept()
    {
        // Runs finish out of start order: logs that started later can already be on disk, so the log
        // just kept can be the oldest by name. Rotating it away, right after it pruned its passing
        // predecessor, would leave `dtk log build` showing an older failure as the latest build.
        var predecessor = SeedLog(RunStart.AddMinutes(-10), "build", 0, LargeBody);
        var olderFailure = SeedLog(RunStart.AddMinutes(-20), "build", 1, LargeBody);
        SeedLog(RunStart.AddMinutes(1), "test", 1, LargeBody);
        var newest = SeedLog(RunStart.AddMinutes(2), "test", 1, LargeBody);
        var (session, path) = CreateSut(keepOnlyOnFailure: true, maxFiles: 2);
        await session.Writer.WriteLineAsync(LargeBody.AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().Contain(path);
        File.Exists(predecessor).Should().BeFalse();
        File.Exists(olderFailure).Should().BeFalse();
        Directory.GetFiles(_tempDir, "*.log").Should().BeEquivalentTo(path, newest);
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
        // Named as an older log so it, not this run's, is the one rotation picks as the oldest.
        var leftover = Path.Combine(_tempDir, TeeLogFileName.Build(RunStart.AddMinutes(-5), "leftover", "build"));
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
            File.Exists(leftover).Should().BeTrue();
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
        // 100 comfortably fits the truncation marker (38 bytes for this three-digit cap, leading
        // line feed included) alongside some real content, so the body still shows both once the
        // cap is hit.
        const int maxBodyBytes = 100;
        var (session, path) = CreateSut(maxBodyBytes: maxBodyBytes, minBodyBytes: 0);

        for (var i = 0; i < 20; i++)
        {
            await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        await session.FinalizeAsync(0);
        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(maxBodyBytes);
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_WritesTheMarker_AsSoonAsTheCapIsHit_WithoutFinalizing()
    {
        // There is no header field for this (see TeeTruncationMarker's remarks): the marker line is
        // the sole signal, and it is flushed to disk by the writer itself, independently of
        // FinalizeAsync -- exactly like the "Running" status survives a run that never finalizes,
        // this proves a run killed right after the cap is hit still leaves a body that says so.
        var (session, path) = CreateSut(maxBodyBytes: 100, minBodyBytes: 0);

        for (var i = 0; i < 20; i++)
        {
            await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        // Deliberately no FinalizeAsync and no DisposeAsync.
        var text = await TeeLogFileReader.ReadAllTextAsync(path);
        TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
        header.Status.Should().Be(TeeLogStatus.Running);
        TeeTruncationMarker.IsMarkerLine(LastLine(TeeLogHeader.StripHeader(text))).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_DoesNotWriteTheMarker_WhenOutputExactlyFillsTheCapAndNoMoreIsWritten()
    {
        // A line landing exactly on the content budget is not proof anything was lost -- it might
        // simply have been the last line the child ever produced. Appending the marker here would
        // be a false positive. 42 is precomputed so the content budget (cap minus the 37-byte marker
        // for this two-digit cap, leading line feed included) comes out to exactly 5 bytes -- the
        // length of "abcd\n".
        var (session, path) = CreateSut(maxBodyBytes: 42, minBodyBytes: 0);

        await session.Writer.WriteLineAsync("abcd".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var text = await TeeLogFileReader.ReadAllTextAsync(path);
        TeeLogHeader.StripHeader(text).Should().Be("abcd\n");
    }

    [Fact]
    public async Task Writer_WritesTheMarkerCleanly_WhenALineLandsExactlyOnTheBudgetAndMoreFollows()
    {
        // Companion to the test above: this time more output does follow, so the marker must be
        // written -- and, because the preceding line already ended with "\n" on its own, without an
        // extra blank line before it. 42 again gives a 5-byte content budget, exactly "abcd\n".
        var (session, path) = CreateSut(maxBodyBytes: 42, minBodyBytes: 0);

        await session.Writer.WriteLineAsync("abcd".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("more".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        body.Should().Be("abcd\n[dtk: output truncated at 42 bytes]\n");
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_WritesTheMarkerCleanly_WhenTheCutLineKeepsNothing()
    {
        // 42 gives a 5-byte content budget: "abc\n" leaves 1 byte, too small for the 2-byte "é", so
        // the cut writes nothing and the body still ends with "abc\n"'s own line feed -- the marker
        // must not add a blank line before itself.
        var (session, path) = CreateSut(maxBodyBytes: 42, minBodyBytes: 0);

        await session.Writer.WriteLineAsync("abc".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("é".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        body.Should().Be("abc\n[dtk: output truncated at 42 bytes]\n");
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_WritesTheMarkerOnlyOnce_WhenManyMoreLinesOverflowTheCap()
    {
        var (session, path) = CreateSut(maxBodyBytes: 100, minBodyBytes: 0);

        for (var i = 0; i < 50; i++)
        {
            await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        await session.FinalizeAsync(0);
        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        var markerOccurrences = body.Split("[dtk: output truncated", StringSplitOptions.None).Length - 1;
        markerOccurrences.Should().Be(1);
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_ProducesADetectableMarkerLine_WhenARealMultiLineBodyIsCutMidLine()
    {
        // The regression this guards: a mid-line cut must leave the marker as its own clean line,
        // not glued onto the tail of the cut content ("xxxx...x[dtk: output truncated ...]"), which
        // TeeTruncationMarker.IsMarkerLine's anchored match would silently fail to detect -- dtk log
        // would never warn for a real log that hit this path. Driven over a realistic multi-line
        // body (distinct complete lines, then one long line cut mid-way through, then more output
        // that must be dropped entirely) through the actual writer, not a hand-built string, with
        // the real detector run against the real last line on disk afterward.
        const int maxBodyBytes = 200;
        var (session, path) = CreateSut(maxBodyBytes: maxBodyBytes, minBodyBytes: 0);

        await session.Writer.WriteLineAsync("first line".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("second line".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("third line".AsMemory(), CancellationToken.None);
        // Far longer than whatever room is left after the three short lines above (at most 200
        // bytes minus the marker's reserved room), so this one is cut mid-line rather than dropped
        // whole or fitting cleanly.
        await session.Writer.WriteLineAsync(new string('x', 500).AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("this must never appear".AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(maxBodyBytes);
        body.Should().Contain("first line").And.Contain("second line").And.Contain("third line");
        body.Should().NotContain("this must never appear");
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task Writer_UsesTheFullCapForContent_WhenTheCapIsSmallerThanTheMarkerItself()
    {
        // A pathologically tiny cap: the marker text itself ("[dtk: output truncated at 5 bytes]\n")
        // cannot fit at all. Reserving room for it anyway would only throw content away for a marker
        // that will never be written, so the writer uses the whole cap for real content instead --
        // there is no way to signal truncation in this scenario (nothing else exists but the marker
        // line to do it), but at least the little content that fits is not needlessly sacrificed too.
        var (session, path) = CreateSut(maxBodyBytes: 5, minBodyBytes: 0);

        await session.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().Be(5);
        body.Should().Be("xxxxx");
    }

    [Fact]
    public async Task FinalizeAsync_KeepsAnOlderTruncatedLog_EvenWhenItsBodyIsUnderTheMinBodyBytesFloor()
    {
        // Regression guard: hitting the cap already proves the run produced at least that many
        // bytes of output, so the MinBodyBytes floor (which exists to drop a one-line "Restored.")
        // must not also discard a log that hit a byte cap configured below the floor -- otherwise
        // every truncated log on a small cap is silently thrown away once a newer run of its kind
        // completes, the exact bug the floor's own clamp (Math.Min(500, MaxFileSizeBytes) in
        // FileTeeService.BeginAsync) exists to prevent.
        var (older, olderPath) = CreateSut(
            maxBodyBytes: 100, minBodyBytes: 1000, header: RunningHeader() with { TimestampUtc = RunStart.AddMinutes(-5) });
        for (var i = 0; i < 20; i++)
        {
            await older.Writer.WriteLineAsync(new string('x', 40).AsMemory(), CancellationToken.None);
        }

        await older.FinalizeAsync(1);
        var (session, _) = CreateSut(maxBodyBytes: 100, minBodyBytes: 1000);
        await session.Writer.WriteLineAsync("tiny".AsMemory(), CancellationToken.None);

        await session.FinalizeAsync(1);

        File.Exists(olderPath).Should().BeTrue();
        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(olderPath));
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_DropsAnOlderShortLog_WhenItNeverHitTheCap()
    {
        // Companion to the truncated-bypass test above: the MinBodyBytes floor must still apply
        // normally to an older run that never hit the cap at all, failed or not.
        var older = SeedLog(RunStart.AddMinutes(-5), "build", 1, "tiny\n");
        var (session, path) = CreateSut(maxBodyBytes: 1_048_576L, minBodyBytes: 1000);
        await session.Writer.WriteLineAsync("tiny".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(1);

        hint.Should().Contain(path);
        File.Exists(older).Should().BeFalse();
    }

    [Fact]
    public async Task Writer_StripsAnsiEscapes()
    {
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("[31mred[0m".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().Be("red\n");
    }

    [Fact]
    public async Task Writer_UsesLineFeedEndings()
    {
        // The tee file is read back by dtk log and must not gain CRLF on Windows.
        var (session, path) = CreateSut(minBodyBytes: 0);

        await session.Writer.WriteLineAsync("a".AsMemory(), CancellationToken.None);
        await session.Writer.WriteLineAsync("b".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().Be("a\nb\n");
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
            stream, path, offset, Encoding.UTF8.GetByteCount(region), new TeeSessionPolicy(1_048_576L, 0, false));

        var act = async () => await session.Writer.WriteLineAsync("first".AsMemory(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // The stream would now succeed, but the latch from the failed write should keep this a
        // no-op rather than letting the sink resume.
        stream.ThrowOnWrite = false;
        await session.Writer.WriteLineAsync("second".AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writer_FlushDoesNotThrow_AndLatches_WhenTheUnderlyingStreamFailsToFlush()
    {
        // The pump flushes after every line, so a failing flush reaches the user's command by the
        // same route a failing write would. It must be swallowed and latched identically — once
        // latched, later flushes must not even touch the stream.
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        await using var stream = new ThrowingFileStream(path);
        var rendered = RunningHeader().Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var headerBytes = Encoding.UTF8.GetBytes(rendered);
#pragma warning disable CA1849, VSTHRD103, S6966
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Flush();
#pragma warning restore CA1849, VSTHRD103, S6966
        await using var session = new FileTeeSession(
            stream, path, offset, Encoding.UTF8.GetByteCount(region), new TeeSessionPolicy(1_048_576L, 0, false));

        stream.ThrowOnFlush = true;
        var act = async () => await session.Writer.FlushAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Latched: the stream would flush fine now, but the sink is done for the rest of the run,
        // so the write below never reaches the file either.
        stream.ThrowOnFlush = false;
        await session.Writer.FlushAsync(CancellationToken.None);
        await session.Writer.WriteLineAsync("after the failure".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync();

        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writer_ReportsUtf8AndLineFeed_AndIgnoresAttemptsToChangeTheLineEnding()
    {
        // PumpAsync captures sink.NewLine to build the text it hands the filter, so this property
        // is what keeps that text identical on every platform — it must stay LF even if set.
        var (session, _) = CreateSut(minBodyBytes: 0);

        session.Writer.Encoding.Should().Be(Encoding.UTF8);
        session.Writer.NewLine.Should().Be("\n");

        session.Writer.NewLine = "\r\n";

        session.Writer.NewLine.Should().Be("\n");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Writer_IgnoresSingleCharacterWrites_WhichTheLineBasedPumpNeverUses()
    {
        // Write(char) exists only because TextWriter requires it; routing it through the async path
        // would deadlock, so it must be a no-op rather than reach the file by some other route.
        var (session, path) = CreateSut(minBodyBytes: 0);

        // Sync on purpose: this is the path TextWriter's non-async members funnel into, and the
        // point of the test is that it reaches the file through no route at all.
#pragma warning disable CA1849, VSTHRD103, S6966
        session.Writer.Write('x');
        session.Writer.Write("ignored too");
#pragma warning restore CA1849, VSTHRD103, S6966
        await session.FinalizeAsync(0);

        TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path)).Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotThrow_WhenDisposalFailsAfterAWriteFailure()
    {
        // Reproduces the outer catch's own cleanup throwing: FlushAsync fails first (e.g. disk
        // full), landing in FinalizeAsync's catch clause, whose own DisposeAsync() call re-flushes
        // the same broken stream on close and would throw again -- unless DisposeAsync swallows it,
        // that second throw escapes FinalizeAsync entirely, costing the caller the child's exit code.
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        var stream = new ThrowingFileStream(path);
        var rendered = RunningHeader().Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var headerBytes = Encoding.UTF8.GetBytes(rendered);
#pragma warning disable CA1849, VSTHRD103, S6966
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Flush();
#pragma warning restore CA1849, VSTHRD103, S6966
        await using var session = new FileTeeSession(
            stream, path, offset, Encoding.UTF8.GetByteCount(region), new TeeSessionPolicy(1_048_576L, 0, false));
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        stream.ThrowOnFlush = true;
        stream.ThrowOnDispose = true;
        var act = async () => await session.FinalizeAsync(0);

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();

        // The assertion above already proved FinalizeAsync's own DisposeAsync() swallows the
        // simulated dispose failure -- which also means the stream's real OS handle was never
        // released, since ThrowingFileStream.DisposeAsync throws before ever reaching
        // base.DisposeAsync(). Release it now, strictly after that behaviour has been asserted, so
        // the fixture's Dispose() can unlink _tempDir even on Windows instead of leaking the handle.
        stream.ThrowOnDispose = false;
        await stream.DisposeAsync();
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
        var text = await TeeLogFileReader.ReadAllTextAsync(path);
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
            stream, path, statusRegionOffset: 0, Encoding.UTF8.GetByteCount(region),
            new TeeSessionPolicy(1_048_576L, 0, false));
        await session.Writer.WriteLineAsync("body".AsMemory(), CancellationToken.None);

        var hint = await session.FinalizeAsync(0);

        hint.Should().BeNull();
        var text = await TeeLogFileReader.ReadAllTextAsync(path);
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
        const int contentBudget = linesAtCap * (lineLength + 1);

        // The marker now eats into the cap, so the cap has to be contentBudget PLUS room for the
        // marker for the content portion to still land on exactly contentBudget bytes. The marker's
        // own length depends on the cap value it reports, so this converges to a fixed point rather
        // than being solved for directly; two iterations are always enough here since padding by a
        // few dozen bytes never changes the cap's digit count.
        var maxBodyBytes = contentBudget;
        for (var i = 0; i < 2; i++)
        {
            // Leading "\n" included, matching SessionWriter.ComputeCap exactly: it is reserved
            // unconditionally (skipped at the write site only when not needed), so it counts against
            // the budget here regardless of whether this particular run ends up needing it.
            var markerLength = Encoding.UTF8.GetByteCount(
                $"\n[dtk: output truncated at {maxBodyBytes.ToString(CultureInfo.InvariantCulture)} bytes]\n");
            maxBodyBytes = contentBudget + markerLength;
        }

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

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        var marker = $"[dtk: output truncated at {maxBodyBytes.ToString(CultureInfo.InvariantCulture)} bytes]";
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var contentLines = lines.Where(line => line != marker).ToArray();
        contentLines.Should().OnlyContain(line => line == lineA || line == lineB);
        contentLines.Should().HaveCount(linesAtCap);
        lines.Should().Contain(marker);
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();

        // One byte short of maxBodyBytes, not equal to it: content always fills contentBudget with
        // exactly linesAtCap whole lines here, so the marker is always appended by the
        // "remaining <= 0" branch (never a mid-line cut) -- the preceding line already ends with
        // "\n" on its own, so the reserved-but-unneeded leading line feed byte in the marker's own
        // bytes is skipped, same as every other cap-exact-fit scenario.
        Encoding.UTF8.GetByteCount(body).Should().Be(maxBodyBytes - 1);
    }

    [Fact]
    public async Task Writer_CutsMultiByteContentOnARuneBoundary_WhenTheByteCapIsReached()
    {
        // "é" is 2 bytes in UTF-8; an odd content budget (101-byte cap minus the 38-byte marker for
        // this three-digit cap, leading line feed included, leaving 63) forces a cut that cannot
        // land evenly between characters. Decoding a split rune back as UTF-8 (the default,
        // replacement-fallback decoder File.ReadAllTextAsync uses) would surface it as U+FFFD. This
        // is also a genuine mid-line cut (the whole point of TeeTruncationMarker.IsMarkerLine being
        // anchored on the real last line below, not a Contain check): the cut content never ends
        // with "\n" of its own, so the marker needs its reserved leading line feed to start cleanly.
        const int maxBodyBytes = 101;
        var (session, path) = CreateSut(maxBodyBytes: maxBodyBytes, minBodyBytes: 0);

        await session.Writer.WriteLineAsync(new string('é', 40).AsMemory(), CancellationToken.None);
        await session.FinalizeAsync(0);

        var body = TeeLogHeader.StripHeader(await TeeLogFileReader.ReadAllTextAsync(path));
        Encoding.UTF8.GetByteCount(body).Should().BeLessThanOrEqualTo(maxBodyBytes);
        body.Should().NotContain("�");
        TeeTruncationMarker.IsMarkerLine(LastLine(body)).Should().BeTrue();
    }

    /// <summary>A <see cref="FileStream"/> whose async writes, flushes, and disposal can be made to fail on demand.</summary>
    /// <param name="path">The file to open for reading and writing.</param>
    private sealed class ThrowingFileStream(string path)
        : FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite)
    {
        /// <summary>Whether the next <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> call throws.</summary>
        public bool ThrowOnWrite { get; set; }

        /// <summary>Whether the next <see cref="FlushAsync(CancellationToken)"/> call throws.</summary>
        public bool ThrowOnFlush { get; set; }

        /// <summary>Whether <see cref="DisposeAsync"/> throws instead of disposing the base stream.</summary>
        public bool ThrowOnDispose { get; set; }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ThrowOnWrite ? throw new IOException("Simulated write failure") : base.WriteAsync(buffer, cancellationToken);

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            ThrowOnFlush ? throw new IOException("Simulated flush failure") : base.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        public override ValueTask DisposeAsync() =>
            ThrowOnDispose ? throw new IOException("Simulated dispose failure") : base.DisposeAsync();
    }
}

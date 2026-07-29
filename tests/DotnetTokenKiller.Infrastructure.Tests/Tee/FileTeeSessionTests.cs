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
    private (FileTeeSession Session, string Path) CreateSut(
        long maxBodyBytes = 1_048_576L,
        long minBodyBytes = 500,
        bool keepOnlyOnFailure = false)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.log");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var header = RunningHeader();
        var rendered = header.Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var bytes = Encoding.UTF8.GetBytes(rendered);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        var session = new FileTeeSession(stream, path, offset, Encoding.UTF8.GetByteCount(region),
            maxBodyBytes, minBodyBytes, keepOnlyOnFailure);
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
    public async Task Writer_DoesNotThrow_WhenTheUnderlyingStreamIsBroken()
    {
        // A sink that throws inside ProcessCommandRunner.PumpAsync would fail the user's build.
        // That is the one way streaming can violate "tee errors never surface", so it is latched.
        var (session, _) = CreateSut(minBodyBytes: 0);
        await session.DisposeAsync();

        var act = async () =>
        {
            await session.Writer.WriteLineAsync("after disposal".AsMemory(), CancellationToken.None);
            await session.Writer.FlushAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsNull_WhenTheSessionIsAlreadyDisposed()
    {
        var (session, _) = CreateSut(minBodyBytes: 0);
        await session.DisposeAsync();

        (await session.FinalizeAsync(0)).Should().BeNull();
    }
}

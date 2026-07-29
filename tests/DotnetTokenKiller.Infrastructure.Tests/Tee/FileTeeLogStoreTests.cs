using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class FileTeeLogStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-logstore-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private FileTeeLogStore CreateSut() =>
        new(new FakeConfigProvider(DtkConfig.Default with { Tee = new TeeConfig(TeeMode.Always) }), _tempDir);

    private void WriteLog(DateTimeOffset timestamp, string slug, string cwd, int exitCode, string body)
    {
        Directory.CreateDirectory(_tempDir);
        var header = new TeeLogHeader($"dotnet {slug}", cwd, exitCode, RunSource.Run, timestamp);
        var path = Path.Combine(_tempDir, TeeLogFileName.Build(timestamp, Guid.NewGuid().ToString("N"), slug));
        File.WriteAllText(path, header.Render() + body);
    }

    private void WriteLegacyLog(DateTimeOffset timestamp, string slug, string body)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, TeeLogFileName.Build(timestamp, Guid.NewGuid().ToString("N"), slug));
        File.WriteAllText(path, body);
    }

    private static DateTimeOffset At(int minute) =>
        new(2026, 7, 28, 9, minute, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenTheDirectoryDoesNotExist()
    {
        var entries = await CreateSut().ListAsync();

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenTheDirectoryIsUnreadable()
    {
        // Covers Directory.GetFiles throwing UnauthorizedAccessException on a directory that
        // exists but cannot be enumerated (e.g. the 0700 tee directory owned by another user).
        // POSIX permission bits are a no-op on Windows, so this is guarded there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteLog(At(1), "build", "/proj", 0, "body");
        var originalMode = File.GetUnixFileMode(_tempDir);
        try
        {
            File.SetUnixFileMode(_tempDir, UnixFileMode.None);

            // Confirm the mode change actually blocks access on this filesystem/user before
            // asserting on it — running as root (common in CI containers) ignores 000 entirely,
            // which would make the assertion below meaningless rather than merely skipped.
            var reallyBlocked = false;
            try
            {
                Directory.GetFiles(_tempDir);
            }
            catch (UnauthorizedAccessException)
            {
                reallyBlocked = true;
            }

            if (!reallyBlocked)
            {
                return;
            }

            var entries = await CreateSut().ListAsync();

            entries.Should().BeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(_tempDir, originalMode);
        }
    }

    [Fact]
    public async Task ListAsync_OrdersNewestFirst()
    {
        WriteLog(At(1), "build", "/proj", 1, "old");
        WriteLog(At(5), "test", "/proj", 0, "new");

        var entries = await CreateSut().ListAsync();

        entries.Select(e => e.Slug).Should().ContainInOrder("test", "build");
    }

    [Fact]
    public async Task ListAsync_ParsesTheHeader()
    {
        WriteLog(At(1), "build", "/home/user/proj", 2, "body");

        var entry = (await CreateSut().ListAsync()).Single();

        entry.Header.Should().NotBeNull();
        entry.Header!.ProjectPath.Should().Be("/home/user/proj");
        entry.Header.ExitCode.Should().Be(2);
        entry.Slug.Should().Be("build");
        entry.SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListAsync_ReturnsANullHeader_ForALegacyFile()
    {
        WriteLegacyLog(At(1), "build", "raw output with no header\n");

        var entry = (await CreateSut().ListAsync()).Single();

        entry.Header.Should().BeNull();
        entry.Slug.Should().Be("build");
        entry.TimestampUtc.Should().Be(At(1));
    }

    [Fact]
    public async Task ListAsync_IgnoresFilesThatAreNotLogs()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "not a log");
        WriteLog(At(1), "build", "/proj", 0, "body");

        var entries = await CreateSut().ListAsync();

        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_HandlesALogFileWhoseNameItDidNotProduce()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "handwritten.log"), "raw");

        var entry = (await CreateSut().ListAsync()).Single();

        // Falls back to the file's write time rather than dropping it from the listing.
        entry.Slug.Should().BeEmpty();
        entry.Header.Should().BeNull();
    }

    [Fact]
    public async Task ReadBodyAsync_ReturnsTheBodyWithoutTheHeader()
    {
        WriteLog(At(1), "build", "/proj", 1, "line one\nline two\n");
        var sut = CreateSut();
        var entry = (await sut.ListAsync()).Single();

        var body = await sut.ReadBodyAsync(entry);

        body.Should().Be("line one\nline two\n");
    }

    [Fact]
    public async Task ReadBodyAsync_ReturnsTheWholeFile_ForALegacyLog()
    {
        WriteLegacyLog(At(1), "build", "raw output\n");
        var sut = CreateSut();
        var entry = (await sut.ListAsync()).Single();

        var body = await sut.ReadBodyAsync(entry);

        body.Should().Be("raw output\n");
    }

    [Fact]
    public async Task ListAsync_ParsesTheHeader_ForAFileShorterThanTheHeadReadLimit()
    {
        // A short body means the whole file is well under the 4096-byte head-read window. A single
        // ReadAsync call on the underlying stream is not guaranteed to fill the buffer even when the
        // requested bytes are all available, so this guards against treating that short read as a
        // parse failure instead of retrying until end-of-stream.
        WriteLog(At(1), "build", "/proj", 0, "ok");

        var entry = (await CreateSut().ListAsync()).Single();

        entry.Header.Should().NotBeNull();
        entry.Header!.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task WrittenByFileTeeService_IsReadableByTheStore()
    {
        var config = new FakeConfigProvider(
            DtkConfig.Default with { Tee = new TeeConfig(TeeMode.Always) });
        var writer = new FileTeeService(config, _tempDir);
        var header = new TeeLogHeader(
            "dotnet build MyApp.slnx", "/home/user/proj", 1, RunSource.Run, At(3));
        var body = new string('x', 600);

        await writer.TeeAndHintAsync(body, "list package", header);

        var store = new FileTeeLogStore(config, _tempDir);
        var entry = (await store.ListAsync()).Single();
        entry.Header.Should().NotBeNull();
        entry.Header!.ProjectPath.Should().Be("/home/user/proj");
        entry.Header.ExitCode.Should().Be(1);
        entry.Slug.Should().Be("list-package");
        (await store.ReadBodyAsync(entry)).Should().Be(body);
    }

    [Fact]
    public async Task ReadBodyAsync_ReadsALiveLog_WhileItsWriterStreamIsStillOpen()
    {
        // FileTeeSession opens its FileStream for writing; on Windows, a second handle can only be
        // opened if that first handle's share mode permits it. If either side regresses from
        // FileShare.ReadWrite back to FileShare.Read, `dtk log` on a command that is still running
        // fails to open the file at all — this is the feature's whole point on that platform.
        Directory.CreateDirectory(_tempDir);
        var timestamp = At(1);
        var path = Path.Combine(_tempDir, TeeLogFileName.Build(timestamp, Guid.NewGuid().ToString("N"), "build"));
        var header = new TeeLogHeader("dotnet build MyApp.slnx", "/home/user/proj", null, RunSource.Run, timestamp);
        var rendered = header.Render();
        var region = TeeLogHeader.RenderStatusAndExit(null);
        var charIndex = rendered.IndexOf(region, StringComparison.Ordinal);
        var offset = Encoding.UTF8.GetByteCount(rendered.AsSpan(0, charIndex));
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        var headerBytes = Encoding.UTF8.GetBytes(rendered);
        await stream.WriteAsync(headerBytes);
        await stream.FlushAsync();
        await using var session = new FileTeeSession(
            stream, path, offset, Encoding.UTF8.GetByteCount(region), 1_048_576L, 0, false);
        await session.Writer.WriteLineAsync("in flight".AsMemory(), CancellationToken.None);
        await session.Writer.FlushAsync(CancellationToken.None);

        var sut = CreateSut();
        var entry = (await sut.ListAsync()).Single();
        var body = await sut.ReadBodyAsync(entry);

        entry.Header.Should().NotBeNull();
        entry.Header!.Status.Should().Be(TeeLogStatus.Running);
        body.Should().Contain("in flight");
    }
}

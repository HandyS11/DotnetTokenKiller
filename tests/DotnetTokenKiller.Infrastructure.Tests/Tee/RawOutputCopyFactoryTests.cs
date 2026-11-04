using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class RawOutputCopyFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-raw-copy-test-{Guid.NewGuid():N}");
    private readonly RawOutputCopyFactory _sut = new();

    public RawOutputCopyFactoryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Open_CreatesOwnerOnlyFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var path = Path.Combine(_tempDir, "raw.log");

        await using (_sut.Open(path))
        {
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Open_TruncatesExisting()
    {
        var path = Path.Combine(_tempDir, "raw.log");
        await File.WriteAllTextAsync(path, "an older run's output, much longer than this one's\n");

        await using (var copy = _sut.Open(path))
        {
            await copy.Writer.WriteLineAsync("new".AsMemory());
        }

        (await File.ReadAllTextAsync(path)).Should().Be("new\n");
    }

    [Fact]
    public async Task Open_ResolvesARelativePathAgainstTheWorkingDirectory()
    {
        var name = $"dtk-raw-copy-{Guid.NewGuid():N}.log";
        var expected = Path.Combine(Environment.CurrentDirectory, name);
        try
        {
            await using (var copy = _sut.Open(name))
            {
                await copy.Writer.WriteLineAsync("line".AsMemory());
            }

            (await File.ReadAllTextAsync(expected)).Should().Be("line\n");
        }
        finally
        {
            File.Delete(expected);
        }
    }

    [Fact]
    public void Open_Throws_WhenParentMissing()
    {
        // As with shell redirection, a missing parent directory is not created for the user.
        var path = Path.Combine(_tempDir, "missing", "raw.log");

        var act = () => _sut.Open(path);

        act.Should().Throw<RawOutputCopyException>()
            .Which.Message.Should().StartWith(path + ": ").And.Contain("Could not find a part of the path");
        Directory.Exists(Path.Combine(_tempDir, "missing")).Should().BeFalse();
    }

    [Fact]
    public void Open_Throws_WhenThePathIsADirectory()
    {
        var act = () => _sut.Open(_tempDir);

        act.Should().Throw<RawOutputCopyException>().Which.Message.Should().StartWith(_tempDir + ": ");
    }

    [Fact]
    public async Task Writer_StripsAnsi()
    {
        var path = Path.Combine(_tempDir, "raw.log");

        await using (var copy = _sut.Open(path))
        {
            await copy.Writer.WriteLineAsync("\e[31merror\e[0m CS0103".AsMemory());
            await copy.Writer.FlushAsync(CancellationToken.None);
        }

        (await File.ReadAllTextAsync(path)).Should().Be("error CS0103\n");
    }

    [Fact]
    public async Task Writer_KeepsEveryLineOfTwoConcurrentWriters()
    {
        // The output pump writes stdout and stderr from two tasks at once, each line followed by a flush.
        var path = Path.Combine(_tempDir, "raw.log");
        const int linesPerWriter = 500;

        await using (var copy = _sut.Open(path))
        {
            await Task.WhenAll(WriteLinesAsync(copy.Writer, "out"), WriteLinesAsync(copy.Writer, "err"));
            copy.Failure.Should().BeNull();
        }

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(2 * linesPerWriter);
        lines.Where(line => line.StartsWith("out ", StringComparison.Ordinal))
            .Should().Equal(Enumerable.Range(0, linesPerWriter).Select(i => $"out {i}"));
        lines.Where(line => line.StartsWith("err ", StringComparison.Ordinal))
            .Should().Equal(Enumerable.Range(0, linesPerWriter).Select(i => $"err {i}"));

        static async Task WriteLinesAsync(TextWriter writer, string prefix)
        {
            await Task.Yield();
            for (var i = 0; i < linesPerWriter; i++)
            {
                await writer.WriteLineAsync($"{prefix} {i}".AsMemory());
                await writer.FlushAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Writer_RecordsFailure_DoesNotThrow()
    {
        var stream = new FileStream(Path.Combine(_tempDir, "raw.log"), FileMode.Create, FileAccess.Write);
        await using var copy = new RawOutputCopy(stream);
        await copy.Writer.WriteLineAsync("kept".AsMemory());
        await copy.Writer.FlushAsync(CancellationToken.None);

        await stream.DisposeAsync();
        var write = async () =>
        {
            await copy.Writer.WriteLineAsync("lost".AsMemory());
            await copy.Writer.FlushAsync(CancellationToken.None);
            await copy.Writer.WriteLineAsync("dropped".AsMemory());
        };

        await write.Should().NotThrowAsync();
        copy.Failure.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Writer_KeepsTheFirstFailure()
    {
        var stream = new FailingStream();
        await using var copy = new RawOutputCopy(stream);

        await copy.Writer.WriteLineAsync("a".AsMemory());
        stream.Reason = "second failure";
        await copy.Writer.WriteLineAsync("b".AsMemory());
        await copy.Writer.FlushAsync(CancellationToken.None);

        copy.Failure.Should().Be("first failure");
        stream.Writes.Should().Be(1, "writes after the first failure are dropped");
    }

    [Fact]
    public async Task DisposeAsync_RecordsAFailureToClose()
    {
        // A full disk can refuse the bytes a buffered stream still holds only when it is closed.
        var stream = new FailingStream { FailOnlyOnFlush = true };
        var copy = new RawOutputCopy(stream);
        await copy.Writer.WriteLineAsync("a".AsMemory());

        await copy.DisposeAsync();

        copy.Failure.Should().Be("first failure");
    }

    [Fact]
    public async Task Writer_DropsWritesAfterDispose()
    {
        var path = Path.Combine(_tempDir, "raw.log");
        var copy = _sut.Open(path);
        await copy.Writer.WriteLineAsync("kept".AsMemory());
        await copy.DisposeAsync();

        await copy.Writer.WriteLineAsync("late".AsMemory());
        await copy.Writer.FlushAsync(CancellationToken.None);
        await copy.DisposeAsync();

        copy.Failure.Should().BeNull("a write after the run ended is not a failure to keep its output");
        (await File.ReadAllTextAsync(path)).Should().Be("kept\n");
    }

    /// <summary>A stream whose writes, or only its flushes, fail with <see cref="Reason"/>.</summary>
    private sealed class FailingStream : Stream
    {
        public string Reason { get; set; } = "first failure";

        public bool FailOnlyOnFlush { get; init; }

        public int Writes { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new IOException(Reason);

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException(Reason));

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            if (!FailOnlyOnFlush)
            {
                throw new IOException(Reason);
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            return FailOnlyOnFlush ? ValueTask.CompletedTask : ValueTask.FromException(new IOException(Reason));
        }
    }
}

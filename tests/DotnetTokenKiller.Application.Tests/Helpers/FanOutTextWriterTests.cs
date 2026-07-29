using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class FanOutTextWriterTests
{
    [Fact]
    public async Task WriteLineAsync_ReachesBothWriters()
    {
        var primary = new StringWriter { NewLine = "\n" };
        var secondary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, secondary);

        await sut.WriteLineAsync("line".AsMemory(), CancellationToken.None);

        primary.ToString().Should().Be("line\n");
        secondary.ToString().Should().Be("line\n");
    }

    [Fact]
    public async Task WriteLineAsync_StillReachesTheTerminal_WhenTheSecondaryThrows()
    {
        // The secondary is the tee. A failing log must never cost the user their output.
        var primary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, new ThrowingWriter());

        var act = async () => await sut.WriteLineAsync("line".AsMemory(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        primary.ToString().Should().Be("line\n");
    }

    [Fact]
    public async Task WriteLineAsync_PropagatesOperationCanceledException_FromSecondary()
    {
        // Cancellation reflects the caller's own request to stop, not a tee failure, and must not be swallowed.
        var primary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, new CancelingWriter());

        var act = async () => await sut.WriteLineAsync("line".AsMemory(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FlushAsync_ReachesBothWriters()
    {
        var primary = new RecordingWriter();
        var secondary = new RecordingWriter();
        var sut = new FanOutTextWriter(primary, secondary);

        await sut.FlushAsync(CancellationToken.None);

        primary.FlushCalled.Should().BeTrue();
        secondary.FlushCalled.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_StillFlushesThePrimary_WhenTheSecondaryThrows()
    {
        // The secondary is the tee. A failing log must never cost the user their output.
        var primary = new RecordingWriter();
        var sut = new FanOutTextWriter(primary, new ThrowingWriter());

        var act = async () => await sut.FlushAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        primary.FlushCalled.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_PropagatesOperationCanceledException_FromSecondary()
    {
        // Cancellation reflects the caller's own request to stop, not a tee failure, and must not be swallowed.
        var primary = new RecordingWriter();
        var sut = new FanOutTextWriter(primary, new CancelingWriter());

        var act = async () => await sut.FlushAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");

        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("disk full");

        public override void Write(char value) => throw new IOException("disk full");
    }

    private sealed class CancelingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            throw new OperationCanceledException();

        public override void Write(char value) => throw new OperationCanceledException();
    }

    /// <summary>A writer that records whether it was flushed, so a dropped fan-out flush is observable.</summary>
    private sealed class RecordingWriter : TextWriter
    {
        /// <summary>Gets a value indicating whether <see cref="FlushAsync(CancellationToken)"/> was called.</summary>
        public bool FlushCalled { get; private set; }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCalled = true;
            return Task.CompletedTask;
        }

        public override void Write(char value)
        {
            // Not exercised by these tests.
        }
    }
}

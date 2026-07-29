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
    public async Task FlushAsync_ReachesBothWriters()
    {
        var primary = new StringWriter { NewLine = "\n" };
        var secondary = new StringWriter { NewLine = "\n" };
        var sut = new FanOutTextWriter(primary, secondary);

        var act = async () => await sut.FlushAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");

        public override void Write(char value) => throw new IOException("disk full");
    }
}

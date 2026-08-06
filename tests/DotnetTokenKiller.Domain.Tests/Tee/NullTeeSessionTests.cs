using DotnetTokenKiller.Domain.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests.Tee;

public sealed class NullTeeSessionTests
{
    [Fact]
    public void Writer_DiscardsEverythingWrittenToIt()
    {
        // The whole point of the null session is that call sites never branch on "is tee on?", so
        // its writer must accept writes silently rather than throw or need a null check.
        var writer = NullTeeSession.Instance.Writer;

        var act = () => writer.WriteLine("output that goes nowhere");

        act.Should().NotThrow();
        writer.Should().BeSameAs(TextWriter.Null);
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsNoPath_BecauseNoFileWasEverWritten()
    {
        (await NullTeeSession.Instance.FinalizeAsync(0)).Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_IsSafeToCallRepeatedly()
    {
        // Instance is shared, so disposal must be a no-op rather than tearing down shared state.
        await NullTeeSession.Instance.DisposeAsync();
        await NullTeeSession.Instance.DisposeAsync();

        NullTeeSession.Instance.Writer.Should().BeSameAs(TextWriter.Null);
    }
}

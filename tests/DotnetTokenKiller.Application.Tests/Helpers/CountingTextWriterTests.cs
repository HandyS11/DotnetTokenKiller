using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class CountingTextWriterTests
{
    [Fact]
    public async Task WriteLineAsync_ForwardsToTheInnerWriterAndFeedsTheCounterWithItsNewLine()
    {
        var seen = new List<string>();
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, int.MaxValue, (text, _) =>
        {
            seen.Add(text);
            return 0;
        });
        var inner = new StringWriter { NewLine = "\n" };
        await using var sut = new CountingTextWriter(inner, counter);

        await sut.WriteLineAsync("first".AsMemory(), CancellationToken.None);
        await sut.FlushAsync(CancellationToken.None);
        await sut.WriteLineAsync("second".AsMemory(), CancellationToken.None);
        counter.Finish("err");
        await counter.TotalAsync();

        inner.ToString().Should().Be("first\nsecond\n");
        seen.Should().Equal("first\nsecond\nerr");
        sut.NewLine.Should().Be("\n");
    }
}

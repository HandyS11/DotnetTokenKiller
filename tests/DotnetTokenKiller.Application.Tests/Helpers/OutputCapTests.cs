using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class OutputCapTests
{
    private static string Body(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"line {i}\n")) + "✓ status\n";

    [Fact]
    public void Truncate_KeepsHeadAndStatusLine()
    {
        var result = OutputCap.Truncate(Body(10), 3, "dtk log");

        result.Should().Be("line 1\nline 2\nline 3\n… 7 more lines (dtk log)\n✓ status\n");
    }

    [Fact]
    public void Truncate_Singular()
    {
        var result = OutputCap.Truncate(Body(4), 3, "dtk log");

        result.Should().Be("line 1\nline 2\nline 3\n… 1 more line (dtk log)\n✓ status\n");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(6)]
    public void Truncate_Unchanged_WhenZeroOrBodyFits(int maxLines)
    {
        var text = Body(5);

        OutputCap.Truncate(text, maxLines, "dtk log").Should().Be(text);
    }

    [Fact]
    public void Truncate_CrlfInput_YieldsLfOutput()
    {
        var result = OutputCap.Truncate(Body(10).ReplaceLineEndings("\r\n"), 3, "dtk log");

        result.Should().NotContain("\r");
        result.Should().Be("line 1\nline 2\nline 3\n… 7 more lines (dtk log)\n✓ status\n");
    }

    [Fact]
    public void Truncate_NoTrailingNewline_StillKeepsLastLine()
    {
        var result = OutputCap.Truncate("a\nb\nc\nstatus", 1, "x");

        result.Should().Be("a\n… 2 more lines (x)\nstatus\n");
    }
}

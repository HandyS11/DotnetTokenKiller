using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class RawTailTests
{
    [Fact]
    public void Last_KeepsTheLastLines()
    {
        RawTail.Last("1\n2\n3\n", lines: 2).Should().Be("2\n3\n");
    }

    [Fact]
    public void Last_DefaultsToFortyLines()
    {
        var text = string.Concat(Enumerable.Range(1, 45).Select(i => $"line {i}\n"));

        RawTail.Last(text).Should().Be(string.Concat(Enumerable.Range(6, 40).Select(i => $"line {i}\n")));
    }

    [Fact]
    public void Last_DropsBlankLines_TerminatesTheLastOne()
    {
        RawTail.Last("a\n\n  b\nc").Should().Be("a\n  b\nc\n");
    }

    [Fact]
    public void Last_CrlfInput_MatchesLf()
    {
        const string lf = "a\n\nb\n\nc\n";

        RawTail.Last(lf.ReplaceLineEndings("\r\n"), lines: 2).Should().Be(RawTail.Last(lf, lines: 2)).And.Be("b\nc\n");
    }

    [Fact]
    public void Last_Empty()
    {
        RawTail.Last(string.Empty).Should().BeEmpty();
    }
}

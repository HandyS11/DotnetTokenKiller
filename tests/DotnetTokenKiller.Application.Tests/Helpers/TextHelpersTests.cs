using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class TextHelpersTests
{
    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello world", 5, "hello...")]
    [InlineData("hello", 5, "hello")]
    public void Truncate_ReturnsExpected(string text, int maxLen, string expected)
    {
        TextHelpers.Truncate(text, maxLen).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(850, "850")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(1200, "1.2K")]
    [InlineData(999_999, "1000.0K")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(3_500_000, "3.5M")]
    public void FormatTokens_ReturnsExpected(int count, string expected)
    {
        TextHelpers.FormatTokens(count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1200, "1.2K")]
    [InlineData(3_500_000, "3.5M")]
    public void FormatTokens_FrenchCulture_NeverUsesADecimalComma(int count, string expected)
    {
        using var _ = new CultureScope("fr-FR");

        TextHelpers.FormatTokens(count).Should().Be(expected);
    }

    [Fact]
    public void ShortenPath_AbsolutePath_ReturnsRelativeWithForwardSlashes()
    {
        const string root = "/home/user/project";
        const string abs = "/home/user/project/src/Foo/Bar.cs";
        TextHelpers.ShortenPath(abs, root).Should().Be("src/Foo/Bar.cs");
    }

    [Fact]
    public void ShortenPath_EmptyInput_ReturnsEmpty()
    {
        TextHelpers.ShortenPath(string.Empty, "/root").Should().Be(string.Empty);
    }

    [InlineData("", 5, "")]
    [Theory]
    public void Truncate_EmptyText_ReturnsEmpty(string text, int maxLen, string expected)
    {
        TextHelpers.Truncate(text, maxLen).Should().Be(expected);
    }

    [Fact]
    public void ShortenPath_NullRoot_FallsBackToFileName()
    {
        TextHelpers.ShortenPath("/some/path/File.cs", null!).Should().Be("File.cs");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData("0042", 42)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("2147483648", int.MaxValue)]
    [InlineData("99999999999999999999", int.MaxValue)]
    public void ParseNumberOrMax_ReadsDigitsAndSortsAnOverflowLast(string digits, int expected)
    {
        TextHelpers.ParseNumberOrMax(digits).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "p1")]
    [InlineData(20, "p1 p2 p3 p4 p5 p6 p7 p8 p9 p10 p11 p12 p13 p14 p15 p16 p17 p18 p19 p20")]
    [InlineData(21, "p1 p2 p3 p4 p5 p6 p7 p8 p9 p10 p11 p12 p13 p14 p15 p16 p17 p18 p19 p20 \u2026 +1 more")]
    [InlineData(3000, "p1 p2 p3 p4 p5 p6 p7 p8 p9 p10 p11 p12 p13 p14 p15 p16 p17 p18 p19 p20 \u2026 +2980 more")]
    public void JoinCapped_ListsTwentyThenCountsTheRest(int count, string expected)
    {
        var items = Enumerable.Range(1, count).Select(i => $"p{i}").ToList();

        TextHelpers.JoinCapped(items, " ").Should().Be(expected);
    }

    [Fact]
    public void JoinCapped_KeepsTheSeparatorAndNeverAddsOneBeforeTheCount()
    {
        var items = Enumerable.Range(1, 22).Select(i => $"{i}:1").ToList();

        TextHelpers.JoinCapped(items, ", ").Should().EndWith("19:1, 20:1 \u2026 +2 more");
    }

    [Fact]
    public void JoinCapped_FrenchCulture_NeverUsesAGroupSeparator()
    {
        using var _ = new CultureScope("fr-FR");
        var items = Enumerable.Range(1, 1020).Select(i => $"p{i}").ToList();

        TextHelpers.JoinCapped(items, " ").Should().EndWith(" \u2026 +1000 more");
    }
}

using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

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
}

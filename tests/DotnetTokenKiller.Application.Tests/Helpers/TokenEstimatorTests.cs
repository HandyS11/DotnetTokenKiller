using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello", 1)]
    [InlineData("Hello world", 2)]
    public void Estimate_ReturnsActualTokenCount(string text, int expected)
    {
        TokenEstimator.Estimate(text).Should().Be(expected);
    }

    [Fact]
    public void Estimate_NullInput_ReturnsZero()
    {
        TokenEstimator.Estimate(null!).Should().Be(0);
    }

    [Fact]
    public void Estimate_LongerTextHasMoreTokens()
    {
        const string shortText = "Hello world";
        var longText = string.Join(" ", Enumerable.Repeat("Hello world", 100));

        TokenEstimator.Estimate(longText).Should().BeGreaterThan(TokenEstimator.Estimate(shortText));
    }
}

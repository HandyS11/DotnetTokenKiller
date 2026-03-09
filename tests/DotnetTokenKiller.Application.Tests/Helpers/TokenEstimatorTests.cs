using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("1234", 1)]
    [InlineData("12345678", 2)]
    [InlineData("123456789012", 3)]
    public void Estimate_ReturnsLengthDividedByFour(string text, int expected)
    {
        TokenEstimator.Estimate(text).Should().Be(expected);
    }

    [Fact]
    public void Estimate_NullInput_ReturnsZero()
    {
        TokenEstimator.Estimate(null!).Should().Be(0);
    }

    [Fact]
    public void Estimate_LargeText_ReturnsCorrectCount()
    {
        var text = new string('x', 4000);
        TokenEstimator.Estimate(text).Should().Be(1000);
    }
}

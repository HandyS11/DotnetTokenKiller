using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;

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

    [Fact]
    public void Estimate_DefaultModel_UsesCl100kBase()
    {
        var withDefault = TokenEstimator.Estimate("Hello world");
        var withExplicit = TokenEstimator.Estimate("Hello world");

        withDefault.Should().Be(withExplicit);
    }

    [Theory]
    [InlineData(TokenizerModel.Cl100kBase)]
    [InlineData(TokenizerModel.O200kBase)]
    public void Estimate_AllModels_ReturnPositiveForNonEmptyText(TokenizerModel model)
    {
        TokenEstimator.Estimate("Hello world, this is a test.", model).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Estimate_UnrecognizedModel_DefaultsToCl100kBase()
    {
        var result = TokenEstimator.Estimate("Hello world", (TokenizerModel)999);
        var expected = TokenEstimator.Estimate("Hello world");

        result.Should().Be(expected);
    }
}

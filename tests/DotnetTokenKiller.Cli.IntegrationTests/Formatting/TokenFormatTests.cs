using DotnetTokenKiller.Cli.Formatting;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Formatting;

public class TokenFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(88_400, "88.4K")]
    [InlineData(995_397, "995.4K")]
    [InlineData(999_999, "1.0M")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(2_600_000, "2.6M")]
    [InlineData(-500, "-500")]
    [InlineData(-1030, "-1.0K")]
    [InlineData(-2_600_000, "-2.6M")]
    public void Tokens_FormatsWithKAndMUnits(long value, string expected)
    {
        TokenFormat.Tokens(value).Should().Be(expected);
    }

    public static TheoryData<TimeSpan, string> DurationCases => new()
    {
        { TimeSpan.Zero, "0ms" },
        { TimeSpan.FromMilliseconds(204), "204ms" },
        { TimeSpan.FromSeconds(2.3), "2.3s" },
        { TimeSpan.FromSeconds(59), "59.0s" },
        { TimeSpan.FromSeconds((38 * 60) + 12), "38m12s" },
        { TimeSpan.FromMinutes(60), "1h00m" },
        { TimeSpan.FromMinutes(125), "2h05m" }
    };

    [Theory]
    [MemberData(nameof(DurationCases))]
    public void Duration_FormatsCompactly(TimeSpan value, string expected)
    {
        TokenFormat.Duration(value).Should().Be(expected);
    }
}

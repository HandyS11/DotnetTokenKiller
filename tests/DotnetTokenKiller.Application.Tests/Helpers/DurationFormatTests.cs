using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class DurationFormatTests
{
    [Theory]
    [InlineData(0, true, "0.00s")]
    [InlineData(10, true, "0.01s")]
    [InlineData(340, true, "0.34s")]
    [InlineData(999.6, true, "1.00s")]
    [InlineData(1000, true, "1.00s")]
    [InlineData(2340, true, "2.34s")]
    [InlineData(42_000, true, "42.00s")]
    public void Format_ExactUnderAMinute_KeepsTwoDecimals(double milliseconds, bool exact, string expected)
    {
        DurationFormat.Format(milliseconds, exact).Should().Be(expected);
    }

    [Theory]
    [InlineData(340, "0.34s")]
    [InlineData(500, "0.50s")]
    public void Format_NotExactUnderASecond_StillKeepsTwoDecimals(double milliseconds, string expected)
    {
        DurationFormat.Format(milliseconds, exact: false).Should().Be(expected);
    }

    [Theory]
    [InlineData(1000, "1s")]
    [InlineData(2000, "2s")]
    [InlineData(12_000, "12s")]
    [InlineData(12_400, "12s")]
    [InlineData(12_500, "13s")]
    [InlineData(59_400, "59s")]
    public void Format_NotExactUnderAMinute_ShowsWholeSeconds(double milliseconds, string expected)
    {
        DurationFormat.Format(milliseconds, exact: false).Should().Be(expected);
    }

    [Theory]
    [InlineData(60_000, true, "1m00s")]
    [InlineData(62_500, true, "1m03s")]
    [InlineData(100_000, false, "1m40s")]
    [InlineData(100_000, true, "1m40s")]
    [InlineData(3_540_000, false, "59m00s")]
    public void Format_AMinuteOrMore_ShowsMinutesAndRoundedSeconds(double milliseconds, bool exact, string expected)
    {
        DurationFormat.Format(milliseconds, exact).Should().Be(expected);
    }

    [Theory]
    [InlineData(3_600_000, false, "1h00m00s")]
    [InlineData(3_723_000, true, "1h02m03s")]
    [InlineData(3_723_400, false, "1h02m03s")]
    [InlineData(90_000_000, true, "25h00m00s")]
    public void Format_AnHourOrMore_ShowsHoursMinutesAndSeconds(double milliseconds, bool exact, string expected)
    {
        DurationFormat.Format(milliseconds, exact).Should().Be(expected);
    }

    [Theory]
    [InlineData(59_994, true, "59.99s")]
    [InlineData(59_996, true, "1m00s")]
    [InlineData(59_499, false, "59s")]
    [InlineData(59_500, false, "1m00s")]
    [InlineData(3_599_499, true, "59m59s")]
    [InlineData(3_599_500, true, "1h00m00s")]
    public void Format_RoundingUpToTheNextUnit_PromotesInsteadOfShowingSixty(
        double milliseconds, bool exact, string expected)
    {
        DurationFormat.Format(milliseconds, exact).Should().Be(expected);
    }

    [Theory]
    [InlineData("89", "ms", true)]
    [InlineData("89", "MS", true)]
    [InlineData("2.5", "s", true)]
    [InlineData("1.5", "sec", true)]
    [InlineData("1", "s", false)]
    [InlineData("2", "sec", false)]
    [InlineData("1", "m", false)]
    [InlineData("1", "min", false)]
    [InlineData("1", "h", false)]
    public void IsExactReading_IsTrueOnlyForMillisecondsOrADecimalPoint(string number, string unit, bool expected)
    {
        DurationFormat.IsExactReading(number, unit).Should().Be(expected);
    }

    [Theory]
    [InlineData(340, true, "0.34s")]
    [InlineData(2340, true, "2.34s")]
    [InlineData(12_000, false, "12s")]
    [InlineData(100_000, false, "1m40s")]
    public void Format_FrenchCulture_NeverUsesADecimalComma(double milliseconds, bool exact, string expected)
    {
        using var _ = new CultureScope("fr-FR");

        DurationFormat.Format(milliseconds, exact).Should().Be(expected);
    }
}

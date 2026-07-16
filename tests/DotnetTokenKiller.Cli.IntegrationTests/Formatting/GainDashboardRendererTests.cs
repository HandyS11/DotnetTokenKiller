using DotnetTokenKiller.Cli.Formatting;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Formatting;

public class GainDashboardRendererTests
{
    private static GainSummary MakeSummary(
        double avgPct = 84.0,
        long totalSaved = 2100,
        TimeSpan? execTime = null)
    {
        var successDetail = new CommandGainDetail(3, 1500, 250, 1250, 83.3,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1500));
        var failureDetail = new CommandGainDetail(2, 1000, 150, 850, 35.0,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1000));
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(5, 2500, 400, totalSaved, avgPct, successDetail, failureDetail,
                TimeSpan.FromMilliseconds(2500))
        };
        return new GainSummary(5, 2500, 400, totalSaved, avgPct, details,
            execTime ?? TimeSpan.FromMilliseconds(2500));
    }

    private static TestConsole Render(GainSummary summary, string scope = "Global Scope")
    {
        var console = new TestConsole();
        GainDashboardRenderer.Render(console, summary, scope);
        return console;
    }

    [Fact]
    public void Render_WritesTitleWithScope()
    {
        var console = Render(MakeSummary(), "Project Scope, last 7 days");

        console.Output.Should().Contain("DTK Token Savings (Project Scope, last 7 days)");
        console.Output.Should().Contain("════");
    }

    [Fact]
    public void Render_WritesRecapBlockWithHumanUnits()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("Total commands:    5");
        console.Output.Should().Contain("Without tool:      2.5K");
        console.Output.Should().Contain("Used by tool:      400");
        console.Output.Should().Contain("Tokens saved:      2.1K (84.0%)");
        console.Output.Should().Contain("Total exec time:   2.5s (avg 500ms)");
    }

    [Fact]
    public void Render_EfficiencyMeter_FillsProportionally()
    {
        // 50% of a 24-char meter = 12 filled blocks.
        var console = Render(MakeSummary(avgPct: 50.0));

        console.Output.Should().Contain("Efficiency meter: " + new string('█', 12) + new string('░', 12) + " 50.0%");
    }

    [Fact]
    public void Render_EfficiencyMeter_ClampsOutOfRangePercentage()
    {
        var console = Render(MakeSummary(avgPct: 150.0));

        console.Output.Should().Contain(new string('█', 24) + " 150.0%");
    }

    [Fact]
    public void Render_Table_ShowsSplitRowsWithImpactBars()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("By Command");

        // Assert per line: the efficiency meter also contains long block runs, so a
        // whole-output Contain() would match the meter instead of the table bars.
        var okLine = console.Lines.Single(l => l.Contains("build (ok)", StringComparison.Ordinal));
        var failLine = console.Lines.Single(l => l.Contains("build (fail)", StringComparison.Ordinal));

        // ok row saved 1250 = max -> full 10-char bar; fail row 850/1250 -> 7 filled.
        okLine.Should().Contain(new string('█', 10));
        failLine.Should().Contain(new string('█', 7) + new string('░', 3));
    }

    [Fact]
    public void Render_Table_FormatsRowValuesWithUnits()
    {
        var console = Render(MakeSummary());

        console.Output.Should().Contain("1.5K"); // ok row Without Tool
        console.Output.Should().Contain("83.3%"); // ok row Avg%
        console.Output.Should().Contain("35.0%"); // fail row Avg%
    }

    [Fact]
    public void Render_Table_NegativeSavings_EmptyImpactBar()
    {
        var detail = new CommandGainDetail(2, 0, 1030, -1030, 0.0,
            TotalExecutionTime: TimeSpan.FromMilliseconds(200));
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["format"] = new(2, 0, 1030, -1030, 0.0, detail, null, TimeSpan.FromMilliseconds(200))
        };
        var summary = new GainSummary(2, 0, 1030, -1030, 0.0, details, TimeSpan.FromMilliseconds(200));

        var console = Render(summary);

        console.Output.Should().Contain("-1.0K");
        // Per line: the 0% efficiency meter is also a long run of '░'.
        var row = console.Lines.Single(l => l.Contains("format (ok)", StringComparison.Ordinal));
        row.Should().Contain(new string('░', 10)); // no fill for negative savings
        row.Should().NotContain("█");
    }

    [Fact]
    public void Render_Table_ImpactBar_MinimumOneBlockForSmallPositiveSavings()
    {
        // Same shape as MakeSummary's ok row (which is known to fit on one line without the
        // Command column wrapping); only the fail row's saved total drops from 850 to 1 so it's
        // the smallest possible positive value relative to the max (1250).
        var successDetail = new CommandGainDetail(3, 1500, 250, 1250, 83.3,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1500));
        var failureDetail = new CommandGainDetail(2, 1000, 150, 1, 35.0,
            TotalExecutionTime: TimeSpan.FromMilliseconds(1000));
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(5, 2500, 400, 1251, 91.0, successDetail, failureDetail,
                TimeSpan.FromMilliseconds(2500))
        };
        var summary = new GainSummary(5, 2500, 400, 1251, 91.0, details, TimeSpan.FromMilliseconds(2500));

        var console = Render(summary);

        var okLine = console.Lines.Single(l => l.Contains("build (ok)", StringComparison.Ordinal));
        var failLine = console.Lines.Single(l => l.Contains("build (fail)", StringComparison.Ordinal));

        // ok row saved 1250 = max -> full 10-block bar; fail row saved 1 -> minimum one block.
        okLine.Should().Contain(new string('█', 10));
        failLine.Should().Contain("█" + new string('░', 9));
        failLine.Should().NotContain(new string('█', 2));
    }

    [Theory]
    [InlineData(85.0, "green")]
    [InlineData(80.0, "green")]
    [InlineData(79.9, "yellow")]
    [InlineData(50.0, "yellow")]
    [InlineData(40.0, "yellow")]
    [InlineData(39.9, "red")]
    [InlineData(10.0, "red")]
    public void PctColor_MapsPercentageToColorBucket(double pct, string expectedColor)
    {
        GainDashboardRenderer.PctColor(pct).Should().Be(expectedColor);
    }

    [Theory]
    [InlineData(1250, "green")]
    [InlineData(-1030, "red")]
    [InlineData(0, "grey")]
    public void SavedColor_MapsSignToColor(long saved, string expectedColor)
    {
        GainDashboardRenderer.SavedColor(saved).Should().Be(expectedColor);
    }
}

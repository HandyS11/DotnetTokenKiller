using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class RunOutcomeTests
{
    [Theory]
    [InlineData(RunOutcome.Filtered)]
    [InlineData(RunOutcome.RawTailFallback)]
    [InlineData(RunOutcome.FilterFaulted)]
    public void CountedInSavings_ContainsEveryOutcomeWhereAFilterRan(RunOutcome outcome)
    {
        RunOutcomes.CountedInSavings.Should().Contain(outcome);
        RunOutcomes.IsPassthrough(outcome).Should().BeFalse();
    }

    [Theory]
    [InlineData(RunOutcome.PassthroughMeasured)]
    [InlineData(RunOutcome.PassthroughUnmeasured)]
    public void CountedInSavings_ExcludesEveryOutcomeWhereNoFilterRan(RunOutcome outcome)
    {
        RunOutcomes.CountedInSavings.Should().NotContain(outcome);
        RunOutcomes.IsPassthrough(outcome).Should().BeTrue();
    }

    [Fact]
    public void CountedInSavings_PartitionsEveryDeclaredOutcome()
    {
        // A new outcome added to the enum without a deliberate decision about whether it counts
        // toward savings would silently land on the passthrough side. Force the choice.
        var all = Enum.GetValues<RunOutcome>();

        all.Should().HaveCount(5);
        all.Count(RunOutcomes.IsPassthrough).Should().Be(2);
    }

    [Fact]
    public void CommandRecord_DefaultsToFiltered_WhenOutcomeNotSupplied()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow,
            "build",
            "/proj",
            new TokenStatistics(100, 10, 90, 90.0),
            TimeSpan.FromMilliseconds(5));

        record.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public void CommandRecord_RoundTripsSuppliedOutcome()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow,
            "publish",
            "/proj",
            new TokenStatistics(100, 100, 0, 0.0),
            TimeSpan.FromMilliseconds(5))
        {
            Success = true,
            Outcome = RunOutcome.PassthroughMeasured
        };

        record.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }
}

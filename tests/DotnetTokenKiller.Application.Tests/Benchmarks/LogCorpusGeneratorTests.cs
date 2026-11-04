using System.Text;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class LogCorpusGeneratorTests
{
    public static TheoryData<string> FilterKeysUnderTest() => new(
        FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore,
        FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage);

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_IsDeterministicForAGivenSeed(string filterKey)
    {
        var first = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);
        var second = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);

        // Byte-identical, not merely equal in length: a benchmark whose input shifts between runs
        // reports noise as change, and a savings number computed from it means nothing.
        second.Should().Be(first);
    }

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_LandsWithinTenPercentOfTheTierTarget(string filterKey)
    {
        foreach (var tier in Enum.GetValues<CorpusTier>())
        {
            var target = LogCorpusGenerator.TargetBytes(tier);
            var actual = Encoding.UTF8.GetByteCount(LogCorpusGenerator.Generate(filterKey, tier));

            actual.Should().BeInRange((int)(target * 0.9), (int)(target * 1.1),
                "{0}/{1} should be near its {2}-byte target", filterKey, tier, target);
        }
    }

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_ProducesOutputItsFilterMeaningfullyCondenses(string filterKey)
    {
        var raw = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);

        var filtered = SavingsScenarios.FilterFor(filterKey).Apply(raw, exitCode: 0);

        // The load-bearing assertion of this task. A generator that drifted into line shapes no
        // filter recognises would still be deterministic and still hit its size target, while
        // making every throughput number meaningless. Requiring real compression pins the
        // generated logs to shapes the filters actually parse.
        filtered.Should().NotBeNullOrWhiteSpace();
        var maxShare = MaxFilteredShare(filterKey);
        Encoding.UTF8.GetByteCount(filtered).Should().BeLessThan(
            (int)(Encoding.UTF8.GetByteCount(raw) * maxShare),
            "{0} should condense its generated log to under {1:P0} of its size", filterKey, maxShare);
    }

    /// <summary>
    /// The share of its raw log a filter's output must stay under. Half, except for <c>build</c>: the generator
    /// prints each warning once, as one line that is mostly the diagnostic itself, and numbers its files at
    /// random, so nearly every warning sits in a file of its own and reads as one line there. All the filter
    /// can drop is the root, the word "warning" and the project suffix, which leaves it near 52%. A real log
    /// prints every diagnostic twice and lets many share a file, a code and a message.
    /// </summary>
    /// <param name="filterKey">The filter whose generated log is judged.</param>
    private static double MaxFilteredShare(string filterKey) => filterKey == FilterKeys.Build ? 2.0 / 3.0 : 0.5;
}

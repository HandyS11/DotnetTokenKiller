using System.Text;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Benchmarks.Corpus;
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

        var filtered = FilterFor(filterKey).Apply(raw, exitCode: 0);

        // The load-bearing assertion of this task. A generator that drifted into line shapes no
        // filter recognises would still be deterministic and still hit its size target, while
        // making every throughput number meaningless. Requiring real compression pins the
        // generated logs to shapes the filters actually parse.
        filtered.Should().NotBeNullOrWhiteSpace();
        Encoding.UTF8.GetByteCount(filtered).Should().BeLessThan(
            Encoding.UTF8.GetByteCount(raw) / 2,
            "{0} should condense its generated log by at least half", filterKey);
    }

    private static IOutputFilter FilterFor(string filterKey) => filterKey switch
    {
        FilterKeys.Build => new DotnetBuildFilter("/repo"),
        FilterKeys.Test => new DotnetTestFilter("/repo"),
        FilterKeys.Restore => new DotnetRestoreFilter("/repo"),
        FilterKeys.Clean => new DotnetCleanFilter("/repo"),
        FilterKeys.Format => new DotnetFormatFilter("/repo"),
        FilterKeys.ListPackage => new DotnetListPackageFilter(),
        _ => throw new ArgumentOutOfRangeException(nameof(filterKey), filterKey, "No filter."),
    };
}

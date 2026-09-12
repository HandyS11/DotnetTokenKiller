using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Filtering cost per filter across the three size tiers. Read this as a curve, not as three
/// numbers: a filter whose cost grows faster than its input is the defect worth catching, and only
/// the ratio between tiers shows it.
/// </summary>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    private string _raw = string.Empty;
    private IOutputFilter _filter = new DotnetBuildFilter(SavingsScenarios.PinnedRoot);

    [Params(FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore,
        FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage)]
    public string FilterKey { get; set; } = FilterKeys.Build;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        _raw = LogCorpusGenerator.Generate(FilterKey, Tier);
        _filter = SavingsScenarios.FilterFor(FilterKey);
    }

    [Benchmark]
    public string Apply() => _filter.Apply(_raw, exitCode: 0);
}

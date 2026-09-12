using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Configuration;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// The combined per-invocation cost: ANSI strip, filter, both token counts, glyph normalisation and
/// the write. This is the closest in-process figure to what a user pays for one wrapped command.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private HermeticState? _state;
    private FilteredOutputPipeline? _pipeline;
    private FilteredOutputRequest? _request;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        // Deliberately no config file: HermeticState only points DTK_CONFIG_PATH at an empty temp
        // directory, so JsonConfigProvider returns DtkConfig.Default without a read or a parse.
        // That is the fresh-install case, which is what most invocations of a new install are, and
        // it keeps this number about the pipeline rather than about config size. StartupBenchmarks
        // writes a populated config and measures the parse on its own.
        _state = HermeticState.Enter();

        var raw = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);

        _pipeline = new FilteredOutputPipeline(
            new NullTracker(),
            TextWriter.Null,
            new JsonConfigProvider(_state.ConfigPath));

        _request = new FilteredOutputRequest(
            SavingsScenarios.FilterFor(FilterKeys.Build),
            raw,
            ExitCode: 0,
            CommandSlug: FilterKeys.Build,
            DisplayCommandLine: "dotnet build",
            Source: RunSource.Run,
            Options: new OutputOptions(),
            StartTimestamp: Stopwatch.GetTimestamp());
    }

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    [Benchmark]
    public async Task<int> ProcessAsync() =>
        await _pipeline!.ProcessAsync(_request!, NullTeeSession.Instance).ConfigureAwait(false);
}

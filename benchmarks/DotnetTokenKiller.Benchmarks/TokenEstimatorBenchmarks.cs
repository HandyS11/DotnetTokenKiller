using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Token counting cost. Every dtk invocation counts the full raw output and the filtered output,
/// purely to record a savings statistic, so this runs twice per command over the raw log's full
/// length. <see cref="EstimateRawThenFiltered"/> measures that pair as the pipeline actually pays
/// for it.
/// </summary>
[MemoryDiagnoser]
public class TokenEstimatorBenchmarks
{
    private string _raw = string.Empty;
    private string _filtered = string.Empty;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [Params(TokenizerModel.Cl100kBase, TokenizerModel.O200kBase)]
    public TokenizerModel Tokenizer { get; set; } = TokenizerModel.Cl100kBase;

    [GlobalSetup]
    public void Setup()
    {
        _raw = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);
        _filtered = SavingsScenarios.FilterFor(FilterKeys.Build).Apply(_raw, exitCode: 0);

        // Warm the tokenizer cache so the one-time vocabulary load is not charged to the first
        // iteration: the benchmarks below are about steady-state tokenization, which is what a
        // process pays on every call after the first. The load itself cannot be measured in
        // process at all — Microsoft.ML.Tokenizers caches the parsed vocabulary in internal
        // static state that no [IterationSetup] can clear, so BenchmarkDotNet's own warmup
        // invocation would populate it and every measured iteration would be a cache hit. The
        // `tokenizer-load` verb measures it honestly instead, one fresh process per sample.
        TokenEstimator.Estimate("warmup", Tokenizer);
    }

    [Benchmark]
    public int EstimateRaw() => TokenEstimator.Estimate(_raw, Tokenizer);

    [Benchmark]
    public int EstimateFiltered() => TokenEstimator.Estimate(_filtered, Tokenizer);

    /// <summary>Both counts, as one dtk invocation pays for them.</summary>
    [Benchmark]
    public int EstimateRawThenFiltered() =>
        TokenEstimator.Estimate(_raw, Tokenizer) + TokenEstimator.Estimate(_filtered, Tokenizer);
}

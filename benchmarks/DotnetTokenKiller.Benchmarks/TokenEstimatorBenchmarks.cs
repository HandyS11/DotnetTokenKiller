using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.ML.Tokenizers;

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

        // Warm the tokenizer cache so the vocabulary load is not charged to the first iteration.
        // Cold load is measured separately by TokenizerLoadBenchmarks.
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

/// <summary>
/// The one-time vocabulary load. <see cref="TokenEstimator"/> caches tokenizers in a private
/// static dictionary that cannot be cleared from outside the type, so this constructs the
/// tokenizer directly — the same underlying work the first <c>Estimate</c> call of a process pays
/// for, and a plausible dominant cost for a short log.
/// </summary>
[MemoryDiagnoser]
public class TokenizerLoadBenchmarks
{
    [Params("cl100k_base", "o200k_base")]
    public string Encoding { get; set; } = "cl100k_base";

    [Benchmark]
    public TiktokenTokenizer LoadTokenizer() => TiktokenTokenizer.CreateForEncoding(Encoding);
}

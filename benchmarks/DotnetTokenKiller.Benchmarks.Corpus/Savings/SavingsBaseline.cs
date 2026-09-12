namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>The committed record of what every scenario saves.</summary>
/// <param name="Tokenizer">The tiktoken encoding the counts were produced with.</param>
/// <param name="TokenizerFingerprint">
/// The token count of a fixed probe string. When the tokenizer's vocabulary data changes, every
/// scenario's counts shift at once; this distinguishes that from a filter regression, which is
/// otherwise indistinguishable in the diff.
/// </param>
/// <param name="Scenarios">Per-scenario measurements, ordered by <see cref="ScenarioSavings.Id"/>.</param>
public sealed record SavingsBaseline(
    string Tokenizer,
    int TokenizerFingerprint,
    IReadOnlyList<ScenarioSavings> Scenarios);

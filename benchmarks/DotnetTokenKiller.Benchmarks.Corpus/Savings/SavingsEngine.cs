using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>Measures what filtering removes, for every scenario.</summary>
public static class SavingsEngine
{
    /// <summary>
    /// The encoding the gate is expressed in: what <see cref="TrackingConfig"/> ships as its
    /// default, so the baseline describes what users actually get. o200k is covered on the timing
    /// side instead.
    /// </summary>
    public const TokenizerModel Tokenizer = TokenizerModel.Cl100kBase;

    /// <summary>Name recorded in the baseline for <see cref="Tokenizer"/>.</summary>
    public const string TokenizerName = "cl100k_base";

    /// <summary>
    /// Fixed text whose token count fingerprints the tokenizer's vocabulary. Deliberately varied —
    /// prose, punctuation, digits, a path, a non-ASCII glyph — so a vocabulary change is likely to
    /// move the count.
    /// </summary>
    private const string FingerprintProbe =
        "dtk: build succeeded — 3 warnings, 0 errors (net10.0) ✓\n"
        + "/repo/src/Project/Service.cs(42,17): warning CA1822: Member 'Handle' …\n"
        + "Passed!  - Failed: 0, Passed: 1200, Skipped: 40, Total: 1240, Duration: 8 s\n";

    /// <summary>Returns the current tokenizer fingerprint.</summary>
    public static int TokenizerFingerprint() => TokenEstimator.Estimate(FingerprintProbe, Tokenizer);

    /// <summary>Measures one scenario.</summary>
    /// <param name="scenario">The scenario to measure.</param>
    public static ScenarioSavings Measure(SavingsScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        // Same order as FilteredOutputPipeline.ProcessAsync: strip first, filter the stripped
        // text, and count the stripped text as the "before". Counting the un-stripped raw output
        // instead would credit dtk for removing escape sequences no model ever sees.
        var stripped = AnsiStrip.Strip(FixtureCorpus.Load(scenario.Fixture));
        var filtered = SavingsScenarios.FilterFor(scenario.FilterKey)
            .Apply(stripped, scenario.ExitCode);

        var rawTokens = TokenEstimator.Estimate(stripped, Tokenizer);
        var filteredTokens = TokenEstimator.Estimate(filtered, Tokenizer);
        var saved = rawTokens - filteredTokens;

        return new ScenarioSavings(
            scenario.Id,
            scenario.Fixture,
            scenario.FilterKey,
            scenario.ExitCode,
            Encoding.UTF8.GetByteCount(stripped),
            rawTokens,
            filteredTokens,
            saved,
            rawTokens > 0 ? Math.Round((double)saved / rawTokens * 100.0, 1) : 0.0);
    }

    /// <summary>Measures every scenario, ordered by identifier for a stable baseline diff.</summary>
    public static SavingsBaseline MeasureAll() => new(
        TokenizerName,
        TokenizerFingerprint(),
        [.. SavingsScenarios.All
            .OrderBy(scenario => scenario.Id, StringComparer.Ordinal)
            .Select(Measure)]);
}

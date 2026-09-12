using System.Globalization;
using System.Text;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

/// <summary>
/// Gates the product metric. <c>ExamplesBindingTests</c> asserts that documented output matches
/// what the filters emit; nothing asserted that the filters still <em>compress</em>. A change that
/// added a line of summary to every filter would cost real tokens on every user's every command
/// and leave this suite entirely green. This is the assertion that notices.
/// </summary>
public sealed class SavingsBaselineTests
{
    [Fact]
    public void EveryScenario_MatchesTheCommittedBaseline()
    {
        var baseline = SavingsBaselineFile.Read();
        var actual = SavingsEngine.MeasureAll();

        actual.Scenarios.Select(s => s.Id).Should().Equal(
            baseline.Scenarios.Select(s => s.Id),
            "the baseline must cover exactly the current scenario set — regenerate with: {0}",
            SavingsBaselineFile.RegenerateCommand);

        var drifted = actual.Scenarios
            .Zip(baseline.Scenarios, (now, then) => (Now: now, Then: then))
            .Where(pair => pair.Now.RawTokens != pair.Then.RawTokens
                           || pair.Now.FilteredTokens != pair.Then.FilteredTokens)
            .ToList();

        drifted.Should().BeEmpty(
            "{0}",
            DriftReport(drifted, baseline.TokenizerFingerprint, actual.TokenizerFingerprint));
    }

    [Fact]
    public void Baseline_RecordsTheTokenizerItWasMeasuredWith()
    {
        var baseline = SavingsBaselineFile.Read();

        baseline.Tokenizer.Should().Be(SavingsEngine.TokenizerName);
    }

    /// <summary>
    /// Explains a drift the way a reviewer needs to read it: which scenarios moved, by how much,
    /// and whether the tokenizer or the filters are the likelier cause. Without the fingerprint
    /// line, a dependency bump that changes the vocabulary data looks exactly like a filter
    /// regression.
    /// </summary>
    /// <param name="drifted">The scenarios whose token counts no longer match the baseline.</param>
    /// <param name="baselineFingerprint">The tokenizer fingerprint recorded in the baseline.</param>
    /// <param name="actualFingerprint">The tokenizer fingerprint measured just now.</param>
    private static string DriftReport(
        List<(ScenarioSavings Now, ScenarioSavings Then)> drifted,
        int baselineFingerprint,
        int actualFingerprint)
    {
        if (drifted.Count == 0)
        {
            return "no drift";
        }

        var report = new StringBuilder();
        report.AppendLine("savings drifted from the committed baseline:").AppendLine();

        foreach (var (now, then) in drifted)
        {
            report.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {now.Id,-28} {then.SavingsPercent,6:F1}% -> {now.SavingsPercent,6:F1}% (raw {then.RawTokens}->{now.RawTokens}, filtered {then.FilteredTokens}->{now.FilteredTokens})"));
        }

        report
            .AppendLine()
            .AppendLine(actualFingerprint != baselineFingerprint
                ? $"  The tokenizer changed (fingerprint {baselineFingerprint} -> {actualFingerprint}). "
                  + "Every count shifts when the vocabulary data does; this is expected after a "
                  + "Microsoft.ML.Tokenizers.Data bump, and regenerating is the correct response."
                : $"  The tokenizer is unchanged (fingerprint {actualFingerprint}), so {drifted.Count} "
                  + "scenario(s) moved because filtering changed. If that was intended, regenerate. "
                  + "If not, this is the regression.")
            .AppendLine()
            .AppendLine($"  Regenerate with: {SavingsBaselineFile.RegenerateCommand}");
        return report.ToString();
    }
}

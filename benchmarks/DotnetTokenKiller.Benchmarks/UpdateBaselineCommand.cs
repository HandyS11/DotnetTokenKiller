using System.Globalization;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;

namespace DotnetTokenKiller.Benchmarks;

internal static class UpdateBaselineCommand
{
    internal static int Run()
    {
        var baseline = SavingsEngine.MeasureAll();
        SavingsBaselineFile.Write(baseline);

        Console.WriteLine($"Wrote {SavingsBaselineFile.Path()}");
        Console.WriteLine(
            $"Tokenizer {baseline.Tokenizer} (fingerprint {baseline.TokenizerFingerprint})");
        Console.WriteLine();
        Console.WriteLine($"{"scenario",-28} {"raw",8} {"filtered",9} {"saved %",8}");

        foreach (var scenario in baseline.Scenarios)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario.Id,-28} {scenario.RawTokens,8} {scenario.FilteredTokens,9} {scenario.SavingsPercent,7:F1}%"));
        }

        var totalRaw = baseline.Scenarios.Sum(s => s.RawTokens);
        var totalFiltered = baseline.Scenarios.Sum(s => s.FilteredTokens);
        var overall = totalRaw > 0 ? (double)(totalRaw - totalFiltered) / totalRaw * 100.0 : 0.0;

        Console.WriteLine();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Overall: {totalRaw} -> {totalFiltered} tokens ({overall:F1}% saved)"));

        return 0;
    }
}

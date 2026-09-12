namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>What one scenario costs before and after filtering.</summary>
/// <param name="Id">The scenario's stable identifier.</param>
/// <param name="Fixture">The fixture measured.</param>
/// <param name="FilterKey">The filter applied.</param>
/// <param name="ExitCode">The exit code the filter was given.</param>
/// <param name="RawBytes">UTF-8 byte count of the ANSI-stripped raw output.</param>
/// <param name="RawTokens">Token count of the ANSI-stripped raw output.</param>
/// <param name="FilteredTokens">Token count of the filtered output.</param>
/// <param name="SavedTokens">
/// <paramref name="RawTokens"/> minus <paramref name="FilteredTokens"/>. Stored rather than
/// computed so the committed baseline reads as a report, not as a puzzle.
/// </param>
/// <param name="SavingsPercent">Percentage saved, rounded to one decimal place.</param>
public sealed record ScenarioSavings(
    string Id,
    string Fixture,
    string FilterKey,
    int ExitCode,
    int RawBytes,
    int RawTokens,
    int FilteredTokens,
    int SavedTokens,
    double SavingsPercent);

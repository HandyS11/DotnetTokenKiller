namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>One measurable case: a captured fixture, the filter that would receive it, and the
/// exit code the producing command returned.</summary>
/// <param name="Id">Stable identifier used in the baseline and in drift reports.</param>
/// <param name="Fixture">The fixture file name, as embedded by <see cref="FixtureCorpus"/>.</param>
/// <param name="FilterKey">A <see cref="Domain.Filters.FilterKeys"/> constant.</param>
/// <param name="ExitCode">
/// The producing command's exit code. Every filter treats this as the sole source of the
/// success/failure verdict, so it changes the output and therefore the savings.
/// </param>
public sealed record SavingsScenario(string Id, string Fixture, string FilterKey, int ExitCode);

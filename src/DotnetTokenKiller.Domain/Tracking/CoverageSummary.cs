namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>One command-outcome-source group in a coverage report.</summary>
/// <param name="Command">The recorded command name.</param>
/// <param name="Outcome">How runs in this group produced their output.</param>
/// <param name="Source">Where the raw output for runs in this group came from.</param>
/// <param name="RunCount">Number of runs in this group.</param>
/// <param name="TotalInputTokens">Total raw output tokens across these runs; zero when unmeasured.</param>
/// <param name="TotalExecutionTime">Total wall-clock execution time across these runs.</param>
public sealed record CoverageDetail(
    string Command,
    RunOutcome Outcome,
    RunSource Source,
    int RunCount,
    long TotalInputTokens,
    TimeSpan TotalExecutionTime);

/// <summary>Where dtk is and is not filtering, ranked by how much output is at stake.</summary>
/// <param name="Entries">Groups ordered by total input tokens descending, then run count descending.</param>
/// <param name="TotalRuns">Total runs across every group, filtered and not.</param>
/// <param name="TotalUnfilteredInputTokens">
/// Total raw tokens that reached the caller without passing through any filter. This is the size
/// of the prize available to the next filter.
/// </param>
public sealed record CoverageSummary(
    IReadOnlyList<CoverageDetail> Entries,
    int TotalRuns,
    long TotalUnfilteredInputTokens);

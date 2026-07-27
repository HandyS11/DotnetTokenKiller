namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>How a tracked run produced the output the user saw.</summary>
public enum RunOutcome
{
    /// <summary>A filter ran and produced the output.</summary>
    Filtered = 0,

    /// <summary>The command failed and its filter produced nothing, so the raw tail was emitted.</summary>
    RawTailFallback = 1,

    /// <summary>The filter threw, so the raw output was emitted unchanged.</summary>
    FilterFaulted = 2,

    /// <summary>No filter exists; output was streamed through and its tokens counted.</summary>
    PassthroughMeasured = 3,

    /// <summary>No filter exists and the output was not captured, so its size is unknown.</summary>
    PassthroughUnmeasured = 4
}

/// <summary>Classification helpers for <see cref="RunOutcome"/>.</summary>
public static class RunOutcomes
{
    /// <summary>
    /// The outcomes where a filter actually ran. Only these contribute to <c>dtk gain</c>'s
    /// savings totals — passthrough runs saved nothing and would drag the average toward zero.
    /// </summary>
    public static readonly IReadOnlySet<RunOutcome> CountedInSavings =
        new HashSet<RunOutcome>
        {
            RunOutcome.Filtered,
            RunOutcome.RawTailFallback,
            RunOutcome.FilterFaulted
        };

    /// <summary>Returns <see langword="true"/> when no filter ran for this outcome.</summary>
    /// <param name="outcome">The outcome to classify.</param>
    public static bool IsPassthrough(RunOutcome outcome) => !CountedInSavings.Contains(outcome);
}

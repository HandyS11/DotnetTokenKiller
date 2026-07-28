namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Where the raw output that a run filtered came from.</summary>
/// <remarks>
/// Deliberately separate from <see cref="RunOutcome"/>. Outcome answers <em>how</em> the output was
/// produced (filtered, raw tail, faulted); source answers <em>where the input came from</em>. Folding
/// the two together would need one outcome value per (outcome × source) pair to stay complete.
/// </remarks>
public enum RunSource
{
    /// <summary>dtk launched the process and captured its output.</summary>
    Run = 0,

    /// <summary>The output arrived on stdin via <c>dtk pipe</c>.</summary>
    Pipe = 1
}

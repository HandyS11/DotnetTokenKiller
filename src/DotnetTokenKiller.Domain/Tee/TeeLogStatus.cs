namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Whether a tee log's run finished.</summary>
public enum TeeLogStatus
{
    /// <summary>
    /// The run had not finished when the log was last written. Either it is in flight, or dtk was
    /// killed before it could finalize — the two are indistinguishable from the file alone.
    /// </summary>
    Running = 0,

    /// <summary>The run finished and its exit code was recorded.</summary>
    Complete = 1
}

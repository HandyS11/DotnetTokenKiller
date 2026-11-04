namespace DotnetTokenKiller.Domain.Filters;

/// <summary>The test-run display options a filter and the pipeline honour.</summary>
/// <param name="Names">Whether to list test names.</param>
/// <param name="ShowOutput">Whether to show the tests' own console output.</param>
/// <param name="FailOnEmpty">Whether a run in which no test matched must fail with <c>ExitCodes.NoTestsMatched</c>.</param>
public sealed record TestOptions(bool Names = false, bool ShowOutput = false, bool FailOnEmpty = false)
{
    /// <summary>No test options set.</summary>
    public static TestOptions None { get; } = new();
}

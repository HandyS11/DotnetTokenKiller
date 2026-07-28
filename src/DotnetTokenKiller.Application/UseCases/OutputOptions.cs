namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Display flags controlling dtk's own output around the filtered content.</summary>
/// <param name="VerbosityLevel">0 (default), 1 (<c>-v</c>), or 2 (<c>--vv</c>).</param>
/// <param name="ShowLogHint">Print the path to the full log file when one was written.</param>
/// <param name="Quiet">Suppress all dtk meta-output; overrides the other two.</param>
public sealed record OutputOptions(int VerbosityLevel = 0, bool ShowLogHint = false, bool Quiet = false)
{
    /// <summary>Applies the quiet precedence rule, which otherwise has to be repeated per call site.</summary>
    /// <returns>The options with verbosity and the log hint forced off when <see cref="Quiet"/> is set.</returns>
    public OutputOptions Normalized() =>
        Quiet ? this with { VerbosityLevel = 0, ShowLogHint = false } : this;
}

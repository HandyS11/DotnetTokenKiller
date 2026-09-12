namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>One finished spawn: how long it took, how it exited and everything it printed.</summary>
/// <param name="Milliseconds">Wall-clock from start to exit, with both pipes drained.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StdOut">Everything the process wrote to stdout.</param>
/// <param name="StdErr">Everything the process wrote to stderr.</param>
internal sealed record TimedRun(double Milliseconds, int ExitCode, string StdOut, string StdErr)
{
    /// <summary>Stderr if the process wrote any, otherwise stdout: whichever explains a failure.</summary>
    internal string Diagnostic => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr;
}

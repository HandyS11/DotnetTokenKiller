namespace DotnetTokenKiller.Domain.Execution;

/// <summary>Exit codes dtk reports on its own behalf rather than passing through from a wrapped command.</summary>
public static class ExitCodes
{
    /// <summary>
    /// A run cancelled by Ctrl+C or SIGTERM before the wrapped command exited on its own: 128 + SIGINT,
    /// the shell convention, and Spectre.Console.Cli's default <c>CancellationExitCode</c>.
    /// </summary>
    public const int Cancelled = 130;
}

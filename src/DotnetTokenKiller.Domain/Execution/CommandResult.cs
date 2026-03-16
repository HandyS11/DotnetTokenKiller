namespace DotnetTokenKiller.Domain.Execution;

/// <summary>The result of executing a command process.</summary>
/// <param name="StdOut">The captured standard output.</param>
/// <param name="StdErr">The captured standard error.</param>
/// <param name="ExitCode">The process exit code.</param>
public record CommandResult(string StdOut, string StdErr, int ExitCode);

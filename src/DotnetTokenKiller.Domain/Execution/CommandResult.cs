namespace DotnetTokenKiller.Domain.Execution;

public record CommandResult(string StdOut, string StdErr, int ExitCode);

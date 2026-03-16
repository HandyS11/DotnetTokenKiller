namespace DotnetTokenKiller.Domain.Execution;

/// <summary>Runs a command and captures its output or passes it through.</summary>
public interface ICommandRunner
{
    /// <summary>Runs the command and captures stdout/stderr.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the command with output passed directly to the terminal.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);
}

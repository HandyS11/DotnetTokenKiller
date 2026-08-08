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

    /// <summary>
    /// Runs the command, writing each line of output to the given sinks as it arrives while also
    /// accumulating it, so the output can be measured without withholding it from the user.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="stdOutSink">Receives standard output, line by line, as it is produced.</param>
    /// <param name="stdErrSink">Receives standard error, line by line, as it is produced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommandResult> RunStreamedAsync(
        string command,
        IReadOnlyList<string> args,
        TextWriter stdOutSink,
        TextWriter stdErrSink,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the command, writes <paramref name="standardInput"/> to its stdin, closes stdin, and
    /// captures stdout/stderr.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="standardInput">Text to write to the child's standard input before closing it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommandResult> RunCapturedWithInputAsync(
        string command,
        IReadOnlyList<string> args,
        string standardInput,
        CancellationToken cancellationToken = default);
}

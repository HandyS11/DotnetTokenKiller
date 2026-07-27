using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Runs a dotnet subcommand dtk has no filter for, recording how much output it produced so the
/// next filter can be chosen from data rather than guessed.
/// </summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="tracker">The tracking store.</param>
/// <param name="stdOut">Receives the child's standard output when the run is measured.</param>
/// <param name="stdErr">Receives the child's standard error when the run is measured.</param>
public sealed class PassthroughRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    TextWriter stdOut,
    TextWriter stdErr)
{
    /// <summary>Runs the command and records the run.</summary>
    /// <param name="config">
    /// The already-loaded configuration. Passed in rather than resolved so the caller reads the
    /// config file exactly once.
    /// </param>
    /// <param name="command">The executable to run.</param>
    /// <param name="dotnetArgs">The arguments to pass to it, starting at the subcommand.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The child process exit code.</returns>
    public async Task<int> RunAsync(
        DtkConfig config,
        string command,
        IReadOnlyList<string> dotnetArgs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        // With tracking off there is nothing to measure, so take the cheapest path and leave the
        // child's stdio attached to the terminal exactly as it is today — colour included.
        if (!config.Tracking.Enabled)
        {
            return await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandName = PassthroughSubcommands.CommandName(dotnetArgs);
        var stopwatch = Stopwatch.StartNew();

        if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))
        {
            var passthroughExit = await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            await TrackAsync(commandName, 0, stopwatch.Elapsed, passthroughExit,
                    RunOutcome.PassthroughUnmeasured, cancellationToken)
                .ConfigureAwait(false);
            return passthroughExit;
        }

        var result = await commandRunner
            .RunStreamedAsync(command, dotnetArgs, stdOut, stdErr, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var stripped = AnsiStrip.Strip(result.StdOut + result.StdErr);
        var tokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
        await TrackAsync(commandName, tokens, stopwatch.Elapsed, result.ExitCode,
                RunOutcome.PassthroughMeasured, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
    }

    /// <summary>Records the run, swallowing any failure so tracking cannot break the workflow.</summary>
    /// <param name="commandName">The allowlisted command name.</param>
    /// <param name="tokens">Raw output tokens, or zero when the run was not measured.</param>
    /// <param name="elapsed">Wall-clock time for the run.</param>
    /// <param name="exitCode">The child process exit code.</param>
    /// <param name="outcome">Which passthrough outcome this run had.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task TrackAsync(
        string commandName,
        int tokens,
        TimeSpan elapsed,
        int exitCode,
        RunOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            // No filter ran, so every input token also reached the caller: output equals input and
            // savings are zero. Recording a negative or synthesized saving here would corrupt the
            // very ranking this exists to produce.
            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                commandName,
                Environment.CurrentDirectory,
                new TokenStatistics(tokens, tokens, 0, 0.0),
                elapsed,
                exitCode == 0,
                outcome);

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
}

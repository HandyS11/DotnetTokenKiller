using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Text;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Runs a dotnet subcommand dtk has no filter for, recording how much output it produced so the
/// next filter can be chosen from data rather than guessed.
/// </summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="tracker">The tracking store, or <see langword="null"/> when tracking is off.</param>
/// <param name="teeService">Opens the log a measured run streams into.</param>
/// <param name="stdOut">Receives the child's standard output when the run is measured.</param>
/// <param name="stdErr">Receives the child's standard error when the run is measured.</param>
public sealed class PassthroughRunUseCase(
    ICommandRunner commandRunner,
    ITracker? tracker,
    ITeeService teeService,
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

        // Tee and tracking are configured independently, so either one is reason enough to capture.
        var teeEnabled = config.Tee.Mode != TeeMode.Never;
        if (!config.Tracking.Enabled && !teeEnabled)
        {
            // Nothing to measure and nothing to log, so leave the child's stdio attached to the
            // terminal exactly as it is today — colour included.
            return await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandName = PassthroughSubcommands.CommandName(dotnetArgs);
        var stopwatch = Stopwatch.StartNew();

        if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))
        {
            // Interactive: stdio stays attached, so there is no output to capture or tee.
            var passthroughExit = await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            await TrackAsync(commandName, null, config.Tracking, stopwatch.Elapsed, passthroughExit,
                    RunOutcome.PassthroughUnmeasured, cancellationToken)
                .ConfigureAwait(false);
            return passthroughExit;
        }

        var provisional = new TeeLogHeader(
            $"{command} {string.Join(' ', dotnetArgs)}",
            Environment.CurrentDirectory,
            null,
            RunSource.Run,
            DateTimeOffset.UtcNow);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var session = await teeService
            .BeginAsync(commandName, provisional, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007

        // The terminal takes the raw line so colour survives; the session strips ANSI itself.
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var outSink = new FanOutTextWriter(stdOut, session.Writer);
        await using var errSink = new FanOutTextWriter(stdErr, session.Writer);
#pragma warning restore CA2007

        var result = await commandRunner
            .RunStreamedAsync(command, dotnetArgs, outSink, errSink, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        // The hint is discarded rather than printed: passthrough emits no dtk meta-output today,
        // and the log is reachable through `dtk log`.
        await FinalizeTeeAsync(session, result.ExitCode, cancellationToken).ConfigureAwait(false);

        // The child's exit code above is already captured before any of this runs, so a throw from
        // here on — including from stripping/tokenizing a very large captured output — cannot alter
        // what dtk returns; TrackAsync's try/catch covers stripping and estimation as well as the
        // store write.
        await TrackAsync(commandName, result.StdOut + result.StdErr, config.Tracking,
                stopwatch.Elapsed, result.ExitCode, RunOutcome.PassthroughMeasured, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
    }

    /// <summary>
    /// Finalizes the tee session, swallowing any failure the same way
    /// <c>FilteredOutputPipeline.FinalizeTeeAsync</c> does for the filtered path.
    /// </summary>
    /// <remarks>
    /// <see cref="ITeeSession.FinalizeAsync"/> already swallows most of its own failures, but its
    /// outer catch's own cleanup call can itself throw (disposing the stream re-flushes whatever IO
    /// failure sent it there in the first place). Left unguarded here, that throw would propagate
    /// past <c>PassthroughEntryPoint</c> uncaught — it runs outside <c>Program.cs</c>'s try/catch —
    /// losing the child's real exit code to a raw stack trace.
    /// </remarks>
    /// <param name="session">The session to finalize.</param>
    /// <param name="exitCode">The producing command's exit code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task FinalizeTeeAsync(
        ITeeSession session, int exitCode, CancellationToken cancellationToken)
    {
        try
        {
            await session.FinalizeAsync(exitCode, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
        }
    }

    /// <summary>
    /// Estimates the token count (when measured) and records the run, swallowing any failure —
    /// including one raised while stripping or tokenizing a large captured output — so tracking
    /// cannot break the workflow.
    /// </summary>
    /// <param name="commandName">The allowlisted command name.</param>
    /// <param name="rawOutput">
    /// The combined, un-stripped child stdout/stderr to measure, or <see langword="null"/> when the
    /// run was not measured, in which case zero tokens are recorded without stripping or estimating.
    /// </param>
    /// <param name="trackingConfig">
    /// The tracking configuration, consulted for both whether recording should happen and which
    /// tokenizer to estimate with.
    /// </param>
    /// <param name="elapsed">Wall-clock time for the run.</param>
    /// <param name="exitCode">The child process exit code.</param>
    /// <param name="outcome">Which passthrough outcome this run had.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task TrackAsync(
        string commandName,
        string? rawOutput,
        TrackingConfig trackingConfig,
        TimeSpan elapsed,
        int exitCode,
        RunOutcome outcome,
        CancellationToken cancellationToken)
    {
        // A null tracker and a disabled config both mean the same thing — nothing to record — and
        // this is the single place either one is checked.
        if (tracker is null || !trackingConfig.Enabled)
        {
            return;
        }

        try
        {
            var tokens = rawOutput is null
                ? 0
                : TokenEstimator.Estimate(AnsiStrip.Strip(rawOutput), trackingConfig.Tokenizer);

            // No filter ran, so every input token also reached the caller: output equals input and
            // savings are zero. Recording a negative or synthesized saving here would corrupt the
            // very ranking this exists to produce.
            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                commandName,
                Environment.CurrentDirectory,
                new TokenStatistics(tokens, tokens, 0, 0.0),
                elapsed)
            {
                Success = exitCode == 0,
                Outcome = outcome
            };

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
}

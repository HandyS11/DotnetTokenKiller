using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Runs a dotnet command and hands its output to the shared filtering pipeline.</summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="teeService">Opens the log the run streams into.</param>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="output">The text writer for user-facing output.</param>
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITeeService teeService,
    FilteredOutputPipeline pipeline,
    TextWriter output)
{
    /// <summary>Executes the command, writes filtered output, and records the run.</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="verbosityLevel">Verbosity level controlling diagnostic output.</param>
    /// <param name="showLogHint">When <see langword="true"/>, prints the path to the full log file if one was written.</param>
    /// <param name="quiet">When <see langword="true"/>, suppresses all DTK meta-output; overrides <paramref name="verbosityLevel"/> and <paramref name="showLogHint"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        int verbosityLevel,
        bool showLogHint = false,
        bool quiet = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(args);

        var options = new OutputOptions(verbosityLevel, showLogHint, quiet).Normalized();
        var startTimestamp = Stopwatch.GetTimestamp();

        // Before the tee session and the child, so the tokenizer load and the SQLite setup run while
        // the child does rather than after it exits. After the timestamp, so the recorded elapsed
        // time still includes the configuration load, as it did when the pipeline loaded it.
        var prepared = await pipeline.BeginAsync(cancellationToken).ConfigureAwait(false);
        var commandSlug = ResolveCommandSlug(command, args);
        var displayCommandLine = args.Count > 0 ? $"{command} {string.Join(' ', args)}" : command;

        if (options.VerbosityLevel >= 1)
        {
            await output.WriteLineAsync($"$ {command} {string.Join(' ', args)}").ConfigureAwait(false);
        }

        // Opened before the child starts, so the log is already on disk if dtk is killed mid-run.
        // The timestamp is therefore the run's start, not its end; the filename derives from it, so
        // ordering is unaffected.
        var provisional = new TeeLogHeader(
            displayCommandLine,
            Environment.CurrentDirectory,
            null,
            RunSource.Run,
            DateTimeOffset.UtcNow);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var session = await teeService
            .BeginAsync(commandSlug, provisional, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007

        // Tracking counts stdout while the child streams it; stderr, usually empty for dotnet, is
        // appended when the child exits. The counter relies on the runner writing to the stdout
        // sink exactly the text it returns as StdOut, which ProcessCommandRunner's pump guarantees.
        var counter = prepared.Config.Tracking.Enabled
            ? new ChunkedTokenCounter(prepared.Config.Tracking.Tokenizer)
            : null;
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var countingSink = counter is null ? null : new CountingTextWriter(session.Writer, counter);
#pragma warning restore CA2007
        var stdOutSink = countingSink ?? session.Writer;

        CommandResult result;
        try
        {
            result = await commandRunner
                .RunStreamedAsync(command, args, stdOutSink, session.Writer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The runner has killed the child's tree. dtk is still alive, so close the log as a
            // cancelled run rather than leave it looking like one dtk was killed in the middle of.
            await CancelledRun.FinalizeTeeAsync(session).ConfigureAwait(false);
            throw;
        }

        // RunStreamedAsync awaits both pumps before returning, so both have finished writing by now:
        // appending after Finish would throw, but nothing more will be appended.
        counter?.Finish(result.StdErr);

        var request = new FilteredOutputRequest(
            filter,
            result.StdOut + result.StdErr,
            result.ExitCode,
            commandSlug,
            displayCommandLine,
            RunSource.Run,
            options,
            startTimestamp)
        {
            InputTokenCounter = counter
        };

        return await pipeline.ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the name a run is recorded and tee'd under.</summary>
    /// <param name="command">The executable that was run, used only when there are no arguments.</param>
    /// <param name="args">Arguments passed to the executable, starting at the subcommand.</param>
    /// <returns>
    /// The canonical subcommand name when <paramref name="args"/> begins with one, otherwise the
    /// first argument (an unfiltered passthrough subcommand) or <paramref name="command"/>.
    /// </returns>
    /// <remarks>
    /// The canonical name, not <c>args[0]</c>: a multi-token subcommand would otherwise be recorded
    /// under its first token ("list"), which no report or coverage row would ever match.
    /// </remarks>
    private static string ResolveCommandSlug(string command, IReadOnlyList<string> args)
    {
        if (DotnetSubcommands.TryMatch(args, out var subcommand))
        {
            return subcommand.Name;
        }

        return args.Count > 0 ? args[0] : command;
    }
}

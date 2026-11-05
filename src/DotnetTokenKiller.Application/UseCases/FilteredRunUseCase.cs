using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Runs a dotnet command and hands its output to the shared filtering pipeline.</summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="teeService">Opens the log the run streams into.</param>
/// <param name="rawOutputCopyFactory">Creates the file <c>--tee &lt;path&gt;</c> copies the raw output into.</param>
/// <param name="fileSnapshotter">Hashes the tree around a run for filters that need the changed files.</param>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="output">The text writer for user-facing output.</param>
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITeeService teeService,
    IRawOutputCopyFactory rawOutputCopyFactory,
    IFileContentSnapshotter fileSnapshotter,
    FilteredOutputPipeline pipeline,
    TextWriter output)
{
    /// <summary>Executes the command, writes filtered output, and records the run.</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="options">Display flags; <see cref="OutputOptions.Normalized"/> is applied here.</param>
    /// <param name="test">Test display options handed to the filter; none when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        OutputOptions options,
        TestOptions? test = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);

        options = options.Normalized();

        // First, before the log, the tracking setup or the child exist: a --tee path that cannot be created
        // stops the run here, so an agent never believes evidence was saved when it was not.
        IRawOutputCopy? rawCopy;
        try
        {
            rawCopy = options.TeePath is { } teePath ? rawOutputCopyFactory.Open(teePath) : null;
        }
        catch (RawOutputCopyException ex)
        {
            return await pipeline.RefuseRawCopyAsync(ex, cancellationToken).ConfigureAwait(false);
        }

        // Closed as soon as the child has exited (below), so a failure closing it is reported with the run;
        // this closes it on every other way out, a cancellation included.
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var copy = rawCopy;
#pragma warning restore CA2007

        var startTimestamp = Stopwatch.GetTimestamp();
        var startedUtc = DateTimeOffset.UtcNow;

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

        // With --tee, every line also goes to the copy, under the counting sink, whatever tee.mode says.
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var session = CopyingTeeSession.Wrap(
            await teeService.BeginAsync(commandSlug, provisional, cancellationToken).ConfigureAwait(false), copy);
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
        SnapshotRun snapshot;
        try
        {
            // Before the child starts, and inside this guard so a Ctrl+C during the walk still closes the log.
            snapshot = TakeSnapshotBefore(filter, args, cancellationToken);
            result = await commandRunner
                .RunStreamedAsync(command, args, stdOutSink, session.Writer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The runner has killed the child's tree. dtk is still alive, so close the log as a
            // cancelled run rather than leave it looking like one dtk was killed in the middle of.
            // None: the run's own token is already cancelled, and this is the cleanup it exists to allow.
            await FilteredOutputPipeline.FinalizeTeeAsync(session, ExitCodes.Cancelled, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        // RunStreamedAsync awaits both pumps before returning, so both have finished writing by now:
        // appending after Finish would throw, but nothing more will be appended.
        counter?.Finish(result.StdErr);
        if (copy is not null)
        {
            await copy.DisposeAsync().ConfigureAwait(false);
        }

        // Only here, once the child has exited on its own: a cancelled run rethrew above. Not cancellable, like
        // the pipeline call below, so a late Ctrl+C cannot drop the output of a run that finished.
        var changes = ChangesSince(snapshot, options.TeePath);

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
            InputTokenCounter = counter,
            RawCopyFailure = copy?.Failure,
            Context = new FilterContext
            {
                Source = RunSource.Run,
                Arguments = ArgumentsAfterSubcommand(args),
                WorkingDirectory = Environment.CurrentDirectory,
                StartedUtc = startedUtc,
                Test = test ?? TestOptions.None,
                Changes = changes
            }
        };

        // None: the child has exited, and a cancellation landing now (Ctrl+C's grace period elapsing)
        // must not drop the filtered output, the log or the record of a run that finished on its own.
        return await pipeline.ProcessAsync(request, session, prepared, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Hashes the tree before the child starts, for a filter that asks for it; never fails the run.</summary>
    /// <param name="filter">The run's filter.</param>
    /// <param name="args">The command's arguments.</param>
    /// <param name="token">Cancels the walk.</param>
    private SnapshotRun TakeSnapshotBefore(IOutputFilter filter, IReadOnlyList<string> args, CancellationToken token)
    {
        if (filter is not ISnapshotFilter snapshotFilter)
        {
            return default;
        }

        try
        {
            var root = snapshotFilter.SnapshotRoot(ArgumentsAfterSubcommand(args), Environment.CurrentDirectory);
            var before = fileSnapshotter.Take(root, token);
            return new SnapshotRun(root, before, before is null ? TreeTooLarge : null);
        }
#pragma warning disable CA1031 // A filter's file bookkeeping must never break the user's command.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new SnapshotRun(string.Empty, null, SnapshotFailed);
        }
    }

    /// <summary>The files that changed during the run, or <see langword="null"/> when the filter asked for none.</summary>
    /// <param name="run">What was taken before the child started.</param>
    /// <param name="teePath">
    /// The run's <c>--tee</c> file, if any: dtk creates it before the snapshot and fills it during the run, so it is
    /// left out rather than listed as a file the tool changed.
    /// </param>
    private FileChangeSet? ChangesSince(SnapshotRun run, string? teePath)
    {
        if (run.Root is null)
        {
            return null;
        }

        if (run.Before is null)
        {
            return FileChangeSet.NotComputed(run.FailureReason ?? SnapshotFailed);
        }

        try
        {
            var after = fileSnapshotter.Take(run.Root);
            if (after is null)
            {
                return FileChangeSet.NotComputed(TreeTooLarge);
            }

            var changed = after.ChangedSince(run.Before);
            var teeEntry = teePath is null ? null : SnapshotEntry(run.Root, teePath);
            return new FileChangeSet(teeEntry is null
                ? changed
                : [.. changed.Where(path => !string.Equals(path, teeEntry, StringComparison.Ordinal))]);
        }
#pragma warning disable CA1031 // A filter's file bookkeeping must never break the user's command.
        catch (Exception)
#pragma warning restore CA1031
        {
            return FileChangeSet.NotComputed(SnapshotFailed);
        }
    }

    /// <summary>
    /// <paramref name="path"/> as a snapshot names it: relative to <paramref name="root"/>, <c>/</c>-separated, or
    /// <see langword="null"/> when it lies outside the root.
    /// </summary>
    /// <param name="root">The snapshot's root.</param>
    /// <param name="path">A path as the user typed it, relative to the working directory or absolute.</param>
    private static string? SnapshotEntry(string root, string path)
    {
        // Resolved as RawOutputCopyFactory opens it: against the working directory.
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return null;
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The arguments after the subcommand's tokens, or after the first one when none matches.</summary>
    /// <param name="args">Arguments passed to the executable, starting at the subcommand.</param>
    private static string[] ArgumentsAfterSubcommand(IReadOnlyList<string> args)
    {
        var skip = DotnetSubcommands.TryMatch(args, out var subcommand) ? subcommand.TokenCount : 1;
        return [.. args.Skip(skip)];
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

    private const string TreeTooLarge = "tree too large";
    private const string SnapshotFailed = "snapshot failed";

    private readonly record struct SnapshotRun(string? Root, FileContentSnapshot? Before, string? FailureReason);
}

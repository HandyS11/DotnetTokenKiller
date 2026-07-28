using System.Diagnostics;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Runs a dotnet command and hands its output to the shared filtering pipeline.</summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="output">The text writer for user-facing output.</param>
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
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

        if (options.VerbosityLevel >= 1)
        {
            await output.WriteLineAsync($"$ {command} {string.Join(' ', args)}").ConfigureAwait(false);
        }

        var result = await commandRunner.RunCapturedAsync(command, args, cancellationToken).ConfigureAwait(false);

        var request = new FilteredOutputRequest(
            filter,
            result.StdOut + result.StdErr,
            result.ExitCode,
            ResolveCommandSlug(command, args),
            args.Count > 0 ? $"{command} {string.Join(' ', args)}" : command,
            RunSource.Run,
            options,
            startTimestamp);

        return await pipeline.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
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

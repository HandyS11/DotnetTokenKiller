using System.Diagnostics;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Filters output that arrived on stdin rather than from a process dtk launched.</summary>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="input">The text reader supplying the piped output.</param>
public sealed class PipeFilterUseCase(FilteredOutputPipeline pipeline, TextReader input)
{
    /// <summary>Reads stdin to the end, filters it, and records the run.</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="commandSlug">The canonical subcommand name the output came from.</param>
    /// <param name="exitCode">
    /// The exit code of the command that produced the output. A shell pipe cannot supply this, so
    /// it is passed explicitly by the caller and defaults to success at the CLI boundary.
    /// </param>
    /// <param name="options">Display flags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><paramref name="exitCode"/>, so a CI step wrapping a failed build still fails.</returns>
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string commandSlug,
        int exitCode,
        OutputOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandSlug);
        ArgumentNullException.ThrowIfNull(options);

        var startTimestamp = Stopwatch.GetTimestamp();
        var raw = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var request = new FilteredOutputRequest(
            filter,
            raw,
            exitCode,
            commandSlug,
            $"dotnet {commandSlug}",
            RunSource.Pipe,
            options.Normalized(),
            startTimestamp);

        return await pipeline.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

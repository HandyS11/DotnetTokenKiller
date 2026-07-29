using System.Diagnostics;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Filters output that arrived on stdin rather than from a process dtk launched.</summary>
/// <param name="pipeline">The shared output-filtering pipeline.</param>
/// <param name="teeService">Opens the log the run's output is copied into.</param>
/// <param name="input">The text reader supplying the piped output.</param>
public sealed class PipeFilterUseCase(FilteredOutputPipeline pipeline, ITeeService teeService, TextReader input)
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

        // Piped input is read to completion before anything can be written, so this path gains no
        // durability. It uses the session API so the header format and the retention rules have a
        // single implementation rather than two that can drift.
        var provisional = new TeeLogHeader(
            $"dotnet {commandSlug}",
            Environment.CurrentDirectory,
            null,
            RunSource.Pipe,
            DateTimeOffset.UtcNow);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var session = await teeService
            .BeginAsync(commandSlug, provisional, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        await session.Writer.WriteLineAsync(raw.AsMemory(), cancellationToken).ConfigureAwait(false);

        var request = new FilteredOutputRequest(
            filter,
            raw,
            exitCode,
            commandSlug,
            $"dotnet {commandSlug}",
            RunSource.Pipe,
            options.Normalized(),
            startTimestamp);

        return await pipeline.ProcessAsync(request, session, cancellationToken).ConfigureAwait(false);
    }
}

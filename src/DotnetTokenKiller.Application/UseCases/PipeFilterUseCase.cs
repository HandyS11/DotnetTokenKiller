using System.Diagnostics;
using System.Text;
using DotnetTokenKiller.Application.Helpers;
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

        // Before reading stdin, so setup overlaps a slow producer on the other end of the pipe.
        var prepared = await pipeline.BeginAsync(cancellationToken).ConfigureAwait(false);
        var counter = prepared.Config.Tracking.Enabled
            ? new ChunkedTokenCounter(prepared.Config.Tracking.Tokenizer)
            : null;
        var raw = await ReadAllAsync(input, counter, cancellationToken).ConfigureAwait(false);
        counter?.Finish(string.Empty);

        // Piped input is read to completion before anything can be written, so this path gains no
        // durability. It uses the session API so the header format and the retention rules have a
        // single implementation rather than two that can drift.
        var displayCommandLine = $"dotnet {commandSlug}";
        var provisional = new TeeLogHeader(
            displayCommandLine,
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
            displayCommandLine,
            RunSource.Pipe,
            options.Normalized(),
            startTimestamp)
        {
            InputTokenCounter = counter
        };

        return await pipeline.ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
    }

    private const int ReadBlockChars = 64 * 1024;

    /// <summary>Reads stdin to the end in blocks, feeding each to the counter as it arrives.</summary>
    /// <param name="input">The text reader supplying the piped output.</param>
    /// <param name="counter">The run's counter, or <see langword="null"/> when tracking is disabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<string> ReadAllAsync(TextReader input, ChunkedTokenCounter? counter, CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBlockChars];
        var raw = new StringBuilder();
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            raw.Append(buffer, 0, read);
            counter?.Append(buffer.AsSpan(0, read));
        }

        return raw.ToString();
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Text;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Condenses captured output and records what it cost, independent of how the output was obtained.
/// </summary>
/// <remarks>
/// Shared by <see cref="FilteredRunUseCase"/> (which runs a process) and
/// <see cref="PipeFilterUseCase"/> (which reads stdin) so the raw-tail rule, the outcome ordering,
/// the glyph rules, and the tracking shape cannot drift between the two entry points.
/// </remarks>
/// <param name="tracker">The tracking store.</param>
/// <param name="output">The text writer for user-facing output.</param>
/// <param name="configProvider">The configuration provider.</param>
public sealed class FilteredOutputPipeline(
    ITracker tracker,
    TextWriter output,
    IConfigProvider configProvider)
{
    /// <summary>
    /// Loads the configuration for a run and, when tracking is enabled, starts tracking setup in the
    /// background. Call it as the run starts, before its output exists, and pass the result to
    /// <see cref="ProcessAsync(FilteredOutputRequest, ITeeSession, PreparedRun, CancellationToken)"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded configuration and the started warm-up.</returns>
    public async Task<PreparedRun> BeginAsync(CancellationToken cancellationToken = default)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var warmUp = config.Tracking.Enabled
            ? TrackingWarmUp.Start(tracker, config.Tracking.Tokenizer, cancellationToken)
            : TrackingWarmUp.None;

        return new PreparedRun(config, warmUp);
    }

    /// <summary>Filters the request's output, writes it, and records the run, preparing it first.</summary>
    /// <remarks>
    /// For callers with no earlier point to start setup at: the configuration load and tracking setup
    /// run here, serially, exactly as they did before <see cref="BeginAsync"/> existed.
    /// </remarks>
    /// <param name="request">The output and metadata to process.</param>
    /// <param name="session">
    /// The log opened for this run, finalized here. Callers with nothing to log pass
    /// <see cref="NullTeeSession.Instance"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request's exit code, unchanged.</returns>
    public async Task<int> ProcessAsync(
        FilteredOutputRequest request,
        ITeeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);

        var prepared = await BeginAsync(cancellationToken).ConfigureAwait(false);
        return await ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Filters the request's output, writes it, and records the run.</summary>
    /// <param name="request">The output and metadata to process.</param>
    /// <param name="session">
    /// The log opened for this run, finalized here. Callers with nothing to log pass
    /// <see cref="NullTeeSession.Instance"/>.
    /// </param>
    /// <param name="prepared">What <see cref="BeginAsync"/> returned when this run started.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request's exit code, unchanged.</returns>
    public async Task<int> ProcessAsync(
        FilteredOutputRequest request,
        ITeeSession session,
        PreparedRun prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prepared);

        var config = prepared.Config;
        var options = request.Options.Normalized();
        var stripped = AnsiStrip.Strip(request.RawOutput);

        var (filtered, filterFaulted) =
            await ApplyFilterSafelyAsync(request.Filter, stripped, request.ExitCode, options.VerbosityLevel)
                .ConfigureAwait(false);

        // Finalized before the raw-tail fallback is built, because the fallback embeds this hint.
        var logHint = await FinalizeTeeAsync(session, request.ExitCode, cancellationToken).ConfigureAwait(false);

        var usedRawTailFallback = false;
        if (request.ExitCode != 0 && string.IsNullOrWhiteSpace(filtered))
        {
            filtered = BuildRawTailFallback(request.DisplayCommandLine, request.ExitCode, stripped,
                options.ShowLogHint ? logHint : null);
            usedRawTailFallback = true;
        }

        // A faulted filter yields the raw text unchanged (see ApplyFilterSafelyAsync), so when the
        // raw output is itself empty or whitespace on a failed command, filterFaulted and
        // usedRawTailFallback are BOTH true at once — the two outcomes are not mutually exclusive.
        // Checking a faulted filter first resolves that overlap in its favour; that ordering is
        // deliberate, not incidental, and is covered by
        // ProcessAsync_PrefersFilterFaulted_WhenBothConditionsOverlap.
        var outcome = (filterFaulted, usedRawTailFallback) switch
        {
            (true, _) => RunOutcome.FilterFaulted,
            (false, true) => RunOutcome.RawTailFallback,
            (false, false) => RunOutcome.Filtered
        };

        filtered = NormalizeGlyphs(filtered, config);

        if (options.VerbosityLevel >= 2)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(request.StartTimestamp).TotalMilliseconds;
            await output.WriteLineAsync("[raw output]").ConfigureAwait(false);
            await output.WriteLineAsync(stripped).ConfigureAwait(false);
            await output.WriteLineAsync($"[elapsed: {elapsedMs}ms]").ConfigureAwait(false);
        }

        await output.WriteAsync(filtered).ConfigureAwait(false);

        if (!usedRawTailFallback && logHint is not null && options.ShowLogHint)
        {
            await output.WriteLineAsync(logHint).ConfigureAwait(false);
        }

        await TrackIfEnabledAsync(prepared, request, stripped, filtered,
                Stopwatch.GetElapsedTime(request.StartTimestamp), outcome, cancellationToken)
            .ConfigureAwait(false);

        return request.ExitCode;
    }

    /// <summary>Applies the filter, falling back to raw output if the filter throws (never breaks the workflow).</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="stripped">The ANSI-stripped command output.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="verbosityLevel">Verbosity level controlling diagnostic output.</param>
    /// <returns>
    /// The filtered output and whether the filter threw. A faulted filter yields the stripped
    /// output unchanged.
    /// </returns>
    private async Task<(string Output, bool Faulted)> ApplyFilterSafelyAsync(
        IOutputFilter filter,
        string stripped,
        int exitCode,
        int verbosityLevel)
    {
        try
        {
            return (filter.Apply(stripped, exitCode), false);
        }
        catch
        {
            // Intentional: filter errors must not break the user's workflow
            if (verbosityLevel >= 2)
            {
                await output.WriteLineAsync("[filter error — using raw output]").ConfigureAwait(false);
            }

            return (stripped, true);
        }
    }

    /// <summary>Replaces ✓/✗/⚠ glyphs with ASCII equivalents when emoji are disabled or NO_COLOR is set.</summary>
    /// <param name="filtered">The filtered output text.</param>
    /// <param name="config">The DTK configuration.</param>
    /// <returns>The output with glyphs normalized according to configuration and environment.</returns>
    private static string NormalizeGlyphs(string filtered, DtkConfig config)
    {
        if (config.Display.Emoji && Environment.GetEnvironmentVariable("NO_COLOR") is null)
        {
            return filtered;
        }

        return filtered
            .Replace("✓", "ok:", StringComparison.Ordinal)
            .Replace("✗", "FAIL:", StringComparison.Ordinal)
            .Replace("⚠", "WARN:", StringComparison.Ordinal);
    }

    /// <summary>Builds a fallback message for failed commands whose output the filter could not parse.</summary>
    /// <param name="command">The command line that was run, for display purposes.</param>
    /// <param name="exitCode">The non-zero process exit code.</param>
    /// <param name="rawOutput">The raw (ANSI-stripped) command output.</param>
    /// <param name="logHint">An optional tee log hint to append when present.</param>
    private static string BuildRawTailFallback(string command, int exitCode, string rawOutput, string? logHint)
    {
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tail = string.Join('\n', lines.TakeLast(40).Select(line => line.TrimEnd('\r')));
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"✗ {command} failed (exit {exitCode})")
            .AppendLine(tail);
        if (logHint is not null)
        {
            sb.AppendLine(logHint);
        }

        return sb.ToString();
    }

    private static async Task<string?> FinalizeTeeAsync(
        ITeeSession session,
        int exitCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await session.FinalizeAsync(exitCode, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
            return null;
        }
    }

    private async Task TrackIfEnabledAsync(
        PreparedRun prepared,
        FilteredOutputRequest request,
        string stripped,
        string filtered,
        TimeSpan elapsed,
        RunOutcome outcome,
        CancellationToken cancellationToken)
    {
        var config = prepared.Config;
        if (!config.Tracking.Enabled)
        {
            return;
        }

        try
        {
            // Setup started in BeginAsync has usually finished while the child ran. This never
            // throws; a failed setup resurfaces in Estimate or RecordAsync below, inside this catch.
            await prepared.WarmUp.WhenReadyAsync().ConfigureAwait(false);

            var inputTokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
            var outputTokens = TokenEstimator.Estimate(filtered, config.Tracking.Tokenizer);
            var savedTokens = inputTokens - outputTokens;
            var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                request.CommandSlug,
                Environment.CurrentDirectory,
                new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
                elapsed)
            {
                Success = request.ExitCode == 0,
                Outcome = outcome,
                Source = request.Source
            };

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
}

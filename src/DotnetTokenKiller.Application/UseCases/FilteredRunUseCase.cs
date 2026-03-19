using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using System.Diagnostics;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Runs a dotnet command, filters its output, and records the token savings.</summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="tracker">The tracking store.</param>
/// <param name="teeService">The tee output service.</param>
/// <param name="output">The text writer for user-facing output.</param>
/// <param name="configProvider">The configuration provider.</param>
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    ITeeService teeService,
    TextWriter output,
    IConfigProvider configProvider)
{
    /// <summary>Executes the command, writes filtered output, and records the run.</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="verbosityLevel">Verbosity level controlling diagnostic output.</param>
    /// <param name="showLogHint">When <see langword="true"/>, prints the path to the full log file if one was written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        int verbosityLevel,
        bool showLogHint = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(args);

        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        if (verbosityLevel >= 1)
        {
            await output.WriteLineAsync($"$ {command} {string.Join(' ', args)}").ConfigureAwait(false);
        }

        var result = await commandRunner.RunCapturedAsync(command, args, cancellationToken).ConfigureAwait(false);

        var raw = result.StdOut + result.StdErr;
        var stripped = AnsiStrip.Strip(raw);

        string filtered;
        try
        {
            filtered = filter.Apply(stripped);
        }
        catch
        {
            // Intentional: filter errors must not break the user's workflow
            if (verbosityLevel >= 2)
            {
                await output.WriteLineAsync("[filter error — using raw output]").ConfigureAwait(false);
            }

            filtered = stripped;
        }

        if (!config.Display.Emoji)
        {
            filtered = filtered.Replace("✓", "ok:", StringComparison.Ordinal);
        }

        if (verbosityLevel >= 2)
        {
            await output.WriteLineAsync("[raw output]").ConfigureAwait(false);
            await output.WriteLineAsync(stripped).ConfigureAwait(false);
            await output.WriteLineAsync($"[elapsed: {stopwatch.ElapsedMilliseconds}ms]").ConfigureAwait(false);
        }

        await output.WriteAsync(filtered).ConfigureAwait(false);

        stopwatch.Stop();

        // Tee: silent — errors never surface
        try
        {
            var commandSlug = args.Count > 0 ? args[0] : command;
            var hint = await teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken).ConfigureAwait(false);
            if (hint is not null && showLogHint)
            {
                await output.WriteLineAsync(hint).ConfigureAwait(false);
            }
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
        }

        // Track: silent — errors never surface
        if (config.Tracking.Enabled)
        {
            try
            {
                var inputTokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
                var outputTokens = TokenEstimator.Estimate(filtered, config.Tracking.Tokenizer);
                var savedTokens = inputTokens - outputTokens;
                var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

                var record = new CommandRecord(
                    DateTimeOffset.UtcNow,
                    args.Count > 0 ? args[0] : command,
                    Environment.CurrentDirectory,
                    inputTokens,
                    outputTokens,
                    savedTokens,
                    savingsPct,
                    stopwatch.Elapsed);

                await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Intentional: tracking errors must not surface to the user
            }
        }

        return result.ExitCode;
    }
}

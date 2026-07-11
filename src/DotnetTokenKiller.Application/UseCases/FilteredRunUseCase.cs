using System.Diagnostics;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

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

        if (quiet)
        {
            verbosityLevel = 0;
            showLogHint = false;
        }

        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        if (verbosityLevel >= 1)
        {
            await output.WriteLineAsync($"$ {command} {string.Join(' ', args)}").ConfigureAwait(false);
        }

        var result = await commandRunner.RunCapturedAsync(command, args, cancellationToken).ConfigureAwait(false);

        var raw = result.StdOut + result.StdErr;
        var stripped = AnsiStrip.Strip(raw);
        var commandSlug = args.Count > 0 ? args[0] : command;

        string filtered;
        try
        {
            filtered = filter.Apply(stripped, result.ExitCode);
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

        var logHint = await GetTeeHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken)
            .ConfigureAwait(false);

        var usedRawTailFallback = false;
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(filtered))
        {
            var commandLine = args.Count > 0 ? $"{command} {string.Join(' ', args)}" : command;
            filtered = BuildRawTailFallback(commandLine, result.ExitCode, stripped, showLogHint ? logHint : null);
            usedRawTailFallback = true;
        }

        if (!config.Display.Emoji || Environment.GetEnvironmentVariable("NO_COLOR") is not null)
        {
            filtered = filtered
                .Replace("✓", "ok:", StringComparison.Ordinal)
                .Replace("✗", "FAIL:", StringComparison.Ordinal);
        }

        if (verbosityLevel >= 2)
        {
            await output.WriteLineAsync("[raw output]").ConfigureAwait(false);
            await output.WriteLineAsync(stripped).ConfigureAwait(false);
            await output.WriteLineAsync($"[elapsed: {stopwatch.ElapsedMilliseconds}ms]").ConfigureAwait(false);
        }

        await output.WriteAsync(filtered).ConfigureAwait(false);

        if (!usedRawTailFallback && logHint is not null && showLogHint)
        {
            await output.WriteLineAsync(logHint).ConfigureAwait(false);
        }

        stopwatch.Stop();

        await TrackIfEnabledAsync(config, commandSlug, stripped, filtered, stopwatch.Elapsed, result.ExitCode,
                cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
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

    private async Task<string?> GetTeeHintAsync(
        string stripped,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await teeService.TeeAndHintAsync(stripped, commandSlug, exitCode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
            return null;
        }
    }

    private async Task TrackIfEnabledAsync(
        DtkConfig config,
        string commandSlug,
        string stripped,
        string filtered,
        TimeSpan elapsed,
        int exitCode,
        CancellationToken cancellationToken)
    {
        if (!config.Tracking.Enabled)
        {
            return;
        }

        try
        {
            var inputTokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
            var outputTokens = TokenEstimator.Estimate(filtered, config.Tracking.Tokenizer);
            var savedTokens = inputTokens - outputTokens;
            var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                commandSlug,
                Environment.CurrentDirectory,
                new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
                elapsed,
                exitCode == 0);

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
}

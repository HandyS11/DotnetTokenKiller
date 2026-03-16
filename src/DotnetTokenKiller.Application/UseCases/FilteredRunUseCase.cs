using DotnetTokenKiller.Application.Helpers;
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
public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    ITeeService teeService)
{
    /// <summary>Executes the command, writes filtered output, and records the run.</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="verbosityLevel">Verbosity level controlling diagnostic output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        int verbosityLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(args);

        var stopwatch = Stopwatch.StartNew();

        if (verbosityLevel >= 1)
        {
            Console.WriteLine($"$ {command} {string.Join(' ', args)}");
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
                Console.WriteLine("[filter error — using raw output]");
            }

            filtered = stripped;
        }

        if (verbosityLevel >= 2)
        {
            Console.WriteLine("[raw output]");
            Console.WriteLine(stripped);
            Console.WriteLine($"[elapsed: {stopwatch.ElapsedMilliseconds}ms]");
        }

        Console.Write(filtered);

        stopwatch.Stop();

        // Tee: silent — errors never surface
        try
        {
            var commandSlug = args.Count > 0 ? args[0] : command;
            var hint = await teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken).ConfigureAwait(false);
            if (hint is not null)
            {
                Console.WriteLine(hint);
            }
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
        }

        // Track: silent — errors never surface
        try
        {
            var inputTokens = TokenEstimator.Estimate(stripped);
            var outputTokens = TokenEstimator.Estimate(AnsiStrip.Strip(filtered));
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

        return result.ExitCode;
    }
}

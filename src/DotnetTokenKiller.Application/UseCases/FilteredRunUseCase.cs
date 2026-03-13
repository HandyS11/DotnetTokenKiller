using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using System.Diagnostics;

namespace DotnetTokenKiller.Application.UseCases;

public sealed class FilteredRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    ITeeService teeService)
{
    public async Task<int> RunAsync(
        IOutputFilter filter,
        string command,
        IReadOnlyList<string> args,
        int verbosityLevel,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (verbosityLevel >= 1)
            Console.WriteLine($"$ {command} {string.Join(' ', args)}");

        var result = await commandRunner.RunCapturedAsync(command, args, cancellationToken);

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
                Console.WriteLine("[filter error — using raw output]");
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
            var hint = await teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken);
            if (hint is not null)
                Console.WriteLine(hint);
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
                Timestamp: DateTimeOffset.UtcNow,
                Command: args.Count > 0 ? args[0] : command,
                ProjectPath: Environment.CurrentDirectory,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                SavedTokens: savedTokens,
                SavingsPercentage: savingsPct,
                ExecutionTime: stopwatch.Elapsed);

            await tracker.RecordAsync(record, cancellationToken);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }

        return result.ExitCode;
    }
}

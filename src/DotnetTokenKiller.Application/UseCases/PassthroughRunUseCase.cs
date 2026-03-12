using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using System.Diagnostics;

namespace DotnetTokenKiller.Application.UseCases;

public sealed class PassthroughRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker)
{
    public async Task<int> RunAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var exitCode = await commandRunner.RunPassthroughAsync(command, args, cancellationToken);
        stopwatch.Stop();

        try
        {
            var subcommand = args.Count > 0 ? args[0] : command;
            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                subcommand,
                Environment.CurrentDirectory,
                0,
                0,
                0,
                0.0,
                stopwatch.Elapsed);
            await tracker.RecordAsync(record, cancellationToken);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }

        return exitCode;
    }
}

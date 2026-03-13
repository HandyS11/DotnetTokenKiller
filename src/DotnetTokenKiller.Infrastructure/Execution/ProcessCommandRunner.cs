using DotnetTokenKiller.Domain.Execution;
using System.Diagnostics;

namespace DotnetTokenKiller.Infrastructure.Execution;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        // CRITICAL: Read both streams concurrently — sequential reads deadlock on large output
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdOutTask, stdErrTask);
        await process.WaitForExitAsync(cancellationToken);

        // Tasks are already complete after WhenAll; await here is instant and satisfies analyzers
        return new CommandResult(await stdOutTask, await stdErrTask, process.ExitCode);
    }

    public async Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

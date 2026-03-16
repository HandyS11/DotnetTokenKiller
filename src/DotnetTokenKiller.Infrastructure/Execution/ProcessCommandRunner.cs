using DotnetTokenKiller.Domain.Execution;
using System.Diagnostics;

namespace DotnetTokenKiller.Infrastructure.Execution;

/// <summary>Runs system processes using <see cref="System.Diagnostics.Process"/>.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <inheritdoc/>
    public async Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

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
        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // Tasks are already complete after WhenAll; await here is instant and satisfies analyzers
        return new CommandResult(await stdOutTask.ConfigureAwait(false), await stdErrTask.ConfigureAwait(false), process.ExitCode);
    }

    /// <inheritdoc/>
    public async Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

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

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

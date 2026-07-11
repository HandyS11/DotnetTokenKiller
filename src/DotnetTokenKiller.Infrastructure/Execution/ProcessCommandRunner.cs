using System.Diagnostics;
using System.Text;
using DotnetTokenKiller.Domain.Execution;

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
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start process: {command}");

#pragma warning disable CA2016 // CancellationToken is handled via registration below
        var registration = cancellationToken.Register(static state => KillProcess((Process)state!), process);
#pragma warning restore CA2016
        try
        {
            // CRITICAL: Read both streams concurrently — sequential reads deadlock on large output
            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Tasks are already complete after WhenAll; await here is instant and satisfies analyzers
            return new CommandResult(await stdOutTask.ConfigureAwait(false), await stdErrTask.ConfigureAwait(false),
                process.ExitCode);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
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
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start process: {command}");

#pragma warning disable CA2016 // CancellationToken is handled via registration below
        var registration = cancellationToken.Register(static state => KillProcess((Process)state!), process);
#pragma warning restore CA2016
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill
        }
    }
}

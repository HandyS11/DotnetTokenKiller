using System.ComponentModel;
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
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = StartProcess(psi, command);

        // Close stdin immediately so a child that reads it sees EOF and exits instead of hanging
        // forever waiting for input this non-interactive capture will never provide.
        process.StandardInput.Close();

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

        using var process = StartProcess(psi, command);

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

    private static Process StartProcess(ProcessStartInfo psi, string command)
    {
        try
        {
            return Process.Start(psi)
                   ?? throw new InvalidOperationException($"Failed to start process: {command}");
        }
        catch (Win32Exception ex)
        {
            // The OS refused to launch (missing binary, no exec permission). The raw Win32Exception
            // never names the command, so wrap it in a message that says which one failed and why.
            throw new InvalidOperationException($"Failed to start process '{command}': {ex.Message}", ex);
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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
        {
            // The process exited between the check and the kill, or the OS refused the kill
            // (already-reaped child / access race). Nothing left to terminate.
        }
    }
}

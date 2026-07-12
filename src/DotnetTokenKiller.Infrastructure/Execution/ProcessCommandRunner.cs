using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        catch (OperationCanceledException)
        {
            // The read/wait tasks cancel the instant the token fires, which can unwind this method
            // before the registration callback's tree-kill has actually reaped the child. Kill and
            // wait for the whole tree here so the process is provably gone before we return.
            await KillAndReapAsync(process).ConfigureAwait(false);
            throw;
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
        catch (OperationCanceledException)
        {
            await KillAndReapAsync(process).ConfigureAwait(false);
            throw;
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

    private static async Task KillAndReapAsync(Process process)
    {
        KillProcess(process);

        // Bounded wait so cancellation cleanup can never hang: the kill was already issued, and if
        // the OS is slow to tear the tree down we stop waiting once the grace period elapses.
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Grace elapsed (or the original token is still firing); nothing more to do here.
        }
    }

    private static void KillProcess(Process process)
    {
        int processId;
        try
        {
            if (process.HasExited)
            {
                return;
            }

            // Capture the id before the kill so the Windows backstop below can still target the tree.
            processId = process.Id;
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
        {
            // The process exited between the check and the kill, or the OS refused the kill
            // (already-reaped child / access race). Nothing left to terminate.
            return;
        }

        // Backstop: Process.Kill(entireProcessTree: true) has proven unreliable at tearing down a
        // shell subtree (e.g. powershell) on the Windows CI runner, leaving the child alive until it
        // exits on its own. taskkill /T force-kills the whole tree by pid. Harmless if already dead.
        if (OperatingSystem.IsWindows())
        {
            TryKillTreeWithTaskkill(processId);
        }
    }

    private static void TryKillTreeWithTaskkill(int processId)
    {
        try
        {
            // Absolute path (System32\taskkill.exe) avoids resolving the command through PATH.
            var taskkillPath = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
            var psi = new ProcessStartInfo(taskkillPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/T");
            psi.ArgumentList.Add("/F");
            psi.ArgumentList.Add("/PID");
            psi.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));

            using var killer = Process.Start(psi);
            if (killer is null)
            {
                return;
            }

            // Bound the wait first so a hung taskkill can never block cleanup indefinitely; only
            // drain the (tiny, sub-pipe-buffer) output once it has actually exited. If it doesn't
            // exit in time, leave it — this is a best-effort backstop during cancellation cleanup.
            if (killer.WaitForExit(milliseconds: 5000))
            {
                _ = killer.StandardOutput.ReadToEnd();
                _ = killer.StandardError.ReadToEnd();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // Best-effort backstop: taskkill may be absent or the pid already gone.
        }
    }
}

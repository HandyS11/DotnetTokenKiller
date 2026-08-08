using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Infrastructure.Execution;

/// <summary>Runs system processes using <see cref="System.Diagnostics.Process"/>.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <summary>
    /// How long the post-kill reap waits before giving up. Bounded so cancellation cleanup can never
    /// hang: the kill was already issued, and if the OS is slow to tear the tree down we stop waiting.
    /// </summary>
    private static readonly TimeSpan ReapGracePeriod = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        return RunRedirectedAsync(
            command,
            args,
            static (reader, token) => reader.ReadToEndAsync(token),
            static (reader, token) => reader.ReadToEndAsync(token),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CommandResult> RunStreamedAsync(
        string command,
        IReadOnlyList<string> args,
        TextWriter stdOutSink,
        TextWriter stdErrSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdOutSink);
        ArgumentNullException.ThrowIfNull(stdErrSink);

        return RunRedirectedAsync(
            command,
            args,
            (reader, token) => PumpAsync(reader, stdOutSink, token),
            (reader, token) => PumpAsync(reader, stdErrSink, token),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CommandResult> RunCapturedWithInputAsync(
        string command,
        IReadOnlyList<string> args,
        string standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardInput);

        return RunRedirectedAsync(
            command,
            args,
            static (reader, token) => reader.ReadToEndAsync(token),
            static (reader, token) => reader.ReadToEndAsync(token),
            cancellationToken,
            standardInput);
    }

    /// <summary>
    /// Runs a command with both output streams redirected, draining each one through the supplied
    /// reader. Capture and streaming differ only in how a stream is drained, so everything else —
    /// start, stdin close, cancellation kill, reap — lives here once.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">The arguments to pass to it.</param>
    /// <param name="readStdOut">Drains the child's stdout and returns everything it read.</param>
    /// <param name="readStdErr">Drains the child's stderr and returns everything it read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="standardInput">
    /// Text to write to the child's stdin before closing it, or <see langword="null"/> for no
    /// payload. Either way, stdin is closed so a child that reads it sees EOF and exits instead of
    /// hanging forever waiting for input this non-interactive capture will never otherwise provide.
    /// </param>
    private static async Task<CommandResult> RunRedirectedAsync(
        string command,
        IReadOnlyList<string> args,
        Func<StreamReader, CancellationToken, Task<string>> readStdOut,
        Func<StreamReader, CancellationToken, Task<string>> readStdErr,
        CancellationToken cancellationToken,
        string? standardInput = null)
    {
        var psi = CreateStartInfo(command, args, redirectStreams: true);

        using var process = StartProcess(psi, command);

#pragma warning disable CA2016 // CancellationToken is handled via registration below
        var registration = cancellationToken.Register(static state => KillProcess((Process)state!), process);
#pragma warning restore CA2016
        try
        {
            // CRITICAL: drain both streams concurrently — sequential reads deadlock on large output
            var stdOutTask = readStdOut(process.StandardOutput, cancellationToken);
            var stdErrTask = readStdErr(process.StandardError, cancellationToken);

            // CRITICAL: start draining before writing stdin — a child that fills its stdout pipe
            // buffer before it has finished consuming stdin would otherwise deadlock: it blocks on
            // its stdout write while we block on our stdin write, with nothing on either side
            // reading. Writing after the drain tasks are already pumping avoids that.
            //
            // Write the payload (if any) and close stdin either way — even if the write throws
            // (cancellation, or the child already closed its end) — so a child that reads it sees
            // EOF and exits instead of hanging forever waiting for input. The registration above
            // (and the catch below) cover cancellation while this write is in flight, same as they
            // cover the drain/wait that follows.
            try
            {
                if (standardInput is not null)
                {
                    try
                    {
                        await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // Broken pipe: the child exited, crashed, or otherwise stopped reading stdin
                        // before we finished writing. That is normal behaviour for a child process —
                        // e.g. a hook script with a syntax error exits immediately without touching
                        // stdin — not a failure of this run. Swallow it here so the drain below still
                        // captures whatever the child actually wrote and its real exit code; do not
                        // widen this to catch anything else, a genuine stdout/stderr drain failure
                        // must still surface.
                    }
                }
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // Same broken-pipe reason as the write above: closing an already-broken pipe can
                    // itself throw on flush. Still non-fatal for the same reason — swallow it so the
                    // child's real output and exit code are what this call reports.
                }
            }

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
            await KillAndReapAsync(process, ReapGracePeriod).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Builds the start info every run shares, optionally with the streams redirected.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">The arguments to pass to it.</param>
    /// <param name="redirectStreams">
    /// <see langword="true"/> to redirect stdin/stdout/stderr as UTF-8; <see langword="false"/> to
    /// let the child inherit the console, as the passthrough path needs.
    /// </param>
    private static ProcessStartInfo CreateStartInfo(
        string command,
        IReadOnlyList<string> args,
        bool redirectStreams)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false
        };

        if (redirectStreams)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    /// <summary>Copies a stream to a sink line by line, returning everything it copied.</summary>
    /// <param name="reader">The child process stream to read.</param>
    /// <param name="sink">The destination to echo each line to as it arrives.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<string> PumpAsync(
        StreamReader reader,
        TextWriter sink,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        // TextWriter.WriteLineAsync terminates lines with sink.NewLine, which is a settable
        // per-writer property. Captured once so the accumulated text mirrors exactly what was
        // written to the sink, rather than always using Environment.NewLine like
        // StringBuilder.AppendLine would.
        var newLine = sink.NewLine;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // Flush per line: the whole point of streaming is that the user sees progress, and a
            // buffered sink would defeat that on a long-running publish.
            await sink.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await sink.FlushAsync(cancellationToken).ConfigureAwait(false);
            builder.Append(line).Append(newLine);
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public async Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var psi = CreateStartInfo(command, args, redirectStreams: false);

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
            await KillAndReapAsync(process, ReapGracePeriod).ConfigureAwait(false);
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

    /// <summary>Kills the process tree and waits, at most <paramref name="gracePeriod"/>, for it to be reaped.</summary>
    /// <param name="process">The process whose tree is torn down.</param>
    /// <param name="gracePeriod">
    /// How long to wait for the child to be reaped. Always <see cref="ReapGracePeriod"/> in
    /// production; a parameter so the grace-elapsed path is reachable from a test.
    /// </param>
    private static async Task KillAndReapAsync(Process process, TimeSpan gracePeriod)
    {
        KillProcess(process);

        using var grace = new CancellationTokenSource(gracePeriod);
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

    [ExcludeFromCodeCoverage(Justification =
        "Windows-only backstop, reached solely via the OperatingSystem.IsWindows() guard in KillProcess. " +
        "Coverage is measured on Linux, where every line here is unreachable and would otherwise be " +
        "reported as permanently uncovered. The guard at the call site is still measured.")]
    private static void TryKillTreeWithTaskkill(int processId)
    {
        // Stryker disable all : Windows-only backstop, reached solely via the OperatingSystem.IsWindows()
        // guard in KillProcess. CI and local mutation runs execute on Linux, so every mutant in this
        // method is unreachable there and would sit in the report as permanent, unkillable noise. The
        // guard itself at the call site is still mutated and covered.
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

        // Stryker restore all
    }
}

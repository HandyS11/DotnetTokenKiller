using System.Diagnostics;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>Spawns one process and times it end to end, for the out-of-process harnesses.</summary>
internal static class TimedProcess
{
    /// <summary>Runs a process to exit and returns its timing, exit code and output.</summary>
    /// <param name="fileName">The executable to spawn.</param>
    /// <param name="arguments">Its arguments, passed verbatim.</param>
    /// <param name="standardInput">Text written to its stdin, or <see langword="null"/> for none.
    /// Stdin is closed either way, so a child that reads it sees EOF instead of waiting forever.</param>
    /// <param name="prependToPath">
    /// A directory to put first on this child's <c>PATH</c> only, or <see langword="null"/>. Set on
    /// the child's start info, never on this process: the harness runs under <c>dotnet run</c> and
    /// must keep resolving the real SDK.
    /// </param>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    internal static async Task<TimedRun> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        string? prependToPath = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // dtk rewrites its ✓/✗/⚠ glyphs when NO_COLOR is set, so a developer's shell setting would
        // otherwise change the very output the harness validates samples against.
        info.Environment.Remove("NO_COLOR");

        if (prependToPath is not null)
        {
            var path = info.Environment.TryGetValue("PATH", out var existing) ? existing : null;
            info.Environment["PATH"] = prependToPath + Path.PathSeparator + path;
        }

        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        // Start draining before writing stdin: a child that fills its stdout pipe while we are still
        // writing its stdin would otherwise block both sides forever.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return new TimedRun(
            elapsed,
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }
}

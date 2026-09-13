using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>What a finished child process wrote and how it exited.</summary>
/// <param name="Stdout">Everything written to standard output.</param>
/// <param name="Stderr">Everything written to standard error.</param>
/// <param name="ExitCode">The exit code.</param>
internal sealed record ProcessOutput(string Stdout, string Stderr, int ExitCode);

/// <summary>Runs a child process to completion with piped standard streams.</summary>
internal static class ParityProcess
{
    /// <summary>
    /// Starts <paramref name="startInfo"/>, writes <paramref name="stdin"/> and closes standard input, and
    /// returns both output streams once the process exits. The output streams are drained before input is
    /// written: a child that writes while its input is still arriving would otherwise fill its output pipe
    /// (about 64 KB on Linux) and block, while this side blocks writing its input.
    /// </summary>
    /// <param name="startInfo">The process to start; its stream settings are overwritten.</param>
    /// <param name="stdin">Text for standard input, or <see langword="null"/> for an empty, closed input.</param>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    internal static async Task<ProcessOutput> RunAsync(ProcessStartInfo startInfo, string? stdin)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.StandardInputEncoding = Encoding.UTF8;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();

        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        return new ProcessOutput(await stdout, await stderr, process.ExitCode);
    }
}

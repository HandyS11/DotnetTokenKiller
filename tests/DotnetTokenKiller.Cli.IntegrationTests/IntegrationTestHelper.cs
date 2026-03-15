using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Cli.IntegrationTests;

internal static class IntegrationTestHelper
{
    private static readonly string DllPath =
        Path.Combine(AppContext.BaseDirectory, "dtk.dll");

    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    internal static string SamplePath(string project) =>
        Path.Combine(RepoRoot, "samples", project);

    internal static Task<(string Output, int ExitCode)> RunDtkAsync(params string[] args) =>
        RunProcessAsync("dotnet", [DllPath, .. args]);

    internal static Task<(string Output, int ExitCode)> RunDotnetAsync(params string[] args) =>
        RunProcessAsync("dotnet", args);

    internal static double CalculateSavings(string rawOutput, string filteredOutput)
    {
        var inputTokens = rawOutput.Length / 4;
        var outputTokens = filteredOutput.Length / 4;
        if (inputTokens == 0) return 0;
        return (inputTokens - outputTokens) * 100.0 / inputTokens;
    }

    private static async Task<(string Output, int ExitCode)> RunProcessAsync(
        string executable, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{executable}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (stdout + stderr, process.ExitCode);
    }
}

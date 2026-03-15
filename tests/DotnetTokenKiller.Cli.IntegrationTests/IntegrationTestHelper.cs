using System.Diagnostics;

namespace DotnetTokenKiller.Cli.IntegrationTests;

internal static class IntegrationTestHelper
{
    private static readonly string _dllPath =
        Path.Combine(AppContext.BaseDirectory, "dtk.dll");

    private static readonly string _repoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    internal static string SamplePath(string project) =>
        Path.Combine(_repoRoot, "sample", project);

    internal static Task<(string Output, int ExitCode)> RunDtkAsync(params string[] args) =>
        RunProcessAsync("dotnet", [_dllPath, .. args]);

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

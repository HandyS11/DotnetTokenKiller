using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

internal static class IntegrationTestHelper
{
    private static readonly string DllPath =
        Path.Combine(AppContext.BaseDirectory, "dtk.dll");

    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>Isolated temp directory for test data so that integration tests
    /// never pollute the user's real tracking database, config, or tee logs.</summary>
    private static readonly string TestDataDir =
        Path.Combine(Path.GetTempPath(), $"dtk-tests-{Guid.NewGuid():N}");

    static IntegrationTestHelper()
    {
        // Best-effort cleanup: delete the isolated temp directory when the test
        // runner process exits so repeated local/CI runs don't accumulate dtk-tests-*
        // directories under the system temp folder.
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try
            {
                if (Directory.Exists(TestDataDir))
                {
                    Directory.Delete(TestDataDir, true);
                }
            }
            catch (IOException)
            {
                /* best-effort */
            }
            catch (UnauthorizedAccessException)
            {
                /* best-effort */
            }
        };
    }

    internal static string SamplePath(string project)
    {
        return Path.Combine(RepoRoot, "samples", project);
    }

    internal static Task<(string Output, int ExitCode)> RunDtkAsync(params string[] args)
    {
        return RunProcessAsync("dotnet", [DllPath, .. args]);
    }

    internal static Task<(string Output, int ExitCode)> RunDotnetAsync(params string[] args)
    {
        return RunProcessAsync("dotnet", args);
    }

    internal static double CalculateSavings(string rawOutput, string filteredOutput)
    {
        var inputTokens = rawOutput.Length / 4;
        var outputTokens = filteredOutput.Length / 4;
        if (inputTokens == 0)
        {
            return 0;
        }

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
            Environment =
            {
                // Prevent MSBuild node reuse and server mode to avoid file-lock
                // contention between sequential test runs on shared sample projects.
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",

                // Redirect all dtk persistence to an isolated temp directory so that
                // integration tests never write into the user's real database/config.
                ["DTK_DB_PATH"] = Path.Combine(TestDataDir, "tracking.db"),
                ["DTK_TEE_DIR"] = Path.Combine(TestDataDir, "tee"),
                ["DTK_CONFIG_PATH"] = Path.Combine(TestDataDir, "config.json")
            }
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

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

using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

internal static class IntegrationTestHelper
{
    /// <summary>Per-test timeout (milliseconds) for integration tests that shell out to a
    /// nested <c>dotnet</c> build/test on a sample project. xunit runs collections in parallel
    /// up to the logical-CPU count, so several of these heavy subprocesses run at once; on a
    /// 2-core CI runner under coverage instrumentation (e.g. the SonarQube job) a tight 60s cap
    /// occasionally tripped even though the work completes. 3 minutes leaves headroom for that
    /// contention while still failing fast on a genuine hang, well inside the CI job timeout.</summary>
    internal const int DefaultTimeoutMs = 180_000;

    private static readonly string DllPath =
        Path.Combine(AppContext.BaseDirectory, "dtk.dll");

    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>Root temp directory shared by all invocations in this test run.
    /// Each <c>RunProcessAsync</c> call creates a unique subdirectory so
    /// that parallel test collections never share SQLite databases or tee logs.</summary>
    private static readonly string TestDataRoot =
        Path.Combine(Path.GetTempPath(), $"dtk-tests-{Guid.NewGuid():N}");

    static IntegrationTestHelper()
    {
        // Best-effort cleanup: delete the root temp directory when the test
        // runner process exits so repeated local/CI runs don't accumulate dtk-tests-*
        // directories under the system temp folder.
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try
            {
                if (Directory.Exists(TestDataRoot))
                {
                    Directory.Delete(TestDataRoot, true);
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

    /// <summary>Runs dtk and also returns the isolated tracking-database path it wrote to,
    /// so a test can assert on what was recorded.</summary>
    /// <param name="args">The arguments to pass to dtk.</param>
    internal static async Task<(string Output, int ExitCode, string DbPath)> RunDtkWithDbAsync(
        params string[] args)
    {
        var isolatedDir = Path.Combine(TestDataRoot, Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(isolatedDir, "tracking.db");
        var (output, exitCode) = await RunProcessAsync("dotnet", [DllPath, .. args], isolatedDir);
        return (output, exitCode, dbPath);
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

    private static Task<(string Output, int ExitCode)> RunProcessAsync(
        string executable, IEnumerable<string> args)
    {
        // Each invocation gets its own subdirectory so parallel test collections
        // never share SQLite databases, tee logs, or config files.
        return RunProcessAsync(executable, args, Path.Combine(TestDataRoot, Guid.NewGuid().ToString("N")));
    }

    private static async Task<(string Output, int ExitCode)> RunProcessAsync(
        string executable, IEnumerable<string> args, string isolatedDir)
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
                ["DTK_DB_PATH"] = Path.Combine(isolatedDir, "tracking.db"),
                ["DTK_TEE_DIR"] = Path.Combine(isolatedDir, "tee"),
                ["DTK_CONFIG_PATH"] = Path.Combine(isolatedDir, "config.json")
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

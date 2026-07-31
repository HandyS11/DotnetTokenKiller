using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using DotnetTokenKiller.Domain.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Proves the durability guarantee out of process: a dtk process that is actually killed — not
/// merely a session that is never finalized — still leaves a readable tee log behind.
/// </summary>
public sealed class TeeDurabilityTests
{
    /// <summary>A dtk process killed mid-run leaves a log with a running status and no exit code.</summary>
    [Fact]
    public async Task KilledRun_LeavesAReadableLog()
    {
        if (OperatingSystem.IsWindows())
        {
            // Kill(entireProcessTree) on Windows is the case ProcessCommandRunner already needs a
            // taskkill backstop for; the guarantee under test is POSIX signal behaviour.
            return;
        }

        var isolatedDir = Path.Combine(Path.GetTempPath(), $"dtk-durability-{Guid.NewGuid():N}");
        var (process, teeDir) = IntegrationTestHelper.StartDetached(
            ["dotnet", "build", "samples/SampleApp.Warnings/SampleApp.Warnings.csproj"], isolatedDir);

        try
        {
            // The log exists from BeginAsync, before the child process even starts, so this waits
            // on the guarantee itself rather than on the build producing output.
            var logPath = await WaitForLogAsync(teeDir, TimeSpan.FromSeconds(30));
            logPath.Should().NotBeNull("dtk must open the tee log before running the command");

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();

            var text = await File.ReadAllTextAsync(logPath);
            TeeLogHeader.TryParse(text, out var header).Should().BeTrue();
            header.Status.Should().Be(TeeLogStatus.Running);
            header.CommandLine.Should().Contain("build");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
            if (Directory.Exists(isolatedDir))
            {
                Directory.Delete(isolatedDir, true);
            }
        }
    }

    private static async Task<string?> WaitForLogAsync(string teeDir, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(teeDir))
            {
                var files = Directory.GetFiles(teeDir, "*.log");
                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            await Task.Delay(50);
        }

        return null;
    }
}

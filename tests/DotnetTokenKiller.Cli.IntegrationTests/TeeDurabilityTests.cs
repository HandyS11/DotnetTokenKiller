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
            // BeginAsync writes the log's header before the child process even starts, so this waits
            // on the guarantee itself rather than on the build producing output.
            var logPath = await WaitForLogAsync(teeDir, TimeSpan.FromSeconds(30));
            logPath.Should().NotBeNull("dtk must write the tee log's header before running the command");

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

    /// <summary>Waits for a tee log whose header is complete.</summary>
    /// <remarks>
    /// Not merely for the file: BeginAsync creates it and then writes the header, so it is empty for a moment. A kill
    /// in that moment, before the run has started, left an empty log and failed this test under the SonarQube job's
    /// coverage instrumentation, which slows dtk down; <c>dtk log</c> skips such a file.
    /// </remarks>
    /// <param name="teeDir">The tee directory the run writes to.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The log's path, or <see langword="null"/> when none had a complete header in time.</returns>
    private static async Task<string?> WaitForLogAsync(string teeDir, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(teeDir))
            {
                foreach (var file in Directory.GetFiles(teeDir, "*.log"))
                {
                    if (TeeLogHeader.TryParse(await File.ReadAllTextAsync(file), out _))
                    {
                        return file;
                    }
                }
            }

            await Task.Delay(50);
        }

        return null;
    }
}

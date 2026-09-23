using System.Diagnostics;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Sends real signals to a dtk process wrapping a fake <c>dotnet</c> script, proving that Ctrl+C and
/// SIGTERM reach the running command: the child is stopped, its log is finalized, and dtk reports an
/// exit code instead of dying mid-run.
/// </summary>
/// <remarks>
/// A signal sent with <c>kill</c> reaches only the process named, unlike Ctrl+C at a terminal, which
/// the child receives too; the tests choose which processes get it to cover both cases.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CancellationTests : IDisposable
{
    private static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(30);

    private readonly string _dir = IntegrationTestHelper.NewIsolatedDir();
    private readonly string _pidFile;
    private readonly List<int> _childPids = [];

    public CancellationTests()
    {
        Directory.CreateDirectory(_dir);
        _pidFile = Path.Combine(_dir, "child.pid");
    }

    public void Dispose()
    {
        foreach (var pid in _childPids)
        {
            SendSignal("KILL", pid);
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [UnixFact]
    public async Task Interrupt_ChildThatExitsOnItsOwn_ReportsTheChildsExitCodeAndFinalizesTheLog()
    {
        // Ctrl+C at a terminal: dtk and the child both receive SIGINT. The child stops by itself
        // within the grace period, so dtk drains its output and reports its exit code, not 130.
        const string script = """
            trap 'kill $SLEEPER 2>/dev/null; echo interrupted; exit 3' INT
            sleep 60 >/dev/null 2>&1 &
            SLEEPER=$!
            wait
            """;
        var (dtk, teeDir) = await StartAsync(script, "build");
        using (dtk)
        {
            var stopwatch = Stopwatch.StartNew();
            SendSignal("INT", dtk.Id, _childPids[0]);
            await WaitForExitAsync(dtk);

            dtk.ExitCode.Should().Be(3, "the child's own exit code wins when it exits within the grace period");
            stopwatch.Elapsed.Should().BeLessThan(RunCancellation.InterruptGracePeriod);
            (await ReadLogHeaderAsync(teeDir)).ExitCode.Should().Be(3);
            Directory.EnumerateFiles(Path.Combine(_dir, "tracking.db.pending"), "*.json").Should()
                .ContainSingle("the interrupted run is recorded like any other");
        }
    }

    [UnixTheory]
    [InlineData("build")]
    [InlineData("msbuild")]
    public async Task Interrupt_ChildThatKeepsRunning_IsKilledAfterTheGracePeriod(string subcommand)
    {
        // SIGINT sent to dtk alone: the child never learns of it, so dtk kills its tree once the
        // grace period elapses. Covers the filtered path (build) and the passthrough path (msbuild).
        var (dtk, teeDir) = await StartAsync("exec sleep 60", subcommand);
        using (dtk)
        {
            SendSignal("INT", dtk.Id);
            await WaitForExitAsync(dtk);

            dtk.ExitCode.Should().Be(ExitCodes.Cancelled);
            IsAlive(_childPids[0]).Should().BeFalse("dtk kills the child's tree before it exits");
            (await ReadLogHeaderAsync(teeDir)).ExitCode.Should().Be(ExitCodes.Cancelled);
        }
    }

    [UnixFact]
    public async Task Terminate_KillsTheChildAtOnce()
    {
        var (dtk, teeDir) = await StartAsync("exec sleep 60", "build");
        using (dtk)
        {
            var stopwatch = Stopwatch.StartNew();
            SendSignal("TERM", dtk.Id);
            await WaitForExitAsync(dtk);

            dtk.ExitCode.Should().Be(ExitCodes.Cancelled);
            stopwatch.Elapsed.Should().BeLessThan(RunCancellation.InterruptGracePeriod,
                "SIGTERM does not wait for the child");
            IsAlive(_childPids[0]).Should().BeFalse();
            (await ReadLogHeaderAsync(teeDir)).ExitCode.Should().Be(ExitCodes.Cancelled);
        }
    }

    [UnixFact]
    public async Task SecondInterrupt_TerminatesAtOnce()
    {
        var (dtk, _) = await StartAsync("exec sleep 60", "build");
        using (dtk)
        {
            var stopwatch = Stopwatch.StartNew();
            SendSignal("INT", dtk.Id);
            await Task.Delay(200);
            SendSignal("INT", dtk.Id);
            await WaitForExitAsync(dtk);

            stopwatch.Elapsed.Should().BeLessThan(RunCancellation.InterruptGracePeriod,
                "a second Ctrl+C must not wait out the grace period");
        }
    }

    /// <summary>
    /// Starts <c>dtk dotnet &lt;subcommand&gt;</c> with a fake <c>dotnet</c> first on <c>PATH</c>, and waits
    /// until the fake has written its output and its pid.
    /// </summary>
    /// <param name="body">Shell script run after the fake has printed its output and recorded its pid.</param>
    /// <param name="subcommand">The dotnet subcommand to wrap.</param>
    /// <exception cref="TimeoutException">The fake never started.</exception>
    private async Task<(Process Dtk, string TeeDir)> StartAsync(string body, string subcommand)
    {
        // Every run is kept, and the fake prints more than the 500-byte floor under which a log is dropped.
        var (_, configExit) = await IntegrationTestHelper.RunDtkInDirAsync(_dir, "config", "set", "tee.mode", "Always");
        configExit.Should().Be(0);
        var journal = Path.Combine(_dir, "tracking.db.pending");
        if (Directory.Exists(journal))
        {
            Directory.Delete(journal, recursive: true); // `config set` itself is a tracked command
        }

        var binDir = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(binDir);
        var fake = Path.Combine(binDir, "dotnet");
        await File.WriteAllTextAsync(fake, $"""
            #!/bin/sh
            i=0
            while [ $i -lt 20 ]; do echo "fake output line $i, long enough to clear the tee floor"; i=$((i+1)); done
            echo $$ > '{_pidFile}'
            {body}

            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var dtk = StartDtk(subcommand, binDir);
        var teeDir = Path.Combine(_dir, "tee");

        var deadline = DateTime.UtcNow + StartLimit;
        while (!File.Exists(_pidFile) || new FileInfo(_pidFile).Length == 0)
        {
            if (DateTime.UtcNow > deadline || dtk.HasExited)
            {
                dtk.Kill(entireProcessTree: true);
                throw new TimeoutException($"the fake dotnet never started: {await dtk.StandardError.ReadToEndAsync()}");
            }

            await Task.Delay(50);
        }

        _childPids.Add(int.Parse((await File.ReadAllTextAsync(_pidFile)).Trim(),
            System.Globalization.CultureInfo.InvariantCulture));

        // Let dtk's pumps copy the fake's output before the signal arrives.
        await Task.Delay(500);
        return (dtk, teeDir);
    }

    /// <summary>Starts dtk with the fake's directory first on <c>PATH</c> and its state isolated in the test directory.</summary>
    /// <remarks>
    /// Not through <c>dotnet dtk.dll</c>, the other integration tests' launcher: on Unix,
    /// <see cref="Process"/> looks for a bare command name in the running executable's own directory
    /// before <c>PATH</c>, so dtk hosted by the dotnet muxer would always find the real <c>dotnet</c>
    /// beside it. An apphost or the Native AOT binary (<c>DTK_TEST_BINARY</c>) has no such neighbour.
    /// </remarks>
    /// <param name="subcommand">The dotnet subcommand to wrap.</param>
    /// <param name="binDir">The directory holding the fake <c>dotnet</c>.</param>
    /// <exception cref="InvalidOperationException">dtk could not be started.</exception>
    private Process StartDtk(string subcommand, string binDir)
    {
        var psi = new ProcessStartInfo(ResolveDtkExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["PATH"] = $"{binDir}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}",
                ["DTK_DB_PATH"] = Path.Combine(_dir, "tracking.db"),
                ["DTK_TEE_DIR"] = Path.Combine(_dir, "tee"),
                ["DTK_CONFIG_PATH"] = Path.Combine(_dir, "config.json")
            }
        };
        psi.ArgumentList.Add("dotnet");
        psi.ArgumentList.Add(subcommand);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dtk");

        // Drained so a chatty run can never block on a full pipe; nothing here asserts on it.
        _ = process.StandardOutput.ReadToEndAsync();
        return process;
    }

    /// <summary>The Native AOT binary under test when one is given, otherwise the CLI project's own apphost.</summary>
    private static string ResolveDtkExecutable()
    {
        var testBinary = Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable);
        if (!string.IsNullOrWhiteSpace(testBinary))
        {
            return testBinary.Trim();
        }

        // The test project copies dtk.dll but not the apphost, so use the one the CLI project built
        // for the same configuration: bin/<Configuration>/<TargetFramework> in both projects.
        var outputDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var apphost = Path.Combine(
            outputDir.Parent!.Parent!.Parent!.Parent!.Parent!.FullName,
            "src", "DotnetTokenKiller.Cli", "bin", outputDir.Parent.Name, outputDir.Name, "dtk");
        File.Exists(apphost).Should().BeTrue($"the CLI project's apphost is expected at {apphost}");
        return apphost;
    }

    private static async Task WaitForExitAsync(Process dtk)
    {
        using var limit = new CancellationTokenSource(ExitLimit);
        try
        {
            await dtk.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            dtk.Kill(entireProcessTree: true);
            throw new TimeoutException("dtk did not exit after the signal");
        }
    }

    private static async Task<TeeLogHeader> ReadLogHeaderAsync(string teeDir)
    {
        var log = Directory.GetFiles(teeDir, "*.log").Should().ContainSingle().Subject;
        TeeLogHeader.TryParse(await File.ReadAllTextAsync(log), out var header).Should().BeTrue();
        return header;
    }

    private static void SendSignal(string signal, params int[] pids)
    {
        var psi = new ProcessStartInfo("kill") { UseShellExecute = false, RedirectStandardError = true };
        psi.ArgumentList.Add($"-{signal}");
        foreach (var pid in pids)
        {
            psi.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using var kill = Process.Start(psi)!;
        kill.StandardError.ReadToEnd();
        kill.WaitForExit();
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

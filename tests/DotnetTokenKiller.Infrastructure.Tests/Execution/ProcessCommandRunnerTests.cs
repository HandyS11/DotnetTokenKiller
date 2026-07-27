using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using DotnetTokenKiller.Infrastructure.Execution;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Execution;

public sealed class ProcessCommandRunnerTests
{
    private readonly ProcessCommandRunner _sut = new();

    private static (string command, string[] args) LongRunningCommand()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("ping", ["-n", "5", "10.255.255.1"]) // non-routable: each ping times out ~4 s
            : ("sleep", ["30"]);
    }

    private static (string command, string[] args) EchoCommand(string message)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd", ["/c", "echo", message])
            : ("echo", [message]);
    }

    [Fact]
    public async Task RunCapturedAsync_BasicCommand_CapturesStdOut()
    {
        var (cmd, args) = EchoCommand("hello world");
        var result = await _sut.RunCapturedAsync(cmd, args);

        result.StdOut.Should().Contain("hello world");
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunPassthroughAsync_BasicCommand_ReturnsZeroExitCode()
    {
        var (cmd, args) = EchoCommand("hello");
        var exitCode = await _sut.RunPassthroughAsync(cmd, args);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunCapturedAsync_Cancellation_KillsRunningProcess()
    {
        // Covers KillProcess when process has not exited (lines 90-93)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var (cmd, args) = LongRunningCommand();
        var token = cts.Token;

        var act = async () => await _sut.RunCapturedAsync(cmd, args, token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunPassthroughAsync_Cancellation_KillsRunningProcess()
    {
        // Covers KillProcess for RunPassthroughAsync path
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var (cmd, args) = LongRunningCommand();
        var token = cts.Token;

        var act = async () => await _sut.RunPassthroughAsync(cmd, args, token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void KillProcess_WithUnstartedProcess_CatchesInvalidOperationException()
    {
        var killMethod = typeof(ProcessCommandRunner)
            .GetMethod("KillProcess", BindingFlags.NonPublic | BindingFlags.Static)!;

        using var unstarted = new Process();
        killMethod.Invoke(null, [unstarted]).Should().BeNull(); // void method returns null on success
    }

    [Fact]
    public async Task KillProcess_WithAlreadyExitedProcess_SkipsKill()
    {
        // Covers false branch of !process.HasExited (line 90):
        // when the process has already exited, Kill is not called.
        var killMethod = typeof(ProcessCommandRunner)
            .GetMethod("KillProcess", BindingFlags.NonPublic | BindingFlags.Static)!;

        var (echoCmd, echoArgs) = EchoCommand("test");
        var psi = new ProcessStartInfo(echoCmd)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        foreach (var arg in echoArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using var exited = Process.Start(psi)!;
        await exited.WaitForExitAsync();
        killMethod.Invoke(null, [exited]).Should().BeNull(); // void method returns null on success
    }

    [Fact]
    public async Task RunCapturedAsync_NullArgs_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.RunCapturedAsync("echo", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunPassthroughAsync_NullArgs_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.RunPassthroughAsync("echo", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunCapturedAsync_MissingExecutable_ThrowsFriendlyErrorNamingTheCommandAsync()
    {
        const string missing = "definitely-not-a-real-binary-xyz";

        var act = async () => await _sut.RunCapturedAsync(missing, []);

        // A raw Win32Exception reports only the OS-level reason and never names the missing
        // command, so the runner must surface a message that an agent can act on.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{missing}*");
    }

    [Fact]
    public async Task RunPassthroughAsync_MissingExecutable_ThrowsFriendlyErrorNamingTheCommandAsync()
    {
        const string missing = "definitely-not-a-real-binary-xyz";

        var act = async () => await _sut.RunPassthroughAsync(missing, []);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{missing}*");
    }

    private static (string command, string[] args) StdErrCommand()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd", ["/c", "echo stderr_content 1>&2"])
            : ("sh", ["-c", "echo stderr_content >&2"]);
    }

    private static (string command, string[] args) ExitCodeCommand(int code)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd", ["/c", $"exit {code}"])
            : ("sh", ["-c", $"exit {code}"]);
    }

    [Fact]
    public async Task RunCapturedAsync_CommandWritesToStdErr_CapturesStdErr()
    {
        var (cmd, args) = StdErrCommand();

        var result = await _sut.RunCapturedAsync(cmd, args);

        result.StdErr.Should().Contain("stderr_content");
    }

    [Fact]
    public async Task RunCapturedAsync_NonZeroExitCode_ReturnsCorrectExitCode()
    {
        var (cmd, args) = ExitCodeCommand(42);

        var result = await _sut.RunCapturedAsync(cmd, args);

        result.ExitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunPassthroughAsync_NonZeroExitCode_ReturnsCorrectExitCode()
    {
        var (cmd, args) = ExitCodeCommand(7);

        var exitCode = await _sut.RunPassthroughAsync(cmd, args);

        exitCode.Should().Be(7);
    }

    private static (string command, string[] args) PrintCliLanguageCommand()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd", ["/c", "echo %DOTNET_CLI_UI_LANGUAGE%"])
            : ("printenv", ["DOTNET_CLI_UI_LANGUAGE"]);
    }

    private static (string command, string[] args) StdinDrainingCommand()
    {
        // Both read stdin until EOF, then exit — they block forever if stdin stays open/inherited.
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("sort", [])
            : ("cat", []);
    }

    [Fact]
    public async Task RunCapturedAsync_ChildReadingStdin_TerminatesInsteadOfHangingAsync()
    {
        var (cmd, args) = StdinDrainingCommand();

        var task = _sut.RunCapturedAsync(cmd, args, CancellationToken.None);
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        done.Should().Be(task); // with stdin redirected+closed, the child sees EOF and exits immediately
        (await task).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunCapturedAsync_SetsEnglishCliLanguageOnChildAsync()
    {
        // Child echoes the env var back. If unset, Windows echoes the literal
        // %DOTNET_CLI_UI_LANGUAGE% and Linux prints nothing — both fail the assertion.
        var (cmd, args) = PrintCliLanguageCommand();

        var result = await _sut.RunCapturedAsync(cmd, args, CancellationToken.None);

        result.StdOut.Trim().Should().Be("en");
    }

    private static (string command, string[] args) MegabyteBothStreamsCommand()
    {
        // Emit ~1 MB on stdout AND ~1 MB on stderr *concurrently* (interleaved). The OS pipe
        // buffer is only tens of KB, so a runner that drains the streams sequentially deadlocks
        // in EITHER read order: whichever stream it reads first, the other fills its buffer and
        // blocks the child. A stdout-then-stderr sequential writer would not catch that.
        // Linux runs both pipelines as background jobs and waits. Windows interleaves 1 KB chunks.
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-Command", "for ($i = 0; $i -lt 1024; $i++) { [Console]::Out.Write('a' * 1024); [Console]::Error.Write('b' * 1024) }"])
            : ("sh", ["-c", "head -c 1048576 /dev/zero | tr '\\0' 'a' & head -c 1048576 /dev/zero | tr '\\0' 'b' 1>&2 & wait"]);
    }

    private static (string command, string[] args) StartedThenDelayedMarkerCommand(
        string startedPath,
        string donePath,
        int sleepSeconds)
    {
        // Create the "started" marker immediately, then sleep, then create the "done" marker. The
        // started marker lets the test cancel only once the child is provably running (immune to CI
        // load skewing a wall-clock delay); the done marker appears only if the process or a
        // descendant survives cancellation — a truly killed tree never reaches that step.
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-Command", $"New-Item -ItemType File -Force -Path '{startedPath}' | Out-Null; Start-Sleep -Seconds {sleepSeconds}; New-Item -ItemType File -Force -Path '{donePath}' | Out-Null"])
            : ("sh", ["-c", $"touch '{startedPath}'; sleep {sleepSeconds}; touch '{donePath}'"]);
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!File.Exists(path))
        {
            if (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"File '{path}' was not created within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task RunCapturedAsync_MegabyteOnBothStreams_DoesNotDeadlockAsync()
    {
        var (cmd, args) = MegabyteBothStreamsCommand();

        var task = _sut.RunCapturedAsync(cmd, args, CancellationToken.None);
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));

        done.Should().Be(task); // a sequential-read deadlock would leave the task unfinished at the timeout
        var result = await task;
        result.StdOut.Length.Should().BeGreaterThan(1_000_000);
        result.StdErr.Length.Should().BeGreaterThan(1_000_000);
    }

    [Fact]
    public async Task RunCapturedAsync_Cancellation_ActuallyTerminatesTheChildAsync()
    {
        // Runs on every platform: RunCapturedAsync now kills the whole child tree and waits for it
        // to be reaped on the cancellation path, so a killed child never reaches the marker step.
        const int childSleepSeconds = 5;
        var dir = Path.Combine(Path.GetTempPath(), $"dtk-kill-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var started = Path.Combine(dir, "started");
        var done = Path.Combine(dir, "done");
        try
        {
            var (cmd, args) = StartedThenDelayedMarkerCommand(started, done, childSleepSeconds);
            using var cts = new CancellationTokenSource();

            // Cancel only once the child is provably running. A fixed wall-clock delay
            // (e.g. CancellationTokenSource(200ms)) is load-sensitive: on a starved CI ThreadPool the
            // cancel timer can fire after a short child has already exited, so nothing gets cancelled
            // and no OperationCanceledException is thrown. Gating the cancel on the "started" marker
            // makes the test deterministic regardless of scheduling latency.
            var cancelWhenStarted = Task.Run(async () =>
            {
                await WaitForFileAsync(started, TimeSpan.FromSeconds(30));
                await cts.CancelAsync();
            });

            var act = async () => await _sut.RunCapturedAsync(cmd, args, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            await cancelWhenStarted; // surface any failure (e.g. the child never started) from the canceller

            // Wait past the child's sleep. A leaked process would create the done marker during this
            // window, whereas a properly killed process tree never reaches the marker-creating step.
            await Task.Delay(TimeSpan.FromSeconds(childSleepSeconds + 2));
            File.Exists(done).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunStreamedAsync_EchoesStdoutToTheSink_AndReturnsTheSameText()
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--version"], stdOut, stdErr);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().NotBeEmpty();
        stdOut.ToString().Should().Be(result.StdOut);
    }

    [Fact]
    public async Task RunStreamedAsync_ReturnsNonZeroExitCode_ForAFailingCommand()
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--this-option-does-not-exist"], stdOut, stdErr);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task RunStreamedAsync_Throws_WhenTheCommandCannotBeStarted()
    {
        var sut = new ProcessCommandRunner();

        var act = async () => await sut.RunStreamedAsync(
            "dtk-no-such-binary-exists", [], TextWriter.Null, TextWriter.Null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunStreamedAsync_EchoesEveryLineOfMultiLineOutput()
    {
        var stdOut = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--info"], stdOut, TextWriter.Null);

        result.ExitCode.Should().Be(0);
        stdOut.ToString().Should().Contain(".NET SDK");
        stdOut.ToString().Split('\n').Length.Should().BeGreaterThan(5);
    }
}

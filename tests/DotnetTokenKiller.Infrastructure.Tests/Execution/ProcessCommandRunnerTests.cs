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

    [Fact]
    public async Task RunCapturedAsync_SetsEnglishCliLanguageOnChildAsync()
    {
        // Child echoes the env var back (Linux only via printenv)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var result = await _sut.RunCapturedAsync("printenv", ["DOTNET_CLI_UI_LANGUAGE"], CancellationToken.None);
        result.StdOut.Trim().Should().Be("en");
    }
}

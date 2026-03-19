using DotnetTokenKiller.Infrastructure.Execution;
using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Execution;

public sealed class ProcessCommandRunnerTests
{
    private readonly ProcessCommandRunner _sut = new();

    private static (string command, string[] args) LongRunningCommand() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("ping", ["-n", "31", "127.0.0.1"])
            : ("sleep", ["30"]);

    private static (string command, string[] args) EchoCommand(string message) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd", ["/c", "echo", message])
            : ("echo", [message]);

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

        var act = async () => await _sut.RunCapturedAsync(cmd, args, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunPassthroughAsync_Cancellation_KillsRunningProcess()
    {
        // Covers KillProcess for RunPassthroughAsync path
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var (cmd, args) = LongRunningCommand();

        var act = async () => await _sut.RunPassthroughAsync(cmd, args, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void KillProcess_WithUnstartedProcess_CatchesInvalidOperationException()
    {
        var killMethod = typeof(ProcessCommandRunner)
            .GetMethod("KillProcess", BindingFlags.NonPublic | BindingFlags.Static)!;

        using var unstarted = new Process();

        var act = () => killMethod.Invoke(null, [unstarted]);

        act.Should().NotThrow();
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

        var act = () => killMethod.Invoke(null, [exited]);

        act.Should().NotThrow();
    }
}

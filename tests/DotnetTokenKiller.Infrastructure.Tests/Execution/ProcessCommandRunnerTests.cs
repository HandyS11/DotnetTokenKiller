using DotnetTokenKiller.Infrastructure.Execution;
using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Execution;

public sealed class ProcessCommandRunnerTests
{
    private readonly ProcessCommandRunner _sut = new();

    [Fact]
    public async Task RunCapturedAsync_BasicCommand_CapturesStdOut()
    {
        var result = await _sut.RunCapturedAsync("echo", ["hello world"]);

        result.StdOut.Should().Contain("hello world");
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunPassthroughAsync_BasicCommand_ReturnsZeroExitCode()
    {
        var exitCode = await _sut.RunPassthroughAsync("echo", ["hello"]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunCapturedAsync_Cancellation_KillsRunningProcess()
    {
        // Covers KillProcess when process has not exited (lines 90-93)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () => await _sut.RunCapturedAsync("sleep", ["30"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunPassthroughAsync_Cancellation_KillsRunningProcess()
    {
        // Covers KillProcess for RunPassthroughAsync path
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () => await _sut.RunPassthroughAsync("sleep", ["30"], cts.Token);

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

        var psi = new ProcessStartInfo("echo")
        {
            ArgumentList =
            {
                "test"
            },
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        using var exited = Process.Start(psi)!;
        await exited.WaitForExitAsync();

        var act = () => killMethod.Invoke(null, [exited]);

        act.Should().NotThrow();
    }
}

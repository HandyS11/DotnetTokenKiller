using System.Runtime.InteropServices;
using DotnetTokenKiller.Cli.Infrastructure;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Infrastructure;

public sealed class RunCancellationTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public void OnSignal_FirstInterrupt_IsAbsorbedAndLeavesTheChildTheGracePeriod()
    {
        using var cancellation = new RunCancellation(Timeout.InfiniteTimeSpan);

        var absorbed = cancellation.OnSignal(PosixSignal.SIGINT);

        absorbed.Should().BeTrue("dtk must survive the first Ctrl+C to stop the child and finalize its log");
        cancellation.Token.IsCancellationRequested.Should()
            .BeFalse("the child received the same Ctrl+C and gets the grace period to exit on its own");
    }

    [Fact]
    public void OnSignal_FirstInterrupt_CancelsOnceTheGracePeriodElapses()
    {
        using var cancellation = new RunCancellation(TimeSpan.FromMilliseconds(50));

        cancellation.OnSignal(PosixSignal.SIGINT);

        cancellation.Token.WaitHandle.WaitOne(WaitLimit).Should().BeTrue();
    }

    [Fact]
    public void OnSignal_FirstTerminate_IsAbsorbedAndCancelsAtOnce()
    {
        using var cancellation = new RunCancellation(Timeout.InfiniteTimeSpan);

        var absorbed = cancellation.OnSignal(PosixSignal.SIGTERM);

        absorbed.Should().BeTrue();
        cancellation.Token.WaitHandle.WaitOne(WaitLimit).Should()
            .BeTrue("SIGTERM is sent to dtk alone, so there is no reason to wait for the child");
    }

    [Theory]
    [InlineData(PosixSignal.SIGINT, PosixSignal.SIGINT)]
    [InlineData(PosixSignal.SIGINT, PosixSignal.SIGTERM)]
    [InlineData(PosixSignal.SIGTERM, PosixSignal.SIGINT)]
    public void OnSignal_SecondSignal_IsNotAbsorbed(PosixSignal first, PosixSignal second)
    {
        using var cancellation = new RunCancellation(Timeout.InfiniteTimeSpan);
        cancellation.OnSignal(first);

        var absorbed = cancellation.OnSignal(second);

        absorbed.Should().BeFalse("a second signal must terminate dtk at once");
    }

    [Fact]
    public void OnSignal_AfterDispose_IsNotAbsorbed()
    {
        var cancellation = new RunCancellation(Timeout.InfiniteTimeSpan);
        cancellation.Dispose();

        cancellation.OnSignal(PosixSignal.SIGINT).Should().BeFalse();
    }

    [Fact]
    public void Register_WiresTheSignalsWithoutCancelling()
    {
        using var cancellation = RunCancellation.Register();

        cancellation.Token.IsCancellationRequested.Should().BeFalse();
    }
}

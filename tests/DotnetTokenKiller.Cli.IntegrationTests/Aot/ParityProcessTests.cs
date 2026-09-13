using System.Diagnostics;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

public sealed class ParityProcessTests
{
    /// <summary>
    /// <c>cat</c> echoes its input as it reads it, so once its output pipe fills it stops reading. Writing all
    /// of standard input before draining standard output deadlocks well below this input's size.
    /// </summary>
    [UnixFact]
    public async Task RunAsync_ChildEchoesLargeInput_ReturnsItAsync()
    {
        var input = string.Concat(Enumerable.Repeat("0123456789abcdef0123456789abcdef0123456789abcdef012345678\n", 16_000));

        var output = await ParityProcess.RunAsync(new ProcessStartInfo("cat"), input)
            .WaitAsync(TimeSpan.FromSeconds(30));

        output.ExitCode.Should().Be(0);
        output.Stdout.Should().Be(input);
        output.Stderr.Should().BeEmpty();
    }
}

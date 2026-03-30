using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.Passthrough")]
[Trait("Category", "Integration")]
public class PassthroughIntegrationTests
{
    [Fact(Timeout = 60_000)]
    public async Task Passthrough_UnknownSubcommand_ForwardsToDotnet()
    {
        // "dotnet --info" is not a filtered subcommand, so dtk should pass it through
        var (dtkOutput, dtkExit) = await IntegrationTestHelper.RunDtkAsync("dotnet", "--info");
        var (_, dotnetExit) = await IntegrationTestHelper.RunDotnetAsync("--info");

        dtkExit.Should().Be(dotnetExit);
        // The passthrough output should contain key dotnet --info content
        dtkOutput.Should().Contain(".NET SDK");
    }

    [Fact(Timeout = 60_000)]
    public async Task Passthrough_UnfilteredSubcommand_ForwardsToDotnet()
    {
        // "dotnet help" is not a registered filtered command
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "help");

        exitCode.Should().Be(0);
        output.Should().NotBeEmpty();
    }
}

using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetPassthroughIntegrationTests
{
    [Fact]
    public async Task Passthrough_ToolList_Success_RawOutputPassedThrough()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "tool", "list");

        exitCode.Should().Be(0);
        output.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Passthrough_InvalidSubcommand_Failure_ExitCodeNonZero()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "totally-invalid-xyz123");

        exitCode.Should().NotBe(0);
        output.Should().NotBeNullOrWhiteSpace();
    }
}

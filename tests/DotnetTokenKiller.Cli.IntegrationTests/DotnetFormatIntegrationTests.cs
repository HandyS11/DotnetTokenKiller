using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class DotnetFormatIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    [Fact(Timeout = 60_000)]
    public async Task Format_SampleApp_NothingToFormat_OutputsCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "format", SampleApp);

        exitCode.Should().Be(0);
        output.Trim().Should().StartWith("✓ dotnet format");
    }

    [Fact(Timeout = 60_000)]
    public async Task Format_SampleApp_VerifyNoChanges_ExitCodeZero()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "format", "--verify-no-changes", SampleApp);

        exitCode.Should().Be(0);
        output.Trim().Should().StartWith("✓ dotnet format");
    }

    [Fact(Timeout = 60_000)]
    public async Task Format_SampleApp_OutputDoesNotContainLoadingConfiguration()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "format", SampleApp);

        output.Should().NotContain("Loading configuration");
    }

    [Fact(Timeout = 60_000)]
    public async Task Format_SampleApp_OutputDoesNotContainFormatComplete()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "format", SampleApp);

        output.Should().NotContain("Format complete");
    }
}

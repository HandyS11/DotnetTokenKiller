using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetNugetIntegrationTests
{
    [Fact]
    public async Task Nuget_LocalsList_Success_ContainsCacheLocation()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "nuget", "locals", "all", "--", "--list");

        exitCode.Should().Be(0);
        output.Should().Contain("http-cache:");
    }

    [Fact]
    public async Task Nuget_LocalsList_Success_NoHttpNoiseLines()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "nuget", "locals", "all", "--", "--list");

        output.Should().NotMatchRegex(@"(PUT|GET|Created|OK)\s+https?://");
    }

    [Fact]
    public async Task Nuget_Push_NonexistentFile_Failure_OutputContainsError()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "nuget", "push", "nonexistent.nupkg",
            "--", "--source", "https://api.nuget.org/v3/index.json");

        exitCode.Should().NotBe(0);
        output.Should().NotBeNullOrWhiteSpace();
    }
}

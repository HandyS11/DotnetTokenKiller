using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetPublishIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact]
    public async Task Publish_SampleApp_Success_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines.Should().HaveCount(1);
        lines[0].Trim().Should().StartWith("✓ dotnet publish →");
    }

    [Fact]
    public async Task Publish_SampleApp_Success_ContainsPublishPath()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleApp);

        exitCode.Should().Be(0);
        output.Should().Contain("publish/");
    }

    [Fact]
    public async Task Publish_SampleApp_Success_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleApp);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Restoring");
        output.Should().NotContain("Build succeeded");
    }

    [Fact]
    public async Task Publish_SampleApp_Success_Savings80Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "publish", "--verbosity", "normal", SampleApp);
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "publish", "--verbosity", "normal", SampleApp);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(80.0, "publish success should achieve ≥80% token savings");
    }

    [Fact]
    public async Task Publish_SampleAppBroken_Failure_OutputStartsWith1Error()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleAppBroken);

        exitCode.Should().NotBe(0);
        output.Trim().Should().StartWith("dotnet publish: 1 error");
    }

    [Fact]
    public async Task Publish_SampleAppBroken_Failure_ErrorContainsShortenedPath()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleAppBroken);

        var absolutePath = Path.Combine(SampleAppBroken, "BrokenClass.cs");
        output.Should().NotContain(absolutePath);
        output.Should().Contain("BrokenClass.cs");
        output.Should().MatchRegex(@"\(\d+,\d+\)");
    }

    [Fact]
    public async Task Publish_SampleAppBroken_Failure_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "publish", "--verbosity", "normal", SampleAppBroken);
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "publish", "--verbosity", "normal", SampleAppBroken);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "publish failure should achieve ≥70% token savings");
    }
}

using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class DotnetBuildIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact]
    public async Task Build_SampleApp_Success_OutputStartsWithCheckmark()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync("build", SampleApp);
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines.Should().HaveCount(1);
        lines[0].Trim().Should().StartWith("✓ dotnet build");

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(80.0, "build success should achieve ≥80% token savings");
    }

    [Fact]
    public async Task Build_SampleApp_Success_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleApp);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Restoring");
        output.Should().NotContain("Build succeeded");
        var lines = output.Trim().Split('\n');
        lines.Should().NotContain(
            static l => string.IsNullOrWhiteSpace(l),
            "no blank lines should be present in the output");
    }

    [Fact]
    public async Task Build_SampleAppBroken_Failure_OutputStartsWith1Error()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        exitCode.Should().NotBe(0);
        output.Trim().Should().StartWith("dotnet build: 1 error");
    }

    [Fact]
    public async Task Build_SampleAppBroken_Failure_ErrorContainsShortenedPath()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        // Absolute path to BrokenClass.cs must NOT appear (path must be shortened)
        var absolutePath = Path.Combine(SampleAppBroken, "BrokenClass.cs");
        output.Should().NotContain(absolutePath);
        output.Should().Contain("BrokenClass.cs");
        output.Should().MatchRegex(@"\(\d+,\d+\)");
    }

    [Fact]
    public async Task Build_SampleAppBroken_Failure_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Build FAILED");
    }

    [Fact]
    public async Task Build_SampleAppBroken_Failure_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "build", "--verbosity", "normal", SampleAppBroken);
        var (dtkOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", "--verbosity", "normal", SampleAppBroken);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, dtkOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "build failure should achieve ≥70% token savings");
    }
}

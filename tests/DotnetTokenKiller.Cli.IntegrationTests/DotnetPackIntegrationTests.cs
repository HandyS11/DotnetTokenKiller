using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetPackIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact]
    public async Task Pack_SampleApp_Success_OutputStartsWithNupkgName()
    {
        // Delete any existing nupkg to force dotnet pack to emit the "Successfully created package" line
        foreach (var nupkg in Directory.GetFiles(SampleApp, "*.nupkg", SearchOption.AllDirectories))
            File.Delete(nupkg);

        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleApp);

        exitCode.Should().Be(0);
        output.Trim().Should().StartWith("✓ dotnet pack → SampleApp.1.0.0.nupkg");
    }

    [Fact]
    public async Task Pack_SampleApp_Success_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleApp);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Restoring");
        output.Should().NotContain("Build succeeded");
    }

    [Fact]
    public async Task Pack_SampleApp_Success_Savings85Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "pack", "--verbosity", "normal", SampleApp);
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "pack", "--verbosity", "normal", SampleApp);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(85.0, "pack success should achieve ≥85% token savings");
    }

    [Fact]
    public async Task Pack_SampleAppBroken_Failure_OutputStartsWith1Error()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleAppBroken);

        exitCode.Should().NotBe(0);
        output.Trim().Should().StartWith("dotnet pack: 1 error");
    }

    [Fact]
    public async Task Pack_SampleAppBroken_Failure_ErrorContainsShortenedPath()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleAppBroken);

        var absolutePath = Path.Combine(SampleAppBroken, "BrokenClass.cs");
        output.Should().NotContain(absolutePath);
        output.Should().Contain("BrokenClass.cs");
        output.Should().MatchRegex(@"\(\d+,\d+\)");
    }
}

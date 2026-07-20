using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class DotnetCleanIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Clean_SampleApp_AfterBuild_OutputExactlyCheckmark()
    {
        // Build first as per AC#5: "after a prior successful build"
        await IntegrationTestHelper.RunDotnetAsync("build", SampleApp);

        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "clean", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines.Should().HaveCount(1);
        lines[0].Trim().Should().StartWith("✓ dotnet clean");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Clean_SampleApp_Savings95Percent()
    {
        // Use --verbosity normal to produce verbose raw output for savings calculation
        await IntegrationTestHelper.RunDotnetAsync("build", SampleApp);
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "clean", "--verbosity", "normal", SampleApp);

        await IntegrationTestHelper.RunDotnetAsync("build", SampleApp);
        var (dtkOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "clean", "--verbosity", "normal", SampleApp);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, dtkOutput);
        savings.Should().BeGreaterThanOrEqualTo(95.0, "clean success should achieve ≥95% token savings");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Clean_SampleAppBroken_SucceedsEvenForBrokenProject()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "clean", SampleAppBroken);

        exitCode.Should().Be(0);
        output.Trim().Should().StartWith("✓ dotnet clean");
    }
}

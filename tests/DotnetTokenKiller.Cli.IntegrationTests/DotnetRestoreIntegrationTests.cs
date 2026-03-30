using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class DotnetRestoreIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBadPackage =
        IntegrationTestHelper.SamplePath("SampleApp.BadPackage");

    [Fact(Timeout = 60_000)]
    public async Task Restore_SampleApp_Success_OutputStartsWithCheckmark()
    {
        // Use --force and --verbosity normal to produce verbose raw output for savings calculation
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "restore", "--force", "--verbosity", "normal", SampleApp);
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "restore", "--force", "--verbosity", "normal", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines.Should().HaveCount(1);
        lines[0].Trim().Should().StartWith("✓ dotnet restore");

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "restore success should achieve ≥90% token savings");
    }

    [Fact(Timeout = 60_000)]
    public async Task Restore_SampleApp_Success_NoProgressNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "restore", SampleApp);

        output.Should().NotContain("Writing assets file");
        output.Should().NotContain("Downloading");
        output.Should().NotContain("Determining projects");
    }

    [Fact(Timeout = 60_000)]
    public async Task Restore_SampleAppBadPackage_Failure_OutputStartsWith1Error()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "restore", SampleAppBadPackage);

        exitCode.Should().NotBe(0);
        output.Trim().Should().StartWith("dotnet restore: 1 error");
    }

    [Fact(Timeout = 60_000)]
    public async Task Restore_SampleAppBadPackage_Failure_ContainsNu1101()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "restore", SampleAppBadPackage);

        output.Should().Contain("NU1101");
        output.Should().Contain("DotnetTokenKiller.DoesNotExist");
    }

    [Fact(Timeout = 60_000)]
    public async Task Restore_SampleAppBadPackage_Failure_NoProgressNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "restore", SampleAppBadPackage);

        output.Should().NotContain("Downloading");
        output.Should().NotContain("Writing assets file");
    }
}

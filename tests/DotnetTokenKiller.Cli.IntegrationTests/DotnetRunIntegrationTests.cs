using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetRunIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    [Fact]
    public async Task Run_SampleApp_Success_ContainsAppOutput()
    {
        // --project must be forwarded via Spectre.Console's Remaining.Raw using --
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "run", "--", "--project", SampleApp);

        exitCode.Should().Be(0);
        output.Should().Contain("Hello from SampleApp!");
    }

    [Fact]
    public async Task Run_SampleApp_Success_NoMsBuildPreamble()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "run", "--", "--project", SampleApp);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Determining projects to restore");
        output.Should().NotContain("Build started");
        output.Should().NotMatchRegex(@"\S+ -> .+\.dll");
    }

    [Fact]
    public async Task Run_SampleApp_Success_Savings60Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "run", "--project", SampleApp, "--verbosity", "normal");
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "run", "--", "--project", SampleApp);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(60.0, "run success should achieve ≥60% token savings");
    }

    [Fact]
    public async Task Run_SampleApp_WithFail_ExitCodeIs1()
    {
        // --project and app arg --fail both forwarded via double --
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "run", "--", "--project", SampleApp, "--", "--fail");

        exitCode.Should().Be(1);
        output.Should().Contain("App starting...");
    }

    [Fact]
    public async Task Run_SampleApp_WithFail_NoMsBuildPreamble()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "run", "--", "--project", SampleApp, "--", "--fail");

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Determining projects to restore");
        output.Should().NotContain("Build started");
        output.Should().NotMatchRegex(@"\S+ -> .+\.dll");
    }
}

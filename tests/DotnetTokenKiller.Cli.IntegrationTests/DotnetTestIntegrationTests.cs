using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class DotnetTestIntegrationTests
{
    private static readonly string SampleTestsCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests/SampleApp.Tests.csproj");

    [Fact]
    public async Task Test_SampleTests_AllPass_OutputStartsWithCheckmark()
    {
        // Use -- to pass --filter through Spectre.Console's Remaining.Raw to dotnet test
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj,
            "--", "--filter", "FullyQualifiedName!~IntentionallyFailing");

        exitCode.Should().Be(0);
        output.TrimEnd().Should().StartWith("✓ dotnet test: 3 passed");
        output.Trim().Should().NotContain("\n");
    }

    [Fact]
    public async Task Test_SampleTests_AllPass_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj,
            "--", "--filter", "FullyQualifiedName!~IntentionallyFailing");

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Copyright (c) Microsoft");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }

    [Fact]
    public async Task Test_SampleTests_AllPass_Savings90Percent()
    {
        // --verbosity normal for raw output only; dtk runs with default verbosity
        // (--verbosity normal changes the output format, breaking DotnetTestFilter)
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "test", SampleTestsCsproj,
            "--filter", "FullyQualifiedName!~IntentionallyFailing",
            "--verbosity", "normal");
        var (filteredOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj,
            "--", "--filter", "FullyQualifiedName!~IntentionallyFailing");

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, filteredOutput);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "test all-pass should achieve ≥90% token savings");
    }

    [Fact]
    public async Task Test_SampleTests_WithFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (1):");
    }

    [Fact]
    public async Task Test_SampleTests_WithFailure_ContainsIntentionallyFailing()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        output.Should().Contain("IntentionallyFailing");
    }

    [Fact]
    public async Task Test_SampleTests_WithFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        output.Should().Contain("dotnet test: 1 failed, 3 passed");
    }

    [Fact]
    public async Task Test_SampleTests_WithFailure_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Copyright (c) Microsoft");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }

    [Fact]
    public async Task Test_SampleTests_WithFailure_Savings70Percent()
    {
        // --verbosity normal for raw output only; dtk runs with default verbosity
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "test", SampleTestsCsproj,
            "--verbosity", "normal");
        var (filteredOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, filteredOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "test with-failure should achieve ≥70% token savings");
    }
}

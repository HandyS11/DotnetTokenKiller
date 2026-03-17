using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class DotnetTestIntegrationTests
{
    private static readonly string SampleTestsCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests/SampleApp.Tests.csproj");

    private static readonly string SampleTestsMultiFailureCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests.MultiFailure/SampleApp.Tests.MultiFailure.csproj");

    private static readonly string SampleTestsNUnitCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests.NUnit/SampleApp.Tests.NUnit.csproj");

    private static readonly string SampleTestsMsTestCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests.MSTest/SampleApp.Tests.MSTest.csproj");

    private static readonly string SampleTestsReqnrollCsproj =
        IntegrationTestHelper.SamplePath("SampleApp.Tests.Reqnroll/SampleApp.Tests.Reqnroll.csproj");

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
    public async Task Test_SampleTests_WithFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (1):");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_SampleTests_WithFailure_ContainsIntentionallyFailing()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        output.Should().Contain("IntentionallyFailing");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_SampleTests_WithFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsCsproj);

        output.Should().Contain("dotnet test: 1 failed, 3 passed");
    }

    [Fact(Timeout = 60_000)]
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

    [Fact(Timeout = 60_000)]
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

    // ── SampleApp.Tests.MultiFailure (xUnit, 12 failures / 9 passes) ───

    [Fact(Timeout = 60_000)]
    public async Task Test_MultiFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMultiFailureCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (12):");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MultiFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMultiFailureCsproj);

        output.Should().Contain("dotnet test: 12 failed, 9 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MultiFailure_ContainsDiverseFailureTypes()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMultiFailureCsproj);

        // Assertion failures
        output.Should().Contain("Equality_Mismatch");
        // Exception failures
        output.Should().Contain("InvalidOperationException");
        // Data-driven failures
        output.Should().Contain("Square_Matches_Expected");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MultiFailure_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMultiFailureCsproj);

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MultiFailure_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "test", SampleTestsMultiFailureCsproj,
            "--verbosity", "normal");
        var (filteredOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMultiFailureCsproj);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, filteredOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "multi-failure test should achieve ≥70% token savings");
    }

    // ── SampleApp.Tests.NUnit (3 failures / 5 passes) ──────────────────

    [Fact(Timeout = 60_000)]
    public async Task Test_NUnit_AllPass_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsNUnitCsproj,
            "--", "--filter", "FullyQualifiedName~PassingTests");

        exitCode.Should().Be(0);
        output.TrimEnd().Should().StartWith("✓ dotnet test: 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_NUnit_WithFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsNUnitCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (3):");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_NUnit_WithFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsNUnitCsproj);

        output.Should().Contain("dotnet test: 3 failed, 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_NUnit_WithFailure_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsNUnitCsproj);

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }

    // ── SampleApp.Tests.MSTest (3 failures / 5 passes) ─────────────────

    [Fact(Timeout = 60_000)]
    public async Task Test_MSTest_AllPass_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMsTestCsproj,
            "--", "--filter", "FullyQualifiedName~PassingTests");

        exitCode.Should().Be(0);
        output.TrimEnd().Should().StartWith("✓ dotnet test: 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MSTest_WithFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMsTestCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (3):");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MSTest_WithFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMsTestCsproj);

        output.Should().Contain("dotnet test: 3 failed, 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_MSTest_WithFailure_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsMsTestCsproj);

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }

    // ── SampleApp.Tests.Reqnroll (2 failures / 5 passes) ───────────────

    [Fact(Timeout = 60_000)]
    public async Task Test_Reqnroll_AllPass_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsReqnrollCsproj,
            "--", "--filter", "FullyQualifiedName!~DivisionByZero&FullyQualifiedName!~IntentionallyWrong");

        exitCode.Should().Be(0);
        output.TrimEnd().Should().StartWith("✓ dotnet test: 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_Reqnroll_WithFailure_OutputStartsWithFailures()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsReqnrollCsproj);

        exitCode.Should().NotBe(0);
        output.Should().StartWith("FAILURES (2):");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_Reqnroll_WithFailure_ContainsSummaryLine()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsReqnrollCsproj);

        output.Should().Contain("dotnet test: 2 failed, 5 passed");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_Reqnroll_WithFailure_ContainsBddScenarioNames()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsReqnrollCsproj);

        output.Should().Contain("Division by zero should fail");
        output.Should().Contain("Intentionally wrong expectation");
    }

    [Fact(Timeout = 60_000)]
    public async Task Test_Reqnroll_WithFailure_NoTestRunnerNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "test", SampleTestsReqnrollCsproj);

        output.Should().NotContain("Starting test execution");
        output.Should().NotContain("Microsoft (R) Test Execution Command Line Tool");
        output.Should().NotContain("Passed!");
        output.Should().NotContain("Failed!");
    }
}

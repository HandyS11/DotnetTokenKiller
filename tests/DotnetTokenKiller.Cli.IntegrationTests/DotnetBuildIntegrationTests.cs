using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class DotnetBuildIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    private static readonly string SampleAppMultiError =
        IntegrationTestHelper.SamplePath("SampleApp.MultiError");

    private static readonly string SampleAppWarnings =
        IntegrationTestHelper.SamplePath("SampleApp.Warnings");

    private static readonly string SampleAppMultiProject =
        IntegrationTestHelper.SamplePath("SampleApp.MultiProject");

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
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

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
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

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppBroken_Failure_OutputStartsWith1Error()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        exitCode.Should().NotBe(0);
        output.Trim().Should().StartWith("dotnet build: 1 error");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppBroken_Failure_ErrorContainsShortenedPath()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        // Absolute path to BrokenClass.cs must NOT appear (path must be shortened)
        var absolutePath = Path.Combine(SampleAppBroken, "BrokenClass.cs");
        output.Should().NotContain(absolutePath);
        output.Should().Contain("BrokenClass.cs");
        output.Should().MatchRegex(@"\(\d+,\d+\)");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppBroken_Failure_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppBroken);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Build FAILED");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppBroken_Failure_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "build", "--verbosity", "normal", SampleAppBroken);
        var (dtkOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", "--verbosity", "normal", SampleAppBroken);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, dtkOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "build failure should achieve ≥70% token savings");
    }

    // ── SampleApp.MultiError ────────────────────────────────────────────

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiError_Failure_OutputStartsWithMultipleErrors()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppMultiError);

        exitCode.Should().NotBe(0);
        output.Should().MatchRegex(@"^dotnet build: \d+ errors");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiError_Failure_ContainsMultipleErrorCodes()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppMultiError);

        // The sample produces errors from several distinct CS codes
        output.Should().Contain("CS0029");
        output.Should().Contain("CS0103");
        output.Should().Contain("CS0122");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiError_Failure_GroupsErrorsByFile()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppMultiError);

        // Errors should be grouped under shortened file paths
        output.Should().Contain("AccessErrors.cs");
        output.Should().Contain("MissingTypes.cs");
        output.Should().Contain("TypeErrors.cs");
        output.Should().Contain("SignatureErrors.cs");
        output.Should().Contain("UndefinedReferences.cs");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiError_Failure_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleAppMultiError);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Build FAILED");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiError_Failure_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "build", "--verbosity", "normal", SampleAppMultiError);
        var (dtkOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppMultiError);

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, dtkOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "multi-error build should achieve ≥70% token savings");
    }

    // ── SampleApp.Warnings ──────────────────────────────────────────────

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppWarnings_Success_ExitCodeZero()
    {
        // Pass --no-incremental through to dotnet so warnings are always emitted
        var (_, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppWarnings, "--no-incremental");

        exitCode.Should().Be(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppWarnings_Success_OutputContainsWarnings()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppWarnings, "--no-incremental");

        output.Should().MatchRegex(@"dotnet build: 0 errors, \d+ warnings");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppWarnings_Success_ContainsWarningCodes()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppWarnings, "--no-incremental");

        // CS-level compiler warnings
        output.Should().Contain("CS0162");
        output.Should().Contain("CS0168");
        output.Should().Contain("CS8600");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppWarnings_Success_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppWarnings, "--no-incremental");

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Build succeeded");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppWarnings_Success_Savings70Percent()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
            "build", "--no-incremental", "--verbosity", "normal", SampleAppWarnings);
        var (dtkOutput, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppWarnings, "--no-incremental");

        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, dtkOutput);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "warnings-only build should achieve ≥70% token savings");
    }

    // ── SampleApp.MultiProject ──────────────────────────────────────────

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiProject_Success_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppMultiProject);

        exitCode.Should().Be(0);
        output.TrimEnd().Should().StartWith("✓ dotnet build");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiProject_Success_MentionsMultipleProjects()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppMultiProject);

        output.Should().Contain("2 projects");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Build_SampleAppMultiProject_Success_NoMsBuildNoise()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleAppMultiProject);

        output.Should().NotContain("MSBuild version");
        output.Should().NotContain("Restoring");
        output.Should().NotContain("Build succeeded");
    }
}

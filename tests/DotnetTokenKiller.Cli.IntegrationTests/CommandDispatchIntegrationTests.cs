using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Drives each top-level command through the real CLI rather than by calling its <c>RunAsync</c>
/// directly, so the wiring every other test in this suite skips — argument binding, dependency
/// resolution, and the dispatch from Spectre's <c>ExecuteAsync</c> into the command body — is
/// exercised as a user would hit it. A command that resolved its dependencies wrongly, or whose
/// settings no longer bind, passes every in-process test and fails here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CommandDispatchIntegrationTests
{
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Doctor_RunsThroughTheRealCliAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        // Isolate HOME/USERPROFILE too: doctor's hook health check inspects global-scope
        // integrations under the real user profile, so this test's result must not depend on
        // whatever the host machine happens to have installed there.
        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, isolateHome: true, "doctor");

        exitCode.Should().Be(0);
        output.Should().Contain("dotnet SDK");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Doctor_RedirectedOutput_KeepsALongPathOnOneLineAsync()
    {
        // The harness captures stdout through a pipe, where Spectre assumes 80 columns and hard-wraps
        // the line mid-path. A path over 150 characters would break across lines without the wider profile.
        var dir = Path.Combine(IntegrationTestHelper.NewIsolatedDir(), new string('d', 100));

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, isolateHome: true, "doctor");

        exitCode.Should().Be(0);
        var dbPath = Path.Combine(dir, "tracking.db");
        dbPath.Length.Should().BeGreaterThan(150);
        output.Should().Contain($"will be created at {dbPath}");
        output.Should().Contain($"will be created at {Path.Combine(dir, "tee")}");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Gain_RedirectedOutput_KeepsItsRulesAndTableAtTheirOwnWidthAsync()
    {
        // The wider profile for piped output must not stretch gain's header rule or its table to it.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, "/src/App.cs(12,20): error CS1002: ; expected [/src/App.csproj]\nBuild FAILED.\n", "pipe", "build");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "gain");

        exitCode.Should().Be(0, output);
        output.Should().Contain("By Command");
        output.ReplaceLineEndings("\n").Split('\n').Should().OnlyContain(line => line.Length <= 80, output);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Config_WithoutSubcommand_ShowsTheConfigurationAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "config");

        exitCode.Should().Be(0, output);
        output.Should().Contain("tracking.enabled").And.Contain("tee.mode");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task ConfigShow_RunsThroughTheRealCliAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "config", "show");

        exitCode.Should().Be(0);
        output.Should().Contain("tracking");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Reset_RunsThroughTheRealCliAndClearsTheIsolatedDatabaseAsync()
    {
        // Every run here is pointed at an isolated DTK_DB_PATH, so this deletes test data only.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "dotnet", "--version");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "reset", "--force");

        exitCode.Should().Be(0);
        output.Should().Contain("cleared");
        (await IntegrationTestHelper.ReadTrackedCommandsAsync(Path.Combine(dir, "tracking.db")))
            .Should().BeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Integrate_RunsThroughTheRealCliAndWritesIntoTheGivenDirectoryAsync()
    {
        // --dir keeps every write inside the temp directory; jetbrains is used because it touches
        // nothing outside the project it is pointed at.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var projectDir = Path.Combine(dir, "project");
        Directory.CreateDirectory(projectDir);

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(
            dir, "integrate", "jetbrains", "--dir", projectDir);

        exitCode.Should().Be(0);
        output.Should().NotBeEmpty();
        Directory.EnumerateFileSystemEntries(projectDir).Should().NotBeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Init_AndTheIntegrateAlias_WriteTheSameFilesAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var viaInit = Path.Combine(dir, "init");
        var viaAlias = Path.Combine(dir, "alias");
        Directory.CreateDirectory(viaInit);
        Directory.CreateDirectory(viaAlias);

        var (initOutput, initExit) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "init", "jetbrains", "--dir", viaInit);
        var (aliasOutput, aliasExit) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "integrate", "jetbrains", "--dir", viaAlias);

        initExit.Should().Be(0);
        aliasExit.Should().Be(initExit);
        aliasOutput.Should().Be(initOutput, "the alias must behave exactly like init");
        Directory.EnumerateFiles(viaAlias, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(viaAlias, f))
            .Should().BeEquivalentTo(Directory.EnumerateFiles(viaInit, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(viaInit, f)));
    }
}

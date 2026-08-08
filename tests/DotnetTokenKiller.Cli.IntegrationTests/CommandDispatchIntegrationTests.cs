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
        // --dir keeps every write inside the temp directory; cursor is used because it touches
        // nothing outside the project it is pointed at.
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var projectDir = Path.Combine(dir, "project");
        Directory.CreateDirectory(projectDir);

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(
            dir, "integrate", "cursor", "--dir", projectDir);

        exitCode.Should().Be(0);
        output.Should().NotBeEmpty();
        Directory.EnumerateFileSystemEntries(projectDir).Should().NotBeEmpty();
    }
}

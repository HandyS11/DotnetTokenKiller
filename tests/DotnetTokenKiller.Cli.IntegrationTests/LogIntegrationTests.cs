using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class LogIntegrationTests
{
    /// <summary>Raw output long enough to clear FileTeeService's 500-character floor.</summary>
    private static string BuildOutput() =>
        string.Join('\n', Enumerable.Range(1, 60)
            .Select(i => $"/src/File{i}.cs(12,5): warning CA1822: Member does not access instance data")) + "\n";

    [Fact]
    public async Task Log_RetrievesWhatAPreviousRunTeedAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var raw = BuildOutput();

        var (_, pipeExit) = await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, raw, "pipe", "build", "--exit-code", "1");
        pipeExit.Should().Be(1);

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log");

        exitCode.Should().Be(0);
        output.Should().Contain("dotnet build");
        output.Should().Contain("exit 1");
        // The retrieved detail is the raw output, not the filtered summary.
        output.Should().Contain("warning CA1822");
        output.Should().Contain("/src/File60.cs");
    }

    [Fact]
    public async Task Log_WindowsByDefault_AndFullWidensItAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, BuildOutput(), "pipe", "build", "--exit-code", "1");

        var (windowed, _) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log", "--lines", "5");
        var (full, _) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log", "--full");

        windowed.Should().Contain("showing last 5 of");
        windowed.Should().NotContain("/src/File1.cs");
        full.Should().Contain("/src/File1.cs");
        full.Length.Should().BeGreaterThan(windowed.Length);
    }

    [Fact]
    public async Task Log_ListsWhatIsAvailableAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, BuildOutput(), "pipe", "build", "--exit-code", "1");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log", "--list");

        exitCode.Should().Be(0);
        output.Should().Contain("build");
    }

    [Fact]
    public async Task Log_ExitsOneAndExplains_WhenNothingWasEverTeedAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log");

        exitCode.Should().Be(1);
        output.Should().Contain("no logs");
    }

    [Fact]
    public async Task Log_RecordsNothingInTheTracker_SoGainStaysHonestAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, BuildOutput(), "pipe", "build", "--exit-code", "1");

        var (before, _) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "gain", "--json");
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "log");
        await IntegrationTestHelper.RunDtkInDirAsync(dir, "log", "--list");
        var (after, _) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "gain", "--json");

        // dtk log runs no dotnet command. A row for it would have no raw output to compare
        // against and would corrupt the savings figures gain reports.
        after.Should().Be(before);
    }

    [Fact]
    public async Task Log_FiltersBySubcommandAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();

        await IntegrationTestHelper.RunDtkWithStdinInDirAsync(
            dir, BuildOutput(), "pipe", "build", "--exit-code", "1");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "log", "test");

        // A build log exists but no test log does, so this must not fall back to the build one.
        exitCode.Should().Be(1);
        output.Should().NotContain("warning CA1822");
    }
}

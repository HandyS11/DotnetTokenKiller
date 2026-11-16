using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Where dtk keeps its state by default when the user's data and config folders do not exist yet, as in a fresh
/// container or a sandboxed <c>HOME</c>: under <c>HOME</c>, never in a relative <c>dtk/</c> folder of the working
/// directory, where a git work tree counts its files as changes and a build lists no warning of the files it changed.
/// </summary>
/// <remarks>
/// Unix only: Windows resolves those folders from the user's profile through the shell, not from <c>USERPROFILE</c>,
/// and they always exist there.
/// </remarks>
public sealed class DefaultStateDirectoryTests : IDisposable
{
    private readonly string _root = IntegrationTestHelper.NewIsolatedDir();

    public DefaultStateDirectoryTests()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(WorkingDirectory);
    }

    private string Home => Path.Combine(_root, "home");

    private string WorkingDirectory => Path.Combine(_root, "work");

    [UnixFact]
    public async Task FailedRun_WithoutADataFolder_KeepsItsTeeLogAndTrackingUnderHome()
    {
        var (output, _) = await IntegrationTestHelper.RunDtkWithDefaultStateAsync(
            Home, WorkingDirectory, "Program.cs(3,1): error CS1002: ; expected\n", "pipe", "build", "--exit-code", "1");

        Directory.EnumerateFileSystemEntries(WorkingDirectory).Should().BeEmpty(output);
        var stateFiles = Directory.EnumerateFiles(Home, "*", SearchOption.AllDirectories).ToList();
        stateFiles.Should().Contain(path => path.Contains(Folder("dtk", "tee")), output);
        stateFiles.Should().Contain(path => path.Contains(Folder("dtk", "tracking.db.pending")), output);
    }

    [UnixFact]
    public async Task ConfigSet_WithoutAConfigFolder_WritesItsFileUnderHome()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkWithDefaultStateAsync(
            Home, WorkingDirectory, null, "config", "set", "tee.mode", "Always");

        exitCode.Should().Be(0, output);
        Directory.EnumerateFileSystemEntries(WorkingDirectory).Should().BeEmpty(output);
        Directory.EnumerateFiles(Home, "config.json", SearchOption.AllDirectories)
            .Should().ContainSingle(path => path.EndsWith(Path.Combine("dtk", "config.json"), StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary><c>/dtk/tee/</c>: the names as whole path segments.</summary>
    /// <param name="names">The folder names, outermost first.</param>
    private static string Folder(params string[] names) =>
        $"{Path.DirectorySeparatorChar}{Path.Combine(names)}{Path.DirectorySeparatorChar}";
}

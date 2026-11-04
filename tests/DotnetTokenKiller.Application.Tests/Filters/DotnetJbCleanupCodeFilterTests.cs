using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public sealed class DotnetJbCleanupCodeFilterTests : IDisposable
{
    private const string Counts = "414 processed, 397 skipped";

    private readonly DotnetJbCleanupCodeFilter _sut = new("/test/project/root");
    private readonly string _temp = Directory.CreateTempSubdirectory("dtk-jb-").FullName;

    public void Dispose() => Directory.Delete(_temp, true);

    [Fact]
    public void Apply_Success_ListsChangedFiles()
    {
        var context = new FilterContext
        {
            Changes = new FileChangeSet(["src/A.cs", "src/B.cs"])
        };

        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, context).Output;

        result.Should().Be($"src/A.cs\nsrc/B.cs\n✓ dotnet jb cleanupcode: 2 files changed ({Counts})\n");
    }

    [Fact]
    public void Apply_SingleChange_UsesSingular()
    {
        var context = new FilterContext { Changes = new FileChangeSet(["src/A.cs"]) };

        _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, context).Output
            .Should().EndWith($"✓ dotnet jb cleanupcode: 1 file changed ({Counts})\n");
    }

    [Fact]
    public void Apply_NoChanges()
    {
        var context = new FilterContext { Changes = new FileChangeSet([]) };

        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, context).Output;

        result.Should().Be($"✓ dotnet jb cleanupcode: no changes ({Counts})\n");
    }

    [Fact]
    public void Apply_Piped_SaysChangedFilesUnknown()
    {
        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, FilterContext.Unknown).Output;

        result.Should().Be($"✓ dotnet jb cleanupcode: {Counts} · changed files unknown (piped)\n");
    }

    [Fact]
    public void Apply_TreeTooLarge()
    {
        var context = new FilterContext { Changes = FileChangeSet.NotComputed("tree too large") };

        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, context).Output;

        result.Should().Be($"✓ dotnet jb cleanupcode: {Counts} · changed files not computed (tree too large)\n");
    }

    [Fact]
    public void Apply_CapsAt30Changed()
    {
        var paths = Enumerable.Range(1, 35).Select(i => $"src/F{i:00}.cs").ToList();
        var context = new FilterContext { Changes = new FileChangeSet(paths) };

        var lines = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_success.txt"), 0, context).Output
            .TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(32);
        lines[0].Should().Be("src/F01.cs");
        lines[29].Should().Be("src/F30.cs");
        lines[30].Should().Be("… 5 more (git status)");
        lines[31].Should().Be($"✓ dotnet jb cleanupcode: 35 files changed ({Counts})");
    }

    [Fact]
    public void Apply_LoadError()
    {
        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_load_error.txt"), 4, FilterContext.Unknown).Output;

        result.Should().Be(
            "[MsBuild] src/DotnetTokenKiller.Domain/DotnetSubcommands.cs (210,11)->(210,11)  CS8955: " +
            "Source file can not contain both file-scoped and normal namespace declarations.\n" +
            "✗ dotnet jb cleanupcode failed (exit 4): 1 project load error\n");
    }

    [Fact]
    public void Apply_LoadError_DeduplicatesAndPluralizes()
    {
        const string output = "JetBrains Cleanup Code 1\n[MsBuild] /test/project/root/A.cs (1,1)->(1,1)  CS1: x\n" +
                              "[MsBuild] /test/project/root/A.cs (1,1)->(1,1)  CS1: x\n" +
                              "[MsBuild] /test/project/root/B.cs (2,2)->(2,2)  CS2: y\n";

        _sut.Apply(output, 4, FilterContext.Unknown).Output.Should().Be(
            "[MsBuild] A.cs (1,1)->(1,1)  CS1: x\n[MsBuild] B.cs (2,2)->(2,2)  CS2: y\n" +
            "✗ dotnet jb cleanupcode failed (exit 4): 2 project load errors\n");
    }

    [Fact]
    public void Apply_MissingPath()
    {
        var result = _sut.Apply(LoadFixture("dotnet_jb_cleanupcode_missing_path.txt"), 3, FilterContext.Unknown).Output;

        result.Should().Be(
            "Warning: Path doesn't exist DoesNotExist.csproj\n" +
            "No items were found to cleanup.\n" +
            "✗ dotnet jb cleanupcode failed (exit 3): nothing to clean up\n");
    }

    [Fact]
    public void Apply_UnknownFailure_UsesBareStatus()
    {
        _sut.Apply("boom\n", 9, FilterContext.Unknown).Output
            .Should().Be("✗ dotnet jb cleanupcode failed (exit 9)\n");
    }

    [Theory]
    [InlineData("dotnet_jb_cleanupcode_success.txt", 0)]
    [InlineData("dotnet_jb_cleanupcode_load_error.txt", 4)]
    [InlineData("dotnet_jb_cleanupcode_missing_path.txt", 3)]
    public void Apply_Crlf(string name, int exitCode)
    {
        var fixture = LoadFixture(name);
        var context = new FilterContext { Changes = new FileChangeSet(["src/A.cs"]) };

        var result = _sut.Apply(fixture.ReplaceLineEndings("\r\n"), exitCode, context).Output;

        result.Should().NotContain("\r");
        result.Should().Be(_sut.Apply(fixture, exitCode, context).Output);
    }

    [Fact]
    public void SnapshotRoot_SolutionInSubfolder()
    {
        _sut.SnapshotRoot(["sub/App.slnx", "--profile=X"], "/work").Should().Be(Path.GetFullPath("/work/sub"));
    }

    [Theory]
    [InlineData("A.sln")]
    [InlineData("A.slnf")]
    [InlineData("src/A.csproj")]
    [InlineData("SRC/A.CSPROJ")]
    public void SnapshotRoot_ProjectExtensions(string argument)
    {
        _sut.SnapshotRoot([argument], "/work").Should().Be(
            Path.GetDirectoryName(Path.GetFullPath(argument, "/work")));
    }

    [Fact]
    public void SnapshotRoot_Directory()
    {
        Directory.CreateDirectory(Path.Combine(_temp, "src"));

        _sut.SnapshotRoot(["src"], _temp).Should().Be(Path.Combine(_temp, "src"));
    }

    [Fact]
    public void SnapshotRoot_PathWithSpaces()
    {
        _sut.SnapshotRoot(["--profile=X", "My Folder/My App.slnx"], "/work").Should().Be(
            Path.GetFullPath("/work/My Folder"));
    }

    [Fact]
    public void SnapshotRoot_AbsoluteSolution()
    {
        var solution = Path.Combine(_temp, "x", "A.slnx");

        _sut.SnapshotRoot([solution], "/elsewhere").Should().Be(Path.Combine(_temp, "x"));
    }

    [Fact]
    public void SnapshotRoot_None_IsWorkingDirectory()
    {
        _sut.SnapshotRoot([], "/work").Should().Be("/work");
        _sut.SnapshotRoot(["--profile=X", "--no-build", "missing-thing"], "/work").Should().Be("/work");
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetJbCleanupCodeFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public sealed class DotnetJbCleanupCodeFilterTests : IDisposable
{
    private const string Counts = "414 processed, 397 skipped";

    /// <summary>
    /// A working directory that is fully qualified on every OS: <c>/work</c> alone is rooted but not qualified on
    /// Windows (no drive), where <see cref="Path.GetFullPath(string, string)"/> rejects it. A real run's working
    /// directory is always qualified.
    /// </summary>
    private static readonly string Work = Path.GetFullPath("/work");

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
    public void Apply_UnknownFailure_LeavesTheRawTailToThePipeline()
    {
        // Nothing the filter recognises: an empty result makes the pipeline show the raw tail above its status line.
        _sut.Apply("boom\n", 9, FilterContext.Unknown).Output.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ToolNotRestored_LeavesTheRawTailToThePipeline()
    {
        // Real output, exit 1: `dotnet jb cleanupcode App.csproj` in a directory whose tool manifest names
        // jetbrains.resharper.globaltools at a version that was never restored.
        const string output = "Run \"dotnet tool restore\" to make the \"jb\" command available.\n";

        _sut.Apply(output, 1, FilterContext.Unknown).Output.Should().BeEmpty();
    }

    [Fact]
    public void Apply_FailureAfterTheHeader_KeepsWhatIsNotAProcessedFile()
    {
        // Real lines: the success fixture up to its header and first entries, then the load-error fixture's
        // [MsBuild] line and the missing-path fixture's Warning line, as if jb had failed while it cleaned up.
        var success = LoadFixture("dotnet_jb_cleanupcode_success.txt").Split('\n');
        var header = Array.FindIndex(success, l => l.StartsWith("Cleaning up using profile", StringComparison.Ordinal));
        var msBuild = LoadFixture("dotnet_jb_cleanupcode_load_error.txt").Split('\n')[4];
        var warning = LoadFixture("dotnet_jb_cleanupcode_missing_path.txt").Split('\n')[3];
        var output = string.Join('\n', success[..(header + 5)]) +
                     $"\n{msBuild}\n{warning}\n{success[header + 5]}\n";

        _sut.Apply(output, 4, FilterContext.Unknown).Output.Should().Be(
            "[MsBuild] src/DotnetTokenKiller.Domain/DotnetSubcommands.cs (210,11)->(210,11)  CS8955: " +
            "Source file can not contain both file-scoped and normal namespace declarations.\n" +
            "Warning: Path doesn't exist DoesNotExist.csproj\n" +
            "✗ dotnet jb cleanupcode failed (exit 4): 1 project load error\n");
    }

    [Fact]
    public void Apply_FailureAfterTheHeader_KeepsTextThatIsNotAFile()
    {
        // Real lines: text the filter does not know, after the header and the first entries, as a crash would leave
        // it. It is shown, not counted as a processed file.
        var success = LoadFixture("dotnet_jb_cleanupcode_success.txt").Split('\n');
        var header = Array.FindIndex(success, l => l.StartsWith("Cleaning up using profile", StringComparison.Ordinal));
        var output = string.Join('\n', success[..(header + 3)]) +
                     "\nRun \"dotnet tool restore\" to make the \"jb\" command available.\n";

        _sut.Apply(output, 1, FilterContext.Unknown).Output.Should().Be(
            "Run \"dotnet tool restore\" to make the \"jb\" command available.\n" +
            "✗ dotnet jb cleanupcode failed (exit 1)\n");
    }

    [Fact]
    public void Apply_ProgressLines_AreNeitherCountedNorKept()
    {
        // Real lines, verbatim, from `dtk dotnet jb cleanupcode DotnetTokenKiller.slnx --include=...` on a tree with
        // changes to make: jb prints its progress, then the header again, then the processed list (here cut to two).
        const string output =
            "JetBrains Cleanup Code 2026.2.3.1\n" +
            "Configuration: Debug, Platform: Any CPU\n" +
            "<src>\\<DotnetTokenKiller.Domain>\\obj\\Debug\\net10.0\\DotnetTokenKiller.Domain.AssemblyInfo.cs" +
            " - Skipping. Generated file\n" +
            "Cleaning up using profile Built-in: Reformat & Apply Syntax Style\n" +
            "\n" +
            "Analyzing file 1 of 46: DtkConfig.cs\n" +
            "\n" +
            "Analyzing file 9 of 46: FileChangeSet.cs\n" +
            "Analyzing file 9 of 46: FileChangeSet.cs - Cleaning up using profile Built-in: Reformat & Apply Syntax" +
            " Style\n" +
            "Cleaning up using profile Built-in: Reformat & Apply Syntax Style\n" +
            "\n" +
            "<src>\\<DotnetTokenKiller.Domain>\\Configuration\\DtkConfig.cs\n" +
            "<src>\\<DotnetTokenKiller.Domain>\\Configuration\\IConfigProvider.cs\n";

        _sut.Apply(output, 0, FilterContext.Unknown).Output
            .Should().Be("✓ dotnet jb cleanupcode: 2 processed, 1 skipped · changed files unknown (piped)\n");
        _sut.Apply(output, 1, FilterContext.Unknown).Output.Should().BeEmpty();
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
        _sut.SnapshotRoot(["sub/App.slnx", "--profile=X"], Work).Should().Be(Path.GetFullPath(Path.Combine(Work, "sub")));
    }

    [Theory]
    [InlineData("A.sln")]
    [InlineData("A.slnf")]
    [InlineData("src/A.csproj")]
    [InlineData("SRC/A.CSPROJ")]
    public void SnapshotRoot_ProjectExtensions(string argument)
    {
        _sut.SnapshotRoot([argument], Work).Should().Be(
            Path.GetDirectoryName(Path.GetFullPath(argument, Work)));
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
        _sut.SnapshotRoot(["--profile=X", "My Folder/My App.slnx"], Work).Should().Be(
            Path.GetFullPath(Path.Combine(Work, "My Folder")));
    }

    [Fact]
    public void SnapshotRoot_AbsoluteSolution()
    {
        var solution = Path.Combine(_temp, "x", "A.slnx");

        _sut.SnapshotRoot([solution], Path.GetFullPath("/elsewhere")).Should().Be(Path.Combine(_temp, "x"));
    }

    [Fact]
    public void SnapshotRoot_None_IsWorkingDirectory()
    {
        _sut.SnapshotRoot([], Work).Should().Be(Work);
        _sut.SnapshotRoot(["--profile=X", "--no-build", "missing-thing"], Work).Should().Be(Work);
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

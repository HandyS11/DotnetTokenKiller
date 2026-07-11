using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetRestoreFilterTests
{
    private readonly DotnetRestoreFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "restore filter should achieve ≥90% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("Writing assets file to disk")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        _sut.Apply(fixture, exitCode: 0).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_NuGetError_ShowsErrorCountAndCode()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                               Determining projects to restore...
                               /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj : error NU1101: Unable to find package 'NonExistent.Package'. No packages exist with this id in source(s): nuget.org
                             """;
        var result = _sut.Apply(input, exitCode: 1);
        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101:");
        result.Should().Contain("src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj");
    }

    [Fact]
    public void Apply_NullInput_ReturnsEmpty()
    {
        _sut.Apply(null!, exitCode: 0).Should().BeEmpty();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
    }

    [Fact]
    public void Apply_MixedRestoredAndUpToDate_SumsCounts()
    {
        const string input = """
                             Restored C:\Code\Proj1.csproj (in 100 ms).
                             3 of 5 projects are up-to-date for restore.
                             """;
        // totalProjects = 1 (restored) + 3 (up-to-date) = 4
        var result = _sut.Apply(input, exitCode: 0);
        result.Should().Be("✓ dotnet restore (4 projects, 0.10s)\n");
    }

    [Fact]
    public void Apply_FSharpProject_IsDetected()
    {
        const string input = "  Restored /path/Proj.fsproj (in 50 ms).";
        var result = _sut.Apply(input, exitCode: 0);
        result.Should().Contain("(1 project, 0.05s)");
    }

    [Fact]
    public void Apply_StandardErrorFormat_WithProject_ShowsError()
    {
        // Covers TryParseStandardError match-success path (lines 99-106), condition 100 false branch
        const string input = "error NU1101: Unable to find package [/path/proj.csproj]";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101");
    }

    [Fact]
    public void Apply_StandardErrorFormat_WithoutProject_ShowsErrorWithoutProject()
    {
        // Covers condition 100 true branch (empty proj) and FormatErrors empty-project path (lines 140-142)
        const string input = "error NU1101: Unable to find package 'Foo'";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101");
        result.Should().NotContain("(");
    }

    [Fact]
    public void Apply_MultipleErrors_UsesPluralForm()
    {
        // Covers (errors.Count == 1 ? "" : "s") false branch (plural) at line 135
        const string input =
            "error NU1101: Package not found [/path/a.csproj]\nerror NU1102: Version mismatch [/path/b.csproj]";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().StartWith("dotnet restore: 2 errors");
    }

    [Fact]
    public void Apply_NoSummaryLines_ReturnsEmpty()
    {
        // Covers FormatOutput totalProjects==0 path (lines 124-125) when no errors and AllUpToDate=false
        const string input = "Some unrecognised restore output";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        // Kills null coalescing mutations (line 15): rootPath ?? Environment.CurrentDirectory
        var filter = new DotnetRestoreFilter();
        const string input = "  Restored /some/Proj.csproj (in 50 ms).";

        var result = filter.Apply(input, exitCode: 0);

        result.Should().Contain("project");
    }

    [Fact]
    public void Apply_SingleProject_UsesSingularForm()
    {
        // Kills conditional mutation (false/true) on plural form (line 136)
        const string input = "  Restored /path/Proj.csproj (in 100 ms).";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("1 project,");
        result.Should().NotContain("projects");
    }

    [Fact]
    public void Apply_AllUpToDate_ReturnsUpToDateMessage()
    {
        // Kills totalProjects == 0 && AllUpToDate path (line 118-120)
        const string input = "All projects are up-to-date for restore.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet restore (all up-to-date)\n");
    }

    [Fact]
    public void Apply_ErrorWithProject_ShowsProjectInParentheses()
    {
        // Kills string mutations on FormatErrors format (lines 99-105)
        const string input =
            "/test/project/root/src/App/App.csproj : error NU1101: Unable to find package";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("NU1101:");
        result.Should().Contain("(src/App/App.csproj)");
    }

    [Fact]
    public void Apply_StandardErrorWithEmptyProject_ShortenPathHandlesEmpty()
    {
        // Kills string mutation on projRaw check (line 100)
        const string input = "error NU1101: Unable to find package 'Foo'";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("NU1101: Unable to find package 'Foo'");
    }

    [Fact]
    public void Apply_RestoredDuration_ReportsSlowestNotSum()
    {
        // Elapsed reports the slowest single restore (300 ms), not the sum (500 ms): restores run
        // in parallel, so summing per-project times overstates the real wall-clock time.
        const string input = """
                               Restored /path/A.csproj (in 200 ms).
                               Restored /path/B.csproj (in 300 ms).
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("0.30s").And.NotContain("0.50s");
    }

    [Fact]
    public void Apply_SingleError_UsesSingularForm()
    {
        // Kills conditional mutation on (errors.Count == 1) at line 136
        const string input =
            "/path/proj.csproj : error NU1101: Not found";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("1 error");
        result.Should().NotContain("1 errors");
    }

    [Fact]
    public void Apply_RestoredCountTracks_EachRestoredLine()
    {
        // Kills statement mutation on restored-count increment (line 47)
        const string input = """
                               Restored /path/A.csproj (in 100 ms).
                               Restored /path/B.csproj (in 200 ms).
                               Restored /path/C.csproj (in 300 ms).
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("3 projects");
    }

    [Fact]
    public void Apply_PartialUpToDate_CountIsParsed()
    {
        // Kills statement mutation on UpToDateCount parse (line 53)
        const string input = "5 of 10 projects are up-to-date for restore.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("5 projects");
    }

    [Fact]
    public void Apply_ProjectFirstError_ProjectPathIncluded()
    {
        // Kills statement mutations on proj assignment and return true (line 64)
        const string input = "/test/project/root/src/App/App.csproj : error NU1101: Package not found";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("(src/App/App.csproj)");
    }

    [Fact]
    public void Apply_StandardError_ProjectShortenedPath()
    {
        // Kills string mutations on proj processing (lines 99-100)
        const string input = "error NU1101: Unable to find package [/test/project/root/src/App/App.csproj]";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("src/App/App.csproj");
    }

    [Fact]
    public void Apply_ErrorFormat_ContainsCodeColonMessage()
    {
        // Kills string mutations on error format (line 85)
        const string input = "error NU1101: Unable to find package 'Foo'";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("NU1101: Unable to find package");
    }

    [Fact]
    public void Apply_TotalProjects_ZeroWithNoUpToDate_ReturnsEmpty()
    {
        // Kills equality mutation on totalProjects != 0 (line 118)
        const string input = "Some unrelated output line";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_RestoreOutput_ContainsCheckmark()
    {
        // Kills string mutation on "✓ dotnet restore" prefix
        const string input = "  Restored /path/Proj.csproj (in 50 ms).";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().StartWith("\u2713 dotnet restore");
    }

    [Fact]
    public void Apply_MultipleErrorsFormat_ContainsDotnetRestore()
    {
        // Kills string mutations on header format (line 136)
        const string input = "error NU1101: p1\nerror NU1102: p2";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().StartWith("dotnet restore:");
    }

    [Fact]
    public void Apply_NonZeroExitWithNoParsedErrors_ReturnsEmpty()
    {
        // Crashed process / unparseable output: no NuGet errors were parsed, but the exit code is
        // non-zero. Returning "0 errors" here would look like a clean success and would prevent
        // FilteredRunUseCase's raw-tail fallback from ever firing.
        const string input = "Segmentation fault (core dumped)";

        var result = _sut.Apply(input, exitCode: 139);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("  Restored /repo/App.csproj (in 1.02 sec).")]
    [InlineData("  Restored /repo/App.csproj (in 1 min 5 sec).")]
    public void Apply_SlowRestore_CountsProject(string line)
    {
        // Second- and minute-scale restore durations were missed by the ms-only pattern,
        // dropping the project from the count entirely.
        var result = new DotnetRestoreFilter().Apply(line + "\n", exitCode: 0);
        result.Should().Contain("1 project");
    }

    [Fact]
    public void Apply_MsbError_IsSurfaced()
    {
        // MSB-class failures (e.g. missing project file) are as fatal as NU errors and must surface.
        const string raw = "MSBUILD : error MSB1009: Project file does not exist.";
        var result = new DotnetRestoreFilter().Apply(raw, exitCode: 1);
        result.Should().Contain("MSB1009");
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetRestoreFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

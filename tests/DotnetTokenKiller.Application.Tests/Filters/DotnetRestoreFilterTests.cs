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
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        var result = _sut.Apply(fixture);
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
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_NuGetError_ShowsErrorCountAndCode()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                               Determining projects to restore...
                               /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj : error NU1101: Unable to find package 'NonExistent.Package'. No packages exist with this id in source(s): nuget.org
                             """;
        var result = _sut.Apply(input);
        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101:");
        result.Should().Contain("src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    [Fact]
    public void Apply_MixedRestoredAndUpToDate_SumsCounts()
    {
        const string input = """
                             Restored C:\Code\Proj1.csproj (in 100 ms).
                             3 of 5 projects are up-to-date for restore.
                             """;
        // totalProjects = 1 (restored) + 3 (up-to-date) = 4
        var result = _sut.Apply(input);
        result.Should().Be("✓ dotnet restore (4 projects, 0.10s)\n");
    }

    [Fact]
    public void Apply_FSharpProject_IsDetected()
    {
        const string input = "  Restored /path/Proj.fsproj (in 50 ms).";
        var result = _sut.Apply(input);
        result.Should().Contain("(1 project, 0.05s)");
    }

    [Fact]
    public void Apply_StandardErrorFormat_WithProject_ShowsError()
    {
        // Covers TryParseStandardError match-success path (lines 99-106), condition 100 false branch
        const string input = "error NU1101: Unable to find package [/path/proj.csproj]";

        var result = _sut.Apply(input);

        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101");
    }

    [Fact]
    public void Apply_StandardErrorFormat_WithoutProject_ShowsErrorWithoutProject()
    {
        // Covers condition 100 true branch (empty proj) and FormatErrors empty-project path (lines 140-142)
        const string input = "error NU1101: Unable to find package 'Foo'";

        var result = _sut.Apply(input);

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

        var result = _sut.Apply(input);

        result.Should().StartWith("dotnet restore: 2 errors");
    }

    [Fact]
    public void Apply_NoSummaryLines_ReturnsEmpty()
    {
        // Covers FormatOutput totalProjects==0 path (lines 124-125) when no errors and AllUpToDate=false
        const string input = "Some unrecognised restore output";

        var result = _sut.Apply(input);

        result.Should().BeEmpty();
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

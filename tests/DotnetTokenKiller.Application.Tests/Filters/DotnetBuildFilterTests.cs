using DotnetTokenKiller.Application.Filters;
using FluentAssertions;
using System.Reflection;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetBuildFilterTests
{
    private readonly DotnetBuildFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_WarningsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_ErrorsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(85.0, "build success filter should achieve ≥85% savings");
    }

    [Fact]
    public void Apply_WarningsFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(75.0, "build warnings filter should achieve ≥75% savings");
    }

    [Fact]
    public void Apply_ErrorsFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "build errors filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("MSBuild version 17.0")]
    [InlineData("Determining projects to restore...")]
    [InlineData("All projects are up-to-date for restore.")]
    [InlineData("Build succeeded.")]
    [InlineData("Build FAILED.")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
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
    public void Apply_AnsiCodesInInput_StrippedFromOutput()
    {
        const string ansiInput = "\x1b[32mBuild succeeded.\x1b[0m\n";
        var result = _sut.Apply(ansiInput);
        result.Should().NotContain("\x1b[");
    }

    [Fact]
    public void Apply_SimpleMsbuildDiagnostic_IsIncluded()
    {
        // Covers SimpleDiagnosticPattern path (lines 79-98) and TryAddDiagnosticLine false return (lines 107-108)
        const string input = "MSBUILD : error MSB1001: Unknown switch.";

        var result = new DotnetBuildFilter().Apply(input);

        result.Should().Contain("MSB1001");
    }

    [Fact]
    public void Apply_SimpleMsbuildDiagnosticWithNonMatchingLine_BothBranchesCovered()
    {
        // "Some random output" → TryAddDiagnosticLine false → SimpleDiagnosticPattern false → continue (lines 80-82)
        // "MSBUILD : error MSB1001: ..." → SimpleDiagnosticPattern true → adds diagnostic (lines 85-98)
        // Duplicate → seen.Add false → continue (lines 86-89)
        const string input = """
                             Some random output
                             MSBUILD : error MSB1001: Unknown switch.
                             MSBUILD : error MSB1001: Unknown switch.
                             """;

        var result = new DotnetBuildFilter().Apply(input);

        result.Should().Contain("MSB1001");
    }

    [Fact]
    public void Apply_SingleWarning_NoErrors_UsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 133
        const string input = "/path/File.cs(1,1): warning CS0001: a warning message [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input);

        result.Should().Contain("1 warning");
        result.Should().NotContain("warnings");
    }

    [Fact]
    public void Apply_SingleErrorAndSingleWarning_SuppressionLineUsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 147
        const string input =
            "/path/File.cs(1,1): error CS0001: error message [Project.csproj]\n/path/File.cs(2,1): warning CS0002: a warning [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input);

        result.Should().Contain("1 warning").And.Contain("suppressed");
    }

    [Fact]
    public void Apply_SingleProjectOutputWithNoElapsed_UsesSingularProjectForm()
    {
        // Covers BuildContext elapsed-empty + single-project branch (lines 165-166, condition at 164, 166)
        const string input = "  MyProject -> /path/MyProject.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("1 project");
        result.Should().NotContain("projects");
    }

    [Fact]
    public void Apply_MultipleProjectsWithNoElapsed_UsesPluralProjectForm()
    {
        // Covers BuildContext plural branch for projectCount != 1 (condition at 166)
        const string input = "  MyProject -> /path/MyProject.dll\n  OtherProject -> /path/OtherProject.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("2 projects");
    }

    [Fact]
    public void Apply_MultipleWarningsWithErrors_UsesPlural()
    {
        // Covers (warnings.Count == 1 ? "" : "s") false branch at line 147 — plural "warnings suppressed"
        const string input = """
                             /path/File.cs(1,1): error CS0001: error message [Project.csproj]
                             /path/File.cs(2,1): warning CS0002: warning one [Project.csproj]
                             /path/File.cs(3,1): warning CS0003: warning two [Project.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input);

        result.Should().Contain("2 warnings suppressed");
    }

    [Fact]
    public void Apply_FormatElapsed_WhenTimeSpanPatternDoesNotMatch_ReturnsEmpty()
    {
        // Covers FormatElapsed early return (lines 181-182) via reflection
        var method = typeof(DotnetBuildFilter)
            .GetMethod("FormatElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, ["Time Elapsed invalid-no-digits"])!;

        result.Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetBuildFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

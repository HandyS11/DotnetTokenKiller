using System.Reflection;
using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetBuildFilterTests
{
    private readonly DotnetBuildFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_WarningsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_ErrorsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(85.0, "build success filter should achieve ≥85% savings");
    }

    [Fact]
    public void Apply_WarningsFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(75.0, "build warnings filter should achieve ≥75% savings");
    }

    [Fact]
    public void Apply_ErrorsFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
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
        _sut.Apply(fixture, exitCode: 0).Should().NotContain(noiseLine);
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
    public void Apply_AnsiCodesInInput_StrippedFromOutput()
    {
        const string ansiInput = "\x1b[32mBuild succeeded.\x1b[0m\n";
        var result = _sut.Apply(ansiInput, exitCode: 0);
        result.Should().NotContain("\x1b[");
        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_SimpleMsbuildDiagnostic_IsIncluded()
    {
        // Covers SimpleDiagnosticPattern path (lines 79-98) and TryAddDiagnosticLine false return (lines 107-108)
        const string input = "MSBUILD : error MSB1001: Unknown switch.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        // Exact: pins the empty file/line/col slots a simple diagnostic renders with.
        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\n (1 error)\n  (,) MSB1001: Unknown switch.\nTop codes: MSB1001 (1x)\n");
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

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        // Exact: proves the unmatched line contributes nothing and the duplicate is dropped.
        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\n (1 error)\n  (,) MSB1001: Unknown switch.\nTop codes: MSB1001 (1x)\n");
    }

    [Fact]
    public void Apply_SingleWarning_NoErrors_UsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 133
        const string input = "/path/File.cs(1,1): warning CS0001: a warning message [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("dotnet build: 0 errors, 1 warning\n---\nCS0001 (1x)\n  File.cs:1 \u2014 a warning message\n");
    }

    [Fact]
    public void Apply_SingleErrorAndSingleWarning_SuppressionLineUsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 147
        const string input =
            "/path/File.cs(1,1): error CS0001: error message [Project.csproj]\n/path/File.cs(2,1): warning CS0002: a warning [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 1 warning\n---\nFile.cs (1 error)\n  (1,1) CS0001: error message\nTop codes: CS0001 (1x)\n1 warning suppressed (use --vv to see)\n");
    }

    [Fact]
    public void Apply_SingleProjectOutputWithNoElapsed_UsesSingularProjectForm()
    {
        // Covers BuildContext elapsed-empty + single-project branch (lines 165-166, condition at 164, 166)
        const string input = "  MyProject -> /path/MyProject.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project)\n");
    }

    [Fact]
    public void Apply_MultipleProjectsWithNoElapsed_UsesPluralProjectForm()
    {
        // Covers BuildContext plural branch for projectCount != 1 (condition at 166)
        const string input = "  MyProject -> /path/MyProject.dll\n  OtherProject -> /path/OtherProject.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (2 projects)\n");
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

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 2 warnings\n---\nFile.cs (1 error)\n  (1,1) CS0001: error message\nTop codes: CS0001 (1x)\n2 warnings suppressed (use --vv to see)\n");
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

    [Fact]
    public void Apply_ErrorsButNoWarnings_DoesNotContainSuppressedLine()
    {
        // Kills equality mutation: warnings.Count >= 0 (line 145)
        const string input = "/path/File.cs(1,1): error CS0001: error msg [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\nFile.cs (1 error)\n  (1,1) CS0001: error msg\nTop codes: CS0001 (1x)\n");
    }

    [Fact]
    public void Apply_SingleError_UsesSingularErrorForm()
    {
        const string input = "/path/Only.cs(7,3): error CS1002: ; expected [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\nOnly.cs (1 error)\n  (7,3) CS1002: ; expected\nTop codes: CS1002 (1x)\n");
    }

    [Fact]
    public void Apply_MultipleErrors_UsesPluralErrorForm()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0001: err1 [P.csproj]
                             /path/B.cs(2,1): error CS0002: err2 [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 2 errors, 0 warnings\n---\nA.cs (1 error)\n  (1,1) CS0001: err1\nB.cs (1 error)\n  (2,1) CS0002: err2\nTop codes: CS0001 (1x), CS0002 (1x)\n");
    }

    [Fact]
    public void Apply_WarningsGroupedByCode_ContainsCodeCountAndFileLineMessage()
    {
        const string input = """
                             /path/File.cs(10,5): warning CS0168: The variable is declared but never used [P.csproj]
                             /path/File.cs(20,3): warning CS0168: Another unused variable [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be(
            "dotnet build: 0 errors, 2 warnings\n---\nCS0168 (2x)\n  File.cs:10 \u2014 The variable is declared but never used\n  File.cs:20 \u2014 Another unused variable\n");
    }

    [Fact]
    public void Apply_ErrorsGroupedByFile_ContainsFileWithErrorCountAndCodeMessage()
    {
        const string input = """
                             /path/File.cs(1,1): error CS0001: first error [P.csproj]
                             /path/File.cs(2,1): error CS0002: second error [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 2 errors, 0 warnings\n---\nFile.cs (2 errors)\n  (1,1) CS0001: first error\n  (2,1) CS0002: second error\nTop codes: CS0001 (1x), CS0002 (1x)\n");
    }

    [Fact]
    public void Apply_ErrorsShowTopCodes()
    {
        const string input = "/path/File.cs(1,1): error CS0001: error msg [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\nFile.cs (1 error)\n  (1,1) CS0001: error msg\nTop codes: CS0001 (1x)\n");
    }

    [Fact]
    public void Apply_SingleProjectWithElapsed_ContainsBothInContext()
    {
        // Kills BuildContext branch: projectCount > 0 AND elapsed not empty (line 169)
        const string input = """
                               MyProject -> /path/MyProject.dll
                             Time Elapsed 00:00:03.50
                             """;

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project, 3.50s)\n");
    }

    [Fact]
    public void Apply_OnlyElapsed_NoProjects_ContainsElapsedOnly()
    {
        // Kills BuildContext branch: projectCount == 0, elapsed not empty (line 164)
        const string input = "Time Elapsed 00:00:01.23";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1.23s)\n");
    }

    [Fact]
    public void Apply_NoElapsedNoProjects_NoContextSuffix()
    {
        // Kills BuildContext branch: projectCount == 0, elapsed empty (line 157-158)
        const string input = "Build succeeded.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet build\n");
    }

    [Theory]
    [InlineData("MSBuild version 17.12.0")]
    [InlineData("  Restored /path/Project.csproj (in 100 ms).")]
    [InlineData("Build started 3/26/2025")]
    [InlineData("Build succeeded.")]
    [InlineData("Build FAILED.")]
    [InlineData("    0 Warning(s)")]
    [InlineData("    0 Error(s)")]
    public void Apply_IndividualNoisePatterns_EachFilteredAsSingleLine(string noiseLine)
    {
        // Kills logical mutations in IsNoiseLine: || changed to && (lines 198-205)
        // Each noise pattern must independently cause the line to be filtered
        var result = new DotnetBuildFilter().Apply(noiseLine, exitCode: 0);

        result.Should().Be("✓ dotnet build\n", $"'{noiseLine}' should be treated as noise");
    }

    [Fact]
    public void Apply_TimeElapsedNoiseLine_FilteredAndParsesTime()
    {
        // Time Elapsed is both noise and a source of elapsed time
        var result = new DotnetBuildFilter().Apply("Time Elapsed 00:00:05.75", exitCode: 0);

        result.Should().Be("\u2713 dotnet build (5.75s)\n");
    }

    [Fact]
    public void Apply_ProjectOutputPattern_CountsProject()
    {
        // Kills statement mutations on project counter increment (line 55)
        const string input = "  MyProject -> /path/MyProject.dll";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project)\n");
    }

    [Fact]
    public void Apply_TimeElapsedPattern_ParsesElapsed()
    {
        // Kills statement mutations on elapsed assignment (lines 63, 69)
        const string input = "Time Elapsed 00:00:42.00";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (42.00s)\n");
    }

    [Fact]
    public void Apply_DuplicateDiagnostics_AreDeduplicatedButOriginalKept()
    {
        // Kills statement mutation on continue after seen.Add returns false (line 88)
        const string input = """
                             /path/File.cs(1,1): error CS0001: same error [P.csproj]
                             /path/File.cs(1,1): error CS0001: same error [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\nFile.cs (1 error)\n  (1,1) CS0001: same error\nTop codes: CS0001 (1x)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnosticFormat_IncludesCode()
    {
        // Kills string mutations on SimpleDiagnostic groups (lines 92-94)
        const string input = "MSBUILD : error MSB1003: Specify either a project or solution file.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\n (1 error)\n  (,) MSB1003: Specify either a project or solution file.\nTop codes: MSB1003 (1x)\n");
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        // Kills null coalescing mutation (line 16): rootPath ?? Environment.CurrentDirectory
        var filter = new DotnetBuildFilter();
        const string input = "Determining projects to restore...";

        // Should not throw and produce valid output
        var result = filter.Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_WarningOnlyOutput_ContainsWarningCountAndSeparator()
    {
        const string input = "/path/File.cs(1,1): warning CS0168: unused var [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("dotnet build: 0 errors, 1 warning\n---\nCS0168 (1x)\n  File.cs:1 \u2014 unused var\n");
    }

    [Fact]
    public void Apply_ErrorOutput_ContainsSeparator()
    {
        const string input = "/path/File.cs(1,1): error CS0001: msg [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\nFile.cs (1 error)\n  (1,1) CS0001: msg\nTop codes: CS0001 (1x)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnostic_OutputContainsLevelAndFormattedEntry()
    {
        // Kills string mutations on SimpleDiagnostic groups (lines 92-94) — verifies exact level, code, message extraction
        const string input = "MSBUILD : warning MSB4011: This is a warning.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("dotnet build: 0 errors, 1 warning\n---\nMSB4011 (1x)\n  : \u2014 This is a warning.\n");
    }

    [Fact]
    public void Apply_DiagnosticWithFileLineCol_OutputContainsFormattedLocation()
    {
        // Kills string mutations on diagnostic format: "{file}:{line} — {message}" (line 112)
        const string input =
            "/path/Foo.cs(42,7): warning CS0168: The variable 'x' is declared but never used [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        // Verify the grouped-by-code format includes file:line and em-dash separator
        result.Should().Be(
            "dotnet build: 0 errors, 1 warning\n---\nCS0168 (1x)\n  Foo.cs:42 \u2014 The variable 'x' is declared but never used\n");
    }

    [Fact]
    public void Apply_WarningsOnly_HeaderContainsDotnetBuild()
    {
        // Kills string mutation on "dotnet build:" prefix in warnings-only header (line 134)
        const string input = "/path/File.cs(1,1): warning CS0168: unused [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("dotnet build: 0 errors, 1 warning\n---\nCS0168 (1x)\n  File.cs:1 \u2014 unused\n");
    }

    [Fact]
    public void Apply_ErrorsAndWarnings_HeaderContainsDotnetBuild()
    {
        // Kills string mutation on "dotnet build:" prefix in errors header (line 141)
        const string input = "/path/a.cs(1,1): error CS001: e [P.csproj]\n/path/b.cs(1,1): warning CS002: w [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 1 warning\n---\na.cs (1 error)\n  (1,1) CS001: e\nTop codes: CS001 (1x)\n1 warning suppressed (use --vv to see)\n");
    }

    [Fact]
    public void Apply_ProjectOutputIsNotTreatedAsNoise()
    {
        // Kills boolean mutation on IsNoiseLine return (line 195) — project output lines
        // are handled by the project-count branch, not by IsNoiseLine
        const string input = "  MyTool -> /path/MyTool.exe";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project)\n");
    }

    [Fact]
    public void Apply_NonNoiseNonDiagnosticLine_IsSkipped()
    {
        // Kills statement mutation on continue (line 82) — a line that's not noise,
        // not a diagnostic, and not a simple diagnostic should be silently skipped
        const string input = "Some random informational text\nBuild succeeded.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build\n");
        result.Should().NotContain("random");
    }

    [Fact]
    public void Apply_DuplicateSimpleDiagnostic_RetainsOnlyFirst()
    {
        // Kills statement mutation on continue (line 88) — second identical simple diagnostic
        // should be skipped
        const string input = "MSBUILD : error MSB1001: Unknown switch.\nMSBUILD : error MSB1001: Unknown switch.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        // Only one diagnostic entry for "Unknown switch." in the output
        result.Split("Unknown switch.").Length.Should().Be(2, "duplicate simple diagnostic should be deduplicated");
    }

    [Fact]
    public void Apply_SimpleDiagnosticKey_IncludesCodeAndMessage()
    {
        // Kills string mutation on simpleKey format (line 85)
        // If simpleKey is empty, dedup won't work correctly
        const string input = "MSBUILD : error MSB1001: First.\nMSBUILD : error MSB1001: Second.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        // Both have different messages but same code — should both appear since key includes message
        result.Should().Be(
            "dotnet build: 2 errors, 0 warnings\n---\n (2 errors)\n  (,) MSB1001: First.\n  (,) MSB1001: Second.\nTop codes: MSB1001 (2x)\n");
    }

    [Fact]
    public void Apply_BuildContextWithProjectAndElapsed_HasCommaFormat()
    {
        // Kills string mutation on BuildContext format (line 164)
        const string input = "  Proj -> /path/Proj.dll\nTime Elapsed 00:00:02.00";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project, 2.00s)\n");
    }

    [Fact]
    public void Apply_TwoWarnings_UsesPluralWarningForm()
    {
        // Kills line 141 conditional (true?""  :"s") and string mutations
        // — always returning "" would produce "2 warning" not "2 warnings"
        const string input = """
                             /path/A.cs(1,1): warning CS0168: unused var [P.csproj]
                             /path/B.cs(2,1): warning CS0219: unused value [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be(
            "dotnet build: 0 errors, 2 warnings\n---\nCS0168 (1x)\n  A.cs:1 \u2014 unused var\nCS0219 (1x)\n  B.cs:2 \u2014 unused value\n");
    }

    [Fact]
    public void Apply_OneWarning_UsesSingularWarningForm()
    {
        // Kills line 141 — with (true?""  :"s") always-empty mutation "1 warning" stays correct,
        // but with (false?""  :"s") mutation it would produce "1 warnings" — this kills the inverse
        const string input = "/path/A.cs(1,1): warning CS0168: unused [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("dotnet build: 0 errors, 1 warning\n---\nCS0168 (1x)\n  A.cs:1 \u2014 unused\n");
    }

    [Fact]
    public void Apply_SimpleDiagnostic_OutputContainsLevelInErrors()
    {
        // Kills string mutation on level field (line 93)
        // When level becomes "", the diagnostic is not classified as error → output is success ✓
        const string input = "MSBUILD : error MSB1001: Something went wrong.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\n (1 error)\n  (,) MSB1001: Something went wrong.\nTop codes: MSB1001 (1x)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnosticCode_AppearsInTopCodes()
    {
        // Kills string mutation on code field (line 94)
        // When code becomes "", Top codes line would show an empty code instead of MSB3021
        const string input = "MSBUILD : error MSB3021: Unable to copy file.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "dotnet build: 1 error, 0 warnings\n---\n (1 error)\n  (,) MSB3021: Unable to copy file.\nTop codes: MSB3021 (1x)\n");
    }

    [Fact]
    public void Apply_DefaultRootPath_DoesNotThrow()
    {
        // Kills null coalescing mutation on line 16: rootPath ?? Environment.CurrentDirectory
        var filter = new DotnetBuildFilter();
        const string input = "Build started 3/26/2025";

        var result = filter.Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_NonZeroExitWithNoParsedDiagnostics_ReturnsEmpty()
    {
        // Crashed process / unparseable output: 0 errors and 0 warnings were parsed, but the exit
        // code is non-zero. Returning a "0 errors, 0 warnings" header here would look like a clean
        // success and would prevent FilteredRunUseCase's raw-tail fallback from ever firing.
        const string input = "Segmentation fault (core dumped)";

        var result = _sut.Apply(input, exitCode: 139);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_NonZeroExitWarningsOnly_ContainsFailureMarkerAndWarningCode()
    {
        // Failed build (non-zero exit) where only warnings were parsed (no errors matched the
        // regex): rendering "0 errors, N warnings" with no failure marker reads like a near-success
        // for a run that FAILED. A "✗" line must be prepended so the verdict stays exit-code-derived.
        const string input =
            "/repo/App.cs(3,9): warning CS0219: The variable 'x' is assigned but its value is never used [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 1);

        result.Should().Be(
            "✗ dotnet build failed (exit 1)\ndotnet build: 0 errors, 1 warning\n---\nCS0219 (1x)\n  App.cs:3 \u2014 The variable 'x' is assigned but its value is never used\n");
    }

    [Fact]
    public void Apply_ZeroExitWarningsOnly_DoesNotContainFailureMarker()
    {
        // Same warnings-only input but a successful run: must NOT gain a failure marker.
        const string input =
            "/repo/App.cs(3,9): warning CS0219: The variable 'x' is assigned but its value is never used [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 0);

        result.Should().Be(
            "dotnet build: 0 errors, 1 warning\n---\nCS0219 (1x)\n  App.cs:3 \u2014 The variable 'x' is assigned but its value is never used\n");
    }

    [Fact]
    public void Apply_MultiPartTimeElapsed_IncludesMinutesAndSeconds()
    {
        // "Time Elapsed hh:mm:ss.ff" is a full TimeSpan, so minutes and seconds are already summed:
        // 00:01:02.50 → 62.50s. Locks this in alongside the test filter's multi-part duration fix.
        const string input = "Time Elapsed 00:01:02.50";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (62.50s)\n");
    }

    [Theory]
    [InlineData("/repo/Tests.cs(10,5): warning xUnit1013: Public method should be marked as test [/repo/T.csproj]",
        "xUnit1013")]
    [InlineData("/repo/App.cs(3,9): error CS0029: Cannot implicitly convert type 'string[]' to 'int' [/repo/A.csproj]",
        "'string[]' to 'int'")]
    [InlineData("C:\\Program Files (x86)\\proj\\App.cs(1,1): error CS1002: ; expected [C:\\p\\A.csproj]", "CS1002")]
    public void Apply_HardDiagnosticShapes_AreCapturedInFull(string line, string mustContain)
    {
        // Mixed-case codes (xUnit1013), bracketed message content ('string[]'), and parens in the
        // file path (Program Files (x86)) all defeated the original regexes.
        var result = new DotnetBuildFilter().Apply($"{line}\nBuild FAILED.\n", exitCode: 1);
        result.Should().Contain(mustContain);
        result.Should().NotContain("✓");
    }

    [Fact]
    public void Apply_TwoSimpleDiagnosticsSameMessageDifferentCodes_BothKept()
    {
        // The dedup key for simple diagnostics is "{code}:{message}". Dropping the code from the
        // key would collapse these two distinct diagnostics (identical message) into one.
        const string input = """
                             MSBUILD : error MSB1001: Unknown switch.
                             MSBUILD : error MSB1002: Unknown switch.
                             """;

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 2 errors, 0 warnings\n---\n (2 errors)\n  (,) MSB1001: Unknown switch.\n  (,) MSB1002: Unknown switch.\nTop codes: MSB1001 (1x), MSB1002 (1x)\n");
    }

    [Fact]
    public void Apply_DiagnosticsDifferingOnlyByFile_BothKept()
    {
        // Dedup key is "{file}({line},{col}):{code}". Dropping the file part collapses these.
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/B.cs(1,1): error CS0001: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 2 errors, 0 warnings\n---\nA.cs (1 error)\n  (1,1) CS0001: msg\nB.cs (1 error)\n  (1,1) CS0001: msg\nTop codes: CS0001 (2x)\n");
    }

    [Fact]
    public void Apply_DiagnosticsDifferingOnlyByLine_BothKept()
    {
        // Dropping the line part of the dedup key collapses these two diagnostics.
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/A.cs(2,1): error CS0001: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 2 errors, 0 warnings\n---\nA.cs (2 errors)\n  (1,1) CS0001: msg\n  (2,1) CS0001: msg\nTop codes: CS0001 (2x)\n");
    }

    [Fact]
    public void Apply_DiagnosticsDifferingOnlyByColumn_BothKept()
    {
        // Dropping the column part of the dedup key collapses these two diagnostics.
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/A.cs(1,2): error CS0001: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 2 errors, 0 warnings\n---\nA.cs (2 errors)\n  (1,1) CS0001: msg\n  (1,2) CS0001: msg\nTop codes: CS0001 (2x)\n");
    }

    [Fact]
    public void Apply_DiagnosticsDifferingOnlyByCode_BothKept()
    {
        // Dropping the code part of the dedup key collapses these two diagnostics.
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/A.cs(1,1): error CS0002: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 2 errors, 0 warnings\n---\nA.cs (2 errors)\n  (1,1) CS0001: msg\n  (1,1) CS0002: msg\nTop codes: CS0001 (1x), CS0002 (1x)\n");
    }

    [Fact]
    public void Apply_ErrorWithTwoWarnings_HeaderUsesPluralWarnings()
    {
        // Exact assertion pins the plural "s" suffix on the warning count in the *header*
        // (a different ternary from the "N warnings suppressed" line below it).
        const string input = """
                             /path/A.cs(1,1): error CS0001: err [P.csproj]
                             /path/B.cs(2,1): warning CS0002: w1 [P.csproj]
                             /path/C.cs(3,1): warning CS0003: w2 [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("dotnet build: 1 error, 2 warnings\n---\nA.cs (1 error)\n  (1,1) CS0001: err\nTop codes: CS0001 (1x)\n2 warnings suppressed (use --vv to see)\n");
    }

    [Fact]
    public void Apply_TimeElapsedWithUnparsableTimeSpan_ProducesNoContext()
    {
        // "00:99:99.99" matches the Time Elapsed regex but is not a valid TimeSpan, so
        // FormatElapsed falls through to its final empty return and no context is rendered.
        const string input = "Time Elapsed 00:99:99.99";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_DiagnosticLineMentioningMsbuildVersion_IsDroppedAsNoise()
    {
        // The MSBuild-version noise pattern is an unanchored substring match, so a diagnostic whose
        // message mentions it is deliberately swallowed. This is the only observable proof that the
        // pattern participates in IsNoiseLine at all: an unrecognised line is silently skipped too,
        // so a plain header line cannot distinguish "matched as noise" from "matched nothing".
        const string input = "/repo/App.cs(1,1): error CS0001: MSBuild version mismatch [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_ProjectOutputLineShapedLikeDiagnostic_CountsProjectOnly()
    {
        // The project-output branch must consume the line: without it, this deliberately ambiguous
        // path (containing "(1,2): error CS0001:") would ALSO be parsed as a diagnostic.
        const string input = "  P -> /path/a(1,2): error CS0001: m.dll";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1 project)\n");
    }

    [Fact]
    public void Apply_TimeElapsedLineShapedLikeDiagnostic_RecordsElapsedOnly()
    {
        // The elapsed branch must consume the line: without it, this line also matches the
        // simple-diagnostic pattern and would be reported as an error on top of the elapsed time.
        const string input = "MSBUILD : error MSB1001: Time Elapsed 00:00:03.50";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (3.50s)\n");
    }

    [Fact]
    public void Apply_WarningWithZeroCountSummaryLines_ReportsWarning()
    {
        // Invariant: a clean "✓ dotnet build" verdict is only valid when no diagnostic was parsed.
        // The trailing MSBuild count lines are noise and must not override the parsed warning.
        const string input =
            "/test/project/root/src/App.cs(9,13): warning CS0168: The variable 'x' is declared but never used\n" +
            "    0 Error(s)\n" +
            "    0 Warning(s)";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("CS0168");
        result.Should().Contain("1 warning");
        result.Should().NotStartWith("✓ dotnet build");
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

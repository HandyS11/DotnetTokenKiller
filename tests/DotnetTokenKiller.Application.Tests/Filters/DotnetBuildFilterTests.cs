using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Tests.Helpers;
using DotnetTokenKiller.Domain.Text;
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

    [Fact]
    public void Apply_ErrorsFixture_EndsWithTheOneVerdictLine()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");

        var result = _sut.Apply(fixture, exitCode: 1);

        // `| tail -1` must carry the verdict, and the verdict is stated once: exactly one line
        // names the command.
        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✗ dotnet build failed (exit 1): 3 errors, 1 warning (2 projects, 1.89s)");
        result.Split('\n').Count(line => line.Contains("dotnet build", StringComparison.Ordinal)).Should().Be(1);
        result.Should().NotContain("---");
    }

    [Fact]
    public void Apply_WarningsFixture_EndsWithTheSuccessVerdictLine()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✓ dotnet build: 0 errors, 3 warnings (2 projects, 2.67s)");
        result.Split('\n').Count(line => line.Contains("dotnet build", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void Apply_NonZeroExitWarningsOnly_StatesTheVerdictOnceAndLast()
    {
        const string input =
            "/repo/App.cs(3,9): warning CS0219: The variable 'x' is assigned but its value is never used [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 2);

        var lines = result.TrimEnd('\n').Split('\n');
        lines[^1].Should().Be("✗ dotnet build failed (exit 2): 0 errors, 1 warning (1 project)");
        lines.Count(line => line.Contains('✗', StringComparison.Ordinal)).Should().Be(1);
        lines.Count(line => line.Contains("dotnet build", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void Apply_WarningsWithProjectAndElapsed_PutsTheContextOnTheVerdictLine()
    {
        const string input = """
                             /repo/App.cs(3,9): warning CS0219: unused [/repo/A.csproj]
                               A -> /repo/A/bin/Debug/net10.0/A.dll
                             Time Elapsed 00:00:02.00
                             """;

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 0);

        result.Should().Be(
            "App.cs(3,9) CS0219: unused\n\u2713 dotnet build: 0 errors, 1 warning (1 project, 2.00s)\n");
    }

    [Fact]
    public void Apply_ZeroExitWithParsedErrors_WarnsInsteadOfTicking()
    {
        // The exit code stays the verdict, so this is not a failure; but a green tick beside an error
        // count would mislead anyone who reads only the last line, so the glyph is a warning.
        const string input = "/repo/X.cs(1,1): error CS0001: boom [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1].Should().Be("⚠ dotnet build: 1 error, 0 warnings (1 project)");
        result.Should().NotContain("✓").And.NotContain("✗");
    }

    [Fact]
    public Task Apply_MultitargetErrorsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_multitarget_errors.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_MultitargetErrorsFixture_CountsTheMultiTargetedProjectOnce()
    {
        // One project built for net8.0 and net10.0 reports its error once per target framework. The
        // failed build printed no "X -> …dll" line, so the project is known only from the "[project]"
        // suffix, and its two target frameworks are one project.
        var fixture = LoadFixture("dotnet_build_multitarget_errors.txt");

        var result = _sut.Apply(fixture, exitCode: 1);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✗ dotnet build failed (exit 1): 2 errors (1 shown), 0 warnings (1 project, 1.13s)");
    }

    [Fact]
    public void Apply_FailedBuildOfOneProject_StatesTheProjectCount()
    {
        const string input = "/path/File.cs(1,1): error CS0001: boom [/path/App.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✗ dotnet build failed (exit 1): 1 error, 0 warnings (1 project)");
    }

    [Fact]
    public void Apply_ProjectOutputPerTargetFramework_CountsTheProjectOnce()
    {
        const string input = """
                               A -> /p/A/bin/Debug/net8.0/A.dll
                               A -> /p/A/bin/Debug/net10.0/A.dll
                               B -> /p/B/bin/Debug/net10.0/B.dll
                             """;

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet build (2 projects)\n");
    }

    [Fact]
    public void Apply_BuiltAndFailedProjects_AreCountedTogether()
    {
        // Lib built (an output line); App did not (only its error names it). A build that stopped at the
        // first failure still involved both.
        const string input = """
                               Lib -> /p/Lib/bin/Debug/net10.0/Lib.dll
                             /p/App/Program.cs(1,1): error CS0001: boom [/p/App/App.csproj]
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✗ dotnet build failed (exit 1): 1 error, 0 warnings (2 projects)");
    }

    [Fact]
    public void Apply_SolutionSuffixedErrorNextToOutputLines_AddsNoProject()
    {
        // `dotnet build Sol.slnx` suffixes a restore failure with the solution, not a project, and a
        // solution is not one of the projects built: Lib is the only one this run names.
        const string input = """
                               Lib -> /r/Lib/bin/Debug/net10.0/Lib.dll
                             /r/App/App.csproj : error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org [/r/Sol.slnx]
                             """;

        var result = new DotnetBuildFilter("/r").Apply(input, exitCode: 1);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("\u2717 dotnet build failed (exit 1): 1 error, 0 warnings (1 project)");
    }

    [Fact]
    public void Apply_SolutionSuffixedWarningsOfTwoBuiltProjects_CountTwoProjects()
    {
        const string input = """
                               A -> /r/A/bin/Debug/net10.0/A.dll
                               B -> /r/B/bin/Debug/net10.0/B.dll
                             /r/A/A.csproj : warning NU1603: A depends on Foo (>= 1.0.0) but Foo 1.0.0 was not found. [/r/Sol.slnx]
                             """;

        var result = new DotnetBuildFilter("/r").Apply(input, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("\u2713 dotnet build: 0 errors, 1 warning (2 projects)");
    }

    [Fact]
    public void Apply_OnlySolutionSuffixedDiagnosticsAndNoOutputLine_ShowsNoProjectCount()
    {
        // Nothing names a project, so there is no count to state, and the context is the duration alone.
        const string input = """
                             /r/A/A.csproj : error NU1101: Unable to find package Foo. [/r/Sol.slnx]
                             /r/B/B.csproj : error NU1101: Unable to find package Bar. [/r/Sol.slnx]
                             Time Elapsed 00:00:01.00
                             """;

        var result = new DotnetBuildFilter("/r").Apply(input, exitCode: 1);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1.00s)");
    }

    [Fact]
    public void Apply_OutputLineNamingADifferentAssembly_AndItsProjectsDiagnostic_CountOneProject()
    {
        // An <AssemblyName> override: the output line is "{project} -> …/{assembly}.dll", named after the
        // project, so it and the diagnostic suffixed with X.csproj are the same project.
        const string input = """
                               X -> /r/X/bin/Debug/net10.0/Renamed.dll
                             /r/X/Program.cs(1,1): warning CS0219: unused [/r/X/X.csproj]
                             """;

        var result = new DotnetBuildFilter("/r").Apply(input, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("\u2713 dotnet build: 0 errors, 1 warning (1 project)");
    }

    [Fact]
    public void Apply_ProjectWithOutputLineAndDiagnostics_CountsOnce()
    {
        const string input = """
                             /p/A/X.cs(1,1): warning CS0219: unused [/p/A/A.csproj::TargetFramework=net8.0]
                             /p/A/X.cs(1,1): warning CS0219: unused [/p/A/A.csproj::TargetFramework=net10.0]
                               A -> /p/A/bin/Debug/net8.0/A.dll
                               A -> /p/A/bin/Debug/net10.0/A.dll
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1]
            .Should().Be("✓ dotnet build: 0 errors, 1 warning (1 project)");
    }

    [Fact]
    public void Apply_ErrorsAndWarnings_ShareOneLayoutGroupedByFile()
    {
        // Warnings no longer group by code with "File:line — msg": both levels read
        // "{file} ({n} …)" and "  ({line},{col}) {CODE}: {msg}", so the column is not dropped. A file that
        // comes to one line has no heading: B.cs is led by its own name.
        const string warnings = """
                                /path/A.cs(3,7): warning CS0219: unused one [P.csproj]
                                /path/A.cs(9,2): warning CS0168: unused two [P.csproj]
                                /path/B.cs(1,1): warning CS0219: unused three [P.csproj]
                                """;
        const string errors = """
                              /path/A.cs(3,7): error CS0219: unused one [P.csproj]
                              /path/A.cs(9,2): error CS0168: unused two [P.csproj]
                              /path/B.cs(1,1): error CS0219: unused three [P.csproj]
                              """;

        var warningBody = new DotnetBuildFilter("/path").Apply(warnings, exitCode: 0);
        var errorBody = new DotnetBuildFilter("/path").Apply(errors, exitCode: 1);

        warningBody.Should().Be(
            "A.cs (2 warnings)\n  (3,7) CS0219: unused one\n  (9,2) CS0168: unused two\n"
            + "B.cs(1,1) CS0219: unused three\n"
            + "✓ dotnet build: 0 errors, 3 warnings (1 project)\n");
        errorBody.Should().Be(
            "A.cs (2 errors)\n  (3,7) CS0219: unused one\n  (9,2) CS0168: unused two\n"
            + "B.cs(1,1) CS0219: unused three\n"
            + "✗ dotnet build failed (exit 1): 3 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_WarningsFixture_ListsWarningsByFileWithPositionAndCode()
    {
        var result = _sut.Apply(LoadFixture("dotnet_build_warnings.txt"), exitCode: 0);

        result.Should().Be(
            "src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs (2 warnings)\n"
            + "  (10,18) CS0219: The variable 'unused' is assigned but its value is never used\n"
            + "  (15,22) CS0219: The variable 'alsoUnused' is assigned but its value is never used\n"
            + "src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs(5,10) CS0169: The field '_unused' is never used\n"
            + "✓ dotnet build: 0 errors, 3 warnings (2 projects, 2.67s)\n");
    }

    [Fact]
    public void Apply_OneDistinctCode_PrintsNoTopCodes()
    {
        // "Top codes: CS0001 (3x)" repeats the file group above it.
        const string input = """
                             /path/A.cs(1,1): error CS0001: one [P.csproj]
                             /path/A.cs(2,1): error CS0001: two [P.csproj]
                             /path/B.cs(3,1): error CS0001: three [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().NotContain("Top codes");
    }

    [Fact]
    public void Apply_SeveralCodesAllShown_PrintsNoTopCodes()
    {
        // Every error is a line of its own: a list of the codes saying nothing the lines do not.
        var result = _sut.Apply(LoadFixture("dotnet_build_errors.txt"), exitCode: 1);

        result.Should().NotContain("Top codes");
    }

    [Fact]
    public void Apply_SeveralCodesAndMoreErrorsThanLines_PrintsTopCodes()
    {
        // MSBuild reports every error once per target framework (6), the report lists each once (3). The
        // codes are then ranked among the three listed: it shows which codes dominate, not the counts of
        // the six MSBuild reported.
        const string input = """
                             /path/A.cs(1,1): error CS0001: one [/path/P.csproj::TargetFramework=net8.0]
                             /path/A.cs(2,1): error CS0001: two [/path/P.csproj::TargetFramework=net8.0]
                             /path/B.cs(3,1): error CS0002: three [/path/P.csproj::TargetFramework=net8.0]
                             /path/A.cs(1,1): error CS0001: one [/path/P.csproj::TargetFramework=net10.0]
                             /path/A.cs(2,1): error CS0001: two [/path/P.csproj::TargetFramework=net10.0]
                             /path/B.cs(3,1): error CS0002: three [/path/P.csproj::TargetFramework=net10.0]
                                 6 Error(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "A.cs (2 errors)\n  (1,1) CS0001: one\n  (2,1) CS0001: two\nB.cs(3,1) CS0002: three\n"
            + "Top codes: CS0001 (2x), CS0002 (1x)\n"
            + "✗ dotnet build failed (exit 1): 6 errors (3 shown), 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_OneDistinctCodeAndMoreErrorsThanLines_PrintsNoTopCodes()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0001: one [/path/P.csproj::TargetFramework=net8.0]
                             /path/A.cs(1,1): error CS0001: one [/path/P.csproj::TargetFramework=net10.0]
                                 2 Error(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().NotContain("Top codes");
    }

    [Fact]
    public void Apply_SeveralWarningCodesAndMoreWarningsThanLines_PrintsTopCodes()
    {
        // The rule is the same for the warnings a build without errors lists.
        const string input = """
                             /path/A.cs(1,1): warning CS0001: one [/path/P.csproj::TargetFramework=net8.0]
                             /path/A.cs(2,1): warning CS0001: two [/path/P.csproj::TargetFramework=net8.0]
                             /path/B.cs(3,1): warning CS0002: three [/path/P.csproj::TargetFramework=net8.0]
                             /path/A.cs(1,1): warning CS0001: one [/path/P.csproj::TargetFramework=net10.0]
                             /path/A.cs(2,1): warning CS0001: two [/path/P.csproj::TargetFramework=net10.0]
                             /path/B.cs(3,1): warning CS0002: three [/path/P.csproj::TargetFramework=net10.0]
                                 6 Warning(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Contain("Top codes: CS0001 (2x), CS0002 (1x)\n");
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
    public void Apply_AlreadyStrippedAnsiInput_ParsesCorrectly()
    {
        // The filter no longer strips ANSI itself: FilteredOutputPipeline strips once, before any
        // filter runs, so this pins that the parser handles text that has already been through
        // AnsiStrip.Strip cleanly, with no stray escape artifacts confusing the "nothing parsed" path.
        var stripped = AnsiStrip.Strip("\x1b[32mBuild succeeded.\x1b[0m\n");

        var result = _sut.Apply(stripped, exitCode: 0);

        result.Should().NotContain("\x1b[");
        result.Should().Be("\u2713 dotnet build\n");
    }

    [Fact]
    public void Apply_SimpleMsbuildDiagnostic_IsIncluded()
    {
        // Covers SimpleDiagnosticPattern path (lines 79-98) and TryAddDiagnosticLine false return (lines 107-108)
        const string input = "MSBUILD : error MSB1001: Unknown switch.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        // Exact: a tool-level diagnostic has no file, line or column, so "(no file)" stands where the
        // location would, on the one line it comes to, and no position follows. Emitting the empty slots
        // produced a nameless heading and a "(,)" stub that read like a location the filter had lost.
        result.Should().Be(
            "(no file) MSB1001: Unknown switch.\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings\n");
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
            "(no file) MSB1001: Unknown switch.\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings\n");
    }

    [Fact]
    public void Apply_SingleWarning_NoErrors_UsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 133
        const string input = "/path/File.cs(1,1): warning CS0001: a warning message [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("File.cs(1,1) CS0001: a warning message\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_SingleErrorAndSingleWarning_SuppressionLineUsesSingularForm()
    {
        // Covers (warnings.Count == 1 ? "" : "s") true branch at line 147
        const string input =
            "/path/File.cs(1,1): error CS0001: error message [Project.csproj]\n/path/File.cs(2,1): warning CS0002: a warning [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "File.cs(1,1) CS0001: error message\n1 warning not shown (dtk log)\n\u2717 dotnet build failed (exit 1): 1 error, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_ErrorsWithSixWarnings_SaysHowManyAreNotShownAndPointsAtDtkLog()
    {
        // MSBuild declares six warnings of which three are parsed. The line counts the six, with no
        // "(3 shown)" beside it, and names the saved log rather than --vv's raw dump.
        const string input = """
                             /path/A.cs(1,1): error CS0001: boom [/path/P.csproj]
                             /path/B.cs(1,1): warning CS0002: one [/path/P.csproj]
                             /path/B.cs(2,1): warning CS0003: two [/path/P.csproj]
                             /path/B.cs(3,1): warning CS0004: three [/path/P.csproj]
                                 1 Error(s)
                                 6 Warning(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Split('\n').Should().Contain("6 warnings not shown (dtk log)");
        result.Should().NotContain("--vv");
        // The warnings are hidden, not shown: the verdict counts them plainly, with no "(3 shown)".
        result.TrimEnd('\n').Split('\n')[^1].Should().Be("✗ dotnet build failed (exit 1): 1 error, 6 warnings (1 project)");
    }

    [Fact]
    public void Apply_ErrorsWithDeclaredWarningsNoneParsed_StillSaysTheWarningsAreNotShown()
    {
        // MSBuild declares six warnings, but no line of the output parses as one. They are still hidden
        // behind the error, so the line is printed from the declared count alone.
        const string input = """
                             /path/A.cs(1,1): error CS0001: boom [/path/P.csproj]
                                 1 Error(s)
                                 6 Warning(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Split('\n').Should().Contain("6 warnings not shown (dtk log)");
        result.TrimEnd('\n').Split('\n')[^1].Should().Be("✗ dotnet build failed (exit 1): 1 error, 6 warnings (1 project)");
    }

    [Fact]
    public void Apply_WarningsListedWithNoErrors_StatusKeepsTheShownCount()
    {
        // With no errors the warnings are listed, so a count of them that outruns the lines shown keeps
        // its "(3 shown)": the verdict does not claim more lines than the report printed.
        const string input = """
                             /path/A.cs(1,1): warning CS0002: one [/path/P.csproj]
                             /path/B.cs(2,1): warning CS0003: two [/path/P.csproj]
                             /path/C.cs(3,1): warning CS0004: three [/path/P.csproj]
                                 4 Warning(s)
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.TrimEnd('\n').Split('\n')[^1].Should().Be("✓ dotnet build: 0 errors, 4 warnings (3 shown) (1 project)");
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
        // Covers (warnings.Count == 1 ? "" : "s") false branch at line 147 — plural "warnings not shown"
        const string input = """
                             /path/File.cs(1,1): error CS0001: error message [Project.csproj]
                             /path/File.cs(2,1): warning CS0002: warning one [Project.csproj]
                             /path/File.cs(3,1): warning CS0003: warning two [Project.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "File.cs(1,1) CS0001: error message\n2 warnings not shown (dtk log)\n\u2717 dotnet build failed (exit 1): 1 error, 2 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_FormatElapsed_WhenTimeSpanPatternDoesNotMatch_ReturnsEmpty()
    {
        var result = MsBuildDiagnosticReport.FormatElapsed("Time Elapsed invalid-no-digits");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ErrorsButNoWarnings_DoesNotContainSuppressedLine()
    {
        // Kills equality mutation: warnings.Count >= 0 (line 145)
        const string input = "/path/File.cs(1,1): error CS0001: error msg [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "File.cs(1,1) CS0001: error msg\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_SingleError_UsesSingularErrorForm()
    {
        const string input = "/path/Only.cs(7,3): error CS1002: ; expected [Project.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "Only.cs(7,3) CS1002: ; expected\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
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
            "A.cs(1,1) CS0001: err1\nB.cs(2,1) CS0002: err2\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_WarningsGroupedByFile_ContainsFileWithWarningCountAndPositionCodeMessage()
    {
        const string input = """
                             /path/File.cs(10,5): warning CS0168: The variable is declared but never used [P.csproj]
                             /path/File.cs(20,3): warning CS0168: Another unused variable [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be(
            "File.cs (2 warnings)\n  (10,5) CS0168: The variable is declared but never used\n  (20,3) CS0168: Another unused variable\n\u2713 dotnet build: 0 errors, 2 warnings (1 project)\n");
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
            "File.cs (2 errors)\n  (1,1) CS0001: first error\n  (2,1) CS0002: second error\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
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
    public void Apply_FrenchCulture_RendersTheElapsedTimeWithADecimalPoint()
    {
        // "0,02s" cost a French-locale user about 670 summaries: the number followed the system culture.
        using var _ = new CultureScope("fr-FR");

        var result = new DotnetBuildFilter().Apply("Time Elapsed 00:00:00.02", exitCode: 0);

        result.Should().Be("\u2713 dotnet build (0.02s)\n");
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
            "File.cs(1,1) CS0001: same error\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnosticFormat_IncludesCode()
    {
        // Kills string mutations on SimpleDiagnostic groups (lines 92-94)
        const string input = "MSBUILD : error MSB1003: Specify either a project or solution file.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "(no file) MSB1003: Specify either a project or solution file.\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings\n");
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
    public void Apply_WarningOnlyOutput_EndsWithTheWarningCountVerdict()
    {
        const string input = "/path/File.cs(1,1): warning CS0168: unused var [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("File.cs(1,1) CS0168: unused var\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_ErrorOutput_EndsWithTheFailureVerdictAndHasNoSeparator()
    {
        const string input = "/path/File.cs(1,1): error CS0001: msg [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().NotContain("---");
        result.Should().Be(
            "File.cs(1,1) CS0001: msg\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnostic_OutputContainsLevelAndFormattedEntry()
    {
        // Kills string mutations on SimpleDiagnostic groups (lines 92-94) — verifies exact level, code,
        // message extraction, and that a fileless diagnostic drops the "{file}:{line} — " prefix
        // rather than rendering it empty as ": —".
        const string input = "MSBUILD : warning MSB4011: This is a warning.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("(no file) MSB4011: This is a warning.\n\u2713 dotnet build: 0 errors, 1 warning\n");
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
            "Foo.cs(42,7) CS0168: The variable 'x' is declared but never used\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_WarningsOnly_VerdictLineNamesDotnetBuild()
    {
        // Kills string mutation on the "dotnet build:" prefix of the warnings-only verdict line
        const string input = "/path/File.cs(1,1): warning CS0168: unused [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("File.cs(1,1) CS0168: unused\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_ErrorsAndWarnings_VerdictLineNamesDotnetBuild()
    {
        // Kills string mutation on the "dotnet build failed" prefix of the errors verdict line
        const string input = "/path/a.cs(1,1): error CS001: e [P.csproj]\n/path/b.cs(1,1): warning CS002: w [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "a.cs(1,1) CS001: e\n1 warning not shown (dtk log)\n\u2717 dotnet build failed (exit 1): 1 error, 1 warning (1 project)\n");
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
            "(no file) (2 errors)\n  MSB1001: First.\n  MSB1001: Second.\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings\n");
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
            "A.cs(1,1) CS0168: unused var\nB.cs(2,1) CS0219: unused value\n\u2713 dotnet build: 0 errors, 2 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_OneWarning_UsesSingularWarningForm()
    {
        // Kills line 141 — with (true?""  :"s") always-empty mutation "1 warning" stays correct,
        // but with (false?""  :"s") mutation it would produce "1 warnings" — this kills the inverse
        const string input = "/path/A.cs(1,1): warning CS0168: unused [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be("A.cs(1,1) CS0168: unused\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_SimpleDiagnostic_OutputContainsLevelInErrors()
    {
        // Kills string mutation on level field (line 93)
        // When level becomes "", the diagnostic is not classified as error → output is success ✓
        const string input = "MSBUILD : error MSB1001: Something went wrong.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "(no file) MSB1001: Something went wrong.\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings\n");
    }

    [Fact]
    public void Apply_SimpleDiagnosticCode_IsRenderedBeforeTheMessage()
    {
        // Kills string mutation on code field (line 94)
        // When code becomes "", the line would lose its MSB3021 prefix
        const string input = "MSBUILD : error MSB3021: Unable to copy file.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().Be(
            "(no file) MSB3021: Unable to copy file.\n\u2717 dotnet build failed (exit 1): 1 error, 0 warnings\n");
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
        // code is non-zero. Returning a "0 errors, 0 warnings" verdict here would look like a clean
        // success and would prevent FilteredRunUseCase's raw-tail fallback from ever firing.
        const string input = "Segmentation fault (core dumped)";

        var result = _sut.Apply(input, exitCode: 139);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_NonZeroExitWarningsOnly_ContainsFailureMarkerAndWarningCode()
    {
        // Failed build (non-zero exit) where only warnings were parsed (no errors matched the
        // regex): a "0 errors, N warnings" verdict with no failure marker reads like a near-success
        // for a run that FAILED. The one final line carries the "✗" and the exit code, so the
        // verdict stays exit-code-derived and is never stated twice.
        const string input =
            "/repo/App.cs(3,9): warning CS0219: The variable 'x' is assigned but its value is never used [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 1);

        result.Should().Be(
            "App.cs(3,9) CS0219: The variable 'x' is assigned but its value is never used\n✗ dotnet build failed (exit 1): 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_ZeroExitWarningsOnly_DoesNotContainFailureMarker()
    {
        // Same warnings-only input but a successful run: must NOT gain a failure marker.
        const string input =
            "/repo/App.cs(3,9): warning CS0219: The variable 'x' is assigned but its value is never used [/repo/A.csproj]";

        var result = new DotnetBuildFilter("/repo").Apply(input, exitCode: 0);

        result.Should().Be(
            "App.cs(3,9) CS0219: The variable 'x' is assigned but its value is never used\n\u2713 dotnet build: 0 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_MultiPartTimeElapsed_IncludesMinutesAndSeconds()
    {
        // "Time Elapsed hh:mm:ss.ff" is a full TimeSpan, so minutes and seconds are already summed:
        // 00:01:02.50 → 1m03s (a minute and more are shown in whole seconds). Locks this in alongside the
        // test filter's multi-part duration fix.
        const string input = "Time Elapsed 00:01:02.50";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().Be("\u2713 dotnet build (1m03s)\n");
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

        result.Should().Be("(no file) (2 errors)\n  MSB1001: Unknown switch.\n  MSB1002: Unknown switch.\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings\n");
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

        result.Should().Be("A.cs(1,1) CS0001: msg\nB.cs(1,1) CS0001: msg\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
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

        result.Should().Be("A.cs(1,1) (2,1) CS0001 ×2: msg\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void Apply_PositionsOfAGroupedLine_AreCappedAtTwentyWithTheRestCounted(int errorCount, bool capped)
    {
        // Reported in reverse, so "the first twenty" are the first twenty in line order.
        var input = string.Join("\n",
            Enumerable.Range(1, errorCount).Reverse().Select(i => $"/path/A.cs({i},1): error CS0001: msg [P.csproj]"));

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        var shown = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"({i},1)"));
        var more = capped ? $" … +{errorCount - 20} more" : string.Empty;
        result.Should().Be(
            $"A.cs{shown}{more} CS0001 ×{errorCount}: msg\n"
            + $"✗ dotnet build failed (exit 1): {errorCount} errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_ThreeThousandIdenticalErrors_ListTwentyPositionsAndKeepTheFullCounts()
    {
        var input = string.Join("\n",
            Enumerable.Range(1, 3000).Select(i => $"/path/A.cs({i},5): error CS0103: The name 'x' does not exist [P.csproj]")
                .Append("/path/A.cs(1,1): error CS0001: other [P.csproj]"));

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        var lines = result.TrimEnd('\n').Split('\n');
        lines[0].Should().Be("A.cs (3001 errors)");
        lines[1].Should().StartWith("  (1,5) (2,5) (3,5)").And.EndWith(" … +2980 more CS0103 ×3000: The name 'x' does not exist");
        lines[2].Should().Be("  (1,1) CS0001: other");
        lines[^1].Should().Be("✗ dotnet build failed (exit 1): 3001 errors, 0 warnings (1 project)");
        result.Length.Should().BeLessThan(1_000);
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

        result.Should().Be("A.cs(1,1) (1,2) CS0001 ×2: msg\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
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

        result.Should().Be("A.cs (2 errors)\n  (1,1) CS0001: msg\n  (1,1) CS0002: msg\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_ErrorWithTwoWarnings_VerdictLineUsesPluralWarnings()
    {
        // Exact assertion pins the plural "s" suffix on the warning count in the *verdict line*
        // (a different ternary from the "N warnings not shown" line below it).
        const string input = """
                             /path/A.cs(1,1): error CS0001: err [P.csproj]
                             /path/B.cs(2,1): warning CS0002: w1 [P.csproj]
                             /path/C.cs(3,1): warning CS0003: w2 [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be("A.cs(1,1) CS0001: err\n2 warnings not shown (dtk log)\n\u2717 dotnet build failed (exit 1): 1 error, 2 warnings (1 project)\n");
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
        // so a plain status line cannot distinguish "matched as noise" from "matched nothing".
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
        result.Should().NotBe("✓ dotnet build\n");
        result.Should().EndWith("✓ dotnet build: 0 errors, 1 warning\n");
    }

    [Fact]
    public void Apply_CodelessExecWarning_IsCountedAndShown()
    {
        // MSBuild's Exec task emits warnings with no diagnostic code. The code was mandatory in
        // SimpleDiagnosticPattern, so these vanished from both the listing and the count: a real
        // build reporting "2 Warning(s)" was summarised by dtk as "1 warning".
        const string input = "EXEC : warning : could not lock config file .git/config: File exists [/p/a.csproj]";

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 0);

        result.Should().Contain("1 warning");
        result.Should().Contain("could not lock config file .git/config: File exists");
    }

    [Fact]
    public void Apply_CodelessErrorAlongsideCodedError_CountsBoth()
    {
        // The dangerous shape: the codeless error is dropped while the coded one still renders, so
        // the verdict line states a confident "1 error" and the dropped failure never reaches the reader.
        const string input = """
                             EXEC : error : the command exited with code 255 [/p/a.csproj]
                             /p/X.cs(1,1): error CS0029: bad [/p/a.csproj]
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().Contain("2 errors");
        result.Should().Contain("the command exited with code 255");
    }

    [Fact]
    public void Apply_DiagnosticRepeatedPerTargetFramework_ReportsMsbuildTotalAndShownCount()
    {
        // A multi-targeted project reports each diagnostic once per TFM and MSBuild counts them all.
        // Collapsing them keeps the output small, but the verdict line must not silently claim there was
        // only one: it reports MSBuild's total and annotates how many distinct entries are shown.
        const string input = """
                             /p/Bad.cs(1,52): error CS0029: Cannot implicitly convert type 'string' to 'int' [/p/m.csproj::TargetFramework=net8.0]
                             /p/Bad.cs(1,52): error CS0029: Cannot implicitly convert type 'string' to 'int' [/p/m.csproj::TargetFramework=net10.0]
                                 0 Warning(s)
                                 2 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet build failed (exit 1): 2 errors (1 shown), 0 warnings (1 project)\n");
        result.Should().Contain("Bad.cs(1,52) CS0029: Cannot implicitly convert type 'string' to 'int'\n");
    }

    [Fact]
    public void Apply_SameDiagnosticFromTwoProjects_ReportsBothSeparately()
    {
        // Two distinct projects can report the identical file/line/code/message — a linked
        // GlobalUsings.cs shared via <Compile Include> between projects, for instance. Deduping on
        // file/line/code/message alone (no project) would merge them into a single entry and hide
        // one project's failure entirely.
        const string input = """
                             /p/GlobalUsings.cs(1,1): error CS0246: The type or namespace name 'Foo' could not be found [/p/A.csproj]
                             /p/GlobalUsings.cs(1,1): error CS0246: The type or namespace name 'Foo' could not be found [/p/B.csproj]
                                 0 Warning(s)
                                 2 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet build failed (exit 1): 2 errors, 0 warnings (2 projects)\n");
        result.Should().NotContain("shown");
        result.Should().Contain("GlobalUsings.cs(1,1) CS0246 ×2: ");
    }

    [Fact]
    public void Apply_SameDiagnosticFromOneProjectAcrossTfmsWithOtherProperties_StillCollapses()
    {
        // The "::" suffix can carry more than just TargetFramework (e.g. a Configuration alongside
        // it, or a different property entirely). Only the project path before the first "::" is
        // keyed on, so any such repeat for the same project still collapses to one entry.
        const string input = """
                             /p/Bad.cs(1,52): error CS0029: Cannot implicitly convert type 'string' to 'int' [/p/m.csproj::TargetFramework=net8.0;Configuration=Debug]
                             /p/Bad.cs(1,52): error CS0029: Cannot implicitly convert type 'string' to 'int' [/p/m.csproj::TargetFramework=net10.0;Configuration=Debug]
                                 0 Warning(s)
                                 2 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet build failed (exit 1): 2 errors (1 shown), 0 warnings (1 project)\n");
        result.Should().Contain("Bad.cs(1,52) CS0029: Cannot implicitly convert type 'string' to 'int'\n");
    }

    [Fact]
    public void Apply_MsbuildSummaryAgreesWithParsedCount_OmitsShownAnnotation()
    {
        // The annotation is a discrepancy signal, not decoration: when nothing was collapsed or
        // missed it must stay out of the verdict line.
        const string input = """
                             /p/A.cs(1,1): error CS0001: msg [/p/a.csproj]
                                 0 Warning(s)
                                 1 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
        result.Should().NotContain("shown");
    }

    [Fact]
    public void Apply_MsbuildSummaryCountOverflowsInt_IsIgnoredAndStillNotADiagnostic()
    {
        // A tally too large for an int cannot be trusted as a total; the parsed count stands, and the
        // line is still consumed as the tally rather than read as a diagnostic or noise.
        const string input = """
                             /p/A.cs(1,1): error CS0001: msg [/p/a.csproj]
                                 0 Warning(s)
                                 99999999999 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet build failed (exit 1): 1 error, 0 warnings (1 project)\n");
        result.Should().NotContain("99999999999");
    }

    [Fact]
    public void Apply_SucceededButSummaryReportsUnparsedWarnings_FallsBackToRawOutput()
    {
        // Nothing parsed while MSBuild declares warnings means the parser missed them (localised
        // SDK, unfamiliar shape). Returning blank hands over to the raw-tail fallback rather than
        // printing a clean tick over output we failed to read.
        const string input = """
                             Somme avertissement non analysable
                                 2 Warning(s)
                                 0 Error(s)
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_DiagnosticsDifferingOnlyBeyondTheCap_AreTwoDiagnosticsGroupedOnOneLine()
    {
        // The dedupe key is the message as MSBuild printed it, so these two survive as the two
        // diagnostics they are; they render alike, so they are grouped, and the reader sees "×2" rather
        // than one error that quietly stands for two. Nothing was collapsed, so no "(N shown)".
        var shared = new string('X', 400);
        var input = $"""
                     /path/A.cs(1,1): error CS0001: {shared}AAA [P.csproj]
                     /path/A.cs(1,1): error CS0001: {shared}BBB [P.csproj]
                         0 Warning(s)
                         2 Error(s)
                     """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            $"A.cs(1,1) CS0001 ×2: {shared}...\n\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_AdvisoriesDifferingOnlyByUrl_AreTwoWarningsGroupedOnOneLine()
    {
        // Two advisories against one package differ only by their link. Once the links are stripped they read
        // alike, but they are two vulnerabilities: grouped with a count, not dropped as a repeat.
        const string input = """
                             /path/A.cs(1,1): warning NU1903: Package 'Foo' 1.0.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-1111 [P.csproj]
                             /path/A.cs(1,1): warning NU1903: Package 'Foo' 1.0.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-2222 [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().Be(
            "A.cs(1,1) NU1903 ×2: Package 'Foo' 1.0.0 has a known high severity vulnerability\n"
            + "\u2713 dotnet build: 0 errors, 2 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_SameAdvisoryRepeatedPerTargetFramework_StillCollapsesToOne()
    {
        // Identical as MSBuild printed them, link and all: the same diagnostic once per framework.
        const string input = """
                             /p/A.cs(1,1): warning NU1903: Package 'Foo' 1.0.0 has a vulnerability, https://github.com/advisories/GHSA-1111 [/p/P.csproj::TargetFramework=net8.0]
                             /p/A.cs(1,1): warning NU1903: Package 'Foo' 1.0.0 has a vulnerability, https://github.com/advisories/GHSA-1111 [/p/P.csproj::TargetFramework=net10.0]
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 0);

        result.Should().StartWith("A.cs(1,1) NU1903: Package 'Foo' 1.0.0 has a vulnerability\n");
        result.Should().NotContain("×");
    }

    [Fact]
    public Task Apply_LongMessagesFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_long_messages.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_LongMessagesFixture_PrintsTheRepeatedMessageOnceWithItsFullText()
    {
        // Three calls pass the same wrong argument: one message, three positions. The compiler's text is
        // 190+ characters long, and the part after "to" names the type that was expected, so cutting it at
        // 120 characters hid the half of the error that says what to change.
        var result = _sut.Apply(LoadFixture("dotnet_build_long_messages.txt"), exitCode: 1);

        result.Should().Contain(
            "  (27,15) (28,15) (29,15) CS1503 ×3: Argument 1: cannot convert from "
            + "'Dictionary<string, List<KeyValuePair<string, IReadOnlyList<long>>>>' to "
            + "'Dictionary<string, List<KeyValuePair<string, IReadOnlyList<int>>>>'\n");
        result.Split("CS1503").Length.Should().Be(2, "the message is printed once");
    }

    [Fact]
    public void Apply_LongMessagesFixture_KeepsBothOverloadsOfAnAmbiguousCall()
    {
        var result = _sut.Apply(LoadFixture("dotnet_build_long_messages.txt"), exitCode: 1);

        result.Should().Contain(
            "CS0121: The call is ambiguous between the following methods or properties: "
            + "'LongMessages.Program.Handle(IEnumerable<string>, ICollection<string>)' and "
            + "'LongMessages.Program.Handle(ICollection<string>, IEnumerable<string>)'\n");
    }

    [Fact]
    public void Apply_LongMessagesFixture_PrintsNoUrl()
    {
        var fixture = LoadFixture("dotnet_build_long_messages.txt");

        _sut.Apply(fixture, exitCode: 1).Should().NotContain("http://").And.NotContain("https://");

        // The warnings are left out of a failed build's report, so read them alone as a passing build's.
        var warningLines = fixture.Split('\n').Where(line => line.Contains(": warning ", StringComparison.Ordinal));
        var result = _sut.Apply(string.Join('\n', warningLines), exitCode: 0);

        result.Should().NotContain("http://").And.NotContain("https://");
        result.Should().Be(
            "Program.cs (2 warnings)\n"
            + "  (31,28) SYSLIB0014: 'WebClient.WebClient()' is obsolete: 'WebRequest, HttpWebRequest, "
            + "ServicePoint, and WebClient are obsolete. Use HttpClient instead.'\n"
            + "  (33,23) SYSLIB0014: 'WebRequest.Create(string)' is obsolete: 'WebRequest, HttpWebRequest, "
            + "ServicePoint, and WebClient are obsolete. Use HttpClient instead.'\n"
            + "\u2713 dotnet build: 0 errors, 2 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_RepeatedMessageInOneFileAmongOthers_GroupsItUnderTheFileHeading()
    {
        // The heading counts the diagnostics, not the lines: five errors, three lines.
        const string input = """
                             /path/A.cs(3,1): error CS0103: The name 'x' does not exist [P.csproj]
                             /path/A.cs(7,1): error CS0103: The name 'x' does not exist [P.csproj]
                             /path/A.cs(9,1): error CS0103: The name 'x' does not exist [P.csproj]
                             /path/A.cs(5,1): error CS0103: The name 'y' does not exist [P.csproj]
                             /path/A.cs(6,1): error CS1002: ; expected [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "A.cs (5 errors)\n"
            + "  (3,1) (7,1) (9,1) CS0103 ×3: The name 'x' does not exist\n"
            + "  (5,1) CS0103: The name 'y' does not exist\n"
            + "  (6,1) CS1002: ; expected\n"
            + "\u2717 dotnet build failed (exit 1): 5 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_FileWhoseDiagnosticsAreOneGroup_PrintsOneLineWithNoHeading()
    {
        const string errors = """
                              /path/A.cs(3,7): error CS0219: unused [P.csproj]
                              /path/A.cs(9,2): error CS0219: unused [P.csproj]
                              /path/A.cs(12,4): error CS0219: unused [P.csproj]
                              """;
        const string warnings = """
                                /path/A.cs(3,7): warning CS0219: unused [P.csproj]
                                /path/A.cs(9,2): warning CS0219: unused [P.csproj]
                                """;

        var errorBody = new DotnetBuildFilter("/path").Apply(errors, exitCode: 1);
        var warningBody = new DotnetBuildFilter("/path").Apply(warnings, exitCode: 0);

        errorBody.Should().Be(
            "A.cs(3,7) (9,2) (12,4) CS0219 ×3: unused\n"
            + "\u2717 dotnet build failed (exit 1): 3 errors, 0 warnings (1 project)\n");
        warningBody.Should().Be(
            "A.cs(3,7) (9,2) CS0219 ×2: unused\n\u2713 dotnet build: 0 errors, 2 warnings (1 project)\n");
    }

    [Fact]
    public void Apply_FileWithOneDiagnostic_PrintsOneLineWithNoHeading()
    {
        // Errors and warnings alike, and a file beside other files that do keep their headings.
        const string input = """
                             /path/A.cs(3,7): error CS0001: first [P.csproj]
                             /path/B.cs(1,1): error CS0002: second [P.csproj]
                             /path/B.cs(2,1): error CS0003: third [P.csproj]
                             /path/C.cs(4,4): warning CS0168: unused [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().Be(
            "B.cs (2 errors)\n  (1,1) CS0002: second\n  (2,1) CS0003: third\n"
            + "A.cs(3,7) CS0001: first\n"
            + "1 warning not shown (dtk log)\n"
            + "\u2717 dotnet build failed (exit 1): 3 errors, 1 warning (1 project)\n");
    }

    [Fact]
    public void Apply_DiagnosticsWithNoFile_KeepTheNoFileHeadingWhenThereAreSeveralLines()
    {
        const string input = """
                             MSBUILD : error MSB1001: First.
                             MSBUILD : error MSB1002: Second.
                             """;

        var result = new DotnetBuildFilter().Apply(input, exitCode: 1);

        result.Should().StartWith("(no file) (2 errors)\n  MSB1001: First.\n  MSB1002: Second.\n");
    }

    [Fact]
    public void Apply_SingleCodelessDiagnostic_PrintsItsMessageAfterTheLocation()
    {
        const string input = "/path/A.cs(1,2): error : could not copy [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,2) could not copy\n");
    }

    [Fact]
    public void Apply_RepeatedCodelessMessage_CountsItWhereTheCodeWouldBe()
    {
        const string input = """
                             /path/A.cs(1,2): error : could not copy [P.csproj]
                             /path/A.cs(5,6): error : could not copy [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,2) (5,6) ×2: could not copy\n");
    }

    [Fact]
    public void Apply_SameMessageUnderDifferentCodes_IsNotGrouped()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/A.cs(2,1): error CS0002: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs (2 errors)\n  (1,1) CS0001: msg\n  (2,1) CS0002: msg\n");
    }

    [Fact]
    public void Apply_SameCodeWithDifferentMessages_IsNotGrouped()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0103: The name 'x' does not exist [P.csproj]
                             /path/A.cs(2,1): error CS0103: The name 'y' does not exist [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith(
            "A.cs (2 errors)\n  (1,1) CS0103: The name 'x' does not exist\n  (2,1) CS0103: The name 'y' does not exist\n");
    }

    [Fact]
    public void Apply_SameMessageInTwoFiles_IsGroupedWithinEachFileOnly()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0001: msg [P.csproj]
                             /path/A.cs(2,1): error CS0001: msg [P.csproj]
                             /path/B.cs(3,1): error CS0001: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,1) (2,1) CS0001 ×2: msg\nB.cs(3,1) CS0001: msg\n");
    }

    [Fact]
    public void Apply_SameDiagnosticFromTwoProjects_PrintsItsPositionOnce()
    {
        // Two projects report one linked file's error at one position: two errors, one place to fix.
        const string input = """
                             /p/Shared.cs(1,1): error CS0246: The type 'Foo' could not be found [/p/A.csproj]
                             /p/Shared.cs(1,1): error CS0246: The type 'Foo' could not be found [/p/B.csproj]
                             """;

        var result = new DotnetBuildFilter("/p").Apply(input, exitCode: 1);

        result.Should().Be(
            "Shared.cs(1,1) CS0246 ×2: The type 'Foo' could not be found\n"
            + "\u2717 dotnet build failed (exit 1): 2 errors, 0 warnings (2 projects)\n");
    }

    [Fact]
    public void Apply_GroupedPositions_AreSortedByLineThenColumnNumerically()
    {
        // Analyzers report out of order, and 23 sorts before 9 as text. The positions read the same whatever
        // order the build printed them in.
        const string input = """
                             /path/A.cs(23,9): error CS0001: msg [P.csproj]
                             /path/A.cs(9,9): error CS0001: msg [P.csproj]
                             /path/A.cs(10,12): error CS0001: msg [P.csproj]
                             /path/A.cs(10,3): error CS0001: msg [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(9,9) (10,3) (10,12) (23,9) CS0001 ×4: msg\n");
    }

    [Fact]
    public void Apply_GroupedLines_KeepTheOrderTheyFirstAppeared()
    {
        const string input = """
                             /path/A.cs(1,1): error CS0002: beta [P.csproj]
                             /path/A.cs(2,1): error CS0001: alpha [P.csproj]
                             /path/A.cs(3,1): error CS0002: beta [P.csproj]
                             """;

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs (3 errors)\n  (1,1) (3,1) CS0002 ×2: beta\n  (2,1) CS0001: alpha\n");
    }

    [Fact]
    public void Apply_ErrorMessage_IsCutAtFourHundredCharacters()
    {
        var longest = new string('E', 400);
        var input = $"/path/A.cs(1,1): error CS0001: {longest}tail [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith($"A.cs(1,1) CS0001: {longest}...\n");
    }

    [Fact]
    public void Apply_ErrorMessageOfFourHundredCharacters_IsKeptWhole()
    {
        var message = new string('E', 400);
        var input = $"/path/A.cs(1,1): error CS0001: {message} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith($"A.cs(1,1) CS0001: {message}\n");
    }

    [Fact]
    public void Apply_WarningMessage_IsCutAtOneHundredSixtyCharacters()
    {
        var kept = new string('W', 160);
        var input = $"/path/A.cs(1,1): warning CS0168: {kept}tail [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().StartWith($"A.cs(1,1) CS0168: {kept}...\n");
    }

    [Fact]
    public void Apply_UrlsAreStrippedBeforeTheCap_SoTheyCostNoneOfIt()
    {
        // 150 characters of text and a 120-character link: 270 as the compiler wrote it, 150 once stripped.
        var text = new string('W', 150);
        var url = $"https://example.com/{new string('p', 100)}";
        var input = $"/path/A.cs(1,1): warning CS0168: {text} {url} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().StartWith($"A.cs(1,1) CS0168: {text}\n");
    }

    [Theory]
    [InlineData("Known high severity vulnerability, https://github.com/advisories/GHSA-xxxx",
        "Known high severity vulnerability")]
    [InlineData("Obsolete. (https://aka.ms/dotnet-warnings/SYSLIB0014)", "Obsolete.")]
    [InlineData("Obsolete. [https://aka.ms/dotnet-warnings/SYSLIB0014]", "Obsolete.")]
    [InlineData("Out of support. Refer to http://aka.ms/support for details.", "Out of support.")]
    [InlineData("Out of support (see https://aka.ms/support) soon.", "Out of support soon.")]
    [InlineData("Not to be used. See https://aka.ms/binaryformatter for more information.", "Not to be used.")]
    [InlineData("Old API.  see   https://aka.ms/x   for   more   information.", "Old API.")]
    [InlineData("Out of support. Please refer to https://aka.ms/dotnet-core-support for more information about the support policy. Upgrade.",
        "Out of support. Upgrade.")]
    [InlineData("Unsupported, see: https://aka.ms/x", "Unsupported")]
    [InlineData("Unsupported (Visit https://aka.ms/x)", "Unsupported")]
    [InlineData("Impossible to foresee https://aka.ms/x", "Impossible to foresee")]
    [InlineData("Two links https://a.example/x and HTTPS://b.example/y end.", "Two links and end.")]
    [InlineData("Keep (this) and [that].", "Keep (this) and [that].")]
    public void Apply_UrlInMessage_IsRemovedWithTheBracketsItEmptied(string message, string expected)
    {
        var input = $"/path/A.cs(1,1): warning CS0168: {message} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().StartWith($"A.cs(1,1) CS0168: {expected}\n");
    }

    [Fact]
    public void Apply_UrlInASimpleDiagnostic_IsRemoved()
    {
        const string input = "MSBUILD : warning NETSDK1138: Out of support. Please refer to https://aka.ms/dotnet-core-support.";

        var result = new DotnetBuildFilter().Apply(input, exitCode: 0);

        result.Should().StartWith("(no file) NETSDK1138: Out of support.\n");
    }

    [Theory]
    [InlineData("'System.Collections.Generic.IEnumerable<int>'", "'IEnumerable<int>'")]
    [InlineData("'Microsoft.Extensions.Logging.ILogger'", "'ILogger'")]
    [InlineData("'MyApp.Models.Order'", "'MyApp.Models.Order'")]
    [InlineData("'System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>'",
        "'Dictionary<string, List<int>>'")]
    [InlineData("'System.Collections.Generic.List<int>.Enumerator'", "'List<int>.Enumerator'")]
    [InlineData("'System.Int32[]'", "'Int32[]'")]
    [InlineData("'MyApp.System.Thing'", "'MyApp.System.Thing'")]
    [InlineData("'Holder.Microsoft.Thing'", "'Holder.Microsoft.Thing'")]
    [InlineData("'SystemClock.Now'", "'SystemClock.Now'")]
    public void Apply_QuotedFrameworkNamespace_IsDropped(string span, string expected)
    {
        var input = $"/path/A.cs(1,1): error CS1503: Cannot convert {span} to 'string' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith($"A.cs(1,1) CS1503: Cannot convert {expected} to 'string'\n");
    }

    [Fact]
    public void Apply_FrameworkNamespaceOutsideQuotes_IsKept()
    {
        const string input = "/path/A.cs(1,1): error CS0001: Use System.Collections.Generic.List<int> or 'string' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,1) CS0001: Use System.Collections.Generic.List<int> or 'string'\n");
    }

    [Fact]
    public void Apply_NamespaceNamedInAQuote_IsKept()
    {
        // 'System.Collections' here is a namespace, not a type in one: dropping "System." would name
        // another namespace.
        const string input =
            "/path/A.cs(1,1): error CS0234: The type or namespace name 'Foo' does not exist in the namespace 'System.Collections' (are you missing an assembly reference?) [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith(
            "A.cs(1,1) CS0234: The type or namespace name 'Foo' does not exist in the namespace 'System.Collections' (are you missing an assembly reference?)\n");
    }

    [Fact]
    public void Apply_FrameworkMemberInASignature_KeepsItsQualifiedNameAndShortensItsParameters()
    {
        // The head of 'System.Linq.Enumerable.Select<…>(…)' names a member: which of its dots end the namespace
        // is not for a text rule to say, so the head stays whole while the parameter types, which are types, shorten.
        const string input =
            "/path/A.cs(1,1): error CS0121: The call is ambiguous between 'System.Linq.Enumerable.Select<int, int>(System.Collections.Generic.IEnumerable<int>, System.Func<int, int>)' and 'MyApp.Q.Select<int, int>(System.Collections.Generic.IEnumerable<int>, System.Func<int, int>)' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith(
            "A.cs(1,1) CS0121: The call is ambiguous between 'System.Linq.Enumerable.Select<int, int>(IEnumerable<int>, Func<int, int>)' and 'MyApp.Q.Select<int, int>(IEnumerable<int>, Func<int, int>)'\n");
    }

    [Fact]
    public void Apply_GenericTypesMemberSignature_IsNotCutInTheMiddleOfItsQualifiedName()
    {
        // Keeping the last two segments of the head gave 'Generic.List<int>.List(int)'.
        const string input =
            "/path/A.cs(1,1): error CS0121: The call is ambiguous between 'System.Collections.Generic.List<int>.List(int)' and 'System.Collections.Generic.List<int>.List(System.Collections.Generic.IEnumerable<int>)' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith(
            "A.cs(1,1) CS0121: The call is ambiguous between 'System.Collections.Generic.List<int>.List(int)' and 'System.Collections.Generic.List<int>.List(IEnumerable<int>)'\n");
    }

    [Fact]
    public void Apply_TwoDistinctTypesThatShortenAlike_KeepTheWholeMessageUnshortened()
    {
        // CS0104: shortening would say "between 'Timer' and 'Timer'", and make the three names one.
        const string message =
            "'Timer' is an ambiguous reference between 'System.Threading.Timer' and 'System.Timers.Timer'";
        var input = $"/path/A.cs(1,1): error CS0104: {message} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith($"A.cs(1,1) CS0104: {message}\n");
    }

    [Fact]
    public void Apply_RepeatedTypeInOneMessage_StillShortens()
    {
        // Two spans that read alike because they ARE alike do not make a collision.
        const string input =
            "/path/A.cs(1,1): error CS1503: Cannot convert 'System.Collections.Generic.IEnumerable<int>' to 'System.Collections.Generic.IEnumerable<int>' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,1) CS1503: Cannot convert 'IEnumerable<int>' to 'IEnumerable<int>'\n");
    }

    [Fact]
    public void Apply_DistinctTypesThatStayDistinct_AreShortenedInTheSameMessage()
    {
        const string input =
            "/path/A.cs(1,1): error CS1503: Cannot convert 'System.Collections.Generic.List<int>' to 'System.Collections.Generic.IEnumerable<string>' [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith("A.cs(1,1) CS1503: Cannot convert 'List<int>' to 'IEnumerable<string>'\n");
    }

    [Theory]
    [InlineData("The type 'System.Object' is defined in an assembly that is not referenced. You must add a reference to assembly 'System.Runtime, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.",
        "The type 'Object' is defined in an assembly that is not referenced. You must add a reference to assembly 'System.Runtime, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.")]
    [InlineData("Assembly 'App, Version=1.0.0.0' uses 'System.Runtime, Version=4.2.2.0, Culture=neutral' which has a higher version than referenced assembly 'System.Runtime, Version=4.0.0.0'",
        "Assembly 'App, Version=1.0.0.0' uses 'System.Runtime, Version=4.2.2.0, Culture=neutral' which has a higher version than referenced assembly 'System.Runtime, Version=4.0.0.0'")]
    [InlineData("The type 'Foo' exists in both 'System.Runtime, Version=4.2.2.0' and 'Microsoft.Foo, Version=1.0.0.0'",
        "The type 'Foo' exists in both 'System.Runtime, Version=4.2.2.0' and 'Microsoft.Foo, Version=1.0.0.0'")]
    [InlineData("Package 'System.Text.Json' 8.0.0 has a known high severity vulnerability",
        "Package 'System.Text.Json' 8.0.0 has a known high severity vulnerability")]
    [InlineData("Package 'Microsoft.Windows.Compatibility 8.0.0' was restored using '.NETFramework,Version=v4.6.1' instead of the project target framework 'net8.0'.",
        "Package 'Microsoft.Windows.Compatibility 8.0.0' was restored using '.NETFramework,Version=v4.6.1' instead of the project target framework 'net8.0'.")]
    [InlineData("Unable to find package 'System.Text.Json' with version (>= 9.0.0)",
        "Unable to find package 'System.Text.Json' with version (>= 9.0.0)")]
    [InlineData("A reference to assembly 'System.Runtime' is missing",
        "A reference to assembly 'System.Runtime' is missing")]
    [InlineData("Metadata file '/repo/lib/Microsoft.Foo.dll' could not be found",
        "Metadata file '/repo/lib/Microsoft.Foo.dll' could not be found")]
    [InlineData("Metadata file 'C:\\libs\\Microsoft.Foo.dll' could not be found",
        "Metadata file 'C:\\libs\\Microsoft.Foo.dll' could not be found")]
    [InlineData("Metadata file 'Microsoft.Foo.dll' could not be found",
        "Metadata file 'Microsoft.Foo.dll' could not be found")]
    [InlineData("Cannot start 'System.Tools.Runner.exe'", "Cannot start 'System.Tools.Runner.exe'")]
    public void Apply_QuotedNameThatIsNotATypeExpression_IsKept(string message, string expected)
    {
        // Real compiler and NuGet messages quote assemblies, packages, files and paths beside the types, and a
        // dotted System./Microsoft. name there is not a type: 'System.Text.Json' read 'Json', 'Microsoft.Foo.dll' read 'dll'.
        var input = $"/path/A.cs(1,1): error CS0012: {message} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 1);

        result.Should().StartWith($"A.cs(1,1) CS0012: {expected}\n");
    }

    [Fact]
    public void Apply_ShortenedMessages_AreWhatTheCapMeasures()
    {
        // Eight spans of 45 characters are 367 as the compiler wrote them and 151 once shortened, which fits.
        const string span = "'System.Collections.Generic.IEnumerable<int>'";
        var message = string.Join(" ", Enumerable.Repeat(span, 8));
        var input = $"/path/A.cs(1,1): warning CS0168: {message} [P.csproj]";

        var result = new DotnetBuildFilter("/path").Apply(input, exitCode: 0);

        result.Should().StartWith($"A.cs(1,1) CS0168: {string.Join(" ", Enumerable.Repeat("'IEnumerable<int>'", 8))}\n");
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

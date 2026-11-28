using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class MsBuildDiagnosticReportTests
{
    private const string Root = "/test/project/root";

    [Fact]
    public void Totals_ErrorsFixture_ReadMsBuildsTally()
    {
        var report = Parse("dotnet_build_errors.txt");

        report.ErrorTotal.Should().Be(3);
        report.WarningTotal.Should().Be(1);
    }

    [Fact]
    public void Totals_WarningsFixture_ReadMsBuildsTally()
    {
        var report = Parse("dotnet_build_warnings.txt");

        report.ErrorTotal.Should().Be(0);
        report.WarningTotal.Should().Be(3);
    }

    [Fact]
    public void Totals_DeclaredTallyExceedsParsed_TakeTheLarger()
    {
        var report = MsBuildDiagnosticReport.Parse(
            "/p/A.cs(1,1): error CS0001: boom [/p/A.csproj]\n    5 Error(s)\n    2 Warning(s)\n", Root);

        report.ErrorTotal.Should().Be(5);
        report.WarningTotal.Should().Be(2);
    }

    [Fact]
    public void FailedProjectCount_ErrorsFixture_CountsTheOneProjectThatFailed()
    {
        var report = Parse("dotnet_build_errors.txt");

        // Three errors, all in DotnetTokenKiller.Application.csproj; the warning in Domain.csproj is not a failure.
        report.FailedProjectCount.Should().Be(1);
    }

    [Fact]
    public void FailedProjectCount_WarningsFixture_IsZero()
    {
        Parse("dotnet_build_warnings.txt").FailedProjectCount.Should().Be(0);
    }

    [Fact]
    public void FailedProjectCount_SameProjectPerTargetFramework_CountsOnce()
    {
        const string output = """
                              /p/A.cs(1,1): error CS0001: boom [/p/A.csproj::TargetFramework=net8.0]
                              /p/B.cs(2,2): error CS0002: bang [/p/A.csproj::TargetFramework=net9.0]
                              /p/C.cs(3,3): error CS0003: crash [/p/C.csproj]
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).FailedProjectCount.Should().Be(2);
    }

    [Fact]
    public void FailedProjectCount_ErrorWithoutProjectSuffix_IsZero()
    {
        var report = MsBuildDiagnosticReport.Parse("/p/A.cs(1,1): error CS0001: boom\n", Root);

        report.FailedProjectCount.Should().Be(0);
    }

    [Fact]
    public void FailedProjectCount_ProjectLevelErrorWithSuffix_Counts()
    {
        var report = MsBuildDiagnosticReport.Parse(
            "CSC : error CS8785: generator failed [/p/Gen.csproj::TargetFramework=net8.0]\n", Root);

        report.FailedProjectCount.Should().Be(1);
    }

    [Fact]
    public void FailedProjectCount_WindowsPathSuffix_UsesTheFileName()
    {
        var report = MsBuildDiagnosticReport.Parse(
            "C:\\p\\A.cs(1,1): error CS0001: boom [C:\\p\\src\\A.csproj]\n", "C:\\p");

        report.FailedProjectCount.Should().Be(1);
    }

    [Theory]
    [InlineData("/r/Sol.sln")]
    [InlineData("/r/Sol.slnx")]
    [InlineData("/r/Sol.slnf")]
    [InlineData("C:\\r\\Sol.slnx")]
    [InlineData("/r/notes.txt")]
    [InlineData("net8.0")]
    public void FailedProjectCount_SuffixThatIsNotAProjectFile_IsZero(string suffix)
    {
        // A solution-level restore error (NU1101 in a solution build) is suffixed with the solution, which
        // is no project: counting "Sol" would put a phantom project in the status line.
        var report = MsBuildDiagnosticReport.Parse(
            $"/r/A/A.csproj : error NU1101: Unable to find package Foo. [{suffix}]\n", "/r");

        report.FailedProjectCount.Should().Be(0);
    }

    [Theory]
    [InlineData("/r/A.csproj")]
    [InlineData("/r/A.fsproj")]
    [InlineData("/r/A.vbproj")]
    [InlineData("/r/A.proj")]
    [InlineData("/r/A.CSPROJ")]
    [InlineData("/r/A.csproj::TargetFramework=net8.0")]
    public void FailedProjectCount_ProjectFileSuffix_Counts(string suffix)
    {
        var report = MsBuildDiagnosticReport.Parse($"/r/X.cs(1,1): error CS0001: boom [{suffix}]\n", "/r");

        report.FailedProjectCount.Should().Be(1);
    }

    [Fact]
    public void FailedProjectCount_SolutionAndProjectSuffixes_CountsOnlyTheProject()
    {
        const string output = """
                              /r/A/A.csproj : error NU1101: Unable to find package Foo. [/r/Sol.slnx]
                              /r/B/X.cs(1,1): error CS0001: boom [/r/B/B.csproj]
                              """;

        MsBuildDiagnosticReport.Parse(output, "/r").FailedProjectCount.Should().Be(1);
    }

    [Fact]
    public void WarningCodes_WarningsFixture_MostFrequentCodeFirst()
    {
        var report = Parse("dotnet_build_warnings.txt");

        report.WarningCodes.Should().Equal(("CS0219", 2), ("CS0169", 1));
    }

    [Fact]
    public void WarningCodes_EqualCounts_OrderedByCodeOrdinal()
    {
        const string output = """
                              /p/A.cs(1,1): warning CS0219: a [/p/A.csproj]
                              /p/A.cs(2,1): warning CS0169: b [/p/A.csproj]
                              /p/A.cs(3,1): warning CS8602: c [/p/A.csproj]
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).WarningCodes
            .Should().Equal(("CS0169", 1), ("CS0219", 1), ("CS8602", 1));
    }

    [Fact]
    public void WarningCodes_CodelessWarning_IsLabelledLikeTheRenderedReport()
    {
        var report = MsBuildDiagnosticReport.Parse("EXEC : warning : could not lock config file\n", Root);

        report.WarningCodes.Should().Equal(("(no code)", 1));
    }

    [Fact]
    public void WarningCodes_ErrorsFixture_CountsWarningsNotErrors()
    {
        Parse("dotnet_build_errors.txt").WarningCodes.Should().Equal(("CS0169", 1));
    }

    [Fact]
    public void WarningCodes_NoWarnings_IsEmpty()
    {
        Parse("dotnet_build_success.txt").WarningCodes.Should().BeEmpty();
    }

    [Fact]
    public void RenderDiagnostics_ErrorsFixture_IsTheBodyWithoutStatusLineOrSeparator()
    {
        var body = Parse("dotnet_build_errors.txt").RenderDiagnostics();

        body.Should().Be(
            "src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs (2 errors)\n"
            + "  (10,5) CS1002: ; expected\n"
            + "  (12,18) CS0103: The name 'undeclared' does not exist in the current context\n"
            + "src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs(8,10) CS1002: ; expected\n"
            + "1 warning not shown (dtk log)\n");
        body.Should().NotContain("dotnet build:").And.NotContain("---");
    }

    [Fact]
    public void RenderDiagnostics_WarningsFixture_ListsWarningsWhenThereAreNoErrors()
    {
        var body = Parse("dotnet_build_warnings.txt").RenderDiagnostics();

        body.Should().StartWith("src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs (2 warnings)\n")
            .And.Contain("src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs(5,10) CS0169: The field '_unused' is never used\n");
        body.Should().NotContain("dotnet build:").And.NotContain("---");
        body.Should().NotContain("\r");
        body.Should().EndWith("\n");
    }

    [Fact]
    public void RenderDiagnostics_CleanBuild_IsEmpty()
    {
        Parse("dotnet_build_success.txt").RenderDiagnostics().Should().BeEmpty();
    }

    [Fact]
    public void RenderDiagnostics_NothingParsed_IsEmpty()
    {
        MsBuildDiagnosticReport.Parse("garbled output\n", Root).RenderDiagnostics().Should().BeEmpty();
    }

    // ── Warnings and the files the work in progress changed ──────────────────

    private const string FourWarnings = """
                                           /test/project/root/src/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/src/App.csproj]
                                           /test/project/root/src/A.cs(9,5): warning CA1822: Member 'M' does not access instance data [/test/project/root/src/App.csproj]
                                           /test/project/root/src/B.cs(2,1): warning CA1822: Member 'N' does not access instance data [/test/project/root/src/App.csproj]
                                           /test/project/root/tests/C.cs(3,1): warning CS0219: The variable 'x' is assigned but its value is never used [/test/project/root/tests/T.csproj]
                                             App -> /test/project/root/src/bin/App.dll
                                             T -> /test/project/root/tests/bin/T.dll
                                               4 Warning(s)
                                               0 Error(s)
                                           """;

    [Fact]
    public void Render_WithChanges_ListsTheWarningsOfChangedFilesAndCountsTheRest()
    {
        var changes = Changes("src/A.cs");

        MsBuildDiagnosticReport.Parse(FourWarnings, Root).Render("dotnet build", 0, [], changes).Should().Be(
            """
            src/A.cs (2 warnings)
              (1,1) CS0168: The variable 'e' is declared but never used
              (9,5) CA1822: Member 'M' does not access instance data
            2 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): CA1822 (1x), CS0219 (1x)
            ✓ dotnet build: 0 errors, 4 warnings, 2 in changed files (2 projects)

            """);
    }

    [Fact]
    public void Render_WithChangesThatRaisedNoWarning_SaysSo()
    {
        MsBuildDiagnosticReport.Parse(FourWarnings, Root).Render("dotnet build", 0, [], Changes("README.md"))
            .Should().Be(
                """
                4 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): CA1822 (2x), CS0168 (1x), CS0219 (1x)
                ✓ dotnet build: 0 errors, 4 warnings, none in changed files (2 projects)

                """);
    }

    [Fact]
    public void Render_WithEveryWarningInChangedFiles_ListsThemAll()
    {
        var changes = Changes("src/A.cs", "src/B.cs", "tests/C.cs");

        var result = MsBuildDiagnosticReport.Parse(FourWarnings, Root).Render("dotnet build", 0, [], changes);

        result.Should().NotContain("not shown");
        result.Should().Contain("tests/C.cs(3,1) CS0219");
        result.Should().EndWith("✓ dotnet build: 0 errors, 4 warnings, 4 in changed files (2 projects)\n");
    }

    [Fact]
    public void Render_WithChanges_NamesTheLogOfItsOwnVerb()
    {
        MsBuildDiagnosticReport.Parse(FourWarnings, Root).Render("dotnet publish", 0, [], Changes())
            .Should().StartWith("4 warnings in unchanged files not shown (dtk log publish --full | dtk pipe publish): ");
    }

    [Fact]
    public void Render_WithChanges_StillListsEveryError()
    {
        const string output = """
                              /test/project/root/src/B.cs(2,1): error CS0103: The name 'y' does not exist in the current context [/test/project/root/src/App.csproj]
                              /test/project/root/src/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/src/App.csproj]
                                  1 Warning(s)
                                  1 Error(s)
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 1, [], Changes("src/A.cs")).Should().Be(
            """
            src/B.cs(2,1) CS0103: The name 'y' does not exist in the current context
            1 warning not shown (dtk log)
            ✗ dotnet build failed (exit 1): 1 error, 1 warning (1 project)

            """);
    }

    [Fact]
    public void Render_WithoutChanges_ListsEveryWarning()
    {
        MsBuildDiagnosticReport.Parse(FourWarnings, Root).Render("dotnet build", 0, [])
            .Should().Contain("tests/C.cs(3,1) CS0219").And.EndWith("✓ dotnet build: 0 errors, 4 warnings (2 projects)\n");
    }

    [Fact]
    public void RenderWarningsIn_ListsOnlyTheChangedFilesWarnings()
    {
        var report = MsBuildDiagnosticReport.Parse(FourWarnings, Root);

        report.RenderWarningsIn(Changes("src/B.cs", "tests/C.cs")).Should().Be(
            "src/B.cs(2,1) CA1822: Member 'N' does not access instance data\n"
            + "tests/C.cs(3,1) CS0219: The variable 'x' is assigned but its value is never used\n");
        report.WarningCountIn(Changes("src/B.cs", "tests/C.cs")).Should().Be(2);
        report.WarningCountIn(Changes()).Should().Be(0);
    }

    [Fact]
    public void Render_WarningRepeatedPerTargetFrameworkAndInTheSummary_IsCountedAsDistinct()
    {
        // MSBuild counts a multi-targeted project's warning once per target framework, and its console logger
        // repeats each one under "Build succeeded."; every one it counted is listed, just once.
        const string output = """
                              /test/project/root/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/App.csproj::TargetFramework=net8.0]
                              /test/project/root/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/App.csproj::TargetFramework=net10.0]
                              Build succeeded.
                              /test/project/root/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/App.csproj::TargetFramework=net8.0]
                              /test/project/root/A.cs(1,1): warning CS0168: The variable 'e' is declared but never used [/test/project/root/App.csproj::TargetFramework=net10.0]
                                  2 Warning(s)
                                  0 Error(s)
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 0, [])
            .Should().EndWith("✓ dotnet build: 0 errors, 2 warnings (1 distinct) (1 project)\n");
    }

    [Fact]
    public void Render_SummaryEchoesBesideAnUnparsedError_StillSaysShown()
    {
        // The console logger prints every diagnostic again under "Build FAILED."; those echoes repeat a line already
        // read, word for word, and stand for nothing MSBuild counted twice. The third error matched no pattern, so
        // the list is short of one, and the count must not read as complete.
        const string output = """
                              /test/project/root/A.cs(1,1): error CS0103: The name 'a' does not exist in the current context [/test/project/root/App.csproj]
                              /test/project/root/B.cs(2,1): error CS0103: The name 'b' does not exist in the current context [/test/project/root/App.csproj]
                              EXEC : the tool failed in a way no pattern reads [/test/project/root/App.csproj]
                              Build FAILED.
                                /test/project/root/A.cs(1,1): error CS0103: The name 'a' does not exist in the current context [/test/project/root/App.csproj]
                                /test/project/root/B.cs(2,1): error CS0103: The name 'b' does not exist in the current context [/test/project/root/App.csproj]
                                  0 Warning(s)
                                  3 Error(s)
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 1, [])
            .Should().EndWith("✗ dotnet build failed (exit 1): 3 errors (2 shown), 0 warnings (1 project)\n");
    }

    // ── Diagnostics without a position ────────────────────────────────────────

    [Fact]
    public void RenderDiagnostics_ProjectLevelWarning_IsFiledUnderItsProjectFile()
    {
        // The real `dotnet build` of a project referencing a vulnerable package: NuGet names the project file
        // before the level, with no position and no [project] suffix.
        Parse("dotnet_build_project_level_warning.txt").RenderDiagnostics().Should().Be(
            """
            app/App.csproj NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability
            app/A.cs(6,13) CS0219: The variable 'unused' is assigned but its value is never used
            app/B.cs(6,13) CS0219: The variable 'unusedToo' is assigned but its value is never used
            Top codes: CS0219 (2x), NU1903 (1x)

            """);
    }

    [Fact]
    public void Render_ProjectLevelWarningPrintedTwiceBeforeTheSummary_IsCountedAsDistinct()
    {
        // NuGet reports NU1903 at restore and again when the build reads the assets file, word for word, and MSBuild
        // counts both: 4 warnings, of which 3 are distinct. Only the copies under "Build succeeded." are echoes.
        Parse("dotnet_build_project_level_warning.txt").Render("dotnet build", 0, [])
            .Should().EndWith("✓ dotnet build: 0 errors, 4 warnings (3 distinct) (1 project, 0.83s)\n");
    }

    [Fact]
    public void RenderDiagnostics_SameProjectLevelWarningOfTwoProjects_IsListedForEach()
    {
        const string output = """
                              /test/project/root/src/App.csproj : warning NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability
                              /test/project/root/src/Lib.csproj : warning NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability
                                  2 Warning(s)
                                  0 Error(s)
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).RenderDiagnostics().Should().Be(
            """
            src/App.csproj NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability
            src/Lib.csproj NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability

            """);
    }

    [Fact]
    public void RenderDiagnostics_ProjectLevelErrorWithARelativeProjectFile_IsFiledUnderIt()
    {
        const string output = "src/App.csproj : error NU1101: Unable to find package Nope. No packages exist with this id in source(s): nuget.org";

        MsBuildDiagnosticReport.Parse(output, Root).RenderDiagnostics()
            .Should().Be("src/App.csproj NU1101: Unable to find package Nope. No packages exist with this id in source(s): nuget.org\n");
    }

    [Theory]
    [InlineData("MSBUILD : warning MSB4011: Imported twice.")]
    [InlineData("CSC : warning CS8032: An instance of analyzer Foo cannot be created.")]
    [InlineData("EXEC : warning : The tool printed a warning. [/test/project/root/src/App.csproj]")]
    [InlineData(" : warning MSB4011: Imported twice.")]
    public void RenderDiagnostics_ToolLevelWarning_NamesNoFile(string line)
    {
        MsBuildDiagnosticReport.Parse(line, Root).RenderDiagnostics().Should().StartWith("(no file) ");
    }

    [Fact]
    public void Render_WithChanges_HidesTheProjectLevelWarningOfAnUnchangedProjectFile()
    {
        Parse("dotnet_build_project_level_warning.txt").Render("dotnet build", 0, [], Changes("app/A.cs")).Should().Be(
            """
            app/A.cs(6,13) CS0219: The variable 'unused' is assigned but its value is never used
            2 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): CS0219 (1x), NU1903 (1x)
            ✓ dotnet build: 0 errors, 4 warnings (3 distinct), 1 in changed files (1 project, 0.83s)

            """);
    }

    [Fact]
    public void Render_WithChanges_CountsEveryUnchangedWarningThatRendersAlike()
    {
        // Two advisories for one package differ only by their link, which rendering removes: two warnings, not one.
        const string output = """
                              /test/project/root/src/App.csproj : warning NU1903: Package 'X' 1.0.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-aaaa
                              /test/project/root/src/App.csproj : warning NU1903: Package 'X' 1.0.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-bbbb
                                  2 Warning(s)
                                  0 Error(s)
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 0, [], Changes("src/A.cs")).Should()
            .StartWith("2 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): NU1903 (2x)\n");
    }

    [Fact]
    public void Render_WithChanges_ListsAWarningThatNamesNoFile()
    {
        // Nothing says which file a tool-level warning comes from, so nothing says the change did not cause it.
        const string output = FourWarnings + "\nCSC : warning CS8032: An instance of analyzer Foo cannot be created.";

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 0, [], Changes("src/A.cs")).Should().Be(
            """
            src/A.cs (2 warnings)
              (1,1) CS0168: The variable 'e' is declared but never used
              (9,5) CA1822: Member 'M' does not access instance data
            (no file) CS8032: An instance of analyzer Foo cannot be created.
            2 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): CA1822 (1x), CS0219 (1x)
            ✓ dotnet build: 0 errors, 5 warnings, 2 in changed files (2 projects)

            """);
    }

    [Fact]
    public void RenderWarningsIn_ListsAWarningThatNamesNoFile()
    {
        const string output = FourWarnings + "\nCSC : warning CS8032: An instance of analyzer Foo cannot be created.";
        var report = MsBuildDiagnosticReport.Parse(output, Root);

        report.RenderWarningsIn(Changes()).Should().Be("(no file) CS8032: An instance of analyzer Foo cannot be created.\n");
        report.WarningCountIn(Changes()).Should().Be(0);
    }

    [Fact]
    public void Render_WithChangesAndAWarningNoPatternMatched_SaysHowManyItParsed()
    {
        // None of the warnings is shown, so "(4 shown)" would contradict the line above it; what the count must still
        // say is that two warnings MSBuild counted were never read, and may be in a changed file.
        var output = FourWarnings.Replace("4 Warning(s)", "6 Warning(s)", StringComparison.Ordinal);

        MsBuildDiagnosticReport.Parse(output, Root).Render("dotnet build", 0, [], Changes("README.md"))
            .Should().EndWith("✓ dotnet build: 0 errors, 6 warnings (4 parsed), none in changed files (2 projects)\n");
    }

    [Theory]
    [InlineData("src/App.csproj")]
    [InlineData("src/App.fsproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Packages.props")]
    [InlineData("build/Analyzers.targets")]
    [InlineData("Directory.Build.rsp")]
    [InlineData(".editorconfig")]
    [InlineData("src/.editorconfig")]
    [InlineData("src/.EditorConfig")]
    [InlineData("analyzers.globalconfig")]
    [InlineData("global.json")]
    [InlineData("NuGet.Config")]
    [InlineData("nuget.config")]
    [InlineData("rules.ruleset")]
    public void ChangesFor_AChangeToTheBuildConfiguration_SinglesNoFileOut(string changed)
    {
        // A project file, an imported .props/.targets, .editorconfig or global.json can raise or silence a warning in
        // any file (enabling <Nullable>, an analyzer's severity, a package): every warning may be the change's.
        var context = new FilterContext
        {
            WorkTreeChanges = new Lazy<WorkTreeChanges?>(Changes("src/A.cs", changed))
        };

        MsBuildDiagnosticReport.Parse(FourWarnings, Root).ChangesFor(context).Should().BeNull();
    }

    [Theory]
    [InlineData("src/A.cs")]
    [InlineData("README.md")]
    [InlineData("src/appsettings.json")]
    [InlineData("docs/editorconfig.md")]
    public void ChangesFor_ChangesToSourcesAlone_AreTheChanges(string changed)
    {
        var changes = Changes(changed);
        var context = new FilterContext { WorkTreeChanges = new Lazy<WorkTreeChanges?>(changes) };

        MsBuildDiagnosticReport.Parse(FourWarnings, Root).ChangesFor(context).Should().BeSameAs(changes);
    }

    [Fact]
    public void RenderDiagnostics_SwitchLineAfterASimpleDiagnostic_JoinsItsMessage()
    {
        // The real `dotnet build nothere.csproj`: MSBuild names the offending argument on the line after
        // its MSB1009, and that line is where the cause is, so it belongs to the message.
        Parse("dotnet_test_missing_project.txt").RenderDiagnostics()
            .Should().Be("(no file) MSB1009: Project file does not exist. Switch: nothere.csproj\n");
    }

    [Fact]
    public void RenderDiagnostics_SwitchLineAfterTheCommandLineEcho_JoinsItsMessage()
    {
        // The real `dotnet test --no-incremental`: MSB1001 echoes the full command line and a response-file
        // heading before the Switch: line, which still names the switch MSBuild rejected.
        Parse("dotnet_test_unknown_switch.txt").RenderDiagnostics()
            .Should().Be("(no file) MSB1001: Unknown switch. Switch: --no-incremental\n");
    }

    [Fact]
    public void RenderDiagnostics_SwitchLineAfterAResponseFilesSwitches_JoinsItsMessage()
    {
        // The real `dotnet build App --bogus` beside a Directory.Build.rsp: MSBuild names each switch the response
        // file added, and where it came from, before the Switch: line.
        const string output = """
                              Some command line switches were read from this response file:
                                /work/Directory.Build.rsp
                              MSBUILD : error MSB1001: Unknown switch.
                                  Full command line: '/usr/share/dotnet/sdk/10.0.404/MSBuild -maxcpucount App --bogus'
                                Switches appended by response files:
                              '-p:Foo=Bar' came from '/work/Directory.Build.rsp'
                              Switch: --bogus

                              For switch syntax, type "MSBuild -help"
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).RenderDiagnostics()
            .Should().Be("(no file) MSB1001: Unknown switch. Switch: --bogus\n");
    }

    [Fact]
    public void RenderDiagnostics_SwitchLineAfterAnUnrelatedLine_IsNotJoined()
    {
        const string output = """
                              MSBUILD : error MSB1001: Unknown switch.
                              Some other output
                              Switch: --no-incremental
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).RenderDiagnostics()
            .Should().Be("(no file) MSB1001: Unknown switch.\n");
    }

    [Fact]
    public void RenderDiagnostics_SwitchLineBesideNoDiagnostic_IsDropped()
    {
        MsBuildDiagnosticReport.Parse("Some output\nSwitch: nothere.csproj\n", Root).RenderDiagnostics()
            .Should().BeEmpty();
    }

    [Fact]
    public void RenderDiagnostics_SameSimpleDiagnosticTwiceWithItsSwitch_ListsItOnce()
    {
        const string output = """
                              MSBUILD : error MSB1009: Project file does not exist.
                              Switch: nothere.csproj
                              MSBUILD : error MSB1009: Project file does not exist.
                              Switch: nothere.csproj
                              """;

        MsBuildDiagnosticReport.Parse(output, Root).RenderDiagnostics()
            .Should().Be("(no file) MSB1009: Project file does not exist. Switch: nothere.csproj\n");
    }

    [Fact]
    public void Render_IsExactlyTheBodyRenderDiagnosticsReturnsPlusTheVerdictLine()
    {
        var expectedVerdicts = new Dictionary<string, string>
        {
            ["dotnet_build_errors.txt"] = "✗ dotnet build failed (exit 1): 3 errors, 1 warning (2 projects, 1.89s)\n",
            ["dotnet_build_warnings.txt"] = "✗ dotnet build failed (exit 1): 0 errors, 3 warnings (2 projects, 2.67s)\n",
        };

        foreach (var (name, verdict) in expectedVerdicts)
        {
            var report = Parse(name);

            var rendered = report.Render("dotnet build", 1, []);

            rendered.Should().Be(report.RenderDiagnostics() + verdict);
        }
    }

    [Fact]
    public void Render_Outputs_SitDirectlyAboveTheVerdictLine()
    {
        var report = Parse("dotnet_build_warnings.txt");

        var rendered = report.Render("dotnet publish", 0, ["App -> bin/publish/"]);

        rendered.Should().StartWith(report.RenderDiagnostics());
        rendered.Should().EndWith(
            "  App -> bin/publish/\n✓ dotnet publish: 0 errors, 3 warnings (2 projects, 2.67s)\n");
    }

    [Fact]
    public void Render_CleanRun_OutputsThenTheTickAsTheOnlyVerdict()
    {
        var report = Parse("dotnet_build_success.txt");

        report.Render("dotnet publish", 0, ["App -> bin/publish/"])
            .Should().Be("  App -> bin/publish/\n✓ dotnet publish: 0 errors, 0 warnings (6 projects, 2.34s)\n");
    }

    [Fact]
    public void Render_CleanRunWithDeclaredCounts_StatesThemBeforeTheContext()
    {
        var report = MsBuildDiagnosticReport.Parse(
            "  App -> bin/App.dll\n  App -> bin/publish/\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n", Root);

        report.Render("dotnet publish", 0, ["  App -> bin/publish/"])
            .Should().EndWith("✓ dotnet publish: 0 errors, 0 warnings (1 project)\n");
    }

    [Fact]
    public void Render_FailedRun_DropsOutputsAndEndsWithTheFailureVerdict()
    {
        var report = Parse("dotnet_build_errors.txt");

        var rendered = report.Render("dotnet pack", 3, ["pkg.nupkg"]);

        rendered.Should().NotContain("pkg.nupkg");
        rendered.Should().StartWith(report.RenderDiagnostics());
        rendered.Should().EndWith("✗ dotnet pack failed (exit 3): 3 errors, 1 warning (2 projects, 1.89s)\n");
    }

    private static WorkTreeChanges Changes(params string[] relativePaths) =>
        new(relativePaths.Select(path => Path.GetFullPath(path, Path.GetFullPath(Root))));

    private static MsBuildDiagnosticReport Parse(string fixtureName) =>
        MsBuildDiagnosticReport.Parse(LoadFixture(fixtureName), Root);

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(MsBuildDiagnosticReportTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

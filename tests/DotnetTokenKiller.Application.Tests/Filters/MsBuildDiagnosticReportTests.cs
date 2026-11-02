using DotnetTokenKiller.Application.Filters;
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
            + "1 warning suppressed (use --vv to see)\n");
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
            .Should().Be("  App -> bin/publish/\n✓ dotnet publish (6 projects, 2.34s)\n");
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

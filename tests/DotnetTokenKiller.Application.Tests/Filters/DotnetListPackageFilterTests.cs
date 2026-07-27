using System.Globalization;
using System.Text;
using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetListPackageFilterTests
{
    private readonly DotnetListPackageFilter _sut = new();

    [Fact]
    public Task Apply_PlainFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_PlainFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_list_package_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "list package filter should achieve ≥70% savings");
    }

    [Fact]
    public void Apply_PlainFixture_HoistsPackagesSharedByEveryProject()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0);

        result.Should().Contain("all projects:")
            .And.Contain("SonarAnalyzer.CSharp 10.29.0.143774");
    }

    [Theory]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Top-level Package")]
    [InlineData("has the following package references")]
    public void Apply_PlainFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0)
            .Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_PlainOutput_OmitsProjectsThatAddNothingBeyondTheSharedSet()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0
                               > OnlyAlpha            2.0.0       2.0.0

                            Project 'Beta' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("all projects: Shared 1.0.0")
            .And.Contain("Alpha: OnlyAlpha 2.0.0");
        result.Should().NotContain("Beta:", "Beta adds nothing beyond the shared set");
    }

    [Fact]
    public void Apply_NoSharedPackages_DegradesToPerProjectListing()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > OnlyAlpha            2.0.0       2.0.0

                            Project 'Beta' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > OnlyBeta             3.0.0       3.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().NotContain("all projects:");
        result.Should().Contain("Alpha: OnlyAlpha 2.0.0").And.Contain("Beta: OnlyBeta 3.0.0");
    }

    [Fact]
    public void Apply_EmptyOutput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
    }

    [Fact]
    public void Apply_ProjectWithZeroPackages_DoesNotFalselyClaimSharedPackages()
    {
        // Gamma has a header but no table rows at all — legitimate output for a project with no
        // package references. It must still count as one of the 3 projects and block "shared",
        // even though it never becomes a key in the by-project package map.
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0

                            Project 'Beta' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0

                            Project 'Gamma' has the following package references
                               [net10.0]:
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().NotContain("all projects:",
            "Gamma is one of the 3 projects and has no packages, so Shared is not present in every project");
    }

    [Fact]
    public Task Apply_OutdatedFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_outdated_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_Outdated_GroupsOnePackageAcrossProjects()
    {
        const string input = """
                            Project `Alpha` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0

                            Project `Beta` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("1 package with updates (all 2 projects)")
            .And.Contain("Analyzer 1.0.0 → 2.0.0 (2 projects)");
    }

    [Fact]
    public void Apply_Outdated_SplitsDistinctTransitionsForTheSamePackage()
    {
        const string input = """
                            Project `Alpha` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0

                            Project `Beta` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.5.0       1.5.0      2.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("Analyzer 1.0.0 → 2.0.0 (Alpha)")
            .And.Contain("Analyzer 1.5.0 → 2.0.0 (Beta)");
    }

    [Fact]
    public void Apply_Outdated_NoUpdates_CollapsesToOneLine()
    {
        const string input = """
                              Determining projects to restore...
                              All projects are up-to-date for restore.

                            The following sources were used:
                               https://api.nuget.org/v3/index.json

                            The given project `Alpha` has no updates given the current sources.
                            """;

        _sut.Apply(input, exitCode: 0)
            .Should().Be("✓ dotnet list package --outdated (all 1 project up to date)\n");
    }

    [Fact]
    public Task Apply_DeprecatedFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_VulnerableFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_vulnerable_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_DeprecatedFixture_DropsThePerProjectCleanLines()
    {
        _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0)
            .Should().NotContain("has no deprecated packages");
    }

    [Fact]
    public void Apply_DeprecatedFixture_KeepsReasonAndAlternative()
    {
        _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0)
            .Should().Contain("xunit 2.9.3 — Legacy → xunit.v3 >= 0.0.0");
    }

    [Fact]
    public void Apply_VulnerableFixture_KeepsSeverityAndAdvisory()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_vulnerable_raw.txt"), exitCode: 0);

        result.Should().Contain("2 vulnerable packages (1 of 2 projects)")
            .And.Contain("Legacy.Crypto 2.1.0 — Critical https://github.com/advisories/GHSA-dddd-eeee-ffff");
    }

    [Fact]
    public void Apply_NoVulnerablePackages_CollapsesToOneLine()
    {
        const string input = """
                            The given project `Alpha` has no vulnerable packages given the current sources.
                            The given project `Beta` has no vulnerable packages given the current sources.
                            """;

        _sut.Apply(input, exitCode: 0)
            .Should().Be("✓ dotnet list package --vulnerable (no vulnerable packages, 2 projects)\n");
    }

    [Fact]
    public void Apply_MultiWordDeprecationReason_IsNotSplit()
    {
        const string input = """
                            Project `Alpha` has the following deprecated packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Reason(s)       Alternative
                               > Risky                1.0.0       1.0.0      Critical Bugs   Safe >= 2.0.0
                            """;

        _sut.Apply(input, exitCode: 0).Should().Contain("Risky 1.0.0 — Critical Bugs → Safe >= 2.0.0");
    }

    [Fact]
    public void Apply_TransitiveTable_ParsesRowsWithoutARequestedColumn()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Direct               1.0.0       1.0.0

                               Transitive Package                 Resolved
                               > Indirect                         3.2.1
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("Direct 1.0.0").And.Contain("Indirect 3.2.1");
    }

    [Fact]
    public void Apply_FloatingVersion_ShowsRequestedAndResolved()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Floating             1.0.*       1.0.7
                            """;

        _sut.Apply(input, exitCode: 0).Should().Contain("Floating 1.0.*→1.0.7");
    }

    [Fact]
    public void Apply_MoreGroupsThanTheCap_StatesWhatWasOmitted()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 40; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Project 'P{i:D2}' has the following package references")
                .AppendLine("   [net10.0]:")
                .AppendLine("   Top-level Package      Requested   Resolved")
                .AppendLine(CultureInfo.InvariantCulture, $"   > Only{i:D2}                1.0.0       1.0.0")
                .AppendLine();
        }

        var result = _sut.Apply(sb.ToString(), exitCode: 0);

        result.Should().Contain("… and ")
            .And.Contain("more package")
            .And.Contain("use --show-log for full output");
    }

    [Fact]
    public void Apply_FailedRunWithUnparseableOutput_ReturnsEmptyForRawTailFallback()
    {
        const string input = "MSBUILD : error MSB1003: Specify a project or solution file.";

        _sut.Apply(input, exitCode: 1).Should().BeEmpty(
            "an empty result is what makes FilteredRunUseCase fall back to the raw tail");
    }

    [Fact]
    public void Apply_FailedRunWithParseableOutput_OmitsTheSuccessGlyph()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Direct               1.0.0       1.0.0
                            """;

        _sut.Apply(input, exitCode: 1).Should().NotContain("✓");
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetListPackageFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

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

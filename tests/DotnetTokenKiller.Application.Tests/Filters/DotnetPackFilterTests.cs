using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetPackFilterTests
{
    private readonly DotnetPackFilter _sut = new("/repo");

    [Fact]
    public void Apply_SuccessFixture_ReportsTheProjectsAndTheCreatedPackage()
    {
        var fixture = LoadFixture("dotnet_pack_success.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().Be(
            "✓ dotnet pack (2 projects)\n"
            + "  samples/SampleApp.MultiProject/bin/Release/SampleApp.MultiProject.0.5.0.nupkg\n");
    }

    [Fact]
    public void Apply_WarningsFixture_ReportsTheCountsThePackageAndTheWarnings()
    {
        var fixture = LoadFixture("dotnet_pack_warnings.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().StartWith(
            "dotnet pack: 0 errors, 34 warnings (1 project)\n"
            + "  samples/SampleApp.Warnings/bin/Release/SampleApp.Warnings.0.5.0.nupkg\n"
            + "---\n"
            + "CA1024 (1x)\n");
        result.Should().NotContain("missing a readme");
    }

    [Fact]
    public void Apply_FailureFixture_UsesTheBuildErrorLayout()
    {
        var fixture = LoadFixture("dotnet_pack_failure.txt");

        var result = _sut.Apply(fixture, exitCode: 1);

        result.Should().Be(
            "dotnet pack: 1 error, 0 warnings\n"
            + "---\n"
            + "samples/SampleApp.Broken/BrokenClass.cs (1 error)\n"
            + "  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'\n"
            + "Top codes: CS0029 (1x)\n");
    }

    [Theory]
    [InlineData("dotnet_pack_success.txt", 0, 70.0)]
    [InlineData("dotnet_pack_warnings.txt", 0, 40.0)]
    [InlineData("dotnet_pack_failure.txt", 1, 30.0)]
    public void Apply_Fixture_SavesAtLeast(string fixtureName, int exitCode, double minimumSavings)
    {
        var fixture = LoadFixture(fixtureName);

        var result = _sut.Apply(fixture, exitCode);

        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(minimumSavings);
    }

    [Fact]
    public void Apply_SymbolsPackage_ListsBothPackages()
    {
        const string input = """
                               Lib -> /repo/Lib/bin/Release/net10.0/Lib.dll
                               Successfully created package '/repo/artifacts/Lib.1.2.3.nupkg'.
                               Successfully created package '/repo/artifacts/Lib.1.2.3.snupkg'.
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be(
            "✓ dotnet pack (1 project)\n  artifacts/Lib.1.2.3.nupkg\n  artifacts/Lib.1.2.3.snupkg\n");
    }

    [Fact]
    public void Apply_NuGetPackWarningWithoutAFile_IsReported()
    {
        const string input = """
                               Lib -> /repo/Lib/bin/Release/net10.0/Lib.dll
                             /repo/Lib/Lib.csproj : warning NU5104: A stable release of a package should not have a prerelease dependency. [/repo/Lib/Lib.csproj]
                               Successfully created package '/repo/Lib/bin/Release/Lib.1.0.0.nupkg'.
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be(
            "dotnet pack: 0 errors, 1 warning (1 project)\n"
            + "  Lib/bin/Release/Lib.1.0.0.nupkg\n"
            + "---\n"
            + "NU5104 (1x)\n"
            + "  A stable release of a package should not have a prerelease dependency.\n");
    }

    [Fact]
    public void Apply_FailedRun_OmitsThePackagesAlreadyCreated()
    {
        const string input = """
                               Successfully created package '/repo/A/bin/Release/A.1.0.0.nupkg'.
                             /repo/B/B.csproj : error NU5017: Cannot create a package that has no dependencies nor content. [/repo/B/B.csproj]
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().NotContain(".nupkg");
        result.Should().StartWith("dotnet pack: 1 error, 0 warnings\n---\n(no file) (1 error)\n");
    }

    [Fact]
    public void Apply_EmptyOutput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetPackFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

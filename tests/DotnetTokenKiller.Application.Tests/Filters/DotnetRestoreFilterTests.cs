using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetRestoreFilterTests
{
    private readonly DotnetRestoreFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

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

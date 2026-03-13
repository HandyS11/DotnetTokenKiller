namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetPackFilterTests
{
    private readonly DotnetPackFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(85.0, "pack filter should achieve ≥85% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Build succeeded")]
    [InlineData("0 Warning(s)")]
    [InlineData("0 Error(s)")]
    [InlineData("Time Elapsed")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_BuildError_ShowsErrorGroupingFormat()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                               /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs(5,1): error CS0001: Type or namespace 'Foo' not found [/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]
                             Build FAILED.
                                 1 Error(s)
                             Time Elapsed 00:00:01.00
                             """;
        var result = _sut.Apply(input);
        result.Should().StartWith("dotnet pack: 1 error");
        result.Should().Contain("CS0001");
        result.Should().Contain("Top codes:");
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
        var assembly = typeof(DotnetPackFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

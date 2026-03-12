using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetNugetFilterTests
{
    private readonly DotnetNugetFilter _sut = new();

    [Fact]
    public Task Apply_PushFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_PushFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(75.0, "nuget push filter should achieve ≥75% savings");
    }

    [Theory]
    [InlineData("PUT https://")]
    [InlineData("Created https://")]
    [InlineData("Pushing DotnetTokenKiller")]
    public void Apply_PushFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_LocalsClear_ReturnsCompactMessage()
    {
        const string input = """
                             http-cache resources have been cleared.
                             global-packages resources have been cleared.
                             temp resources have been cleared.
                             plugins-cache resources have been cleared.
                             """;
        _sut.Apply(input).Should().Be("✓ nuget locals cleared\n");
    }

    [Fact]
    public void Apply_FallbackWithNoise_StripsHttpLines()
    {
        const string input = """
                             NuGet sources:
                               GET https://api.nuget.org/v3/index.json
                               OK https://api.nuget.org/v3/index.json 120ms
                             nuget.org [Enabled]
                               https://api.nuget.org/v3/index.json
                             """;
        var result = _sut.Apply(input);
        result.Should().NotContain("GET https://");
        result.Should().NotContain("OK https://");
        result.Should().Contain("nuget.org [Enabled]");
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
        var assembly = typeof(DotnetNugetFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

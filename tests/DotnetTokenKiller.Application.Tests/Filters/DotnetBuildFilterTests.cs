using DotnetTokenKiller.Application.Filters;
using FluentAssertions;
using VerifyXunit;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetBuildFilterTests
{
    private readonly DotnetBuildFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        return Verifier.Verify(result);
    }

    [Fact]
    public Task Apply_WarningsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        return Verifier.Verify(result);
    }

    [Fact]
    public Task Apply_ErrorsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        return Verifier.Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(85.0, because: "build success filter should achieve ≥85% savings");
    }

    [Fact]
    public void Apply_WarningsFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(75.0, because: "build warnings filter should achieve ≥75% savings");
    }

    [Fact]
    public void Apply_ErrorsFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(70.0, because: "build errors filter should achieve ≥70% savings");
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
        _sut.Apply(fixture).Should().NotContain(noiseLine);
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

    [Fact]
    public void Apply_AnsiCodesInInput_StrippedFromOutput()
    {
        const string ansiInput = "\x1b[32mBuild succeeded.\x1b[0m\n";
        var result = _sut.Apply(ansiInput);
        result.Should().NotContain("\x1b[");
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

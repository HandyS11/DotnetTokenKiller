using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetRunFilterTests
{
    private readonly DotnetRunFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_run_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast60Percent()
    {
        var fixture = LoadFixture("dotnet_run_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(60.0, "run filter should achieve ≥60% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Build succeeded")]
    [InlineData("Time Elapsed")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_run_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_SuccessFixture_PreservesAppOutput()
    {
        var fixture = LoadFixture("dotnet_run_raw.txt");
        var result = _sut.Apply(fixture);
        result.Should().Contain("DotnetTokenKiller v0.1.0");
        result.Should().Contain("Usage: dtk dotnet");
    }

    [Fact]
    public void Apply_PreambleOnly_ReturnsFallback()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                               Determining projects to restore...
                               All projects are up-to-date for restore.
                               MyApp -> /path/to/MyApp.dll
                             Build succeeded.
                                 0 Warning(s)
                                 0 Error(s)

                             Time Elapsed 00:00:01.23
                             """;
        _sut.Apply(input).Should().Be("✓ dotnet run completed\n");
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
    public void Apply_NoPreamble_PreservesOutputExactly()
    {
        const string input = "Hello World\nError: something failed\nDone.";
        _sut.Apply(input).Should().Be(input + "\n");
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetRunFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

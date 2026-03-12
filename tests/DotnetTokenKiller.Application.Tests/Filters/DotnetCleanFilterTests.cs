using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetCleanFilterTests
{
    private readonly DotnetCleanFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast95Percent()
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(95.0, "clean filter should achieve ≥95% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Cleaning configuration")]
    [InlineData("Build succeeded")]
    [InlineData("0 Warning(s)")]
    [InlineData("Time Elapsed")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_FailedBuild_ShowsUpToFiveErrorLines()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                             error MSB4057: The target "Clean" does not exist in the project.
                             error MSB4057: The target "Clean" does not exist in the project.
                             error MSB4057: The target "Clean" does not exist in the project.
                             error MSB4057: The target "Clean" does not exist in the project.
                             error MSB4057: The target "Clean" does not exist in the project.
                             error MSB4057: The target "Clean" does not exist in the project.
                             Build FAILED.
                                 0 Warning(s)
                                 6 Error(s)
                             Time Elapsed 00:00:00.10
                             """;
        var result = _sut.Apply(input);
        var errorLines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        errorLines.Length.Should().BeLessThanOrEqualTo(5, "only up to 5 error lines should be shown");
        result.Should().Contain("error MSB4057");
    }

    [Fact]
    public void Apply_FailedBuild_OutputContainsErrorContent()
    {
        const string input = """
                             MSBuild version 17.11.9+a69bbaaf5 for .NET
                             error MSB4019: The imported project "/missing/file.targets" was not found.
                             Build FAILED.
                                 1 Error(s)
                             Time Elapsed 00:00:00.05
                             """;
        var result = _sut.Apply(input);
        result.Should().Contain("error MSB4019");
        result.Should().NotStartWith("✓");
    }

    [Fact]
    public void Apply_AnsiInput_StripsAnsiBeforeProcessing()
    {
        const string input = "\x1b[32mBuild succeeded.\x1b[0m\n    0 Warning(s)\n    0 Error(s)\n";
        var result = _sut.Apply(input);
        result.Should().Be("✓ dotnet clean\n");
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
        var assembly = typeof(DotnetCleanFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

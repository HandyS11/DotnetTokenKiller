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
    public void Apply_FailedBuild_ShowsUpToFiveErrorLinesAndTruncationNotice()
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
        var outputLines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var errorLines = outputLines.Where(l =>
                l.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("...", StringComparison.Ordinal))
            .ToArray();
        errorLines.Length.Should().BeLessThanOrEqualTo(5, "only up to 5 error lines should be shown");
        result.Should().Contain("error MSB4057");
        result.Should().Contain("... and 1 more error");
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
    public void Apply_NullInput_ReturnsEmpty()
    {
        _sut.Apply(null!).Should().BeEmpty();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        // Kills null coalescing mutations (line 15)
        var filter = new DotnetCleanFilter();
        const string input = "Build succeeded.";

        var result = filter.Apply(input);

        result.Should().Be("✓ dotnet clean\n");
    }

    [Fact]
    public void Apply_SuccessOutput_ReturnsCheckmarkMessage()
    {
        // Kills string mutation on "✓ dotnet clean" (line 23)
        const string input = "Build succeeded.\n    0 Warning(s)\n    0 Error(s)";

        var result = _sut.Apply(input);

        result.Should().Be("✓ dotnet clean\n");
    }

    [Fact]
    public void Apply_ExactlyMaxErrorLines_NoTruncationNotice()
    {
        // Kills equality mutation: totalErrors >= MaxErrorLines → totalErrors > MaxErrorLines (line 62)
        var lines = string.Join("\n",
            Enumerable.Range(1, 5).Select(i => $"error MSB400{i}: Error {i}"));
        var input = $"{lines}\nBuild FAILED.";

        var result = _sut.Apply(input);

        result.Should().NotContain("... and");
        result.Should().Contain("error MSB4001");
        result.Should().Contain("error MSB4005");
    }

    [Fact]
    public void Apply_SixErrors_ShowsTruncationWithSingularForm()
    {
        // Kills conditional/equality/arithmetic/string mutations on "more error(s)" line (line 65)
        var lines = string.Join("\n",
            Enumerable.Range(1, 6).Select(i => $"error MSB400{i}: Error {i}"));
        var input = $"{lines}\nBuild FAILED.";

        var result = _sut.Apply(input);

        result.Should().Contain("... and 1 more error");
        result.Should().NotContain("more errors");
    }

    [Fact]
    public void Apply_SevenErrors_ShowsTruncationWithPluralForm()
    {
        // Kills conditional mutations on plural form (line 65)
        var lines = string.Join("\n",
            Enumerable.Range(1, 7).Select(i => $"error MSB40{i:D2}: Error {i}"));
        var input = $"{lines}\nBuild FAILED.";

        var result = _sut.Apply(input);

        result.Should().Contain("... and 2 more errors");
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

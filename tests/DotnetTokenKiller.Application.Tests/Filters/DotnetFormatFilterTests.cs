using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetFormatFilterTests
{
    private readonly DotnetFormatFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_FilesFormattedFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_ViolationsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_VerboseNothingFixture_SavingsAtLeast90Percent()
    {
        // Represents `dotnet format --verbosity diagnostic` when nothing needs formatting.
        // Default-verbosity format produces 0 bytes (covered by Apply_EmptyInput_ReturnsNothingToFormat).
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "format filter should achieve ≥90% savings on verbose output");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_DoesNotContainLoadingConfiguration()
    {
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        _sut.Apply(fixture, exitCode: 0).Should().NotContain("Loading configuration");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_DoesNotContainFormatComplete()
    {
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        _sut.Apply(fixture, exitCode: 0).Should().NotContain("Format complete");
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNothingToFormat()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNothingToFormat()
    {
        _sut.Apply(null!, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_AnsiOnlyInput_ReturnsNothingToFormat()
    {
        const string input = "\x1b[32m\x1b[0m\n";
        _sut.Apply(input, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_ReturnsNothingToFormatWithElapsed()
    {
        // Verbose output contains "Format complete in 3000ms." — the filter extracts the timing.
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Be("✓ dotnet format (nothing to format, 3.00s)\n");
    }

    [Fact]
    public void Apply_FilesFormattedFixture_ReturnsFormattedCount()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Contain("3 files formatted");
    }

    [Fact]
    public void Apply_FilesFormattedFixture_IncludesElapsed()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Contain("2.00s");
    }

    [Fact]
    public void Apply_SingleFileFormatted_UsesSingular()
    {
        const string input = """
                               Formatted code file '/test/project/root/src/Foo.cs'.
                             Format complete in 1000ms.
                             """;
        _sut.Apply(input, exitCode: 0).Should().Be("✓ dotnet format (1 file formatted, 1.00s)\n");
    }

    [Fact]
    public void Apply_ViolationsFixture_ShowsViolationCount()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        result.Should().Contain("2 violations");
    }

    [Fact]
    public void Apply_ViolationsFixture_ShowsShortPaths()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        result.Should().NotContain("/test/project/root/");
        result.Should().Contain("src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs");
    }

    [Fact]
    public void Apply_ViolationsFixture_DoesNotContainFormatComplete()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        _sut.Apply(fixture, exitCode: 1).Should().NotContain("Format complete");
    }

    [Fact]
    public void Apply_SingleViolation_UsesSingular()
    {
        const string input =
            "  /test/project/root/src/Foo.cs(1,1): error whitespace: Fix whitespace formatting.\nFormat complete in 500ms.";
        var result = _sut.Apply(input, exitCode: 1);
        result.Should().Contain("1 violation");
        result.Should().NotContain("1 violations");
    }

    [Fact]
    public void Apply_ElevenViolations_ShowsTruncationNotice()
    {
        var lines = string.Join("\n",
            Enumerable.Range(1, 11).Select(i =>
                $"  /test/project/root/src/File{i}.cs(1,1): error whitespace: Fix whitespace formatting."));
        var result = _sut.Apply(lines, exitCode: 1);
        result.Should().Contain("... and 1 more violation");
        result.Should().NotContain("more violations");
    }

    [Fact]
    public void Apply_TwelveViolations_ShowsPluralTruncationNotice()
    {
        var lines = string.Join("\n",
            Enumerable.Range(1, 12).Select(i =>
                $"  /test/project/root/src/File{i}.cs(1,1): error whitespace: Fix whitespace formatting."));
        var result = _sut.Apply(lines, exitCode: 1);
        result.Should().Contain("... and 2 more violations");
    }

    [Fact]
    public void Apply_ExactlyMaxViolationLines_NoTruncationNotice()
    {
        var lines = string.Join("\n",
            Enumerable.Range(1, 10).Select(i =>
                $"  /test/project/root/src/File{i}.cs(1,1): error whitespace: Fix whitespace formatting."));
        var result = _sut.Apply(lines, exitCode: 1);
        result.Should().NotContain("... and");
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        var filter = new DotnetFormatFilter();
        var result = filter.Apply(string.Empty, exitCode: 0);
        result.Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_NoElapsedInOutput_OmitsElapsedFromSummary()
    {
        const string input = "  Formatted code file '/test/project/root/src/Foo.cs'.";
        var result = _sut.Apply(input, exitCode: 0);
        result.Should().Be("✓ dotnet format (1 file formatted)\n");
    }

    [Fact]
    public void Apply_NonZeroExitWithNoParsedViolations_ReturnsEmpty()
    {
        // Crashed process / unparseable output: no violation lines were parsed, but the exit code
        // is non-zero. Returning "0 violations" here would look like a clean success and would
        // prevent FilteredRunUseCase's raw-tail fallback from ever firing.
        const string input = "Segmentation fault (core dumped)";

        var result = _sut.Apply(input, exitCode: 139);

        result.Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetFormatFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

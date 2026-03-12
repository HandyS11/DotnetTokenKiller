using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetFormatFilterTests
{
    private readonly DotnetFormatFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "format filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("Determining projects")]
    [InlineData("All projects are up-to-date")]
    [InlineData("DotnetBuildFilter.cs formatted")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_SuccessFixture_ContainsFileCount()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        result.Should().Contain("3 files");
    }

    [Fact]
    public void Apply_CheckModeWithWarnings_ReturnsFileList()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        const string input = """
                             Determining projects to restore...
                             All projects are up-to-date for restore.
                             /repo/src/Foo.cs - warning IDE0055: Fix formatting.
                             /repo/src/Bar.cs - warning WHITESPACE: Fix whitespace.
                             Format complete in 0.15s.
                             """;
        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 2 files need formatting");
        result.Should().Contain("src/Foo.cs");
        result.Should().Contain("src/Bar.cs");
    }

    [Fact]
    public void Apply_CheckModeWithWarnings_DeduplicatesFiles()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        const string input = """
                             /repo/src/Foo.cs - warning IDE0055: Fix formatting.
                             /repo/src/Foo.cs - warning WHITESPACE: Fix whitespace.
                             /repo/src/Bar.cs - warning IDE0055: Fix formatting.
                             Format complete in 0.10s.
                             """;
        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 2 files need formatting");
        result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("Foo.cs", StringComparison.Ordinal))
            .Should().Be(1, "duplicate files must be deduplicated");
    }

    [Fact]
    public void Apply_CheckModeTruncation_ShowsMaxFilesAndRemainder()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        var lines = Enumerable.Range(1, 25)
            .Select(i => $"/repo/src/File{i:D2}.cs - warning IDE0055: Fix formatting.")
            .ToList();
        var input = string.Join('\n', lines) + "\nFormat complete in 0.50s.\n";

        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 25 files need formatting");
        result.Should().Contain("+5 more");
        result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("File", StringComparison.Ordinal) && l.Contains(".cs", StringComparison.Ordinal))
            .Should().Be(20, "only 20 files should be shown");
    }

    [Fact]
    public void Apply_CheckModeNoChanges_ReturnsNoChanges()
    {
        const string input = """
                             Determining projects to restore...
                             All projects are up-to-date for restore.
                             Format complete in 0.10s.
                             """;
        _sut.Apply(input).Should().Be("✓ dotnet format (no changes)\n");
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
        var assembly = typeof(DotnetFormatFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

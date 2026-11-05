using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetToolRestoreFilterTests
{
    private readonly DotnetToolRestoreFilter _sut = new("/test/project/root");

    [Fact]
    public void Apply_Success_OneLineWithCommands()
    {
        var result = _sut.Apply(LoadFixture("dotnet_tool_restore_success.txt"), exitCode: 0);

        result.Should().Be("✓ dotnet tool restore: 4 tools (docfx, jb, dotnet-stryker, dtk)\n");
    }

    [Fact]
    public void Apply_MissingPackage_KeepsErrorThenStatus()
    {
        var result = _sut.Apply(LoadFixture("dotnet_tool_restore_missing_package.txt"), exitCode: 1);

        result.Should().Be(
            "Version 1.0.0 of package dotnettokenkiller.doesnotexist is not found in NuGet feeds https://api.nuget.org/v3/index.json.\n" +
            "✗ dotnet tool restore failed (exit 1): 1 error\n");
    }

    [Fact]
    public void Apply_UnreachableFeed_DeduplicatesRepeatedErrors()
    {
        const string line = "Unable to load the service index for source https://127.0.0.1:9/v3/index.json.";
        var input = $"{line}\n{line}\n{line}\n";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be($"{line}\n✗ dotnet tool restore failed (exit 1): 1 error\n");
    }

    [Fact]
    public void Apply_PartialFailure_ReportsErrorsAndRestoredTools()
    {
        // The output never says how many tools a failed restore attempted (SDK 10.0.403 stops at the first missing
        // package and prints no "was restored" line), so the status counts only what it shows: the distinct error
        // lines, and the tools that were restored.
        const string input = "Tool 'a' (version '1') was restored. Available commands: a\n" +
                             "Tool 'b' (version '1') was restored. Available commands: b\n" +
                             "Tool 'c' (version '1') was restored. Available commands: c\n" +
                             "Version 1.0.0 of package d is not found in NuGet feeds x.\n" +
                             "Version 2.0.0 of package e is not found in NuGet feeds x.\n";

        _sut.Apply(input, exitCode: 1).Should().EndWith(
            "✗ dotnet tool restore failed (exit 1): 2 errors · 3 tools restored\n");
    }

    [Fact]
    public void Apply_UnreachableFeed_FixtureEndsWithStatus()
    {
        var result = _sut.Apply(LoadFixture("dotnet_tool_restore_unreachable_feed.txt"), exitCode: 1);

        result.Should().StartWith("Unhandled exception: Unable to load the service index");
        result.Should().EndWith("✗ dotnet tool restore failed (exit 1): 1 error\n");
    }

    [Theory]
    [InlineData("dotnet_tool_restore_success.txt", 0)]
    [InlineData("dotnet_tool_restore_missing_package.txt", 1)]
    [InlineData("dotnet_tool_restore_unreachable_feed.txt", 1)]
    public void Apply_Crlf_MatchesLf(string name, int exitCode)
    {
        var fixture = LoadFixture(name);

        var result = _sut.Apply(fixture.ReplaceLineEndings("\r\n"), exitCode);

        result.Should().NotContain("\r");
        result.Should().Be(_sut.Apply(fixture, exitCode));
    }

    [Fact]
    public void Apply_Empty_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
        _sut.Apply(string.Empty, exitCode: 1).Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetToolRestoreFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

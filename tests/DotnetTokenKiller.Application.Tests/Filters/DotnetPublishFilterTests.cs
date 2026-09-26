using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetPublishFilterTests
{
    private readonly DotnetPublishFilter _sut = new("/repo");

    [Fact]
    public void Apply_SuccessFixture_ReportsTheProjectsAndThePublishDirectory()
    {
        var fixture = LoadFixture("dotnet_publish_success.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().Be(
            "✓ dotnet publish (2 projects)\n"
            + "  SampleApp.MultiProject -> samples/SampleApp.MultiProject/bin/Release/net10.0/publish/\n");
    }

    [Fact]
    public void Apply_WarningsFixture_ReportsTheCountsThePublishDirectoryAndTheWarnings()
    {
        var fixture = LoadFixture("dotnet_publish_warnings.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().StartWith(
            "dotnet publish: 0 errors, 34 warnings (1 project)\n"
            + "  SampleApp.Warnings -> samples/SampleApp.Warnings/bin/Release/net10.0/publish/\n"
            + "---\n"
            + "CA1024 (1x)\n");
        result.Should().Contain("  samples/SampleApp.Warnings/NullableWarnings.cs:10 — Converting null literal");
    }

    [Fact]
    public void Apply_FailureFixture_UsesTheBuildErrorLayout()
    {
        var fixture = LoadFixture("dotnet_publish_failure.txt");

        var result = _sut.Apply(fixture, exitCode: 1);

        result.Should().Be(
            "dotnet publish: 1 error, 0 warnings\n"
            + "---\n"
            + "samples/SampleApp.Broken/BrokenClass.cs (1 error)\n"
            + "  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'\n"
            + "Top codes: CS0029 (1x)\n");
    }

    [Theory]
    [InlineData("dotnet_publish_success.txt", 0, 75.0)]
    [InlineData("dotnet_publish_warnings.txt", 0, 40.0)]
    [InlineData("dotnet_publish_failure.txt", 1, 35.0)]
    public void Apply_Fixture_SavesAtLeast(string fixtureName, int exitCode, double minimumSavings)
    {
        var fixture = LoadFixture(fixtureName);

        var result = _sut.Apply(fixture, exitCode);

        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(minimumSavings);
    }

    [Fact]
    public void Apply_FailedRun_OmitsThePublishDirectoriesOfTheProjectsThatSucceeded()
    {
        // A failed publish's summary is a failed build's: listing the directories that did get
        // published would read as a partial success the exit code does not grant.
        const string input = """
                               Lib -> /repo/Lib/bin/Release/net10.0/Lib.dll
                               Lib -> /repo/Lib/bin/Release/net10.0/publish/
                             /repo/App/Program.cs(3,1): error CS0103: The name 'x' does not exist in the current context [/repo/App/App.csproj]
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().NotContain("publish/");
        result.Should().StartWith("dotnet publish: 1 error, 0 warnings (1 project)\n---\n");
    }

    [Fact]
    public void Apply_NormalVerbosity_ReadsTheDeeperIndentedPublishLineAndTheTally()
    {
        const string input = """
                             Build started 9/22/2026 10:00:00 AM.
                                  1>Project "/repo/App/App.csproj" on node 1 (Publish target(s)).
                                    App -> /repo/App/bin/Release/net10.0/App.dll
                                  Publish:
                                    App -> /repo/App/bin/Release/net10.0/publish/
                             Build succeeded.
                                 0 Warning(s)
                                 0 Error(s)

                             Time Elapsed 00:00:01.50
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet publish (1 project, 1.50s)\n  App -> App/bin/Release/net10.0/publish/\n");
    }

    [Fact]
    public void Apply_SeveralProjects_ListsEveryPublishDirectoryInOrder()
    {
        const string input = """
                               A -> /repo/A/bin/Release/net10.0/A.dll
                               A -> /repo/A/bin/Release/net10.0/publish/
                               B -> /repo/B/bin/Release/net10.0/linux-x64/B.dll
                               B -> /repo/B/bin/Release/net10.0/linux-x64/publish/
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be(
            "✓ dotnet publish (2 projects)\n"
            + "  A -> A/bin/Release/net10.0/publish/\n"
            + "  B -> B/bin/Release/net10.0/linux-x64/publish/\n");
    }

    [Fact]
    public void Apply_EmptyOutput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
    }

    [Fact]
    public void Apply_FailureWithNothingParsed_ReturnsEmptySoTheRawTailIsShown()
    {
        _sut.Apply("Something the filter cannot read\n", exitCode: 1).Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetPublishFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

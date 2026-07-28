using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.Passthrough")]
[Trait("Category", "Integration")]
public class ListPackageIntegrationTests
{
    private static readonly string SampleApp = IntegrationTestHelper.SamplePath("SampleApp");

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task ListPackage_IsFiltered_AndTrackedUnderItsFullName()
    {
        // The test host's working directory has no ambient project/solution to discover, so a
        // target must be supplied explicitly, as every other integration test does with a sample
        // app. `dotnet list <PROJECT> package` puts the project between the two tokens, which
        // would land it outside the args this command builds, so `--project` is used instead.
        var (output, exitCode, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync(
            "dotnet", "list", "package", "--project", SampleApp);

        exitCode.Should().Be(0);
        output.Should().NotContain("Top-level Package", "the raw table must not survive filtering");
        output.Should().Contain("dotnet list package");

        var commands = await IntegrationTestHelper.ReadTrackedCommandsAsync(dbPath);
        commands.Should().Contain("list package");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task ListReference_StillPassesThroughUnfiltered()
    {
        var (output, _, _) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "list", "reference");

        // Passthrough forwards dotnet's own output verbatim, whatever it is — it must never be
        // rendered by the list package filter.
        output.Should().NotContain("✓ dotnet list package");
    }
}

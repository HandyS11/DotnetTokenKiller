using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class DotnetPublishPackIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Publish_SampleApp_ReportsSuccessAndThePublishDirectory()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines[0].Should().StartWith("✓ dotnet publish");
        lines.Should().Contain(line => line.Contains("SampleApp -> ", StringComparison.Ordinal)
                                       && line.TrimEnd().EndsWith("publish/", StringComparison.Ordinal));
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Publish_SampleAppBroken_ShowsTheBuildErrorLayout()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleAppBroken);

        exitCode.Should().NotBe(0);
        output.Should().Contain("dotnet publish: 1 error").And.Contain("CS0029");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Pack_SampleApp_ReportsSuccessAndTheCreatedPackage()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"dtk-pack-{Guid.NewGuid():N}");
        try
        {
            var (output, exitCode) =
                await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleApp, "-o", outputDir);

            exitCode.Should().Be(0);
            var lines = output.Trim().Split('\n');
            lines[0].Should().StartWith("✓ dotnet pack");
            lines.Should().Contain(line => line.TrimEnd().EndsWith(".nupkg", StringComparison.Ordinal));
            output.Should().NotContain("missing a readme");
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }
}

using System.Text.Json;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Output another program parses reaches it untouched. The runs read SampleApp (an implicit restore may rewrite
/// its <c>obj/</c>), so they share the collection of every other suite that touches it.
/// </summary>
[Collection("Integration.SampleApp")]
[Trait("Category", "Integration")]
public class MachineReadableOutputIntegrationTests
{
    private static readonly string SampleApp = IntegrationTestHelper.SamplePath("SampleApp");

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task BuildGetProperty_PrintsExactlyWhatDotnetPrints()
    {
        // -getProperty prints the value and no build log; the build filter would replace it with a status line.
        var (raw, rawExit) = await IntegrationTestHelper.RunDotnetAsync("build", SampleApp, "-getProperty:TargetFramework");

        var (output, exitCode, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync(
            "dotnet", "build", SampleApp, "-getProperty:TargetFramework");

        // The SDK ends the value with the platform's newline (CRLF on Windows); only the bytes dtk passes on must match.
        raw.ReplaceLineEndings("\n").Should().Be("net10.0\n", "the fixture is the SDK's own output");
        output.Should().Be(raw);
        exitCode.Should().Be(rawExit);
        var commands = await IntegrationTestHelper.ReadTrackedCommandsAsync(dbPath);
        commands.Should().Equal("build");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task BuildGetProperty_DoesNotForwardDtkOptionsToDotnet()
    {
        // dotnet rejects -q, so forwarding it would turn a value into a usage error.
        var (raw, _) = await IntegrationTestHelper.RunDotnetAsync("build", SampleApp, "-getProperty:TargetFramework");

        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "build", SampleApp, "-q", "-getProperty:TargetFramework");

        output.Should().Be(raw);
        exitCode.Should().Be(0);
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task ListPackageJson_IsByteIdenticalToDotnetAndParses()
    {
        var (raw, rawExit) = await IntegrationTestHelper.RunDotnetAsync(
            "list", "package", "--project", SampleApp, "--format", "json");

        var (output, exitCode, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync(
            "dotnet", "list", "package", "--project", SampleApp, "--format", "json");

        output.Should().Be(raw);
        exitCode.Should().Be(rawExit);
        output.Should().NotContain("⚠", "a trailing status line would make the document unparseable");
        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("projects").GetArrayLength().Should().BeGreaterThan(0);
        var commands = await IntegrationTestHelper.ReadTrackedCommandsAsync(dbPath);
        commands.Should().Equal("list package");
    }
}

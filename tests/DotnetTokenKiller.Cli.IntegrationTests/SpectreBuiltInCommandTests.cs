using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using DotnetTokenKiller.Domain.Text;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Spectre.Console.Cli adds hidden built-in commands whose constructors take its internal services, so
/// they resolve only through the reflective <c>TypeRegistrar.Register</c>. Parity tests cannot catch a
/// break that hits the JIT and AOT builds alike; these can. With <c>DTK_TEST_BINARY</c> set they run
/// against that binary.
/// </summary>
public sealed class SpectreBuiltInCommandTests
{
    [Theory(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    [InlineData(new[] { "cli", "version" }, "Spectre.Console.Cli version")]
    [InlineData(new[] { "cli", "explain" }, "CLI Configuration")]
    [InlineData(new[] { "cli", "opencli" }, "\"opencli\"")]
    [InlineData(new[] { "--help-dump-opencli" }, "\"opencli\"")]
    public async Task BuiltInCommand_RunsAsync(string[] arguments, string expected)
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(arguments);

        // Spectre colours its output when it detects a CI service such as GitHub Actions, and
        // `cli version` colours the library name apart from the word after it.
        var text = AnsiStrip.Strip(output);
        exitCode.Should().Be(0, text);
        text.Should().Contain(expected).And.NotContain("Could not resolve type");
    }
}

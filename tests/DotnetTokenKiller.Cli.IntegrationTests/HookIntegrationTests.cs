using System.Text.Json.Nodes;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>Runs <c>dtk hook</c> as the harnesses do: a separate process, payload on stdin, reply on stdout.</summary>
public class HookIntegrationTests
{
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Claude_RewritesAndPreservesNonAsciiTextAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"tool_input":{"command":"dotnet build # répertoire"}}""", "hook", "claude");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build # répertoire", "stdin must be decoded as UTF-8 whatever the console code page");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Gemini_NothingToRewrite_AllowsAsync()
    {
        var (stdout, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"tool_input":{"command":"ls"}}""", "hook", "gemini");

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("""{"decision":"allow"}""");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_CopilotCli_CompoundCommand_AsksAsync()
    {
        var (stdout, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolName":"bash","toolArgs":"{\"command\":\"dotnet test; ls\"}"}""", "hook", "copilot-cli");

        exitCode.Should().Be(0);
        var root = JsonNode.Parse(stdout)!;
        root["permissionDecision"]!.GetValue<string>().Should().Be("ask");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test; ls");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_UnknownProvider_ExitsZeroWithUsageOnStderrAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync("{}", "hook", "cursor");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("dtk hook <claude|gemini|copilot-cli>");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_DoesNotTouchTrackingOrConfigAsync()
    {
        // The hook fires on every shell tool call, so it must never open the tracking database or read config.
        var dir = IntegrationTestHelper.NewIsolatedDir();

        var (_, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsInDirAsync(
            dir, """{"tool_input":{"command":"dotnet build"}}""", "hook", "claude");

        exitCode.Should().Be(0);
        Directory.Exists(dir).Should().BeFalse("dtk hook must write nothing under the tracking, tee or config paths");
    }
}

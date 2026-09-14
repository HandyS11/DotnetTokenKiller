using System.Text;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class HookPayloadsTests
{
    [Theory]
    [InlineData("claude", HookPayloadKind.ClaudeCode)]
    [InlineData("gemini", HookPayloadKind.GeminiCli)]
    [InlineData("copilot-cli", HookPayloadKind.CopilotCli)]
    internal void TryGetKind_KnownProvider_Resolves(string provider, HookPayloadKind expected)
    {
        HookPayloads.TryGetKind(provider, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("copilot")]
    [InlineData("")]
    public void TryGetKind_UnknownProvider_Fails(string provider)
    {
        HookPayloads.TryGetKind(provider, out _).Should().BeFalse();
    }

    [Fact]
    public void HookCommands_RenderTheRegisteredCommands()
    {
        HookCommands.Invocation("claude").Should().Be("dtk hook claude");
        HookCommands.FailOpen("gemini").Should().Be("dtk hook gemini; exit 0");
    }

    [Fact]
    public void Claude_Rewrite_ReturnsTheWholeToolInputWithTheCommandReplaced()
    {
        var reply = Reply(HookPayloadKind.ClaudeCode,
            """{"tool_name":"Bash","tool_input":{"command":"dotnet build","description":"Build","timeout":120000}}""");

        var root = JsonNode.Parse(reply!)!;
        root["hookSpecificOutput"]!["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        var updated = root["hookSpecificOutput"]!["updatedInput"]!;
        updated["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        updated["description"]!.GetValue<string>().Should().Be("Build");
        updated["timeout"]!.GetValue<int>().Should().Be(120000);
    }

    [Theory]
    [InlineData("""{"tool_input":{"command":"ls -la"}}""")]
    [InlineData("""{"tool_input":{"command":""}}""")]
    [InlineData("""{"tool_input":{"command":42}}""")]
    [InlineData("""{"tool_input":{}}""")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("")]
    public void Claude_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.ClaudeCode, payload).Should().BeNull();
    }

    [Fact]
    public void Claude_NonAsciiCommand_RoundTripsExactly()
    {
        var reply = Reply(HookPayloadKind.ClaudeCode, """{"tool_input":{"command":"dotnet build # répertoire ’ok’"}}""");

        JsonNode.Parse(reply!)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build # répertoire ’ok’");
    }

    [Fact]
    public void Claude_LeadingUtf8Bom_IsIgnored()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("""{"tool_input":{"command":"dotnet test"}}""")).ToArray();

        HookPayloads.Reply(HookPayloadKind.ClaudeCode, bytes).Should().Contain("dtk dotnet test");
    }

    [Fact]
    public void Gemini_Rewrite_AllowsWithTheRewrittenToolInput()
    {
        var reply = Reply(HookPayloadKind.GeminiCli, """{"tool_name":"run_shell_command","tool_input":{"command":"dotnet test"}}""");

        var root = JsonNode.Parse(reply!)!;
        root["decision"]!.GetValue<string>().Should().Be("allow");
        root["hookSpecificOutput"]!["tool_input"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
    }

    [Theory]
    [InlineData("""{"tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_input":{}}""")]
    [InlineData("[]")]
    public void Gemini_ParsedButNothingToRewrite_AllowsWithoutChange(string payload)
    {
        Reply(HookPayloadKind.GeminiCli, payload).Should().Be("""{"decision":"allow"}""");
    }

    [Fact]
    public void Gemini_InvalidJson_PrintsNothing()
    {
        Reply(HookPayloadKind.GeminiCli, "{").Should().BeNull();
    }

    [Theory]
    [InlineData("""{"toolName":"bash","toolArgs":{"command":"dotnet build","description":"d"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"{\"command\":\"dotnet build\",\"description\":\"d\"}"}""")]
    public void Copilot_SimpleCommand_AllowsWithModifiedArgs(string payload)
    {
        var root = JsonNode.Parse(Reply(HookPayloadKind.CopilotCli, payload)!)!;

        root["permissionDecision"]!.GetValue<string>().Should().Be("allow");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        root["modifiedArgs"]!["description"]!.GetValue<string>().Should().Be("d");
    }

    [Fact]
    public void Copilot_CompoundCommand_AsksInsteadOfAllowing()
    {
        var root = JsonNode.Parse(Reply(HookPayloadKind.CopilotCli,
            """{"toolName":"bash","toolArgs":{"command":"dotnet build && rm -rf x"}}""")!)!;

        root["permissionDecision"]!.GetValue<string>().Should().Be("ask");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build && rm -rf x");
    }

    [Theory]
    [InlineData("""{"toolName":"powershell","toolArgs":{"command":"dotnet build"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":{"command":"ls"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"not json"}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"[1]"}""")]
    [InlineData("""{"toolName":"bash"}""")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Copilot_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.CopilotCli, payload).Should().BeNull();
    }

    private static string? Reply(HookPayloadKind kind, string payload) =>
        HookPayloads.Reply(kind, Encoding.UTF8.GetBytes(payload));
}

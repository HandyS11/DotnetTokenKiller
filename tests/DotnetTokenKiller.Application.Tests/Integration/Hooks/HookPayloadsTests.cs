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
    [InlineData("codex", HookPayloadKind.CodexCli)]
    [InlineData("opencode", HookPayloadKind.OpenCode)]
    [InlineData("antigravity", HookPayloadKind.AntigravityCli)]
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
        HookCommands.OrExitZero("antigravity").Should().Be("dtk hook antigravity || exit 0");
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

    [Theory]
    [InlineData("""{"tool_input":{"command":"a","command":"b"}}""")]
    [InlineData("""{"tool_input":{"command":"a"},"tool_input":{"command":"b"}}""")]
    public void Claude_DuplicateJsonKey_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.ClaudeCode, payload).Should().BeNull();
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

    [Fact]
    public void Gemini_DuplicateJsonKey_AllowsWithoutChange()
    {
        Reply(HookPayloadKind.GeminiCli, """{"tool_input":{"command":"a","command":"b"}}""").Should().Be("""{"decision":"allow"}""");
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

    [Theory]
    [InlineData("""{"toolName":"bash","toolArgs":{"command":"a"},"toolArgs":{"command":"b"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"{\"command\":\"a\",\"command\":\"b\"}"}""")]
    public void Copilot_DuplicateJsonKey_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.CopilotCli, payload).Should().BeNull();
    }

    [Fact]
    public void Codex_Rewrite_AllowsWithTheWholeToolInputAsUpdatedInput()
    {
        var reply = Reply(HookPayloadKind.CodexCli,
            """{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet build","extra":1}}""");

        var output = JsonNode.Parse(reply!)!["hookSpecificOutput"]!;
        output["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        output["permissionDecision"]!.GetValue<string>().Should().Be("allow", "Codex rejects updatedInput without it");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        output["updatedInput"]!["extra"]!.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_name":"apply_patch","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":42,"tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":""}}""")]
    [InlineData("""{"tool_name":"Bash"}""")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Codex_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.CodexCli, payload).Should().BeNull();
    }

    [Fact]
    public void Codex_PayloadWithoutAToolName_StillRewrites()
    {
        // doctor's probe sends only tool_input.
        Reply(HookPayloadKind.CodexCli, """{"tool_input":{"command":"dotnet test"}}""").Should().Contain("dtk dotnet test");
    }

    [Fact]
    public void Codex_DuplicateJsonKey_PrintsNothing()
    {
        Reply(HookPayloadKind.CodexCli, """{"tool_name":"Bash","tool_input":{"command":"a","command":"b"}}""").Should().BeNull();
    }

    [Fact]
    public void OpenCode_Rewrite_PrintsOnlyTheRewrittenCommand()
    {
        var reply = JsonNode.Parse(Reply(HookPayloadKind.OpenCode, """{"command":"dotnet build # répertoire","extra":1}""")!)!.AsObject();

        reply.Count.Should().Be(1, "only the command crosses the boundary");
        reply["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire");
    }

    [Theory]
    [InlineData("""{"command":"ls ~/dotnet-notes"}""")]
    [InlineData("""{"command":""}""")]
    [InlineData("""{"command":7}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"command":"a","command":"b"}""")]
    public void OpenCode_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.OpenCode, payload).Should().BeNull();
    }

    [Fact]
    public void Antigravity_Rewrite_AsksWithAnOverwriteOfOnlyTheCommandLine()
    {
        var reply = Reply(HookPayloadKind.AntigravityCli,
            """{"artifactDirectoryPath":"/home/u/.gemini/antigravity-cli/brain/72c8f41f-64fa-43fb-8f24-bc62834d372f","conversationId":"72c8f41f-64fa-43fb-8f24-bc62834d372f","modelName":"gemini-3.8-flash-high","stepIdx":2,"toolCall":{"args":{"CommandLine":"dotnet test","Cwd":"/tmp/ws","WaitMsBeforeAsync":5000,"toolAction":"Running command in terminal","toolSummary":"Run dotnet test"},"name":"run_command"}}""");

        var root = JsonNode.Parse(reply!)!.AsObject();
        root["decision"]!.GetValue<string>().Should().Be("ask", "allow would auto-approve a command the user's rules may not allow");
        root["overwrite"]!.AsObject().Count.Should().Be(1, "overwrite is a shallow merge into the tool arguments");
        root["overwrite"]!["CommandLine"]!.GetValue<string>().Should().Be("dtk dotnet test");
        root.ContainsKey("reason").Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":"ls"}}}""")]
    [InlineData("""{"toolCall":{"name":"view_file","args":{"CommandLine":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"args":{"CommandLine":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"command":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command"}}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":"a","CommandLine":"b"}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":""}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":7}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":"x"}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":[]}}""")]
    [InlineData("""{"toolCall":{"name":42,"args":{"CommandLine":"dotnet build"}}}""")]
    public void Antigravity_NothingToRewrite_PrintsTheNeutralReply(string payload)
    {
        Reply(HookPayloadKind.AntigravityCli, payload).Should().BeNull();
    }

    [Fact]
    public void Antigravity_NonAsciiCommand_RoundTripsExactly()
    {
        var reply = Reply(HookPayloadKind.AntigravityCli,
            """{"toolCall":{"name":"run_command","args":{"CommandLine":"dotnet build # répertoire ’ok’"}}}""");

        JsonNode.Parse(reply!)!["overwrite"]!["CommandLine"]!.GetValue<string>()
            .Should().Be("dtk dotnet build # répertoire ’ok’");
    }

    private static string? Reply(HookPayloadKind kind, string payload) =>
        HookPayloads.Reply(kind, Encoding.UTF8.GetBytes(payload));
}

using System.Text;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

/// <summary><c>dtk hook claude</c>'s answer to <c>SessionStart</c> and <c>SubagentStart</c>: dtk's rules, in a .NET project.</summary>
public sealed class ClaudeContextHookTests : IDisposable
{
    private readonly string _dotnetProject = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "dtk-context-" + Guid.NewGuid().ToString("N")));

    private readonly string _plainFolder = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "dtk-plain-" + Guid.NewGuid().ToString("N")));

    public ClaudeContextHookTests()
    {
        Directory.CreateDirectory(_dotnetProject);
        File.WriteAllText(Path.Combine(_dotnetProject, "App.csproj"), string.Empty);
        Directory.CreateDirectory(_plainFolder);
        File.WriteAllText(Path.Combine(_plainFolder, "README.md"), string.Empty);
    }

    public void Dispose()
    {
        Directory.Delete(_dotnetProject, true);
        Directory.Delete(_plainFolder, true);
    }

    [Theory]
    [InlineData("SessionStart")]
    [InlineData("SubagentStart")]
    public void Claude_ContextEventInADotnetProject_InjectsTheRules(string eventName)
    {
        var reply = Reply(Payload(eventName, _dotnetProject));

        var output = JsonNode.Parse(reply!)!["hookSpecificOutput"]!;
        output["hookEventName"]!.GetValue<string>().Should().Be(eventName);
        output["additionalContext"]!.GetValue<string>().Should().Be(IntegrationInstructions.AgentRules);
    }

    [Theory]
    [InlineData("SessionStart")]
    [InlineData("SubagentStart")]
    public void Claude_ContextEventOutsideADotnetProject_RepliesNothing(string eventName)
    {
        Reply(Payload(eventName, _plainFolder)).Should().BeNull();
    }

    [Theory]
    [InlineData("web-fetch")]
    [InlineData("statusline-setup")]
    public void Claude_SubagentWithoutAShell_GetsNoRules(string agentType)
    {
        Reply(Payload("SubagentStart", _dotnetProject, agentType)).Should().BeNull();
    }

    [Theory]
    [InlineData("general-purpose")]
    [InlineData("Explore")]
    [InlineData("Plan")]
    [InlineData("feature-dev:code-reviewer")]
    public void Claude_AnyOtherSubagent_GetsTheRules(string agentType)
    {
        Reply(Payload("SubagentStart", _dotnetProject, agentType)).Should().Contain("additionalContext");
    }

    [Fact]
    public void Claude_ContextEventWithoutAUsableCwd_RepliesNothing()
    {
        Reply("""{"hook_event_name":"SessionStart"}""").Should().BeNull();
        Reply("""{"hook_event_name":"SessionStart","cwd":42}""").Should().BeNull();
        Reply("""{"hook_event_name":"SubagentStart","agent_type":"Explore","cwd":"relative/dir"}""").Should().BeNull();
        Reply("""{"hook_event_name":"SubagentStart","agent_type":7,"cwd":null}""").Should().BeNull();
    }

    [Theory]
    [InlineData("sessionStart")]
    [InlineData("preToolUse")]
    [InlineData("PreToolUse")]
    public void Claude_OtherEventName_KeepsTodaysRewrite(string eventName)
    {
        var withCommand = new JsonObject
        {
            ["hook_event_name"] = eventName,
            ["cwd"] = _dotnetProject,
            ["tool_input"] = new JsonObject { ["command"] = "dotnet build" }
        }.ToJsonString();

        JsonNode.Parse(Reply(withCommand)!)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build");
        Reply(new JsonObject { ["hook_event_name"] = eventName, ["cwd"] = _dotnetProject }.ToJsonString())
            .Should().BeNull();
    }

    [Fact]
    public void Claude_ContextEventWithACommand_IsNotRewritten()
    {
        var payload = new JsonObject
        {
            ["hook_event_name"] = "SessionStart",
            ["cwd"] = _dotnetProject,
            ["tool_input"] = new JsonObject { ["command"] = "dotnet build" }
        }.ToJsonString();

        var output = JsonNode.Parse(Reply(payload)!)!["hookSpecificOutput"]!.AsObject();
        output.ContainsKey("updatedInput").Should().BeFalse();
        output["additionalContext"]!.GetValue<string>().Should().Be(IntegrationInstructions.AgentRules);
    }

    [Fact]
    public void Devin_ContextEventName_IsNotAnsweredWithTheRules()
    {
        // Only Claude Code's own hook answers the context events; Devin's keeps its rewrite-only reply.
        HookPayloads.Reply(HookPayloadKind.Devin, Encoding.UTF8.GetBytes(Payload("SessionStart", _dotnetProject)))
            .Should().BeNull();
    }

    private static string Payload(string eventName, string cwd, string? agentType = null)
    {
        var payload = new JsonObject
        {
            ["session_id"] = "s",
            ["hook_event_name"] = eventName,
            ["cwd"] = cwd
        };
        if (agentType is not null)
        {
            payload["agent_type"] = agentType;
            payload["agent_id"] = "a1";
        }
        else if (eventName == "SessionStart")
        {
            payload["source"] = "startup";
        }

        return payload.ToJsonString();
    }

    private static string? Reply(string payload) =>
        HookPayloads.Reply(HookPayloadKind.ClaudeCode, Encoding.UTF8.GetBytes(payload));
}

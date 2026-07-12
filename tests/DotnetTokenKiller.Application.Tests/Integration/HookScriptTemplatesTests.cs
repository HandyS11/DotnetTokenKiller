using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class HookScriptTemplatesTests
{
    [Fact]
    public void CopilotCliHook_ReusesSharedRewriteCore()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().Contain("def rewrite");
        script.Should().Contain("def main");
    }

    [Fact]
    public void CopilotCliHook_EmitsCopilotDecisionSchema()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().Contain("\"toolName\"");
        script.Should().Contain("\"toolArgs\"");
        script.Should().Contain("permissionDecision");
        script.Should().Contain("modifiedArgs");
    }

    [Fact]
    public void CopilotCliHook_DoesNotUseClaudeOrGeminiSchema()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().NotContain("updatedInput");
        script.Should().NotContain("hookSpecificOutput");
    }
}

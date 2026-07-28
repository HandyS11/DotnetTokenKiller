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

    [Fact]
    public void CopilotCliHook_GatesAutoApprovalOnSimpleCommands()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        // A compound command must be downgraded to "ask" rather than auto-approved,
        // so the hook never silently approves non-dotnet parts of a chained command.
        script.Should().Contain("def _is_simple_command");
        script.Should().Contain("\"allow\" if _is_simple_command(command) else \"ask\"");
    }

    [Fact]
    public void SharedCore_MatchesAnyWhitespaceBetweenSubcommandTokens()
    {
        // A tuple entry containing a literal space would match exactly one space, so
        // `dotnet list   package` would silently not be rewritten.
        HookScriptTemplates.ClaudeHook.Should().Contain("""s.replace(" ", r"\s+")""");
    }

    [Fact]
    public void SharedCore_OrdersTheAlternationLongestFirst()
    {
        // Python's alternation is first-match-wins, so a subcommand that prefixes a longer one
        // would shadow it.
        HookScriptTemplates.ClaudeHook.Should().Contain("key=len, reverse=True");
    }

    [Fact]
    public void AllThreeHooks_ShareTheIdenticalPatternConstruction()
    {
        foreach (var hook in new[]
                 {
                     HookScriptTemplates.ClaudeHook,
                     HookScriptTemplates.GeminiHook,
                     HookScriptTemplates.CopilotCliHook
                 })
        {
            hook.Should().Contain("""s.replace(" ", r"\s+")""");
        }
    }
}

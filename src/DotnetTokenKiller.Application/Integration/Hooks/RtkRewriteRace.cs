using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Claude Code runs every matching PreToolUse hook in parallel and applies only one hook's <c>updatedInput</c>, so when
/// rtk's hook and dtk's hook both rewrite a command, one rewrite is lost. This decides when dtk should refuse such a
/// command instead, naming the command that carries both rewrites.
/// </summary>
internal static class RtkRewriteRace
{
    /// <summary>The start of the refusal's reason; the merged command follows it.</summary>
    internal const string ReasonLead = "dtk and rtk would each rewrite this command, and Claude Code applies only one hook's rewrite. Run this instead, which carries both:\n";

    /// <summary>Works out the command that carries both rewrites, when refusing is warranted.</summary>
    /// <param name="command">The command as the agent wrote it.</param>
    /// <param name="dtkRewrite">What dtk would rewrite it to (equal to <paramref name="command"/> when it would not).</param>
    /// <param name="rewriteWithDtk">dtk's rewrite, to check that the merged command is stable under it.</param>
    /// <param name="rtk">Asks rtk how it would rewrite a command.</param>
    /// <returns>The merged command when a refusal is warranted, else null. Never throws on rtk's trouble.</returns>
    internal static string? MergedCommand(
        string command,
        string dtkRewrite,
        Func<string, string> rewriteWithDtk,
        IRtkRewriter rtk)
    {
        if (dtkRewrite == command)
        {
            return null;
        }

        var first = rtk.Rewrite(command);
        if (!Rewrites(first) || first!.Output == command)
        {
            return null;
        }

        var second = rtk.Rewrite(dtkRewrite);
        string merged;
        if (Rewrites(second))
        {
            merged = second!.Output;
        }
        else if (second is { ExitCode: 1 })
        {
            merged = dtkRewrite;
        }
        else
        {
            return null;
        }

        if (rewriteWithDtk(merged) != merged)
        {
            return null;
        }

        var third = rtk.Rewrite(merged);
        var stable = third is not null
            && (third.ExitCode is 1 or 2 || (third.ExitCode is 0 or 3 && third.Output == merged));
        return stable ? merged : null;
    }

    /// <summary>Claude Code's refusal, whose reason carries the merged command.</summary>
    /// <param name="merged">What <see cref="MergedCommand"/> returned.</param>
    internal static string DenyReply(string merged) =>
        new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = "deny",
                ["permissionDecisionReason"] = ReasonLead + merged
            }
        }.ToJsonString();

    /// <summary>Whether rtk answered with a rewrite (exit 0 or 3) that has a command.</summary>
    /// <param name="answer">rtk's answer, or null when it could not be asked.</param>
    private static bool Rewrites(RtkRewrite? answer) =>
        answer is { ExitCode: 0 or 3, Output.Length: > 0 };
}

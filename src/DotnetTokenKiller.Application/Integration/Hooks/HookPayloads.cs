using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>Turns a harness's pre-tool payload into the reply that makes it run <c>dtk dotnet …</c>.</summary>
/// <remarks>
/// The replies are the ones the Python hooks printed. Other fields of the tool input round-trip untouched.
/// An unexpected payload shape yields no rewrite rather than an exception, because a hook that fails must
/// never block the tool call. This also covers a payload with a duplicate JSON object key: <see cref="JsonNode"/>
/// builds a <see cref="JsonObject"/>'s property dictionary lazily, so parsing such a payload succeeds and the
/// <see cref="ArgumentException"/> only surfaces the first time something here indexes into the duplicated
/// object — <see cref="Reply"/> guards the per-provider dispatch, not just the initial parse, so that still
/// yields the provider's no-rewrite result instead of an unhandled exception.
/// </remarks>
internal static class HookPayloads
{
    /// <summary>
    /// Gemini CLI's reply when nothing about the payload calls for a rewrite. Unlike Copilot CLI's
    /// <c>permissionDecision: "allow"</c> (a real bypass, so <see cref="ReplyToCopilot"/> gates it on
    /// <see cref="DotnetCommandRewriter.IsAutoApprovable"/>), a <c>BeforeTool</c> hook's <c>"allow"</c> decision does
    /// not bypass Gemini CLI's own confirmation: the scheduler only ever special-cases a hook decision of
    /// <c>"ask"</c> (forced into <c>PolicyDecision.ASK_USER</c>) or <c>"deny"</c>/<c>"block"</c> (rejected before
    /// the policy check runs); anything else, including <c>"allow"</c>, is indistinguishable from no decision at
    /// all and leaves the call to the policy engine — the user's own trust rules and approval mode — exactly like
    /// <see cref="AntigravityNeutralReply"/> leaves Antigravity's. See
    /// github.com/google-gemini/gemini-cli packages/core/src/scheduler/hook-utils.ts (`evaluateBeforeToolHook`
    /// only maps `isAskDecision()` to `hookDecision = 'ask'`; nothing maps `"allow"`) and
    /// packages/core/src/scheduler/scheduler.ts (`_processToolCall`: `decision = policyDecision` unless
    /// `hookDecision === 'ask'`, which alone forces `PolicyDecision.ASK_USER`), confirmed against
    /// packages/core/src/confirmation-bus/message-bus.ts (a hook's `forcedDecision` can only ever be
    /// `'ask_user'`, and only an already-trusted bus honors it — "Remove forcedDecision to prevent policy
    /// bypass" guards the untrusted path). docs/hooks/reference.md's own `BeforeTool` section documents only
    /// `"deny"`/`"block"` as having an effect, which agrees.
    /// </summary>
    private const string GeminiAllowReply = """{"decision":"allow"}""";

    /// <summary>
    /// Antigravity CLI's reply when there is nothing to rewrite: nothing at all, which gate G1 showed leaves the call to
    /// the user's own permissions. Never <c>allow</c>, which would auto-approve the command.
    /// </summary>
    private const string? AntigravityNeutralReply = null;

    /// <summary>
    /// Cursor's reply when there is nothing to rewrite. Never empty: Cursor blocks the tool call when a permission
    /// hook's output does not match its schema (cursor.com/docs/hooks.md), and <c>{}</c> is what rtk's Cursor hook
    /// prints for the same case.
    /// </summary>
    internal const string CursorNeutralReply = "{}";

    /// <summary>Cursor's shell tool.</summary>
    private const string CursorShellTool = "Shell";

    /// <summary>Devin's shell tool.</summary>
    private const string DevinShellTool = "exec";

    /// <summary>Factory Droid's shell tool.</summary>
    private const string DroidShellTool = "Execute";

    /// <summary>Crush's shell tool.</summary>
    private const string CrushShellTool = "bash";

    /// <summary>The payload property naming the tool in Claude-shaped payloads (Claude Code, Codex CLI, Cursor, Devin).</summary>
    private const string ToolNameProperty = "tool_name";

    /// <summary>The payload property holding Claude Code's, Gemini CLI's and Codex CLI's tool input.</summary>
    private const string ToolInputProperty = "tool_input";

    /// <summary>The tool input property holding the shell command.</summary>
    private const string CommandProperty = "command";

    /// <summary>The permission value that auto-approves a rewritten command, in every harness that has one.</summary>
    private const string AllowDecision = "allow";

    /// <summary>
    /// Resolves a provider name (<c>claude</c>, <c>gemini</c>, <c>copilot-cli</c>, <c>codex</c>, <c>opencode</c>,
    /// <c>antigravity</c>, <c>pi</c>, <c>oh-my-pi</c>, <c>cursor</c>, <c>devin</c>, <c>droid</c>, <c>crush</c>, <c>kilo</c>, <c>amp</c>) to its payload shape.
    /// </summary>
    /// <param name="provider">The name passed to <c>dtk hook</c>.</param>
    /// <param name="kind">The payload shape, when the name is known.</param>
    internal static bool TryGetKind(string provider, out HookPayloadKind kind)
    {
        (var known, kind) = provider switch
        {
            "claude" => (true, HookPayloadKind.ClaudeCode),
            "gemini" => (true, HookPayloadKind.GeminiCli),
            "copilot-cli" => (true, HookPayloadKind.CopilotCli),
            "codex" => (true, HookPayloadKind.CodexCli),
            "opencode" => (true, HookPayloadKind.OpenCode),
            "antigravity" => (true, HookPayloadKind.AntigravityCli),
            "pi" => (true, HookPayloadKind.Pi),
            "oh-my-pi" => (true, HookPayloadKind.OhMyPi),
            "cursor" => (true, HookPayloadKind.Cursor),
            "devin" => (true, HookPayloadKind.Devin),
            "droid" => (true, HookPayloadKind.FactoryDroid),
            "crush" => (true, HookPayloadKind.Crush),
            "kilo" => (true, HookPayloadKind.Kilo),
            "amp" => (true, HookPayloadKind.Amp),
            _ => (false, default)
        };
        return known;
    }

    /// <summary>Builds the reply for one payload.</summary>
    /// <param name="kind">The harness that sent the payload.</param>
    /// <param name="payload">The UTF-8 payload, with or without a byte order mark.</param>
    /// <returns>The JSON to print, or <see langword="null"/> when the hook should print nothing.</returns>
    internal static string? Reply(HookPayloadKind kind, ReadOnlySpan<byte> payload)
    {
        if (payload.StartsWith("\uFEFF"u8))
        {
            payload = payload[3..];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return kind == HookPayloadKind.Cursor ? CursorNeutralReply : null;
        }

        try
        {
            return kind switch
            {
                HookPayloadKind.ClaudeCode => ReplyWithUpdatedInput(root, expectedTool: null),
                HookPayloadKind.Devin => ReplyWithUpdatedInput(root, DevinShellTool),
                HookPayloadKind.FactoryDroid => ReplyWithUpdatedInput(root, DroidShellTool),
                HookPayloadKind.Cursor => ReplyToCursor(root),
                HookPayloadKind.GeminiCli => ReplyToGemini(root),
                HookPayloadKind.CopilotCli => ReplyToCopilot(root),
                HookPayloadKind.CodexCli => ReplyToCodex(root),
                HookPayloadKind.OpenCode or HookPayloadKind.Pi or HookPayloadKind.OhMyPi or HookPayloadKind.Kilo or HookPayloadKind.Amp => ReplyToOpenCode(root),
                HookPayloadKind.AntigravityCli => ReplyToAntigravity(root),
                HookPayloadKind.Crush => ReplyToCrush(root),
                _ => null
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            // A duplicate key in a JSON object (e.g. two "command" properties) parses without error —
            // JsonObject builds its property dictionary lazily — and only throws the first time something
            // above indexes into that object. Treat it the same as any other unexpected shape: the no-rewrite
            // result for this provider, never an unhandled exception.
            return kind switch
            {
                HookPayloadKind.GeminiCli => GeminiAllowReply,
                HookPayloadKind.Cursor => CursorNeutralReply,
                _ => null
            };
        }
    }

    /// <summary>
    /// Claude Code's reply, which Devin Local and Devin CLI share: the whole tool input with the command replaced, and
    /// no permission decision, so the harness still applies its own approval to the rewritten command.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    /// <param name="expectedTool">
    /// The harness's shell tool, or <see langword="null"/> to skip the check (Claude Code's matcher already selects
    /// <c>Bash</c>). A payload without <c>tool_name</c> — doctor's probe — always passes.
    /// </param>
    private static string? ReplyWithUpdatedInput(JsonNode? root, string? expectedTool)
    {
        if (root is not JsonObject payload
            || (expectedTool is not null && NamesAnotherTool(payload, ToolNameProperty, expectedTool))
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput[CommandProperty] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    /// <summary>
    /// Replies to Cursor's <c>preToolUse</c>. Cursor enforces only <c>allow</c> and <c>deny</c> there, and a rewrite
    /// must carry <c>allow</c>, which may also skip Cursor's own approval; so, like Copilot CLI's reply, dtk rewrites
    /// only commands <see cref="DotnetCommandRewriter.IsAutoApprovable"/> accepts and leaves every other command to
    /// the agent, answering <see cref="CursorNeutralReply"/>.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string ReplyToCursor(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, ToolNameProperty, CursorShellTool)
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out var command, out var rewritten)
            || !DotnetCommandRewriter.IsAutoApprovable(command))
        {
            return CursorNeutralReply;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput[CommandProperty] = rewritten;
        return new JsonObject { ["permission"] = AllowDecision, ["updated_input"] = updatedInput }.ToJsonString();
    }

    /// <summary>
    /// Replies to Crush's <c>PreToolUse</c> in Crush's own envelope. <c>updated_input</c> is a shallow-merge patch, so
    /// only the command is sent; there is never a <c>decision</c>, because Crush's <c>allow</c> bypasses its permission
    /// prompt entirely and dtk leaves that decision to the user.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string? ReplyToCrush(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, ToolNameProperty, CrushShellTool)
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        return new JsonObject
        {
            ["version"] = 1,
            ["updated_input"] = new JsonObject { [CommandProperty] = rewritten }
        }.ToJsonString();
    }

    private static string ReplyToGemini(JsonNode? root)
    {
        var reply = new JsonObject { ["decision"] = AllowDecision };
        if (root is JsonObject payload
            && payload[ToolInputProperty] is JsonObject toolInput
            && TryRewrite(toolInput, out _, out var rewritten))
        {
            reply["hookSpecificOutput"] = new JsonObject
            {
                [ToolInputProperty] = new JsonObject { [CommandProperty] = rewritten }
            };
        }

        return reply.ToJsonString();
    }

    private static string? ReplyToCopilot(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["toolName"] is not JsonValue toolName
            || !toolName.TryGetValue<string>(out var name)
            || name != "bash")
        {
            return null;
        }

        var toolArgs = payload["toolArgs"] switch
        {
            JsonObject obj => (JsonObject)obj.DeepClone(),
            JsonValue value when value.TryGetValue<string>(out var text) => ParseObject(text),
            _ => null
        };

        if (toolArgs is null || !TryRewrite(toolArgs, out var command, out var rewritten))
        {
            return null;
        }

        toolArgs[CommandProperty] = rewritten;
        return new JsonObject
        {
            ["permissionDecision"] = DotnetCommandRewriter.IsAutoApprovable(command) ? AllowDecision : "ask",
            ["modifiedArgs"] = toolArgs
        }.ToJsonString();
    }

    private static string? ReplyToCodex(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, ToolNameProperty, "Bash")
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput[CommandProperty] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                // Codex rejects updatedInput unless the reply also allows; it still applies its approval policy and
                // sandbox to the rewritten command.
                ["permissionDecision"] = AllowDecision,
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    /// <summary>
    /// Replies to dtk's generated OpenCode plugin, pi-family extension, Kilo Code plugin, and Amp plugin. The contract
    /// is dtk's own, because dtk writes both ends: only the command crosses the process boundary.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string? ReplyToOpenCode(JsonNode? root)
    {
        if (root is not JsonObject payload || !TryRewrite(payload, out _, out var rewritten))
        {
            return null;
        }

        return new JsonObject { [CommandProperty] = rewritten }.ToJsonString();
    }

    /// <summary>
    /// Replies to Google Antigravity CLI's <c>run_command</c> tool call with an <c>ask</c> decision and a shallow
    /// overwrite of just <c>CommandLine</c>: gate G1 showed an empty reply leaves the call to the user's own
    /// permissions, but Antigravity has no analogue of Gemini's "allow with an updated command", so <c>ask</c> is the
    /// closest verb that still lets the rewrite through without auto-approving a command the user's rules may not.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string? ReplyToAntigravity(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["toolCall"] is not JsonObject toolCall
            || toolCall["name"] is not JsonValue name || !name.TryGetValue<string>(out var toolName) || toolName != "run_command"
            || toolCall["args"] is not JsonObject arguments
            || !TryRewrite(arguments, "CommandLine", out _, out var rewritten))
        {
            return AntigravityNeutralReply;
        }

        return new JsonObject
        {
            // "ask" defers to the user's permission rules and "Always Allow" choices; "allow" would auto-approve.
            ["decision"] = "ask",
            ["overwrite"] = new JsonObject { ["CommandLine"] = rewritten }
        }.ToJsonString();
    }

    /// <summary>
    /// Whether <paramref name="payload"/> names a tool other than <paramref name="expected"/> under <paramref name="key"/>.
    /// A payload without the key names none, which is what doctor's probe sends.
    /// </summary>
    /// <param name="payload">The hook payload.</param>
    /// <param name="key">The property holding the tool name.</param>
    /// <param name="expected">The shell tool's name.</param>
    private static bool NamesAnotherTool(JsonObject payload, string key, string expected) =>
        payload[key] is not null
        && (payload[key] is not JsonValue value || !value.TryGetValue<string>(out var name) || name != expected);

    private static bool TryRewrite(JsonObject arguments, out string command, out string rewritten) =>
        TryRewrite(arguments, CommandProperty, out command, out rewritten);

    private static bool TryRewrite(JsonObject arguments, string key, out string command, out string rewritten)
    {
        command = string.Empty;
        rewritten = string.Empty;
        if (arguments[key] is not JsonValue value || !value.TryGetValue(out string? text) || text.Length == 0)
        {
            return false;
        }

        command = text;
        rewritten = DotnetCommandRewriter.Rewrite(text);
        return !ReferenceEquals(rewritten, text);
    }

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

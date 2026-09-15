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
    /// <summary>Gemini CLI's reply when nothing about the payload calls for a rewrite.</summary>
    private const string GeminiAllowReply = """{"decision":"allow"}""";

    /// <summary>Resolves a provider name (<c>claude</c>, <c>gemini</c>, <c>copilot-cli</c>, <c>codex</c>) to its payload shape.</summary>
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
            return null;
        }

        try
        {
            return kind switch
            {
                HookPayloadKind.ClaudeCode => ReplyToClaude(root),
                HookPayloadKind.GeminiCli => ReplyToGemini(root),
                HookPayloadKind.CopilotCli => ReplyToCopilot(root),
                HookPayloadKind.CodexCli => ReplyToCodex(root),
                _ => null
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            // A duplicate key in a JSON object (e.g. two "command" properties) parses without error —
            // JsonObject builds its property dictionary lazily — and only throws the first time something
            // above indexes into that object. Treat it the same as any other unexpected shape: the no-rewrite
            // result for this provider, never an unhandled exception.
            return kind == HookPayloadKind.GeminiCli ? GeminiAllowReply : null;
        }
    }

    private static string? ReplyToClaude(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["tool_input"] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput["command"] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    private static string ReplyToGemini(JsonNode? root)
    {
        var reply = new JsonObject { ["decision"] = "allow" };
        if (root is JsonObject payload
            && payload["tool_input"] is JsonObject toolInput
            && TryRewrite(toolInput, out _, out var rewritten))
        {
            reply["hookSpecificOutput"] = new JsonObject
            {
                ["tool_input"] = new JsonObject { ["command"] = rewritten }
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

        toolArgs["command"] = rewritten;
        return new JsonObject
        {
            ["permissionDecision"] = DotnetCommandRewriter.IsSimpleCommand(command) ? "allow" : "ask",
            ["modifiedArgs"] = toolArgs
        }.ToJsonString();
    }

    private static string? ReplyToCodex(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, "tool_name", "Bash")
            || payload["tool_input"] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput["command"] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                // Codex rejects updatedInput unless the reply also allows; it still applies its approval policy and
                // sandbox to the rewritten command.
                ["permissionDecision"] = "allow",
                ["updatedInput"] = updatedInput
            }
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

    private static bool TryRewrite(JsonObject arguments, out string command, out string rewritten)
    {
        command = string.Empty;
        rewritten = string.Empty;
        if (arguments["command"] is not JsonValue value || !value.TryGetValue(out string? text) || text.Length == 0)
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

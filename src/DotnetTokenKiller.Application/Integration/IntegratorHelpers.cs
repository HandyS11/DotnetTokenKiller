using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

internal sealed record HookSpec(
    string ScriptPath,
    string Script,
    string SettingsPath,
    string EventKey,
    string Matcher,
    string Command);

internal static class IntegratorHelpers
{
    private const string HooksKey = "hooks";

    internal static async Task WriteFileAsync(
        string path,
        string content,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (ShouldSkipWrite(exists, context.Force))
        {
            context.Skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? context.Updated : context.Created).Add(path);
    }

    /// <summary>
    /// Decides whether a write to an existing file should be skipped: <see langword="true"/>
    /// whenever <paramref name="fileExists"/> and <paramref name="force"/> is <see langword="false"/>.
    /// This is the single source of truth for "existing file, no --force ⇒ leave it alone",
    /// consumed by <see cref="WriteFileAsync"/>, <see cref="WriteSectionBasedFileAsync"/>, and
    /// <see cref="AiderIntegrator"/>'s merge into an existing external <c>read:</c> key — so those
    /// decisions can never drift apart.
    /// </summary>
    /// <param name="fileExists">Whether the target file already exists.</param>
    /// <param name="force">Whether the integration is running with the force flag.</param>
    internal static bool ShouldSkipWrite(bool fileExists, bool force) => fileExists && !force;

    /// <summary>
    /// Writes a file that uses begin/end section markers to track a dtk-managed block.
    /// If the file already exists — whether or not it contains the section marker — and
    /// <c>context.Force</c> is <see langword="false"/>: skips, leaving the file completely
    /// untouched (matching <see cref="WriteFileAsync"/>'s contract).
    /// If the file already exists and <c>context.Force</c> is <see langword="true"/>: replaces the
    /// dtk-managed span when the marker is present, or appends the section when it is not.
    /// If the file does not exist: creates it with the section as the only content.
    /// </summary>
    /// <param name="path">Path to the target file.</param>
    /// <param name="sectionMarker">String that marks the beginning of the dtk-managed block.</param>
    /// <param name="sectionEndMarker">String that marks the end of the dtk-managed block.</param>
    /// <param name="section">Full text of the dtk-managed block to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteSectionBasedFileAsync(
        string path,
        string sectionMarker,
        string sectionEndMarker,
        string section,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (ShouldSkipWrite(exists, context.Force))
        {
            context.Skipped.Add(path);
            return;
        }

        if (!exists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, section, cancellationToken).ConfigureAwait(false);
            context.Created.Add(path);
            return;
        }

        var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var updated = current.Contains(sectionMarker, StringComparison.Ordinal)
            ? ReplaceDtkSection(current, sectionMarker, sectionEndMarker, section)
            : AppendSection(current, section);

        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    private static string AppendSection(string current, string section)
    {
        var trimmed = current.TrimEnd();
        return string.IsNullOrWhiteSpace(trimmed)
            ? section
            : trimmed + Environment.NewLine + section;
    }

    private static string ReplaceDtkSection(string content, string marker, string endMarker, string section)
    {
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            // The begin marker is present but the end marker is missing (e.g. a user accidentally
            // deleted just the "<!-- /dtk -->" line). The dtk-managed span can no longer be
            // reliably identified, so don't guess how far it extends: insert the fresh section in
            // place of the begin marker and preserve everything that followed it (stale dtk body
            // text and/or real user content) immediately after, rather than deleting it.
            return content[..start] + section + content[(start + marker.Length)..];
        }

        return content[..start] + section + content[(end + endMarker.Length)..];
    }

    /// <summary>
    /// Writes the hook script file and merges the hook registration into the provider's settings JSON.
    /// </summary>
    /// <param name="spec">Hook installation specification.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteHookAndSettingsAsync(
        HookSpec spec,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        await WriteFileAsync(spec.ScriptPath, spec.Script, context, cancellationToken).ConfigureAwait(false);

        await MergeJsonSettingsAsync(
            spec.SettingsPath,
            spec.EventKey,
            new JsonObject
            {
                ["matcher"] = spec.Matcher,
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = spec.Command
                    }
                }
            },
            spec.Command,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges a hook entry into a JSON settings file under
    /// <c>hooks[<paramref name="hookEventKey"/>]</c>.
    /// Existing content is preserved; the entry is only added if not already present
    /// (detected by matching <paramref name="hookCommand"/> in the "command" field).
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="hookEventKey">Key of the hook event array within the hooks object (e.g. "PreToolUse").</param>
    /// <param name="hookEntry">The JSON object to append to the hook event array.</param>
    /// <param name="hookCommand">The command string used to detect whether the hook is already registered.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The settings file contains invalid JSON or an unexpected root type.</exception>
    internal static async Task MergeJsonSettingsAsync(
        string path,
        string hookEventKey,
        JsonObject hookEntry,
        string hookCommand,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);
        JsonObject root;

        if (exists)
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse JSON settings file '{path}'. The file must contain a valid JSON object at the root.",
                    ex);
            }

            if (parsed is JsonObject obj)
            {
                root = obj;
            }
            else
            {
                var actualType = parsed?.GetType().Name ?? "null";
                throw new InvalidOperationException(
                    $"The settings file '{path}' must contain a JSON object at the root, but found '{actualType}'.");
            }
        }
        else
        {
            root = [];
        }

        root.TryGetPropertyValue(HooksKey, out var hooksNode);
        var hooks = hooksNode switch
        {
            null => [],
            JsonObject hooksObj => hooksObj,
            _ => throw new InvalidOperationException(
                $"The settings file '{path}' has a '{HooksKey}' property of unexpected type '{hooksNode.GetType().Name}'; expected a JSON object.")
        };

        hooks.TryGetPropertyValue(hookEventKey, out var eventNode);
        var hookArray = eventNode switch
        {
            null => [],
            JsonArray arr => arr,
            _ => throw new InvalidOperationException(
                $"The settings file '{path}' has a '{HooksKey}.{hookEventKey}' property of unexpected type '{eventNode.GetType().Name}'; expected a JSON array.")
        };

        if (IsHookAlreadyRegistered(hookArray, hookCommand))
        {
            context.Skipped.Add(path);
            return;
        }

        hookArray.Add(hookEntry);
        hooks[hookEventKey] = hookArray;
        root[HooksKey] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }),
            cancellationToken).ConfigureAwait(false);

        (exists ? context.Updated : context.Created).Add(path);
    }

    private static bool IsHookAlreadyRegistered(JsonArray hookArray, string hookCommand)
    {
        foreach (var item in hookArray)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            entry.TryGetPropertyValue(HooksKey, out var innerHooksNode);
            if (innerHooksNode is not JsonArray innerHooks)
            {
                continue;
            }

            foreach (var inner in innerHooks)
            {
                if (inner is JsonObject innerEntry &&
                    innerEntry["command"]?.GetValue<string>() == hookCommand)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

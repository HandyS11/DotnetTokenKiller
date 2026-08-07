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
    /// Substring present in every generation of the Python hooks, used to recognize an unstamped
    /// copy installed by dtk 0.6.0 or earlier.
    /// </summary>
    internal const string HookLegacySignature = "_DTK_SUBCOMMANDS";

    /// <summary>
    /// Writes an artifact dtk generates in full, refreshing it when dtk can prove it wrote the
    /// copy already on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule, in order: a missing file is created; a file already equal to the stamped current
    /// template is reported unchanged; a file whose stamp still verifies came from an older dtk
    /// and is refreshed; an unstamped file carrying
    /// <see cref="GeneratedArtifact.LegacySignature"/> predates stamping and is refreshed with a
    /// note; anything else is left alone unless <c>--force</c> is passed.
    /// </para>
    /// <para>
    /// The legacy branch exists because no artifact installed before stamping carries a stamp, so
    /// without it every existing user would fall through to the skip branch and the refresh would
    /// only begin working one release after the one that adds it. It costs a one-time overwrite
    /// for anyone who hand-edited an unstamped hook, which is why the overwrite is reported rather
    /// than silent, and it becomes unreachable once one stamped generation is installed.
    /// </para>
    /// </remarks>
    /// <param name="artifact">The artifact to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteGeneratedFileAsync(
        GeneratedArtifact artifact,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var content = ArtifactStamping.Apply(artifact.Body, artifact.Style);

        if (!File.Exists(artifact.Path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(artifact.Path)!);
            await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
            context.Created.Add(artifact.Path);
            return;
        }

        var existing = (await File.ReadAllTextAsync(artifact.Path, cancellationToken).ConfigureAwait(false))
            .ReplaceLineEndings("\n");

        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            context.Unchanged.Add(artifact.Path);
            return;
        }

        var isLegacy = !ArtifactStamping.TryParse(existing, out _, out _)
                       && existing.Contains(artifact.LegacySignature, StringComparison.Ordinal);

        if (!ArtifactStamping.IsAuthentic(existing) && !isLegacy && !context.Force)
        {
            context.Skipped.Add(artifact.Path);
            return;
        }

        await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(artifact.Path);

        if (isLegacy)
        {
            context.Notes.Add(
                $"{artifact.Path} was written by an older dtk and carried no provenance stamp; it was "
                + "regenerated. Any local edits to it are recoverable from version control.");
        }
    }

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
            // Explicit '\n', not Environment.NewLine, so the written section is byte-identical across
            // platforms and never introduces a CR into an otherwise LF file.
            : trimmed + "\n" + section;
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
        await WriteGeneratedFileAsync(
            new GeneratedArtifact(spec.ScriptPath, spec.Script, StampStyle.HashComment, HookLegacySignature),
            context,
            cancellationToken).ConfigureAwait(false);

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
    /// (detected by matching <paramref name="hookCommand"/> in the "command" field). When the
    /// entry is already present and no legacy entry needs replacing, nothing is written and the
    /// path is reported <see cref="IntegrationContext.Unchanged"/> — this branch is
    /// force-independent (there is nothing to write and <c>--force</c> would not change that), so
    /// it must never be reported as a <see cref="IntegrationContext.Skipped"/> file, which implies
    /// re-running with <c>--force</c> would help.
    /// If an entry carrying the pre-<c>$..._PROJECT_DIR</c> relative form of
    /// <paramref name="hookCommand"/> is found (see <see cref="DeriveLegacyCommand"/>), that stale
    /// entry is replaced in place instead of appending a duplicate alongside it — otherwise a
    /// project integrated before the hook command was rooted at an env var would keep the old,
    /// broken entry registered forever, even across repeated <c>--force</c> runs.
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
        var root = await ReadRootObjectAsync(path, exists, cancellationToken).ConfigureAwait(false);

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

        var newAlreadyRegistered = FindRegisteredCommandEntry(hookArray, hookCommand) is not null;
        var legacyCommand = DeriveLegacyCommand(hookCommand);
        var legacyEntry = legacyCommand is null ? null : FindRegisteredCommandEntry(hookArray, legacyCommand);

        if (newAlreadyRegistered)
        {
            if (legacyEntry is null)
            {
                context.Unchanged.Add(path);
                return;
            }

            // Both the new command and a stale legacy relative command are registered (possible when
            // an in-between build appended the new one alongside the old). Drop the legacy duplicate
            // so the broken relative entry can't keep firing, leaving exactly one registration.
            RemoveRegisteredCommandEntry(hookArray, legacyCommand!);
        }
        else if (legacyEntry is not null)
        {
            legacyEntry["command"] = hookCommand;
        }
        else
        {
            hookArray.Add(hookEntry);
        }

        hooks[hookEventKey] = hookArray;
        root[HooksKey] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            // WriteIndented emits '\r\n' on Windows; normalize so the written JSON is identical everywhere.
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }).ReplaceLineEndings("\n"),
            cancellationToken).ConfigureAwait(false);

        (exists ? context.Updated : context.Created).Add(path);
    }

    /// <summary>
    /// Reads and parses the settings file into its root <see cref="JsonObject"/>, or returns an empty
    /// object when the file does not exist. Throws <see cref="InvalidOperationException"/> when the file
    /// contains invalid JSON or a non-object root.
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="exists">Whether the file already exists on disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the file contains invalid JSON or the root is not a JSON object.</exception>
    private static async Task<JsonObject> ReadRootObjectAsync(
        string path,
        bool exists,
        CancellationToken cancellationToken)
    {
        if (!exists)
        {
            return [];
        }

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
            return obj;
        }

        var actualType = parsed?.GetType().Name ?? "null";
        throw new InvalidOperationException(
            $"The settings file '{path}' must contain a JSON object at the root, but found '{actualType}'.");
    }

    /// <summary>Finds the inner hook object whose <c>"command"</c> field equals <paramref name="command"/>, if any.</summary>
    /// <param name="hookArray">The hook event array (e.g. <c>hooks.PreToolUse</c>) to search.</param>
    /// <param name="command">The command string to match.</param>
    private static JsonObject? FindRegisteredCommandEntry(JsonArray hookArray, string command)
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
                    innerEntry["command"]?.GetValue<string>() == command)
                {
                    return innerEntry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Removes every registered hook whose <c>"command"</c> equals <paramref name="command"/>,
    /// dropping any outer entry whose inner <c>hooks</c> list becomes empty as a result.
    /// </summary>
    /// <param name="hookArray">The hook event array (e.g. <c>hooks.PreToolUse</c>) to prune.</param>
    /// <param name="command">The command string whose registrations should be removed.</param>
    private static void RemoveRegisteredCommandEntry(JsonArray hookArray, string command)
    {
        for (var outer = hookArray.Count - 1; outer >= 0; outer--)
        {
            if (hookArray[outer] is not JsonObject entry)
            {
                continue;
            }

            entry.TryGetPropertyValue(HooksKey, out var innerHooksNode);
            if (innerHooksNode is not JsonArray innerHooks)
            {
                continue;
            }

            for (var inner = innerHooks.Count - 1; inner >= 0; inner--)
            {
                if (innerHooks[inner] is JsonObject innerEntry &&
                    innerEntry["command"]?.GetValue<string>() == command)
                {
                    innerHooks.RemoveAt(inner);
                }
            }

            if (innerHooks.Count == 0)
            {
                hookArray.RemoveAt(outer);
            }
        }
    }

    /// <summary>
    /// Derives the pre-<c>$..._PROJECT_DIR</c> relative form of a hook command shaped as
    /// <c>&lt;prefix&gt;"$XXX_PROJECT_DIR"/&lt;relative path&gt;</c> (e.g.
    /// <c>python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py</c> becomes
    /// <c>python3 .claude/hooks/dotnet-to-dtk.py</c>), so a settings file carrying the old, broken
    /// relative command can be recognized and replaced regardless of which provider's env var
    /// prefixes the current command. Derived structurally from <paramref name="hookCommand"/>
    /// rather than a hardcoded per-provider lookup, so it keeps working for any future provider
    /// that adopts the same env-var-rooting convention.
    /// </summary>
    /// <param name="hookCommand">The current, env-var-rooted hook command.</param>
    /// <returns>The legacy relative command, or <see langword="null"/> when <paramref name="hookCommand"/> doesn't follow that shape.</returns>
    private static string? DeriveLegacyCommand(string hookCommand)
    {
        var quoteStart = hookCommand.IndexOf("\"$", StringComparison.Ordinal);
        if (quoteStart < 0)
        {
            return null;
        }

        var quoteEnd = hookCommand.IndexOf('"', quoteStart + 1);
        if (quoteEnd < 0 || quoteEnd + 1 >= hookCommand.Length || hookCommand[quoteEnd + 1] != '/')
        {
            return null;
        }

        // Drop the opening quote/env-var/closing quote and the leading '/' of the relative path so
        // the two prefix/suffix halves join into the legacy bare-relative form.
        return hookCommand[..quoteStart] + hookCommand[(quoteEnd + 2)..];
    }
}

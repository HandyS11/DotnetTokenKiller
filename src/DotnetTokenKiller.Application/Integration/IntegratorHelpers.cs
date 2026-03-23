using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

internal static class IntegratorHelpers
{
    private const string HooksKey = "hooks";

    internal static async Task WriteFileAsync(
        string path,
        string content,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists && !force)
        {
            skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? updated : created).Add(path);
    }

    /// <summary>
    /// Writes a file that uses begin/end section markers to track a dtk-managed block.
    /// If the file already contains the section marker: replaces when <paramref name="force"/> is <see langword="true"/>, skips otherwise.
    /// If the file exists but has no marker: appends the section.
    /// If the file does not exist: creates it with the section as the only content.
    /// </summary>
    /// <param name="path">Path to the target file.</param>
    /// <param name="sectionMarker">String that marks the beginning of the dtk-managed block.</param>
    /// <param name="sectionEndMarker">String that marks the end of the dtk-managed block.</param>
    /// <param name="section">Full text of the dtk-managed block to write.</param>
    /// <param name="force">When <see langword="true"/>, replaces an existing section.</param>
    /// <param name="created">Accumulator for newly created file paths.</param>
    /// <param name="updated">Accumulator for updated file paths.</param>
    /// <param name="skipped">Accumulator for skipped file paths.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteSectionBasedFileAsync(
        string path,
        string sectionMarker,
        string sectionEndMarker,
        string section,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists)
        {
            var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (current.Contains(sectionMarker, StringComparison.Ordinal))
            {
                if (!force)
                {
                    skipped.Add(path);
                    return;
                }

                var replaced = ReplaceDtkSection(current, sectionMarker, sectionEndMarker, section);
                await File.WriteAllTextAsync(path, replaced, cancellationToken).ConfigureAwait(false);
                updated.Add(path);
                return;
            }

            var trimmed = current.TrimEnd();
            var appended = string.IsNullOrWhiteSpace(trimmed)
                ? section
                : trimmed + Environment.NewLine + section;
            await File.WriteAllTextAsync(path, appended, cancellationToken).ConfigureAwait(false);
            updated.Add(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, section, cancellationToken).ConfigureAwait(false);
            created.Add(path);
        }
    }

    private static string ReplaceDtkSection(string content, string marker, string endMarker, string section)
    {
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            return content[..start] + section;
        }

        return content[..start] + section + content[(end + endMarker.Length)..];
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
    /// <param name="created">Accumulator for newly created file paths.</param>
    /// <param name="updated">Accumulator for updated file paths.</param>
    /// <param name="skipped">Accumulator for skipped file paths.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The settings file contains invalid JSON or an unexpected root type.</exception>
    internal static async Task MergeJsonSettingsAsync(
        string path,
        string hookEventKey,
        JsonObject hookEntry,
        string hookCommand,
        List<string> created,
        List<string> updated,
        List<string> skipped,
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
        var hooks = hooksNode as JsonObject ?? [];

        hooks.TryGetPropertyValue(hookEventKey, out var eventNode);
        var hookArray = eventNode as JsonArray ?? [];

        if (IsHookAlreadyRegistered(hookArray, hookCommand))
        {
            skipped.Add(path);
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

        (exists ? updated : created).Add(path);
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

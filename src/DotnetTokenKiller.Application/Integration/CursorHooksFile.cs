using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Reads and writes dtk's entry in Cursor's <c>hooks.json</c>:
/// <c>{"version":1,"hooks":{"preToolUse":[{"command":"dtk hook cursor","matcher":"Shell","timeout":10}]}}</c>.
/// </summary>
/// <remarks>
/// Cursor's entries are flat — one object per hook carrying its own <c>command</c> — unlike the matcher groups of
/// Claude Code's settings, so <see cref="IntegratorHelpers.WriteHookRegistrationAsync"/> does not apply. dtk's entry
/// is any entry whose command is exactly dtk's; every other entry, rtk's included, and every other event is kept.
/// </remarks>
internal static class CursorHooksFile
{
    /// <summary>Seconds Cursor waits for the hook.</summary>
    internal const int TimeoutSeconds = 10;

    /// <summary>The matcher selecting Cursor's shell tool.</summary>
    internal const string ShellMatcher = "Shell";

    private const string EventKey = "preToolUse";
    private const string HooksKey = "hooks";
    private const string VersionKey = "version";
    private const string CommandKey = "command";

    /// <summary>Adds dtk's entry, keeping everything else; reports the file unchanged when it is already there.</summary>
    /// <param name="path">The <c>hooks.json</c> to merge into.</param>
    /// <param name="command">The command dtk registers.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file is not JSON, or <c>hooks</c>/<c>hooks.preToolUse</c> has an unexpected type.</exception>
    internal static async Task WriteAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);
        var root = await IntegratorHelpers.ReadRootObjectAsync(path, exists, cancellationToken).ConfigureAwait(false);
        var hookArray = EventArray(root, path, create: true)!;
        var matches = Matches(hookArray, command);

        if (matches.Count == 1 && root[VersionKey] is not null)
        {
            context.Unchanged.Add(path);
            return;
        }

        if (matches.Count == 0)
        {
            // The JsonNode overload: Add<JsonObject> is neither trim- nor AOT-safe.
            hookArray.Add((JsonNode)new JsonObject
            {
                [CommandKey] = command,
                ["matcher"] = ShellMatcher,
                ["timeout"] = TimeoutSeconds
            });
        }

        foreach (var duplicate in matches.Skip(1))
        {
            hookArray.Remove(duplicate);
        }

        root[VersionKey] ??= 1;

        await IntegratorHelpers.WriteSettingsJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
        (exists ? context.Updated : context.Created).Add(path);
    }

    /// <summary>
    /// Removes dtk's entry; drops the event and <c>hooks</c> when that empties them, and deletes the file when nothing
    /// but <c>version</c> is left.
    /// </summary>
    /// <param name="path">The <c>hooks.json</c> to remove dtk's entry from.</param>
    /// <param name="command">The command dtk registers.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file is not JSON, or has an unexpected shape.</exception>
    internal static async Task RemoveAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var root = await IntegratorHelpers.ReadRootObjectAsync(path, exists: true, cancellationToken).ConfigureAwait(false);
        if (EventArray(root, path, create: false) is not { } hookArray || Matches(hookArray, command) is not { Count: > 0 } matches)
        {
            context.Unchanged.Add(path);
            return;
        }

        foreach (var match in matches)
        {
            hookArray.Remove(match);
        }

        var hooks = (JsonObject)root[HooksKey]!;
        if (hookArray.Count == 0)
        {
            hooks.Remove(EventKey);
        }

        if (hooks.Count == 0)
        {
            root.Remove(HooksKey);
        }

        if (root.Count == 0 || (root.Count == 1 && root.ContainsKey(VersionKey)))
        {
            UninstallHelpers.DeleteFile(path, context);
            return;
        }

        await IntegratorHelpers.WriteSettingsJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>The <c>hooks.preToolUse</c> array, created when <paramref name="create"/> is set, else <see langword="null"/> when absent.</summary>
    /// <param name="root">The root JSON object.</param>
    /// <param name="path">The file path for error reporting.</param>
    /// <param name="create">Whether to create missing structure.</param>
    /// <returns>The <c>preToolUse</c> array, or <see langword="null"/> if it does not exist and <paramref name="create"/> is <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">The hooks or preToolUse property is not the expected JSON type.</exception>
    private static JsonArray? EventArray(JsonObject root, string path, bool create)
    {
        if (root[HooksKey] is null)
        {
            if (!create)
            {
                return null;
            }

            root[HooksKey] = new JsonObject();
        }

        if (root[HooksKey] is not JsonObject hooks)
        {
            throw new InvalidOperationException($"The Cursor hooks file '{path}' has a 'hooks' property that is not a JSON object.");
        }

        if (hooks[EventKey] is null)
        {
            if (!create)
            {
                return null;
            }

            hooks[EventKey] = new JsonArray();
        }

        return hooks[EventKey] as JsonArray
            ?? throw new InvalidOperationException($"The Cursor hooks file '{path}' has a 'hooks.{EventKey}' property that is not a JSON array.");
    }

    /// <summary>The entries whose command is exactly <paramref name="command"/>, in file order.</summary>
    /// <param name="hookArray">The array of hook entries to search.</param>
    /// <param name="command">The command to match.</param>
    /// <returns>A list of matching entries.</returns>
    private static List<JsonObject> Matches(JsonArray hookArray, string command) =>
        [.. hookArray.OfType<JsonObject>().Where(entry =>
            entry[CommandKey] is JsonValue value && value.TryGetValue<string>(out var text) && text == command)];
}

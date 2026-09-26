using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Finds the file whose <c>PreToolUse</c> hooks Factory Droid runs. Droid reads the root <c>hooks.json</c> (the legacy
/// <c>hooks/hooks.json</c> only when the root file is absent) and merges it over the <c>hooks</c> key of
/// <c>settings.json</c> per event key (rtk <c>src/hooks/init/droid.rs</c>, verified on Droid v0.164.0). A hook written
/// to a shadowed file never runs, and a <c>PreToolUse</c> added to <c>hooks.json</c> would shadow the user's own
/// <c>settings.json</c> hooks, so dtk writes where Droid already reads <c>PreToolUse</c> from.
/// </summary>
internal static class FactoryDroidHooks
{
    /// <summary>The hook event dtk registers.</summary>
    internal const string EventKey = "PreToolUse";

    /// <summary>The <c>settings.json</c> property holding its hook events.</summary>
    private const string SettingsContainerKey = "hooks";

    /// <summary>
    /// Every file Droid may read <c>PreToolUse</c> from, in Droid's order, each with the property holding its events
    /// (<see langword="null"/> when they sit at the root).
    /// </summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static IReadOnlyList<(string Path, string? ContainerKey)> Candidates(string factoryDir) =>
    [
        (Path.Combine(factoryDir, "hooks.json"), null),
        (Path.Combine(factoryDir, "hooks", "hooks.json"), null),
        (Path.Combine(factoryDir, "settings.json"), SettingsContainerKey)
    ];

    /// <summary>
    /// The file dtk registers its hook in: the live <c>hooks.json</c> when it already defines <c>PreToolUse</c>; else
    /// <c>settings.json</c> when its <c>hooks.PreToolUse</c> is non-empty; else the live <c>hooks.json</c> when one
    /// exists; else a new root <c>hooks.json</c>, where Droid's own <c>/hooks</c> UI writes.
    /// </summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static (string Path, string? ContainerKey) ResolveTarget(string factoryDir)
    {
        var candidates = Candidates(factoryDir);
        var root = candidates[0].Path;
        var legacy = candidates[1].Path;
        var settings = candidates[2].Path;

        var liveHooksJson = GetLiveHooksJson(root, legacy);

        if (liveHooksJson is not null && DefinesPreToolUse(liveHooksJson, containerKey: null))
        {
            return (liveHooksJson, null);
        }

        if (DefinesPreToolUse(settings, SettingsContainerKey))
        {
            return (settings, SettingsContainerKey);
        }

        return (liveHooksJson ?? root, null);
    }

    /// <summary>Returns the root hooks.json path if it exists, the legacy path if that exists, or null.</summary>
    /// <param name="root">The root hooks.json path.</param>
    /// <param name="legacy">The legacy hooks/hooks.json path.</param>
    private static string? GetLiveHooksJson(string root, string legacy)
    {
        if (File.Exists(root))
        {
            return root;
        }

        return File.Exists(legacy) ? legacy : null;
    }

    /// <summary>Whether <paramref name="path"/> holds a non-empty <c>PreToolUse</c> array; unreadable files hold none.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="containerKey">The property holding the events, or <see langword="null"/> for the root.</param>
    private static bool DefinesPreToolUse(string path, string? containerKey)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: IntegratorHelpers.LenientJson);
            var events = containerKey is null ? node as JsonObject : (node as JsonObject)?[containerKey] as JsonObject;
            return events?[EventKey] is JsonArray { Count: > 0 };
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

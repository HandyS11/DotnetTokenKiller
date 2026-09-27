using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration.Hooks;

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

    /// <summary>The command dtk registers, which marks a <c>PreToolUse</c> handler as dtk's own.</summary>
    private static readonly string DtkCommand = HookCommands.Invocation("droid");

    /// <summary>
    /// The file dtk registers its hook in: the live <c>hooks.json</c> when it already defines <c>PreToolUse</c>; else
    /// <c>settings.json</c> when its <c>hooks.PreToolUse</c> is non-empty; else the live <c>hooks.json</c> when one
    /// exists; else a new root <c>hooks.json</c>, where Droid's own <c>/hooks</c> UI writes. A <c>hooks.json</c>
    /// <c>PreToolUse</c> holding only dtk's own handlers does not count as defining one when <c>settings.json</c>'s is
    /// non-empty: it exists only because an earlier install put dtk there, and would otherwise keep shadowing the hooks
    /// the user added to <c>settings.json</c> since.
    /// </summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static (string Path, string? ContainerKey) ResolveTarget(string factoryDir)
    {
        var candidates = Candidates(factoryDir);
        var root = candidates[0].Path;
        var legacy = candidates[1].Path;
        var settings = candidates[2].Path;

        var liveHooksJson = GetLiveHooksJson(root, legacy);
        var hooksJsonPreToolUse = liveHooksJson is null ? null : ReadPreToolUse(liveHooksJson, containerKey: null);
        var settingsDefinesPreToolUse = ReadPreToolUse(settings, SettingsContainerKey) is { Count: > 0 };

        if (hooksJsonPreToolUse is { Count: > 0 } && !(settingsDefinesPreToolUse && HoldsOnlyDtk(hooksJsonPreToolUse)))
        {
            return (liveHooksJson!, null);
        }

        if (settingsDefinesPreToolUse)
        {
            return (settings, SettingsContainerKey);
        }

        return (liveHooksJson ?? root, null);
    }

    /// <summary>
    /// Whether <paramref name="path"/>'s <c>PreToolUse</c> registers dtk's hook, by the match the install and uninstall
    /// use; a file that is missing or cannot be read leniently holds none.
    /// </summary>
    /// <param name="path">The candidate file to read.</param>
    /// <param name="containerKey">The property holding the events, or <see langword="null"/> for the root.</param>
    internal static bool HoldsDtkHandler(string path, string? containerKey) =>
        ReadPreToolUse(path, containerKey) is { } preToolUse && CountDtkHandlers(preToolUse) > 0;

    /// <summary>Whether <paramref name="path"/> parses leniently (comments and trailing commas allowed) as JSON.</summary>
    /// <param name="path">An existing candidate file.</param>
    internal static bool IsReadable(string path)
    {
        try
        {
            _ = JsonNode.Parse(File.ReadAllText(path), documentOptions: IntegratorHelpers.LenientJson);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether every handler in <paramref name="preToolUse"/> is dtk's own; anything that is not a matcher group of
    /// command handlers counts as the user's.
    /// </summary>
    /// <param name="preToolUse">A non-empty <c>PreToolUse</c> array.</param>
    private static bool HoldsOnlyDtk(JsonArray preToolUse)
    {
        var handlers = 0;
        foreach (var group in preToolUse)
        {
            if (group is not JsonObject { } entry || entry["hooks"] is not JsonArray inner)
            {
                return false;
            }

            handlers += inner.Count;
        }

        return CountDtkHandlers(preToolUse) == handlers;
    }

    /// <summary>How many handlers in <paramref name="preToolUse"/> are dtk's; zero when a handler's shape is unexpected.</summary>
    /// <param name="preToolUse">A <c>PreToolUse</c> array read from disk.</param>
    private static int CountDtkHandlers(JsonArray preToolUse)
    {
        try
        {
            return IntegratorHelpers.FindEquivalentEntries(preToolUse, DtkCommand).Count;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            // A "command" that is not a string: the file is the user's to fix, and holds nothing dtk can prove is its own.
            return 0;
        }
    }

    /// <summary>The <c>hooks.json</c> Droid reads: the root one when it exists, else the legacy one when that exists.</summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static string? LiveHooksJson(string factoryDir)
    {
        var candidates = Candidates(factoryDir);
        return GetLiveHooksJson(candidates[0].Path, candidates[1].Path);
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

    /// <summary>
    /// The <c>PreToolUse</c> array in <paramref name="path"/>, read leniently (comments and trailing commas allowed), or
    /// <see langword="null"/> when the file is missing, unreadable or has no such array.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="containerKey">The property holding the events, or <see langword="null"/> for the root.</param>
    private static JsonArray? ReadPreToolUse(string path, string? containerKey)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: IntegratorHelpers.LenientJson);
            var events = containerKey is null ? node as JsonObject : (node as JsonObject)?[containerKey] as JsonObject;
            return events?[EventKey] as JsonArray;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}

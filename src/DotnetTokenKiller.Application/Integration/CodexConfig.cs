using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>The parts of Codex CLI's <c>config.toml</c> doctor reads: hook approvals and project trust.</summary>
/// <remarks>
/// Codex records an approved hook under <c>[hooks.state."&lt;hooks.json path&gt;:pre_tool_use:&lt;group&gt;:&lt;handler&gt;"]</c>
/// with a <c>trusted_hash</c> over that one handler's definition, and adds <c>enabled = false</c> there when the hook
/// is switched off under <c>/hooks</c> (verified against Codex 0.154). The indices are the handler's raw positions in
/// the file, so an approval is only ever for one handler. The hash is internal to Codex, so this reads only whether
/// an approval exists for that position, never whether it still matches.
/// </remarks>
internal sealed class CodexConfig
{
    private readonly TomlTable? _root;
    private readonly bool _ignoreCase;

    private CodexConfig(TomlTable? root, bool ignoreCase)
    {
        _root = root;
        _ignoreCase = ignoreCase;
    }

    /// <summary>Gets a value indicating whether the file was absent or parsed; <see langword="false"/> when it could not be read.</summary>
    internal bool IsReadable => _root is not null;

    /// <summary>Reads a <c>config.toml</c>; a missing file reads as empty.</summary>
    /// <param name="path">The file to read.</param>
    /// <remarks>Path keys compare ignoring ASCII case on Windows, as Codex compares project trust keys there.</remarks>
    internal static CodexConfig Load(string path) => Load(path, OperatingSystem.IsWindows());

    /// <summary>Test seam: reads a <c>config.toml</c>, choosing how its path keys compare.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="ignoreCase">
    /// Whether <c>[projects]</c> and <c>[hooks.state]</c> keys match a path ignoring ASCII case, as on Windows.
    /// </param>
    internal static CodexConfig Load(string path, bool ignoreCase)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new CodexConfig([], ignoreCase);
            }

            return TomlSerializer.TryDeserialize(File.ReadAllText(path), TomlTableContext.Default, out TomlTable? model)
                ? new CodexConfig(model, ignoreCase)
                : new CodexConfig(null, ignoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CodexConfig(null, ignoreCase);
        }
    }

    /// <summary>
    /// Whether the <c>PreToolUse</c> handler at this position in <paramref name="hooksJsonPath"/> is approved and
    /// switched on.
    /// </summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    /// <param name="groupIndex">The position of the handler's group in the <c>PreToolUse</c> array.</param>
    /// <param name="handlerIndex">The position of the handler in its group's <c>hooks</c> array.</param>
    internal bool HasHookApproval(string hooksJsonPath, int groupIndex, int handlerIndex) =>
        HookState(hooksJsonPath, groupIndex, handlerIndex) is { } state
        && state.ContainsKey("trusted_hash")
        && !IsSwitchedOff(state);

    /// <summary>
    /// Whether the <c>PreToolUse</c> handler at this position in <paramref name="hooksJsonPath"/> is switched off under
    /// <c>/hooks</c>.
    /// </summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    /// <param name="groupIndex">The position of the handler's group in the <c>PreToolUse</c> array.</param>
    /// <param name="handlerIndex">The position of the handler in its group's <c>hooks</c> array.</param>
    internal bool IsHookTurnedOff(string hooksJsonPath, int groupIndex, int handlerIndex) =>
        HookState(hooksJsonPath, groupIndex, handlerIndex) is { } state && IsSwitchedOff(state);

    /// <summary>Whether Codex trusts <paramref name="projectDirectory"/>, looking it up the way Codex does.</summary>
    /// <param name="projectDirectory">The directory holding <c>.codex/</c>.</param>
    /// <remarks>
    /// Codex takes the first <c>[projects]</c> entry, trusted or not, found for the directory itself and then for its
    /// project root: the closest ancestor holding a <c>.git</c> file, or a <c>.git</c> directory with a <c>HEAD</c>.
    /// Other ancestors do not count. On Windows the keys match ignoring ASCII case, as Codex writes and compares them
    /// lowercased there. Not modelled: a custom <c>project_root_markers</c>, and a linked worktree trusted only through
    /// its main checkout, which both read as untrusted here.
    /// </remarks>
    internal bool TrustsProject(string projectDirectory)
    {
        if (Table(_root, "projects") is not { } projects)
        {
            return false;
        }

        var directory = Path.GetFullPath(projectDirectory);
        foreach (var lookup in (string?[])[directory, RepositoryRoot(directory)])
        {
            if (lookup is not null
                && PathEntry(projects, lookup) is { } project
                && project.TryGetValue("trust_level", out var level))
            {
                return level is "trusted";
            }
        }

        return false;
    }

    private static bool IsSwitchedOff(TomlTable state) => state.TryGetValue("enabled", out var enabled) && enabled is false;

    private static string? RepositoryRoot(string directory)
    {
        for (var candidate = new DirectoryInfo(directory); candidate is not null; candidate = candidate.Parent)
        {
            var marker = Path.Combine(candidate.FullName, ".git");
            if (File.Exists(marker) || File.Exists(Path.Combine(marker, "HEAD")))
            {
                return candidate.FullName;
            }
        }

        return null;
    }

    private TomlTable? HookState(string hooksJsonPath, int groupIndex, int handlerIndex) =>
        PathEntry(Table(Table(_root, "hooks"), "state"), $"{hooksJsonPath}:pre_tool_use:{groupIndex}:{handlerIndex}");

    /// <summary>The table under a key naming a path, compared as Codex compares project trust keys.</summary>
    /// <param name="table">The table holding path keys.</param>
    /// <param name="key">The key to look up.</param>
    /// <remarks>
    /// When ignoring case, as Codex does on Windows: the path lowercased (ASCII only) is looked up first, then the
    /// first, in ordinal order, of the keys that lowercase to it. Codex looks <c>[hooks.state]</c> keys up exactly;
    /// they compare the same way here because the path dtk builds may differ in case from the one Codex discovered.
    /// </remarks>
    private TomlTable? PathEntry(TomlTable? table, string key)
    {
        if (!_ignoreCase || table is null)
        {
            return Table(table, key);
        }

        var lookup = LowercaseAscii(key);
        return Table(table, lookup)
               ?? table.Where(entry => string.Equals(LowercaseAscii(entry.Key), lookup, StringComparison.Ordinal))
                   .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                   .Select(entry => entry.Value)
                   .OfType<TomlTable>()
                   .FirstOrDefault();
    }

    private static string LowercaseAscii(string value) =>
        string.Create(value.Length, value, static (buffer, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                buffer[i] = char.IsAsciiLetterUpper(source[i]) ? (char)(source[i] | 0x20) : source[i];
            }
        });

    private static TomlTable? Table(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as TomlTable : null;
}

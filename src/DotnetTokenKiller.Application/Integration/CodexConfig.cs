using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>The parts of Codex CLI's <c>config.toml</c> doctor reads: hook approvals and project trust.</summary>
/// <remarks>
/// Codex records an approved hook under <c>[hooks.state."&lt;hooks.json path&gt;:pre_tool_use:&lt;group&gt;:&lt;handler&gt;"]</c>
/// with a <c>trusted_hash</c> over the hook's definition, and adds <c>enabled = false</c> there when the hook is switched
/// off under <c>/hooks</c> (verified against Codex 0.154). The hash is internal to Codex, so this reads only whether an
/// approval exists for a file, never whether it still matches.
/// </remarks>
internal sealed class CodexConfig
{
    private readonly TomlTable? _root;

    private CodexConfig(TomlTable? root) => _root = root;

    /// <summary>Gets a value indicating whether the file was absent or parsed; <see langword="false"/> when it could not be read.</summary>
    internal bool IsReadable => _root is not null;

    /// <summary>Reads a <c>config.toml</c>; a missing file reads as empty.</summary>
    /// <param name="path">The file to read.</param>
    internal static CodexConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new CodexConfig([]);
            }

            return TomlSerializer.TryDeserialize(File.ReadAllText(path), TomlTableContext.Default, out TomlTable? model)
                ? new CodexConfig(model)
                : new CodexConfig(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CodexConfig(null);
        }
    }

    /// <summary>Whether any <c>PreToolUse</c> handler in <paramref name="hooksJsonPath"/> is approved and switched on.</summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    internal bool HasHookApproval(string hooksJsonPath) =>
        HookStates(hooksJsonPath).Any(state => state.ContainsKey("trusted_hash") && !IsSwitchedOff(state));

    /// <summary>Whether a <c>PreToolUse</c> handler in <paramref name="hooksJsonPath"/> is switched off under <c>/hooks</c>.</summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    internal bool IsHookTurnedOff(string hooksJsonPath) => HookStates(hooksJsonPath).Any(IsSwitchedOff);

    /// <summary>Whether Codex trusts <paramref name="projectDirectory"/>, looking it up the way Codex does.</summary>
    /// <param name="projectDirectory">The directory holding <c>.codex/</c>.</param>
    /// <remarks>
    /// Codex takes the first <c>[projects]</c> entry, trusted or not, found for the directory itself and then for its
    /// project root: the closest ancestor holding a <c>.git</c> file, or a <c>.git</c> directory with a <c>HEAD</c>.
    /// Other ancestors do not count. Not modelled: a custom <c>project_root_markers</c>, and a linked worktree trusted
    /// only through its main checkout, which both read as untrusted here.
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
                && Table(projects, lookup) is { } project
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

    private IEnumerable<TomlTable> HookStates(string hooksJsonPath)
    {
        var prefix = hooksJsonPath + ":pre_tool_use:";
        return Table(Table(_root, "hooks"), "state") is { } state
            ? state.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => entry.Value)
                .OfType<TomlTable>()
            : [];
    }

    private static TomlTable? Table(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as TomlTable : null;
}

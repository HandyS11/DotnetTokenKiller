using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>The parts of Codex CLI's <c>config.toml</c> doctor reads: hook approvals and project trust.</summary>
/// <remarks>
/// Codex records an approved hook under <c>[hooks.state."&lt;hooks.json path&gt;:pre_tool_use:&lt;group&gt;:&lt;handler&gt;"]</c>
/// with a <c>trusted_hash</c> over the hook's definition. The hash is internal to Codex, so this reads only whether an
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

    /// <summary>Whether any <c>PreToolUse</c> handler in <paramref name="hooksJsonPath"/> has a recorded approval.</summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    internal bool HasHookApproval(string hooksJsonPath)
    {
        var prefix = hooksJsonPath + ":pre_tool_use:";
        return Table(Table(_root, "hooks"), "state") is { } state
               && state.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="projectDirectory"/> or one of its ancestors is marked trusted.</summary>
    /// <param name="projectDirectory">The project root.</param>
    internal bool TrustsProject(string projectDirectory)
    {
        if (Table(_root, "projects") is not { } projects)
        {
            return false;
        }

        for (var directory = new DirectoryInfo(Path.GetFullPath(projectDirectory)); directory is not null; directory = directory.Parent)
        {
            if (Table(projects, directory.FullName) is { } project
                && project.TryGetValue("trust_level", out var level)
                && level is "trusted")
            {
                return true;
            }
        }

        return false;
    }

    private static TomlTable? Table(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as TomlTable : null;
}

using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Cursor and Devin also run the hooks registered in Claude Code's settings files, so dtk's Claude Code hook may fire
/// there beside the harness's own. Both rewrite the same command at most once between them, because
/// <see cref="DotnetCommandRewriter"/> never rewrites a command <c>dtk</c> already runs; the install says so.
/// </summary>
internal static class ImportedClaudeHook
{
    /// <summary>The Claude Code settings files a harness importing them reads.</summary>
    /// <param name="home">Resolves the user's Claude directory.</param>
    /// <param name="projectDirectory">The project root, or <see langword="null"/> for a global install.</param>
    internal static IReadOnlyList<string> SettingsFiles(HomePaths home, string? projectDirectory)
    {
        List<string> files =
            [Path.Combine(home.ClaudeDir, "settings.json"), Path.Combine(home.ClaudeDir, "settings.local.json")];
        if (projectDirectory is not null)
        {
            files.Add(Path.Combine(projectDirectory, ".claude", "settings.json"));
            files.Add(Path.Combine(projectDirectory, ".claude", "settings.local.json"));
        }

        return files;
    }

    /// <summary>Whether any of <paramref name="files"/> registers dtk's Claude Code hook; unreadable files count as no.</summary>
    /// <param name="files">The settings files to search.</param>
    internal static bool IsRegisteredIn(IEnumerable<string> files) => files.Any(Mentions);

    /// <summary>The note printed when <paramref name="harness"/> will also run dtk's Claude Code hook.</summary>
    /// <param name="harness">The harness's display name.</param>
    internal static string Note(string harness) =>
        $"{harness} also runs Claude Code's hooks, and dtk's Claude Code hook is registered: both may see the same "
        + "command, which is harmless — dtk never rewrites a command dtk already runs.";

    private static bool Mentions(string path)
    {
        try
        {
            return File.Exists(path)
                && File.ReadAllText(path).Contains(HookCommands.Invocation("claude"), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

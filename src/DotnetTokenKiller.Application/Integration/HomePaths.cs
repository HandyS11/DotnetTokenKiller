namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Resolves the user's home directory and the per-provider config paths used by global integration.
/// Mirrors the <see cref="RtkHookCoexistence"/> test seam: the public ctor resolves the real home and
/// environment; the internal ctors inject an isolated home, and
/// <c>HomePaths(string home, Func&lt;string, string?&gt; environment)</c> also injects the environment that
/// variables such as <c>CODEX_HOME</c> are read from, so tests never touch the real machine.
/// </summary>
internal sealed class HomePaths
{
    private readonly Func<string, string?> _environment;

    /// <summary>Creates an instance rooted at the real user profile directory, reading the real environment.</summary>
    public HomePaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>Test seam: inject an isolated home directory, with no environment overrides.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    internal HomePaths(string home)
        : this(home, static _ => null)
    {
    }

    /// <summary>Test seam: inject an isolated home directory and environment.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    /// <param name="environment">Reads an environment variable; returns <see langword="null"/> when unset.</param>
    internal HomePaths(string home, Func<string, string?> environment)
    {
        Home = home;
        _environment = environment;
    }

    /// <summary>Gets the user's home directory.</summary>
    internal string Home { get; }

    /// <summary>Gets the user-level Claude Code config directory (<c>~/.claude</c>).</summary>
    internal string ClaudeDir => Path.Combine(Home, ".claude");

    /// <summary>Gets the user-level Gemini CLI config directory (<c>~/.gemini</c>).</summary>
    internal string GeminiDir => Path.Combine(Home, ".gemini");

    /// <summary>Gets the user-level Aider config file path (<c>~/.aider.conf.yml</c>).</summary>
    internal string AiderConfPath => Path.Combine(Home, ".aider.conf.yml");

    /// <summary>Gets the user-level dtk instructions file path (<c>~/.aider-dtk-instructions.md</c>).</summary>
    internal string AiderInstructionsPath => Path.Combine(Home, ".aider-dtk-instructions.md");

    /// <summary>Gets the user-level GitHub Copilot CLI hooks directory (<c>~/.copilot/hooks</c>).</summary>
    internal string CopilotHooksDir => Path.Combine(Home, ".copilot", "hooks");

    /// <summary>Gets Codex CLI's home directory: <c>$CODEX_HOME</c> when it is an absolute path, else <c>~/.codex</c>.</summary>
    internal string CodexDir => RootedOrDefault("CODEX_HOME", Path.Combine(Home, ".codex"));

    /// <summary>Gets the user-level skills directory Codex CLI and OpenCode both read (<c>~/.agents/skills</c>).</summary>
    internal string AgentsSkillsDir => Path.Combine(Home, ".agents", "skills");

    /// <summary>An environment variable's value when it is an absolute path, otherwise <paramref name="fallback"/>.</summary>
    /// <param name="variable">The variable to read.</param>
    /// <param name="fallback">The path to use when the variable is unset, empty or relative.</param>
    private string RootedOrDefault(string variable, string fallback)
    {
        var value = _environment(variable);
        return string.IsNullOrEmpty(value) || !Path.IsPathRooted(value) ? fallback : value;
    }
}

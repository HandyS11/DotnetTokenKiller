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

    /// <summary>
    /// Gets the user-level config directory Antigravity CLI, the Antigravity app and the IDE share for hooks
    /// (<c>~/.gemini/config</c>).
    /// </summary>
    internal string AntigravityConfigDir => Path.Combine(GeminiDir, "config");

    /// <summary>Gets the user-level skills directory Antigravity CLI reads.</summary>
    internal string AntigravitySkillsDir => Path.Combine(AntigravityConfigDir, "skills");

    /// <summary>Gets the user-level Aider config file path (<c>~/.aider.conf.yml</c>).</summary>
    internal string AiderConfPath => Path.Combine(Home, ".aider.conf.yml");

    /// <summary>Gets the user-level dtk instructions file path (<c>~/.aider-dtk-instructions.md</c>).</summary>
    internal string AiderInstructionsPath => Path.Combine(Home, ".aider-dtk-instructions.md");

    /// <summary>Gets the user-level GitHub Copilot CLI hooks directory (<c>~/.copilot/hooks</c>).</summary>
    internal string CopilotHooksDir => Path.Combine(Home, ".copilot", "hooks");

    /// <summary>Gets Codex CLI's home directory: <c>$CODEX_HOME</c> when it is an absolute path, else <c>~/.codex</c>.</summary>
    internal string CodexDir => RootedOrDefault("CODEX_HOME", Path.Combine(Home, ".codex"));

    /// <summary>Gets Cursor's user directory (<c>~/.cursor</c>), which holds its user-level <c>hooks.json</c>.</summary>
    internal string CursorDir => Path.Combine(Home, ".cursor");

    /// <summary>
    /// Gets Devin CLI's and Devin Local's user config directory: <c>%APPDATA%\devin</c> on Windows (when
    /// <c>APPDATA</c> is an absolute path, else <c>~\AppData\Roaming\devin</c>), <c>~/.config/devin</c> elsewhere.
    /// </summary>
    internal string DevinConfigDir => OperatingSystem.IsWindows()
        ? Path.Combine(RootedOrDefault("APPDATA", Path.Combine(Home, "AppData", "Roaming")), "devin")
        : Path.Combine(Home, ".config", "devin");

    /// <summary>
    /// Gets the global rules file Devin Desktop (formerly Windsurf) always loads:
    /// <c>~/.codeium/windsurf/memories/global_rules.md</c>.
    /// </summary>
    internal string WindsurfGlobalRulesPath => Path.Combine(Home, ".codeium", "windsurf", "memories", "global_rules.md");

    /// <summary>
    /// Gets the user-level skills directory Codex CLI, OpenCode, pi and oh-my-pi all read (<c>~/.agents/skills</c>).
    /// </summary>
    internal string AgentsSkillsDir => Path.Combine(Home, ".agents", "skills");

    /// <summary>
    /// Gets OpenCode's user config directory: <c>$XDG_CONFIG_HOME/opencode</c> when that variable is an absolute path,
    /// else <c>~/.config/opencode</c> — on Windows too, where OpenCode also uses <c>~/.config</c>.
    /// </summary>
    internal string OpenCodeConfigDir =>
        Path.Combine(RootedOrDefault("XDG_CONFIG_HOME", Path.Combine(Home, ".config")), "opencode");

    /// <summary>
    /// Gets pi's agent directory: <c>$PI_CODING_AGENT_DIR</c> when it is an absolute path or starts with <c>~</c>
    /// (expanded against <see cref="Home"/>, as pi does), else <c>~/.pi/agent</c>.
    /// </summary>
    internal string PiAgentDir => PiCodingAgentDir() ?? Path.Combine(Home, ".pi", "agent");

    /// <summary>
    /// Gets oh-my-pi's agent directory: with no profile selected (<c>OMP_PROFILE</c> and <c>PI_PROFILE</c> unset or
    /// empty) oh-my-pi honors <c>$PI_CODING_AGENT_DIR</c> too, resolved as for <see cref="PiAgentDir"/>; otherwise, or
    /// when it is unset, <c>~/.omp/agent</c>. Named profiles and <c>PI_CONFIG_DIR</c> are not supported.
    /// </summary>
    internal string OhMyPiAgentDir =>
        (string.IsNullOrEmpty(_environment("OMP_PROFILE")) && string.IsNullOrEmpty(_environment("PI_PROFILE"))
            ? PiCodingAgentDir()
            : null)
        ?? Path.Combine(Home, ".omp", "agent");

    /// <summary>Gets whether pi and oh-my-pi resolve to the same global agent directory, so both load one extension.</summary>
    internal bool PiAndOhMyPiShareAgentDir =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(PiAgentDir)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(OhMyPiAgentDir)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// <c>$PI_CODING_AGENT_DIR</c> with a leading <c>~</c>, <c>~/</c> or (on Windows) <c>~\</c> expanded against
    /// <see cref="Home"/>, when the result is an absolute path; otherwise <see langword="null"/>.
    /// </summary>
    private string? PiCodingAgentDir()
    {
        var value = _environment("PI_CODING_AGENT_DIR");
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value == "~")
        {
            value = Home;
        }
        else if (value.Length > 1 && value[0] == '~'
                 && (value[1] == Path.DirectorySeparatorChar || value[1] == Path.AltDirectorySeparatorChar))
        {
            value = Path.Combine(Home, value[2..]);
        }

        return Path.IsPathRooted(value) ? value : null;
    }

    /// <summary>An environment variable's value when it is an absolute path, otherwise <paramref name="fallback"/>.</summary>
    /// <param name="variable">The variable to read.</param>
    /// <param name="fallback">The path to use when the variable is unset, empty or relative.</param>
    private string RootedOrDefault(string variable, string fallback)
    {
        var value = _environment(variable);
        return string.IsNullOrEmpty(value) || !Path.IsPathRooted(value) ? fallback : value;
    }
}

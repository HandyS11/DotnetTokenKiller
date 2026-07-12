namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Resolves the user's home directory and the per-provider config paths used by global integration.
/// Mirrors the <see cref="RtkHookCoexistence"/> test seam: the public ctor resolves the real home;
/// the internal ctor injects an isolated home so tests never touch the real machine.
/// </summary>
internal sealed class HomePaths
{
    /// <summary>Creates an instance rooted at the real user profile directory.</summary>
    public HomePaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <summary>Test seam: inject an isolated home directory.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    internal HomePaths(string home)
    {
        Home = home;
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
}

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Where a hook is installed.</summary>
internal enum HookScope
{
    /// <summary>Installed under the current project directory.</summary>
    Project = 0,

    /// <summary>Installed under the user's home configuration.</summary>
    Global = 1
}

/// <summary>Shape of the pre-tool-execution payload a host CLI feeds its hook on stdin.</summary>
internal enum HookPayloadKind
{
    /// <summary>Claude Code's <c>PreToolUse</c> payload.</summary>
    ClaudeCode = 0,

    /// <summary>Gemini CLI's <c>BeforeTool</c> payload.</summary>
    GeminiCli = 1,

    /// <summary>GitHub Copilot CLI's <c>preToolUse</c> payload.</summary>
    CopilotCli = 2,

    /// <summary>OpenAI Codex CLI's <c>PreToolUse</c> payload.</summary>
    CodexCli = 3,

    /// <summary>dtk's own OpenCode plugin payload: <c>{"command": …}</c>.</summary>
    OpenCode = 4,

    /// <summary>Google Antigravity CLI's <c>PreToolUse</c> payload.</summary>
    AntigravityCli = 5,

    /// <summary>dtk's own pi extension payload, the same as <see cref="OpenCode"/>'s.</summary>
    Pi = 6,

    /// <summary>dtk's own oh-my-pi extension payload, the same as <see cref="OpenCode"/>'s.</summary>
    OhMyPi = 7,

    /// <summary>Cursor's <c>preToolUse</c> payload, for its <c>Shell</c> tool.</summary>
    Cursor = 8,

    /// <summary>Devin Local's and Devin CLI's <c>PreToolUse</c> payload: Claude Code's shape, for the <c>exec</c> tool.</summary>
    Devin = 9,

    /// <summary>Factory Droid's <c>PreToolUse</c> payload: Claude Code's shape, for the <c>Execute</c> tool.</summary>
    FactoryDroid = 10,

    /// <summary>Crush's <c>PreToolUse</c> payload, for its <c>bash</c> tool.</summary>
    Crush = 11,

    /// <summary>dtk's own Kilo Code plugin payload, the same as <see cref="OpenCode"/>'s.</summary>
    Kilo = 12,

    /// <summary>dtk's own Amp plugin payload, the same as <see cref="OpenCode"/>'s.</summary>
    Amp = 13
}

/// <summary>One installed (or installable) rewrite hook, described once for both installer and diagnostics.</summary>
/// <param name="ProviderName">The provider this hook belongs to, which is also the <c>dtk hook</c> argument (e.g. "claude").</param>
/// <param name="Scope">Whether this describes the project or the home-config install.</param>
/// <param name="RegistrationPath">
/// The file that registers the hook with the host CLI — a merged <c>settings.json</c> for Claude Code and
/// Gemini CLI, a dedicated <c>dtk-dotnet.json</c> for Copilot CLI, a merged <c>.codex/hooks.json</c> for Codex CLI,
/// a merged <c>.agents/hooks.json</c> (project) or <c>~/.gemini/config/hooks.json</c> (global) for Antigravity CLI,
/// a Bash script (Crush's <c>crushrc</c>, see <see cref="IsScriptRegistration"/>), or — when
/// <see cref="PluginArtifact"/> is non-null — the generated plugin file itself, for OpenCode.
/// </param>
/// <param name="Command">The exact command dtk registers, e.g. <c>dtk hook gemini; exit 0</c>.</param>
/// <param name="LegacyScriptPath">
/// Where dtk installed the Python hook this registration replaces, or <see langword="null"/> for a provider that
/// never had one.
/// </param>
/// <param name="PayloadKind">Payload shape to use when probing this hook.</param>
/// <param name="PluginArtifact">
/// The generated plugin file that is this registration, for harnesses that load plugins instead of running hook
/// commands; <see langword="null"/> for a JSON registration.
/// </param>
internal sealed record HookInstallation(
    string ProviderName,
    HookScope Scope,
    string RegistrationPath,
    string Command,
    string? LegacyScriptPath,
    HookPayloadKind PayloadKind,
    GeneratedArtifact? PluginArtifact = null)
{
    /// <summary>
    /// Gets a value indicating whether <see cref="RegistrationPath"/> is a script (Crush's <c>crushrc</c>) rather than
    /// JSON, so diagnostics search its uncommented lines for the hook command (<see cref="CrushrcFile.RunsCommand"/>)
    /// instead of parsing it.
    /// </summary>
    internal bool IsScriptRegistration { get; init; }
}

/// <summary>
/// Implemented by provider integrators that install a rewrite hook, so a diagnostic can find that
/// hook without keeping its own copy of where the installer put it.
/// </summary>
internal interface IHookIntegrator
{
    /// <summary>Describes the hooks this provider installs at the given scope.</summary>
    /// <param name="directory">Project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to describe.</param>
    IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope);
}

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
    OpenCode = 4
}

/// <summary>One installed (or installable) rewrite hook, described once for both installer and diagnostics.</summary>
/// <param name="ProviderName">The provider this hook belongs to, which is also the <c>dtk hook</c> argument (e.g. "claude").</param>
/// <param name="Scope">Whether this describes the project or the home-config install.</param>
/// <param name="RegistrationPath">
/// The file that registers the hook with the host CLI — a merged <c>settings.json</c> for Claude Code and
/// Gemini CLI, a dedicated <c>dtk-dotnet.json</c> for Copilot CLI, a merged <c>.codex/hooks.json</c> for Codex CLI,
/// or — when <see cref="PluginArtifact"/> is non-null — the generated plugin file itself, for OpenCode.
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
    GeneratedArtifact? PluginArtifact = null);

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

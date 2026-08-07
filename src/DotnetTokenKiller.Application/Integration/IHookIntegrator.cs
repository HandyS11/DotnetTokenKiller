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
    CopilotCli = 2
}

/// <summary>One installed (or installable) rewrite hook, described once for both installer and diagnostics.</summary>
/// <param name="ProviderName">The provider this hook belongs to (e.g. "claude").</param>
/// <param name="Scope">Whether this describes the project or the home-config install.</param>
/// <param name="Script">The hook script as a generated artifact, including its current template body.</param>
/// <param name="RegistrationPath">
/// The JSON file that registers the hook with the host CLI — a merged <c>settings.json</c> for most
/// providers, a dedicated <c>dtk-dotnet.json</c> for Copilot CLI.
/// </param>
/// <param name="PayloadKind">Payload shape to use when probing this hook.</param>
internal sealed record HookInstallation(
    string ProviderName,
    HookScope Scope,
    GeneratedArtifact Script,
    string RegistrationPath,
    HookPayloadKind PayloadKind);

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

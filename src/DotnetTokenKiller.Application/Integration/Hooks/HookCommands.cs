namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>The <c>dtk hook</c> verb and the commands <c>dtk init</c> registers with each harness.</summary>
internal static class HookCommands
{
    /// <summary>The verb a harness runs: <c>dtk hook &lt;provider&gt;</c>.</summary>
    internal const string Verb = "hook";

    /// <summary>The bare hook command, e.g. <c>dtk hook claude</c>.</summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string Invocation(string provider) => $"dtk {Verb} {provider}";

    /// <summary>
    /// The hook command followed by <c>; exit 0</c>, for harnesses that block the tool call when the hook
    /// exits non-zero (Gemini CLI on any code but 0 or 1, Copilot CLI on any code). It keeps a missing
    /// <c>dtk</c> from blocking every shell command, and parses the same under bash, PowerShell 7 and
    /// Windows PowerShell 5.1, which has no <c>||</c>.
    /// </summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string FailOpen(string provider) => $"{Invocation(provider)}; exit 0";
}

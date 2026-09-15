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

    /// <summary>
    /// The hook command followed by <c>|| exit 0</c>, for Antigravity CLI, which blocks the tool call when a hook fails
    /// and runs hook commands through <c>sh -c</c> on Unix and <c>cmd /c</c> on Windows. <c>; exit 0</c> is not valid
    /// under <c>cmd</c>; <c>|| exit 0</c> is valid under both (though not under Windows PowerShell 5.1, which
    /// Antigravity does not use for hooks). A missing <c>dtk</c> exits 127 under sh and 9009 under cmd, and the guard
    /// turns either into success with no output, which gate G1 showed leaves the call to the user's permissions. The
    /// guard cannot help a <c>dtk</c> older than 0.8.0 that is present but does not know <c>hook</c>: it prints its
    /// usage error to stdout and exits non-zero, so <c>|| exit 0</c> fixes the exit code but leaves that text on
    /// stdout, and gate G10 showed Antigravity fails a hook closed on non-JSON stdout regardless of exit code.
    /// Antigravity runs a hook in the directory holding its <c>hooks.json</c> — a trusted workspace's
    /// <c>.agents</c> directory or <c>~/.gemini/config</c> — so <c>cmd</c>'s search of the current directory before
    /// <c>PATH</c> cannot pick up a <c>dtk</c> the workspace could not already register as a hook.
    /// </summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string OrExitZero(string provider) => $"{Invocation(provider)} || exit 0";
}

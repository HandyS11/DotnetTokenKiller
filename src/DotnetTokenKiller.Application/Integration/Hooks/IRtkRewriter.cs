namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>Asks rtk how it would rewrite a shell command.</summary>
internal interface IRtkRewriter
{
    /// <summary>Asks <c>rtk rewrite</c> about <paramref name="command"/>.</summary>
    /// <param name="command">The shell command, as the agent wrote it.</param>
    /// <returns>rtk's answer, or null when rtk could not be asked (not on PATH, timeout, failed to start).</returns>
    RtkRewrite? Rewrite(string command);
}

/// <summary>What <c>rtk rewrite</c> answered.</summary>
/// <param name="ExitCode">0 allow + rewrite, 1 none, 2 deny rule, 3 ask + rewrite (rtk's protocol).</param>
/// <param name="Output">stdout with one trailing "\n" or "\r\n" removed.</param>
internal sealed record RtkRewrite(int ExitCode, string Output);

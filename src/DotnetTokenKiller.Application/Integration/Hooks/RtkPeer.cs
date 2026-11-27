namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// What <c>dtk hook claude</c> asks about rtk's own Claude Code hook, to refuse a command both hooks would rewrite
/// (<see cref="RtkRewriteRace"/>). Both members are asked only once dtk has rewritten the command.
/// </summary>
/// <param name="IsHookRegistered">
/// Whether rtk's hook is registered for Claude Code in the user's settings or those of the project directory it is
/// given.
/// </param>
/// <param name="Rewriter">Asks rtk how it would rewrite a command.</param>
internal sealed record RtkPeer(Func<string, bool> IsHookRegistered, IRtkRewriter Rewriter);

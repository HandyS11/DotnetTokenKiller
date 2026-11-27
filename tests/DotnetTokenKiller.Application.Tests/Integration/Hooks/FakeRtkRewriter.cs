using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

/// <summary>An <see cref="IRtkRewriter"/> that answers from a function and records what it was asked.</summary>
/// <param name="answer">What to answer for a command.</param>
internal sealed class FakeRtkRewriter(Func<string, RtkRewrite?> answer) : IRtkRewriter
{
    private static readonly Regex UnprefixedGit = new(@"(?<!rtk )\bgit\b");

    /// <summary>The commands asked about, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// Like rtk: prefixes <c>git</c> with <c>rtk </c> (exit 0) and answers exit 1 when nothing changes, so an
    /// already-prefixed command is left alone.
    /// </summary>
    /// <param name="command">The command asked about.</param>
    public static RtkRewrite? PrefixGit(string command)
    {
        var rewritten = UnprefixedGit.Replace(command, "rtk git");
        return rewritten == command ? new RtkRewrite(1, "") : new RtkRewrite(0, rewritten);
    }

    /// <inheritdoc />
    public RtkRewrite? Rewrite(string command)
    {
        Calls.Add(command);
        return answer(command);
    }
}

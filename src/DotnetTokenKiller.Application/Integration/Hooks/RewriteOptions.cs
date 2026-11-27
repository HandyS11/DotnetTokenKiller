namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>What <see cref="DotnetCommandRewriter.Rewrite(string, RewriteOptions)"/> may do beyond prefixing <c>dtk</c>.</summary>
/// <param name="ConvertTailPipes">
/// Whether <c>| tail -N</c> and <c>| head -N</c> become <c>--max-lines N</c>. Off when the hook runs with
/// <c>DTK_RAW</c> on, where dtk would pass the run through and drop the cap.
/// </param>
/// <param name="ConvertExplicitDtk">Whether a command that already names <c>dtk</c> has its <c>| tail -N</c> converted too (never prefixed). Only Claude Code's reply sets it. It does nothing while <paramref name="ConvertTailPipes"/> is off.</param>
internal sealed record RewriteOptions(bool ConvertTailPipes, bool ConvertExplicitDtk)
{
    /// <summary>Tail pipes converted, explicit <c>dtk</c> commands left alone.</summary>
    internal static RewriteOptions Default { get; } = new(ConvertTailPipes: true, ConvertExplicitDtk: false);
}

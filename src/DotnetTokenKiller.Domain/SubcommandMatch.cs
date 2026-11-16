namespace DotnetTokenKiller.Domain;

/// <summary>A matched dtk-handled subcommand and how many argv tokens it consumed.</summary>
/// <param name="Name">The canonical, space-joined subcommand name, e.g. <c>list package</c>.</param>
/// <param name="TokenCount">
/// How many leading argv tokens the match consumed. Callers that need to look past the subcommand
/// — inserting a <c>--</c> separator, say — must use this rather than assuming 1.
/// </param>
/// <param name="TargetIndex">
/// The index, among the consumed tokens, of the project or solution dotnet takes between the subcommand's words
/// (<c>ISDM.slnx</c> at 1 in <c>list ISDM.slnx package</c>), or <c>-1</c> when the words are contiguous.
/// </param>
public readonly record struct SubcommandMatch(string Name, int TokenCount, int TargetIndex = -1)
{
    /// <summary>The indices, among the consumed tokens, of the subcommand's own words: every one but the target.</summary>
    public IEnumerable<int> WordIndices
    {
        get
        {
            var target = TargetIndex;
            return Enumerable.Range(0, TokenCount).Where(i => i != target);
        }
    }
}

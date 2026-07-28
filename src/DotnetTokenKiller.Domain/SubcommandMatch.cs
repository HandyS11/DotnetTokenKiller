namespace DotnetTokenKiller.Domain;

/// <summary>A matched dtk-handled subcommand and how many argv tokens it consumed.</summary>
/// <param name="Name">The canonical, space-joined subcommand name, e.g. <c>list package</c>.</param>
/// <param name="TokenCount">
/// How many leading argv tokens the match consumed. Callers that need to look past the subcommand
/// — inserting a <c>--</c> separator, say — must use this rather than assuming 1.
/// </param>
public readonly record struct SubcommandMatch(string Name, int TokenCount);

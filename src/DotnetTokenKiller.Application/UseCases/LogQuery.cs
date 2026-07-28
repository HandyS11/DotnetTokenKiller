using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Which previously written log to retrieve, and how much of it.</summary>
/// <param name="Subcommand">
/// The canonical subcommand to restrict to, e.g. <c>list package</c>, or <see langword="null"/> for
/// any.
/// </param>
/// <param name="ProjectPath">
/// The working directory to scope to, or <see langword="null"/> to include every project.
/// </param>
/// <param name="Index">Which of the matches to view, 1-based, newest first.</param>
/// <param name="Lines">How many trailing lines to show when <paramref name="Full"/> is false.</param>
/// <param name="Full">Show the whole body, ignoring <paramref name="Lines"/>.</param>
public sealed record LogQuery(
    string? Subcommand = null,
    string? ProjectPath = null,
    int Index = 1,
    int Lines = 100,
    bool Full = false);

/// <summary>The logs matching a query.</summary>
/// <param name="Matches">The matching logs, newest first.</param>
/// <param name="LegacyExcluded">
/// How many logs were dropped because they carry no project metadata. Non-zero only for a scoped
/// query, and reported to the user so an empty result does not read as data loss.
/// </param>
public sealed record LogSelection(IReadOnlyList<TeeLogEntry> Matches, int LegacyExcluded);

/// <summary>One log's contents, windowed.</summary>
/// <param name="Entry">The log this view came from.</param>
/// <param name="Body">The lines to display, joined with a line feed and without a trailing one.</param>
/// <param name="TotalLines">How many lines the body has in total.</param>
/// <param name="ShownLines">How many of them <paramref name="Body"/> holds.</param>
public sealed record LogView(TeeLogEntry Entry, string Body, int TotalLines, int ShownLines);

/// <summary>The outcome of a view request.</summary>
/// <param name="View">
/// The view, or <see langword="null"/> when there was nothing to show. A null view with a non-empty
/// <paramref name="Selection"/> means the requested index was out of range.
/// </param>
/// <param name="Selection">The matches the query produced, for the caller's messaging.</param>
public sealed record LogViewResult(LogView? View, LogSelection Selection);

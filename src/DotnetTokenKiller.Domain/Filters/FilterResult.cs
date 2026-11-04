namespace DotnetTokenKiller.Domain.Filters;

/// <summary>A contextual filter's output and its verdicts about the run.</summary>
/// <param name="Output">The condensed output.</param>
/// <param name="NoTestsMatched">Whether the run executed no test.</param>
public readonly record struct FilterResult(string Output, bool NoTestsMatched = false);

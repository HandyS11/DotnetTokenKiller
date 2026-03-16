namespace DotnetTokenKiller.Domain.Filters;

/// <summary>Filters and condenses raw command output.</summary>
public interface IOutputFilter
{
    /// <summary>Applies the filter to the raw output and returns the condensed result.</summary>
    /// <param name="rawOutput">The raw output to filter.</param>
    string Apply(string rawOutput);
}

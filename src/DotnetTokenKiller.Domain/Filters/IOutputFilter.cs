namespace DotnetTokenKiller.Domain.Filters;

public interface IOutputFilter
{
    string Apply(string rawOutput);
}

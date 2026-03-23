using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

internal sealed class IntegrationContext
{
    internal IntegrationContext(bool force)
    {
        Force = force;
    }

    internal bool Force { get; }
    internal List<string> Created { get; } = [];
    internal List<string> Updated { get; } = [];
    internal List<string> Skipped { get; } = [];

    internal IntegrationResult ToResult()
    {
        return new IntegrationResult(Created, Updated, Skipped);
    }
}

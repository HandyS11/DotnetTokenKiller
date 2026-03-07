using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

public sealed class NullTeeService : ITeeService
{
    public Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

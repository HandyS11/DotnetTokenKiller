using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Infrastructure.Configuration;

public sealed class NullConfigProvider : IConfigProvider
{
    public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DtkConfig.Default);
    }

    public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

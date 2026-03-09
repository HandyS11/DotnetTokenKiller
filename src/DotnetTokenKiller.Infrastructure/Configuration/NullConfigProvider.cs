using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Infrastructure.Configuration;

public sealed class NullConfigProvider : IConfigProvider
{
    public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DtkConfig.Default);

    public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

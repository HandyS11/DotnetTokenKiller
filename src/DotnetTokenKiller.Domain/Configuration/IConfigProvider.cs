namespace DotnetTokenKiller.Domain.Configuration;

public interface IConfigProvider
{
    Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default);
}

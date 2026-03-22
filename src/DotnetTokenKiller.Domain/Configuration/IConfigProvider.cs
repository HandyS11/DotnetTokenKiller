namespace DotnetTokenKiller.Domain.Configuration;

/// <summary>Loads and persists tool configuration.</summary>
public interface IConfigProvider
{
    /// <summary>Loads the current configuration synchronously, returning defaults if none exists.</summary>
    /// <remarks>Use only at startup (e.g. DI factory). Prefer <see cref="LoadAsync"/> everywhere else.</remarks>
    DtkConfig Load();

    /// <summary>Loads the current configuration, returning defaults if none exists.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the given configuration.</summary>
    /// <param name="config">The configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default);
}

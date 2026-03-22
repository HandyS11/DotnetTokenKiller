namespace DotnetTokenKiller.Domain.Configuration;

/// <summary>Loads and persists tool configuration.</summary>
public interface IConfigProvider
{
    /// <summary>
    /// Loads the current configuration synchronously, returning defaults if no configuration exists
    /// or if the existing configuration is unreadable or invalid.
    /// </summary>
    /// <remarks>
    /// Configuration load errors are intentionally tolerated; if the configuration cannot be read
    /// or is invalid, default values are returned instead. Use only at startup (e.g. DI factory).
    /// Prefer <see cref="LoadAsync"/> everywhere else.
    /// </remarks>
    DtkConfig Load();

    /// <summary>
    /// Loads the current configuration, returning defaults if no configuration exists
    /// or if the existing configuration is unreadable or invalid.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the given configuration.</summary>
    /// <param name="config">The configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default);
}

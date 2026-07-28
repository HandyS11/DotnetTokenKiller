using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Infrastructure.Tests;

/// <summary>Fake <see cref="IConfigProvider"/> — avoids a mocking dependency the test projects don't reference.</summary>
/// <param name="initialConfig">The configuration to return from <see cref="Load"/> and <see cref="LoadAsync"/>.</param>
public sealed class FakeConfigProvider(DtkConfig initialConfig) : IConfigProvider
{
    /// <inheritdoc/>
    public DtkConfig Load()
    {
        return initialConfig;
    }

    /// <inheritdoc/>
    public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(initialConfig);
    }

    /// <inheritdoc/>
    public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

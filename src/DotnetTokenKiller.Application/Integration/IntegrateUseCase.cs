using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Runs the integration for a named provider.</summary>
/// <param name="integrators">All registered provider integrators.</param>
public sealed class IntegrateUseCase(IEnumerable<IProviderIntegrator> integrators)
{
    private readonly IReadOnlyDictionary<string, IProviderIntegrator> _integrators =
        integrators.ToDictionary(i => i.ProviderName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the names of all available providers.</summary>
    public IEnumerable<string> AvailableProviders => _integrators.Keys;

    /// <summary>Runs the named provider's integration.</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude", "copilot").</param>
    /// <param name="directory">Target project root directory.</param>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The integration result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerName"/> is unknown.</exception>
    public Task<IntegrationResult> RunAsync(
        string providerName,
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!_integrators.TryGetValue(providerName, out var integrator))
        {
            throw new ArgumentException(
                $"Unknown provider '{providerName}'. Available: {string.Join(", ", _integrators.Keys)}",
                nameof(providerName));
        }

        return integrator.IntegrateAsync(directory, force, cancellationToken);
    }
}

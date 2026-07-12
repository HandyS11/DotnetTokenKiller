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
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="providerName"/> is unknown.</exception>
    public Task<IntegrationResult> RunAsync(
        string providerName,
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var integrator = ResolveOrThrow(providerName);

        return integrator.IntegrateAsync(directory, force, cancellationToken);
    }

    /// <summary>Runs the named provider's global (home config) integration.</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude").</param>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The integration result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="providerName"/> is unknown, or when the provider does not support
    /// global integration (it is repository-scoped).
    /// </exception>
    public Task<IntegrationResult> RunGlobalAsync(
        string providerName,
        bool force,
        CancellationToken cancellationToken)
    {
        var integrator = ResolveOrThrow(providerName);

        if (integrator is not IGlobalIntegrator globalIntegrator)
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' is repository-scoped and has no global config. " +
                $"Run 'dtk integrate {providerName}' inside a project.");
        }

        return globalIntegrator.IntegrateGlobalAsync(force, cancellationToken);
    }

    /// <summary>Resolves a provider by name, throwing if it is not registered.</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude", "copilot").</param>
    /// <returns>The resolved integrator.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="providerName"/> is unknown.</exception>
    private IProviderIntegrator ResolveOrThrow(string providerName)
    {
        // InvalidOperationException (not ArgumentException): IntegrateCommand validates
        // settings.Provider against AvailableProviders before calling RunAsync, so this path
        // is a defense-in-depth guard for other callers of this public use case rather than
        // the CLI's primary error path.
        if (!_integrators.TryGetValue(providerName, out var integrator))
        {
            throw new InvalidOperationException(
                $"Unknown provider '{providerName}'. Available: {string.Join(", ", _integrators.Keys)}");
        }

        return integrator;
    }
}

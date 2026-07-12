namespace DotnetTokenKiller.Domain.Integration;

/// <summary>
/// Optional capability implemented by provider integrators that support installing their artifacts
/// into the user's home configuration (so the integration applies across all projects), in addition
/// to the per-project install described by <see cref="IProviderIntegrator"/>.
/// </summary>
public interface IGlobalIntegrator
{
    /// <summary>Installs all integration artifacts into the user's home configuration.</summary>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken);
}

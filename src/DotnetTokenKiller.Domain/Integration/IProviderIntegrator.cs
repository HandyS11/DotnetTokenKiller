namespace DotnetTokenKiller.Domain.Integration;

/// <summary>Installs dtk integration artifacts for a specific AI assistant provider.</summary>
public interface IProviderIntegrator
{
    /// <summary>Gets the unique provider identifier (e.g. "claude", "copilot").</summary>
    string ProviderName { get; }

    /// <summary>Installs all integration artifacts into the given project directory.</summary>
    /// <param name="directory">Root directory of the target project.</param>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken);
}

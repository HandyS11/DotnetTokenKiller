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
                $"Run 'dtk init {providerName}' inside a project.");
        }

        return globalIntegrator.IntegrateGlobalAsync(force, cancellationToken);
    }

    /// <summary>Removes what the named provider's integration installs (<c>dtk init &lt;provider&gt; --uninstall</c>).</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude").</param>
    /// <param name="directory">Target project root directory; ignored when <paramref name="global"/> is set.</param>
    /// <param name="global">Whether to remove the global (home config) integration instead of the project's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was removed, left unchanged and kept.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="providerName"/> is unknown or cannot be uninstalled, or when
    /// <paramref name="global"/> is set for a repository-scoped provider.
    /// </exception>
    /// <remarks>
    /// An <c>AGENTS.md</c>, <c>GEMINI.md</c> or skill another provider's install also writes keeps dtk's part while
    /// that provider's hook is still registered in the same scope, so uninstalling one harness does not take the
    /// instructions from another. Providers without a hook cannot be detected that way and never hold a file back.
    /// </remarks>
    public Task<IntegrationResult> UninstallAsync(
        string providerName,
        string directory,
        bool global,
        CancellationToken cancellationToken)
    {
        var integrator = ResolveOrThrow(providerName);

        if (global && integrator is not IGlobalIntegrator)
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' is repository-scoped and has no global config. " +
                $"Run 'dtk init {providerName} --uninstall' inside a project.");
        }

        if (integrator is not IUninstallIntegrator uninstaller)
        {
            throw new InvalidOperationException($"Provider '{providerName}' does not support --uninstall.");
        }

        var scope = global ? HookScope.Global : HookScope.Project;

        return uninstaller.UninstallAsync(directory, scope, SharedArtifactsInUse(integrator, directory, scope), cancellationToken);
    }

    /// <summary>
    /// The shared files that providers other than <paramref name="target"/>, whose hook is registered in
    /// <paramref name="scope"/>, also write, mapped to the first such provider's name.
    /// </summary>
    /// <param name="target">The provider being uninstalled.</param>
    /// <param name="directory">Project root; ignored for <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">The scope being uninstalled.</param>
    private Dictionary<string, string> SharedArtifactsInUse(IProviderIntegrator target, string directory, HookScope scope)
    {
        var inUse = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var other in _integrators.Values)
        {
            if (ReferenceEquals(other, target)
                || other is not (IUninstallIntegrator uninstaller and IHookIntegrator hooks)
                || !hooks.DescribeHooks(directory, scope).Any(UninstallHelpers.IsRegistered))
            {
                continue;
            }

            foreach (var path in uninstaller.SharedArtifactPaths(directory, scope))
            {
                inUse.TryAdd(path, other.ProviderName);
            }
        }

        return inUse;
    }

    /// <summary>Resolves a provider by name, throwing if it is not registered.</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude", "copilot").</param>
    /// <returns>The resolved integrator.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="providerName"/> is unknown.</exception>
    private IProviderIntegrator ResolveOrThrow(string providerName)
    {
        // InvalidOperationException (not ArgumentException): InitCommand validates
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

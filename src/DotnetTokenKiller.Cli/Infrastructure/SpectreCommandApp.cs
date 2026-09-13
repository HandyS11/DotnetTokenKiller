using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Creates the Spectre.Console.Cli application, which Native AOT does not officially support.</summary>
internal static class SpectreCommandApp
{
    /// <summary>Creates a <see cref="CommandApp"/> that resolves commands through <paramref name="registrar"/>.</summary>
    /// <param name="registrar">The registrar bridging Spectre.Console.Cli to dependency injection.</param>
    /// <returns>The unconfigured application.</returns>
    [UnconditionalSuppressMessage(
        "AotAnalysis",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "Spectre.Console.Cli marks CommandApp as unsupported under Native AOT because it binds "
                        + "commands and settings by reflection. dtk roots both assemblies (TrimmerRootAssembly), "
                        + "limits settings to the member types CommandSettingsAotGuardTests allows, and "
                        + "AotParityTests runs every command family through the AOT binary on each release RID. "
                        + "One of the two trim or AOT suppressions at the Spectre.Console.Cli boundary; see the "
                        + "native AOT design spec.")]
    internal static CommandApp Create(ITypeRegistrar registrar) => new(registrar);
}

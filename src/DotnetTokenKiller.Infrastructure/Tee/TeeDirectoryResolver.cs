using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>
/// Resolves the tee directory from the override, the environment, config, and the platform default.
/// </summary>
/// <remarks>
/// Shared by <see cref="FileTeeService"/> (which writes) and <see cref="FileTeeLogStore"/> (which
/// reads). Two copies of this precedence chain could disagree, and the symptom would be logs that
/// are written successfully and then cannot be found.
/// </remarks>
public static class TeeDirectoryResolver
{
    /// <summary>The environment variable that overrides the configured tee directory.</summary>
    public const string EnvironmentVariable = "DTK_TEE_DIR";

    /// <summary>Resolves the effective tee directory.</summary>
    /// <param name="config">The tee configuration section.</param>
    /// <param name="teeDirOverride">An explicit override, typically supplied by a test.</param>
    /// <returns>The directory tee logs are written to and read from.</returns>
    public static string Resolve(TeeConfig config, string? teeDirOverride)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (teeDirOverride is not null)
        {
            return teeDirOverride;
        }

        var fromEnvironment = EnvironmentOverride.Read(EnvironmentVariable);
        return fromEnvironment ?? (string.IsNullOrEmpty(config.Directory) ? GetDefault() : config.Directory);
    }

    /// <summary>Returns the platform-default tee directory.</summary>
    /// <returns>The default directory used when nothing is configured.</returns>
    public static string GetDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "dtk", "tee");
    }
}

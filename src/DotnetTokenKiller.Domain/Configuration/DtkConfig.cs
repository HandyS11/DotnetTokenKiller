namespace DotnetTokenKiller.Domain.Configuration;

/// <summary>Root configuration for the dtk tool.</summary>
/// <param name="Tracking">Tracking settings.</param>
/// <param name="Display">Display settings.</param>
/// <param name="Tee">Tee output settings.</param>
public sealed record DtkConfig(
    TrackingConfig Tracking,
    DisplayConfig Display,
    TeeConfig Tee)
{
    /// <summary>Gets the default configuration.</summary>
    public static DtkConfig Default => new(
        new TrackingConfig(),
        new DisplayConfig(),
        new TeeConfig());
}

/// <summary>Configuration for the command tracking feature.</summary>
/// <param name="Enabled">Whether tracking is enabled.</param>
/// <param name="RetentionDays">How many days to retain records.</param>
/// <param name="DbPath">Optional custom database path.</param>
public sealed record TrackingConfig(
    bool Enabled = true,
    int RetentionDays = 90,
    string? DbPath = null);

/// <summary>Configuration for display output.</summary>
/// <param name="Colors">Whether to use ANSI colors.</param>
/// <param name="Emoji">Whether to use emoji in output.</param>
/// <param name="Width">Maximum output width in characters.</param>
public sealed record DisplayConfig(
    bool Colors = true,
    bool Emoji = true,
    int Width = 120);

/// <summary>Supported tee output modes.</summary>
public enum TeeMode
{
    /// <summary>Only tee output for commands that fail (non-zero exit code).</summary>
    Failures = 0,

    /// <summary>Always tee output regardless of exit code.</summary>
    Always = 1,

    /// <summary>Never tee output.</summary>
    Never = 2
}

/// <summary>Configuration for the tee file feature.</summary>
/// <param name="Mode">Tee mode controlling when output is saved.</param>
/// <param name="Directory">Optional custom tee directory.</param>
/// <param name="MaxFiles">Maximum number of tee files to keep.</param>
/// <param name="MaxFileSizeBytes">Maximum size of a single tee file in bytes.</param>
public sealed record TeeConfig(
    TeeMode Mode = TeeMode.Failures,
    string? Directory = null,
    int MaxFiles = 20,
    long MaxFileSizeBytes = 1_048_576L);

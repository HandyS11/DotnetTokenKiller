namespace DotnetTokenKiller.Domain.Configuration;

public sealed record DtkConfig(
    TrackingConfig Tracking,
    DisplayConfig Display,
    TeeConfig Tee)
{
    public static DtkConfig Default => new(
        new TrackingConfig(),
        new DisplayConfig(),
        new TeeConfig());
}

public sealed record TrackingConfig(
    bool Enabled = true,
    int RetentionDays = 90,
    string? DbPath = null);

public sealed record DisplayConfig(
    bool Colors = true,
    bool Emoji = true,
    int Width = 120);

public sealed record TeeConfig(
    string Mode = "failures",
    string? Directory = null,
    int MaxFiles = 20,
    long MaxFileSizeBytes = 1_048_576L);

using DotnetTokenKiller.Domain.Configuration;
using System.Text.Json;

namespace DotnetTokenKiller.Infrastructure.Configuration;

/// <summary>Loads and saves configuration from a JSON file on disk.</summary>
/// <param name="configPath">Path to the JSON configuration file.</param>
public sealed class JsonConfigProvider(string configPath) : IConfigProvider
{
    /// <summary>Initializes a new instance using the default configuration path.</summary>
    public JsonConfigProvider() : this(GetDefaultConfigPath()) { }

    /// <inheritdoc/>
    public async Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return DtkConfig.Default;
            }

            var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize(json, DtkConfigJsonContext.Default.DtkConfig);
            return loaded is null ? DtkConfig.Default : Merge(loaded);
        }
        catch
        {
            return DtkConfig.Default;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Environment.CurrentDirectory;
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config, DtkConfigJsonContext.Default.DtkConfig);
        await File.WriteAllTextAsync(configPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dtk", "config.json");
    }

    private static DtkConfig Merge(DtkConfig loaded)
    {
        var defaults = DtkConfig.Default;
        var tracking = loaded.Tracking;
        var display = loaded.Display;
        var tee = loaded.Tee;
        return new DtkConfig(
            new TrackingConfig(
                tracking?.Enabled ?? defaults.Tracking.Enabled,
                tracking?.RetentionDays ?? defaults.Tracking.RetentionDays,
                tracking?.DbPath ?? defaults.Tracking.DbPath),
            new DisplayConfig(
                display?.Colors ?? defaults.Display.Colors,
                display?.Emoji ?? defaults.Display.Emoji,
                display?.Width ?? defaults.Display.Width),
            new TeeConfig(
                tee?.Mode ?? defaults.Tee.Mode,
                tee?.Directory ?? defaults.Tee.Directory,
                tee?.MaxFiles ?? defaults.Tee.MaxFiles,
                tee?.MaxFileSizeBytes ?? defaults.Tee.MaxFileSizeBytes));
    }
}

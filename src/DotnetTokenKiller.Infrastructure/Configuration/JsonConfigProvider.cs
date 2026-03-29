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
    public DtkConfig Load()
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return DtkConfig.Default;
            }

            var json = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize(json, DtkConfigJsonContext.Default.DtkConfig);
            return loaded is null ? DtkConfig.Default : Validate(Merge(loaded));
        }
        catch
        {
            return DtkConfig.Default;
        }
    }

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
            return loaded is null ? DtkConfig.Default : Validate(Merge(loaded));
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

    /// <inheritdoc/>
    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        return Task.CompletedTask;
    }

    private static string GetDefaultConfigPath()
    {
        var envPath = Environment.GetEnvironmentVariable("DTK_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath.Trim();
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dtk", "config.json");
    }

    /// <summary>Passes <paramref name="value"/> through as nullable, breaking the NRT flow chain.
    /// JSON deserialization can produce null for absent sub-objects even when annotated non-nullable.</summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="value">The value to treat as nullable.</param>
    private static T? AsNullable<T>(T? value) where T : class
    {
        return value;
    }

    private static DtkConfig Merge(DtkConfig loaded)
    {
        var defaults = DtkConfig.Default;
        // JSON deserialization may produce null for absent sub-objects despite non-nullable NRT annotations
        var tracking = AsNullable(loaded.Tracking);
        var display = AsNullable(loaded.Display);
        var tee = AsNullable(loaded.Tee);
        return new DtkConfig(
            new TrackingConfig(
                tracking?.Enabled ?? defaults.Tracking.Enabled,
                tracking?.RetentionDays ?? defaults.Tracking.RetentionDays,
                tracking?.DbPath ?? defaults.Tracking.DbPath,
                tracking?.Tokenizer ?? defaults.Tracking.Tokenizer),
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

    private static DtkConfig Validate(DtkConfig config)
    {
        var tracking = config.Tracking with
        {
            RetentionDays = Math.Max(1, config.Tracking.RetentionDays)
        };
        var display = config.Display with
        {
            Width = Math.Max(40, config.Display.Width)
        };
        var tee = config.Tee with
        {
            MaxFiles = Math.Max(1, config.Tee.MaxFiles),
            MaxFileSizeBytes = Math.Max(0, config.Tee.MaxFileSizeBytes)
        };
        return new DtkConfig(tracking, display, tee);
    }
}

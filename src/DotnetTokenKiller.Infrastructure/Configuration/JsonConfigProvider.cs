using System.Text.Json;
using DotnetTokenKiller.Domain.Configuration;

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
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
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

        // Write to a temp file then atomically move into place so a crash mid-write can never
        // leave a truncated, unparseable config behind. The finally clears the temp file if the
        // write or move fails (or is cancelled) so stray *.tmp files never accumulate.
        var tempPath = configPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
        var envPath = EnvironmentOverride.Read("DTK_CONFIG_PATH");
        if (envPath is not null)
        {
            return envPath;
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
                display?.Emoji ?? defaults.Display.Emoji),
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
        var tee = config.Tee with
        {
            MaxFiles = Math.Max(1, config.Tee.MaxFiles),
            // Floor at 1, not 0: a zero/negative cap would tee an empty log while the hint still
            // promises the full output.
            MaxFileSizeBytes = Math.Max(1, config.Tee.MaxFileSizeBytes)
        };
        return new DtkConfig(tracking, config.Display, tee);
    }
}

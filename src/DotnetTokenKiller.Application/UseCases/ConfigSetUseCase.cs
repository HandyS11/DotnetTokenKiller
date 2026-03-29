using DotnetTokenKiller.Domain.Configuration;
using System.Globalization;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Sets a single configuration value by dot-notation key and persists the result.</summary>
/// <param name="configProvider">The configuration provider.</param>
public sealed class ConfigSetUseCase(IConfigProvider configProvider)
{
    /// <summary>
    /// All supported keys and the valid values for enumeration types.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> SupportedKeys =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tracking.enabled"] = "true|false",
            ["tracking.retentionDays"] = "integer ≥ 1",
            ["tracking.dbPath"] = "file path (empty string to reset to default)",
            ["tracking.tokenizer"] = "Cl100kBase|O200kBase",
            ["display.colors"] = "true|false",
            ["display.emoji"] = "true|false",
            ["display.width"] = "integer ≥ 40",
            ["tee.mode"] = "Failures|Always|Never",
            ["tee.directory"] = "directory path (empty string to reset to default)",
            ["tee.maxFiles"] = "integer ≥ 1",
            ["tee.maxFileSizeBytes"] = "integer ≥ 0"
        };

    /// <summary>
    /// Loads the current config, applies the given key/value update, and saves the result.
    /// </summary>
    /// <param name="key">Dot-notation key (e.g. <c>tracking.enabled</c>).</param>
    /// <param name="value">String representation of the new value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is unknown.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> cannot be parsed for the key's type.</exception>
    public async Task ExecuteAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var updated = Apply(config, key, value);
        await configProvider.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private static DtkConfig Apply(DtkConfig config, string key, string value)
    {
        return key.ToLowerInvariant() switch
        {
            "tracking.enabled" => config with
            {
                Tracking = config.Tracking with
                {
                    Enabled = ParseBool(key, value)
                }
            },
            "tracking.retentiondays" => config with
            {
                Tracking = config.Tracking with
                {
                    RetentionDays = ParseInt(key, value, 1)
                }
            },
            "tracking.dbpath" => config with
            {
                Tracking = config.Tracking with
                {
                    DbPath = NullableString(value)
                }
            },
            "tracking.tokenizer" => config with
            {
                Tracking = config.Tracking with
                {
                    Tokenizer = ParseEnum<TokenizerModel>(key, value)
                }
            },
            "display.colors" => config with
            {
                Display = config.Display with
                {
                    Colors = ParseBool(key, value)
                }
            },
            "display.emoji" => config with
            {
                Display = config.Display with
                {
                    Emoji = ParseBool(key, value)
                }
            },
            "display.width" => config with
            {
                Display = config.Display with
                {
                    Width = ParseInt(key, value, 40)
                }
            },
            "tee.mode" => config with
            {
                Tee = config.Tee with
                {
                    Mode = ParseEnum<TeeMode>(key, value)
                }
            },
            "tee.directory" => config with
            {
                Tee = config.Tee with
                {
                    Directory = NullableString(value)
                }
            },
            "tee.maxfiles" => config with
            {
                Tee = config.Tee with
                {
                    MaxFiles = ParseInt(key, value, 1)
                }
            },
            "tee.maxfilesizebytes" => config with
            {
                Tee = config.Tee with
                {
                    MaxFileSizeBytes = ParseLong(key, value, 0)
                }
            },
            _ => throw new ArgumentException(
                $"Unknown configuration key '{key}'. Supported keys: {string.Join(", ", SupportedKeys.Keys)}",
                nameof(key))
        };
    }

    private static bool ParseBool(string key, string value)
    {
        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        throw new FormatException(
            $"Invalid value '{value}' for '{key}'. Expected: true or false.");
    }

    private static int ParseInt(string key, string value, int min)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException(
                $"Invalid value '{value}' for '{key}'. Expected: integer.");
        }

        if (result < min)
        {
            throw new FormatException(
                $"Invalid value '{value}' for '{key}'. Minimum allowed value is {min}.");
        }

        return result;
    }

    private static long ParseLong(string key, string value, long min)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException(
                $"Invalid value '{value}' for '{key}'. Expected: integer.");
        }

        if (result < min)
        {
            throw new FormatException(
                $"Invalid value '{value}' for '{key}'. Minimum allowed value is {min}.");
        }

        return result;
    }

    private static TEnum ParseEnum<TEnum>(string key, string value) where TEnum : struct, Enum
    {
        if (!char.IsLetter(value[0]))
        {
            var names = string.Join("|", Enum.GetNames<TEnum>());
            throw new FormatException(
                $"Invalid value '{value}' for '{key}'. Expected one of: {names}.");
        }

        if (Enum.TryParse<TEnum>(value, true, out var result) && Enum.IsDefined(result))
        {
            return result;
        }

        var validNames = string.Join("|", Enum.GetNames<TEnum>());
        throw new FormatException(
            $"Invalid value '{value}' for '{key}'. Expected one of: {validNames}.");
    }

    private static string? NullableString(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

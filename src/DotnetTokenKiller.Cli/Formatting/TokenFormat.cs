using System.Globalization;

namespace DotnetTokenKiller.Cli.Formatting;

/// <summary>Formats token counts and durations compactly for the gain dashboard.</summary>
internal static class TokenFormat
{
    /// <summary>Formats a token count with K/M units, one decimal, invariant culture.</summary>
    /// <param name="value">The token count.</param>
    public static string Tokens(long value)
    {
        if (value < 0)
        {
            return "-" + Tokens(-value);
        }

        if (value < 1_000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value < 1_000_000)
        {
            var thousands = value / 1_000.0;

            // 999_999 rounds to "1000.0K"; promote to the next unit instead.
            return Math.Round(thousands, 1) >= 1_000.0
                ? Millions(value)
                : thousands.ToString("F1", CultureInfo.InvariantCulture) + "K";
        }

        return Millions(value);
    }

    /// <summary>Formats a duration with its two most significant units (e.g. 204ms, 2.3s, 38m12s, 2h05m).</summary>
    /// <param name="value">The duration.</param>
    public static string Duration(TimeSpan value)
    {
        if (value.TotalSeconds < 1)
        {
            return ((int)Math.Round(value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) + "ms";
        }

        if (value.TotalMinutes < 1)
        {
            return value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        }

        if (value.TotalHours < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes}m{value.Seconds:D2}s");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}h{value.Minutes:D2}m");
    }

    private static string Millions(long value)
        => (value / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M";
}

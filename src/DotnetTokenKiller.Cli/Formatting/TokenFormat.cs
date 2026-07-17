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
            // -long.MinValue overflows back to itself and would recurse forever; clamp first.
            var magnitude = value == long.MinValue ? long.MaxValue : -value;
            return "-" + Tokens(magnitude);
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
        // 999.6ms rounds to "1000ms"; promote to the next unit instead.
        var wholeMs = (long)Math.Round(value.TotalMilliseconds);
        if (wholeMs < 1_000)
        {
            return wholeMs.ToString(CultureInfo.InvariantCulture) + "ms";
        }

        // 59.97s rounds to "60.0s" at one-decimal precision; promote to the next unit instead.
        if (value.TotalMinutes < 1 && Math.Round(value.TotalSeconds, 1) < 60.0)
        {
            return value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        }

        var totalWholeSeconds = (long)Math.Round(value.TotalSeconds);

        // 59m59.97s rounds its seconds up to the next minute, reaching 60m; promote to hours instead.
        if (value.TotalHours < 1 && totalWholeSeconds < 3_600)
        {
            var minutes = totalWholeSeconds / 60;
            var seconds = totalWholeSeconds % 60;
            return string.Create(CultureInfo.InvariantCulture, $"{minutes}m{seconds:D2}s");
        }

        var totalWholeMinutes = (long)Math.Round(value.TotalSeconds / 60.0);
        var hours = totalWholeMinutes / 60;
        var mins = totalWholeMinutes % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours}h{mins:D2}m");
    }

    private static string Millions(long value)
        => (value / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M";
}

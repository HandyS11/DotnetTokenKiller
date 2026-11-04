using System.Globalization;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// Renders the durations that filters print, always with the invariant culture and never with more
/// precision than the source gave: a decimal comma or a spurious <c>100.00s</c> costs tokens and
/// claims a figure the tool it summarises never reported.
/// </summary>
internal static class DurationFormat
{
    private const double MillisecondsPerSecond = 1_000.0;
    private const long SecondsPerMinute = 60;
    private const long SecondsPerHour = 3_600;

    /// <summary>
    /// Whether one number of a duration, as the tool printed it, is known more finely than whole seconds: it is
    /// given in milliseconds or carries a decimal point. A bare <c>1 s</c> or <c>2 sec</c> may have been
    /// truncated, and a count of minutes or hours says nothing below its own unit.
    /// </summary>
    /// <param name="number">The digits as printed, such as <c>89</c> or <c>2.5</c>.</param>
    /// <param name="unit">The unit as printed, such as <c>ms</c>, <c>s</c>, <c>sec</c> or <c>m</c>.</param>
    /// <returns>Whether the reading counts as exact for <see cref="Format"/>.</returns>
    public static bool IsExactReading(string number, string unit)
    {
        return unit.Equals("ms", StringComparison.OrdinalIgnoreCase) || number.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>
    /// Formats a duration compactly: <c>0.34s</c> below a second, <c>2.34s</c> or <c>12s</c> below a minute,
    /// <c>1m40s</c> from a minute and <c>1h02m03s</c> from an hour.
    /// </summary>
    /// <param name="milliseconds">The duration in milliseconds.</param>
    /// <param name="exact">
    /// Whether the source reported the duration more finely than whole seconds. A source that prints whole
    /// units (VSTest's <c>Duration: 1 s</c>) is not exact, and its figure is shown in whole seconds. Below
    /// a second the figure is always shown to two decimals, and from a minute up whole seconds are all that
    /// is ever shown, so <paramref name="exact"/> matters only between one second and one minute.
    /// </param>
    /// <returns>The text, such as <c>0.34s</c>, <c>12s</c> or <c>1m40s</c>.</returns>
    public static string Format(double milliseconds, bool exact)
    {
        // Whole hundredths, so that 59.996 s promotes to "1m00s" instead of printing "60.00s".
        var centiseconds = (long)Math.Round(milliseconds / 10.0, MidpointRounding.AwayFromZero);
        if (centiseconds < 100 * SecondsPerMinute && (exact || milliseconds < MillisecondsPerSecond))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{centiseconds / 100}.{centiseconds % 100:D2}s");
        }

        var seconds = (long)Math.Round(milliseconds / MillisecondsPerSecond, MidpointRounding.AwayFromZero);
        if (seconds < SecondsPerMinute)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds}s");
        }

        var minutes = seconds % SecondsPerHour / SecondsPerMinute;
        return seconds >= SecondsPerHour
            ? string.Create(CultureInfo.InvariantCulture,
                $"{seconds / SecondsPerHour}h{minutes:D2}m{seconds % SecondsPerMinute:D2}s")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m{seconds % SecondsPerMinute:D2}s");
    }
}

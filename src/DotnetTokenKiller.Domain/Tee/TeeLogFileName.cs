using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// The tee log filename format, <c>{unixMillis}_{uniqueSuffix}_{slug}.log</c>.
/// </summary>
/// <remarks>
/// Shared by the writer and the reader for the same reason as <see cref="TeeLogHeader"/>. The
/// timestamp leads so an ordinal sort of the directory is also a chronological sort, which is what
/// rotation relies on.
/// </remarks>
public static partial class TeeLogFileName
{
    /// <summary>The extension every tee log carries.</summary>
    public const string Extension = ".log";

    /// <summary>Reduces a subcommand name to characters safe in a filename.</summary>
    /// <param name="slug">The subcommand name, e.g. <c>list package</c>.</param>
    /// <returns>The sanitised slug, e.g. <c>list-package</c>.</returns>
    public static string Sanitize(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var safe = NonSafeCharRegex().Replace(slug, "-");
        safe = CollapseHyphensRegex().Replace(safe, "-");
        return safe.Trim('-');
    }

    /// <summary>Builds a tee log filename.</summary>
    /// <param name="timestampUtc">When the run completed.</param>
    /// <param name="uniqueSuffix">A collision-avoiding suffix, typically a compact GUID.</param>
    /// <param name="slug">The subcommand name; sanitised by this method.</param>
    /// <returns>The filename, without a directory.</returns>
    public static string Build(DateTimeOffset timestampUtc, string uniqueSuffix, string slug)
    {
        var millis = timestampUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        return $"{millis}_{Sanitize(uniqueSuffix)}_{Sanitize(slug)}{Extension}";
    }

    /// <summary>Extracts the timestamp and slug from a filename this type produced.</summary>
    /// <param name="fileName">The filename, without a directory.</param>
    /// <param name="timestampUtc">The run timestamp, or <see langword="default"/> on failure.</param>
    /// <param name="slug">The subcommand slug, or an empty string on failure.</param>
    /// <returns><see langword="true"/> when the name matched the format.</returns>
    public static bool TryParse(string fileName, out DateTimeOffset timestampUtc, out string slug)
    {
        timestampUtc = default;
        slug = string.Empty;
        if (string.IsNullOrEmpty(fileName) ||
            !fileName.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = fileName[..^Extension.Length];
        var parts = stem.Split('_');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var millis))
        {
            return false;
        }

        timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(millis);
        slug = parts[2];
        return true;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial Regex NonSafeCharRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseHyphensRegex();
}

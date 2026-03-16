using System.Globalization;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>String formatting utilities.</summary>
public static class TextHelpers
{
    /// <summary>Truncates text to the given length, appending "..." if truncated.</summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLen">Maximum allowed length before truncation.</param>
    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
        {
            return text;
        }

        return $"{text[..maxLen]}...";
    }

    /// <summary>Formats a token count as a human-readable string (e.g. "1.2K").</summary>
    /// <param name="count">The token count to format.</param>
    public static string FormatTokens(int count)
    {
        return count switch
        {
            >= 1_000_000 => $"{count / 1_000_000.0:F1}M",
            >= 1_000 => $"{count / 1_000.0:F1}K",
            _ => count.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Returns the path relative to rootPath, falling back to filename on error.</summary>
    /// <param name="absolutePath">The absolute path to shorten.</param>
    /// <param name="rootPath">The root path to make the path relative to.</param>
    public static string ShortenPath(string absolutePath, string rootPath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return absolutePath;
        }

        try
        {
            var relative = Path.GetRelativePath(rootPath, absolutePath);
            return relative.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(absolutePath);
        }
    }
}

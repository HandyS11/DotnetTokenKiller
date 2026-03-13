using System.Globalization;

namespace DotnetTokenKiller.Application.Helpers;

public static class TextHelpers
{
    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
        {
            return text;
        }

        return $"{text[..maxLen]}...";
    }

    public static string FormatTokens(int count)
    {
        if (count >= 1_000_000)
        {
            return $"{count / 1_000_000.0:F1}M";
        }

        if (count >= 1_000)
        {
            return $"{count / 1_000.0:F1}K";
        }

        return count.ToString(CultureInfo.InvariantCulture);
    }

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

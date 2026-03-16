using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Strips ANSI/VT100 escape sequences from text.</summary>
public static partial class AnsiStrip
{
    /// <summary>Returns the text with all ANSI CSI sequences removed.</summary>
    /// <param name="text">The text to strip.</param>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return CsiPattern().Replace(text, string.Empty);
    }

    // Matches all ANSI/VT100 CSI sequences: ESC [ ... final-byte
    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex CsiPattern();
}

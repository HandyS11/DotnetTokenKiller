using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Helpers;

public static partial class AnsiStrip
{
    // Matches all ANSI/VT100 CSI sequences: ESC [ ... final-byte
    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex CsiPattern();

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return CsiPattern().Replace(text, string.Empty);
    }
}

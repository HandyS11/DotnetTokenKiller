using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Domain.Text;

/// <summary>Strips ANSI/VT100 escape sequences from text.</summary>
public static partial class AnsiStrip
{
    /// <summary>Returns the text with all ANSI escape sequences removed.</summary>
    /// <remarks>
    /// Handles CSI sequences (<c>ESC [</c>), OSC sequences (<c>ESC ]</c> terminated by BEL or ST),
    /// and bare/incomplete escape characters that are not part of a recognised sequence.
    /// </remarks>
    /// <param name="text">The text to strip.</param>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // OSC first so its ESC ] prefix is consumed before the bare-ESC fallback
        var result = OscPattern().Replace(text, string.Empty);
        result = CsiPattern().Replace(result, string.Empty);
        result = BareEscPattern().Replace(result, string.Empty);
        return result;
    }

    // Matches all ANSI/VT100 CSI sequences: ESC [ ... final-byte
    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex CsiPattern();

    /// <summary>
    /// Matches OSC sequences: ESC ] ... BEL  or  ESC ] ... ST (ESC \)
    /// </summary>
    [GeneratedRegex(@"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")]
    private static partial Regex OscPattern();

    /// <summary>
    /// Matches any remaining bare ESC character (e.g. incomplete/truncated sequences)
    /// </summary>
    [GeneratedRegex(@"\x1b")]
    private static partial Regex BareEscPattern();
}

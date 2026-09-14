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

    /// <summary>
    /// Whether <paramref name="text"/> ends inside a CSI or OSC sequence that later text would
    /// complete, so that stripping it alone and stripping it with what follows could differ.
    /// </summary>
    /// <remarks>
    /// Only the last escape matters: the OSC pattern cannot cross an ESC, so every earlier
    /// sequence is either complete or already a bare ESC, whatever follows. A lone trailing ESC
    /// counts as inside, since either sequence could start there. The existing CSI and OSC
    /// patterns can only match at the tail's start because it holds exactly one ESC at index 0.
    /// </remarks>
    /// <param name="text">The text so far.</param>
    public static bool EndsInsideEscapeSequence(ReadOnlySpan<char> text)
    {
        var last = text.LastIndexOf('\x1b');
        if (last < 0)
        {
            return false;
        }

        var tail = text[last..];
        if (tail.Length == 1)
        {
            return true;
        }

        return tail[1] switch
        {
            '[' => !CsiPattern().IsMatch(tail),
            ']' => !OscPattern().IsMatch(tail),
            _ => false
        };
    }
}

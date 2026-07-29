using System.Text;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>UTF-8 helpers shared by the tee writer and the streaming session.</summary>
internal static class Utf8Text
{
    /// <summary>Cuts text to a UTF-8 byte budget on a code-point boundary.</summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="maxBytes">The budget in UTF-8 bytes.</param>
    /// <returns>The longest prefix of <paramref name="text"/> fitting the budget.</returns>
    public static string TruncateToUtf8Bytes(string text, long maxBytes)
    {
        // MaxFileSizeBytes is a byte budget; slicing the string by char count could overshoot the cap
        // (multi-byte runes) or split a rune and emit U+FFFD. Cut on a UTF-8 code-point boundary instead.
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        // Walk runes and stop before the budget is exceeded rather than materializing the whole
        // string as a byte[] — captured output can be very large, and that allocation is the OOM
        // risk the tee path swallows (silently dropping the log). Slicing on a rune boundary also
        // guarantees we never split a multi-byte sequence.
        var chars = 0;
        var runeBytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (runeBytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            runeBytes += rune.Utf8SequenceLength;
            chars += rune.Utf16SequenceLength;
        }

        return text[..chars];
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// The marker line <c>FileTeeSession</c> appends as a truncated body's last line when the byte cap
/// is first hit.
/// </summary>
/// <remarks>
/// This is the sole truncation signal — there is no header field for it. <see cref="TeeLogHeader"/>
/// rejects a header carrying any key it does not recognise (<c>TryReadFields</c>), so adding one
/// would make every log written by a newer dtk unreadable by an older one sharing the same tee
/// directory: hidden from a plain listing, shown with "exit unknown", its header printed as if it
/// were body text. A body line has no such compatibility hazard — an older dtk simply displays it
/// like any other line. Shared by the writer (<c>FileTeeSession</c>) and the reader
/// (<c>TeeLogRenderer</c>) for the same reason <see cref="TeeLogHeader"/> and
/// <see cref="TeeLogFileName"/> are: a divergence between the two would show as a truncated log
/// either failing to warn or a normal log falsely claiming truncation.
/// </remarks>
public static partial class TeeTruncationMarker
{
    /// <summary>Renders the marker text for a given byte cap, without a trailing line feed.</summary>
    /// <param name="maxBodyBytes">The configured byte cap the marker reports.</param>
    /// <returns>The marker line's text, e.g. <c>[dtk: output truncated at 1048576 bytes]</c>.</returns>
    public static string Render(long maxBodyBytes) =>
        $"[dtk: output truncated at {maxBodyBytes.ToString(CultureInfo.InvariantCulture)} bytes]";

    /// <summary>Detects the marker by exact shape, independent of which cap value it reports.</summary>
    /// <param name="line">A single line, with no trailing line feed — typically a body's last line.</param>
    /// <returns><see langword="true"/> when <paramref name="line"/> is a truncation marker.</returns>
    public static bool IsMarkerLine(string line) => LinePattern().IsMatch(line);

    [GeneratedRegex(@"^\[dtk: output truncated at \d+ bytes\]$")]
    private static partial Regex LinePattern();
}

using System.Security.Cryptography;
using System.Text;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Comment syntax used to carry the provenance stamp in a generated artifact.</summary>
internal enum StampStyle
{
    /// <summary>A <c>#</c> line comment, for Python hook scripts.</summary>
    HashComment = 0,

    /// <summary>An HTML comment, for Markdown artifacts such as <c>SKILL.md</c>.</summary>
    HtmlComment = 1
}

/// <summary>
/// A file dtk generates in full and therefore owns end to end.
/// </summary>
/// <param name="Path">Absolute path the artifact is written to.</param>
/// <param name="Body">The generated content, before stamping.</param>
/// <param name="Style">Comment syntax for the stamp line.</param>
/// <param name="LegacySignature">
/// A substring present in every generation of this artifact, used to recognize an unstamped copy
/// left behind by dtk 0.6.0 or earlier. See <c>IntegratorHelpers.WriteGeneratedFileAsync</c>.
/// </param>
internal sealed record GeneratedArtifact(
    string Path,
    string Body,
    StampStyle Style,
    string LegacySignature);

/// <summary>
/// Applies and verifies the provenance line dtk appends to artifacts it generates in full.
/// </summary>
/// <remarks>
/// The stamp answers one question: <em>is this body exactly what some dtk wrote, or did someone
/// edit it?</em> That is what lets an upgrade refresh an untouched artifact without asking while
/// leaving an edited one alone.
/// <para>
/// It carries no version number deliberately. Freshness is decided by comparing the installed body
/// against the current template, never against a version — and a version would make this repo's
/// committed <c>.claude/hooks/dotnet-to-dtk.py</c> change on every release, breaking the test that
/// locks it to the generator for reasons unrelated to the hook's content.
/// </para>
/// <para>
/// The stamp is the last line rather than the first so it need not be positioned below a Python
/// shebang in one artifact and below YAML frontmatter in another; the only per-artifact difference
/// is the comment syntax.
/// </para>
/// </remarks>
internal static class ArtifactStamping
{
    /// <summary>The text introducing the digest on the stamp line.</summary>
    internal const string StampPrefix = "dtk-generated sha256:";

    /// <summary>Length of the lowercase hex SHA-256 digest carried by the stamp.</summary>
    private const int HashLength = 64;

    /// <summary>Appends the provenance line to <paramref name="body"/>.</summary>
    /// <param name="body">The generated content to stamp.</param>
    /// <param name="style">Comment syntax for the stamp line.</param>
    /// <returns>The content as it should be written to disk, stamp included.</returns>
    internal static string Apply(string body, StampStyle style)
    {
        var normalized = Normalize(body);
        var stamp = StampPrefix + ComputeHash(normalized);

        var line = style switch
        {
            StampStyle.HtmlComment => $"<!-- {stamp} -->",
            _ => $"# {stamp}"
        };

        return normalized + line + "\n";
    }

    /// <summary>
    /// The exact characters that may follow the hash on a well-formed stamp line, one per
    /// <see cref="StampStyle"/> — <see cref="StampStyle.HashComment"/> has nothing after the hash
    /// but the newline; <see cref="StampStyle.HtmlComment"/> closes the HTML comment first. Tried
    /// in order regardless of which style produced the content, since <see cref="TryParse"/> is not
    /// told which style it is verifying.
    /// </summary>
    private static readonly string[] LineTerminators = ["\n", " -->\n"];

    /// <summary>
    /// Splits stamped content into the body that was hashed and the digest recorded for it.
    /// </summary>
    /// <remarks>
    /// Requires the stamp line to be the last line of <paramref name="content"/>: the digest only
    /// ever covers the body above the stamp, so anything appended below a genuine stamp — e.g. a
    /// hand-added trailing comment — would otherwise still verify. A stamp that is not the last
    /// line is therefore treated as not a well-formed stamp at all, not merely as one whose body
    /// changed.
    /// </remarks>
    /// <param name="content">File content to inspect.</param>
    /// <param name="body">Set to the content above the stamp line, line endings normalized.</param>
    /// <param name="hash">Set to the digest recorded on the stamp line.</param>
    /// <returns><see langword="true"/> when a well-formed stamp line is present.</returns>
    internal static bool TryParse(string content, out string body, out string hash)
    {
        body = string.Empty;
        hash = string.Empty;

        var normalized = content.ReplaceLineEndings("\n");
        var prefixIndex = normalized.LastIndexOf(StampPrefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return false;
        }

        var hashStart = prefixIndex + StampPrefix.Length;
        if (normalized.Length < hashStart + HashLength)
        {
            return false;
        }

        var candidate = normalized.Substring(hashStart, HashLength);
        if (!candidate.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        var tail = normalized[(hashStart + HashLength)..];
        if (!LineTerminators.Contains(tail, StringComparer.Ordinal))
        {
            return false;
        }

        body = normalized[..(normalized.LastIndexOf('\n', prefixIndex) + 1)];
        hash = candidate;
        return true;
    }

    /// <summary>
    /// Whether the content carries a stamp whose digest still matches the body above it — i.e. the
    /// body is untouched output of some dtk version, with nothing added below the stamp either.
    /// </summary>
    /// <param name="content">File content to verify.</param>
    internal static bool IsAuthentic(string content)
        => TryParse(content, out var body, out var hash) && ComputeHash(body) == hash;

    /// <summary>
    /// Whether the stamp's introductory text appears anywhere in <paramref name="content"/>,
    /// regardless of whether the rest of the stamp is well-formed.
    /// </summary>
    /// <remarks>
    /// This is the test for "has dtk ever stamped this file", used to tell a pre-stamping legacy
    /// artifact (no stamp at all) apart from a stamped artifact that has since been damaged —
    /// truncated, hand-edited, or had text appended below the stamp line. <see cref="TryParse"/>
    /// returns <see langword="false"/> for both, so keying the legacy check on <c>!TryParse(...)</c>
    /// would misclassify a damaged stamped artifact as legacy and refresh it without <c>--force</c>,
    /// silently discarding whatever the damage was — exactly the data loss stamping exists to
    /// prevent. Keying it on <c>!HasStamp(...)</c> instead only lets truly unstamped content take
    /// the legacy path.
    /// </remarks>
    /// <param name="content">File content to check.</param>
    internal static bool HasStamp(string content)
        => content.ReplaceLineEndings("\n").Contains(StampPrefix, StringComparison.Ordinal);

    /// <summary>Computes the lowercase hex SHA-256 of the normalized body.</summary>
    /// <param name="body">The content to digest.</param>
    internal static string ComputeHash(string body)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(body))));

    /// <summary>
    /// Normalizes to LF and guarantees a trailing newline, so a CRLF checkout does not read as
    /// tampering and the stamp always lands on its own line.
    /// </summary>
    /// <param name="body">The content to normalize.</param>
    private static string Normalize(string body)
    {
        var normalized = body.ReplaceLineEndings("\n");
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}

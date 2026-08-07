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
    /// Splits stamped content into the body that was hashed and the digest recorded for it.
    /// </summary>
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

        body = normalized[..(normalized.LastIndexOf('\n', prefixIndex) + 1)];
        hash = candidate;
        return true;
    }

    /// <summary>
    /// Whether the content carries a stamp whose digest still matches the body above it — i.e. the
    /// body is untouched output of some dtk version.
    /// </summary>
    /// <param name="content">File content to verify.</param>
    internal static bool IsAuthentic(string content)
        => TryParse(content, out var body, out var hash) && ComputeHash(body) == hash;

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

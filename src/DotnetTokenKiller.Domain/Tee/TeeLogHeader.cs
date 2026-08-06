using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// The metadata block written at the head of a tee log file, making the file self-describing.
/// </summary>
/// <remarks>
/// The format is a strict line-based grammar: a version line, one <c># key: value</c> line per
/// field, then a <c>---</c> delimiter, then the raw body. It lives in Domain because
/// <c>FileTeeService</c> writes it and <c>FileTeeLogStore</c> reads it — a divergence between the
/// two would present as logs that exist but cannot be listed.
/// </remarks>
/// <param name="CommandLine">The command line that produced the output, for display.</param>
/// <param name="ProjectPath">The working directory the command ran in.</param>
/// <param name="ExitCode">
/// The producing command's exit code, or <see langword="null"/> when the run has not finished.
/// This is the sole in-memory representation of completion: <see cref="Status"/> derives from it,
/// so the two can never disagree.
/// </param>
/// <param name="Source">Whether dtk ran the command or received its output on stdin.</param>
/// <param name="TimestampUtc">When the run started, i.e. when the tee session was opened.</param>
public sealed record TeeLogHeader(
    string CommandLine,
    string ProjectPath,
    int? ExitCode,
    RunSource Source,
    DateTimeOffset TimestampUtc)
{
    /// <summary>The first line of a v2 header.</summary>
    public const string VersionLine = "# dtk-log v2";

    /// <summary>The first line of a v1 header, still accepted when reading.</summary>
    public const string LegacyVersionLine = "# dtk-log v1";

    /// <summary>The line separating the header from the raw body.</summary>
    public const string Delimiter = "---";

    /// <summary>Width of the status value, sized to the longest word it can hold.</summary>
    private const int StatusFieldWidth = 8;

    /// <summary>Width of the exit value, sized to <c>-2147483648</c>.</summary>
    private const int ExitFieldWidth = 11;

    private const string RunningText = "running";
    private const string CompleteText = "complete";

    private const string CommandKey = "command";
    private const string CwdKey = "cwd";
    private const string ExitKey = "exit";
    private const string SourceKey = "source";
    private const string StatusKey = "status";
    private const string UtcKey = "utc";

    /// <summary>Whether the run this log came from finished.</summary>
    public TeeLogStatus Status => ExitCode is null ? TeeLogStatus.Running : TeeLogStatus.Complete;

    /// <summary>
    /// Renders the status and exit lines, which sit last and adjacent so finalizing a log is one
    /// contiguous overwrite at a known byte offset.
    /// </summary>
    /// <param name="exitCode">The exit code, or <see langword="null"/> for a run still in flight.</param>
    /// <returns>
    /// Both lines including their trailing line feeds. The length is identical for every input —
    /// the in-place overwrite depends on it, and <c>RenderStatusAndExit_ProducesTheSameLength…</c>
    /// guards it.
    /// </returns>
    public static string RenderStatusAndExit(int? exitCode)
    {
        var status = (exitCode is null ? RunningText : CompleteText).PadRight(StatusFieldWidth);
        var exit = (exitCode?.ToString(CultureInfo.InvariantCulture) ?? "-").PadRight(ExitFieldWidth);
        return $"# {StatusKey}: {status}\n# {ExitKey}:   {exit}\n";
    }

    /// <summary>Renders the header, including the trailing delimiter line.</summary>
    /// <returns>The header text; the body is appended directly after it.</returns>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(VersionLine).Append('\n')
            .Append("# ").Append(CommandKey).Append(": ").Append(Flatten(CommandLine)).Append('\n')
            .Append("# ").Append(CwdKey).Append(": ").Append(Flatten(ProjectPath)).Append('\n')
            .Append("# ").Append(SourceKey).Append(": ").Append(Source.ToString()).Append('\n')
            .Append("# ").Append(UtcKey).Append(": ")
            .Append(TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(RenderStatusAndExit(ExitCode))
            .Append(Delimiter).Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a v1 or v2 header from the start of <paramref name="text"/>.</summary>
    /// <param name="text">
    /// The file's leading text. It need not be the whole file, but it must contain the delimiter —
    /// a read that stopped short of it is rejected rather than yielding a partial header.
    /// </param>
    /// <param name="header">The parsed header, or <see langword="null"/> when parsing failed.</param>
    /// <returns><see langword="true"/> when a complete header was present.</returns>
    public static bool TryParse(string text, out TeeLogHeader header)
    {
        header = null!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var lines = text.Split('\n');
        var version = lines[0].TrimEnd('\r');
        if (version != VersionLine && version != LegacyVersionLine)
        {
            return false;
        }

        var fields = new HeaderFields();
        if (!TryReadFields(lines, fields))
        {
            return false;
        }

        if (fields.Command is null || fields.Cwd is null || fields.Exit is null ||
            fields.Source is null || fields.Utc is null)
        {
            return false;
        }

        if (!TryParseExit(fields.Exit, fields.Status, out var exitCode))
        {
            return false;
        }

        if (!Enum.TryParse<RunSource>(fields.Source, ignoreCase: false, out var runSource) ||
            !DateTimeOffset.TryParse(fields.Utc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return false;
        }

        header = new TeeLogHeader(fields.Command, fields.Cwd, exitCode, runSource, timestamp);
        return true;
    }

    /// <summary>
    /// Reads the <c># key: value</c> lines into <paramref name="fields"/>, stopping at the
    /// delimiter. Whether the fields it collected are sufficient is the caller's judgement; this
    /// only rejects text that is not a well-formed header at all.
    /// </summary>
    /// <param name="lines">The file's leading text, split on line feeds. Index 0 is the version line.</param>
    /// <param name="fields">Receives the raw values, one per recognised key.</param>
    /// <returns>
    /// <see langword="false"/> on a malformed line, an unrecognised key, or a run of lines that
    /// never reaches the delimiter.
    /// </returns>
    private static bool TryReadFields(string[] lines, HeaderFields fields)
    {
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line == Delimiter)
            {
                return true;
            }

            if (!line.StartsWith("# ", StringComparison.Ordinal))
            {
                return false;
            }

            var rest = line[2..];
            var separator = rest.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                return false;
            }

            if (!fields.TryAssign(rest[..separator], rest[(separator + 1)..].TrimStart(' ')))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The raw, still-unvalidated field values collected while scanning a header.</summary>
    private sealed class HeaderFields
    {
        public string? Command { get; private set; }

        public string? Cwd { get; private set; }

        public string? Exit { get; private set; }

        public string? Source { get; private set; }

        public string? Status { get; private set; }

        public string? Utc { get; private set; }

        /// <summary>Stores one header line's value under its key.</summary>
        /// <param name="key">The key text, between <c>"# "</c> and the colon.</param>
        /// <param name="value">The value text, already stripped of its leading spaces.</param>
        /// <returns><see langword="false"/> when the key is not one this version writes.</returns>
        public bool TryAssign(string key, string value)
        {
            switch (key)
            {
                case CommandKey:
                    Command = value;
                    break;
                case CwdKey:
                    Cwd = value;
                    break;
                case ExitKey:
                    // Trimmed at both ends: this field is padded to a fixed width so it can be
                    // overwritten in place. The others are not trimmed at the end, because Flatten
                    // can legitimately leave a trailing space in a command line.
                    Exit = value.TrimEnd(' ');
                    break;
                case SourceKey:
                    Source = value;
                    break;
                case StatusKey:
                    Status = value.TrimEnd(' ');
                    break;
                case UtcKey:
                    Utc = value;
                    break;
                default:
                    return false;
            }

            return true;
        }
    }

    /// <summary>Returns the body of a tee log, without its header.</summary>
    /// <param name="fileText">The complete file text.</param>
    /// <returns>
    /// The text after the delimiter line, or <paramref name="fileText"/> unchanged when it carries
    /// no header (a log written before headers existed).
    /// </returns>
    public static string StripHeader(string fileText)
    {
        ArgumentNullException.ThrowIfNull(fileText);
        if (!TryParse(fileText, out _))
        {
            return fileText;
        }

        var marker = "\n" + Delimiter + "\n";
        var index = fileText.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            // CRLF variant; TryParse accepted it, so one of the two forms must be present.
            marker = "\n" + Delimiter + "\r\n";
            index = fileText.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return fileText;
            }
        }

        return fileText[(index + marker.Length)..];
    }

    /// <summary>Resolves the exit field against the status field, rejecting any disagreement.</summary>
    /// <param name="exit">The raw exit value: a decimal integer, or <c>-</c> for a run in flight.</param>
    /// <param name="status">The raw status value, or <see langword="null"/> in a v1 header.</param>
    /// <param name="exitCode">The parsed exit code, or <see langword="null"/> when still running.</param>
    /// <returns><see langword="true"/> when the two fields agree and parse.</returns>
    private static bool TryParseExit(string exit, string? status, out int? exitCode)
    {
        exitCode = null;

        // v1 had no status line and could only be written after the process exited.
        if (status is null)
        {
            if (!int.TryParse(exit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v1Exit))
            {
                return false;
            }

            exitCode = v1Exit;
            return true;
        }

        if (status == RunningText)
        {
            return exit == "-";
        }

        if (status != CompleteText)
        {
            return false;
        }

        if (!int.TryParse(exit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedExit))
        {
            return false;
        }

        exitCode = parsedExit;
        return true;
    }

    /// <summary>Flattens line breaks so a value can never split its own header line.</summary>
    /// <param name="value">The raw field value.</param>
    /// <returns>The value with CR and LF replaced by spaces.</returns>
    private static string Flatten(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}

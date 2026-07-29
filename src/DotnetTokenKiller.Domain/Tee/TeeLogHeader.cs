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
/// <param name="ExitCode">The producing command's exit code.</param>
/// <param name="Source">Whether dtk ran the command or received its output on stdin.</param>
/// <param name="TimestampUtc">When the run completed.</param>
public sealed record TeeLogHeader(
    string CommandLine,
    string ProjectPath,
    int ExitCode,
    RunSource Source,
    DateTimeOffset TimestampUtc)
{
    /// <summary>The first line of a v1 header.</summary>
    public const string VersionLine = "# dtk-log v1";

    /// <summary>The line separating the header from the raw body.</summary>
    public const string Delimiter = "---";

    private const string CommandKey = "command";
    private const string CwdKey = "cwd";
    private const string ExitKey = "exit";
    private const string SourceKey = "source";
    private const string UtcKey = "utc";

    /// <summary>Renders the header, including the trailing delimiter line.</summary>
    /// <returns>The header text; the body is appended directly after it.</returns>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(VersionLine).Append('\n')
            .Append("# ").Append(CommandKey).Append(": ").Append(Flatten(CommandLine)).Append('\n')
            .Append("# ").Append(CwdKey).Append(": ").Append(Flatten(ProjectPath)).Append('\n')
            .Append("# ").Append(ExitKey).Append(": ")
            .Append(ExitCode.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("# ").Append(SourceKey).Append(": ").Append(Source.ToString()).Append('\n')
            .Append("# ").Append(UtcKey).Append(": ")
            .Append(TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(Delimiter).Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a v1 header from the start of <paramref name="text"/>.</summary>
    /// <param name="text">
    /// The file's leading text. It need not be the whole file, but it must contain the delimiter —
    /// a read that stopped short of it is rejected rather than yielding a partial header.
    /// </param>
    /// <param name="header">The parsed header, or <see langword="null"/> when parsing failed.</param>
    /// <returns><see langword="true"/> when a complete v1 header was present.</returns>
    public static bool TryParse(string text, out TeeLogHeader header)
    {
        header = null!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var lines = text.Split('\n');
        if (lines[0].TrimEnd('\r') != VersionLine)
        {
            return false;
        }

        string? command = null;
        string? cwd = null;
        string? exit = null;
        string? source = null;
        string? utc = null;
        var sawDelimiter = false;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line == Delimiter)
            {
                sawDelimiter = true;
                break;
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

            var key = rest[..separator];
            var value = rest[(separator + 1)..].TrimStart(' ');
            switch (key)
            {
                case CommandKey:
                    command = value;
                    break;
                case CwdKey:
                    cwd = value;
                    break;
                case ExitKey:
                    exit = value;
                    break;
                case SourceKey:
                    source = value;
                    break;
                case UtcKey:
                    utc = value;
                    break;
                default:
                    return false;
            }
        }

        if (!sawDelimiter || command is null || cwd is null || exit is null || source is null || utc is null)
        {
            return false;
        }

        if (!int.TryParse(exit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode) ||
            !Enum.TryParse<RunSource>(source, ignoreCase: false, out var runSource) ||
            !DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return false;
        }

        header = new TeeLogHeader(command, cwd, exitCode, runSource, timestamp);
        return true;
    }

    /// <summary>Returns the body of a tee log, without its header.</summary>
    /// <param name="fileText">The complete file text.</param>
    /// <returns>
    /// The text after the delimiter line, or <paramref name="fileText"/> unchanged when it carries
    /// no v1 header (a log written before headers existed).
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

    /// <summary>Flattens line breaks so a value can never split its own header line.</summary>
    /// <param name="value">The raw field value.</param>
    /// <returns>The value with CR and LF replaced by spaces.</returns>
    private static string Flatten(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}

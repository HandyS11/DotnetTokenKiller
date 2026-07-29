namespace DotnetTokenKiller.Domain.Tee;

/// <summary>One tee log file discovered on disk.</summary>
/// <param name="FilePath">The absolute path to the log file.</param>
/// <param name="Header">
/// The parsed header, or <see langword="null"/> for a log written before headers existed. A null
/// header means the project the log came from is unknown, not that the log is unreadable.
/// </param>
/// <param name="SizeBytes">The file's size on disk, header included.</param>
/// <param name="TimestampUtc">When the run started, taken from the filename.</param>
/// <param name="Slug">The sanitised subcommand name, or an empty string when the name is unrecognised.</param>
public sealed record TeeLogEntry(
    string FilePath,
    TeeLogHeader? Header,
    long SizeBytes,
    DateTimeOffset TimestampUtc,
    string Slug);

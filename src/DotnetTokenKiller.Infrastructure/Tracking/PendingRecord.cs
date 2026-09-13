using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>
/// One run as it waits in the journal: the flat fields of <see cref="CommandRecord"/>, with strings
/// where the database has strings, plus a version so a later dtk can tell an old file's shape.
/// </summary>
/// <remarks>
/// A class with init-only properties rather than a positional record: twelve constructor parameters
/// would trip S107, and the serializer binds properties either way.
/// </remarks>
internal sealed class PendingRecord
{
    /// <summary>The shape this dtk writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The shape version the file was written with.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>When the command ran, round-trip format, UTC.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>The dotnet subcommand name.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>The working directory when the command ran.</summary>
    public string ProjectPath { get; init; } = string.Empty;

    /// <summary>Estimated tokens in the raw output.</summary>
    public int InputTokens { get; init; }

    /// <summary>Estimated tokens in the filtered output.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Tokens saved by filtering.</summary>
    public int SavedTokens { get; init; }

    /// <summary>Percentage of tokens saved.</summary>
    public double SavingsPercentage { get; init; }

    /// <summary>Wall-clock time of the command, in milliseconds.</summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>Whether the command exited with code 0.</summary>
    public bool Success { get; init; } = true;

    /// <summary>The <see cref="RunOutcome"/> name.</summary>
    public string Outcome { get; init; } = nameof(RunOutcome.Filtered);

    /// <summary>The <see cref="RunSource"/> name.</summary>
    public string Source { get; init; } = nameof(RunSource.Run);

    /// <summary>The journal shape of <paramref name="record"/>.</summary>
    /// <param name="record">The record to write.</param>
    public static PendingRecord From(CommandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new PendingRecord
        {
            Timestamp = record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Command = record.Command,
            ProjectPath = record.ProjectPath,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            SavedTokens = record.SavedTokens,
            SavingsPercentage = record.SavingsPercentage,
            ExecutionTimeMs = record.ExecutionTime.TotalMilliseconds,
            Success = record.Success,
            Outcome = record.Outcome.ToString(),
            Source = record.Source.ToString()
        };
    }

    /// <summary>The record this file describes; unknown outcome or source names degrade as the database reader's do.</summary>
    public CommandRecord ToCommandRecord()
    {
        var outcome = Enum.TryParse<RunOutcome>(Outcome, ignoreCase: true, out var parsedOutcome)
            ? parsedOutcome
            : RunOutcome.Filtered;
        var source = Enum.TryParse<RunSource>(Source, ignoreCase: true, out var parsedSource)
            ? parsedSource
            : RunSource.Run;

        return new CommandRecord(
            DateTimeOffset.ParseExact(Timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Command,
            ProjectPath,
            new TokenStatistics(InputTokens, OutputTokens, SavedTokens, SavingsPercentage),
            TimeSpan.FromMilliseconds(ExecutionTimeMs))
        {
            Success = Success,
            Outcome = outcome,
            Source = source
        };
    }
}

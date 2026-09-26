using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Runs a tracker's filtered queries and maps their rows to domain results.</summary>
/// <remarks>
/// Owns no connection or semaphore of its own: <see cref="SqliteTracker"/> owns both and passes
/// them here, along with a delegate that folds the journal under the same semaphore before every
/// query runs, exactly as <see cref="ExecuteWithFilterAsync{T}"/> did as a method on the tracker.
/// </remarks>
/// <param name="semaphore">The tracker's semaphore, held for the duration of each query.</param>
/// <param name="connectionAccessor">Returns the tracker's open connection, or throws before initialization.</param>
/// <param name="ensureInitializedAsync">Opens the connection and creates the schema if not already done.</param>
/// <param name="foldBeforeReadAsync">Folds the journal, waiting for a concurrent fold to finish.</param>
internal sealed class SqliteQueryReader(
    SemaphoreSlim semaphore,
    Func<SqliteConnection> connectionAccessor,
    Func<CancellationToken, Task> ensureInitializedAsync,
    Func<CancellationToken, Task> foldBeforeReadAsync)
{
    /// <summary>
    /// Initializes the tracker, folds the journal, then runs <paramref name="sql"/> with the standard
    /// since/path/command filter parameters (and an optional row limit) and maps its rows with
    /// <paramref name="readResultsAsync"/>.
    /// </summary>
    /// <param name="days">How many days back <c>@since</c> should cover.</param>
    /// <param name="projectPath">Optional project path filter (<c>@path</c>), or <see langword="null"/> for all projects.</param>
    /// <param name="commandFilter">Optional command filter (<c>@cmd</c>), or <see langword="null"/> for all commands.</param>
    /// <param name="sql">The query text; must reference <c>@since</c>, <c>@path</c>, <c>@cmd</c> and, if <paramref name="limit"/> is set, <c>@limit</c>.</param>
    /// <param name="readResultsAsync">Maps the executed reader to the result.</param>
    /// <param name="limit">The value for <c>@limit</c>, or <see langword="null"/> if the query does not use it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The result <paramref name="readResultsAsync"/> produces.</typeparam>
    public async Task<T> ExecuteWithFilterAsync<T>(
        int days,
        string? projectPath,
        string? commandFilter,
        string sql,
        Func<SqliteCommand, CancellationToken, Task<T>> readResultsAsync,
        int? limit,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ensureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await foldBeforeReadAsync(cancellationToken).ConfigureAwait(false);
            var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = CreateCommand();
#pragma warning restore CA2007
#pragma warning disable CA2100 // sql is a caller-supplied constant literal; no user input reaches this parameter
            cmd.CommandText = sql;
#pragma warning restore CA2100
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cmd", (object?)commandFilter ?? DBNull.Value);
            if (limit.HasValue)
            {
                cmd.Parameters.AddWithValue("@limit", limit.Value);
            }

            return await readResultsAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Maps a <c>GetSummaryAsync</c> query's rows into a <see cref="GainSummary"/>.</summary>
    /// <param name="cmd">The executed command to read rows from.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<GainSummary> ReadSummaryAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var totalCommands = 0;
        long totalInput = 0;
        long totalOutput = 0;
        long totalSaved = 0;
        var totalMs = 0.0;

        // Accumulate per-command, per-status rows before building CommandGainDetail
        var grouped =
            new Dictionary<string, List<(bool Success, int RunCount, long SumInput, long SumOutput, long SumSaved, double
                AvgPct, double SumMs)>>(StringComparer.Ordinal);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var cmdName = reader.GetString(0);
            var success = reader.GetInt32(1) != 0;
            var runCount = reader.GetInt32(2);
            var sumInput = reader.GetInt64(3);
            var sumOutput = reader.GetInt64(4);
            var sumSaved = reader.GetInt64(5);
            var avgPct = reader.GetDouble(6);
            var sumMs = reader.GetDouble(7);

            totalCommands += runCount;
            totalInput += sumInput;
            totalOutput += sumOutput;
            totalSaved += sumSaved;
            totalMs += sumMs;

            if (!grouped.TryGetValue(cmdName, out var rows))
            {
                rows = [];
                grouped[cmdName] = rows;
            }

            rows.Add((success, runCount, sumInput, sumOutput, sumSaved, avgPct, sumMs));
        }

        var commandDetails = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal);
        foreach (var (cmdName, rows) in grouped)
        {
            CommandGainDetail? successDetail = null;
            CommandGainDetail? failureDetail = null;
            var totalRunsCmd = 0;
            long totalInputCmd = 0;
            long totalOutputCmd = 0;
            long totalSavedCmd = 0;
            var weightedPctSum = 0.0;
            var totalMsCmd = 0.0;

            foreach (var (success, runCount, sumInput, sumOutput, sumSaved, avgPct, sumMs) in rows)
            {
                var statusDetail = new CommandGainDetail(runCount, sumInput, sumOutput, sumSaved, avgPct,
                    TotalExecutionTime: TimeSpan.FromMilliseconds(sumMs));
                if (success)
                {
                    successDetail = statusDetail;
                }
                else
                {
                    failureDetail = statusDetail;
                }

                totalRunsCmd += runCount;
                totalInputCmd += sumInput;
                totalOutputCmd += sumOutput;
                totalSavedCmd += sumSaved;
                weightedPctSum += runCount * avgPct;
                totalMsCmd += sumMs;
            }

            // Run-count-weighted average of the per-status SQL AVG(savings_percentage) values,
            // keeping the same per-run-average semantics as SuccessDetail/FailureDetail.
            var avgPctCmd = totalRunsCmd > 0 ? weightedPctSum / totalRunsCmd : 0.0;
            commandDetails[cmdName] = new CommandGainDetail(totalRunsCmd, totalInputCmd, totalOutputCmd, totalSavedCmd,
                avgPctCmd, successDetail, failureDetail, TimeSpan.FromMilliseconds(totalMsCmd));
        }

        var averagePct = totalInput > 0 ? (double)totalSaved / totalInput * 100.0 : 0.0;
        return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, commandDetails,
            TimeSpan.FromMilliseconds(totalMs));
    }

    /// <summary>Maps a <c>GetHistoryAsync</c> query's rows into <see cref="CommandRecord"/> values.</summary>
    /// <param name="cmd">The executed command to read rows from.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IReadOnlyList<CommandRecord>> ReadHistoryAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<CommandRecord>();

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // An outcome written by a newer dtk that this build does not know is treated as
            // Filtered rather than crashing the report. Case-insensitive so a value differing
            // only in case (e.g. a manual database edit) still parses instead of falling back.
            var outcome = Enum.TryParse<RunOutcome>(reader.GetString(9), ignoreCase: true, out var parsed)
                ? parsed
                : RunOutcome.Filtered;

            // A source written by a newer dtk that this build does not know reads as Run, matching
            // how an unknown outcome degrades to Filtered rather than crashing the report.
            var source = Enum.TryParse<RunSource>(reader.GetString(10), ignoreCase: true, out var parsedSource)
                ? parsedSource
                : RunSource.Run;

            results.Add(new CommandRecord(
                DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetString(1),
                reader.GetString(2),
                new TokenStatistics(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetDouble(6)),
                TimeSpan.FromMilliseconds(reader.GetDouble(7)))
            {
                Success = reader.GetInt32(8) != 0,
                Outcome = outcome,
                Source = source
            });
        }

        return results;
    }

    /// <summary>Maps a <c>GetCoverageAsync</c> query's rows into a <see cref="CoverageSummary"/>.</summary>
    /// <param name="cmd">The executed command to read rows from.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<CoverageSummary> ReadCoverageAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var entries = new List<CoverageDetail>();
        var totalRuns = 0;
        long totalUnfiltered = 0;

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var outcome = Enum.TryParse<RunOutcome>(reader.GetString(1), ignoreCase: true, out var parsed)
                ? parsed
                : RunOutcome.Filtered;
            var source = Enum.TryParse<RunSource>(reader.GetString(2), ignoreCase: true, out var parsedSource)
                ? parsedSource
                : RunSource.Run;
            var runCount = reader.GetInt32(3);
            var inputTokens = reader.GetInt64(4);

            entries.Add(new CoverageDetail(
                reader.GetString(0),
                outcome,
                source,
                runCount,
                inputTokens,
                TimeSpan.FromMilliseconds(reader.GetDouble(5))));

            totalRuns += runCount;
            if (RunOutcomes.IsPassthrough(outcome))
            {
                totalUnfiltered += inputTokens;
            }
        }

        return new CoverageSummary(entries, totalRuns, totalUnfiltered);
    }

    private SqliteCommand CreateCommand() => connectionAccessor().CreateCommand();
}

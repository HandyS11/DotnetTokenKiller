using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Persists command tracking records in a SQLite database.</summary>
/// <param name="connectionString">The SQLite connection string.</param>
/// <param name="defaultRetentionDays">Number of days to retain records before automatic cleanup.</param>
public sealed class SqliteTracker(string connectionString, int defaultRetentionDays = 90)
    : ITracker, IDisposable, IAsyncDisposable
{
    private const int HistoryLimit = 500;
    private readonly SqliteConnection _connection = new(connectionString);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _initialized;

    /// <summary>Asynchronously releases managed resources.</summary>
    public async ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Releases managed resources.</summary>
    public void Dispose()
    {
        _semaphore.Dispose();
        _connection.Dispose();
    }

    /// <inheritdoc/>
    public async Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
            cmd.CommandText = """
                              INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                                  saved_tokens, savings_percentage, execution_time_ms, success)
                              VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms, @success)
                              """;
            cmd.Parameters.AddWithValue("@ts",
                record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@cmd", record.Command);
            cmd.Parameters.AddWithValue("@path", record.ProjectPath);
            cmd.Parameters.AddWithValue("@in", record.InputTokens);
            cmd.Parameters.AddWithValue("@out", record.OutputTokens);
            cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
            cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
            cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT command, success,
                                  COUNT(*) as run_count,
                                  SUM(input_tokens) as total_input,
                                  SUM(output_tokens) as total_output,
                                  SUM(saved_tokens) as total_saved,
                                  AVG(savings_percentage) as avg_pct,
                                  SUM(execution_time_ms) as total_ms
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           GROUP BY command, success
                           ORDER BY command, success DESC
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadSummaryAsync, null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT timestamp, command, project_path, input_tokens, output_tokens,
                                  saved_tokens, savings_percentage, execution_time_ms, success
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           ORDER BY timestamp DESC
                           LIMIT @limit
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadHistoryAsync, HistoryLimit,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await CleanupCoreAsync(retentionDays, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
            cmd.CommandText = "DELETE FROM commands";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Returns the default tracking-database path used when no override is configured.</summary>
    /// <returns>The platform-default database path.</returns>
    public static string GetDefaultDbPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "dtk", "tracking.db");
    }

    private static void EnsureDataDirectory(string cs)
    {
        if (cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var csb = new SqliteConnectionStringBuilder(cs);
        if (string.IsNullOrWhiteSpace(csb.DataSource) || csb.DataSource == ":memory:")
        {
            return;
        }

        var dir = Path.GetDirectoryName(csb.DataSource);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        EnsureDataDirectory(connectionString);
        await _connection.OpenAsync(ct).ConfigureAwait(false);
        await InitializeSchemaAsync(ct).ConfigureAwait(false);

        // A one-shot CLI runs as a single process with a single tracker instance, so purge
        // expired rows once here at startup. A single delete per process is cheap and replaces
        // the old per-insert counter cleanup that could never fire during a one-command run.
        await CleanupCoreAsync(defaultRetentionDays, ct).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var createCmd = _connection.CreateCommand();
#pragma warning restore CA2007
        createCmd.CommandText = """
                                CREATE TABLE IF NOT EXISTS commands (
                                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    timestamp TEXT NOT NULL,
                                    command TEXT NOT NULL,
                                    project_path TEXT NOT NULL,
                                    input_tokens INTEGER NOT NULL,
                                    output_tokens INTEGER NOT NULL,
                                    saved_tokens INTEGER NOT NULL,
                                    savings_percentage REAL NOT NULL,
                                    execution_time_ms REAL NOT NULL,
                                    success INTEGER NOT NULL DEFAULT 1
                                );
                                CREATE INDEX IF NOT EXISTS idx_commands_timestamp ON commands(timestamp);
                                CREATE INDEX IF NOT EXISTS idx_commands_project_path ON commands(project_path);
                                """;
        await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Migration for databases created before the success column existed. Fresh databases
        // already have it (see CREATE TABLE above) so this ALTER only runs on legacy files.
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var checkCmd = _connection.CreateCommand();
#pragma warning restore CA2007
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('commands') WHERE name='success'";
        var columnExists = (long)(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
        if (!columnExists)
        {
            try
            {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
                await using var alterCmd = _connection.CreateCommand();
#pragma warning restore CA2007
                alterCmd.CommandText = "ALTER TABLE commands ADD COLUMN success INTEGER NOT NULL DEFAULT 1";
                await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // A concurrent initializer added the column between our pragma check and this ALTER.
                // The column now exists, which is all we required — the losing racer is fine.
            }
        }
    }

    private async Task<T> ExecuteWithFilterAsync<T>(
        int days,
        string? projectPath,
        string? commandFilter,
        string sql,
        Func<SqliteCommand, CancellationToken, Task<T>> readResultsAsync,
        int? limit,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = _connection.CreateCommand();
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
            _semaphore.Release();
        }
    }

    private static async Task<GainSummary> ReadSummaryAsync(SqliteCommand cmd, CancellationToken ct)
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

    private static async Task<IReadOnlyList<CommandRecord>> ReadHistoryAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<CommandRecord>();

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new CommandRecord(
                DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetString(1),
                reader.GetString(2),
                new TokenStatistics(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetDouble(6)),
                TimeSpan.FromMilliseconds(reader.GetDouble(7)),
                reader.GetInt32(8) != 0));
        }

        return results;
    }

    private async Task CleanupCoreAsync(int days, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
        cmd.CommandText = "DELETE FROM commands WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

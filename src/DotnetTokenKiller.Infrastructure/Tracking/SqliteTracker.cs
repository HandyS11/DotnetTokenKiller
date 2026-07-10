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
    private const int CleanupIntervalInserts = 50;
    private readonly SqliteConnection _connection = new(connectionString);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _initialized;
    private int _insertsSinceCleanup;

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
            cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@cmd", record.Command);
            cmd.Parameters.AddWithValue("@path", record.ProjectPath);
            cmd.Parameters.AddWithValue("@in", record.InputTokens);
            cmd.Parameters.AddWithValue("@out", record.OutputTokens);
            cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
            cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
            cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _insertsSinceCleanup++;
            if (_insertsSinceCleanup >= CleanupIntervalInserts)
            {
                _insertsSinceCleanup = 0;
                await CleanupCoreAsync(defaultRetentionDays, cancellationToken).ConfigureAwait(false);
            }
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
                                  AVG(savings_percentage) as avg_pct
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           GROUP BY command, success
                           ORDER BY command, success DESC
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadSummaryAsync, cancellationToken);
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
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadHistoryAsync, cancellationToken);
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

    internal static string GetDefaultDbPath()
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
                                    execution_time_ms REAL NOT NULL
                                );
                                CREATE INDEX IF NOT EXISTS idx_commands_timestamp ON commands(timestamp);
                                CREATE INDEX IF NOT EXISTS idx_commands_project_path ON commands(project_path);
                                """;
        await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Migration: add success column for existing databases (old records default to success=1)
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var checkCmd = _connection.CreateCommand();
#pragma warning restore CA2007
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('commands') WHERE name='success'";
        var columnExists = (long)(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
        if (!columnExists)
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var alterCmd = _connection.CreateCommand();
#pragma warning restore CA2007
            alterCmd.CommandText = "ALTER TABLE commands ADD COLUMN success INTEGER NOT NULL DEFAULT 1";
            await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<T> ExecuteWithFilterAsync<T>(
        int days,
        string? projectPath,
        string? commandFilter,
        string sql,
        Func<SqliteCommand, CancellationToken, Task<T>> readResultsAsync,
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
        var totalInput = 0;
        var totalOutput = 0;
        var totalSaved = 0;

        // Accumulate per-command, per-status rows before building CommandGainDetail
        var grouped =
            new Dictionary<string, List<(bool Success, int RunCount, int SumInput, int SumOutput, int SumSaved, double
                AvgPct)>>(StringComparer.Ordinal);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var cmdName = reader.GetString(0);
            var success = reader.GetInt32(1) != 0;
            var runCount = reader.GetInt32(2);
            var sumInput = reader.GetInt32(3);
            var sumOutput = reader.GetInt32(4);
            var sumSaved = reader.GetInt32(5);
            var avgPct = reader.GetDouble(6);

            totalCommands += runCount;
            totalInput += sumInput;
            totalOutput += sumOutput;
            totalSaved += sumSaved;

            if (!grouped.TryGetValue(cmdName, out var rows))
            {
                rows = [];
                grouped[cmdName] = rows;
            }

            rows.Add((success, runCount, sumInput, sumOutput, sumSaved, avgPct));
        }

        var commandDetails = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal);
        foreach (var (cmdName, rows) in grouped)
        {
            CommandGainDetail? successDetail = null;
            CommandGainDetail? failureDetail = null;
            var totalRunsCmd = 0;
            var totalInputCmd = 0;
            var totalOutputCmd = 0;
            var totalSavedCmd = 0;
            var weightedPctSum = 0.0;

            foreach (var (success, runCount, sumInput, sumOutput, sumSaved, avgPct) in rows)
            {
                var statusDetail = new CommandGainDetail(runCount, sumInput, sumOutput, sumSaved, avgPct);
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
            }

            // Run-count-weighted average of the per-status SQL AVG(savings_percentage) values,
            // keeping the same per-run-average semantics as SuccessDetail/FailureDetail.
            var avgPctCmd = totalRunsCmd > 0 ? weightedPctSum / totalRunsCmd : 0.0;
            commandDetails[cmdName] = new CommandGainDetail(totalRunsCmd, totalInputCmd, totalOutputCmd, totalSavedCmd,
                avgPctCmd, successDetail, failureDetail);
        }

        var averagePct = totalInput > 0 ? (double)totalSaved / totalInput * 100.0 : 0.0;
        return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, commandDetails);
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

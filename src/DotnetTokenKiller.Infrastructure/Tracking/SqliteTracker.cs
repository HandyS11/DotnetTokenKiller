using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Persists command tracking records in a SQLite database.</summary>
/// <remarks>
/// On a file data source a tracked run never opens SQLite: <see cref="RecordAsync"/> writes one file
/// to the pending-record journal (<c>&lt;database file&gt;.pending</c> beside the database, such as
/// <c>tracking.db.pending</c>), and every reader, as well as <see cref="CleanupAsync"/>, folds the
/// journal into the database before it queries, so what it returns includes every run that has
/// finished. Retention runs when the database is initialized and at fold time. An in-memory data
/// source has no directory to journal into, so there <see cref="RecordAsync"/> inserts directly.
/// </remarks>
/// <param name="connectionString">The SQLite connection string.</param>
/// <param name="defaultRetentionDays">Number of days to retain records before automatic cleanup.</param>
/// <param name="foldThreshold">Pending runs at which <see cref="WarmUpAsync"/> starts a background fold.</param>
public sealed class SqliteTracker(
    string connectionString,
    int defaultRetentionDays = 90,
    int foldThreshold = SqliteTracker.DefaultFoldThreshold)
    : ITracker, IDisposable, IAsyncDisposable
{
    /// <summary>Pending runs at which a warm-up folds the journal in the background, so it never grows unbounded.</summary>
    public const int DefaultFoldThreshold = 64;

    private const int HistoryLimit = 500;

    /// <summary>SQLite's data source name for a private, in-memory database.</summary>
    private const string MemoryDataSource = ":memory:";

    /// <summary>
    /// The SQL literal list of outcomes that count toward savings, derived from
    /// <see cref="RunOutcomes.CountedInSavings"/> so the two can never disagree.
    /// </summary>
    private static readonly string CountedInSavingsSqlList =
        string.Join(", ", RunOutcomes.CountedInSavings.Select(o => $"'{o}'"));

    private readonly PendingRecordJournal? _journal = CreateJournal(connectionString);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Task? _backgroundFold;
    private SqliteConnection? _connection;
    private bool _disposed;
    private bool _initialized;

    /// <summary>Asynchronously releases managed resources.</summary>
    /// <remarks>
    /// Waits for a fold that <see cref="WarmUpAsync"/> started in the background, then takes the
    /// semaphore, so an initialization still running on a background thread (a warm-up that outlived
    /// its run) finishes before its connection is disposed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await BackgroundFoldAsync().ConfigureAwait(false);
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }

    /// <summary>Releases managed resources.</summary>
    /// <remarks>Waits for an in-flight initialization, as <see cref="DisposeAsync"/> does.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// On a file data source this writes one journal file and touches nothing else: no connection,
    /// no statement, no fsync. The next read folds it into the database. An in-memory data source
    /// inserts the row directly.
    /// </remarks>
    public async Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_journal is { } journal)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await journal.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await InsertAsync(record, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// On a file data source this creates the journal directory and, once at least the fold
    /// threshold of runs wait in it, starts folding them on the thread pool without awaiting the
    /// fold; <see cref="DisposeAsync"/> waits for it. It opens no connection itself. An in-memory
    /// data source opens its connection and creates the schema.
    /// </remarks>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (_journal is not { } journal)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }

            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(journal.Root);

        // The journal folds itself on read; this keeps it bounded for a user who never reads. The
        // fold's SQLite setup, inserts and fsync run while the child does, and RecordAsync never
        // waits for it: an aborted fold is refolded next time (see PendingRecordJournal.FoldAsync).
        if (journal.Count() >= foldThreshold)
        {
            _backgroundFold = Task.Run(() => FoldInBackgroundAsync(cancellationToken), CancellationToken.None);
        }
    }

    /// <inheritdoc/>
    public Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
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
                     AND outcome IN ({CountedInSavingsSqlList})
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
                                  saved_tokens, savings_percentage, execution_time_ms, success, outcome, source
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
    public Task<CoverageSummary> GetCoverageAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT command, outcome, source,
                                  COUNT(*) as run_count,
                                  SUM(input_tokens) as total_input,
                                  SUM(execution_time_ms) as total_ms
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           GROUP BY command, outcome, source
                           ORDER BY total_input DESC, run_count DESC
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadCoverageAsync, null,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await FoldLockedAsync(wait: true, cancellationToken).ConfigureAwait(false);
            await CleanupCoreAsync(retentionDays, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Also deletes the runs still waiting in the journal and the record of past folds. The journal
    /// is cleared first, so a clear that fails aborts the reset before any row is deleted: a claim
    /// directory left by a fold that died after its commit is only recognised by its id in the folds
    /// table, and deleting that row while the directory survived would refold its runs next time.
    /// </remarks>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            _journal?.Clear();
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = CreateCommand();
#pragma warning restore CA2007
            cmd.CommandText = "DELETE FROM commands; DELETE FROM folds";
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

    /// <summary>The database file's path, or <see langword="null"/> for an in-memory or unnamed database.</summary>
    /// <param name="cs">The SQLite connection string.</param>
    private static string? FileDataSource(string cs)
    {
        if (cs.Contains(MemoryDataSource, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dataSource = new SqliteConnectionStringBuilder(cs).DataSource;
        return string.IsNullOrWhiteSpace(dataSource) ? null : dataSource;
    }

    private static void EnsureDataDirectory(string cs)
    {
        var dataSource = FileDataSource(cs);
        if (dataSource is null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>The journal beside the database file, or <see langword="null"/> for an in-memory database.</summary>
    /// <param name="cs">The SQLite connection string.</param>
    private static PendingRecordJournal? CreateJournal(string cs)
    {
        var dataSource = FileDataSource(cs);
        if (dataSource is null)
        {
            return null;
        }

        // Named after the database file, as SQLite names its own "-journal": the database path is
        // user-configurable, and a generic folder name in that directory could belong to someone
        // else, whose files a fold would claim and delete and a reset would clear.
        var fullPath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        return new PendingRecordJournal(Path.Combine(directory, Path.GetFileName(fullPath) + ".pending"));
    }

    private async Task FoldInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FoldAsync(wait: false, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: best effort; the next read folds what this one did not
        }
    }

    /// <summary>The fold <see cref="WarmUpAsync"/> started, or a completed task. Never throws.</summary>
    private Task BackgroundFoldAsync() => _backgroundFold ?? Task.CompletedTask;

    private async Task FoldAsync(bool wait, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await FoldLockedAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Folds the journal. The caller holds the semaphore and has initialized the connection.</summary>
    /// <param name="wait">Wait for another process's fold (readers), or skip when one is running (background).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task FoldLockedAsync(bool wait, CancellationToken cancellationToken)
    {
        if (_journal is not { } journal)
        {
            return;
        }

        await journal.FoldAsync(IsFoldCommittedAsync, CommitFoldAsync, wait, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsFoldCommittedAsync(string foldId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = CreateCommand();
#pragma warning restore CA2007
        cmd.CommandText = "SELECT COUNT(*) FROM folds WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", foldId);
        return (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;
    }

    /// <summary>Inserts the folded records, every fold id, and applies retention, in one transaction.</summary>
    /// <param name="foldIds">The new claim's id and every recovered claim's id.</param>
    /// <param name="records">The claimed records, in run order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Called before initialization succeeded.</exception>
    private async Task CommitFoldAsync(
        IReadOnlyList<string> foldIds, IReadOnlyList<CommandRecord> records, CancellationToken cancellationToken)
    {
        var connection = _connection ?? throw new InvalidOperationException("The tracker was used before it was initialized.");
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        foreach (var record in records)
        {
            await InsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
        }

        foreach (var foldId in foldIds)
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = CreateCommand();
#pragma warning restore CA2007
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO folds (id) VALUES (@id)";
            cmd.Parameters.AddWithValue("@id", foldId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await CleanupCoreAsync(defaultRetentionDays, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertAsync(CommandRecord record, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = CreateCommand();
#pragma warning restore CA2007
        cmd.Transaction = transaction;
        cmd.CommandText = """
                          INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                              saved_tokens, savings_percentage, execution_time_ms, success, outcome, source)
                          VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms, @success, @outcome, @source)
                          """;
        cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@cmd", record.Command);
        cmd.Parameters.AddWithValue("@path", record.ProjectPath);
        cmd.Parameters.AddWithValue("@in", record.InputTokens);
        cmd.Parameters.AddWithValue("@out", record.OutputTokens);
        cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
        cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
        cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
        cmd.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("@outcome", record.Outcome.ToString());
        cmd.Parameters.AddWithValue("@source", record.Source.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        try
        {
            EnsureDataDirectory(connectionString);

            // Created here rather than in a field initializer: SqliteConnection's static initializer
            // loads the native SQLite library, about 23 ms, which a tracker that is built but never
            // used (tracking disabled) should not pay.
            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(ct).ConfigureAwait(false);

            // A one-shot CLI runs as a single process with a single tracker instance, so purge
            // expired rows once here at startup. A single delete per process is cheap and replaces
            // the old per-insert counter cleanup that could never fire during a one-command run. A
            // fold applies retention again, inside its transaction, to the records it inserts.
            await CleanupCoreAsync(defaultRetentionDays, null, ct).ConfigureAwait(false);
            _initialized = true;
        }
        catch
        {
            // A failed attempt leaves nothing behind, so the next call (a read after a failed one,
            // or after a failed warm-up) starts again from a fresh connection instead of reopening
            // a half-initialized one. The field is cleared before disposing so a throwing
            // DisposeAsync cannot mask the original setup exception or leave a disposed connection
            // behind in the field.
            var failed = _connection;
            _connection = null;
            if (failed is not null)
            {
                await failed.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>Creates a command on the connection <see cref="EnsureInitializedAsync"/> opened.</summary>
    /// <exception cref="InvalidOperationException">Called before initialization succeeded.</exception>
    private SqliteCommand CreateCommand() =>
        (_connection ?? throw new InvalidOperationException("The tracker was used before it was initialized."))
        .CreateCommand();

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var createCmd = CreateCommand();
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
                                    success INTEGER NOT NULL DEFAULT 1,
                                    outcome TEXT NOT NULL DEFAULT 'Filtered',
                                    source TEXT NOT NULL DEFAULT 'Run'
                                );
                                CREATE INDEX IF NOT EXISTS idx_commands_timestamp ON commands(timestamp);
                                CREATE INDEX IF NOT EXISTS idx_commands_project_path ON commands(project_path);
                                CREATE TABLE IF NOT EXISTS folds (
                                    id TEXT PRIMARY KEY
                                );
                                """;
        await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Migrations for databases created before these columns existed. Fresh databases already
        // have them (see CREATE TABLE above) so these only run on legacy files.
        await EnsureColumnAsync("success", "INTEGER NOT NULL DEFAULT 1", ct).ConfigureAwait(false);
        await EnsureColumnAsync("outcome", "TEXT NOT NULL DEFAULT 'Filtered'", ct).ConfigureAwait(false);
        await EnsureColumnAsync("source", "TEXT NOT NULL DEFAULT 'Run'", ct).ConfigureAwait(false);
    }

    /// <summary>Adds a column to the commands table if it is not already present.</summary>
    /// <param name="columnName">The column to ensure exists.</param>
    /// <param name="columnDefinition">The SQL type and constraints for the column.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task EnsureColumnAsync(string columnName, string columnDefinition, CancellationToken ct)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var checkCmd = CreateCommand();
#pragma warning restore CA2007
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('commands') WHERE name = @name";
        checkCmd.Parameters.AddWithValue("@name", columnName);
        var columnExists = (long)(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
        if (columnExists)
        {
            return;
        }

        try
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var alterCmd = CreateCommand();
#pragma warning restore CA2007
#pragma warning disable CA2100, S2077 // columnName and columnDefinition are caller-supplied constant literals
            alterCmd.CommandText = $"ALTER TABLE commands ADD COLUMN {columnName} {columnDefinition}";
#pragma warning restore CA2100, S2077
            await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // A concurrent initializer added the column between our pragma check and this ALTER.
            // The column now exists, which is all we required — the losing racer is fine.
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
            await FoldLockedAsync(wait: true, cancellationToken).ConfigureAwait(false);
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

    private static async Task<CoverageSummary> ReadCoverageAsync(SqliteCommand cmd, CancellationToken ct)
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

    private async Task CleanupCoreAsync(int days, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = CreateCommand();
#pragma warning restore CA2007
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM commands WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

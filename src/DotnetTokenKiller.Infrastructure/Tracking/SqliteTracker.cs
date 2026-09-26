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
/// This type owns the connection and the semaphore that serializes every access to it; schema
/// migration, journal folding and filtered queries are delegated to
/// <see cref="SqliteSchemaMigrator"/>, <see cref="SqliteFoldCoordinator"/> and
/// <see cref="SqliteQueryReader"/>, each of which receives a connection accessor (and the journal
/// or semaphore, where needed) rather than opening a connection of its own.
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

    /// <summary>
    /// Lazily built on first use rather than in a field initializer: a primary constructor's field
    /// initializers cannot reference other instance members (CS0236), and this wrapper needs
    /// <see cref="GetConnection"/> bound to this instance. Stateless (it only holds a delegate), so a
    /// benign race building two on first concurrent use is harmless. Touches no SQLite connection;
    /// that still happens only inside <see cref="EnsureInitializedAsync"/>.
    /// </summary>
    private SqliteSchemaMigrator SchemaMigrator => field ??= new SqliteSchemaMigrator(GetConnection);

    /// <summary>Lazily built on first use; see <see cref="SchemaMigrator"/> for why.</summary>
    private SqliteFoldCoordinator FoldCoordinator => field ??= new SqliteFoldCoordinator(
        _journal, _semaphore, GetConnection, EnsureInitializedAsync, InsertAsync, CleanupCoreAsync, defaultRetentionDays);

    /// <summary>Lazily built on first use; see <see cref="SchemaMigrator"/> for why.</summary>
    private SqliteQueryReader QueryReader => field ??=
        new SqliteQueryReader(_semaphore, GetConnection, EnsureInitializedAsync, FoldBeforeReadAsync);

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
            _backgroundFold = Task.Run(() => FoldCoordinator.FoldInBackgroundAsync(cancellationToken), CancellationToken.None);
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

        return QueryReader.ExecuteWithFilterAsync(
            days, projectPath, commandFilter, sql, SqliteQueryReader.ReadSummaryAsync, null, cancellationToken);
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

        return QueryReader.ExecuteWithFilterAsync(
            days, projectPath, commandFilter, sql, SqliteQueryReader.ReadHistoryAsync, HistoryLimit, cancellationToken);
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

        return QueryReader.ExecuteWithFilterAsync(
            days, projectPath, commandFilter, sql, SqliteQueryReader.ReadCoverageAsync, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await FoldCoordinator.FoldLockedAsync(wait: true, cancellationToken).ConfigureAwait(false);
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

    /// <summary>The fold <see cref="WarmUpAsync"/> started, or a completed task. Never throws.</summary>
    private Task BackgroundFoldAsync() => _backgroundFold ?? Task.CompletedTask;

    /// <summary>The fold every reader runs before its query, waiting for a concurrent fold to finish.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private Task FoldBeforeReadAsync(CancellationToken cancellationToken) =>
        FoldCoordinator.FoldLockedAsync(wait: true, cancellationToken);

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
            await SchemaMigrator.InitializeSchemaAsync(ct).ConfigureAwait(false);

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

    /// <summary>The connection <see cref="EnsureInitializedAsync"/> opened.</summary>
    /// <exception cref="InvalidOperationException">Called before initialization succeeded.</exception>
    private SqliteConnection GetConnection() =>
        _connection ?? throw new InvalidOperationException("The tracker was used before it was initialized.");

    /// <summary>Creates a command on the connection <see cref="EnsureInitializedAsync"/> opened.</summary>
    /// <exception cref="InvalidOperationException">Called before initialization succeeded.</exception>
    private SqliteCommand CreateCommand() => GetConnection().CreateCommand();

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

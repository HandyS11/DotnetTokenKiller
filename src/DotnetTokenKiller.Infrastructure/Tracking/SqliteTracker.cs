using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Persists command tracking records in a SQLite database.</summary>
/// <param name="connectionString">The SQLite connection string.</param>
/// <param name="retentionDays">Number of days to retain records before automatic cleanup.</param>
public sealed class SqliteTracker(string connectionString, int retentionDays = 90)
    : ITracker, IDisposable, IAsyncDisposable
{
    private const int CleanupIntervalInserts = 50;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly SqliteConnection _connection = new(connectionString);
    private bool _initialized;
    private int _insertsSinceCleanup;

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
        await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
        cmd.CommandText = """
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
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
                                  saved_tokens, savings_percentage, execution_time_ms)
                              VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms)
                              """;
            cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@cmd", record.Command);
            cmd.Parameters.AddWithValue("@path", record.ProjectPath);
            cmd.Parameters.AddWithValue("@in", record.InputTokens);
            cmd.Parameters.AddWithValue("@out", record.OutputTokens);
            cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
            cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
            cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _insertsSinceCleanup++;
            if (_insertsSinceCleanup >= CleanupIntervalInserts)
            {
                _insertsSinceCleanup = 0;
                await CleanupCoreAsync(retentionDays, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
            cmd.CommandText = """
                              SELECT command,
                                     COUNT(*) as run_count,
                                     SUM(input_tokens) as total_input,
                                     SUM(output_tokens) as total_output,
                                     SUM(saved_tokens) as total_saved,
                                     AVG(savings_percentage) as avg_pct
                              FROM commands
                              WHERE timestamp >= @since
                                AND (@path IS NULL OR project_path = @path)
                              GROUP BY command
                              """;
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value);

            var totalCommands = 0;
            var totalInput = 0;
            var totalOutput = 0;
            var totalSaved = 0;
            var commandDetails = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var cmdName = reader.GetString(0);
                var runCount = reader.GetInt32(1);
                var sumInput = reader.GetInt32(2);
                var sumOutput = reader.GetInt32(3);
                var sumSaved = reader.GetInt32(4);
                var avgPct = reader.GetDouble(5);

                totalCommands += runCount;
                totalInput += sumInput;
                totalOutput += sumOutput;
                totalSaved += sumSaved;
                commandDetails[cmdName] = new CommandGainDetail(runCount, sumInput, sumOutput, sumSaved, avgPct);
            }

            var averagePct = totalInput > 0 ? (double)totalSaved / totalInput * 100.0 : 0.0;
            return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, commandDetails);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = _connection.CreateCommand();
#pragma warning restore CA2007
            cmd.CommandText = """
                              SELECT timestamp, command, project_path, input_tokens, output_tokens,
                                     saved_tokens, savings_percentage, execution_time_ms
                              FROM commands
                              WHERE timestamp >= @since
                                AND (@path IS NULL OR project_path = @path)
                              ORDER BY timestamp DESC
                              """;
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value);

            var results = new List<CommandRecord>();
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CommandRecord(
                    DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetDouble(6),
                    TimeSpan.FromMilliseconds(reader.GetDouble(7))));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
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

    /// <summary>Releases managed resources.</summary>
    public void Dispose()
    {
        _semaphore.Dispose();
        _connection.Dispose();
    }

    /// <summary>Asynchronously releases managed resources.</summary>
    public async ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}

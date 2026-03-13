using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace DotnetTokenKiller.Infrastructure.Tracking;

public sealed class SqliteTracker(string connectionString) : ITracker, IAsyncDisposable
{
    private const int RetentionDays = 90;
    private readonly SqliteConnection _connection = new(connectionString);
    private bool _initialized;

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
            Directory.CreateDirectory(dir);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        EnsureDataDirectory(connectionString);
        await _connection.OpenAsync(ct);
        await InitializeSchemaAsync(ct);
        _initialized = true;
    }

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
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
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var cmd = _connection.CreateCommand();
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
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await CleanupAsync(RetentionDays, cancellationToken);
    }

    public async Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

        await using var cmd = _connection.CreateCommand();
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
        var totalAvgPct = 0.0;
        var commandCount = 0;
        var savedByCommand = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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
            totalAvgPct += avgPct;
            commandCount++;
            savedByCommand[cmdName] = sumSaved;
        }

        var averagePct = commandCount > 0 ? totalAvgPct / commandCount : 0.0;
        return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, savedByCommand);
    }

    public async Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture);

        await using var cmd = _connection.CreateCommand();
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
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CommandRecord(
                Timestamp: DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Command: reader.GetString(1),
                ProjectPath: reader.GetString(2),
                InputTokens: reader.GetInt32(3),
                OutputTokens: reader.GetInt32(4),
                SavedTokens: reader.GetInt32(5),
                SavingsPercentage: reader.GetDouble(6),
                ExecutionTime: TimeSpan.FromMilliseconds(reader.GetDouble(7))));
        }
        return results;
    }

    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O", CultureInfo.InvariantCulture);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM commands WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

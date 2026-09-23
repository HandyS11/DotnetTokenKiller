using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Creates and migrates the <c>commands</c>/<c>folds</c> schema on a tracker's connection.</summary>
/// <remarks>
/// Owns no connection of its own: <see cref="SqliteTracker"/> opens and disposes the connection, and
/// passes an accessor here so every command this migrator creates runs on that same connection.
/// </remarks>
/// <param name="connectionAccessor">
/// Returns the tracker's open connection, or throws if called before initialization succeeded.
/// </param>
internal sealed class SqliteSchemaMigrator(Func<SqliteConnection> connectionAccessor)
{
    /// <summary>Creates the schema if missing and applies column migrations for legacy databases.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeSchemaAsync(CancellationToken ct)
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

    private SqliteCommand CreateCommand() => connectionAccessor().CreateCommand();
}

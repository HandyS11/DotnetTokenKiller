using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Folds a tracker's pending-record journal into its database.</summary>
/// <remarks>
/// Owns no connection or semaphore of its own: <see cref="SqliteTracker"/> owns both and passes
/// them here so callers on either side of the split still serialize through the same lock and run
/// on the same connection. <see cref="FoldLockedAsync"/> assumes the caller already holds that
/// semaphore and that the connection is initialized, exactly as it did as a method on the tracker.
/// </remarks>
/// <param name="journal">The journal to fold, or <see langword="null"/> for an in-memory database.</param>
/// <param name="semaphore">The tracker's semaphore, held by every caller of <see cref="FoldLockedAsync"/>.</param>
/// <param name="connectionAccessor">Returns the tracker's open connection, or throws before initialization.</param>
/// <param name="ensureInitializedAsync">Opens the connection and creates the schema if not already done.</param>
/// <param name="insertAsync">Inserts one record, optionally inside a transaction.</param>
/// <param name="cleanupCoreAsync">Deletes rows older than the retention cutoff, optionally inside a transaction.</param>
/// <param name="defaultRetentionDays">Retention applied to the records a fold commits.</param>
internal sealed class SqliteFoldCoordinator(
    PendingRecordJournal? journal,
    SemaphoreSlim semaphore,
    Func<SqliteConnection> connectionAccessor,
    Func<CancellationToken, Task> ensureInitializedAsync,
    Func<CommandRecord, SqliteTransaction?, CancellationToken, Task> insertAsync,
    Func<int, SqliteTransaction?, CancellationToken, Task> cleanupCoreAsync,
    int defaultRetentionDays)
{
    private async Task FoldAsync(bool wait, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ensureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await FoldLockedAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Folds the journal. The caller holds the semaphore and has initialized the connection.</summary>
    /// <param name="wait">Wait for another process's fold (readers), or skip when one is running (background).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FoldLockedAsync(bool wait, CancellationToken cancellationToken)
    {
        if (journal is not { } j)
        {
            return;
        }

        await j.FoldAsync(IsFoldCommittedAsync, CommitFoldAsync, wait, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Folds the journal on the thread pool without waiting for another process's fold. Never throws.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FoldInBackgroundAsync(CancellationToken cancellationToken)
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
        var connection = connectionAccessor();
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        foreach (var record in records)
        {
            await insertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
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

        await cleanupCoreAsync(defaultRetentionDays, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteCommand CreateCommand() => connectionAccessor().CreateCommand();
}

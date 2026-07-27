using DotnetTokenKiller.Domain.Configuration;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Builds a configured <see cref="SqliteTracker"/>.</summary>
/// <remarks>
/// The DI registration and the non-DI passthrough entry point both need a tracker built the same
/// way. Resolving the database path in one place keeps them from drifting apart.
/// </remarks>
public static class TrackerFactory
{
    /// <summary>Creates a tracker using the configured database path and retention period.</summary>
    /// <param name="config">The loaded configuration.</param>
    /// <returns>A tracker the caller owns and must dispose.</returns>
    public static SqliteTracker Create(DtkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var dbPath = EnvironmentOverride.Read("DTK_DB_PATH")
                     ?? config.Tracking.DbPath
                     ?? SqliteTracker.GetDefaultDbPath();
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        return new SqliteTracker(connectionString, config.Tracking.RetentionDays);
    }
}

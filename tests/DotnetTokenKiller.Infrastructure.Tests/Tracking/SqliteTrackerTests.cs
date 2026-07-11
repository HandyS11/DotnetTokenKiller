using System.Reflection;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

public class SqliteTrackerTests : IAsyncDisposable
{
    private readonly SqliteTracker _sut = new("Data Source=:memory:");

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static CommandRecord MakeRecord(
        string command = "build",
        string projectPath = "/proj",
        int inputTokens = 1000,
        int outputTokens = 150,
        int savedTokens = 850,
        double savingsPct = 85.0,
        DateTimeOffset? timestamp = null,
        bool success = true)
    {
        return new CommandRecord(
            timestamp ?? DateTimeOffset.UtcNow,
            command,
            projectPath,
            new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
            TimeSpan.FromMilliseconds(500),
            success);
    }

    [Fact]
    public async Task RecordAsync_PersistsRecord_RetrievableViaGetHistoryAsync()
    {
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordAsync_PersistsAllFields_Correctly()
    {
        var ts = DateTimeOffset.UtcNow.AddDays(-1);
        var record = MakeRecord(
            "test",
            "/my/project",
            2000,
            200,
            1800,
            90.0,
            ts,
            false);

        await _sut.RecordAsync(record);
        var history = await _sut.GetHistoryAsync(365, null);

        var stored = history.Should().ContainSingle().Subject;
        stored.Command.Should().Be("test");
        stored.ProjectPath.Should().Be("/my/project");
        stored.InputTokens.Should().Be(2000);
        stored.OutputTokens.Should().Be(200);
        stored.SavedTokens.Should().Be(1800);
        stored.SavingsPercentage.Should().BeApproximately(90.0, 0.001);
        stored.Timestamp.Should().BeCloseTo(ts, TimeSpan.FromSeconds(1));
        stored.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RecordAsync_FirstInsertOfProcess_PurgesExpiredRowsAsync()
    {
        // A one-shot CLI is one process = one tracker instance. Cleanup must run once at
        // initialization so a fresh process purges rows that expired while it was not running.
        var dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "tracking.db");
        try
        {
            // Seed an expired row using a first tracker (a prior process).
            await using (var seeder = new SqliteTracker($"Data Source={dbPath}"))
            {
                await seeder.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-100)));
            }

            SqliteConnection.ClearAllPools();

            // Fresh instance = fresh process. Its first insert must purge the expired row.
            await using var tracker = new SqliteTracker($"Data Source={dbPath}");
            await tracker.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow));

            var history = await tracker.GetHistoryAsync(365, null);
            history.Should().ContainSingle(); // expired row purged at init, only the fresh one remains
            history.Should().NotContain(r => r.Timestamp < DateTimeOffset.UtcNow.AddDays(-90));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public async Task RecordAsync_ExpiredRowInsertedMidSession_NotPurgedUntilNextProcessAsync()
    {
        // Cleanup runs once at init, not on every insert. A row inserted mid-session that is
        // already older than the cutoff is NOT retroactively purged by later inserts.
        await _sut.RecordAsync(MakeRecord()); // triggers init + cleanup (db empty, no-op)
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-100)));
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(365, null);

        history.Should().HaveCount(3); // no per-insert cleanup; the old row survives this process
    }

    [Fact]
    public async Task InitializeAsync_TwoTrackersOneFreshFile_NeitherThrowsAsync()
    {
        // Two processes racing to initialize the same brand-new db file must not collide on
        // schema creation / migration (old failure: "duplicate column name: success").
        // Looped to make the timing-dependent race reliable.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "race.db");
            try
            {
                await using var a = new SqliteTracker($"Data Source={dbPath}");
                await using var b = new SqliteTracker($"Data Source={dbPath}");

                var act = () => Task.WhenAll(
                    a.RecordAsync(MakeRecord(), default),
                    b.RecordAsync(MakeRecord(), default));

                await act.Should().NotThrowAsync();
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }

    [Fact]
    public async Task LegacyDbWithoutSuccessColumn_IsMigratedAsync()
    {
        // A db created before the `success` column existed must still be usable: the pragma
        // check detects the missing column and ALTERs it in.
        var dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "legacy.db");
        try
        {
            await CreateLegacySchemaByHandAsync(dbPath);

            await using var tracker = new SqliteTracker($"Data Source={dbPath}");
            var act = () => tracker.RecordAsync(MakeRecord(), default);

            await act.Should().NotThrowAsync();
            (await tracker.GetHistoryAsync(1, null)).Should().ContainSingle();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public async Task GetHistoryAsync_CapsResultsAtLimitAsync()
    {
        // History is bounded so a huge tracking db can't load an unbounded result set into memory.
        for (var i = 0; i < 501; i++)
        {
            await _sut.RecordAsync(MakeRecord());
        }

        var history = await _sut.GetHistoryAsync(365, null);

        history.Should().HaveCount(500); // newest 500, older rows dropped by the LIMIT
    }

    private static async Task CreateLegacySchemaByHandAsync(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE commands (
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
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesCorrectly()
    {
        await _sut.RecordAsync(MakeRecord(savedTokens: 800));
        await _sut.RecordAsync(MakeRecord(savedTokens: 900));
        await _sut.RecordAsync(MakeRecord("test", savedTokens: 500));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalCommands.Should().Be(3);
        summary.TotalSavedTokens.Should().Be(2200);
        summary.CommandDetails["build"].TotalSavedTokens.Should().Be(1700);
        summary.CommandDetails["test"].TotalSavedTokens.Should().Be(500);
    }

    [Fact]
    public async Task GetSummaryAsync_SplitsSuccessAndFailureIntoSubDetails()
    {
        // Two success rows at 80 %, one failure row at 50 % — deliberately different so
        // the combined AverageSavingsPercentage asserts the run-count-weighted average:
        //   (2 × 80.0 + 1 × 50.0) / 3 = 70.0
        await _sut.RecordAsync(MakeRecord(savedTokens: 800, savingsPct: 80.0, success: true));
        await _sut.RecordAsync(MakeRecord(savedTokens: 900, savingsPct: 80.0, success: true));
        await _sut.RecordAsync(MakeRecord(savedTokens: 500, savingsPct: 50.0, success: false));

        var summary = await _sut.GetSummaryAsync(30, null);

        var buildDetail = summary.CommandDetails["build"];
        buildDetail.RunCount.Should().Be(3);
        buildDetail.SuccessDetail.Should().NotBeNull();
        buildDetail.SuccessDetail!.RunCount.Should().Be(2);
        buildDetail.SuccessDetail.TotalSavedTokens.Should().Be(1700);
        buildDetail.SuccessDetail.AverageSavingsPercentage.Should().BeApproximately(80.0, 0.001);
        buildDetail.FailureDetail.Should().NotBeNull();
        buildDetail.FailureDetail!.RunCount.Should().Be(1);
        buildDetail.FailureDetail.TotalSavedTokens.Should().Be(500);
        buildDetail.FailureDetail.AverageSavingsPercentage.Should().BeApproximately(50.0, 0.001);
        // Combined: run-count-weighted average of per-status SQL AVG values
        buildDetail.AverageSavingsPercentage.Should().BeApproximately(70.0, 0.001);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByDays()
    {
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-10)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-1)));

        var summary = await _sut.GetSummaryAsync(7, null);

        summary.TotalCommands.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByProjectPath()
    {
        await _sut.RecordAsync(MakeRecord(projectPath: "/proj/a"));
        await _sut.RecordAsync(MakeRecord(projectPath: "/proj/b"));

        var summary = await _sut.GetSummaryAsync(30, "/proj/a");

        summary.TotalCommands.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByCommandFilter()
    {
        await _sut.RecordAsync(MakeRecord());
        await _sut.RecordAsync(MakeRecord("test"));
        await _sut.RecordAsync(MakeRecord());

        var summary = await _sut.GetSummaryAsync(30, null, "build");

        summary.TotalCommands.Should().Be(2);
        summary.CommandDetails.Should().ContainKey("build");
        summary.CommandDetails.Should().NotContainKey("test");
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByCommandFilter()
    {
        await _sut.RecordAsync(MakeRecord());
        await _sut.RecordAsync(MakeRecord("test"));

        var history = await _sut.GetHistoryAsync(30, null, "test");

        history.Should().ContainSingle(r => r.Command == "test");
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRecordsInDescendingTimestampOrder()
    {
        var ts1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ts2 = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _sut.RecordAsync(MakeRecord("first", timestamp: ts1));
        await _sut.RecordAsync(MakeRecord("second", timestamp: ts2));

        var history = await _sut.GetHistoryAsync(1, null);

        history[0].Command.Should().Be("second"); // most recent first
        history[1].Command.Should().Be("first");
    }

    [Fact]
    public async Task ResetAsync_DeletesAllRecords()
    {
        await _sut.RecordAsync(MakeRecord());
        await _sut.RecordAsync(MakeRecord("test"));

        await _sut.ResetAsync();

        var history = await _sut.GetHistoryAsync(365, null);
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetAsync_IsIdempotent_WhenDatabaseIsEmpty()
    {
        await _sut.ResetAsync();

        var history = await _sut.GetHistoryAsync(365, null);
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupAsync_DeletesOldRecords_LeavesRecentOnes()
    {
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-100)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-50)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-1)));

        await _sut.CleanupAsync(90);

        var history = await _sut.GetHistoryAsync(365, null);
        history.Should().HaveCount(2); // 50 days and 1 day remain; 100 days gone
    }

    [Fact]
    public async Task RecordAsync_ConcurrentWrites_AllRecordsArePersisted()
    {
        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency)
            .Select(i => _sut.RecordAsync(MakeRecord($"cmd-{i}")))
            .ToArray();

        await Task.WhenAll(tasks);

        var history = await _sut.GetHistoryAsync(1, null);
        history.Should().HaveCount(concurrency);
    }

    [Fact]
    public async Task RecordAsync_FileBasedDb_PersistsRecord()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "tracking.db");
        try
        {
            await using var tracker = new SqliteTracker($"Data Source={tempPath}");
            await tracker.RecordAsync(MakeRecord());

            var history = await tracker.GetHistoryAsync(1, null);
            history.Should().HaveCount(1);
        }
        finally
        {
            // ClearAllPools releases Windows file locks held by SQLite connection pooling
            SqliteConnection.ClearAllPools();
            var dir = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public async Task GetSummaryAsync_ConcurrentReadsDuringWrites_DoesNotThrow()
    {
        // Seed some data first
        await _sut.RecordAsync(MakeRecord());

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(_sut.RecordAsync(MakeRecord($"cmd-{i}")));
            tasks.Add(_sut.GetSummaryAsync(30, null));
        }

        var act = () => Task.WhenAll(tasks);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_ReturnsZeroAverageSavingsPct()
    {
        // Covers totalInput == 0 → averagePct = 0.0 branch (line 218)
        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalCommands.Should().Be(0);
        summary.AverageSavingsPercentage.Should().Be(0.0);
    }

    [Fact]
    public void EnsureDataDirectory_EmptyDataSource_ReturnsEarlyWithoutThrowing()
    {
        // Covers string.IsNullOrWhiteSpace(csb.DataSource) true branch (lines 33-35) via reflection
        var method = typeof(SqliteTracker)
            .GetMethod("EnsureDataDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;

        // A connection string with no Data Source key → csb.DataSource = "" → IsNullOrWhiteSpace is true
        var act = () => method.Invoke(null, ["Mode=ReadWriteCreate"]);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesInputAndOutputTokensCorrectly()
    {
        // Kills AddAssignmentExpression → SubtractAssignmentExpression mutations on totalInput/totalOutput
        await _sut.RecordAsync(MakeRecord(inputTokens: 1000, outputTokens: 150, savedTokens: 850));
        await _sut.RecordAsync(MakeRecord(inputTokens: 2000, outputTokens: 300, savedTokens: 1700));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalInputTokens.Should().Be(3000);
        summary.TotalOutputTokens.Should().Be(450);
        summary.TotalSavedTokens.Should().Be(2550);
    }

    [Fact]
    public async Task GetSummaryAsync_CommandDetail_AggregatesInputAndOutputTokensCorrectly()
    {
        // Kills += → -= mutations on per-command totalInputCmd/totalOutputCmd aggregation
        await _sut.RecordAsync(MakeRecord(inputTokens: 500, outputTokens: 100, savedTokens: 400));
        await _sut.RecordAsync(MakeRecord(inputTokens: 1500, outputTokens: 200, savedTokens: 1300));

        var summary = await _sut.GetSummaryAsync(30, null);

        var detail = summary.CommandDetails["build"];
        detail.TotalInputTokens.Should().Be(2000);
        detail.TotalOutputTokens.Should().Be(300);
        detail.TotalSavedTokens.Should().Be(1700);
    }

    [Fact]
    public async Task GetSummaryAsync_AverageSavingsPct_IsComputedFromTotalSavedOverTotalInput()
    {
        // Kills arithmetic mutations: totalSaved / totalInput * 100.0 vs / 100.0 vs * totalInput
        // totalSaved = 400 + 900 = 1300; totalInput = 1000 + 3000 = 4000; pct = 1300/4000*100 = 32.5%
        await _sut.RecordAsync(MakeRecord(inputTokens: 1000, outputTokens: 600, savedTokens: 400, savingsPct: 40.0));
        await _sut.RecordAsync(MakeRecord(inputTokens: 3000, outputTokens: 2100, savedTokens: 900, savingsPct: 30.0));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.AverageSavingsPercentage.Should().BeApproximately(32.5, 0.01);
    }

    [Fact]
    public async Task RecordAsync_OnDatabaseWithExistingSuccessColumn_DoesNotThrow()
    {
        // Kills equality mutation: (long)count > 0 → (long)count < 0
        // With < 0 mutation, columnExists is always false and ALTER TABLE always runs,
        // which fails with "duplicate column name: success" on the second initialization.
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "tracking.db");
        try
        {
            await using (var firstTracker = new SqliteTracker($"Data Source={tempPath}"))
            {
                await firstTracker.RecordAsync(MakeRecord());
            }

            SqliteConnection.ClearAllPools();

            // Second tracker opens same DB where success column already exists
            await using var secondTracker = new SqliteTracker($"Data Source={tempPath}");
            await secondTracker.RecordAsync(MakeRecord());

            var history = await secondTracker.GetHistoryAsync(1, null);
            history.Should().NotBeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var dir = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}

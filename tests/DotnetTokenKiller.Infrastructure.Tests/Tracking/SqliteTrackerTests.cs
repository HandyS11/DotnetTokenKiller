using System.Globalization;
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
        bool success = true,
        RunOutcome outcome = RunOutcome.Filtered,
        RunSource source = RunSource.Run)
    {
        return new CommandRecord(
            timestamp ?? DateTimeOffset.UtcNow,
            command,
            projectPath,
            new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
            TimeSpan.FromMilliseconds(500))
        {
            Success = success,
            Outcome = outcome,
            Source = source
        };
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
    public async Task LegacyDbWhoseColumnThePragmaProbeMisses_StillInitializesAsync()
    {
        // The probe compares names case-sensitively while SQLite treats them case-insensitively, so
        // a column spelled `Success` reads as missing and the ALTER then reports it as a duplicate.
        // That is the same "someone already added it" condition a concurrent initializer produces,
        // and it must not fail the run — the column is there either way, which is all that matters.
        var dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "legacy.db");
        try
        {
            await CreateLegacySchemaByHandAsync(dbPath);
            await using (var legacy = new SqliteConnection($"Data Source={dbPath}"))
            {
                await legacy.OpenAsync();
                await using var alter = legacy.CreateCommand();
                alter.CommandText = "ALTER TABLE commands ADD COLUMN Success INTEGER NOT NULL DEFAULT 1";
                await alter.ExecuteNonQueryAsync();
            }

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
        buildDetail.SuccessDetail.RunCount.Should().Be(2);
        buildDetail.SuccessDetail.TotalSavedTokens.Should().Be(1700);
        buildDetail.SuccessDetail.AverageSavingsPercentage.Should().BeApproximately(80.0, 0.001);
        buildDetail.FailureDetail.Should().NotBeNull();
        buildDetail.FailureDetail.RunCount.Should().Be(1);
        buildDetail.FailureDetail.TotalSavedTokens.Should().Be(500);
        buildDetail.FailureDetail.AverageSavingsPercentage.Should().BeApproximately(50.0, 0.001);
        // Combined: run-count-weighted average of per-status SQL AVG values
        buildDetail.AverageSavingsPercentage.Should().BeApproximately(70.0, 0.001);
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesExecutionTime()
    {
        // MakeRecord always stamps 500ms per run.
        await _sut.RecordAsync(MakeRecord(command: "build", success: true));
        await _sut.RecordAsync(MakeRecord(command: "build", success: true));
        await _sut.RecordAsync(MakeRecord(command: "build", success: false));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        var build = summary.CommandDetails["build"];
        build.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        build.SuccessDetail!.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1000));
        build.FailureDetail!.TotalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(500));
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

    [Fact]
    public async Task RecordAsync_RoundTripsOutcome()
    {
        await _sut.RecordAsync(MakeRecord(outcome: RunOutcome.PassthroughMeasured));

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }

    [Fact]
    public async Task RecordAsync_DefaultsToFiltered_WhenOutcomeNotSupplied()
    {
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public async Task RecordAsync_RoundTripsPipeSourceAsync()
    {
        await _sut.RecordAsync(MakeRecord(source: RunSource.Pipe));

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Source.Should().Be(RunSource.Pipe);
    }

    [Fact]
    public async Task RecordAsync_DefaultsToRunSourceAsync()
    {
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Source.Should().Be(RunSource.Run);
    }

    [Fact]
    public async Task LegacyDbWithoutSourceColumn_IsMigrated_AndReadsAsRunAsync()
    {
        // Every row written before this column existed came from a run dtk executed itself,
        // so 'Run' is the correct backfill, not merely a convenient default.
        var dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "legacy.db");
        try
        {
            await CreateLegacySchemaByHandAsync(dbPath);

            await using var tracker = new SqliteTracker($"Data Source={dbPath}");
            await tracker.RecordAsync(MakeRecord(), default);

            var history = await tracker.GetHistoryAsync(1, null);
            history.Should().ContainSingle().Which.Source.Should().Be(RunSource.Run);
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
    public async Task GetSummaryAsync_IsUnchanged_WhenPassthroughRowsAreAdded()
    {
        // The guarantee that shipping coverage tracking does not move anybody's gain numbers.
        // If the outcome exclusion is ever dropped, this fails loudly.
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 1000, outputTokens: 100, savedTokens: 900));
        var before = await _sut.GetSummaryAsync(1, null);

        await _sut.RecordAsync(MakeRecord(
            "publish",
            inputTokens: 50_000,
            outputTokens: 50_000,
            savedTokens: 0,
            savingsPct: 0.0,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("run", inputTokens: 0, outputTokens: 0, savedTokens: 0,
            savingsPct: 0.0, outcome: RunOutcome.PassthroughUnmeasured));

        var after = await _sut.GetSummaryAsync(1, null);

        after.TotalCommands.Should().Be(before.TotalCommands);
        after.TotalInputTokens.Should().Be(before.TotalInputTokens);
        after.TotalOutputTokens.Should().Be(before.TotalOutputTokens);
        after.TotalSavedTokens.Should().Be(before.TotalSavedTokens);
        after.AverageSavingsPercentage.Should().BeApproximately(before.AverageSavingsPercentage, 0.001);
        after.CommandDetails.Should().NotContainKey("publish");
        after.CommandDetails.Should().NotContainKey("run");
    }

    [Fact]
    public async Task GetHistoryAsync_UnrecognizedOutcome_FallsBackToFiltered()
    {
        // Bypasses RecordAsync (which only accepts the enum) via a raw INSERT, simulating a row
        // written by a newer dtk with an outcome value this build does not know.
        await _sut.RecordAsync(MakeRecord()); // forces schema init so `commands` already exists
        await InsertRawOutcomeRowAsync("bogus-cmd", "TotallyUnknownOutcome");

        var history = await _sut.GetHistoryAsync(1, null, "bogus-cmd");

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public async Task GetCoverageAsync_UnrecognizedOutcome_FallsBackToFiltered()
    {
        await _sut.RecordAsync(MakeRecord()); // forces schema init so `commands` already exists
        await InsertRawOutcomeRowAsync("bogus-cmd", "TotallyUnknownOutcome");

        var coverage = await _sut.GetCoverageAsync(1, null, "bogus-cmd");

        coverage.Entries.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public async Task GetHistoryAsync_DifferentlyCasedOutcome_ParsesInsteadOfFallingBackToFiltered()
    {
        await _sut.RecordAsync(MakeRecord()); // forces schema init so `commands` already exists
        await InsertRawOutcomeRowAsync("bogus-cmd", "passthroughmeasured");

        var history = await _sut.GetHistoryAsync(1, null, "bogus-cmd");

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }

    [Fact]
    public async Task GetCoverageAsync_DifferentlyCasedOutcome_ParsesInsteadOfFallingBackToFiltered()
    {
        await _sut.RecordAsync(MakeRecord()); // forces schema init so `commands` already exists
        await InsertRawOutcomeRowAsync("bogus-cmd", "passthroughmeasured");

        var coverage = await _sut.GetCoverageAsync(1, null, "bogus-cmd");

        coverage.Entries.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }

    /// <summary>
    /// Inserts a row directly through the tracker's own live connection (found via reflection),
    /// bypassing <see cref="SqliteTracker.RecordAsync"/> so an outcome string outside the
    /// <see cref="RunOutcome"/> enum can be persisted, the way a newer dtk build might.
    /// </summary>
    /// <param name="command">The command name to store on the row.</param>
    /// <param name="outcome">The raw, possibly-unrecognized outcome string to store on the row.</param>
    private async Task InsertRawOutcomeRowAsync(string command, string outcome)
    {
        var connectionField = typeof(SqliteTracker)
            .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var connection = (SqliteConnection)connectionField.GetValue(_sut)!;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                              saved_tokens, savings_percentage, execution_time_ms, success, outcome)
                          VALUES (@ts, @cmd, '/proj', 100, 100, 0, 0.0, 10.0, 1, @outcome)
                          """;
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@cmd", command);
        cmd.Parameters.AddWithValue("@outcome", outcome);
        await cmd.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData(RunOutcome.RawTailFallback)]
    [InlineData(RunOutcome.FilterFaulted)]
    public async Task GetSummaryAsync_IncludesDegradedFilteredRuns(RunOutcome outcome)
    {
        // These ran a filter — badly — so they still represent real savings and stay in the math.
        await _sut.RecordAsync(MakeRecord("build", outcome: outcome));

        var summary = await _sut.GetSummaryAsync(1, null);

        summary.TotalCommands.Should().Be(1);
        summary.CommandDetails.Should().ContainKey("build");
    }

    [Fact]
    public async Task Schema_AddsOutcomeColumnToLegacyDatabase_DefaultingExistingRowsToFiltered()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dtk-legacy-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        try
        {
            // Build the pre-outcome schema by hand and seed a row, as an older dtk would have.
            await using (var legacy = new SqliteConnection(connectionString))
            {
                await legacy.OpenAsync();
                await using var cmd = legacy.CreateCommand();
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
                                      execution_time_ms REAL NOT NULL,
                                      success INTEGER NOT NULL DEFAULT 1
                                  );
                                  INSERT INTO commands
                                      (timestamp, command, project_path, input_tokens, output_tokens,
                                       saved_tokens, savings_percentage, execution_time_ms, success)
                                  VALUES (@ts, 'build', '/legacy', 900, 90, 810, 90.0, 12.0, 1);
                                  """;
                cmd.Parameters.AddWithValue("@ts",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await cmd.ExecuteNonQueryAsync();
            }

            await using var tracker = new SqliteTracker(connectionString);
            var history = await tracker.GetHistoryAsync(1, null);

            var stored = history.Should().ContainSingle().Subject;
            stored.Command.Should().Be("build");
            stored.Outcome.Should().Be(RunOutcome.Filtered);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task GetCoverageAsync_RanksByInputTokensDescending()
    {
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 500, outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 9000, outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("list package", inputTokens: 3000,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Select(e => e.Command)
            .Should().ContainInOrder("publish", "list package", "pack");
    }

    [Fact]
    public async Task GetCoverageAsync_BreaksTokenTiesByRunCount()
    {
        // Every PassthroughUnmeasured row has zero tokens. Without the tiebreak the most-run
        // unmeasured command would sort arbitrarily and stay invisible.
        for (var i = 0; i < 5; i++)
        {
            await _sut.RecordAsync(MakeRecord("watch", inputTokens: 0,
                outcome: RunOutcome.PassthroughUnmeasured));
        }

        await _sut.RecordAsync(MakeRecord("run", inputTokens: 0, outcome: RunOutcome.PassthroughUnmeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Select(e => e.Command).Should().ContainInOrder("watch", "run");
    }

    [Fact]
    public async Task GetCoverageAsync_GroupsByCommandAndOutcome()
    {
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 100,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 200,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 0,
            outcome: RunOutcome.PassthroughUnmeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        var measured = coverage.Entries.Should()
            .ContainSingle(e => e.Command == "publish" && e.Outcome == RunOutcome.PassthroughMeasured).Subject;
        measured.RunCount.Should().Be(2);
        measured.TotalInputTokens.Should().Be(300);

        coverage.Entries.Should()
            .ContainSingle(e => e.Command == "publish" && e.Outcome == RunOutcome.PassthroughUnmeasured);
    }

    [Fact]
    public async Task GetCoverageAsync_IncludesFilteredRuns_SoCoveredAndUncoveredCanBeCompared()
    {
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 7000));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 100,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Should().Contain(e => e.Command == "build" && e.Outcome == RunOutcome.Filtered);
        coverage.TotalRuns.Should().Be(2);
    }

    [Fact]
    public async Task GetCoverageAsync_TotalUnfilteredInputTokens_CountsOnlyPassthroughRows()
    {
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 7000));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.TotalUnfilteredInputTokens.Should().Be(1000);
    }

    [Fact]
    public async Task GetCoverageAsync_HonoursTheCommandFilter()
    {
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null, "publish");

        coverage.Entries.Should().ContainSingle().Which.Command.Should().Be("publish");
    }

    [Fact]
    public async Task GetCoverageAsync_HonoursTheProjectFilter()
    {
        await _sut.RecordAsync(MakeRecord("publish", "/a", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", "/b", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, "/a");

        coverage.Entries.Should().ContainSingle().Which.TotalInputTokens.Should().Be(400);
    }

    [Fact]
    public async Task GetCoverageAsync_ReturnsEmpty_WhenNothingRecorded()
    {
        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Should().BeEmpty();
        coverage.TotalRuns.Should().Be(0);
        coverage.TotalUnfilteredInputTokens.Should().Be(0);
    }

    [Fact]
    public async Task GetCoverageAsync_SplitsSameCommandBySourceAsync()
    {
        await _sut.RecordAsync(MakeRecord(command: "build", source: RunSource.Run));
        await _sut.RecordAsync(MakeRecord(command: "build", source: RunSource.Pipe));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Should().HaveCount(2);
        coverage.Entries.Select(e => e.Source)
            .Should().BeEquivalentTo([RunSource.Run, RunSource.Pipe]);
        coverage.Entries.Should().OnlyContain(e => e.Command == "build");
    }

    [Fact]
    public async Task GetSummaryAsync_TotalsAreUnaffectedByTheSourceSplitAsync()
    {
        // The default dashboard aggregates across sources: splitting coverage rows must not
        // change what `dtk gain` reports.
        await _sut.RecordAsync(MakeRecord(command: "build", source: RunSource.Run));
        await _sut.RecordAsync(MakeRecord(command: "build", source: RunSource.Pipe));

        var summary = await _sut.GetSummaryAsync(1, null);

        summary.TotalCommands.Should().Be(2);
        summary.TotalSavedTokens.Should().Be(1700);
    }

    [Fact]
    public async Task WarmUpAsync_ThenRecordAsync_PersistsExactlyOneRowAsync()
    {
        await _sut.WarmUpAsync();
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle();
    }

    [Fact]
    public async Task Dispose_TrackerNeverUsed_DoesNotThrowEvenTwiceAsync()
    {
        // A tracker built for a run with tracking disabled never creates its connection.
        var syncTracker = new SqliteTracker("Data Source=:memory:");
        var asyncTracker = new SqliteTracker("Data Source=:memory:");

        var disposeSync = () =>
        {
            syncTracker.Dispose();
            syncTracker.Dispose();
        };
        var disposeAsync = async () =>
        {
            await asyncTracker.DisposeAsync();
            await asyncTracker.DisposeAsync();
        };

        disposeSync.Should().NotThrow();
        await disposeAsync.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordAsync_AfterWarmUpFailedBeforeOpening_RecoversAsync()
    {
        // A file sits where the database's directory must go, so creating the directory throws
        // before any connection exists. Once it is gone, recording must work.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        var blocker = Path.Combine(root, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory");
        try
        {
            await using var tracker =
                new SqliteTracker($"Data Source={Path.Combine(blocker, "tracking.db")};Pooling=False");

            var warmUp = () => tracker.WarmUpAsync();
            await warmUp.Should().ThrowAsync<IOException>();

            File.Delete(blocker);
            await tracker.RecordAsync(MakeRecord());

            (await tracker.GetHistoryAsync(1, null)).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAsync_AfterWarmUpFailedOnAnOpenedConnection_RecoversAsync()
    {
        // A file that is not a SQLite database fails during setup, after the connection was
        // created. The retry must start from a fresh connection, not reopen the failed one.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        var dbPath = Path.Combine(root, "tracking.db");
        await File.WriteAllTextAsync(dbPath, new string('x', 4096));
        try
        {
            await using var tracker = new SqliteTracker($"Data Source={dbPath};Pooling=False");

            var warmUp = () => tracker.WarmUpAsync();
            await warmUp.Should().ThrowAsync<SqliteException>();

            File.Delete(dbPath);
            await tracker.RecordAsync(MakeRecord());

            (await tracker.GetHistoryAsync(1, null)).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_WhileWarmUpIsRunning_DoesNotThrowAsync()
    {
        // A warm-up can outlive its run (the child failed to launch and the tracker is disposed on
        // the way out). Disposal runs on the user's path and must be clean; the warm-up itself may
        // lose the race and see ObjectDisposedException, which TrackingWarmUp discards. Looped
        // because the race is timing-dependent: this guards the fix but cannot prove its absence.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var dbPath = Path.Combine(root, $"race-{attempt}.db");
                var tracker = new SqliteTracker($"Data Source={dbPath};Pooling=False");
                var warmUp = Task.Run(() => tracker.WarmUpAsync());

                var dispose = async () => await tracker.DisposeAsync();

                await dispose.Should().NotThrowAsync();
                try
                {
                    await warmUp;
                }
                catch (ObjectDisposedException)
                {
                    // Expected when disposal won the race; see the comment above.
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

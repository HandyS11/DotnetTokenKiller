using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

/// <summary>The tracker on a file data source, where the journal is in play (":memory:" has no directory).</summary>
public sealed class SqliteTrackerJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtk-tracker-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_root, "tracking.db");

    private string PendingDir => DbPath + ".pending";

    private SqliteTracker Create(int foldThreshold = SqliteTracker.DefaultFoldThreshold, int retentionDays = 90) =>
        new($"Data Source={DbPath};Pooling=False", retentionDays, foldThreshold);

    private int PendingFiles => Directory.Exists(PendingDir) ? Directory.GetFiles(PendingDir, "*.json").Length : 0;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAsync_WritesAJournalFileAndOpensNoDatabase()
    {
        await using var sut = Create();

        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        PendingFiles.Should().Be(1);
        File.Exists(DbPath).Should().BeFalse("a tracked run must not touch SQLite");
    }

    [Fact]
    public async Task RecordAsync_JournalsBesideTheDatabaseUnderItsOwnName()
    {
        // The database path is user-configurable, so its directory may hold anything. A generic
        // folder name there could belong to someone else, and a fold deletes what does not parse.
        var foreign = Path.Combine(_root, "pending");
        Directory.CreateDirectory(foreign);
        var foreignFile = Path.Combine(foreign, "foo.json");
        const string foreignContent = "not a dtk record";
        await File.WriteAllTextAsync(foreignFile, foreignContent);
        await using var sut = Create();

        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
        PendingFiles.Should().Be(1, "the record lands in the journal named after the database");
        var summary = await sut.GetSummaryAsync(days: 3650, projectPath: null);
        await sut.ResetAsync();

        summary.TotalCommands.Should().Be(1);
        Directory.GetFileSystemEntries(foreign).Should().Equal(foreignFile);
        (await File.ReadAllTextAsync(foreignFile)).Should().Be(foreignContent);
    }

    [Fact]
    public async Task GetSummaryAsync_FoldsTheJournalFirst()
    {
        await using var sut = Create();
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("build"));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("build"));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("test"));

        var summary = await sut.GetSummaryAsync(days: 3650, projectPath: null);

        summary.TotalCommands.Should().Be(3);
        PendingFiles.Should().Be(0);
        File.Exists(DbPath).Should().BeTrue();
    }

    [Fact]
    public async Task Reports_MatchWhatADirectInsertReports()
    {
        // The journal changes where a run waits, not what gain or --coverage report: the in-memory
        // tracker still inserts directly, so it is the reference.
        CommandRecord[] records =
        [
            Record("build", 1000, 150, RunOutcome.Filtered, RunSource.Run, success: true),
            Record("build", 2500, 400, RunOutcome.Filtered, RunSource.Pipe, success: false),
            Record("test", 800, 90, RunOutcome.RawTailFallback, RunSource.Run, success: true),
            Record("publish", 5000, 5000, RunOutcome.PassthroughMeasured, RunSource.Run, success: true),
            Record("run", 0, 0, RunOutcome.PassthroughUnmeasured, RunSource.Run, success: true)
        ];
        await using var direct = new SqliteTracker("Data Source=:memory:");
        await using var journaled = Create();
        foreach (var record in records)
        {
            await direct.RecordAsync(record);
            await journaled.RecordAsync(record);
        }

        var summary = await journaled.GetSummaryAsync(days: 3650, projectPath: null);
        var coverage = await journaled.GetCoverageAsync(days: 3650, projectPath: null);

        summary.Should().BeEquivalentTo(await direct.GetSummaryAsync(days: 3650, projectPath: null));
        coverage.Should().BeEquivalentTo(await direct.GetCoverageAsync(days: 3650, projectPath: null));
        summary.TotalCommands.Should().Be(3, "the two passthrough runs stay out of the savings");
    }

    private static CommandRecord Record(
        string command, int input, int output, RunOutcome outcome, RunSource source, bool success) =>
        new(DateTimeOffset.UtcNow.AddMinutes(-input % 97), command, "/proj",
            new TokenStatistics(input, output, input - output, input == 0 ? 0 : 100.0 * (input - output) / input),
            TimeSpan.FromMilliseconds(input / 10.0))
        {
            Success = success,
            Outcome = outcome,
            Source = source
        };

    [Fact]
    public async Task GetHistoryAsync_ReturnsFoldedRecordsWithEveryField()
    {
        await using var sut = Create();
        var record = PendingRecordJournalTests.MakeRecord();
        await sut.RecordAsync(record);

        var history = await sut.GetHistoryAsync(days: 3650, projectPath: null);

        history.Should().ContainSingle().Which.Should().BeEquivalentTo(record);
    }

    [Fact]
    public async Task GetHistoryAsync_AppliesRetentionToTheRecordsItFolds()
    {
        // The database is empty when the read initializes it, so only the fold's own retention can
        // drop the 100-day-old record.
        await using var sut = Create(retentionDays: 90);
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("expired", DateTimeOffset.UtcNow.AddDays(-100)));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("fresh", DateTimeOffset.UtcNow));

        var history = await sut.GetHistoryAsync(days: 3650, projectPath: null);

        history.Should().ContainSingle().Which.Command.Should().Be("fresh");
    }

    [Fact]
    public async Task GetCoverageAsync_AndCleanupAsync_FoldFirst()
    {
        await using var sut = Create(retentionDays: 90);
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-100)));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord(timestamp: DateTimeOffset.UtcNow));

        await sut.CleanupAsync(retentionDays: 90);
        var coverage = await sut.GetCoverageAsync(days: 3650, projectPath: null);

        coverage.TotalRuns.Should().Be(1, "retention runs at fold time and the 100-day-old row is gone");
        PendingFiles.Should().Be(0);
    }

    [Fact]
    public async Task ResetAsync_ClearsTheJournalToo()
    {
        await using var sut = Create();
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        await sut.ResetAsync();

        PendingFiles.Should().Be(0);
        (await sut.GetHistoryAsync(days: 3650, projectPath: null)).Should().BeEmpty();
    }

    [Fact]
    public async Task ResetAsync_JournalClearFails_DeletesNoRowsAndNeverRefoldsACommittedClaim()
    {
        if (OperatingSystem.IsWindows())
        {
            // POSIX permission bits (used below to make Clear fail) are not meaningful on Windows,
            // where the same failure is a sharing violation on a file in the claim directory.
            return;
        }

        // A fold that committed claim X and died before deleting folding-X/ leaves the directory
        // behind; the folds table is what tells the next fold not to refold it. If a reset deleted
        // the folds rows and only then failed to clear the journal, folding-X/ would survive with
        // its id forgotten, and the next read would refold X into the table the user just reset.
        await using var sut = Create();
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
        var pendingRecord = Directory.GetFiles(PendingDir, "*.json").Should().ContainSingle().Subject;
        var recordJson = await File.ReadAllTextAsync(pendingRecord);
        (await sut.GetSummaryAsync(days: 3650, projectPath: null)).TotalCommands.Should().Be(1);

        var committedFold = await ReadTablesAsync();
        committedFold.Folds.Should().Be(1);
        var claim = Path.Combine(PendingDir, "folding-" + committedFold.LastFoldId);
        Directory.CreateDirectory(claim);
        await File.WriteAllTextAsync(Path.Combine(claim, Path.GetFileName(pendingRecord)), recordJson);

        File.SetUnixFileMode(claim, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var reset = () => sut.ResetAsync();

            await reset.Should().ThrowAsync<Exception>()
                .Where(ex => ex is IOException || ex is UnauthorizedAccessException);
            (await ReadTablesAsync()).Should().Be(committedFold, "a reset whose journal clear failed must delete nothing");
        }
        finally
        {
            File.SetUnixFileMode(claim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var summary = await sut.GetSummaryAsync(days: 3650, projectPath: null);

        summary.TotalCommands.Should().Be(1, "the leftover claim's id is still in folds, so it is deleted, not refolded");
        Directory.Exists(claim).Should().BeFalse();
    }

    /// <summary>Reads the database directly, bypassing the tracker and so its fold.</summary>
    private async Task<(long Commands, long Folds, string LastFoldId)> ReadTablesAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT (SELECT COUNT(*) FROM commands), (SELECT COUNT(*) FROM folds),
                                     COALESCE((SELECT MAX(id) FROM folds), '')
                              """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2));
    }

    [Fact]
    public async Task WarmUpAsync_BelowTheThreshold_CreatesTheDirectoryAndOpensNothing()
    {
        await using (var sut = Create(foldThreshold: 64))
        {
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.WarmUpAsync();
        }

        Directory.Exists(PendingDir).Should().BeTrue();
        PendingFiles.Should().Be(1);
        File.Exists(DbPath).Should().BeFalse();
    }

    [Fact]
    public async Task WarmUpAsync_AtTheThreshold_FoldsInTheBackground()
    {
        await using (var sut = Create(foldThreshold: 2))
        {
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.WarmUpAsync();
            // DisposeAsync waits for the background fold, so the assertions below see its result.
        }

        PendingFiles.Should().Be(0);
        File.Exists(DbPath).Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_AfterDispose_Throws()
    {
        var sut = Create();
        await sut.DisposeAsync();

        var act = () => sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}

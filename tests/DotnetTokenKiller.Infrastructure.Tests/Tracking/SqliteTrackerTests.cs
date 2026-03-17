using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
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
        DateTimeOffset? timestamp = null)
    {
        return new CommandRecord(
            timestamp ?? DateTimeOffset.UtcNow,
            command,
            projectPath,
            inputTokens,
            outputTokens,
            savedTokens,
            savingsPct,
            TimeSpan.FromMilliseconds(500));
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
            ts);

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
    }

    [Fact]
    public async Task RecordAsync_TriggersCleanup_DeletesOldRecords()
    {
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-91);
        await _sut.RecordAsync(MakeRecord(timestamp: oldTimestamp));

        // Cleanup runs every 50 inserts; insert enough to trigger it
        for (var i = 0; i < 50; i++)
        {
            await _sut.RecordAsync(MakeRecord());
        }

        var history = await _sut.GetHistoryAsync(365, null);

        // The old record should have been cleaned up; only the 50 recent ones remain
        history.Should().HaveCount(50);
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
}

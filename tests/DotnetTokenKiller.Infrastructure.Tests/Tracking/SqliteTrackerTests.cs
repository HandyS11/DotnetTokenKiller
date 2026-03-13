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
        DateTimeOffset? timestamp = null) =>
        new(
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            Command: command,
            ProjectPath: projectPath,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            SavedTokens: savedTokens,
            SavingsPercentage: savingsPct,
            ExecutionTime: TimeSpan.FromMilliseconds(500));

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
        var ts = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var record = MakeRecord(
            command: "test",
            projectPath: "/my/project",
            inputTokens: 2000,
            outputTokens: 200,
            savedTokens: 1800,
            savingsPct: 90.0,
            timestamp: ts);

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
        await _sut.RecordAsync(MakeRecord()); // triggers cleanup

        var history = await _sut.GetHistoryAsync(365, null);

        history.Should().HaveCount(1); // only the recent record remains
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesCorrectly()
    {
        await _sut.RecordAsync(MakeRecord(command: "build", savedTokens: 800));
        await _sut.RecordAsync(MakeRecord(command: "build", savedTokens: 900));
        await _sut.RecordAsync(MakeRecord(command: "test", savedTokens: 500));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalCommands.Should().Be(3);
        summary.TotalSavedTokens.Should().Be(2200);
        summary.SavedByCommand["build"].Should().Be(1700);
        summary.SavedByCommand["test"].Should().Be(500);
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
        await _sut.RecordAsync(MakeRecord(command: "first", timestamp: ts1));
        await _sut.RecordAsync(MakeRecord(command: "second", timestamp: ts2));

        var history = await _sut.GetHistoryAsync(1, null);

        history[0].Command.Should().Be("second"); // most recent first
        history[1].Command.Should().Be("first");
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
}

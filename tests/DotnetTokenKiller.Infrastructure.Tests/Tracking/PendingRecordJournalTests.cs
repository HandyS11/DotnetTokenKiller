using System.Text.Json;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

public sealed class PendingRecordJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtk-journal-" + Guid.NewGuid().ToString("N"));

    private string PendingDir => Path.Combine(_root, "pending");

    private PendingRecordJournal Journal => new(PendingDir);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    internal static CommandRecord MakeRecord(string command = "build", DateTimeOffset? timestamp = null) =>
        new(timestamp ?? DateTimeOffset.UtcNow.AddHours(-1), command, "/proj",
            new TokenStatistics(1000, 150, 850, 85.0), TimeSpan.FromMilliseconds(500))
        {
            Success = false,
            Outcome = RunOutcome.RawTailFallback,
            Source = RunSource.Pipe
        };

    [Fact]
    public async Task WriteAsync_CreatesOneFileThatRoundTripsTheRecord()
    {
        var record = MakeRecord();
        await Journal.WriteAsync(record);

        var files = Directory.GetFiles(PendingDir, "*.json");
        files.Should().HaveCount(1);
        var pending = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(files[0]), PendingRecordJsonContext.Default.PendingRecord)!;
        pending.Version.Should().Be(PendingRecord.CurrentVersion);
        pending.ToCommandRecord().Should().BeEquivalentTo(record);
    }

    [Fact]
    public async Task WriteAsync_ParallelWriters_CreateDistinctFiles()
    {
        var journal = Journal;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => journal.WriteAsync(MakeRecord())));

        journal.Count().Should().Be(20);
    }

    [Fact]
    public void Count_MissingDirectory_IsZero()
    {
        Journal.Count().Should().Be(0);
    }
}

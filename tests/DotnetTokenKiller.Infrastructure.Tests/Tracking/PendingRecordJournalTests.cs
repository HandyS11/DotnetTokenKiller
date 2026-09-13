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

    [Fact]
    public async Task WriteAsync_LeavesNoTemporaryFile()
    {
        await Journal.WriteAsync(MakeRecord());

        Directory.GetFiles(PendingDir, "*.json").Should().HaveCount(1);
        Directory.GetFiles(PendingDir, "*.tmp").Should().BeEmpty();
    }

    private static Task<bool> NeverCommittedAsync(string id, CancellationToken ct) => Task.FromResult(false);

    [Fact]
    public async Task FoldAsync_CommitsEveryRecordOnceAndDeletesTheFiles()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord("build"));
        await journal.WriteAsync(MakeRecord("test"));
        var committed = new List<CommandRecord>();
        var ids = new List<string>();

        var outcome = await journal.FoldAsync(NeverCommittedAsync, (foldIds, records, _) =>
        {
            ids.AddRange(foldIds);
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 2, 0));
        committed.Select(r => r.Command).Should().BeEquivalentTo("build", "test");
        ids.Should().ContainSingle();
        journal.Count().Should().Be(0);
        Directory.GetDirectories(PendingDir, "folding-*").Should().BeEmpty();
    }

    [Fact]
    public async Task FoldAsync_NothingPending_DoesNotCommit()
    {
        var commits = 0;

        var outcome = await Journal.FoldAsync(NeverCommittedAsync, (_, _, _) => { commits++; return Task.CompletedTask; }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 0, 0));
        commits.Should().Be(0);
    }

    [Fact]
    public async Task FoldAsync_CommitThrows_LeavesTheClaimForTheNextFold()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());

        var act = () => journal.FoldAsync(NeverCommittedAsync, (_, _, _) => throw new IOException("disk full"), wait: true);

        await act.Should().ThrowAsync<IOException>();
        Directory.GetDirectories(PendingDir, "folding-*").Should().ContainSingle()
            .Which.Should().Match(dir => Directory.GetFiles(dir, "*.json").Length == 1);

        var recovered = new List<CommandRecord>();
        var ids = new List<string>();
        var outcome = await journal.FoldAsync(NeverCommittedAsync, (foldIds, records, _) =>
        {
            ids.AddRange(foldIds);
            recovered.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Records.Should().Be(1);
        recovered.Should().ContainSingle();
        ids.Should().ContainSingle("the old claim's id is committed under its own name, so a die-after-commit can never refold it");
        Directory.GetDirectories(PendingDir, "folding-*").Should().BeEmpty();
    }

    [Fact]
    public async Task FoldAsync_ClaimAlreadyCommitted_IsDeletedWithoutASecondCommit()
    {
        var claim = Path.Combine(PendingDir, "folding-abc");
        Directory.CreateDirectory(claim);
        await File.WriteAllTextAsync(Path.Combine(claim, "1.json"), "{}");
        var commits = 0;

        var outcome = await Journal.FoldAsync(
            (id, _) => Task.FromResult(id == "abc"),
            (_, _, _) => { commits++; return Task.CompletedTask; },
            wait: true);

        outcome.Should().Be(new FoldOutcome(true, 0, 0));
        commits.Should().Be(0);
        Directory.Exists(claim).Should().BeFalse();
    }

    [Fact]
    public async Task FoldAsync_TruncatedFile_IsSkippedDeletedAndCounted()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());
        await File.WriteAllTextAsync(Path.Combine(PendingDir, "0000000000000000000-1-torn.json"), "{\"Version\":1,\"Timesta");
        var committed = new List<CommandRecord>();

        var outcome = await journal.FoldAsync(NeverCommittedAsync, (_, records, _) =>
        {
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 1, 1));
        committed.Should().ContainSingle();
        journal.Count().Should().Be(0);
    }

    [Fact]
    public async Task FoldAsync_ValidJsonThatIsNotARecord_IsSkippedDeletedAndCounted()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());
        await File.WriteAllTextAsync(Path.Combine(PendingDir, "0000000000000000000-1-empty.json"), "{}");
        var committed = new List<CommandRecord>();

        var outcome = await journal.FoldAsync(NeverCommittedAsync, (_, records, _) =>
        {
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 1, 1));
        committed.Should().ContainSingle();
        journal.Count().Should().Be(0);
        Directory.GetDirectories(PendingDir, "folding-*").Should().BeEmpty();
    }

    [Fact]
    public async Task FoldAsync_IgnoresAFreshTemporaryFileAndDeletesAStaleOne()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());
        var freshTemp = Path.Combine(PendingDir, "x.tmp");
        var staleTemp = Path.Combine(PendingDir, "y.tmp");
        await File.WriteAllTextAsync(freshTemp, string.Empty);
        await File.WriteAllTextAsync(staleTemp, string.Empty);
        File.SetLastWriteTimeUtc(staleTemp, DateTime.UtcNow.AddHours(-2));
        var committed = new List<CommandRecord>();

        var outcome = await journal.FoldAsync(NeverCommittedAsync, (_, records, _) =>
        {
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Records.Should().Be(1);
        committed.Should().ContainSingle();
        File.Exists(freshTemp).Should().BeTrue();
        File.Exists(staleTemp).Should().BeFalse();
    }

    [Fact]
    public async Task FoldAsync_HandsRecordsToCommitInFileNameOrder()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord("first"));
        await journal.WriteAsync(MakeRecord("second"));
        await journal.WriteAsync(MakeRecord("third"));

        var oldClaim = Path.Combine(PendingDir, "folding-old");
        Directory.CreateDirectory(oldClaim);
        var lastFile = Directory.GetFiles(PendingDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Last();
        File.Move(lastFile, Path.Combine(oldClaim, Path.GetFileName(lastFile)));

        var committed = new List<CommandRecord>();

        var outcome = await journal.FoldAsync(NeverCommittedAsync, (_, records, _) =>
        {
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Records.Should().Be(3);
        committed.Select(r => r.Command).Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task FoldAsync_LockHeld_ReturnsAtOnceWithoutWaitAndTimesOutWithWait()
    {
        var journal = new PendingRecordJournal(PendingDir, lockWait: TimeSpan.FromMilliseconds(200));
        await journal.WriteAsync(MakeRecord());
        Directory.CreateDirectory(PendingDir);
        await using var held = new FileStream(
            Path.Combine(PendingDir, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var skipped = await journal.FoldAsync(NeverCommittedAsync, (_, _, _) => Task.CompletedTask, wait: false);
        var waiting = () => journal.FoldAsync(NeverCommittedAsync, (_, _, _) => Task.CompletedTask, wait: true);

        skipped.Folded.Should().BeFalse();
        await waiting.Should().ThrowAsync<TimeoutException>();
        journal.Count().Should().Be(1, "nothing was claimed while the lock was held elsewhere");
    }

    [Fact]
    public async Task Clear_RemovesFilesAndClaimsButKeepsTheLock()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());
        Directory.CreateDirectory(Path.Combine(PendingDir, "folding-old"));
        await File.WriteAllTextAsync(Path.Combine(PendingDir, ".lock"), string.Empty);

        journal.Clear();

        journal.Count().Should().Be(0);
        Directory.GetDirectories(PendingDir).Should().BeEmpty();
        File.Exists(Path.Combine(PendingDir, ".lock")).Should().BeTrue();
    }
}

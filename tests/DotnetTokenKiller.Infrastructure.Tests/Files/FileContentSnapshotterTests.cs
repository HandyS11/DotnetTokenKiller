using System.Diagnostics;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Infrastructure.Files;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Files;

public sealed class FileContentSnapshotterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtk-snap-" + Guid.NewGuid().ToString("N"));

    public FileContentSnapshotterTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static bool IsCSharp(string fileName) => fileName.EndsWith(".cs", StringComparison.Ordinal);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Take_HashesFiles_SkipsIgnoredDirectories()
    {
        Write("a.cs", "a");
        Write("src/b.cs", "b");
        foreach (var ignored in new[] { "obj", "bin", ".git", "node_modules", "artifacts", "StrykerOutput", ".hidden" })
        {
            Write($"{ignored}/skipped.cs", "x");
        }

        var snapshot = new FileContentSnapshotter().Take(_root);

        snapshot.Should().NotBeNull();
        Write("a.cs", "changed");
        Write("src/b.cs", "changed");
        Write("obj/skipped.cs", "changed");
        Write(".git/skipped.cs", "changed");
        Write("node_modules/skipped.cs", "changed");
        var after = new FileContentSnapshotter().Take(_root);
        after!.ChangedSince(snapshot).Should().Equal("a.cs", "src/b.cs");
    }

    [Fact]
    public void ChangedSince_ListsOnlyContentChanges()
    {
        Write("same.cs", "same");
        Write("edited.cs", "before");
        Write("new-only.cs", "n");
        var before = new FileContentSnapshotter().Take(_root)!;

        Write("same.cs", "same");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "same.cs"), DateTime.UtcNow.AddHours(1));
        Write("edited.cs", "after");
        Write("added-later.cs", "x");
        File.Delete(Path.Combine(_root, "new-only.cs"));
        var after = new FileContentSnapshotter().Take(_root)!;

        after.ChangedSince(before).Should().Equal("edited.cs");
    }

    [Fact]
    public void ChangedSince_IsOrdinalSortedAndSlashSeparated()
    {
        // Names that differ by more than case, so the test also holds on a case-insensitive file system
        // (Windows, macOS); ordinal order still differs from a culture-aware one ("B.cs" before "a/z.cs").
        Write("c.cs", "1");
        Write("a/z.cs", "1");
        Write("B.cs", "1");
        var before = new FileContentSnapshotter().Take(_root)!;
        Write("c.cs", "2");
        Write("a/z.cs", "2");
        Write("B.cs", "2");

        new FileContentSnapshotter().Take(_root)!.ChangedSince(before).Should().Equal("B.cs", "a/z.cs", "c.cs");
    }

    [Fact]
    public void Take_ReturnsNull_OverFileLimit()
    {
        Write("1.txt", "1");
        Write("2.txt", "2");
        Write("3.txt", "3");

        new FileContentSnapshotter(maxFiles: 2, maxBytes: 1024).Take(_root).Should().BeNull();
        new FileContentSnapshotter(maxFiles: 3, maxBytes: 1024).Take(_root).Should().NotBeNull();
    }

    [Fact]
    public void Take_ReturnsNull_OverByteLimit()
    {
        Write("1.txt", "12345");
        Write("2.txt", "12345");

        new FileContentSnapshotter(maxFiles: 10, maxBytes: 9).Take(_root).Should().BeNull();
        new FileContentSnapshotter(maxFiles: 10, maxBytes: 10).Take(_root).Should().NotBeNull();
    }

    [Fact]
    public void Take_WithAFileFilter_HashesOnlyTheFilesItIncludes()
    {
        Write("a.cs", "a");
        Write("data/rows.csv", "1,2");
        var before = new FileContentSnapshotter().Take(_root, IsCSharp)!;

        Write("a.cs", "changed");
        Write("data/rows.csv", "3,4");

        new FileContentSnapshotter().Take(_root, IsCSharp)!.ChangedSince(before).Should().Equal("a.cs");
    }

    [Fact]
    public void Take_WithAFileFilter_CountsOnlyIncludedFilesAgainstTheLimits()
    {
        // Sample data the tool never rewrites must not make the tree too large to compare.
        Write("a.cs", "a");
        Write("data/rows.csv", new string('x', 100));

        new FileContentSnapshotter(maxFiles: 1, maxBytes: 50).Take(_root, IsCSharp).Should().NotBeNull();
        new FileContentSnapshotter(maxFiles: 1, maxBytes: 50).Take(_root).Should().BeNull();
    }

    [Fact]
    public void Take_InAWorkTree_HashesOnlyWhatGitDoesNotIgnore()
    {
        Write("a.cs", "a");
        Write("src/b.cs", "b");
        Write("tmp/ignored.cs", new string('x', 100));
        var git = new FakeGitWorkingTree(["a.cs", "src/b.cs", "gone.cs"]);
        var before = new FileContentSnapshotter(maxFiles: 10, maxBytes: 50, git).Take(_root);

        before.Should().NotBeNull("the ignored file counts toward no limit");
        Write("a.cs", "changed");
        Write("tmp/ignored.cs", "changed");
        new FileContentSnapshotter(maxFiles: 10, maxBytes: 50, git).Take(_root)!.ChangedSince(before)
            .Should().Equal("a.cs");
        git.Directories.Should().AllBe(_root);
    }

    [Fact]
    public void Take_InAWorkTree_StillSkipsBuildOutputAndHiddenDirectories()
    {
        Write("a.cs", "a");
        Write("obj/generated.cs", "g");
        Write(".github/x.cs", "x");
        var git = new FakeGitWorkingTree(["a.cs", "obj/generated.cs", ".github/x.cs"]);
        var before = new FileContentSnapshotter(maxFiles: 10, maxBytes: 1024, git).Take(_root)!;

        Write("a.cs", "changed");
        Write("obj/generated.cs", "changed");
        Write(".github/x.cs", "changed");

        new FileContentSnapshotter(maxFiles: 10, maxBytes: 1024, git).Take(_root)!.ChangedSince(before)
            .Should().Equal("a.cs");
    }

    [Fact]
    public void Take_WhenGitCannotAnswer_WalksTheTree()
    {
        Write("a.cs", "a");
        var git = new FakeGitWorkingTree(null);
        var before = new FileContentSnapshotter(maxFiles: 10, maxBytes: 1024, git).Take(_root)!;

        Write("a.cs", "changed");

        new FileContentSnapshotter(maxFiles: 10, maxBytes: 1024, git).Take(_root)!.ChangedSince(before)
            .Should().Equal("a.cs");
    }

    [Fact]
    public void Take_MissingRoot_ReturnsNull()
    {
        new FileContentSnapshotter().Take(Path.Combine(_root, "nope")).Should().BeNull();
    }

    [Fact]
    public void Take_Cancelled_Throws()
    {
        Write("1.txt", "1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new FileContentSnapshotter().Take(_root, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Take_OverByteLimit_DecidesFromSizesWithoutReadingTheFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Unreadable: were it opened, it would be skipped and the snapshot would succeed (unless running as
        // root); judging by the size alone gives up before any read.
        Write("big.bin", new string('x', 100));
        var path = Path.Combine(_root, "big.bin");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            new FileContentSnapshotter(maxFiles: 10, maxBytes: 50).Take(_root).Should().BeNull();
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Take_SkipsNamedPipes_AndReturnsPromptly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("a.cs", "a");
        var fifo = Path.Combine(_root, "pipe");
        using (var mkfifo = Process.Start("mkfifo", fifo))
        {
            await mkfifo.WaitForExitAsync();
            mkfifo.ExitCode.Should().Be(0);
        }

        var take = Task.Run(() => new FileContentSnapshotter().Take(_root));

        var finished = await Task.WhenAny(take, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(take, "a named pipe must not block the snapshot");
        var before = await take;
        before.Should().NotBeNull();
        Write("a.cs", "b");
        new FileContentSnapshotter().Take(_root)!.ChangedSince(before).Should().Equal("a.cs");
    }

    private sealed class FakeGitWorkingTree(IReadOnlyList<string>? files) : IGitWorkingTree
    {
        public List<string> Directories { get; } = [];

        public IReadOnlyList<string>? UnignoredFiles(string directory, CancellationToken cancellationToken = default)
        {
            Directories.Add(directory);
            return files;
        }

        public IReadOnlyList<string>? ChangedFiles(string directory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

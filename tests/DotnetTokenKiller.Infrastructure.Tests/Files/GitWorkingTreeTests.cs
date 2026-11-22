using System.Diagnostics;
using DotnetTokenKiller.Infrastructure.Files;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Files;

public sealed class GitWorkingTreeTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "dtk-git-" + Guid.NewGuid().ToString("N")));

    /// <summary>
    /// A deadline far past the production one: a CI runner busy building other tests' projects can take seconds to
    /// start each git command, and the answer must not be given up as it would be on a machine that slow in real use.
    /// </summary>
    private readonly GitWorkingTree _sut = new(GitWorkingTree.FindOnPath(), TimeSpan.FromMinutes(2));

    public GitWorkingTreeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // git writes its objects read-only, which Directory.Delete refuses on Windows.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, true);
    }

    [Fact]
    public void UnignoredFiles_OutsideAWorkTree_IsNull()
    {
        Write("a.cs", "a");

        _sut.UnignoredFiles(_root).Should().BeNull();
    }

    [Fact]
    public void UnignoredFiles_ListsTrackedAndUntrackedFiles_ButNotIgnoredOnes()
    {
        InitRepository();
        Write(".gitignore", "tmp/\n*.log\n");
        Write("src/a.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        Write("src/b.cs", "b");
        Write("tmp/c.cs", "c");
        Write("build.log", "x");

        _sut.UnignoredFiles(_root).Should().BeEquivalentTo(".gitignore", "src/a.cs", "src/b.cs");
    }

    [Fact]
    public void UnignoredFiles_OfASubdirectory_AreRelativeToIt()
    {
        InitRepository();
        Write("src/a.cs", "a");
        Write("other.cs", "o");

        _sut.UnignoredFiles(Path.Combine(_root, "src")).Should().Equal("a.cs");
    }

    [Fact]
    public void ChangedFiles_OutsideAWorkTree_IsNull()
    {
        _sut.ChangedFiles(_root).Should().BeNull();
    }

    [Fact]
    public void ChangedFiles_WithoutADefaultBranch_AreUnknownRatherThanTheUncommittedOnes()
    {
        // A remote added by hand (git remote add, then fetch) has no origin/HEAD. Without it, what a branch committed
        // cannot be told from what it inherited, and the uncommitted files alone would leave its commits out: a
        // warning in a file it committed would be counted among the unchanged ones and hidden.
        InitRepository();
        Write("base.cs", "b");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("switch", "-q", "-c", "feature");
        Write("committed.cs", "c");
        Git("add", ".");
        Git("commit", "-q", "-m", "work");
        Write("stray.txt", "s");

        _sut.ChangedFiles(_root).Should().BeNull();
    }

    [Fact]
    public void ChangedFiles_OnTheDefaultBranch_AreTheUncommittedOnes()
    {
        InitRepository();
        Write(".gitignore", "tmp/\n");
        Write("kept.cs", "k");
        Write("edited.cs", "e");
        Write("staged.cs", "s");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        PointOriginHeadAtHead();
        Write("edited.cs", "edited");
        Write("staged.cs", "staged");
        Git("add", "staged.cs");
        Write("new.cs", "n");
        Write("tmp/ignored.cs", "i");

        _sut.ChangedFiles(Path.Combine(_root))
            .Should().BeEquivalentTo(Absolute("edited.cs"), Absolute("staged.cs"), Absolute("new.cs"));
    }

    [Fact]
    public void ChangedFiles_WithAnOriginHeadNamingAMissingBranch_AreUnknown()
    {
        // origin/HEAD left behind by a default branch the remote renamed, then pruned.
        InitRepository();
        Write("a.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/gone");
        Write("a.cs", "changed");

        _sut.ChangedFiles(_root).Should().BeNull();
    }

    [Fact]
    public void ChangedFiles_OnABranch_IncludeWhatItCommittedSinceTheDefaultBranch()
    {
        InitRepository();
        Write("base.cs", "b");
        Write("touched.cs", "t");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        PointOriginHeadAtHead();
        Git("switch", "-q", "-c", "feature");
        Write("touched.cs", "touched");
        Write("added.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "work");
        Write("dirty.cs", "d");

        _sut.ChangedFiles(Path.Combine(_root))
            .Should().BeEquivalentTo(Absolute("touched.cs"), Absolute("added.cs"), Absolute("dirty.cs"));
    }

    [Fact]
    public void ChangedFiles_FromASubdirectory_CoverTheWholeWorkTree()
    {
        InitRepository();
        Write("src/a.cs", "a");
        Write("b.cs", "b");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        PointOriginHeadAtHead();
        Write("b.cs", "changed");

        _sut.ChangedFiles(Path.Combine(_root, "src")).Should().Equal(Absolute("b.cs"));
    }

    [Fact]
    public void ChangedFiles_BeforeTheFirstCommit_AreUnknown()
    {
        // Every file is new, which a caller that does without the answer treats alike: it lists every warning.
        InitRepository();
        Write("a.cs", "a");

        _sut.ChangedFiles(_root).Should().BeNull();
    }

    [Fact]
    public void ChangedFiles_ThroughASymbolicLink_AreSpelledAsTheCallerReachedThem()
    {
        // git prints its top directory resolved (a symbolic link on Unix; an 8.3 name, a junction or a subst drive on
        // Windows), but MSBuild names files under the directory it was started in: the paths must use that one.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        InitRepository();
        Write("src/a.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        PointOriginHeadAtHead();
        Write("src/a.cs", "changed");
        var link = _root + "-link";
        Directory.CreateSymbolicLink(link, _root);
        try
        {
            _sut.ChangedFiles(Path.Combine(link, "src"))
                .Should().Equal(Path.GetFullPath(Path.Combine(link, "src", "a.cs")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void ChangedFiles_WhenGitCannotReadHead_AreUnknownRatherThanEveryFile()
    {
        // A HEAD naming a missing object, with an origin/HEAD that resolves: git cannot compare the two.
        InitRepository();
        Write("a.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        PointOriginHeadAtHead();
        File.WriteAllText(Path.Combine(_root, ".git", "refs", "heads", "main"), new string('1', 40) + "\n");

        _sut.ChangedFiles(_root).Should().BeNull();
    }

    [Fact]
    public void ChangedFiles_Cancelled_Throws()
    {
        InitRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.ChangedFiles(_root, cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Answers_WithoutGit_AreUnknown()
    {
        var withoutGit = new GitWorkingTree(gitPath: null);
        InitRepository();

        withoutGit.ChangedFiles(_root).Should().BeNull();
        withoutGit.UnignoredFiles(_root).Should().BeNull();
    }

    /// <summary>Gives the repository the origin/HEAD a clone has, on the commit checked out.</summary>
    private void PointOriginHeadAtHead()
    {
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
    }

    private string Absolute(string relative) => Path.GetFullPath(Path.Combine(_root, relative));

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void InitRepository()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "dtk tests");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "commit.gpgsign", "false");
    }

    private void Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var error = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => error.AppendLine(e.Data);
        process.BeginErrorReadLine();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)} failed: {error}");
    }
}

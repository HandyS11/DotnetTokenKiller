using System.Diagnostics;
using DotnetTokenKiller.Infrastructure.Files;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Files;

public sealed class GitWorkingTreeTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "dtk-git-" + Guid.NewGuid().ToString("N")));

    private readonly GitWorkingTree _sut = new();

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
    public void ChangedFiles_WithoutADefaultBranch_AreTheUncommittedOnes()
    {
        InitRepository();
        Write(".gitignore", "tmp/\n");
        Write("kept.cs", "k");
        Write("edited.cs", "e");
        Write("staged.cs", "s");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        Write("edited.cs", "edited");
        Write("staged.cs", "staged");
        Git("add", "staged.cs");
        Write("new.cs", "n");
        Write("tmp/ignored.cs", "i");

        _sut.ChangedFiles(Path.Combine(_root))
            .Should().BeEquivalentTo(Absolute("edited.cs"), Absolute("staged.cs"), Absolute("new.cs"));
    }

    [Fact]
    public void ChangedFiles_OnABranch_IncludeWhatItCommittedSinceTheDefaultBranch()
    {
        InitRepository();
        Write("base.cs", "b");
        Write("touched.cs", "t");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
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
        Write("b.cs", "changed");

        _sut.ChangedFiles(Path.Combine(_root, "src")).Should().Equal(Absolute("b.cs"));
    }

    [Fact]
    public void ChangedFiles_BeforeTheFirstCommit_AreEveryUnignoredFile()
    {
        InitRepository();
        Write("a.cs", "a");

        _sut.ChangedFiles(_root).Should().Equal(Absolute("a.cs"));
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
    public void ChangedFiles_WhenGitCannotCompareWithHead_AreUnknownRatherThanEveryFile()
    {
        // Only a repository without a commit lists every tracked file as changed; a diff that fails for another
        // reason (here, a HEAD naming a missing object) says nothing.
        InitRepository();
        Write("a.cs", "a");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
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

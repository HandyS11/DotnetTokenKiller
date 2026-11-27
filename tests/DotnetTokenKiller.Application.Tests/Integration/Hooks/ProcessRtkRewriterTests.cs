using System.Diagnostics;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Application.Tests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class ProcessRtkRewriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dtk-rtk-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [UnixFact]
    public void Rewrite_RtkRewrites_ReturnsItsExitCodeAndOutput()
    {
        var rewriter = Rewriter("printf 'rtk %s\\n' \"$2\"; exit 3");

        rewriter.Rewrite("git status").Should().Be(new RtkRewrite(3, "rtk git status"));
    }

    [UnixFact]
    public void Rewrite_RtkHasNoRewrite_ReturnsExitOneAndNoOutput()
    {
        var rewriter = Rewriter("exit 1");

        rewriter.Rewrite("echo hi").Should().Be(new RtkRewrite(1, ""));
    }

    [UnixFact]
    public void Rewrite_PassesTheCommandAsOneArgumentVerbatim()
    {
        var rewriter = Rewriter("printf '%s\\n%s\\n' \"$#\" \"$2\"");
        const string command = "dotnet build \"My App.sln\"; git log --format=\"%h $x\" | grep 'é'";

        rewriter.Rewrite(command).Should().Be(new RtkRewrite(0, "2\n" + command));
    }

    [UnixFact]
    public void Rewrite_RemovesRtkRewriteHostFromTheEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("RTK_REWRITE_HOST");
        Environment.SetEnvironmentVariable("RTK_REWRITE_HOST", "1");
        try
        {
            var rewriter = Rewriter("printf '%s' \"${RTK_REWRITE_HOST-unset}\"");

            rewriter.Rewrite("git status").Should().Be(new RtkRewrite(0, "unset"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RTK_REWRITE_HOST", previous);
        }
    }

    [UnixFact]
    public void Rewrite_RtkOutlivesTheTimeout_ReturnsNullWithinTheTimeout()
    {
        var path = Script("sleep 10");
        var rewriter = new ProcessRtkRewriter(() => path, TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();
        var result = rewriter.Rewrite("git status");

        result.Should().BeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [UnixFact]
    public void Rewrite_NoRtkOnPath_ReturnsNullAndStartsNothing()
    {
        var rewriter = new ProcessRtkRewriter(() => null, ProcessRtkRewriter.DefaultTimeout);

        rewriter.Rewrite("git status").Should().BeNull();
    }

    [UnixFact]
    public void Rewrite_LocatesRtkOnce()
    {
        var path = Script("exit 1");
        var calls = 0;
        var rewriter = new ProcessRtkRewriter(() =>
        {
            calls++;
            return path;
        }, ProcessRtkRewriter.DefaultTimeout);

        rewriter.Rewrite("a");
        rewriter.Rewrite("b");

        calls.Should().Be(1);
    }

    [Fact]
    public void Rewrite_LocatorThrows_ReturnsNull()
    {
        var rewriter = new ProcessRtkRewriter(() => throw new InvalidOperationException("boom"),
            ProcessRtkRewriter.DefaultTimeout);

        rewriter.Rewrite("git status").Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\tools\rtk.cmd", false)]
    [InlineData(@"C:\tools\rtk.BAT", false)]
    [InlineData(@"C:\tools\rtk", false)]
    [InlineData(@"C:\tools\rtk.exe", true)]
    [InlineData(@"C:\tools\RTK.EXE", true)]
    [InlineData(@"C:\tools\rtk.com", true)]
    public void CanStartWithoutShell_OnWindows_AllowsOnlyExeAndCom(string path, bool expected)
    {
        ProcessRtkRewriter.CanStartWithoutShell(path, isWindows: true).Should().Be(expected);
    }

    [Theory]
    [InlineData("/usr/bin/rtk")]
    [InlineData("/opt/rtk.cmd")]
    public void CanStartWithoutShell_OffWindows_AllowsAnyPath(string path)
    {
        ProcessRtkRewriter.CanStartWithoutShell(path, isWindows: false).Should().BeTrue();
    }

    private ProcessRtkRewriter Rewriter(string body)
    {
        var path = Script(body);
        return new ProcessRtkRewriter(() => path, ProcessRtkRewriter.DefaultTimeout);
    }

    private string Script(string body)
    {
        var path = Path.Combine(_directory, $"rtk-{Guid.NewGuid():N}");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}

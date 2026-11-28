using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Application.Tests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

/// <summary>
/// The tests that occupy every thread-pool thread on purpose: they run alone, so no other test is slowed by it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class ThreadPoolStarvation
{
    /// <summary>The collection's name, as <c>[Collection]</c> and <c>[CollectionDefinition]</c> spell it.</summary>
    public const string Name = "ThreadPoolStarvation";
}

[Collection(ThreadPoolStarvation.Name)]
public sealed class ProcessRtkRewriterStarvationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dtk-rtk-pool-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// A test suite running thousands of tests in parallel, or a busy machine, can leave no thread-pool thread free
    /// for a while. rtk's answer must still be read: reading it must not wait for the pool, or the hook would fall back
    /// to no refusal, and the race it exists to prevent would come back silently.
    /// </summary>
    [UnixFact]
    public async Task Rewrite_WhileEveryThreadPoolThreadIsBusy_StillReturnsRtksAnswerAsync()
    {
        var path = Path.Combine(_directory, "rtk");
        await File.WriteAllTextAsync(path, "#!/bin/sh\nprintf 'rtk %s\\n' \"$2\"; exit 3\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var rewriter = new ProcessRtkRewriter(() => path, ProcessRtkRewriter.DefaultTimeout);
        ThreadPool.GetMinThreads(out var workers, out _);
        using var release = new ManualResetEventSlim();
        var blockers = Enumerable.Range(0, workers * 4)
            .Select(_ => Task.Factory.StartNew(release.Wait, CancellationToken.None, TaskCreationOptions.None,
                TaskScheduler.Default))
            .ToArray();
        try
        {
            rewriter.Rewrite("git status").Should().Be(new RtkRewrite(3, "rtk git status"));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(blockers);
        }
    }
}

using System.Globalization;
using System.Runtime.Versioning;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Times the real <c>dtk</c> binary end to end, out of process, in three scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <c>dtk pipe build</c> with a fixture on stdin routes through PipeFilterUseCase into the same
/// FilteredOutputPipeline as a wrapped command, exercising process start, JIT, the DI graph, the
/// config load, ANSI stripping, filtering, both token counts and the SQLite write. It reads stdin
/// to the end before any of that starts, so it measures dtk as a strictly serial cost.
/// </para>
/// <para>
/// <c>dtk dotnet build</c> is the path users run, and it is shaped differently: dtk launches
/// <c>dotnet</c>, streams its output into the tee log while it runs, and only then filters and
/// tracks. A <see cref="FakeDotnet"/> stands in for the SDK, once exiting at once (the worst case:
/// nothing for setup to overlap with) and once after sleeping <see cref="DelayedChildSleep"/> (the
/// best case: an idle CPU to overlap with). A real build competes for cores, so its cost lies
/// between the two. Each iteration times the fake child alone and then dtk wrapping it, and records
/// the difference: pairing adjacent runs cancels machine drift, which subtracting two separately
/// collected medians would not.
/// </para>
/// <para>
/// Hand-written rather than a BenchmarkDotNet job: BenchmarkDotNet would measure its own harness
/// wrapped around the spawn. Median and p95 of raw wall-clock is the honest figure for startup.
/// </para>
/// </remarks>
internal static class ColdStartCommand
{
    private const int WarmupRuns = 5;
    private const int MeasuredRuns = 50;
    private const string Fixture = "dotnet_build_errors.txt";

    /// <summary>
    /// The exit code dtk must reproduce for <see cref="Fixture"/>, a real captured failing build,
    /// in every scenario. Anything else means the child did not run the pipeline this harness
    /// intends to measure, so the sample is not trustworthy.
    /// </summary>
    private const int ExpectedExitCode = 1;

    /// <summary>
    /// How long the delayed fake child sleeps: longer than the tiktoken vocabulary load and the
    /// SQLite setup combined, so setup started in the background can overlap it completely.
    /// </summary>
    private static readonly TimeSpan DelayedChildSleep = TimeSpan.FromMilliseconds(1000);

    internal static async Task<int> RunAsync(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync(
                "cold-start needs a POSIX shell: its wrapped scenarios run dtk against a shell-script "
                + "stand-in for dotnet. Run it on Linux or macOS.").ConfigureAwait(false);
            return 1;
        }

        var binary = ResolveBinary(args);

        if (binary is null)
        {
            await Console.Error.WriteLineAsync(
                "Could not locate a dtk binary. Pass one explicitly:\n"
                + "  ... -- cold-start /path/to/dtk\n"
                + "or build one first:\n"
                + "  dotnet build src/DotnetTokenKiller.Cli -c Release").ConfigureAwait(false);

            // Failing loudly matters more here than anywhere else in this project: a harness that
            // silently measured nothing would report an impressively fast startup time.
            return 1;
        }

        var input = FixtureCorpus.Load(Fixture);
        var summaryLine = FilteredSummaryLine(input);
        using var state = HermeticState.Enter();

        await Console.Out.WriteLineAsync($"Cold start: {binary}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Input: {Fixture} ({input.Length} chars), exit code {ExpectedExitCode}")).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Runs per scenario: {WarmupRuns} warmup + {MeasuredRuns} measured").ConfigureAwait(false);

        await MeasurePipeAsync(binary, input, summaryLine).ConfigureAwait(false);

        await MeasureWrappedAsync(
            binary, state, input, summaryLine, TimeSpan.Zero,
            "dotnet build, instant child (worst case: nothing for setup to overlap with)").ConfigureAwait(false);

        await MeasureWrappedAsync(
            binary, state, input, summaryLine, DelayedChildSleep,
            string.Create(
                CultureInfo.InvariantCulture,
                $"dotnet build, child sleeping {DelayedChildSleep.TotalMilliseconds:0} ms (best case: an idle CPU to overlap with)"))
            .ConfigureAwait(false);

        return 0;
    }

    /// <summary>Scenario 1: <c>dtk pipe build</c> with the fixture on stdin, wall-clock per spawn.</summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="input">The fixture text.</param>
    /// <param name="summaryLine">The line every dtk run must print.</param>
    private static async Task MeasurePipeAsync(string binary, string input, string summaryLine)
    {
        await WriteHeadingAsync("pipe build, fixture on stdin (wall-clock)").ConfigureAwait(false);

        string[] arguments = ["pipe", "build", "--exit-code", ExpectedExitCode.ToString(CultureInfo.InvariantCulture)];
        var samples = new List<double>(MeasuredRuns);

        for (var i = 0; i < WarmupRuns + MeasuredRuns; i++)
        {
            var run = await TimedProcess.RunAsync(binary, arguments, standardInput: input).ConfigureAwait(false);
            EnsureDtkFiltered(run, binary, summaryLine);

            if (i >= WarmupRuns)
            {
                samples.Add(run.Milliseconds);
            }
        }

        await TimingReport.WriteAsync(samples).ConfigureAwait(false);
    }

    /// <summary>
    /// Scenarios 2 and 3: <c>dtk dotnet build</c> wrapping a <see cref="FakeDotnet"/>, sampled in
    /// pairs of child alone then dtk wrapping it.
    /// </summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="state">The hermetic state the fake child is written under.</param>
    /// <param name="input">The fixture text the fake child prints.</param>
    /// <param name="summaryLine">The line every dtk run must print.</param>
    /// <param name="childSleep">How long the fake child sleeps before printing.</param>
    /// <param name="heading">The section heading.</param>
    [UnsupportedOSPlatform("windows")]
    private static async Task MeasureWrappedAsync(
        string binary,
        HermeticState state,
        string input,
        string summaryLine,
        TimeSpan childSleep,
        string heading)
    {
        await WriteHeadingAsync(heading).ConfigureAwait(false);

        var fake = FakeDotnet.Create(
            Path.Combine(
                state.RootPath,
                string.Create(CultureInfo.InvariantCulture, $"fake-dotnet-{childSleep.TotalMilliseconds:0}ms")),
            input,
            ExpectedExitCode,
            childSleep);

        string[] arguments = ["dotnet", "build"];
        var overhead = new List<double>(MeasuredRuns);
        var wall = new List<double>(MeasuredRuns);

        for (var i = 0; i < WarmupRuns + MeasuredRuns; i++)
        {
            var child = await TimedProcess.RunAsync(fake.ScriptPath, []).ConfigureAwait(false);
            EnsureFakeChildRan(child, fake, input);

            var wrapped = await TimedProcess
                .RunAsync(binary, arguments, prependToPath: fake.DirectoryPath)
                .ConfigureAwait(false);
            EnsureDtkFiltered(wrapped, binary, summaryLine);

            if (i >= WarmupRuns)
            {
                overhead.Add(wrapped.Milliseconds - child.Milliseconds);
                wall.Add(wrapped.Milliseconds);
            }
        }

        await Console.Out.WriteLineAsync("dtk overhead (wrapped minus child alone, paired per iteration)")
            .ConfigureAwait(false);
        await TimingReport.WriteAsync(overhead).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("wrapped wall-clock").ConfigureAwait(false);
        await TimingReport.WriteAsync(wall).ConfigureAwait(false);
    }

    private static async Task WriteHeadingAsync(string heading)
    {
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync(heading).ConfigureAwait(false);
    }

    /// <summary>
    /// The first non-empty line the build filter produces for <paramref name="input"/>: its summary.
    /// It carries the fixture's own counts and elapsed time and no file paths, so it is the same
    /// whatever directory dtk runs in, which the rest of the filtered output is not.
    /// </summary>
    /// <param name="input">The raw fixture text.</param>
    private static string FilteredSummaryLine(string input) =>
        SavingsScenarios.FilterFor(FilterKeys.Build)
            .Apply(AnsiStrip.Strip(input), ExpectedExitCode)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .First(line => !string.IsNullOrWhiteSpace(line));

    /// <summary>Throws unless dtk exited as expected and printed the filtered fixture's summary.</summary>
    /// <param name="run">The timed dtk run.</param>
    /// <param name="binary">The dtk binary, for the message.</param>
    /// <param name="summaryLine">The line from <see cref="FilteredSummaryLine"/>.</param>
    /// <exception cref="InvalidOperationException">Either check failed.</exception>
    private static void EnsureDtkFiltered(TimedRun run, string binary, string summaryLine)
    {
        if (run.ExitCode != ExpectedExitCode)
        {
            // A binary that starts and exits with the wrong code (a crash, a bad argument, a
            // missing filter registration) would otherwise still produce a plausible-looking
            // timing sample. Fail loudly instead, with enough of the child's own output to
            // diagnose it.
            throw new InvalidOperationException(
                $"{binary} exited with code {run.ExitCode}, expected {ExpectedExitCode}. "
                + $"Output:\n{run.Diagnostic}");
        }

        if (!run.StdOut.Contains(summaryLine, StringComparison.Ordinal))
        {
            // The exit code alone cannot prove the pipeline ran over this fixture. On a wrapped
            // scenario whose PATH wiring broke, the real SDK runs `dotnet build` with no project,
            // which also exits 1, and dtk would then filter that output instead.
            throw new InvalidOperationException(
                $"{binary} exited with code {ExpectedExitCode} but did not print the build filter's "
                + $"summary line for {Fixture}:\n  {summaryLine}\nso it did not filter that fixture. "
                + "On a wrapped scenario, the real dotnet SDK most likely ran instead of the fake one. "
                + $"Output:\n{run.StdOut}");
        }
    }

    /// <summary>
    /// Throws unless the fake child, run alone, reproduced the fixture and its exit code. Timing dtk
    /// against a child that prints something else would measure the wrong pipeline.
    /// </summary>
    /// <param name="run">The timed fake-child run.</param>
    /// <param name="fake">The fake child, for the message.</param>
    /// <param name="input">The fixture text it must print verbatim.</param>
    /// <exception cref="InvalidOperationException">Either check failed.</exception>
    private static void EnsureFakeChildRan(TimedRun run, FakeDotnet fake, string input)
    {
        if (run.ExitCode == ExpectedExitCode && string.Equals(run.StdOut, input, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The fake dotnet at {fake.ScriptPath} did not reproduce {Fixture}: exit code {run.ExitCode} "
            + $"(expected {ExpectedExitCode}), {run.StdOut.Length} chars on stdout (expected {input.Length}). "
            + $"Stderr:\n{run.StdErr}");
    }

    /// <summary>
    /// Resolves the binary from an explicit argument, then the Release build output, then whatever
    /// <c>dtk</c> is installed on PATH.
    /// </summary>
    /// <param name="args">The command-line arguments, checked for an explicit binary path.</param>
    private static string? ResolveBinary(string[] args)
    {
        if (args is [_, var explicitPath, ..])
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var name = OperatingSystem.IsWindows() ? "dtk.exe" : "dtk";

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                continue;
            }

            var built = Path.Combine(
                dir.FullName, "src", "DotnetTokenKiller.Cli", "bin", "Release", "net10.0", name);

            return File.Exists(built) ? built : FromPath(name);
        }

        return FromPath(name);
    }

    private static string? FromPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator)
        .Where(dir => !string.IsNullOrWhiteSpace(dir))
        .Select(dir => Path.Combine(dir, name))
        .FirstOrDefault(File.Exists);
}

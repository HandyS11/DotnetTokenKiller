using System.Diagnostics;
using System.Globalization;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Support;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Times the real <c>dtk</c> binary end to end, out of process.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>dtk pipe build</c> with a fixture on stdin. That routes through PipeFilterUseCase into
/// the same FilteredOutputPipeline as a wrapped command, exercising process start, JIT, the DI
/// graph, the config load, ANSI stripping, filtering, both token counts and the SQLite write —
/// without running a dotnet build, which would swamp the measurement and cannot be relied on to
/// run on a developer machine at all.
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
    /// The exit code <c>dtk pipe build --exit-code 1</c> must reproduce for <see cref="Fixture"/>,
    /// a real captured failing build. Anything else means the child did not run the pipeline this
    /// harness intends to measure, so the sample is not trustworthy.
    /// </summary>
    private const int ExpectedExitCode = 1;

    internal static async Task<int> RunAsync(string[] args)
    {
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
        using var state = HermeticState.Enter();

        await Console.Out.WriteLineAsync($"Cold start: {binary}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Input: {Fixture} ({input.Length} chars), exit code 1").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Runs: {WarmupRuns} warmup + {MeasuredRuns} measured").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);

        for (var i = 0; i < WarmupRuns; i++)
        {
            await TimeOnceAsync(binary, input).ConfigureAwait(false);
        }

        var samples = new List<double>(MeasuredRuns);
        for (var i = 0; i < MeasuredRuns; i++)
        {
            samples.Add(await TimeOnceAsync(binary, input).ConfigureAwait(false));
        }

        samples.Sort();
        await ReportAsync("median", Percentile(samples, 0.50)).ConfigureAwait(false);
        await ReportAsync("p95", Percentile(samples, 0.95)).ConfigureAwait(false);
        await ReportAsync("min", samples[0]).ConfigureAwait(false);
        await ReportAsync("max", samples[^1]).ConfigureAwait(false);
        return 0;
    }

    private static Task ReportAsync(string label, double milliseconds) => Console.Out.WriteLineAsync(
        string.Create(CultureInfo.InvariantCulture, $"  {label,-8} {milliseconds,8:F1} ms"));

    /// <summary>Nearest-rank percentile over the sorted samples.</summary>
    /// <param name="sorted">The samples, already sorted ascending.</param>
    /// <param name="fraction">The percentile to compute, in the range [0, 1].</param>
    /// <exception cref="ArgumentException"><paramref name="sorted"/> is empty.</exception>
    private static double Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
        {
            throw new ArgumentException("Cannot compute a percentile of an empty sample set.", nameof(sorted));
        }

        var rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static async Task<double> TimeOnceAsync(string binary, string input)
    {
        var info = new ProcessStartInfo(binary)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("pipe");
        info.ArgumentList.Add("build");
        info.ArgumentList.Add("--exit-code");
        info.ArgumentList.Add("1");

        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {binary}.");

        await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
        process.StandardInput.Close();

        // Drain both pipes before waiting: a child that fills its stdout buffer blocks forever.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != ExpectedExitCode)
        {
            // A binary that starts and exits with the wrong code (a crash, a bad argument, a
            // missing filter registration) would otherwise still produce a plausible-looking
            // timing sample. Fail loudly instead, with enough of the child's own output to
            // diagnose it.
            var stderr = await stderrTask.ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var diagnostic = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            throw new InvalidOperationException(
                $"{binary} exited with code {process.ExitCode}, expected {ExpectedExitCode}. "
                + $"Output:\n{diagnostic}");
        }

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
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

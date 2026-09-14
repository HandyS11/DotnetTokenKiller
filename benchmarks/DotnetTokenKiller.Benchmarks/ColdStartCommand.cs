using System.Collections;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Times the real <c>dtk</c> binary end to end, out of process, in four scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <c>dtk pipe build</c> with a fixture on stdin routes through PipeFilterUseCase into the same
/// FilteredOutputPipeline as a wrapped command, exercising process start, JIT, the DI graph, the
/// config load, ANSI stripping, filtering, both token counts and the tracking journal write (one
/// file; the fold into SQLite happens when this harness reads the totals after the scenario, outside
/// the timed runs). It reads stdin to the end before any of that starts, so it measures dtk as a
/// strictly serial cost.
/// </para>
/// <para>
/// <c>dtk dotnet build</c> is the path users run, and it is shaped differently: dtk launches
/// <c>dotnet</c>, streams its output into the tee log while it runs, and only then filters and
/// tracks. A <see cref="FakeDotnet"/> stands in for the SDK, once exiting at once (the worst case:
/// nothing for setup to overlap with) and once after sleeping <see cref="DelayedChildSleep"/> (the
/// best case: an idle CPU to overlap with). A real build competes for cores, so for output this
/// fixture's size (2.6 KB) its cost lies between the two; dtk's per-line tee flush and token
/// counting grow with output size, so this bracket does not say anything about a much larger build
/// log. Each iteration times the fake child alone and dtk wrapping it, alternating which runs
/// first, and records the difference: pairing adjacent runs cancels machine drift, which
/// subtracting two separately collected medians would not.
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
    /// How long the delayed fake child sleeps: longer than the tiktoken vocabulary load and the
    /// tracker's setup combined, so setup started in the background can overlap it completely.
    /// </summary>
    private static readonly TimeSpan DelayedChildSleep = TimeSpan.FromMilliseconds(1000);

    /// <summary>What a scenario's child prints, the code it exits with, and the line dtk must then print.</summary>
    /// <param name="Text">The child's stdout.</param>
    /// <param name="ExitCode">The child's exit code, which dtk must reproduce.</param>
    /// <param name="SummaryLine">The build filter's first non-empty line for <paramref name="Text"/>.</param>
    private sealed record ChildOutput(string Text, int ExitCode, string SummaryLine)
    {
        /// <summary>
        /// Computes the summary line through the same filter dtk runs, so a sample counts only if dtk
        /// really filtered this text: the real SDK found on PATH by mistake also exits 1 for a missing
        /// project, and prints something else.
        /// </summary>
        /// <param name="text">The child's stdout.</param>
        /// <param name="exitCode">The child's exit code.</param>
        /// <exception cref="InvalidOperationException">The filter printed nothing for this text.</exception>
        internal static ChildOutput From(string text, int exitCode)
        {
            var summary = SavingsScenarios.FilterFor(FilterKeys.Build)
                .Apply(AnsiStrip.Strip(text), exitCode)
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? throw new InvalidOperationException(
                    $"The build filter printed nothing for a {text.Length}-char input with exit code {exitCode}, "
                    + "so no sample could be validated against it.");
            return new ChildOutput(text, exitCode, summary);
        }
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync(
                "cold-start needs a POSIX shell: its wrapped scenarios run dtk against a shell-script "
                + "stand-in for dotnet. Run it on Linux or macOS.").ConfigureAwait(false);
            return 1;
        }

        (string? Binary, string? StateDir) parsed;
        try
        {
            parsed = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "Usage: cold-start [/path/to/dtk] [--state-dir <directory>]").ConfigureAwait(false);
            return 1;
        }

        var binary = ResolveBinary(parsed.Binary);

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

        var fixture = ChildOutput.From(FixtureCorpus.Load(Fixture), exitCode: 1);
        var largeLog = ChildOutput.From(LogCorpusGenerator.Generate(FilterKeys.Build, CorpusTier.Large), exitCode: 0);
        using var state = HermeticState.Enter(parsed.StateDir);

        await Console.Out.WriteLineAsync($"Cold start: {binary}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Built: {File.GetLastWriteTime(binary):yyyy-MM-dd HH:mm:ss} (local)")).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Input: {Fixture} ({fixture.Text.Length} chars), exit code {fixture.ExitCode}")).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Large log: generated {FilterKeys.Build} tier {CorpusTier.Large} ({largeLog.Text.Length} chars), "
            + $"exit code {largeLog.ExitCode}")).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Runs per scenario: {WarmupRuns} warmup + {MeasuredRuns} measured").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Runtime environment: {DescribeRuntimeEnvironment()}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Runtime config: {DescribeRuntimeConfig(binary)}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"State: {state.RootPath} ({DescribeFileSystem(state.RootPath)})")
            .ConfigureAwait(false);

        await MeasurePipeAsync(binary, state, fixture).ConfigureAwait(false);

        await MeasureWrappedAsync(
            binary, state, fixture, TimeSpan.Zero,
            "dotnet build, instant child (worst case: nothing for setup to overlap with)").ConfigureAwait(false);

        await MeasureWrappedAsync(
            binary, state, fixture, DelayedChildSleep,
            string.Create(
                CultureInfo.InvariantCulture,
                $"dotnet build, child sleeping {DelayedChildSleep.TotalMilliseconds:0} ms (best case: an idle CPU to overlap with)"))
            .ConfigureAwait(false);

        await MeasureWrappedAsync(
            binary, state, largeLog, DelayedChildSleep,
            string.Create(
                CultureInfo.InvariantCulture,
                $"dotnet build, child sleeping {DelayedChildSleep.TotalMilliseconds:0} ms, "
                + $"{largeLog.Text.Length / 1024} KB generated log (counting and tee at size)"))
            .ConfigureAwait(false);

        return 0;
    }

    /// <summary>Scenario 1: <c>dtk pipe build</c> with the fixture on stdin, wall-clock per spawn.</summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="state">The hermetic state holding the tracking database this scenario's dtk
    /// runs must each record a row in.</param>
    /// <param name="fixture">The child output every dtk run must reproduce and filter.</param>
    private static async Task MeasurePipeAsync(string binary, HermeticState state, ChildOutput fixture)
    {
        await WriteHeadingAsync("pipe build, fixture on stdin (wall-clock)").ConfigureAwait(false);

        string[] arguments = ["pipe", "build", "--exit-code", fixture.ExitCode.ToString(CultureInfo.InvariantCulture)];
        var samples = new List<double>(MeasuredRuns);
        var before = await ReadTrackingCountsAsync(state.DbPath).ConfigureAwait(false);

        for (var i = 0; i < WarmupRuns + MeasuredRuns; i++)
        {
            var run = await TimedProcess.RunAsync(binary, arguments, standardInput: fixture.Text).ConfigureAwait(false);
            EnsureDtkFiltered(run, binary, fixture);

            if (i >= WarmupRuns)
            {
                samples.Add(run.Milliseconds);
            }
        }

        var after = await ReadTrackingCountsAsync(state.DbPath).ConfigureAwait(false);
        EnsureTrackingRecorded("pipe build", before, after);

        await TimingReport.WriteAsync(samples).ConfigureAwait(false);
    }

    /// <summary>
    /// Scenarios 2 and 3: <c>dtk dotnet build</c> wrapping a <see cref="FakeDotnet"/>, sampled in
    /// pairs of child alone and dtk wrapping it, in alternating order.
    /// </summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="state">The hermetic state the fake child is written under.</param>
    /// <param name="output">The child output the fake child prints and dtk must reproduce and filter.</param>
    /// <param name="childSleep">How long the fake child sleeps before printing.</param>
    /// <param name="heading">The section heading.</param>
    [UnsupportedOSPlatform("windows")]
    private static async Task MeasureWrappedAsync(
        string binary,
        HermeticState state,
        ChildOutput output,
        TimeSpan childSleep,
        string heading)
    {
        await WriteHeadingAsync(heading).ConfigureAwait(false);

        var fake = FakeDotnet.Create(
            Path.Combine(
                state.RootPath,
                string.Create(CultureInfo.InvariantCulture, $"fake-dotnet-{childSleep.TotalMilliseconds:0}ms")),
            output.Text,
            output.ExitCode,
            childSleep);

        string[] arguments = ["dotnet", "build"];
        var overhead = new List<double>(MeasuredRuns);
        var wall = new List<double>(MeasuredRuns);
        var before = await ReadTrackingCountsAsync(state.DbPath).ConfigureAwait(false);

        for (var i = 0; i < WarmupRuns + MeasuredRuns; i++)
        {
            // Alternate which process runs first. dtk always running straight after an idle child
            // inflated the sleeping-child scenario by 2-4 ms; alternating cancels that order effect
            // while keeping each pair adjacent.
            TimedRun child;
            TimedRun wrapped;
            if (i % 2 == 0)
            {
                child = await RunFakeChildAsync(fake, output).ConfigureAwait(false);
                wrapped = await RunWrappedDtkAsync(binary, arguments, fake, output).ConfigureAwait(false);
            }
            else
            {
                wrapped = await RunWrappedDtkAsync(binary, arguments, fake, output).ConfigureAwait(false);
                child = await RunFakeChildAsync(fake, output).ConfigureAwait(false);
            }

            if (i >= WarmupRuns)
            {
                overhead.Add(wrapped.Milliseconds - child.Milliseconds);
                wall.Add(wrapped.Milliseconds);
            }
        }

        var after = await ReadTrackingCountsAsync(state.DbPath).ConfigureAwait(false);
        EnsureTrackingRecorded(heading, before, after);

        await Console.Out.WriteLineAsync("dtk overhead (wrapped minus child alone, paired per iteration)")
            .ConfigureAwait(false);
        await TimingReport.WriteAsync(overhead).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("wrapped wall-clock").ConfigureAwait(false);
        await TimingReport.WriteAsync(wall).ConfigureAwait(false);
    }

    /// <summary>Runs the fake child alone and validates it.</summary>
    /// <param name="fake">The fake child.</param>
    /// <param name="output">The output it must print.</param>
    private static async Task<TimedRun> RunFakeChildAsync(FakeDotnet fake, ChildOutput output)
    {
        var child = await TimedProcess.RunAsync(fake.ScriptPath, []).ConfigureAwait(false);
        EnsureFakeChildRan(child, fake, output);
        return child;
    }

    /// <summary>Runs dtk wrapping the fake child and validates it.</summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="arguments">dtk's arguments.</param>
    /// <param name="fake">The fake child, whose directory is prepended to dtk's PATH.</param>
    /// <param name="output">The output dtk must reproduce and filter.</param>
    private static async Task<TimedRun> RunWrappedDtkAsync(
        string binary, string[] arguments, FakeDotnet fake, ChildOutput output)
    {
        var wrapped = await TimedProcess
            .RunAsync(binary, arguments, prependToPath: fake.DirectoryPath)
            .ConfigureAwait(false);
        EnsureDtkFiltered(wrapped, binary, output);
        return wrapped;
    }

    private static async Task WriteHeadingAsync(string heading)
    {
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync(heading).ConfigureAwait(false);
    }

    /// <summary>
    /// The <c>DOTNET_*</c> and <c>COMPlus_*</c> variables in this process's environment, which every
    /// spawned dtk inherits. Settings such as <c>DOTNET_TieredPGO</c> or <c>DOTNET_TieredCompilation</c>
    /// move these timings by tens of milliseconds, so a figure printed without them is not comparable.
    /// </summary>
    private static string DescribeRuntimeEnvironment()
    {
        var variables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(entry => (Name: (string)entry.Key, Value: entry.Value as string))
            .Where(entry => entry.Name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                            || entry.Name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.Name}={entry.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

        return variables.Count == 0 ? "none" : string.Join(' ', variables);
    }

    /// <summary>
    /// The <c>configProperties</c> of the measured binary's <c>runtimeconfig.json</c>, which carries
    /// project-level runtime settings such as <c>System.Runtime.TieredPGO</c>.
    /// </summary>
    /// <param name="binary">The dtk binary; its runtimeconfig sits beside it.</param>
    private static string DescribeRuntimeConfig(string binary)
    {
        var fullPath = Path.GetFullPath(binary);
        var path = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? ".",
            Path.GetFileNameWithoutExtension(fullPath) + ".runtimeconfig.json");

        if (!File.Exists(path))
        {
            // An installed tool's shim on PATH has no runtimeconfig beside it.
            return "no runtimeconfig.json beside the binary";
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var options)
            || !options.TryGetProperty("configProperties", out var properties))
        {
            return "no configProperties";
        }

        var pairs = properties.EnumerateObject()
            // GetRawText, not ToString: JsonElement.ToString prints a JSON false as "False".
            .Select(property => $"{property.Name}={property.Value.GetRawText()}")
            .Order(StringComparer.Ordinal)
            .ToList();

        return pairs.Count == 0 ? "no configProperties" : string.Join(' ', pairs);
    }

    /// <summary>Throws unless dtk exited as expected and printed the filtered output's summary.</summary>
    /// <param name="run">The timed dtk run.</param>
    /// <param name="binary">The dtk binary, for the message.</param>
    /// <param name="output">The child output dtk must have reproduced and filtered.</param>
    /// <exception cref="InvalidOperationException">Either check failed.</exception>
    private static void EnsureDtkFiltered(TimedRun run, string binary, ChildOutput output)
    {
        if (run.ExitCode != output.ExitCode)
        {
            // A binary that starts and exits with the wrong code (a crash, a bad argument, a
            // missing filter registration) would otherwise still produce a plausible-looking
            // timing sample. Fail loudly instead, with enough of the child's own output to
            // diagnose it.
            throw new InvalidOperationException(
                $"{binary} exited with code {run.ExitCode}, expected {output.ExitCode}. "
                + $"Output:\n{run.Diagnostic}");
        }

        if (!run.StdOut.Contains(output.SummaryLine, StringComparison.Ordinal))
        {
            // The exit code alone cannot prove the pipeline ran over this input. On a wrapped
            // scenario whose PATH wiring broke, the real SDK runs `dotnet build` with no project,
            // which also exits 1, and dtk would then filter that output instead.
            throw new InvalidOperationException(
                $"{binary} exited with code {output.ExitCode} but did not print the build filter's "
                + $"summary line for this {output.Text.Length}-char input:\n  {output.SummaryLine}\nso it "
                + "did not filter that input. On a wrapped scenario, the real dotnet SDK most likely ran "
                + "instead of the fake one; otherwise the dtk binary's build filter differs from this "
                + "source tree (a stale Release build, or an installed dtk found on PATH). "
                + $"Output:\n{run.StdOut}");
        }
    }

    /// <summary>
    /// Throws unless the fake child, run alone, reproduced its output and exit code. Timing dtk
    /// against a child that prints something else would measure the wrong pipeline.
    /// </summary>
    /// <param name="run">The timed fake-child run.</param>
    /// <param name="fake">The fake child, for the message.</param>
    /// <param name="output">The output it must print verbatim.</param>
    /// <exception cref="InvalidOperationException">Either check failed.</exception>
    private static void EnsureFakeChildRan(TimedRun run, FakeDotnet fake, ChildOutput output)
    {
        if (run.ExitCode == output.ExitCode && string.Equals(run.StdOut, output.Text, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The fake dotnet at {fake.ScriptPath} did not reproduce its expected {output.Text.Length}-char "
            + $"output: exit code {run.ExitCode} (expected {output.ExitCode}), {run.StdOut.Length} chars on "
            + $"stdout. Stderr:\n{run.StdErr}");
    }

    /// <summary>
    /// Reads the running totals a scenario's tracking rows must move, straight from the tracking
    /// database rather than trusting dtk's own exit code: <c>FilteredOutputPipeline</c> swallows
    /// every exception from tracking so a faulted tokenizer load or journal write never breaks a
    /// user's build, which also means dtk still exits as expected and prints its summary having
    /// silently skipped that work. The dtk runs leave their records in the journal beside the
    /// database; reading through the tracker folds them in first, so the totals include every run.
    /// Pooling is disabled so the connection this opens is fully released on dispose, before the
    /// next scenario's runs are timed.
    /// </summary>
    /// <param name="dbPath">The tracking database path, from <see cref="HermeticState.DbPath"/>.</param>
    private static async Task<(int Commands, long InputTokens)> ReadTrackingCountsAsync(string dbPath)
    {
        var tracker = new SqliteTracker($"Data Source={dbPath};Pooling=False");
        try
        {
            var summary = await tracker.GetSummaryAsync(days: 1, projectPath: null).ConfigureAwait(false);
            return (summary.TotalCommands, summary.TotalInputTokens);
        }
        finally
        {
            await tracker.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Throws unless a scenario's dtk runs each recorded a tracking row with a non-zero input
    /// token count. Without this, a silently faulted tracking step would still leave dtk exiting
    /// and printing normally (see <see cref="ReadTrackingCountsAsync"/>), so this harness would
    /// accept the resulting sample as a valid, faster-than-real timing.
    /// </summary>
    /// <param name="scenario">The scenario name, for the exception message.</param>
    /// <param name="before">Totals read before the scenario's runs.</param>
    /// <param name="after">Totals read after the scenario's runs.</param>
    /// <exception cref="InvalidOperationException">
    /// Fewer than <see cref="WarmupRuns"/> + <see cref="MeasuredRuns"/> rows were recorded, or their
    /// input-token total did not grow.
    /// </exception>
    private static void EnsureTrackingRecorded(
        string scenario, (int Commands, long InputTokens) before, (int Commands, long InputTokens) after)
    {
        const int expectedRuns = WarmupRuns + MeasuredRuns;
        var actualRuns = after.Commands - before.Commands;
        var tokenDelta = after.InputTokens - before.InputTokens;

        if (actualRuns != expectedRuns || tokenDelta <= 0)
        {
            throw new InvalidOperationException(
                $"{scenario}: dtk's tracking step did not record every run, so this scenario's timings "
                + $"skipped that work and are not trustworthy. Expected {expectedRuns} new tracking rows "
                + $"with a positive input-token delta; the tracking database shows {actualRuns} new rows "
                + $"and an input-token delta of {tokenDelta}.");
        }
    }

    /// <summary>
    /// Splits the verb's arguments: <c>--state-dir &lt;dir&gt;</c> and at most one binary path.
    /// </summary>
    /// <param name="args">The command line after the verb.</param>
    /// <exception cref="ArgumentException">An unknown option or a second positional argument.</exception>
    internal static (string? Binary, string? StateDir) ParseArguments(string[] args)
    {
        string? binary = null;
        string? stateDir = null;

        var i = 1;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--state-dir" when i + 1 < args.Length:
                    stateDir = args[i + 1];
                    i += 2;
                    break;
                case "--state-dir":
                    throw new ArgumentException("--state-dir needs a directory.", nameof(args));
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    throw new ArgumentException($"Unknown option '{option}'.", nameof(args));
                case var path when binary is null:
                    binary = path;
                    i++;
                    break;
                default:
                    throw new ArgumentException($"Unexpected argument '{args[i]}'.", nameof(args));
            }
        }

        return (binary, stateDir);
    }

    /// <summary>The filesystem type under <paramref name="path"/>, for the header.</summary>
    /// <param name="path">A directory that exists.</param>
    private static string DescribeFileSystem(string path)
    {
        try
        {
            return new DriveInfo(Path.GetFullPath(path)).DriveFormat;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return "unknown filesystem";
        }
    }

    /// <summary>
    /// Resolves the binary from an explicit argument, then the Release build output, then whatever
    /// <c>dtk</c> is installed on PATH.
    /// </summary>
    /// <param name="explicitPath">An explicit binary path, or null to search.</param>
    private static string? ResolveBinary(string? explicitPath)
    {
        if (explicitPath is not null)
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

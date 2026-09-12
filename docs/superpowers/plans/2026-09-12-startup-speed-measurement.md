# Startup Speed Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `cold-start` time the wrapped-command path (`dtk dotnet build`) as well as `dtk pipe
build`, so moving setup into the background while the child runs shows up in the numbers.

**Architecture:** Two new support types in the benchmarks runner: `TimedProcess`, which spawns and
times one process and returns everything it printed, and `FakeDotnet`, which generates a POSIX
shell script that stands in for the SDK on a child's `PATH`. `ColdStartCommand` keeps its `pipe
build` scenario and gains two wrapped scenarios, one with an instant child and one with a child that
sleeps 1000 ms. Both sample in pairs (child alone, then dtk wrapping it) and report the difference.
Every sample is validated by exit code **and** by the build filter's summary line in dtk's output.

**Tech Stack:** net10.0, C# 14, `System.Diagnostics.Process`, POSIX `sh`.

**Spec:** [docs/superpowers/specs/2026-09-12-startup-speed-measurement-design.md](../specs/2026-09-12-startup-speed-measurement-design.md)

## Global Constraints

- **Nothing under `src/` changes.** Every edit is in `benchmarks/DotnetTokenKiller.Benchmarks`,
  plus `CLAUDE.md` and the spec's status line.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all` (Roslynator, SonarAnalyzer,
  NetAnalyzers, VS Threading). Every analyzer warning is a build error. Resolve them; do not
  suppress. The benchmarks project already has `GenerateDocumentationFile=false`.
- **Code style:** file-scoped namespaces; `var` preferred; private fields `_camelCase`; async
  methods suffixed `Async`; `ConfigureAwait(false)` on every await, as the existing harness does;
  one type per file.
- **Formatting:** LF, no trailing whitespace, no BOM, 4-space indent. Run
  `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before every commit.
- **Use `dtk`, not raw `dotnet`,** for build/test/format. `dotnet run` is not rewritten and is
  used as-is.
- **Sampling:** 5 warmup + 50 measured iterations per scenario, as `cold-start` already does.
- **Delayed child sleep:** exactly 1000 ms.
- **Fixture and exit code:** `dotnet_build_errors.txt`, exit code `1`, unchanged.
- **Windows:** `cold-start` exits non-zero before measuring anything. The Benchmarks workflow runs
  on `ubuntu-latest` and needs no change.
- **Failing loudly:** a harness failure throws `InvalidOperationException` with the child's own
  output, as the existing harness does. Never drop a bad sample silently.
- **CLI integration tests do not run on a developer machine** (project history). Verify with the
  Application test project only; nothing under `src/` changes anyway.

## File Structure

| File | Responsibility |
|---|---|
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedRun.cs` (new) | Result of one timed spawn: milliseconds, exit code, stdout, stderr |
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedProcess.cs` (new) | Spawn, feed stdin, drain both pipes, time it; optional `PATH` prefix; strips `NO_COLOR` |
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/FakeDotnet.cs` (new) | Generate the stand-in `dotnet` script and its output file |
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs` (modify) | Expose the temp root so the fake can live inside it |
| `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (modify) | Orchestrate the three scenarios, validate every sample, report |
| `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs` (modify) | Usage text for `cold-start` |
| `CLAUDE.md` (modify) | Describe the scenarios and record the measured figures |

---

### Task 1: Validate cold-start samples by output, through a shared `TimedProcess`

Today a `pipe build` sample counts if dtk exits `1`. That can't tell "filtered the fixture" apart
from "exited 1 for some other reason". This task moves the spawn into a reusable `TimedProcess` and
makes each sample also prove that dtk printed the build filter's summary line for the fixture.

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedRun.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedProcess.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (whole file below)

**Interfaces:**
- Consumes: `FixtureCorpus.Load(string)`, `SavingsScenarios.FilterFor(string)` (pinned to `/repo`),
  `FilterKeys.Build`, `AnsiStrip.Strip(string)`, `IOutputFilter.Apply(string, int)`,
  `HermeticState.Enter()`, `TimingReport.WriteAsync(List<double>)`.
- Produces:
  - `internal sealed record TimedRun(double Milliseconds, int ExitCode, string StdOut, string StdErr)`
    with `internal string Diagnostic` (stderr if non-blank, else stdout).
  - `internal static Task<TimedRun> TimedProcess.RunAsync(string fileName, IReadOnlyList<string> arguments, string? standardInput = null, string? prependToPath = null)`.
  - In `ColdStartCommand`: `private static string FilteredSummaryLine(string input)` and
    `private static void EnsureDtkFiltered(TimedRun run, string binary, string summaryLine)`.
    Task 2 reuses both.

- [ ] **Step 1: Create `TimedRun`**

`benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedRun.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>One finished spawn: how long it took, how it exited and everything it printed.</summary>
/// <param name="Milliseconds">Wall-clock from start to exit, with both pipes drained.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StdOut">Everything the process wrote to stdout.</param>
/// <param name="StdErr">Everything the process wrote to stderr.</param>
internal sealed record TimedRun(double Milliseconds, int ExitCode, string StdOut, string StdErr)
{
    /// <summary>Stderr if the process wrote any, otherwise stdout: whichever explains a failure.</summary>
    internal string Diagnostic => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr;
}
```

- [ ] **Step 2: Create `TimedProcess`**

`benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedProcess.cs`:

```csharp
using System.Diagnostics;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>Spawns one process and times it end to end, for the out-of-process harnesses.</summary>
internal static class TimedProcess
{
    /// <summary>Runs a process to exit and returns its timing, exit code and output.</summary>
    /// <param name="fileName">The executable to spawn.</param>
    /// <param name="arguments">Its arguments, passed verbatim.</param>
    /// <param name="standardInput">Text written to its stdin, or <see langword="null"/> for none.
    /// Stdin is closed either way, so a child that reads it sees EOF instead of waiting forever.</param>
    /// <param name="prependToPath">
    /// A directory to put first on this child's <c>PATH</c> only, or <see langword="null"/>. Set on
    /// the child's start info, never on this process: the harness runs under <c>dotnet run</c> and
    /// must keep resolving the real SDK.
    /// </param>
    internal static async Task<TimedRun> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        string? prependToPath = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // dtk rewrites its ✓/✗/⚠ glyphs when NO_COLOR is set, so a developer's shell setting would
        // otherwise change the very output the harness validates samples against.
        info.Environment.Remove("NO_COLOR");

        if (prependToPath is not null)
        {
            var path = info.Environment.TryGetValue("PATH", out var existing) ? existing : null;
            info.Environment["PATH"] = prependToPath + Path.PathSeparator + path;
        }

        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        // Start draining before writing stdin: a child that fills its stdout pipe while we are still
        // writing its stdin would otherwise block both sides forever.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return new TimedRun(
            elapsed,
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }
}
```

- [ ] **Step 3: Rewrite `ColdStartCommand` to use it and validate output**

Replace the whole of `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` with:

```csharp
using System.Globalization;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

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
        var summaryLine = FilteredSummaryLine(input);
        using var state = HermeticState.Enter();

        await Console.Out.WriteLineAsync($"Cold start: {binary}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Input: {Fixture} ({input.Length} chars), exit code {ExpectedExitCode}")).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Runs: {WarmupRuns} warmup + {MeasuredRuns} measured").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);

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
        return 0;
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
```

- [ ] **Step 4: Build the runner and a fresh dtk**

Run:
```bash
dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release
dtk dotnet build src/DotnetTokenKiller.Cli -c Release
```
Expected: both succeed with 0 warnings. If an analyzer fires, fix the code; do not suppress.

- [ ] **Step 5: Run `cold-start` and confirm it still reports**

Run: `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start`
Expected: exit 0 after about 20 s, printing `Cold start: …/src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk`,
then `median`, `p95`, `min` and `max` lines. On the reference machine the median is ~290 ms.

- [ ] **Step 6: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedRun.cs \
        benchmarks/DotnetTokenKiller.Benchmarks/Support/TimedProcess.cs \
        benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs
git commit -m "fix: make cold-start prove dtk filtered the fixture, not just exited 1"
```

The commit message body explains that the exit code alone cannot tell the pipeline ran, and ends
with the `Co-Authored-By` trailer.

- [ ] **Step 7: Prove the output check fails loudly, then restore**

Temporarily break the expected line. In `ColdStartCommand.FilteredSummaryLine`, change
`.First(line => !string.IsNullOrWhiteSpace(line));` to
`.First(line => !string.IsNullOrWhiteSpace(line)) + "X";`, then run:

```bash
dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start; echo "exit $?"
```
Expected: an unhandled `System.InvalidOperationException` whose message contains
`did not print the build filter's summary line`, printed after the first spawn, and a non-zero
`exit`. Then restore the file and confirm the tree is clean:

```bash
git checkout -- benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs
git status --short
```
Expected: no output from `git status --short`.

---

### Task 2: Wrapped scenarios with a fake `dotnet`, paired sampling and the recorded figures

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Support/FakeDotnet.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs` (add `RootPath`)
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (whole file below)
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs:12-13` (usage text)
- Modify: `CLAUDE.md:37,81-82,87-88` (scenario description and figures)
- Modify: `docs/superpowers/specs/2026-09-12-startup-speed-measurement-design.md:1` (status)

**Interfaces:**
- Consumes (from Task 1): `TimedProcess.RunAsync(...)`, `TimedRun`, and in `ColdStartCommand`
  `FilteredSummaryLine` and `EnsureDtkFiltered`.
- Produces:
  - `internal sealed class FakeDotnet` with `internal string DirectoryPath`,
    `internal string ScriptPath`, and
    `[UnsupportedOSPlatform("windows")] internal static FakeDotnet Create(string directoryPath, string output, int exitCode, TimeSpan delay)`.
  - `HermeticState.RootPath` (`internal string`).

- [ ] **Step 1: Expose the hermetic root**

In `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs`, add this property directly
above `internal string ConfigPath { get; }`:

```csharp
    /// <summary>The temporary directory everything else lives under, deleted on dispose.</summary>
    internal string RootPath => _root.FullName;

```

- [ ] **Step 2: Create `FakeDotnet`**

`benchmarks/DotnetTokenKiller.Benchmarks/Support/FakeDotnet.cs`:

```csharp
using System.Globalization;
using System.Runtime.Versioning;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// A stand-in <c>dotnet</c> for the wrapped cold-start scenarios: a POSIX shell script that
/// optionally sleeps, prints captured output and exits with a fixed code, ignoring its arguments.
/// </summary>
/// <remarks>
/// dtk launches the bare name <c>dotnet</c> through <c>PATH</c>, so putting
/// <see cref="DirectoryPath"/> first on a child's <c>PATH</c> makes dtk run this script instead of
/// the SDK. A real build cannot be the child: build-spawning runs are unreliable on a developer
/// machine, and a build's own variance would swamp a sub-second overhead. The script itself costs
/// a shell start and a <c>cat</c>, one to two milliseconds.
/// </remarks>
internal sealed class FakeDotnet
{
    private FakeDotnet(string directoryPath)
    {
        DirectoryPath = directoryPath;
        ScriptPath = Path.Combine(directoryPath, "dotnet");
    }

    /// <summary>The directory holding the script, to put first on a child's <c>PATH</c>.</summary>
    internal string DirectoryPath { get; }

    /// <summary>The script itself, spawnable directly to time the child alone.</summary>
    internal string ScriptPath { get; }

    /// <summary>Writes the script and the output it prints into <paramref name="directoryPath"/>.</summary>
    /// <param name="directoryPath">A directory to create and hold both files; must not contain a
    /// single quote, which the script uses to quote the output path.</param>
    /// <param name="output">The text the script prints to stdout.</param>
    /// <param name="exitCode">The code the script exits with.</param>
    /// <param name="delay">How long the script sleeps before printing; <see cref="TimeSpan.Zero"/>
    /// for none.</param>
    [UnsupportedOSPlatform("windows")]
    internal static FakeDotnet Create(string directoryPath, string output, int exitCode, TimeSpan delay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(output);

        Directory.CreateDirectory(directoryPath);
        var fake = new FakeDotnet(directoryPath);

        var outputPath = Path.Combine(directoryPath, "output.txt");
        File.WriteAllText(outputPath, output);

        var sleep = delay > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"sleep {delay.TotalSeconds:0.###}\n")
            : string.Empty;
        var script = string.Create(
            CultureInfo.InvariantCulture,
            $"#!/bin/sh\n{sleep}cat '{outputPath}'\nexit {exitCode}\n");

        File.WriteAllText(fake.ScriptPath, script);
        File.SetUnixFileMode(
            fake.ScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return fake;
    }
}
```

- [ ] **Step 3: Rewrite `ColdStartCommand` with the three scenarios**

Replace the whole of `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` with:

```csharp
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
```

- [ ] **Step 4: Update the usage text**

In `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`, replace:

```
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
              Times the published dtk binary end to end, out of process.
```

with:

```
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
              Times the built dtk binary end to end, out of process: piped, and wrapping a fake dotnet.
```

- [ ] **Step 5: Build**

Run: `dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release`
Expected: success, 0 warnings. If CA1416 fires on the `MeasureWrappedAsync` calls, the
`OperatingSystem.IsWindows()` early return must stay in `RunAsync`, the same method as those
calls. Fix the structure, don't suppress.

- [ ] **Step 6: Run `cold-start` and check the three sections**

Run: `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start | tee /tmp/cold-start.txt`
Expected: exit 0 after about 3 minutes, with three headed sections:
1. `pipe build, fixture on stdin (wall-clock)`, median ~290 ms on the reference machine.
2. `dotnet build, instant child (…)`, with two blocks: `dtk overhead` (median in the same
   few-hundred-millisecond range as section 1) and `wrapped wall-clock`.
3. `dotnet build, child sleeping 1000 ms (…)`, with `wrapped wall-clock` median above 1000 ms
   by no more than section 2's overhead median.

If any figure is outside those bounds, stop and investigate before continuing.

- [ ] **Step 7: Record the figures in CLAUDE.md**

In `CLAUDE.md`, replace:

```
# Measure the end-to-end cold-start cost of the built binary
```

with:

```
# Measure the end-to-end cold-start cost of the built binary (piped, and wrapping a fake dotnet; ~3 min, Linux/macOS)
```

Replace:

```
- `cold-start` times the built `dtk` binary end to end (`dtk pipe build` with a fixture on stdin),
  55 spawns, median and p95.
```

with the text below. Substitute the four medians from `/tmp/cold-start.txt`, rounded to 0.1 ms:
`P` = section 1 median, `I` = section 2 overhead median, `D` = section 3 overhead median,
`W` = section 3 wrapped wall-clock median.

```
- `cold-start` times the built `dtk` binary end to end in three scenarios, 55 spawns each: `dtk pipe
  build` with a fixture on stdin, and `dtk dotnet build` wrapping a generated shell-script `dotnet`
  on the child's `PATH` that either exits at once or sleeps 1000 ms first. The wrapped scenarios run
  the fake child alone and then dtk around it on every iteration, and report the paired difference
  as dtk's overhead. The instant child is the worst case (nothing for background setup to overlap
  with); the sleeping child is the best case (an idle CPU). A real build lies between them. Every
  sample must print the build filter's summary line, because the real SDK found on `PATH` by mistake
  also exits 1. Needs a POSIX shell. Measured 2026-09-12: pipe P ms; wrapped overhead I ms (instant
  child) and D ms (1000 ms child, wall-clock W ms).
```

Then replace `a 287.9 ms cold-start median on the same machine.` with
`a 287.9 ms pipe cold-start median on the same machine.`

- [ ] **Step 8: Mark the spec implemented**

In `docs/superpowers/specs/2026-09-12-startup-speed-measurement-design.md`, replace line 1
`**Status:** Designed` with:

```
**Status:** Implemented — see [the plan](../plans/2026-09-12-startup-speed-measurement.md)
```

- [ ] **Step 9: Run the savings gate**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all tests pass. Nothing it covers changed; this confirms the corpus reference still
resolves.

- [ ] **Step 10: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/DotnetTokenKiller.Benchmarks/Support/FakeDotnet.cs \
        benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs \
        benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs \
        benchmarks/DotnetTokenKiller.Benchmarks/Program.cs \
        CLAUDE.md \
        docs/superpowers/specs/2026-09-12-startup-speed-measurement-design.md
git commit -m "feat: time the wrapped-command path in cold-start"
```

The commit message body explains the two bounds, the paired sampling and the recorded figures, and
ends with the `Co-Authored-By` trailer.

- [ ] **Step 11: Prove both wrapped checks fail loudly, then restore**

**(a) A broken fake child.** In `FakeDotnet.Create`, change `cat '{outputPath}'` to
`cat '{outputPath}.missing'`, then run:

```bash
dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start; echo "exit $?"
```
Expected: section 1 completes (~20 s), then an unhandled `InvalidOperationException` containing
`did not reproduce dotnet_build_errors.txt`, and a non-zero `exit`. Restore:
`git checkout -- benchmarks/DotnetTokenKiller.Benchmarks/Support/FakeDotnet.cs`.

**(b) The real SDK found instead of the fake.** In `MeasureWrappedAsync`, change
`prependToPath: fake.DirectoryPath` to `prependToPath: null`. Run from an empty directory, so the
real `dotnet build` finds no project and exits 1 (MSB1003), exactly the failure the check exists for:

```bash
dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release
empty=$(mktemp -d)
(cd "$empty" && dotnet run -c Release --project /home/cloudcli/projects/DotnetTokenKiller/benchmarks/DotnetTokenKiller.Benchmarks -- cold-start; echo "exit $?")
```
Expected: section 1 completes, then an unhandled `InvalidOperationException` containing
`the real dotnet SDK most likely ran instead of the fake one`, and a non-zero `exit`. Restore and
confirm the tree is clean:

```bash
git checkout -- benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs
dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release
git status --short
```
Expected: no output from `git status --short`.

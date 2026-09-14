# Serial Costs and Windows AOT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the `INSERT` fsync and the raw-output token count off dtk's serial path, make the
harness measure both, and ship a Native AOT `dtk.exe` to Windows x64 as the packaged shim of a
framework-dependent tool package.

**Architecture:** Three independent parts, each its own pull request. (A) `cold-start` gains
`--state-dir` and a 1 MB delayed-child scenario. (B) `SqliteTracker` writes each run to a
`PendingRecordJournal` (one JSON file per run) and folds the journal into SQLite on read, under a
lock with exact-once claim directories; a `ChunkedTokenCounter` counts stdout in 64 K-char chunks on
the thread pool while the child runs, cutting only where the tiktoken pre-tokenizer provably cannot
span. (C) The win-x64 package is packed framework-dependent with `PackAsToolShimRuntimeIdentifiers`,
its generated shim replaced by the AOT `dtk.exe`, which links `SQLitePCLRaw.bundle_winsqlite3` so it
needs no native file beside it.

**Tech Stack:** net10.0, C# 14, Microsoft.Data.Sqlite.Core 10.0.12 + SQLitePCLRaw bundles,
Microsoft.ML.Tokenizers 2.0.0, System.Text.Json source generation, xunit, FluentAssertions,
NSubstitute, GitHub Actions (`windows-latest`), POSIX `sh` under Git Bash.

**Spec:** [docs/superpowers/specs/2026-09-13-serial-costs-and-windows-aot-design.md](../specs/2026-09-13-serial-costs-and-windows-aot-design.md)

## Global Constraints

- **Token counts stay exact.** `SavingsBaselineTests` must pass and
  `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` must not change.
  Never run `update-baseline` in this plan.
- **Tracking failures stay non-fatal:** no exception from the journal, a fold, the counter or the
  tokenizer may change dtk's output or exit code. Every new failure path must land in the existing
  catch in `FilteredOutputPipeline.TrackIfEnabledAsync` or in `PassthroughRunUseCase.TrackAsync`.
- **A tracked run opens no SQLite connection** once part B lands (the `:memory:` data source that
  tests use is the one exception).
- **Still exactly two trim or AOT suppressions** in the repository (`SpectreCommandApp.Create` and
  `TypeRegistrar.Register`). `IsAotCompatible` is on for the three libraries: no reflection-based
  JSON, no `Type`-based DI additions.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all` (Roslynator, SonarAnalyzer,
  NetAnalyzers, VS Threading). Fix every warning; never suppress one with a pragma or
  `.editorconfig` change. Known traps: S107 (more than 7 parameters; records' primary constructors
  are exempt, classes' are not), VSTHRD003 (awaiting a `Task` read from a field, property or
  parameter; await the result of a method call instead), CA2016 (forward a `CancellationToken`),
  CA2007 (`ConfigureAwait(false)` on every await in `src/` and `benchmarks/`; `await using` needs
  the `#pragma warning disable CA2007` pair the tracker already uses).
- **Code style:** file-scoped namespaces; `var`; private fields `_camelCase`; async methods end in
  `Async`; XML doc comments on public members in `src/` (`GenerateDocumentationFile` is on); one
  type per file in `src/`.
- **Formatting:** LF, no trailing whitespace, no BOM, 4-space indent for `.cs`, 2-space for XML,
  YAML and JSON. Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before every commit.
- **Use `dtk dotnet …`** for build, test and format. `dotnet run`, `dotnet publish`, `dotnet pack`
  and `dotnet tool` are not rewritten and are used as-is.
- **The Bash hook rewrites `dtk dotnet build|test|restore|clean|format|list package` anywhere in a
  command,** including quoted strings and heredocs. Write every commit message to a file with the
  Write tool and commit with `git commit -F <file>`. Never put a commit message inline.
- **The Bash hook rewrites `grep` to `rtk grep`, which summarizes and truncates matches.** Use
  `rtk proxy grep …` or `sed -n` when you need the lines.
- **Commit messages end with** a blank line and
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- **CLI integration tests cannot pass on this machine.** Build the whole solution (so their
  `StubTracker` classes compile) but run tests per project:
  `tests/DotnetTokenKiller.Domain.Tests`, `tests/DotnetTokenKiller.Application.Tests`,
  `tests/DotnetTokenKiller.Infrastructure.Tests`. CI gates the rest.
- **`cold-start` does not rebuild the CLI** and takes about 4 minutes with the fourth scenario.
  Always build first and set the Bash timeout to `600000`. Measure against an installed linux-x64
  AOT package (`sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed
  artifacts/aot/pack-linux-x64.log` then `sh eng/aot/test-package.sh linux-x64 0.0.0-local
  artifacts/aot/feed artifacts/aot/pack-linux-x64.log artifacts/aot/tools/linux-x64`, needs
  Docker), or, when Docker is unavailable, against
  `dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot/publish` and
  say so in the numbers.
- **Constants from the spec:** fold threshold 64 files; minimum chunk 64 K chars; lock wait 2 s.
- **Part C cannot be run on this machine.** Windows steps are verified by CI; `sh -n` and
  `shellcheck` (when installed) check the scripts, `actionlint` (when installed) the workflows.

---

## File Structure

### Part A: harness

| File | Responsibility |
|---|---|
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs` (modify) | `Enter(string? root)`: hermetic state under a chosen directory |
| `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (modify) | `--state-dir`, filesystem in the header, `ChildOutput`, fourth scenario |
| `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs` (modify) | Usage line for `--state-dir` |
| `CLAUDE.md` (modify) | Commands and Benchmarks sections |

### Part B: tracking hot path

| File | Responsibility |
|---|---|
| `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecord.cs` (new) | Flat JSON shape of one run, versioned; to and from `CommandRecord` |
| `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecordJsonContext.cs` (new) | Source-generated serializer context |
| `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecordJournal.cs` (new) | Write one file per run; count; fold under a lock with claim directories |
| `src/DotnetTokenKiller.Infrastructure/Tracking/FoldOutcome.cs` (new) | What a fold did |
| `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (modify) | Journal on write; fold on read; `folds` table; threshold fold in `WarmUpAsync` |
| `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/PendingRecordJournalTests.cs` (new) | Journal behaviour |
| `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerJournalTests.cs` (new) | Tracker on a file data source |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs` (modify) | Positive control is `gain` |
| `src/DotnetTokenKiller.Domain/Text/AnsiStrip.cs` (modify) | `EndsInsideEscapeSequence` |
| `src/DotnetTokenKiller.Application/Helpers/ChunkedTokenCounter.cs` (new) | Safe cuts; chunk tasks; `Finish`/`TotalAsync` |
| `src/DotnetTokenKiller.Application/Helpers/CountingTextWriter.cs` (new) | Forwards lines to the tee and the counter |
| `src/DotnetTokenKiller.Application/UseCases/FilteredOutputRequest.cs` (modify) | `InputTokenCounter` |
| `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs` (modify) | Use the counter's total when present |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` (modify) | Counting stdout sink; `Finish(stderr)` |
| `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs` (modify) | Block reads feeding the counter |
| `tests/DotnetTokenKiller.Domain.Tests/Text/AnsiStripTests.cs` (modify) | Escape-sequence tail cases |
| `tests/DotnetTokenKiller.Application.Tests/Helpers/ChunkedTokenCounterTests.cs` (new) | The exactness proof and unit cases |
| `tests/DotnetTokenKiller.Application.Tests/Helpers/CountingTextWriterTests.cs` (new) | Forwarding and feeding |
| `tests/DotnetTokenKiller.Application.Tests/UseCases/{FilteredRunUseCase,PipeFilterUseCase,FilteredOutputPipeline}Tests.cs` (modify) | Wiring |
| `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/articles/getting-started.md`, `CLAUDE.md` (modify) | Old-glibc wording; numbers |

### Part C: Windows x64

| File | Responsibility |
|---|---|
| `Directory.Packages.props` (modify) | `Microsoft.Data.Sqlite.Core`, `SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.bundle_winsqlite3` |
| `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj` (modify) | `Microsoft.Data.Sqlite.Core` |
| `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (modify) | Bundle choice; `win-x64` RID; `UseNativeBinaryAsPackagedShim`; no `.pdb`/`.xml` in RID packs |
| `tests/DotnetTokenKiller.Infrastructure.Tests/…csproj`, `benchmarks/DotnetTokenKiller.Benchmarks/…csproj` (modify) | `SQLitePCLRaw.bundle_e_sqlite3` |
| `eng/aot/pack-windows.sh` (new) | Build, AOT-publish, pack with the shim, verify the shim byte for byte |
| `eng/aot/test-windows.sh` (new) | Install from the feed, assert the shim, tracking smoke, dll path, uninstall and reinstall, timings |
| `.github/workflows/aot-package.yml` (modify) | Windows branches in `pack` and `test` |
| `.github/workflows/ci.yml`, `.github/workflows/publish.yml` (modify) | `win-x64` matrix row |
| `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/articles/getting-started.md`, `CLAUDE.md` (modify) | Platform table and Native AOT section |

---

# Part A: harness

### Task 1: `--state-dir` and the filesystem in the header

**Files:**
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs:60-123` (`RunAsync`, `ResolveBinary`)
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs:7-18` (usage)

**Interfaces:**
- Produces: `HermeticState.Enter(string? root = null)`; `ColdStartCommand.ParseArguments(string[] args)`
  returning `(string? Binary, string? StateDir)`; `ColdStartCommand.DescribeFileSystem(string path)`.

The benchmarks project has no test project; its verification is running it.

- [ ] **Step 1: Let `HermeticState` take a root**

In `HermeticState.cs`, replace the private constructor's first line and `Enter`:

```csharp
    private HermeticState(string? root)
    {
        _root = root is null
            ? Directory.CreateTempSubdirectory("dtk-bench-")
            : Directory.CreateDirectory(Path.Combine(
                Path.GetFullPath(root),
                string.Create(CultureInfo.InvariantCulture, $"dtk-bench-{Guid.NewGuid():N}")));
```

(keep the rest of the constructor as it is) and

```csharp
    /// <summary>Redirects dtk's state into a fresh directory, under <paramref name="root"/> when given.</summary>
    /// <param name="root">
    /// A directory to create the hermetic state under, or <see langword="null"/> for the temp root.
    /// The temp root is tmpfs on some machines, where a tracking write costs no fsync; passing a
    /// directory on a real disk makes <c>cold-start</c> pay what users pay.
    /// </param>
    internal static HermeticState Enter(string? root = null) => new(root);
```

Add `using System.Globalization;` at the top.

- [ ] **Step 2: Parse `--state-dir` in `ColdStartCommand`**

Replace `ResolveBinary(string[] args)` and its call with argument parsing. Add to `ColdStartCommand`:

```csharp
    /// <summary>
    /// Splits the verb's arguments: <c>--state-dir &lt;dir&gt;</c> and at most one binary path.
    /// </summary>
    /// <param name="args">The command line after the verb.</param>
    /// <exception cref="ArgumentException">An unknown option or a second positional argument.</exception>
    internal static (string? Binary, string? StateDir) ParseArguments(string[] args)
    {
        string? binary = null;
        string? stateDir = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--state-dir" when i + 1 < args.Length:
                    stateDir = args[++i];
                    break;
                case "--state-dir":
                    throw new ArgumentException("--state-dir needs a directory.", nameof(args));
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    throw new ArgumentException($"Unknown option '{option}'.", nameof(args));
                case var path when binary is null:
                    binary = path;
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
```

Change `ResolveBinary` to take the parsed value: replace its signature with
`private static string? ResolveBinary(string? explicitPath)` and its first branch with

```csharp
        if (explicitPath is not null)
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }
```

In `RunAsync`, replace `var binary = ResolveBinary(args);` with

```csharp
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
```

replace `using var state = HermeticState.Enter();` with `using var state = HermeticState.Enter(parsed.StateDir);`,
and add after the `Runtime config:` header line:

```csharp
        await Console.Out.WriteLineAsync($"State: {state.RootPath} ({DescribeFileSystem(state.RootPath)})")
            .ConfigureAwait(false);
```

- [ ] **Step 3: Document the option in `Program.cs`**

Change the `cold-start` usage lines to:

```
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start [dtk] [--state-dir <dir>]
              Times the built dtk binary end to end, out of process: piped, and wrapping a fake dotnet.
              --state-dir puts the hermetic state (and its tracking database) under <dir>, e.g. a real disk.
```

- [ ] **Step 4: Build and run it both ways**

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release && dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release`
Expected: both succeed with no warnings.

Run (timeout 600000): `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks --no-build -- cold-start --state-dir "$HOME/dtk-bench-state" 2>&1 | head -12`
Expected: a `State: /home/…/dtk-bench-state/dtk-bench-… (ext4)` line (or the home filesystem's type) after `Runtime config:`; the run then proceeds.

Run: `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks --no-build -- cold-start --bogus; echo "exit=$?"`
Expected: `Unknown option '--bogus'.`, the usage line, `exit=1`.

Run: `ls "$HOME/dtk-bench-state"`
Expected: empty (the state directory is deleted on dispose). Then `rmdir "$HOME/dtk-bench-state"`.

- [ ] **Step 5: Commit**

Write the message to `/tmp/commit-a1.txt`:

```
bench: let cold-start put its state on a chosen filesystem

The hermetic state lives under the temp root, tmpfs on the measuring
machine, so the harness has never paid the tracking INSERT's fsync.
--state-dir <dir> puts it on a real disk, and the header names the
filesystem so the two kinds of run are distinguishable in artifacts.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add -A benchmarks CLAUDE.md && git commit -F /tmp/commit-a1.txt`

### Task 2: the 1 MB delayed-child scenario

**Files:**
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (`RunAsync`, `MeasurePipeAsync`, `MeasureWrappedAsync`, `RunWrappedDtkAsync`, `FilteredSummaryLine`, `EnsureDtkFiltered`, `EnsureFakeChildRan`)

**Interfaces:**
- Consumes: `LogCorpusGenerator.Generate(string filterKey, CorpusTier tier)` and `CorpusTier.Large`
  (about 1 MB, a successful build); `FakeDotnet.Create(string directoryPath, string output, int exitCode, TimeSpan delay)`.
- Produces: `private sealed record ChildOutput(string Text, int ExitCode, string SummaryLine)` with
  `static ChildOutput From(string text, int exitCode)`.

- [ ] **Step 1: Introduce `ChildOutput`**

Add inside `ColdStartCommand`, replacing the `ExpectedExitCode` constant's uses one by one:

```csharp
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
```

Delete `FilteredSummaryLine` and the `ExpectedExitCode` constant. Change signatures:

- `MeasurePipeAsync(string binary, HermeticState state, ChildOutput fixture)`: the arguments become
  `["pipe", "build", "--exit-code", fixture.ExitCode.ToString(CultureInfo.InvariantCulture)]`,
  stdin is `fixture.Text`, validation is `EnsureDtkFiltered(run, binary, fixture)`.
- `MeasureWrappedAsync(string binary, HermeticState state, ChildOutput output, TimeSpan childSleep, string heading)`:
  `FakeDotnet.Create(…, output.Text, output.ExitCode, childSleep)`; child validation
  `RunFakeChildAsync(fake, output)`; dtk validation `RunWrappedDtkAsync(binary, arguments, fake, output)`.
- `EnsureDtkFiltered(TimedRun run, string binary, ChildOutput output)`: compare
  `run.ExitCode != output.ExitCode` and search `output.SummaryLine`; keep the messages, substituting the
  record's values.
- `EnsureFakeChildRan(TimedRun run, FakeDotnet fake, ChildOutput output)`: compare against `output.Text`
  and `output.ExitCode`.

In `RunAsync`, replace the fixture handling with

```csharp
        var fixture = ChildOutput.From(FixtureCorpus.Load(Fixture), exitCode: 1);
        var largeLog = ChildOutput.From(LogCorpusGenerator.Generate(FilterKeys.Build, CorpusTier.Large), exitCode: 0);
```

update the `Input:` header line to print `fixture.Text.Length` and `fixture.ExitCode`, add a line
`$"Large log: generated {FilterKeys.Build} tier {CorpusTier.Large} ({largeLog.Text.Length} chars), exit code {largeLog.ExitCode}"`,
pass `fixture` to the three existing scenario calls, and add the fourth after them:

```csharp
        await MeasureWrappedAsync(
            binary, state, largeLog, DelayedChildSleep,
            string.Create(
                CultureInfo.InvariantCulture,
                $"dotnet build, child sleeping {DelayedChildSleep.TotalMilliseconds:0} ms, {largeLog.Text.Length / 1024} KB generated log (counting and tee at size)"))
            .ConfigureAwait(false);
```

Add `using DotnetTokenKiller.Benchmarks.Corpus;` if missing (it is already imported for `FixtureCorpus`).

- [ ] **Step 2: Build and run the whole verb once**

Run: `dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release`
Expected: success, no warnings.

Run (timeout 600000): `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks --no-build -- cold-start 2>&1 | tee /tmp/cold-start-a.txt | tail -40`
Expected: four sections; the fourth's heading contains `KB generated log`; its overhead median is
tens of milliseconds above the third's (about 45 ms of counting plus tee work at 1 MB on the AOT
binary; hundreds of milliseconds on a JIT build); no `InvalidOperationException`.

If `ChildOutput.From` throws for the generated log, the build filter prints nothing for a
successful build with exit code 0 at that size; inspect
`SavingsScenarios.FilterFor(FilterKeys.Build).Apply(text, 0)` in a scratch test and pick the tier's
exit code the filter answers (the fixture's shape, a failing build, always has a summary). Do not
change a filter.

- [ ] **Step 3: Commit**

Write `/tmp/commit-a2.txt`:

```
bench: time a wrapped 1 MB log behind a sleeping child

The 2.6 KB fixture cannot show costs that grow with output size. The
fourth scenario prints the generator's large build tier behind a
1000 ms child, so counting and the tee are measured at a realistic size.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add -A benchmarks && git commit -F /tmp/commit-a2.txt`

### Task 3: baseline measurements and docs

**Files:**
- Modify: `CLAUDE.md` (Commands: the `cold-start` lines; Benchmarks: the scenario list and figures)

- [ ] **Step 1: Install a linux-x64 AOT package from this commit**

Run (timeout 600000): `sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log && sh eng/aot/test-package.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log artifacts/aot/tools/linux-x64`
Expected: both succeed; `artifacts/aot/tools/linux-x64/dtk` exists. If Docker is unavailable, run
`dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot/publish` and use
`artifacts/aot/publish/dtk` below, noting "local publish, host glibc" beside the figures.

- [ ] **Step 2: Measure on tmpfs and on disk**

Run (timeout 600000): `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start artifacts/aot/tools/linux-x64/dtk 2>&1 | tee artifacts/cold-start-baseline-tmpfs.txt | tail -60`

Run (timeout 600000): `mkdir -p "$HOME/dtk-bench-state" && dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start artifacts/aot/tools/linux-x64/dtk --state-dir "$HOME/dtk-bench-state" 2>&1 | tee artifacts/cold-start-baseline-disk.txt | tail -60`

Expected: the `State:` lines name `tmpfs` and the home filesystem; on disk, the 1000 ms-child
overhead median is higher than on tmpfs by roughly the disk's fsync cost (5 ms or more on ext4).

- [ ] **Step 3: Record in CLAUDE.md**

In Commands, change the `cold-start` comment block to mention `--state-dir` and that the fourth
scenario prints a 1 MB generated log. In Benchmarks, extend the `cold-start` bullet: four scenarios;
the fourth's purpose; and a dated sentence with both runs' medians (pipe, instant child, 1000 ms
child, 1000 ms child with 1 MB), tmpfs against disk, in the form the existing sentences use:
`Measured 2026-MM-DD, tmpfs → ext4: pipe X → Y ms; …`. Replace the tmpfs caveat sentence with one
that says the on-disk run exists and what it showed.

- [ ] **Step 4: Commit and open the pull request**

Write `/tmp/commit-a3.txt`:

```
docs: record cold-start on tmpfs and on disk, with the 1 MB scenario

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add CLAUDE.md && git commit -F /tmp/commit-a3.txt`

Open a pull request titled `bench: measure cold-start on a real disk and at 1 MB` with the two runs'
medians in its body, ending with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
Parts B and C branch from this one and merge separately.

---

# Part B: tracking hot path

Branch from part A's branch. Tasks 4 to 7 are the journal, 8 to 12 the streaming count; both ship in
one pull request, measured after each half.

### Task 4: `PendingRecord`, its JSON context, and journal writes

**Files:**
- Create: `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecord.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecordJsonContext.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecordJournal.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/PendingRecordJournalTests.cs`

**Interfaces:**
- Consumes: `CommandRecord` (Domain), `TokenStatistics`, `RunOutcome`, `RunSource`.
- Produces: `internal sealed class PendingRecord` with `static PendingRecord From(CommandRecord)` and
  `CommandRecord ToCommandRecord()`; `internal sealed class PendingRecordJournal(string root, TimeSpan? lockWait = null)`
  with `string Root`, `Task WriteAsync(CommandRecord, CancellationToken)`, `int Count()`.
  `DotnetTokenKiller.Infrastructure` already grants `InternalsVisibleTo` to its test project.

- [ ] **Step 1: Write the failing tests**

`tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/PendingRecordJournalTests.cs`:

```csharp
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

    internal static CommandRecord MakeRecord(string command = "build") =>
        new(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), command, "/proj",
            new TokenStatistics(1000, 150, 850, 85.0), TimeSpan.FromMilliseconds(500))
        {
            Success = false,
            Outcome = RunOutcome.RawTailFallback,
            Source = RunSource.Pipe
        };

    [Fact]
    public async Task WriteAsync_CreatesOneFileThatRoundTripsTheRecord()
    {
        await Journal.WriteAsync(MakeRecord());

        var files = Directory.GetFiles(PendingDir, "*.json");
        files.Should().HaveCount(1);
        var pending = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(files[0]), PendingRecordJsonContext.Default.PendingRecord)!;
        pending.Version.Should().Be(PendingRecord.CurrentVersion);
        pending.ToCommandRecord().Should().BeEquivalentTo(MakeRecord());
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
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~PendingRecordJournalTests"`
Expected: build errors, `PendingRecordJournal` and `PendingRecord` do not exist.

- [ ] **Step 3: Write `PendingRecord`**

```csharp
using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>
/// One run as it waits in the journal: the flat fields of <see cref="CommandRecord"/>, with strings
/// where the database has strings, plus a version so a later dtk can tell an old file's shape.
/// </summary>
/// <remarks>
/// A class with init-only properties rather than a positional record: twelve constructor parameters
/// would trip S107, and the serializer binds properties either way.
/// </remarks>
internal sealed class PendingRecord
{
    /// <summary>The shape this dtk writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The shape version the file was written with.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>When the command ran, round-trip format, UTC.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>The dotnet subcommand name.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>The working directory when the command ran.</summary>
    public string ProjectPath { get; init; } = string.Empty;

    /// <summary>Estimated tokens in the raw output.</summary>
    public int InputTokens { get; init; }

    /// <summary>Estimated tokens in the filtered output.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Tokens saved by filtering.</summary>
    public int SavedTokens { get; init; }

    /// <summary>Percentage of tokens saved.</summary>
    public double SavingsPercentage { get; init; }

    /// <summary>Wall-clock time of the command, in milliseconds.</summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>Whether the command exited with code 0.</summary>
    public bool Success { get; init; } = true;

    /// <summary>The <see cref="RunOutcome"/> name.</summary>
    public string Outcome { get; init; } = nameof(RunOutcome.Filtered);

    /// <summary>The <see cref="RunSource"/> name.</summary>
    public string Source { get; init; } = nameof(RunSource.Run);

    /// <summary>The journal shape of <paramref name="record"/>.</summary>
    /// <param name="record">The record to write.</param>
    public static PendingRecord From(CommandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new PendingRecord
        {
            Timestamp = record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Command = record.Command,
            ProjectPath = record.ProjectPath,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            SavedTokens = record.SavedTokens,
            SavingsPercentage = record.SavingsPercentage,
            ExecutionTimeMs = record.ExecutionTime.TotalMilliseconds,
            Success = record.Success,
            Outcome = record.Outcome.ToString(),
            Source = record.Source.ToString()
        };
    }

    /// <summary>The record this file describes; unknown outcome or source names degrade as the database reader's do.</summary>
    public CommandRecord ToCommandRecord()
    {
        var outcome = Enum.TryParse<RunOutcome>(Outcome, ignoreCase: true, out var parsedOutcome)
            ? parsedOutcome
            : RunOutcome.Filtered;
        var source = Enum.TryParse<RunSource>(Source, ignoreCase: true, out var parsedSource)
            ? parsedSource
            : RunSource.Run;

        return new CommandRecord(
            DateTimeOffset.ParseExact(Timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Command,
            ProjectPath,
            new TokenStatistics(InputTokens, OutputTokens, SavedTokens, SavingsPercentage),
            TimeSpan.FromMilliseconds(ExecutionTimeMs))
        {
            Success = Success,
            Outcome = outcome,
            Source = source
        };
    }
}
```

`PendingRecordJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Infrastructure.Tracking;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PendingRecord))]
internal sealed partial class PendingRecordJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Write the journal's write side**

`PendingRecordJournal.cs` (the fold arrives in Task 5):

```csharp
using System.Globalization;
using System.Text.Json;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>
/// Where a tracked run leaves its record without opening SQLite: one JSON file per run under
/// <c>pending</c> beside the database, folded into the database by whoever reads it next.
/// </summary>
/// <remarks>
/// One file per run rather than one appended file: no two processes ever hold the same handle, so
/// neither POSIX append atomicity nor Windows share modes are relied on, and a process killed
/// mid-write can only leave a truncated file of its own, which a fold deletes.
/// </remarks>
/// <param name="root">The journal directory.</param>
/// <param name="lockWait">How long a waiting fold retries for the lock; the spec's 2 s unless a test shortens it.</param>
internal sealed class PendingRecordJournal(string root, TimeSpan? lockWait = null)
{
    private const string FileExtension = ".json";

    /// <summary>The journal directory, created on the first write.</summary>
    public string Root { get; } = root;

    /// <summary>Writes one file for <paramref name="record"/>. Costs a directory check and one small file; no fsync.</summary>
    /// <param name="record">The run to journal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(Root);

        // Ticks first so a directory listing is chronological; the pid and a random suffix keep two
        // processes, or two runs in one tick, from colliding. CreateNew turns a collision into an
        // exception rather than a silently shared file.
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow.Ticks:D19}-{Environment.ProcessId}-{Guid.NewGuid():N}{FileExtension}");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var stream = new FileStream(Path.Combine(Root, name), options);
#pragma warning restore CA2007
        await JsonSerializer.SerializeAsync(
                stream, PendingRecord.From(record), PendingRecordJsonContext.Default.PendingRecord, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The number of runs waiting to be folded; zero when the directory does not exist.</summary>
    public int Count() =>
        Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*" + FileExtension).Count() : 0;
}
```

The `lockWait` parameter is unused until Task 5; if the build flags it (S1172 does not apply to
primary constructors, but check), keep it and reference it in a private readonly field:
`private readonly TimeSpan _lockWait = lockWait ?? TimeSpan.FromSeconds(2);`.

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~PendingRecordJournalTests"`
Expected: 3 passed.

- [ ] **Step 6: Commit**

Write `/tmp/commit-b4.txt`:

```
feat: journal each tracked run as one JSON file

A run's record no longer needs SQLite to be written: PendingRecordJournal
writes one small file per run, with no fsync. Folding into the database
comes next.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src/DotnetTokenKiller.Infrastructure/Tracking tests/DotnetTokenKiller.Infrastructure.Tests/Tracking && git commit -F /tmp/commit-b4.txt`

### Task 5: exact-once folds

**Files:**
- Create: `src/DotnetTokenKiller.Infrastructure/Tracking/FoldOutcome.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/PendingRecordJournal.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/PendingRecordJournalTests.cs`

**Interfaces:**
- Produces: `internal readonly record struct FoldOutcome(bool Folded, int Records, int Corrupt)`;
  `Task<FoldOutcome> PendingRecordJournal.FoldAsync(Func<string, CancellationToken, Task<bool>> committed, Func<IReadOnlyList<string>, IReadOnlyList<CommandRecord>, CancellationToken, Task> commit, bool wait, CancellationToken cancellationToken = default)`;
  `void PendingRecordJournal.Clear()`.

- [ ] **Step 1: Write the failing tests**

Append to `PendingRecordJournalTests`:

```csharp
    private static Task<bool> NeverCommitted(string id, CancellationToken ct) => Task.FromResult(false);

    [Fact]
    public async Task FoldAsync_CommitsEveryRecordOnceAndDeletesTheFiles()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord("build"));
        await journal.WriteAsync(MakeRecord("test"));
        var committed = new List<CommandRecord>();
        var ids = new List<string>();

        var outcome = await journal.FoldAsync(NeverCommitted, (foldIds, records, _) =>
        {
            ids.AddRange(foldIds);
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 2, 0));
        committed.Select(r => r.Command).Should().BeEquivalentTo(["build", "test"]);
        ids.Should().ContainSingle();
        journal.Count().Should().Be(0);
        Directory.GetDirectories(PendingDir, "folding-*").Should().BeEmpty();
    }

    [Fact]
    public async Task FoldAsync_NothingPending_DoesNotCommit()
    {
        var commits = 0;

        var outcome = await Journal.FoldAsync(NeverCommitted, (_, _, _) => { commits++; return Task.CompletedTask; }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 0, 0));
        commits.Should().Be(0);
    }

    [Fact]
    public async Task FoldAsync_CommitThrows_LeavesTheClaimForTheNextFold()
    {
        var journal = Journal;
        await journal.WriteAsync(MakeRecord());

        var act = () => journal.FoldAsync(NeverCommitted, (_, _, _) => throw new IOException("disk full"), wait: true);

        await act.Should().ThrowAsync<IOException>();
        Directory.GetDirectories(PendingDir, "folding-*").Should().ContainSingle()
            .Which.Should().Match(dir => Directory.GetFiles(dir, "*.json").Length == 1);

        var recovered = new List<CommandRecord>();
        var ids = new List<string>();
        var outcome = await journal.FoldAsync(NeverCommitted, (foldIds, records, _) =>
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

        var outcome = await journal.FoldAsync(NeverCommitted, (_, records, _) =>
        {
            committed.AddRange(records);
            return Task.CompletedTask;
        }, wait: true);

        outcome.Should().Be(new FoldOutcome(true, 1, 1));
        committed.Should().ContainSingle();
        journal.Count().Should().Be(0);
    }

    [Fact]
    public async Task FoldAsync_LockHeld_ReturnsAtOnceWithoutWaitAndTimesOutWithWait()
    {
        var journal = new PendingRecordJournal(PendingDir, lockWait: TimeSpan.FromMilliseconds(200));
        await journal.WriteAsync(MakeRecord());
        Directory.CreateDirectory(PendingDir);
        await using var held = new FileStream(
            Path.Combine(PendingDir, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var skipped = await journal.FoldAsync(NeverCommitted, (_, _, _) => Task.CompletedTask, wait: false);
        var waiting = () => journal.FoldAsync(NeverCommitted, (_, _, _) => Task.CompletedTask, wait: true);

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
```

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~PendingRecordJournalTests"`
Expected: build errors, `FoldAsync`, `Clear`, `FoldOutcome` missing.

- [ ] **Step 3: Write `FoldOutcome` and the fold**

`FoldOutcome.cs`:

```csharp
namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>What one <see cref="PendingRecordJournal.FoldAsync"/> did.</summary>
/// <param name="Folded"><see langword="false"/> when the lock was busy and the caller did not wait.</param>
/// <param name="Records">Records handed to the commit.</param>
/// <param name="Corrupt">Files deleted because they did not parse.</param>
internal readonly record struct FoldOutcome(bool Folded, int Records, int Corrupt);
```

Add to `PendingRecordJournal` (with `using System.Diagnostics;`):

```csharp
    private const string LockFileName = ".lock";
    private const string ClaimPrefix = "folding-";
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(25);
    private readonly TimeSpan _lockWait = lockWait ?? TimeSpan.FromSeconds(2);

    /// <summary>
    /// Folds every waiting run into the database exactly once, whatever process dies when.
    /// </summary>
    /// <remarks>
    /// Under an exclusive lock: leftover claim directories are deleted if <paramref name="committed"/>
    /// knows their id (a fold that died after its commit) or refolded if not (one that died before);
    /// then every pending file is moved into a new claim directory, parsed, and handed to
    /// <paramref name="commit"/> together with every claim id involved, in one call. The claim
    /// directories are deleted only after the commit returns. A commit that throws leaves them for
    /// the next fold; a process that dies releases the lock with its handle.
    /// </remarks>
    /// <param name="committed">Whether the database already holds the fold with this id.</param>
    /// <param name="commit">Inserts the records and every fold id in one transaction.</param>
    /// <param name="wait">
    /// Retry for the lock for up to the journal's wait and then throw <see cref="TimeoutException"/>
    /// (readers), or return at once when it is busy (a writer's background fold).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FoldOutcome> FoldAsync(
        Func<string, CancellationToken, Task<bool>> committed,
        Func<IReadOnlyList<string>, IReadOnlyList<CommandRecord>, CancellationToken, Task> commit,
        bool wait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(commit);

        Directory.CreateDirectory(Root);

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var lockHandle = await TryLockAsync(wait, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        if (lockHandle is null)
        {
            return new FoldOutcome(false, 0, 0);
        }

        var claimIds = new List<string>();
        var claimDirs = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(Root, ClaimPrefix + "*").ToList())
        {
            var id = Path.GetFileName(dir)[ClaimPrefix.Length..];
            if (await committed(id, cancellationToken).ConfigureAwait(false))
            {
                Directory.Delete(dir, recursive: true);
            }
            else
            {
                claimIds.Add(id);
                claimDirs.Add(dir);
            }
        }

        var files = Directory.EnumerateFiles(Root, "*" + FileExtension).ToList();
        if (files.Count > 0)
        {
            var id = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(Root, ClaimPrefix + id);
            Directory.CreateDirectory(dir);
            foreach (var file in files)
            {
                File.Move(file, Path.Combine(dir, Path.GetFileName(file)));
            }

            claimIds.Add(id);
            claimDirs.Add(dir);
        }

        if (claimDirs.Count == 0)
        {
            return new FoldOutcome(true, 0, 0);
        }

        var records = new List<CommandRecord>();
        var corrupt = 0;
        var claimedFiles = claimDirs.SelectMany(d => Directory.EnumerateFiles(d, "*" + FileExtension)).ToList();
        foreach (var file in claimedFiles)
        {
            var pending = await TryReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (pending is null)
            {
                corrupt++;
                File.Delete(file);
                continue;
            }

            records.Add(pending.ToCommandRecord());
        }

        await commit(claimIds, records, cancellationToken).ConfigureAwait(false);

        foreach (var dir in claimDirs)
        {
            Directory.Delete(dir, recursive: true);
        }

        return new FoldOutcome(true, records.Count, corrupt);
    }

    /// <summary>Deletes every pending file and claim directory. The lock file stays; it is never deleted.</summary>
    public void Clear()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*" + FileExtension).ToList())
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.EnumerateDirectories(Root, ClaimPrefix + "*").ToList())
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<PendingRecord?> TryReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var stream = File.OpenRead(file);
#pragma warning restore CA2007
            return await JsonSerializer
                .DeserializeAsync(stream, PendingRecordJsonContext.Default.PendingRecord, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A process killed mid-write leaves a truncated file; nothing else writes here.
            return null;
        }
    }

    /// <summary>
    /// Opens the lock file with <see cref="FileShare.None"/>: an exclusive <c>flock</c> on Unix, a
    /// sharing violation for everyone else on Windows, released with the handle in both cases.
    /// </summary>
    private async Task<FileStream?> TryLockAsync(bool wait, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Root, LockFileName);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            try
            {
                return new FileStream(path, options);
            }
            catch (IOException) when (!wait)
            {
                return null;
            }
            catch (IOException ex) when (Stopwatch.GetElapsedTime(started) >= _lockWait)
            {
                throw new TimeoutException($"Another process held the tracking journal lock at {path} for over {_lockWait.TotalSeconds:0.#} s.", ex);
            }
            catch (IOException)
            {
                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
        }
    }
```

`FileMode.OpenOrCreate` with `FileShare.None` on Windows also throws `UnauthorizedAccessException`
in some sharing cases; if the lock test fails on Windows CI with that type, widen the three
`catch (IOException)` filters to `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)`.

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~PendingRecordJournalTests"`
Expected: 10 passed (the lock test takes about 0.3 s).

- [ ] **Step 5: Commit**

Write `/tmp/commit-b5.txt`:

```
feat: fold the tracking journal exactly once

FoldAsync claims pending files into a directory named by a fold id under
an exclusive lock, commits records and ids together, and deletes the
claim only afterwards. A fold that dies before its commit is refolded;
one that dies after it is recognised by its id and discarded.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src/DotnetTokenKiller.Infrastructure/Tracking tests/DotnetTokenKiller.Infrastructure.Tests/Tracking && git commit -F /tmp/commit-b5.txt`

### Task 6: `SqliteTracker` writes to the journal and folds on read

**Files:**
- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerJournalTests.cs` (new)

**Interfaces:**
- Consumes: `PendingRecordJournal` from Tasks 4 and 5.
- Produces: `SqliteTracker(string connectionString, int defaultRetentionDays = 90, int foldThreshold = SqliteTracker.DefaultFoldThreshold)`;
  `public const int DefaultFoldThreshold = 64`. `ITracker` is unchanged, so every stub and fake keeps compiling.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

/// <summary>The tracker on a file data source, where the journal is in play (":memory:" has no directory).</summary>
public sealed class SqliteTrackerJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtk-tracker-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_root, "tracking.db");

    private string PendingDir => Path.Combine(_root, "pending");

    private SqliteTracker Create(int foldThreshold = SqliteTracker.DefaultFoldThreshold, int retentionDays = 90) =>
        new($"Data Source={DbPath};Pooling=False", retentionDays, foldThreshold);

    private int PendingFiles => Directory.Exists(PendingDir) ? Directory.GetFiles(PendingDir, "*.json").Length : 0;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAsync_WritesAJournalFileAndOpensNoDatabase()
    {
        await using var sut = Create();

        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        PendingFiles.Should().Be(1);
        File.Exists(DbPath).Should().BeFalse("a tracked run must not touch SQLite");
    }

    [Fact]
    public async Task GetSummaryAsync_FoldsTheJournalFirst()
    {
        await using var sut = Create();
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("build"));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("build"));
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord("test"));

        var summary = await sut.GetSummaryAsync(days: 3650, projectPath: null);

        summary.TotalCommands.Should().Be(3);
        PendingFiles.Should().Be(0);
        File.Exists(DbPath).Should().BeTrue();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsFoldedRecordsWithEveryField()
    {
        await using var sut = Create();
        var record = PendingRecordJournalTests.MakeRecord();
        await sut.RecordAsync(record);

        var history = await sut.GetHistoryAsync(days: 3650, projectPath: null);

        history.Should().ContainSingle().Which.Should().BeEquivalentTo(record);
    }

    [Fact]
    public async Task GetCoverageAsync_AndCleanupAsync_FoldFirst()
    {
        await using var sut = Create(retentionDays: 90);
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord() with { Timestamp = DateTimeOffset.UtcNow.AddDays(-100) });
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord() with { Timestamp = DateTimeOffset.UtcNow });

        await sut.CleanupAsync(retentionDays: 90);
        var coverage = await sut.GetCoverageAsync(days: 3650, projectPath: null);

        coverage.TotalRuns.Should().Be(1, "retention runs at fold time and the 100-day-old row is gone");
        PendingFiles.Should().Be(0);
    }

    [Fact]
    public async Task ResetAsync_ClearsTheJournalToo()
    {
        await using var sut = Create();
        await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        await sut.ResetAsync();

        PendingFiles.Should().Be(0);
        (await sut.GetHistoryAsync(days: 3650, projectPath: null)).Should().BeEmpty();
    }

    [Fact]
    public async Task WarmUpAsync_BelowTheThreshold_CreatesTheDirectoryAndOpensNothing()
    {
        await using (var sut = Create(foldThreshold: 64))
        {
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.WarmUpAsync();
        }

        Directory.Exists(PendingDir).Should().BeTrue();
        PendingFiles.Should().Be(1);
        File.Exists(DbPath).Should().BeFalse();
    }

    [Fact]
    public async Task WarmUpAsync_AtTheThreshold_FoldsInTheBackground()
    {
        await using (var sut = Create(foldThreshold: 2))
        {
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.RecordAsync(PendingRecordJournalTests.MakeRecord());
            await sut.WarmUpAsync();
            // DisposeAsync waits for the background fold, so the assertions below see its result.
        }

        PendingFiles.Should().Be(0);
        File.Exists(DbPath).Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_AfterDispose_Throws()
    {
        var sut = Create();
        await sut.DisposeAsync();

        var act = () => sut.RecordAsync(PendingRecordJournalTests.MakeRecord());

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
```

`CoverageSummary.TotalRuns` is the existing property name (see `ReadCoverageAsync`). `MakeRecord`
is `internal static` in `PendingRecordJournalTests` (Task 4).

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~SqliteTrackerJournalTests"`
Expected: build error on the three-argument constructor, or failures on the first assertions.

- [ ] **Step 3: Change `SqliteTracker`**

Signature and fields:

```csharp
public sealed class SqliteTracker(
    string connectionString,
    int defaultRetentionDays = 90,
    int foldThreshold = SqliteTracker.DefaultFoldThreshold)
    : ITracker, IDisposable, IAsyncDisposable
{
    /// <summary>Pending runs at which a warm-up folds the journal in the background, so it never grows unbounded.</summary>
    public const int DefaultFoldThreshold = 64;

    private const int HistoryLimit = 500;
    …
    private readonly PendingRecordJournal? _journal = CreateJournal(connectionString);
    private Task? _backgroundFold;
```

Add the journal factory beside `EnsureDataDirectory`:

```csharp
    /// <summary>The journal beside the database file, or <see langword="null"/> for an in-memory database.</summary>
    private static PendingRecordJournal? CreateJournal(string cs)
    {
        if (cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var csb = new SqliteConnectionStringBuilder(cs);
        if (string.IsNullOrWhiteSpace(csb.DataSource) || csb.DataSource == ":memory:")
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(csb.DataSource)) ?? Environment.CurrentDirectory;
        return new PendingRecordJournal(Path.Combine(directory, "pending"));
    }
```

`RecordAsync`: before taking the semaphore, add

```csharp
        if (_journal is { } journal)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await journal.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }
```

and move the `INSERT` into a helper the fold reuses:

```csharp
    private async Task InsertAsync(CommandRecord record, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = CreateCommand();
#pragma warning restore CA2007
        cmd.Transaction = transaction;
        cmd.CommandText = """
                          INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                              saved_tokens, savings_percentage, execution_time_ms, success, outcome, source)
                          VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms, @success, @outcome, @source)
                          """;
        cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@cmd", record.Command);
        cmd.Parameters.AddWithValue("@path", record.ProjectPath);
        cmd.Parameters.AddWithValue("@in", record.InputTokens);
        cmd.Parameters.AddWithValue("@out", record.OutputTokens);
        cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
        cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
        cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
        cmd.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("@outcome", record.Outcome.ToString());
        cmd.Parameters.AddWithValue("@source", record.Source.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
```

so the `:memory:` path of `RecordAsync` becomes `await InsertAsync(record, null, cancellationToken)` inside its semaphore.

`WarmUpAsync`:

```csharp
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (_journal is not { } journal)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }

            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(journal.Root);

        // The journal folds itself on read; this keeps it bounded for a user who never reads. The
        // fold's SQLite setup, inserts and fsync run while the child does, and RecordAsync never
        // waits for it: an aborted fold is refolded next time (see PendingRecordJournal.FoldAsync).
        if (journal.Count() >= foldThreshold)
        {
            _backgroundFold = Task.Run(() => FoldInBackgroundAsync(cancellationToken), CancellationToken.None);
        }
    }

    private async Task FoldInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FoldAsync(wait: false, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: best effort; the next read folds what this one did not
        }
    }

    private Task BackgroundFoldAsync() => _backgroundFold ?? Task.CompletedTask;

    private async Task FoldAsync(bool wait, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await FoldLockedAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Folds the journal. The caller holds the semaphore and has initialized the connection.</summary>
    private async Task FoldLockedAsync(bool wait, CancellationToken cancellationToken)
    {
        if (_journal is not { } journal)
        {
            return;
        }

        await journal.FoldAsync(IsFoldCommittedAsync, CommitFoldAsync, wait, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsFoldCommittedAsync(string foldId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var cmd = CreateCommand();
#pragma warning restore CA2007
        cmd.CommandText = "SELECT COUNT(*) FROM folds WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", foldId);
        return (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;
    }

    private async Task CommitFoldAsync(
        IReadOnlyList<string> foldIds, IReadOnlyList<CommandRecord> records, CancellationToken cancellationToken)
    {
        var connection = _connection ?? throw new InvalidOperationException("The tracker was used before it was initialized.");
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007
        foreach (var record in records)
        {
            await InsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
        }

        foreach (var foldId in foldIds)
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var cmd = CreateCommand();
#pragma warning restore CA2007
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO folds (id) VALUES (@id)";
            cmd.Parameters.AddWithValue("@id", foldId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await CleanupCoreAsync(defaultRetentionDays, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
```

`CleanupCoreAsync` gains a `SqliteTransaction? transaction` parameter and sets `cmd.Transaction = transaction`;
its other caller (`EnsureInitializedAsync`) passes `null`. Microsoft.Data.Sqlite refuses to execute
a command on a connection with an open transaction unless the command carries it.

Readers: in `ExecuteWithFilterAsync`, after `EnsureInitializedAsync`, add
`await FoldLockedAsync(wait: true, cancellationToken).ConfigureAwait(false);`. Same line in
`CleanupAsync` before `CleanupCoreAsync(retentionDays, null, cancellationToken)`.

`ResetAsync`: change the command text to `"DELETE FROM commands; DELETE FROM folds"` and add
`_journal?.Clear();` after it, inside the semaphore.

`InitializeSchemaAsync`: append to the `CREATE` script:

```sql
CREATE TABLE IF NOT EXISTS folds (
    id TEXT PRIMARY KEY
);
```

`DisposeAsync`: as its first statement after the `_disposed` check, add
`await BackgroundFoldAsync().ConfigureAwait(false);` so an in-flight fold finishes before the
semaphore is taken (it swallows its own exceptions). `Dispose` is unchanged: a fold still waiting
for the semaphore finds the tracker disposed and is swallowed by `FoldInBackgroundAsync`.

Update the class remarks and the `WarmUpAsync` and `RecordAsync` doc comments to say a file data
source journals and folds on read; `:memory:` inserts directly.

- [ ] **Step 4: Run all Infrastructure tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests`
Expected: every test passes, the existing `SqliteTrackerTests` (`:memory:`) included.

- [ ] **Step 5: Build everything and run the other library tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test tests/DotnetTokenKiller.Application.Tests && dtk dotnet test tests/DotnetTokenKiller.Domain.Tests`
Expected: no warnings; all pass.

- [ ] **Step 6: Commit**

Write `/tmp/commit-b6.txt`:

```
perf: take SQLite off the tracked run's write path

RecordAsync writes the journal; every reader folds it first; a warm-up
folds in the background once 64 runs wait. A tracked run no longer
opens a connection, so it pays neither the INSERT's fsync nor the
native library's setup, and concurrent runs no longer contend for the
write lock. A folds table makes recovery exact-once.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src tests && git commit -F /tmp/commit-b6.txt`

### Task 7: the loader test, the docs, and the journal measurement

**Files:**
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs`
- Modify: `README.md:105-111`, `src/DotnetTokenKiller.Cli/README.md:27-33`, `docfx/articles/getting-started.md:23-29`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Move the positive control to `gain`**

Rename the test to `PipeBuild_NeverLoadsSqlite_GainDoesAsync` and replace its body between the
sandbox creation and the `finally` with:

```csharp
            var sandbox = new ParitySandbox(root);

            var tracked = await RunPipeBuildAsync(binary, sandbox);
            tracked.Stderr.Should().NotContain(SqliteLibrary, "a tracked run writes the journal and must not load SQLite");

            var gain = await RunWithLoaderTraceAsync(binary, sandbox, ["gain", "--json"]);
            gain.ExitCode.Should().Be(0, gain.Stdout + gain.Stderr);
            gain.Stdout.Should().Contain("\"TotalCommands\":1", "gain folds the journal it just found");
            gain.Stderr.Should().Contain(SqliteLibrary,
                "gain reads the database, so dyld must report loading SQLite, or the assertions above prove nothing");

            var disable = await ParityProcess.RunAsync(
                ParityRunner.CreateStartInfo(binary, sandbox, ["config", "set", "tracking.enabled", "false"]), stdin: null);
            disable.ExitCode.Should().Be(0, disable.Stdout + disable.Stderr);

            var trackingOff = await RunPipeBuildAsync(binary, sandbox);
            trackingOff.Stderr.Should().NotContain(SqliteLibrary, "tracking off must not load SQLite");
```

and extract the environment setup into a helper both callers use:

```csharp
    private static async Task<ProcessOutput> RunWithLoaderTraceAsync(string binary, ParitySandbox sandbox, string[] arguments, string? stdin = null)
    {
        var startInfo = ParityRunner.CreateStartInfo(binary, sandbox, arguments);
        startInfo.Environment["DYLD_PRINT_LIBRARIES"] = "1";
        return await ParityProcess.RunAsync(startInfo, stdin);
    }
```

with `RunPipeBuildAsync` calling it with `["pipe", "build", "--exit-code", "1"]` and the fixture
text as stdin, keeping its two assertions. Update the class summary: a tracked run loads no SQLite
on any platform; `gain` is the positive control.

- [ ] **Step 2: Build the integration test project**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests`
Expected: no warnings. (The test itself runs on macOS in CI.)

- [ ] **Step 3: Update the three platform paragraphs**

In each of the three files, replace the sentence beginning "The framework-dependent build cannot
record token savings on glibc older than 2.34" with:

> The framework-dependent build records runs on any glibc, but `dtk gain` cannot report them on glibc
> older than 2.34, because its SQLite library needs GLIBC_2.34
> ([ericsink/SQLitePCL.raw#674](https://github.com/ericsink/SQLitePCL.raw/issues/674)); filtering
> and recording still work, and the records are reported once the tool runs on a newer glibc.

- [ ] **Step 4: Describe the journal in CLAUDE.md**

Add a `## Tracking` section after `## Native AOT`:

> A tracked run writes one JSON file to `pending/` beside the tracking database and opens no SQLite
> connection. Readers (`gain`, with `--coverage` and `--export`; `reset`; retention) fold the journal first under
> `pending/.lock`, claiming files into `folding-<id>/` and recording the id in the `folds` table, so a
> fold interrupted at any point is neither lost nor duplicated. A warm-up folds in the background when
> 64 or more files wait. `SqliteLoaderTests` uses `gain` as its positive control for that reason.
> `:memory:` data sources insert directly.

- [ ] **Step 5: Measure the journal**

Repeat Task 3 steps 1 and 2 (pack, install, `cold-start` on tmpfs and with `--state-dir` on disk),
saving to `artifacts/cold-start-journal-{tmpfs,disk}.txt`. Compare with the baseline files from
Task 3: on disk, the 1000 ms-child overhead must drop by at least 5 ms; no scenario's median may
rise by more than 3 ms. Every scenario must still pass the harness's row check (it folds through
`GetSummaryAsync`). Add the figures to the Benchmarks section as `Journal, measured …: …`.

If the pipe or instant-child median rose by more than 3 ms, the likely cause is the journal
directory creation or a background fold racing the run (the harness's 55 runs cross the threshold
once); read the on-disk run's `State:` line and the fourth section, then stop and report rather
than tuning.

- [ ] **Step 6: Commit**

Write `/tmp/commit-b7.txt`:

```
docs: journal semantics, old-glibc wording, and the loader test's new control

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add -A tests README.md src/DotnetTokenKiller.Cli/README.md docfx CLAUDE.md && git commit -F /tmp/commit-b7.txt`

### Task 8: `AnsiStrip.EndsInsideEscapeSequence`

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Text/AnsiStrip.cs`
- Test: `tests/DotnetTokenKiller.Domain.Tests/Text/AnsiStripTests.cs`

**Interfaces:**
- Produces: `public static bool AnsiStrip.EndsInsideEscapeSequence(ReadOnlySpan<char> text)`.

- [ ] **Step 1: Write the failing tests**

Append to `AnsiStripTests`:

```csharp
    [Theory]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    [InlineData("red " + Esc + "[31m", false)]
    [InlineData("cut " + Esc + "[31", true)]
    [InlineData("title " + Esc + "]0;dtk" + Bel, false)]
    [InlineData("title " + Esc + "]0;dtk" + Esc + "\\", false)]
    [InlineData("title " + Esc + "]0;dtk\nmore", true)]
    [InlineData(Esc + "]0;open" + Esc + "[0m", false)]
    [InlineData("lone " + Esc, true)]
    [InlineData("other " + Esc + "M", false)]
    public void EndsInsideEscapeSequence_TellsATerminatedTailFromAnOpenOne(string text, bool expected)
    {
        AnsiStrip.EndsInsideEscapeSequence(text).Should().Be(expected);
    }
```

The eighth case: an OSC that never terminates but is followed by a CSI. The OSC pattern cannot cross
an ESC, so `Strip` treats the OSC's ESC as bare either way, and the tail is the terminated CSI.

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~AnsiStripTests"`
Expected: build error, `EndsInsideEscapeSequence` missing.

- [ ] **Step 3: Implement**

Add to `AnsiStrip`:

```csharp
    /// <summary>
    /// Whether <paramref name="text"/> ends inside a CSI or OSC sequence that later text would
    /// complete, so that stripping it alone and stripping it with what follows could differ.
    /// </summary>
    /// <remarks>
    /// Only the last escape matters: the OSC pattern cannot cross an ESC, so every earlier
    /// sequence is either complete or already a bare ESC, whatever follows. A lone trailing ESC
    /// counts as inside, since either sequence could start there.
    /// </remarks>
    /// <param name="text">The text so far.</param>
    public static bool EndsInsideEscapeSequence(ReadOnlySpan<char> text)
    {
        var last = text.LastIndexOf('\x1b');
        if (last < 0)
        {
            return false;
        }

        var tail = text[last..];
        if (tail.Length == 1)
        {
            return true;
        }

        return tail[1] switch
        {
            '[' => !CsiAtStartPattern().IsMatch(tail),
            ']' => !OscAtStartPattern().IsMatch(tail),
            _ => false
        };
    }

    // The two sequence patterns anchored at the start, for the tail check above.
    [GeneratedRegex(@"^\x1b\[[0-9;]*[A-Za-z]")]
    private static partial Regex CsiAtStartPattern();

    [GeneratedRegex(@"^\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")]
    private static partial Regex OscAtStartPattern();
```

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~AnsiStripTests"`
Expected: all pass, the 10 new cases included.

- [ ] **Step 5: Commit**

Write `/tmp/commit-b8.txt`:

```
feat: tell whether text ends inside an escape sequence

Needed to cut streamed output into chunks that strip the same way alone
as they do together.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src/DotnetTokenKiller.Domain tests/DotnetTokenKiller.Domain.Tests && git commit -F /tmp/commit-b8.txt`

### Task 9: `ChunkedTokenCounter` and its exactness proof

**Files:**
- Create: `src/DotnetTokenKiller.Application/Helpers/ChunkedTokenCounter.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/ChunkedTokenCounterTests.cs`

**Interfaces:**
- Consumes: `TokenEstimator.Estimate(string, TokenizerModel)`, `AnsiStrip.Strip`, `AnsiStrip.EndsInsideEscapeSequence`,
  `FixtureCorpus.Names`/`Load`, `LogCorpusGenerator.Generate(string, CorpusTier)`, `FilterKeys`.
- Produces: `public sealed class ChunkedTokenCounter` with `public const int DefaultMinChunkChars = 64 * 1024`,
  public constructor `(TokenizerModel model, int minChunkChars = DefaultMinChunkChars)`, internal constructor
  `(TokenizerModel model, int minChunkChars, Func<string, TokenizerModel, int> estimate)`,
  `void Append(ReadOnlySpan<char> text)`, `void Finish(string trailing)`, `Task<int> TotalAsync()`,
  `internal static int FindLastSafeCut(string text)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class ChunkedTokenCounterTests
{
    private const string Esc = "\e";

    // Small enough that every input is cut hundreds of times; production uses 64 K chars.
    private const int TinyChunk = 64;

    private static readonly TokenizerModel[] Models = [TokenizerModel.Cl100kBase, TokenizerModel.O200kBase];

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var name in FixtureCorpus.Names)
        {
            data.Add(name);
        }

        return data;
    }

    public static TheoryData<string, CorpusTier> GeneratedLogs()
    {
        var data = new TheoryData<string, CorpusTier>();
        foreach (var key in new[] { FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore, FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage })
        {
            foreach (var tier in Enum.GetValues<CorpusTier>())
            {
                data.Add(key, tier);
            }
        }

        return data;
    }

    public static TheoryData<string> Adversarial() =>
    [
        "a\n\n\nb",
        "foo \n  bar\n\tbaz",
        "x;\n\ny",
        "path\n/usr/bin\n/etc\n",
        "line\r\nnext\r\n\r\nlast",
        "\t\t\n \n\t",
        new string('\n', 200),
        "no newline at all",
        "",
        "trailing newline\n",
        "  leading spaces\n   more\n",
        Esc + "[31mred" + Esc + "[0m\nplain\n",
        Esc + "]0;title\nmore\aafter\n",
        Esc + "]0;never terminated\nline\nline\n",
        Esc + "[3" + "1m\nrest\n",
        "<|endoftext|>\nx\n<|endoftext|>",
        "é ñ 日本語\n中文\n🚀 emoji\n",
        "don't\nwe're\nI'll\n",
        "123456\n7890\n",
        string.Concat(Enumerable.Repeat("word ", 5000)),
        string.Concat(Enumerable.Repeat("error CS0001: boom\n", 800)),
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fixture_ChunkedCountEqualsWholeCount(string fixture)
    {
        await AssertExactAsync(FixtureCorpus.Load(fixture), string.Empty);
        await AssertExactAsync(FixtureCorpus.Load(fixture), "error: stderr said something\n");
    }

    [Theory]
    [MemberData(nameof(GeneratedLogs))]
    public async Task GeneratedLog_ChunkedCountEqualsWholeCount(string filterKey, CorpusTier tier)
    {
        await AssertExactAsync(LogCorpusGenerator.Generate(filterKey, tier), string.Empty);
    }

    [Theory]
    [MemberData(nameof(Adversarial))]
    public async Task Adversarial_ChunkedCountEqualsWholeCount(string text)
    {
        await AssertExactAsync(text, string.Empty);
        await AssertExactAsync(text, " trailing that starts with a space\n");
        await AssertExactAsync(text, "/slash first");
    }

    [Fact]
    public void FindLastSafeCut_CutsAfterANewlineBeforeNonSpaceOnly()
    {
        ChunkedTokenCounter.FindLastSafeCut("ab\ncd\nef").Should().Be(6);
        ChunkedTokenCounter.FindLastSafeCut("ab\ncd\n ef").Should().Be(3);
        ChunkedTokenCounter.FindLastSafeCut("ab\ncd\n/ef").Should().Be(3);
        ChunkedTokenCounter.FindLastSafeCut("ab\ncd\n").Should().Be(3, "a cut needs a following character");
        ChunkedTokenCounter.FindLastSafeCut("abcdef").Should().Be(-1);
        ChunkedTokenCounter.FindLastSafeCut(Esc + "]title\nrest\nmore").Should().Be(-1, "the OSC is never terminated");
        ChunkedTokenCounter.FindLastSafeCut(Esc + "[0m\nrest\nmore").Should().Be(10);
    }

    [Fact]
    public async Task Append_AfterFinish_Throws_AndTotalBeforeFinish_Throws()
    {
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase);

        var total = () => counter.TotalAsync();
        await total.Should().ThrowAsync<InvalidOperationException>();

        counter.Finish(string.Empty);
        var append = () => counter.Append("x");
        append.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task TotalAsync_PropagatesAFailedEstimate()
    {
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, TinyChunk,
            static (_, _) => throw new InvalidOperationException("no vocabulary"));
        counter.Append("some text\nmore\n");
        counter.Finish(string.Empty);

        var total = () => counter.TotalAsync();

        await total.Should().ThrowAsync<InvalidOperationException>().WithMessage("no vocabulary");
    }

    private static async Task AssertExactAsync(string text, string trailing)
    {
        foreach (var model in Models)
        {
            var counter = new ChunkedTokenCounter(model, TinyChunk);
            for (var i = 0; i < text.Length; i += 37)
            {
                counter.Append(text.AsSpan(i, Math.Min(37, text.Length - i)));
            }

            counter.Finish(trailing);

            var expected = TokenEstimator.Estimate(AnsiStrip.Strip(text + trailing), model);
            (await counter.TotalAsync()).Should().Be(expected, "chunking must not change the count for {0}", model);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~ChunkedTokenCounterTests"`
Expected: build error, `ChunkedTokenCounter` missing.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// Counts the tokens of text that arrives while a child runs, in chunks on the thread pool, so that
/// only the last chunk is counted after the child has exited.
/// </summary>
/// <remarks>
/// <para>
/// The sum over chunks equals the count of the whole text because a chunk ends only at a <b>safe
/// cut</b>: just after a line feed, before a character that is neither whitespace nor <c>/</c>, and
/// not inside an escape sequence. From the tiktoken pre-tokenizer patterns in
/// Microsoft.ML.Tokenizers 2.0.0, the only pre-tokens that can contain a line feed are runs of
/// whitespace, and punctuation followed by line feeds (and, for <c>o200k_base</c>, slashes); every
/// one of them ends at the end of the line-feed run when such a character follows, so no
/// pre-token spans the cut, and byte-pair merges never cross pre-tokens. The escape check keeps
/// <see cref="AnsiStrip.Strip"/> chunk-local. <c>ChunkedTokenCounterTests</c> proves this over the
/// fixture corpus, the generated logs and adversarial strings for both encodings.
/// </para>
/// <para>
/// Fed with stdout only; <see cref="Finish"/> takes the stderr text, so the total is the count of
/// stdout followed by stderr, as <see cref="TokenEstimator.Estimate"/> over the concatenation was.
/// </para>
/// </remarks>
public sealed class ChunkedTokenCounter
{
    /// <summary>Chunks are at least this long: about 3 ms of counting each, and at most this much left for the end.</summary>
    public const int DefaultMinChunkChars = 64 * 1024;

    private readonly TokenizerModel _model;
    private readonly int _minChunkChars;
    private readonly Func<string, TokenizerModel, int> _estimate;
    private readonly StringBuilder _pending = new();
    private readonly List<Task<int>> _chunks = [];
    private readonly Lock _gate = new();
    private Task<int>? _final;

    /// <summary>Creates a counter for <paramref name="model"/>.</summary>
    /// <param name="model">The tokenizer to count with.</param>
    /// <param name="minChunkChars">The length a chunk must reach before it is cut and counted.</param>
    public ChunkedTokenCounter(TokenizerModel model, int minChunkChars = DefaultMinChunkChars)
        : this(model, minChunkChars, TokenEstimator.Estimate)
    {
    }

    /// <summary>For tests: a counter whose estimate can be observed or made to fail.</summary>
    internal ChunkedTokenCounter(TokenizerModel model, int minChunkChars, Func<string, TokenizerModel, int> estimate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minChunkChars, 1);
        ArgumentNullException.ThrowIfNull(estimate);

        _model = model;
        _minChunkChars = minChunkChars;
        _estimate = estimate;
    }

    /// <summary>Adds text; once enough has accumulated, counts everything up to the last safe cut on the thread pool.</summary>
    /// <param name="text">The next piece of stdout, in order.</param>
    /// <exception cref="InvalidOperationException"><see cref="Finish"/> was already called.</exception>
    public void Append(ReadOnlySpan<char> text)
    {
        lock (_gate)
        {
            if (_final is not null)
            {
                throw new InvalidOperationException("The counter has been finished.");
            }

            _pending.Append(text);
            if (_pending.Length < _minChunkChars)
            {
                return;
            }

            var buffered = _pending.ToString();
            var cut = FindLastSafeCut(buffered);
            if (cut <= 0)
            {
                return;
            }

            var chunk = buffered[..cut];
            _pending.Clear().Append(buffered, cut, buffered.Length - cut);
            _chunks.Add(Task.Run(() => _estimate(AnsiStrip.Strip(chunk), _model)));
        }
    }

    /// <summary>Counts what remains plus <paramref name="trailing"/> as the final chunk. Idempotent.</summary>
    /// <param name="trailing">The stderr text, appended after everything <see cref="Append"/> saw.</param>
    public void Finish(string trailing)
    {
        ArgumentNullException.ThrowIfNull(trailing);

        lock (_gate)
        {
            if (_final is not null)
            {
                return;
            }

            var last = _pending.ToString() + trailing;
            _pending.Clear();
            _final = Task.Run(() => _estimate(AnsiStrip.Strip(last), _model));
        }
    }

    /// <summary>The total over every chunk. Throws what any chunk's estimate threw.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Finish"/> was not called.</exception>
    public async Task<int> TotalAsync()
    {
        Task<int>[] all;
        lock (_gate)
        {
            var final = _final ?? throw new InvalidOperationException("Call Finish before TotalAsync.");
            all = [.. _chunks, final];
        }

        var counts = await Task.WhenAll(all).ConfigureAwait(false);
        return counts.Sum();
    }

    /// <summary>The last safe cut in <paramref name="text"/>, or -1.</summary>
    /// <param name="text">The buffered text.</param>
    internal static int FindLastSafeCut(string text)
    {
        for (var cut = text.Length - 1; cut >= 1; cut--)
        {
            if (text[cut - 1] != '\n' || char.IsWhiteSpace(text[cut]) || text[cut] == '/')
            {
                continue;
            }

            if (AnsiStrip.EndsInsideEscapeSequence(text.AsSpan(0, cut)))
            {
                continue;
            }

            return cut;
        }

        return -1;
    }
}
```

`Lock` is `System.Threading.Lock` (.NET 9+); the analyzers prefer it to `lock (object)`.

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~ChunkedTokenCounterTests"`
Expected: all pass (about 40 theory cases; the large generated tiers take a second or two each).
If any exactness case fails, the rule is wrong for that input class: print the failing text, find
which pre-token the cut split, and tighten `FindLastSafeCut` (never loosen it, never touch the
expected value). Then re-run everything.

- [ ] **Step 5: Commit**

Write `/tmp/commit-b9.txt`:

```
feat: count streamed output in chunks that cannot change the total

ChunkedTokenCounter cuts only after a line feed, before a character that
is neither whitespace nor a slash, and outside escape sequences, which
no tiktoken pre-token spans; the test proves the sum equals the
whole-text count over the corpus and adversarial strings.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests && git commit -F /tmp/commit-b9.txt`

### Task 10: `CountingTextWriter`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Helpers/CountingTextWriter.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/CountingTextWriterTests.cs`

**Interfaces:**
- Produces: `internal sealed class CountingTextWriter(TextWriter inner, ChunkedTokenCounter counter) : TextWriter`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class CountingTextWriterTests
{
    [Fact]
    public async Task WriteLineAsync_ForwardsToTheInnerWriterAndFeedsTheCounterWithItsNewLine()
    {
        var seen = new List<string>();
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, int.MaxValue, (text, _) =>
        {
            seen.Add(text);
            return 0;
        });
        var inner = new StringWriter { NewLine = "\n" };
        await using var sut = new CountingTextWriter(inner, counter);

        await sut.WriteLineAsync("first".AsMemory(), CancellationToken.None);
        await sut.FlushAsync(CancellationToken.None);
        await sut.WriteLineAsync("second".AsMemory(), CancellationToken.None);
        counter.Finish("err");
        await counter.TotalAsync();

        inner.ToString().Should().Be("first\nsecond\n");
        seen.Should().Equal("first\nsecond\nerr");
        sut.NewLine.Should().Be("\n");
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CountingTextWriterTests"`
Expected: build error, `CountingTextWriter` missing.

- [ ] **Step 3: Implement**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// The stdout sink of a tracked run: every line goes to the tee writer as before, and to the counter.
/// </summary>
/// <remarks>
/// Like <see cref="FanOutTextWriter"/>, only <see cref="WriteLineAsync(ReadOnlyMemory{char}, CancellationToken)"/>
/// and <see cref="FlushAsync(CancellationToken)"/> are routed, because the output pump calls nothing
/// else. Unlike it, nothing is swallowed: a counter that cannot append is a bug, not a broken tee.
/// The counter receives each line plus the inner writer's <see cref="NewLine"/>, which is what the
/// pump accumulates for the filter, so the counted text is the filtered text.
/// </remarks>
/// <param name="inner">The tee session's writer.</param>
/// <param name="counter">The run's counter.</param>
internal sealed class CountingTextWriter(TextWriter inner, ChunkedTokenCounter counter) : TextWriter
{
    /// <inheritdoc/>
    public override Encoding Encoding => inner.Encoding;

    /// <inheritdoc/>
    public override string NewLine
    {
        get => inner.NewLine;
#pragma warning disable CS8765 // matches TextWriter.NewLine's [AllowNull] contract; Roslyn still flags the override.
        [param: AllowNull]
        set => inner.NewLine = value;
#pragma warning restore CS8765
    }

    /// <inheritdoc/>
    public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
        counter.Append(buffer.Span);
        counter.Append(inner.NewLine);
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task FlushAsync() => inner.FlushAsync();

    /// <inheritdoc/>
    public override void Write(char value) => inner.Write(value);
}
```

- [ ] **Step 4: Run the test**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CountingTextWriterTests"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

Write `/tmp/commit-b10.txt`:

```
feat: a stdout sink that feeds the chunked counter

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests && git commit -F /tmp/commit-b10.txt`

### Task 11: wire the counter into the request, the pipeline and both use cases

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputRequest.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs` (`TrackIfEnabledAsync`)
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs`,
  `FilteredRunUseCaseTests.cs`, `PipeFilterUseCaseTests.cs`

**Interfaces:**
- Produces: `FilteredOutputRequest.InputTokenCounter` (`ChunkedTokenCounter?`, init-only, default null).
- Contract the use cases rely on: the text an `ICommandRunner` writes to the stdout sink, line by
  line with the sink's `NewLine`, equals `CommandResult.StdOut`. `ProcessCommandRunner.PumpAsync`
  guarantees it; test fakes must do the same when they return stdout.

- [ ] **Step 1: Write the failing tests**

In `FilteredOutputPipelineTests`, add (with `using DotnetTokenKiller.Application.Helpers;` already imported):

```csharp
    [Fact]
    public async Task ProcessAsync_RecordsTheCountersTotalWhenPresent()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase);
        counter.Append("raw output\nsecond line\n");
        counter.Finish("stderr text\n");
        var request = Request(raw: "raw output\nsecond line\nstderr text\n") with { InputTokenCounter = counter };

        await _sut.ProcessAsync(request, NullTeeSession.Instance);

        var expected = TokenEstimator.Estimate("raw output\nsecond line\nstderr text\n");
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FaultedCounter_RecordsNothingAndKeepsOutputAndExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, 64,
            static (_, _) => throw new InvalidOperationException("no vocabulary"));
        counter.Finish(string.Empty);
        var output = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, output, _configProvider);
        var request = Request(exitCode: 3) with { InputTokenCounter = counter };

        var exitCode = await sut.ProcessAsync(request, NullTeeSession.Instance);

        exitCode.Should().Be(3);
        output.ToString().Should().Be("filtered");
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }
```

In `FilteredRunUseCaseTests`, add a fake runner that pumps like the real one, and two tests:

```csharp
    private void RunnerWrites(string stdout, string stderr, int exitCode = 0)
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var sink = call.ArgAt<TextWriter>(2);
                foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    await sink.WriteLineAsync(line.AsMemory(), CancellationToken.None);
                    await sink.FlushAsync(CancellationToken.None);
                }

                return new CommandResult(stdout, stderr, exitCode);
            });
    }

    [Fact]
    public async Task RunAsync_TrackingOn_CountsStdoutAsItStreamsAndStderrAtTheEnd()
    {
        RunnerWrites("first line\nsecond line\n", "warning: stderr\n");
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        var expected = TokenEstimator.Estimate("first line\nsecond line\nwarning: stderr\n");
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens == expected), Arg.Any<CancellationToken>());
        await _runner.Received(1).RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<TextWriter>(w => w is CountingTextWriter), Arg.Is<TextWriter>(w => w is not CountingTextWriter),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingOff_PassesTheSessionWriterThrough()
    {
        var config = new DtkConfig(
            new TrackingConfig(false, 90, null, TokenizerModel.Cl100kBase), DtkConfig.Default.Display, DtkConfig.Default.Tee);
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        RunnerWrites("out\n", "");
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _runner.Received(1).RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<TextWriter>(w => ReferenceEquals(w, TextWriter.Null)), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>());
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }
```

Check `TrackingConfig`'s constructor order in `src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs`
before writing the `DtkConfig` above; `JsonConfigProvider.Merge` builds it as
`new TrackingConfig(enabled, retentionDays, dbPath, tokenizer)`.

In `PipeFilterUseCaseTests`, add:

```csharp
    [Fact]
    public async Task RunAsync_ReadsInputLargerThanOneBlockUnchanged()
    {
        var big = string.Concat(Enumerable.Repeat("a line of piped output\n", 20_000));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create(big).RunAsync(_filter, "build", 0, new OutputOptions());

        _filter.Received(1).Apply(big, 0);
    }

    [Fact]
    public async Task RunAsync_TrackingOn_RecordsTheExactCount()
    {
        const string stdin = "raw\nmore raw\n";
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create(stdin).RunAsync(_filter, "build", 0, new OutputOptions());

        var expected = TokenEstimator.Estimate(stdin);
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens == expected), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FilteredOutputPipelineTests|FullyQualifiedName~FilteredRunUseCaseTests|FullyQualifiedName~PipeFilterUseCaseTests"`
Expected: build error on `InputTokenCounter`.

- [ ] **Step 3: Implement**

`FilteredOutputRequest`: add `using DotnetTokenKiller.Application.Helpers;` and, inside the record body
(convert `);` at the end to `)` followed by a body):

```csharp
{
    /// <summary>
    /// The counter that was fed the raw stdout as it streamed and finished with the stderr text, or
    /// <see langword="null"/> to count <see cref="RawOutput"/> when tracking. When present, its total
    /// is the count of the stripped <see cref="RawOutput"/>, computed mostly while the child ran.
    /// </summary>
    public ChunkedTokenCounter? InputTokenCounter { get; init; }
}
```

`FilteredOutputPipeline.TrackIfEnabledAsync`: replace the `inputTokens` line with

```csharp
            var inputTokens = request.InputTokenCounter is { } counter
                ? await counter.TotalAsync().ConfigureAwait(false)
                : TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
```

`FilteredRunUseCase.RunAsync`: after the tee session is opened, replace the runner call with

```csharp
        // Tracking counts stdout while the child streams it; stderr, usually empty for dotnet, is
        // appended when the child exits. The counter relies on the runner writing to the stdout
        // sink exactly the text it returns as StdOut, which ProcessCommandRunner's pump guarantees.
        var counter = prepared.Config.Tracking.Enabled
            ? new ChunkedTokenCounter(prepared.Config.Tracking.Tokenizer)
            : null;
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var countingSink = counter is null ? null : new CountingTextWriter(session.Writer, counter);
#pragma warning restore CA2007
        var stdOutSink = countingSink ?? session.Writer;

        var result = await commandRunner
            .RunStreamedAsync(command, args, stdOutSink, session.Writer, cancellationToken)
            .ConfigureAwait(false);

        counter?.Finish(result.StdErr);
```

and add `{ InputTokenCounter = counter }` as an object initializer on the `FilteredOutputRequest`
construction. Add `using DotnetTokenKiller.Application.Helpers;`.

`PipeFilterUseCase.RunAsync`: replace `var raw = await input.ReadToEndAsync(cancellationToken)…` with

```csharp
        var counter = prepared.Config.Tracking.Enabled
            ? new ChunkedTokenCounter(prepared.Config.Tracking.Tokenizer)
            : null;
        var raw = await ReadAllAsync(input, counter, cancellationToken).ConfigureAwait(false);
        counter?.Finish(string.Empty);
```

add `{ InputTokenCounter = counter }` to the request, and add

```csharp
    private const int ReadBlockChars = 64 * 1024;

    /// <summary>Reads stdin to the end in blocks, feeding each to the counter as it arrives.</summary>
    private static async Task<string> ReadAllAsync(TextReader input, ChunkedTokenCounter? counter, CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBlockChars];
        var raw = new StringBuilder();
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            raw.Append(buffer, 0, read);
            counter?.Append(buffer.AsSpan(0, read));
        }

        return raw.ToString();
    }
```

with `using System.Text;` and `using DotnetTokenKiller.Application.Helpers;`.

- [ ] **Step 4: Run the Application tests, then everything local**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass, including `SavingsBaselineTests` (the baseline engine is untouched) and the
existing pipeline tests without a counter.

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test tests/DotnetTokenKiller.Domain.Tests && dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests`
Expected: no warnings; all pass.

Run: `git status --short benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/`
Expected: empty.

- [ ] **Step 5: Commit**

Write `/tmp/commit-b11.txt`:

```
perf: count the raw output while the child runs

FilteredRunUseCase hands the runner a stdout sink that feeds the chunked
counter, and PipeFilterUseCase feeds it block by block; the pipeline
records the counter's total. Only the last chunk and stderr are counted
after the child exits. Counts are unchanged, as the baseline shows.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore && git add src tests && git commit -F /tmp/commit-b11.txt`

### Task 12: measure the streaming count and open the pull request

**Files:**
- Modify: `CLAUDE.md` (Benchmarks figures; the `FilteredOutputPipeline` remark if any describes counting)

- [ ] **Step 1: Measure**

Repeat Task 3 steps 1 and 2, saving to `artifacts/cold-start-streaming-{tmpfs,disk}.txt`. Compare
with the journal files from Task 7: the 1 MB, 1000 ms-child overhead must drop by at least 30 ms;
no other scenario's median may rise by more than 3 ms; every scenario passes the row check.

- [ ] **Step 2: Record and commit**

Add `Streaming count, measured …: …` beside the journal figures in CLAUDE.md, and update the
Benchmarks paragraph that says "dtk's per-line tee flush and token counting grow with output size"
to say counting now overlaps the child except for the last chunk. Write `/tmp/commit-b12.txt`:

```
docs: record the journal and streaming-count measurements

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add CLAUDE.md && git commit -F /tmp/commit-b12.txt`

- [ ] **Step 3: Open the pull request**

Title: `perf: journal tracking writes and count output while the child runs`. Body: the three
measurement pairs (baseline, journal, streaming; tmpfs and disk), the exactness proof's case
count, the loader test's new control, and the unchanged baseline file. End with
`🤖 Generated with [Claude Code](https://claude.com/claude-code)`. CI gates the integration
suite, the macOS loader test, and parity on every RID.

---

# Part C: Windows x64 through a packaged shim

Branch from part A's branch (or `develop` once A is merged). Nothing here runs on this machine
except the csproj changes and their Linux check; the first CI run on the branch is the probe the
spec asks for, with a decision gate in Task 16.

### Task 13: choose the SQLite bundle per build

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj:8`
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (package references)
- Modify: `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj`,
  `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`

**Interfaces:**
- Produces: the MSBuild property `DtkUseWinSqlite3` (true only for `PublishAot=true` and `RuntimeIdentifier=win-x64`).

- [ ] **Step 1: Pin the packages**

Check the versions first:

```bash
dotnet list src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj package --include-transitive | rtk proxy grep -i sqlite
curl -sS https://api.nuget.org/v3-flatcontainer/sqlitepclraw.bundle_winsqlite3/index.json
```

Expected today: `Microsoft.Data.Sqlite.Core 10.0.12`, `SQLitePCLRaw.bundle_e_sqlite3 2.1.12`, and
`2.1.11` as the newest `bundle_winsqlite3`. Add to `Directory.Packages.props`, keeping the existing
`Microsoft.Data.Sqlite` line (the CLI integration tests still use the metapackage):

```xml
    <PackageVersion Include="Microsoft.Data.Sqlite.Core" Version="10.0.12"/>
    <PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12"/>
    <!-- Windows' own SQLite, for the Native AOT dtk.exe that is installed alone as the win-x64 package's shim. -->
    <PackageVersion Include="SQLitePCLRaw.bundle_winsqlite3" Version="2.1.11"/>
```

(use whatever versions the two commands printed if they differ).

- [ ] **Step 2: Move the bundle choice to the application**

`DotnetTokenKiller.Infrastructure.csproj`: replace `<PackageReference Include="Microsoft.Data.Sqlite"/>`
with `<PackageReference Include="Microsoft.Data.Sqlite.Core"/>` and the comment
`<!-- Core only: the application chooses the SQLitePCLRaw bundle (see the CLI csproj). -->`.

`DotnetTokenKiller.Cli.csproj`: add to the first `PropertyGroup`:

```xml
    <!--
      The win-x64 package's shim is the Native AOT dtk.exe, installed alone in ~/.dotnet/tools (see
      UseNativeBinaryAsPackagedShim below), so it cannot find e_sqlite3.dll beside it. That build uses
      winsqlite3.dll, which Windows 10 1903 and later ship in System32. Every other build, the
      framework-dependent dtk.dll in the same package included, keeps e_sqlite3.
    -->
    <DtkUseWinSqlite3>false</DtkUseWinSqlite3>
    <DtkUseWinSqlite3 Condition="'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' == 'win-x64'">true</DtkUseWinSqlite3>
```

and to the `ItemGroup` holding the Spectre references:

```xml
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Condition="!$(DtkUseWinSqlite3)"/>
    <PackageReference Include="SQLitePCLRaw.bundle_winsqlite3" Condition="$(DtkUseWinSqlite3)"/>
```

`DotnetTokenKiller.Infrastructure.Tests.csproj` and `DotnetTokenKiller.Benchmarks.csproj`: add
`<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3"/>` to their package `ItemGroup` (both
open databases through `SqliteTracker` and no longer inherit a bundle).

- [ ] **Step 3: Verify the evaluation for each build shape**

Run: `dotnet msbuild src/DotnetTokenKiller.Cli -getItem:PackageReference | rtk proxy grep -o '"Identity": "SQLitePCLRaw[^"]*"'`
Expected: `SQLitePCLRaw.bundle_e_sqlite3` and `SQLitePCLRaw.lib.e_sqlite3` only.

Run: `dotnet msbuild src/DotnetTokenKiller.Cli -getItem:PackageReference -p:RuntimeIdentifier=win-x64 -p:PublishAot=true | rtk proxy grep -o '"Identity": "SQLitePCLRaw[^"]*"'`
Expected: `SQLitePCLRaw.bundle_winsqlite3` and `SQLitePCLRaw.lib.e_sqlite3` (the static library reference is unconditional and harmless).

Run: `dotnet msbuild src/DotnetTokenKiller.Cli -getItem:PackageReference -p:RuntimeIdentifier=win-x64 -p:PublishAot=false | rtk proxy grep -o '"Identity": "SQLitePCLRaw[^"]*"'`
Expected: `bundle_e_sqlite3` again (the framework-dependent payload of the Windows package).

- [ ] **Step 4: Build, test, and check the Linux AOT binary still tracks**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests && dtk dotnet test tests/DotnetTokenKiller.Application.Tests && dtk dotnet test tests/DotnetTokenKiller.Domain.Tests`
Expected: no warnings (NU1xxx included); all pass.

Run (timeout 600000):

```bash
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot/publish
rm -rf /tmp/dtk-bundle-check && mkdir -p /tmp/dtk-bundle-check/logs
export DTK_CONFIG_PATH=/tmp/dtk-bundle-check/config.json DTK_DB_PATH=/tmp/dtk-bundle-check/tracking.db DTK_TEE_DIR=/tmp/dtk-bundle-check/logs
artifacts/aot/publish/dtk pipe build --exit-code 1 < tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt | head -1
artifacts/aot/publish/dtk gain --json | head -c 60; echo
unset DTK_CONFIG_PATH DTK_DB_PATH DTK_TEE_DIR
```

Expected: `dtk dotnet build: 3 errors, 1 warning (1.89s)` and `{"TotalCommands":1,…`.

- [ ] **Step 5: Commit**

Write `/tmp/commit-c13.txt`:

```
build: let the application choose its SQLite bundle

Infrastructure references Microsoft.Data.Sqlite.Core; the CLI references
bundle_e_sqlite3, or bundle_winsqlite3 for a Native AOT win-x64 publish,
whose exe will be installed with no e_sqlite3.dll beside it.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add Directory.Packages.props src tests benchmarks && git commit -F /tmp/commit-c13.txt`

### Task 14: the packaged-shim target and the `win-x64` RID

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (`ToolPackageRuntimeIdentifiers` and its comment; the `.pdb`/`.xml` exclusion; a new target)

**Interfaces:**
- Produces: the MSBuild property `DtkPackagedShim` (path of the exe to pack as the shim) and the
  target `UseNativeBinaryAsPackagedShim`.

- [ ] **Step 1: Add the RID and rewrite its comment**

Change `ToolPackageRuntimeIdentifiers` to
`linux-x64;linux-arm64;linux-musl-x64;linux-musl-arm64;osx-arm64;win-x64;any` and replace the two
sentences about Windows in the comment above it with:

```
      win-x64 is a framework-dependent package (runner `dotnet`) whose packaged shim is the Native AOT
      dtk.exe: the SDK writes a dtk.cmd batch file for a native tool, which Git Bash cannot run and which
      re-parses | & ^ % in arguments, but copies a packaged shim by name, unchecked, to ~/.dotnet/tools.
      Windows arm64 and x86 install `any`. See UseNativeBinaryAsPackagedShim below and
      docs/superpowers/specs/2026-09-13-serial-costs-and-windows-aot-design.md.
```

- [ ] **Step 2: Keep `.pdb` and `.xml` out of every RID package**

Change the condition on the `CopyOutputSymbolsToPublishDirectory` property group and on the
`KeepAotToolPackageRuntimeOnly` target from
`'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' != '' and '$(RuntimeIdentifier)' != 'any'` to
`'$(RuntimeIdentifier)' != '' and '$(RuntimeIdentifier)' != 'any'`, rename the target to
`KeepRidToolPackageRuntimeOnly`, and reword its comment: "A RID package, AOT or the framework-dependent
win-x64 one, carries the runtime files only …".

- [ ] **Step 3: Add the target**

After `KeepRidToolPackageRuntimeOnly`:

```xml
  <!--
    The win-x64 package is packed framework-dependent with -p:PublishAot=false -p:UseAppHost=false, so its
    runner is `dotnet` and the SDK generates an apphost shim for PackAsToolShimRuntimeIdentifiers. This
    target then replaces that shim with the Native AOT dtk.exe named by DtkPackagedShim: on install the SDK
    copies a packaged shim by file name, unchecked, to ~/.dotnet/tools/dtk.exe (dotnet/sdk,
    ShellShimRepository.TryGetPackagedShim), so the command every shell runs is the native binary, while
    `dotnet tool run`, manifests and dnx run dtk.dll on the runtime. eng/aot/pack-windows.sh checks the
    packed shim is the exe byte for byte; here only its existence is checked.
  -->
  <Target Name="UseNativeBinaryAsPackagedShim" AfterTargets="GenerateShimsAssets" Condition="'$(DtkPackagedShim)' != ''">
    <Error Condition="'$(PackAsToolShimRuntimeIdentifiers)' == ''"
           Text="DtkPackagedShim needs PackAsToolShimRuntimeIdentifiers, or the SDK generates no shim to replace."/>
    <Error Condition="!Exists('$(DtkPackagedShim)')" Text="DtkPackagedShim '$(DtkPackagedShim)' does not exist."/>
    <PropertyGroup>
      <_DtkShimExtension Condition="'$(RuntimeIdentifier)' == 'win-x64'">.exe</_DtkShimExtension>
      <_DtkShimTarget>$([MSBuild]::NormalizePath($(PackagedShimOutputRootDirectory), 'shims', $(_ToolPackShortTargetFrameworkName), $(RuntimeIdentifier), '$(ToolCommandName)$(_DtkShimExtension)'))</_DtkShimTarget>
    </PropertyGroup>
    <Error Condition="!Exists('$(_DtkShimTarget)')"
           Text="The SDK generated no shim at '$(_DtkShimTarget)'; its packaged-shim layout changed."/>
    <Copy SourceFiles="$(DtkPackagedShim)" DestinationFiles="$(_DtkShimTarget)" SkipUnchangedFiles="false"/>
    <Message Importance="high" Text="Packaged shim replaced with $(DtkPackagedShim)."/>
  </Target>
```

`PackagedShimOutputRootDirectory` defaults to `$(OutDir)` and `_ToolPackShortTargetFrameworkName` to
`net10.0` in `Microsoft.NET.PackTool.targets`; the SDK writes the generated shim to
`<OutDir>/shims/net10.0/<rid>/<command>[.exe]`.

- [ ] **Step 4: Check it on Linux with a stand-in exe**

Run (timeout 600000):

```bash
shim=$(readlink -f artifacts/aot/tools/linux-x64/dtk 2>/dev/null || echo artifacts/aot/publish/dtk)
rm -rf artifacts/shimcheck
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:PublishAot=false -p:UseAppHost=false -p:IncludeSymbols=false \
  -p:PackAsToolShimRuntimeIdentifiers=linux-x64 -p:DtkPackagedShim="$shim" -p:Version=0.0.0-shimcheck -o artifacts/shimcheck
mkdir -p artifacts/shimcheck/x && (cd artifacts/shimcheck/x && unzip -q ../DotnetTokenKiller.linux-x64.0.0.0-shimcheck.nupkg)
cmp "$shim" artifacts/shimcheck/x/tools/net10.0/linux-x64/shims/linux-x64/dtk && echo "shim is the binary"
cat artifacts/shimcheck/x/tools/net10.0/linux-x64/DotnetToolSettings.xml
find artifacts/shimcheck/x/tools -name '*.pdb' -o -name '*.xml' | rtk proxy grep -v DotnetToolSettings
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.0-shimcheck -o artifacts/shimcheck
unzip -p artifacts/shimcheck/DotnetTokenKiller.0.0.0-shimcheck.nupkg tools/net10.0/any/DotnetToolSettings.xml
```

Expected: `shim is the binary`; `Runner="dotnet"` with `EntryPoint="dtk.dll"`; no `.pdb` or `.xml`
listed; the pointer lists `win-x64` among its `RuntimeIdentifierPackage` entries.

Run: `dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:PublishAot=false -p:UseAppHost=false -p:IncludeSymbols=false -p:PackAsToolShimRuntimeIdentifiers=linux-x64 -p:DtkPackagedShim=/nonexistent -p:Version=0.0.0-shimcheck -o artifacts/shimcheck; echo "exit=$?"`
Expected: the `does not exist` error and a non-zero exit.

Then `rm -rf artifacts/shimcheck`.

- [ ] **Step 5: Confirm the existing packs are untouched**

Run: `dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version=0.0.0-shimcheck -o artifacts/shimcheck && unzip -l artifacts/shimcheck/DotnetTokenKiller.any.0.0.0-shimcheck.nupkg | rtk proxy grep -c '\.pdb'; rm -rf artifacts/shimcheck`
Expected: a positive count (the `any` package keeps its `.pdb` files for the `.snupkg`).

- [ ] **Step 6: Commit**

Write `/tmp/commit-c14.txt`:

```
build: pack win-x64 with the native binary as its packaged shim

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj && git commit -F /tmp/commit-c14.txt`

### Task 15: the Windows pack and test scripts

**Files:**
- Create: `eng/aot/unzip-file.sh`
- Create: `eng/aot/pack-windows.sh`
- Create: `eng/aot/test-windows.sh`

**Interfaces:**
- Produces: `sh eng/aot/pack-windows.sh <version> <feed-dir> <publish-dir> <publish-log>`;
  `sh eng/aot/test-windows.sh <version> <feed-dir> <tools-dir> [--compare-any]`, which appends
  `DTK_INSTALLED` (Windows path of the shim) and `DTK_STORE` to `$GITHUB_ENV` when that variable is set.

- [ ] **Step 1: The zip helper**

Git Bash's `tar` is GNU tar, which reads no zip, and `unzip` is not always present; Windows itself
ships bsdtar. `eng/aot/unzip-file.sh`:

```sh
#!/bin/sh
# Extracts one entry of a zip (a .nupkg) to a file with whatever the machine has: unzip, Windows' own
# bsdtar (Git Bash's tar is GNU tar, which reads no zip), or python.
#
# Usage: sh eng/aot/unzip-file.sh <zip> <entry> <destination>
set -eu

usage="usage: sh eng/aot/unzip-file.sh <zip> <entry> <destination>"
zip=${1:?$usage}
entry=${2:?$usage}
dest=${3:?$usage}

if command -v unzip >/dev/null 2>&1; then
    unzip -p "$zip" "$entry" > "$dest"
    exit 0
fi

if [ -n "${SYSTEMROOT:-}" ] && command -v cygpath >/dev/null 2>&1; then
    bsdtar="$(cygpath -u "$SYSTEMROOT")/System32/tar.exe"
    if [ -x "$bsdtar" ]; then
        tmp=$(mktemp -d)
        "$bsdtar" -xf "$(cygpath -w "$zip")" -C "$(cygpath -w "$tmp")" "$entry"
        mv "$tmp/$entry" "$dest"
        rm -rf "$tmp"
        exit 0
    fi
fi

python - "$zip" "$entry" "$dest" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive, open(sys.argv[3], 'wb') as out:
    out.write(archive.read(sys.argv[2]))
PY
```

- [ ] **Step 2: The pack script**

`eng/aot/pack-windows.sh`:

```sh
#!/bin/sh
# Packs the Windows x64 tool package: a framework-dependent RID package whose packaged shim is the Native AOT
# dtk.exe published just before (UseNativeBinaryAsPackagedShim in the CLI csproj). Runs on Windows under
# Git Bash, as CI does; needs the .NET 10 SDK and the MSVC toolset the AOT compile needs.
#
# Usage: sh eng/aot/pack-windows.sh <version> <feed-dir> <publish-dir> <publish-log>
set -eu

usage="usage: sh eng/aot/pack-windows.sh <version> <feed-dir> <publish-dir> <publish-log>"
version=${1:?$usage}
feed=${2:?$usage}
publish=${3:?$usage}
log=${4:?$usage}
repo=$(cd "$(dirname "$0")/../.." && pwd)

cd "$repo"
mkdir -p "$feed" "$publish" "$(dirname "$log")"
feed=$(cd "$feed" && pwd)
publish=$(cd "$publish" && pwd)
log="$(cd "$(dirname "$log")" && pwd)/$(basename "$log")"

# The parity tests compare against this JIT build, so it must carry the packs' version.
dtk dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"

# The native binary. AotWarningLogTests reads this log: its IL warnings must be exactly Spectre's three.
dotnet publish src/DotnetTokenKiller.Cli -c Release -r win-x64 -p:Version="$version" -o "$publish" \
    "-flp:LogFile=$log;Verbosity=minimal"
exe="$publish/dtk.exe"
if [ ! -f "$exe" ]; then
    echo "pack-windows.sh: expected $exe" >&2
    exit 1
fi
if [ -f "$publish/e_sqlite3.dll" ]; then
    echo "pack-windows.sh: the AOT publish carries e_sqlite3.dll, so DtkUseWinSqlite3 did not apply" >&2
    exit 1
fi

# Framework-dependent (runner dotnet, entry point dtk.dll), with the SDK's apphost shim replaced by the exe.
dotnet pack src/DotnetTokenKiller.Cli -c Release -r win-x64 -p:PublishAot=false -p:UseAppHost=false \
    -p:IncludeSymbols=false -p:PackAsToolShimRuntimeIdentifiers=win-x64 -p:DtkPackagedShim="$exe" \
    -p:Version="$version" -o "$feed"
nupkg="$feed/DotnetTokenKiller.win-x64.$version.nupkg"
if [ ! -f "$nupkg" ]; then
    echo "pack-windows.sh: expected $nupkg" >&2
    exit 1
fi

# What was packed is what the SDK copies to ~/.dotnet/tools/dtk.exe: check it is the exe, byte for byte.
extract=$(mktemp -d)
sh "$repo/eng/aot/unzip-file.sh" "$nupkg" tools/net10.0/win-x64/shims/win-x64/dtk.exe "$extract/dtk.exe"
if ! cmp -s "$exe" "$extract/dtk.exe"; then
    echo "pack-windows.sh: the packaged shim is not the published dtk.exe" >&2
    exit 1
fi
rm -rf "$extract"
echo "Packed $nupkg ($(wc -c < "$nupkg") bytes) with the native shim ($(wc -c < "$exe") bytes)."
```

- [ ] **Step 3: The test script**

`eng/aot/test-windows.sh`:

```sh
#!/bin/sh
# Installs the Windows x64 package from a local feed and checks what the shim route promises: the command
# on PATH is the native binary and not a .cmd, tracking works with no e_sqlite3.dll beside it, the managed
# entry point still runs, and uninstall then reinstall round-trips. Prints 21-run medians for the shim and,
# with --compare-any, for the `any` package installed beside it. Git Bash on Windows. The parity tests, the
# integration suite and the three-shell smokes run from the workflow, which reads DTK_INSTALLED and
# DTK_STORE from $GITHUB_ENV when this script sets them.
#
# Usage: sh eng/aot/test-windows.sh <version> <feed-dir> <tools-dir> [--compare-any]
set -eu

usage="usage: sh eng/aot/test-windows.sh <version> <feed-dir> <tools-dir> [--compare-any]"
version=${1:?$usage}
feed=${2:?$usage}
tools=${3:?$usage}
compare_any=${4:-}
repo=$(cd "$(dirname "$0")/../.." && pwd)
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
fixture="$repo/tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt"

cd "$repo"
mkdir -p "$tools" "$nuget"
feed=$(cd "$feed" && pwd)
tools=$(cd "$tools" && pwd)
version_lower=$(echo "$version" | tr '[:upper:]' '[:lower:]')

fail() {
    echo "test-windows.sh: $1" >&2
    exit 1
}

# The parity tests compare against this JIT build, so it must carry the packs' version.
dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$version" -o "$feed"

# A package already in the NuGet cache at this ID and version is installed from the cache, not the feed.
rm -rf "$nuget/dotnettokenkiller/$version_lower" "$nuget/dotnettokenkiller.win-x64/$version_lower" \
    "$nuget/dotnettokenkiller.any/$version_lower"

# A relative source resolves against the config file's directory; an absolute Git Bash path is
# POSIX-style, which NuGet cannot read. nuget.org serves everything except dtk's own packages.
write_config() {
    cat > "$1" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$2" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotnetTokenKiller*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
}
config="$(dirname "$feed")/$(basename "$feed").nuget.config"
write_config "$config" "$(basename "$feed")"

install() {
    dotnet tool install --tool-path "$tools" --configfile "$config" DotnetTokenKiller --version "$version"
}

store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.win-x64/$version_lower/tools/net10.0/win-x64"
shim="$tools/dtk.exe"
check_installed() {
    [ -d "$store" ] || { ls -R "$tools/.store" >&2; fail "expected $store: the install did not pick the win-x64 package"; }
    [ ! -f "$tools/dtk.cmd" ] || fail "the SDK wrote a dtk.cmd shim: the package's runner is not dotnet"
    [ -f "$shim" ] || fail "expected the shim $shim"
    cmp -s "$shim" "$reference" || fail "the installed dtk.exe is not the packaged native binary"
}

reference=$(mktemp -d)/dtk.exe
sh eng/aot/unzip-file.sh "$feed/DotnetTokenKiller.win-x64.$version.nupkg" \
    tools/net10.0/win-x64/shims/win-x64/dtk.exe "$reference"

install
check_installed
"$shim" --version

# Tracking with nothing beside the shim: the exe must find winsqlite3 in System32. dtk is a Windows
# program, so its environment gets Windows paths.
state="$tools/state"
mkdir -p "$state/logs"
DTK_CONFIG_PATH=$(cygpath -w "$state/config.json")
DTK_DB_PATH=$(cygpath -w "$state/tracking.db")
DTK_TEE_DIR=$(cygpath -w "$state/logs")
export DTK_CONFIG_PATH DTK_DB_PATH DTK_TEE_DIR
status=0
"$shim" pipe build --exit-code 1 < "$fixture" > "$state/pipe.txt" 2>&1 || status=$?
[ "$status" -eq 1 ] || { cat "$state/pipe.txt"; fail "pipe build exited $status, expected 1"; }
grep -q "dotnet build: 3 errors" "$state/pipe.txt" || { cat "$state/pipe.txt"; fail "pipe build did not print the filtered summary"; }
"$shim" gain --json > "$state/gain.json" || { cat "$state/gain.json"; fail "gain failed"; }
grep -q '"TotalCommands":1' "$state/gain.json" || { cat "$state/gain.json"; fail "gain did not report the tracked run: SQLite is not working"; }

# The managed entry point every `dotnet tool run`, manifest and dnx invocation uses.
dotnet "$store/dtk.dll" --version

# The SDK's RemoveShim and CreateShim paths.
dotnet tool uninstall --tool-path "$tools" DotnetTokenKiller
[ ! -f "$shim" ] || fail "uninstall left $shim behind"
install
check_installed

median_ms() {
    sort -n | awk '{ a[NR] = $1 } END { printf "%.1f", (NR % 2) ? a[(NR + 1) / 2] : (a[NR / 2] + a[NR / 2 + 1]) / 2 }'
}
time_runs() {
    i=0
    while [ "$i" -lt 21 ]; do
        start=$(date +%s%N)
        "$@" > /dev/null 2>&1 < "${STDIN_FILE:-/dev/null}" || true
        end=$(date +%s%N)
        echo $(( (end - start) / 1000000 ))
        i=$((i + 1))
    done | median_ms
}
echo "win-x64 shim: --version median $(time_runs "$shim" --version) ms"
echo "win-x64 shim: pipe build median $(STDIN_FILE=$fixture time_runs "$shim" pipe build --exit-code 1) ms"

if [ "$compare_any" = "--compare-any" ]; then
    any_feed="$feed-any"
    any_tools="$tools-any"
    mkdir -p "$any_feed" "$any_tools"
    dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version="$version" -o "$any_feed"
    dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:ToolPackageRuntimeIdentifiers=any \
        -p:Version="$version" -o "$any_feed"
    any_config="$(dirname "$any_feed")/$(basename "$any_feed").nuget.config"
    write_config "$any_config" "$(basename "$any_feed")"
    rm -rf "$nuget/dotnettokenkiller/$version_lower"
    dotnet tool install --tool-path "$any_tools" --configfile "$any_config" DotnetTokenKiller --version "$version"
    echo "any package: --version median $(time_runs "$any_tools/dtk.exe" --version) ms"
    echo "any package: pipe build median $(STDIN_FILE=$fixture time_runs "$any_tools/dtk.exe" pipe build --exit-code 1) ms"
fi

if [ -n "${GITHUB_ENV:-}" ]; then
    echo "DTK_INSTALLED=$(cygpath -w "$shim")" >> "$GITHUB_ENV"
    echo "DTK_STORE=$(cygpath -w "$store")" >> "$GITHUB_ENV"
fi
```

- [ ] **Step 4: Syntax-check the scripts**

Run: `for f in eng/aot/unzip-file.sh eng/aot/pack-windows.sh eng/aot/test-windows.sh; do sh -n "$f" && echo "$f ok"; done; command -v shellcheck >/dev/null && shellcheck eng/aot/*.sh || echo "shellcheck not installed"`
Expected: three `ok` lines; shellcheck clean when present (SC2016 on the python heredoc is a false positive; the heredoc is quoted).

Run: `sh eng/aot/unzip-file.sh "$(ls artifacts/aot/feed/DotnetTokenKiller.linux-x64.*.nupkg | head -1)" tools/net10.0/any/linux-x64/DotnetToolSettings.xml /tmp/settings.xml 2>/dev/null && cat /tmp/settings.xml || echo "no local feed to test with; the helper is exercised in CI"`
Expected: the settings XML, or the fallback message. (The AOT packages use the `tools/any/<rid>` layout; the win-x64 package uses `tools/net10.0/win-x64`.)

- [ ] **Step 5: Commit**

Write `/tmp/commit-c15.txt`:

```
ci: scripts that pack and test the Windows x64 package under Git Bash

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add eng/aot && git commit -F /tmp/commit-c15.txt`

### Task 16: the workflows, and the probe run

**Files:**
- Modify: `.github/workflows/aot-package.yml`
- Modify: `.github/workflows/ci.yml:60-105` (the `aot` matrix)
- Modify: `.github/workflows/publish.yml:52-90` (the `pack-rid` matrix)

- [ ] **Step 1: Windows branches in `aot-package.yml`**

In the `pack` job, after the macOS pack step add:

```yaml
      - name: Pack the AOT package
        if: runner.os == 'Windows'
        run: sh eng/aot/pack-windows.sh "$VERSION" artifacts/feed artifacts/aot/win-x64 "artifacts/pack-$RID.log"
```

and add `src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.pdb` to the
`Upload the native symbols` step's `path` list.

In the `test` job, give the existing `Install and test the package` step `if: runner.os != 'Windows'`
and append, before `Smoke-test on Rocky Linux 8`:

```yaml
      - name: Install and test the package (Windows)
        if: runner.os == 'Windows'
        run: sh eng/aot/test-windows.sh "$VERSION" artifacts/feed artifacts/tools --compare-any

      # DTK_AOT_REQUIRED=1 makes a missing binary or log fail these tests instead of skipping them.
      - name: Check parity and the publish log
        if: runner.os == 'Windows'
        env:
          DTK_AOT_REQUIRED: '1'
        run: >-
          DTK_AOT_BINARY="$DTK_INSTALLED"
          DTK_AOT_PACK_LOG="$(cygpath -w "$PWD/artifacts/pack-$RID.log")"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build
          --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot" --verbosity normal

      - name: Run the integration suite against the installed binary
        if: runner.os == 'Windows' && inputs.run-integration-suite
        run: >-
          DTK_TEST_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --verbosity normal
```

then the three shell-smoke steps copied verbatim from `fallback-package.yml` (Git Bash, pwsh,
cmd), with `DTK_TOOLS: ${{ github.workspace }}\artifacts\tools` in their `env` instead of the
runner temp path, and the Git Bash one also asserting `[ ! -f "$tools_posix/dtk.cmd" ]`.

- [ ] **Step 2: The matrix rows**

Add to both matrices, after `osx-arm64`:

```yaml
          - rid: win-x64
            pack-runner: windows-latest
            test-runner: windows-latest
            test-image: ''
            glibc: false
            run-integration-suite: true
```

(`ci.yml`'s matrix has no `run-integration-suite` key on some rows; add it as `true` here and pass it
through if the `with:` block does not already.)

- [ ] **Step 3: Lint what can be linted here**

Run: `command -v actionlint >/dev/null && actionlint || echo "actionlint not installed"; python3 -c "import yaml,sys; [yaml.safe_load(open(f)) for f in ['.github/workflows/aot-package.yml','.github/workflows/ci.yml','.github/workflows/publish.yml']]; print('yaml ok')"`
Expected: `yaml ok` (and actionlint clean when installed).

- [ ] **Step 4: Commit and run the probe**

Write `/tmp/commit-c16.txt`:

```
ci: pack, install and test the Windows x64 package on windows-latest

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add .github && git commit -F /tmp/commit-c16.txt && git push -u origin HEAD`

Run: `gh workflow run ci.yml --ref "$(git branch --show-current)" && sleep 20 && gh run list --workflow ci.yml --branch "$(git branch --show-current)" --limit 1`
Then: `gh run watch "$(gh run list --workflow ci.yml --branch "$(git branch --show-current)" --limit 1 --json databaseId --jq '.[0].databaseId')" --exit-status` (timeout 600000; re-run the watch if it times out; a full CI run takes 30 to 45 minutes).

- [ ] **Step 5: Decision gate**

Read the `win-x64` pack and test job logs (`gh run view <id> --log --job <job-id>`).

- **Pass:** every step green, the `test-windows.sh` output shows `win-x64 shim: --version median`
  under 30 ms, and the `any` medians beside it. Copy both pairs of medians and the package size
  from the pack log into the notes for Task 17.
- **The shim is not the exe** (`check_installed` fails, or `dtk.cmd` exists): the SDK on Windows
  does not take the packaged-shim path the Linux run took. Stop. Report the log lines to the owner
  with the spec's fallback (a ReadyToRun framework-dependent package: same pack command with
  `-p:PublishReadyToRun=true`, no `DtkPackagedShim`, no AOT publish) and wait for the decision.
- **Tracking fails** (`gain did not report the tracked run`): winsqlite3 did not load. Try the
  spec's alternative, a `DllImportResolver` for `e_sqlite3` that probes the store directory next
  to the shim (`Path.Combine(AppContext.BaseDirectory, ".store", "dotnettokenkiller", <version>, "dotnettokenkiller.win-x64", <version>, "tools", "net10.0", "win-x64", "e_sqlite3.dll")`),
  registered in `Program.cs` on Windows only, and re-run; if that fails too, stop and report.
- **Anything else red:** fix as for any CI failure and re-run.

### Task 17: docs and the pull request

**Files:**
- Modify: `README.md:99-111`, `src/DotnetTokenKiller.Cli/README.md:21-33`, `docfx/articles/getting-started.md:17-29`
- Modify: `CLAUDE.md` (Commands; Native AOT)

- [ ] **Step 1: The platform paragraph, in all three files**

Replace the sentences from "Windows and every other platform get the framework-dependent build"
through "…`^` and `%` in arguments." with:

> On Windows x64 it installs the same natively compiled `dtk.exe` as the command (Windows 10 1903 or
> later, whose own SQLite and ICU it uses); `dotnet tool run dtk`, tool manifests and `dnx` run the
> package's managed build on the .NET 10 runtime instead. Windows arm64 and every other platform get
> the framework-dependent build, which runs on the .NET 10 runtime.

`DocsBindingTests` checks command and option names, which this does not change; run
`dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests` to be sure nothing else binds to the
old text.

- [ ] **Step 2: CLAUDE.md**

Commands: add under the pack lines

```bash
# Pack and test the Windows x64 package (Git Bash on Windows; needs the MSVC toolset for the AOT compile).
sh eng/aot/pack-windows.sh 0.0.0-local artifacts/aot/feed artifacts/aot/win-x64 artifacts/aot/pack-win-x64.log
sh eng/aot/test-windows.sh 0.0.0-local artifacts/aot/feed artifacts/aot/tools/win-x64 --compare-any
```

Native AOT: replace the sentences "Windows has no AOT package because … (docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md)."
with a paragraph saying: win-x64 is a framework-dependent RID package (`Runner="dotnet"`) whose
packaged shim is the Native AOT `dtk.exe` (`DtkPackagedShim`, target `UseNativeBinaryAsPackagedShim`),
which the SDK copies by name to `~/.dotnet/tools/dtk.exe`, so every shell runs the native binary and
`dotnet tool run`/`dnx` run `dtk.dll`; that exe uses `SQLitePCLRaw.bundle_winsqlite3`
(`DtkUseWinSqlite3`) because nothing sits beside it; `eng/aot/pack-windows.sh` verifies the packed
shim byte for byte and `eng/aot/test-windows.sh` checks the install; the `.cmd` reason is in the
Linux and Windows AOT spec. Add the measured Windows medians (shim against `any`, from the probe's
`test-windows.sh` output, dated, "windows-latest, indicative") beside the other measurements.

- [ ] **Step 3: Commit and open the pull request**

Write `/tmp/commit-c17.txt`:

```
docs: Windows x64 runs the native dtk.exe as its command

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```

Run: `git add README.md src/DotnetTokenKiller.Cli/README.md docfx CLAUDE.md && git commit -F /tmp/commit-c17.txt && git push`

Title: `feat: ship the Native AOT dtk.exe to Windows x64 as the package's shim`. Body: the mechanism
in three sentences, the probe run's link, the medians (shim against `any`), the package size, and
two owner actions before a release: reserve `DotnetTokenKiller.win-x64` on nuget.org (the other
RID ids were the same case in #150) and confirm trusted publishing may create it. End with
`🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

---

## Self-review notes

- **Spec coverage.** Solution 1 → Tasks 1–3. Solution 2.1 → Tasks 4–7 (journal, folds, tracker,
  loader test, docs, measurement). Solution 2.2 → Tasks 8–12 (escape check, counter and proof,
  sink, wiring, measurement). Solution 3 → Tasks 13–17 (bundle, csproj, scripts, workflows and
  probe gate, docs). Measurement protocol steps 1–3 → Tasks 3, 7, 12; step 4 → Task 15's timings
  read in Task 16. Every item in the spec's Testing section has a test above, in the task that
  introduces the code it tests.
- **Deviations from the spec text**, applied to the spec in the same commit as this plan: the
  counter API is `Finish(trailing)` plus `TotalAsync()` and the request carries the counter, not a
  task (awaiting a task read from a property trips VSTHRD003); the MSBuild target checks the shim
  file's existence and the pack script checks its bytes (MSBuild property functions cannot read a
  file's size); the folds commit takes every claim id involved; `bundle_e_sqlite3` is 2.1.12.
- **Type consistency**: `PendingRecordJournal(string root, TimeSpan? lockWait = null)`,
  `FoldAsync(committed, commit, wait, cancellationToken)`, `FoldOutcome(Folded, Records, Corrupt)`,
  `SqliteTracker(connectionString, defaultRetentionDays, foldThreshold)`,
  `ChunkedTokenCounter(model, minChunkChars[, estimate])` with `Append`/`Finish`/`TotalAsync`/
  `FindLastSafeCut`, `CountingTextWriter(inner, counter)`, `FilteredOutputRequest.InputTokenCounter`,
  `ChildOutput(Text, ExitCode, SummaryLine)`, `HermeticState.Enter(root)`, `DtkUseWinSqlite3`,
  `DtkPackagedShim`, `UseNativeBinaryAsPackagedShim` are used with these exact names throughout.

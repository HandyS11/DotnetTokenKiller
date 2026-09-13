# Tracking Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start the tiktoken vocabulary load and the SQLite setup while the child process runs
instead of after it, turn `TieredPGO` off, and make `cold-start` show the result.

**Architecture:** `ITracker` gains `WarmUpAsync`; `SqliteTracker` creates its connection lazily and
recovers from a failed setup. A new `TrackingWarmUp` starts both setups on the thread pool and
exposes a never-throwing `WhenReadyAsync`. `FilteredOutputPipeline.BeginAsync` loads config and
starts the warm-up; `FilteredRunUseCase` and `PipeFilterUseCase` call it before the child or the
stdin read, and `PassthroughRunUseCase` starts its own. Tracking awaits the warm-up and then runs
exactly as today, so every failure still lands in the existing catch.

**Tech Stack:** net10.0, C# 14, Microsoft.Data.Sqlite 10.0.12, Microsoft.ML.Tokenizers 2.0.0, xunit,
FluentAssertions, NSubstitute.

**Spec:** [docs/superpowers/specs/2026-09-13-tracking-path-design.md](../specs/2026-09-13-tracking-path-design.md)

## Global Constraints

- **Token counts stay exact.** `SavingsBaselineTests` must pass and
  `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` must not change.
  Never run `update-baseline` in this plan.
- **Tracking failures stay non-fatal:** no exception from tokenizer loading, SQLite setup or
  recording may change dtk's output or exit code.
- **Background work starts only when tracking is enabled** (and, for passthrough, only when a
  tracker exists).
- **No SQLite statement, schema, retention or journal-mode changes.** The spec rules them out with
  measurements.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all` (Roslynator, SonarAnalyzer,
  NetAnalyzers, VS Threading). Fix every warning; never suppress one with a pragma or
  `.editorconfig` change. Known traps: S107 (more than 7 parameters), VSTHRD003 (awaiting a `Task`
  held in a field or property), CA2016 (forward a `CancellationToken`).
- **Code style:** file-scoped namespaces; `var`; private fields `_camelCase`; async methods end in
  `Async`; `ConfigureAwait(false)` on every await in `src/` and `benchmarks/` (tests do not use it);
  XML doc comments on public members in `src/` (`GenerateDocumentationFile` is on); one type per
  file in `src/`.
- **Formatting:** LF, no trailing whitespace, no BOM, 4-space indent for `.cs`, 2-space for XML.
  Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before every commit.
- **Use `dtk dotnet …`** for build, test and format. `dotnet run` is not rewritten and is used as-is.
- **The Bash hook rewrites `dotnet build|test|restore|clean|format|list package` anywhere in a
  command,** including quoted strings and heredocs. Write every commit message to a file with the
  Write tool and commit with `git commit -F <file>`. Never put a commit message inline.
- **Commit messages end with** a blank line and
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **CLI integration tests cannot pass on this machine.** Build the whole solution (so their
  `StubTracker` classes compile) but run tests per project:
  `tests/DotnetTokenKiller.Domain.Tests`, `tests/DotnetTokenKiller.Application.Tests`,
  `tests/DotnetTokenKiller.Infrastructure.Tests`. CI gates the rest.
- **`cold-start` does not rebuild the CLI** and takes about 3 minutes. Always build first and set
  the Bash timeout to `600000`.

## File Structure

| File | Responsibility |
|---|---|
| `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs` (modify) | Alternate pair order; print runtime environment and runtime config |
| `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (modify) | `TieredPGO` off |
| `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` (modify) | `WarmUpAsync` contract |
| `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (modify) | Lazy connection, `WarmUpAsync`, reset on failed setup, dispose waits for setup |
| `benchmarks/DotnetTokenKiller.Benchmarks/Support/NullTracker.cs` (modify) | Implement `WarmUpAsync` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/{Gain,Pipe,Reset}CommandTests.cs` (modify) | `StubTracker` implements `WarmUpAsync` |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` (modify) | `WarmUp(TokenizerModel)` |
| `src/DotnetTokenKiller.Application/UseCases/TrackingWarmUp.cs` (new) | Start setup on the thread pool; never-throwing `WhenReadyAsync` |
| `src/DotnetTokenKiller.Application/UseCases/PreparedRun.cs` (new) | Config + warm-up produced when a filtered run starts |
| `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs` (modify) | `BeginAsync`, prepared `ProcessAsync` overload, await warm-up before tracking |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` (modify) | Begin before the tee and the child |
| `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs` (modify) | Begin before reading stdin |
| `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs` (modify) | Start warm-up before the child; await before tracking |
| `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs` (modify) | Correct the remark about when SQLite opens |
| `CLAUDE.md` (modify) | Harness description, tmpfs caveat, new figures |
| `docs/superpowers/specs/2026-09-13-tracking-path-design.md` (modify) | Status line |

## Measurement Commands

Tasks 1, 2 and 7 use these. Results go under `artifacts/tracking-path/`, which is git-ignored
(`artifacts/` is in `.gitignore`). Never commit them.

**Cold start** (replace `<name>` with the file name the task gives):

```bash
mkdir -p artifacts/tracking-path
dtk dotnet build src/DotnetTokenKiller.Cli -c Release
set -o pipefail; dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start 2>&1 | tee artifacts/tracking-path/<name>.txt
```

Check the output's `Built:` line is a timestamp from the build you just ran. The run must print all
three sections; a thrown `InvalidOperationException` means a sample was rejected and the figures are
not usable.

**Tracking off.** Create `artifacts/tracking-path/tracking-off.py` with the Write tool if it does
not exist:

```python
#!/usr/bin/env python3
"""Times `dtk pipe build` with tracking disabled, in a hermetic state directory.

Usage: python3 artifacts/tracking-path/tracking-off.py <dtk binary>   (run from the repo root)
"""
import os
import pathlib
import statistics
import subprocess
import sys
import tempfile
import time

WARMUP = 5
MEASURED = 21

binary = sys.argv[1]
fixture = pathlib.Path("tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt").read_bytes()

with tempfile.TemporaryDirectory(prefix="dtk-tracking-off-") as root:
    env = dict(os.environ,
               DTK_CONFIG_PATH=f"{root}/config.json",
               DTK_DB_PATH=f"{root}/tracking.db",
               DTK_TEE_DIR=f"{root}/logs")
    env.pop("NO_COLOR", None)
    subprocess.run([binary, "config", "set", "tracking.enabled", "false"],
                   env=env, check=True, stdout=subprocess.DEVNULL)

    samples = []
    for i in range(WARMUP + MEASURED):
        start = time.perf_counter()
        run = subprocess.run([binary, "pipe", "build", "--exit-code", "1"],
                             input=fixture, env=env, capture_output=True)
        elapsed = (time.perf_counter() - start) * 1000
        if run.returncode != 1 or not run.stdout.strip():
            sys.exit(f"unexpected run: exit {run.returncode}\n{run.stdout.decode()}\n{run.stderr.decode()}")
        if i >= WARMUP:
            samples.append(elapsed)

    if os.path.exists(f"{root}/tracking.db"):
        sys.exit("tracking.db was created, so tracking was not off; these are not tracking-off timings")

    samples.sort()
    print(f"tracking off, dtk pipe build: median {statistics.median(samples):.1f} ms, "
          f"min {samples[0]:.1f} ms, max {samples[-1]:.1f} ms ({MEASURED} runs)")
```

Run it after building the CLI in Release (replace `<name>`):

```bash
python3 artifacts/tracking-path/tracking-off.py src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk | tee artifacts/tracking-path/<name>.txt
```

---

### Task 1: `cold-start` alternates pair order and prints runtime settings; record the baseline

Two harness fixes from sub-project 1's deferred list, then the baseline every later number is
compared against. The benchmarks project has no test project; the verification is running it.

**Files:**
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs`

**Interfaces:**
- Consumes: `TimedProcess.RunAsync(string fileName, IReadOnlyList<string> arguments, string? standardInput = null, string? prependToPath = null)`,
  `FakeDotnet` (`ScriptPath`, `DirectoryPath`), `EnsureFakeChildRan`, `EnsureDtkFiltered`,
  `TimingReport.WriteAsync(List<double>)`, all existing.
- Produces: header lines `Runtime environment: …` and `Runtime config: …`. Later tasks read them.

- [ ] **Step 1: Add the runtime-settings header**

Add `using System.Collections;` and `using System.Text.Json;` to the top of `ColdStartCommand.cs`
(keep usings sorted). In `RunAsync`, directly after the `Runs per scenario` line, add:

```csharp
        await Console.Out.WriteLineAsync($"Runtime environment: {DescribeRuntimeEnvironment()}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Runtime config: {DescribeRuntimeConfig(binary)}")
            .ConfigureAwait(false);
```

Add these two methods after `WriteHeadingAsync`:

```csharp
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
```

- [ ] **Step 2: Alternate the order within each pair**

In `MeasureWrappedAsync`, replace the whole `for` loop with:

```csharp
        for (var i = 0; i < WarmupRuns + MeasuredRuns; i++)
        {
            // Alternate which process runs first. dtk always running straight after an idle child
            // inflated the sleeping-child scenario by 2-4 ms; alternating cancels that order effect
            // while keeping each pair adjacent.
            TimedRun child;
            TimedRun wrapped;
            if (i % 2 == 0)
            {
                child = await RunFakeChildAsync(fake, input).ConfigureAwait(false);
                wrapped = await RunWrappedDtkAsync(binary, arguments, fake, summaryLine).ConfigureAwait(false);
            }
            else
            {
                wrapped = await RunWrappedDtkAsync(binary, arguments, fake, summaryLine).ConfigureAwait(false);
                child = await RunFakeChildAsync(fake, input).ConfigureAwait(false);
            }

            if (i >= WarmupRuns)
            {
                overhead.Add(wrapped.Milliseconds - child.Milliseconds);
                wall.Add(wrapped.Milliseconds);
            }
        }
```

Add these two methods directly after `MeasureWrappedAsync`:

```csharp
    /// <summary>Runs the fake child alone and validates it.</summary>
    /// <param name="fake">The fake child.</param>
    /// <param name="input">The fixture text it must print.</param>
    private static async Task<TimedRun> RunFakeChildAsync(FakeDotnet fake, string input)
    {
        var child = await TimedProcess.RunAsync(fake.ScriptPath, []).ConfigureAwait(false);
        EnsureFakeChildRan(child, fake, input);
        return child;
    }

    /// <summary>Runs dtk wrapping the fake child and validates it.</summary>
    /// <param name="binary">The dtk binary.</param>
    /// <param name="arguments">dtk's arguments.</param>
    /// <param name="fake">The fake child, whose directory is prepended to dtk's PATH.</param>
    /// <param name="summaryLine">The line dtk must print.</param>
    private static async Task<TimedRun> RunWrappedDtkAsync(
        string binary, string[] arguments, FakeDotnet fake, string summaryLine)
    {
        var wrapped = await TimedProcess
            .RunAsync(binary, arguments, prependToPath: fake.DirectoryPath)
            .ConfigureAwait(false);
        EnsureDtkFiltered(wrapped, binary, summaryLine);
        return wrapped;
    }
```

Mark `RunFakeChildAsync` with `[UnsupportedOSPlatform("windows")]` only if the build reports CA1416
for it (it calls nothing platform-specific itself; `FakeDotnet.Create` is where the Unix file mode
is set).

- [ ] **Step 3: Update the two doc comments that describe the order**

In the class `<remarks>`, replace `Each iteration times the fake child alone and then dtk wrapping
it, and records` with `Each iteration times the fake child alone and dtk wrapping it, alternating
which runs first, and records`.

In `MeasureWrappedAsync`'s `<summary>`, replace `sampled in pairs of child alone then dtk wrapping
it.` with `sampled in pairs of child alone and dtk wrapping it, in alternating order.`

- [ ] **Step 4: Build**

Run: `dtk dotnet build benchmarks/DotnetTokenKiller.Benchmarks -c Release`
Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 5: Format and commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task1-commit.txt`:

```text
perf: alternate cold-start pair order and print runtime settings

dtk always ran straight after the idle fake child, which inflated the
sleeping-child scenario by a few milliseconds. The header now also prints
DOTNET_*/COMPlus_* variables and the binary's runtimeconfig properties,
both of which move the figures.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs
git commit -F /tmp/dtk-task1-commit.txt
```

- [ ] **Step 6: Record the cold-start baseline**

Nothing under `src/` has changed yet. Run **Cold start** from Measurement Commands with
`<name>` = `1-baseline`.
Expected: three sections; header shows `Runtime config:` with
`System.Reflection.Metadata.MetadataUpdater.IsSupported=false` and no `TieredPGO` entry. Figures
near the 2026-09-12 ones (pipe ~287 ms; overheads ~286 and ~288 ms). The 1000 ms overhead may now
be a few ms lower because of the alternating order.

- [ ] **Step 7: Record the tracking-off baseline**

Create the script and run **Tracking off** from Measurement Commands with
`<name>` = `tracking-off-before`.
Expected: one line with a median around 216 ms, and no "tracking.db was created" failure.

Report the four medians (pipe, instant overhead, 1000 ms overhead, tracking off) in your task report.

---

### Task 2: Turn `TieredPGO` off for the CLI

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`

**Interfaces:**
- Consumes: Task 1's `Runtime config:` header line.
- Produces: `dtk.runtimeconfig.json` containing `"System.Runtime.TieredPGO": false`.

- [ ] **Step 1: Add the property**

In the first `<PropertyGroup>` of `DotnetTokenKiller.Cli.csproj`, after `<ToolCommandName>dtk</ToolCommandName>`, add:

```xml
    <!--
      A dtk process lives for a single command, too briefly for dynamic PGO's instrumented tier to
      pay for itself: DOTNET_TieredPGO=0 measured 9 ms faster per run on a JIT build (2026-09-12).
      Set here rather than in Directory.Build.props so test and benchmark processes keep the default.
    -->
    <TieredPGO>false</TieredPGO>
```

- [ ] **Step 2: Build and check the runtimeconfig**

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release`
Then: `grep -n TieredPGO src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk.runtimeconfig.json`
Expected: one line, `"System.Runtime.TieredPGO": false`.

- [ ] **Step 3: Commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task2-commit.txt`:

```text
perf: turn TieredPGO off for the dtk CLI

A one-command process never runs long enough for dynamic PGO's
instrumented tier to pay back its cost.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj
git commit -F /tmp/dtk-task2-commit.txt
```

- [ ] **Step 4: Measure**

Run **Cold start** with `<name>` = `2-tieredpgo`.
Expected: header `Runtime config:` now includes `System.Runtime.TieredPGO=false`. Report each
scenario's median next to its `1-baseline` median. A few milliseconds lower is expected; a rise of
more than 5 ms in any scenario means stop and report rather than continue.

---

### Task 3: `ITracker.WarmUpAsync` and a lazily connected, recoverable `SqliteTracker`

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Support/NullTracker.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs` (`StubTracker`, line ~522)
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/PipeCommandTests.cs` (`StubTracker`, line ~46)
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/ResetCommandTests.cs` (`StubTracker`, line ~126)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs`

**Interfaces:**
- Produces: `Task ITracker.WarmUpAsync(CancellationToken cancellationToken = default)`, implemented
  by `SqliteTracker`. Tasks 4–6 call it.
- Keeps: the private field name `_connection` (an existing test reads it by reflection after a
  `RecordAsync`).

- [ ] **Step 1: Write the failing tests**

Append to `SqliteTrackerTests` (before the private helpers at the end of the class is fine; it
already has `using Microsoft.Data.Sqlite;`):

```csharp
    [Fact]
    public async Task WarmUpAsync_ThenRecordAsync_PersistsExactlyOneRowAsync()
    {
        await _sut.WarmUpAsync();
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle();
    }

    [Fact]
    public async Task Dispose_TrackerNeverUsed_DoesNotThrowEvenTwiceAsync()
    {
        // A tracker built for a run with tracking disabled never creates its connection.
        var syncTracker = new SqliteTracker("Data Source=:memory:");
        var asyncTracker = new SqliteTracker("Data Source=:memory:");

        var disposeSync = () =>
        {
            syncTracker.Dispose();
            syncTracker.Dispose();
        };
        var disposeAsync = async () =>
        {
            await asyncTracker.DisposeAsync();
            await asyncTracker.DisposeAsync();
        };

        disposeSync.Should().NotThrow();
        await disposeAsync.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordAsync_AfterWarmUpFailedBeforeOpening_RecoversAsync()
    {
        // A file sits where the database's directory must go, so creating the directory throws
        // before any connection exists. Once it is gone, recording must work.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        var blocker = Path.Combine(root, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory");
        try
        {
            await using var tracker =
                new SqliteTracker($"Data Source={Path.Combine(blocker, "tracking.db")};Pooling=False");

            var warmUp = () => tracker.WarmUpAsync();
            await warmUp.Should().ThrowAsync<IOException>();

            File.Delete(blocker);
            await tracker.RecordAsync(MakeRecord());

            (await tracker.GetHistoryAsync(1, null)).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAsync_AfterWarmUpFailedOnAnOpenedConnection_RecoversAsync()
    {
        // A file that is not a SQLite database fails during setup, after the connection was
        // created. The retry must start from a fresh connection, not reopen the failed one.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        var dbPath = Path.Combine(root, "tracking.db");
        await File.WriteAllTextAsync(dbPath, new string('x', 4096));
        try
        {
            await using var tracker = new SqliteTracker($"Data Source={dbPath};Pooling=False");

            var warmUp = () => tracker.WarmUpAsync();
            await warmUp.Should().ThrowAsync<SqliteException>();

            File.Delete(dbPath);
            await tracker.RecordAsync(MakeRecord());

            (await tracker.GetHistoryAsync(1, null)).Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_WhileWarmUpIsRunning_DoesNotThrowAsync()
    {
        // A warm-up can outlive its run (the child failed to launch and the tracker is disposed on
        // the way out). Disposal runs on the user's path and must be clean; the warm-up itself may
        // lose the race and see ObjectDisposedException, which TrackingWarmUp discards. Looped
        // because the race is timing-dependent: this guards the fix but cannot prove its absence.
        var root = Directory.CreateTempSubdirectory("dtk-warmup-").FullName;
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var dbPath = Path.Combine(root, $"race-{attempt}.db");
                var tracker = new SqliteTracker($"Data Source={dbPath};Pooling=False");
                var warmUp = Task.Run(() => tracker.WarmUpAsync());

                var dispose = async () => await tracker.DisposeAsync();

                await dispose.Should().NotThrowAsync();
                try
                {
                    await warmUp;
                }
                catch (ObjectDisposedException)
                {
                    // Expected when disposal won the race; see the comment above.
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests`
Expected: build fails with CS1061, `'SqliteTracker' does not contain a definition for 'WarmUpAsync'`.

- [ ] **Step 3: Add `WarmUpAsync` to `ITracker`**

In `ITracker.cs`, after `RecordAsync`, add:

```csharp
    /// <summary>
    /// Performs the one-time setup the first call would otherwise do, so it can run early (in the
    /// background, while a child process runs) and leave only the record itself for later.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it in the three stubs and `NullTracker`**

In `NullTracker.cs`, after `RecordAsync`:

```csharp
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
```

In each `StubTracker` (Gain, Pipe and Reset command tests), after its `RecordAsync` method, in the
block-bodied style those classes use:

```csharp
        public Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
```

- [ ] **Step 5: Implement it in `SqliteTracker`**

Replace the field block and both dispose methods (from `private readonly SqliteConnection _connection = new(connectionString);`
through the end of `Dispose()`) with:

```csharp
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private SqliteConnection? _connection;
    private bool _disposed;
    private bool _initialized;

    /// <summary>Asynchronously releases managed resources.</summary>
    /// <remarks>
    /// Takes the semaphore first, so an initialization still running on a background thread (a
    /// warm-up that outlived its run) finishes before its connection is disposed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }

    /// <summary>Releases managed resources.</summary>
    /// <remarks>Waits for an in-flight initialization, as <see cref="DisposeAsync"/> does.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }
```

After `RecordAsync`, add:

```csharp
    /// <inheritdoc/>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
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
    }
```

Replace `EnsureInitializedAsync` with:

```csharp
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        try
        {
            EnsureDataDirectory(connectionString);

            // Created here rather than in a field initializer: SqliteConnection's static initializer
            // loads the native SQLite library, about 23 ms, which a tracker that is built but never
            // used (tracking disabled) should not pay.
            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(ct).ConfigureAwait(false);

            // A one-shot CLI runs as a single process with a single tracker instance, so purge
            // expired rows once here at startup. A single delete per process is cheap and replaces
            // the old per-insert counter cleanup that could never fire during a one-command run.
            await CleanupCoreAsync(defaultRetentionDays, ct).ConfigureAwait(false);
            _initialized = true;
        }
        catch
        {
            // A failed attempt leaves nothing behind, so the next call (a RecordAsync after a
            // background warm-up failed) starts again from a fresh connection instead of reopening
            // a half-initialized one.
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            throw;
        }
    }
```

Add this helper next to `EnsureDataDirectory`:

```csharp
    /// <summary>Creates a command on the connection <see cref="EnsureInitializedAsync"/> opened.</summary>
    /// <exception cref="InvalidOperationException">Called before initialization succeeded.</exception>
    private SqliteCommand CreateCommand() =>
        (_connection ?? throw new InvalidOperationException("The tracker was used before it was initialized."))
        .CreateCommand();
```

Then replace every remaining `_connection.CreateCommand()` in the file with `CreateCommand()`
(in `RecordAsync`, `ResetAsync`, `InitializeSchemaAsync`, `EnsureColumnAsync` twice,
`ExecuteWithFilterAsync` and `CleanupCoreAsync`). Keep the surrounding
`#pragma warning disable CA2007` lines as they are.

- [ ] **Step 6: Build the whole solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: 0 warnings, 0 errors. This compiles the integration-test stubs and `NullTracker`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests`
Expected: all pass, including the five new tests and the existing reflection-based
`InsertRawOutcomeRowAsync` tests.

- [ ] **Step 8: Commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task3-commit.txt`:

```text
perf: let the tracker warm up early and create its connection lazily

ITracker gains WarmUpAsync so setup can run while a child process does.
SqliteTracker no longer loads the native SQLite library when it is merely
constructed, recovers from a failed setup with a fresh connection, and
waits for an in-flight setup before disposing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add src/DotnetTokenKiller.Domain/Tracking/ITracker.cs src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs benchmarks/DotnetTokenKiller.Benchmarks/Support/NullTracker.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/PipeCommandTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/ResetCommandTests.cs tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs
git commit -F /tmp/dtk-task3-commit.txt
```

---

### Task 4: `TokenEstimator.WarmUp` and `TrackingWarmUp`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs`
- Create: `src/DotnetTokenKiller.Application/UseCases/TrackingWarmUp.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/TokenEstimatorTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/TrackingWarmUpTests.cs` (new)

**Interfaces:**
- Consumes: `ITracker.WarmUpAsync(CancellationToken)` (Task 3).
- Produces:
  - `public static void TokenEstimator.WarmUp(TokenizerModel model = TokenizerModel.Cl100kBase)`
  - `public sealed class TrackingWarmUp` in `DotnetTokenKiller.Application.UseCases` with
    `public static TrackingWarmUp None { get; }`,
    `public static TrackingWarmUp Start(ITracker tracker, TokenizerModel? tokenizer, CancellationToken cancellationToken)`,
    `public Task WhenReadyAsync()`. Tasks 5 and 6 use all three.

- [ ] **Step 1: Write the failing tests**

Append to `TokenEstimatorTests`:

```csharp
    [Theory]
    [InlineData(TokenizerModel.Cl100kBase)]
    [InlineData(TokenizerModel.O200kBase)]
    public async Task WarmUp_RacingEstimate_ReturnsTheSameCount(TokenizerModel model)
    {
        // Materialized before awaiting so the warm-ups and estimates really run concurrently.
        var warmUps = Enumerable.Range(0, 4).Select(_ => Task.Run(() => TokenEstimator.WarmUp(model))).ToArray();
        var estimates = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => TokenEstimator.Estimate("Hello world", model)))
            .ToArray();

        await Task.WhenAll(warmUps);
        var counts = await Task.WhenAll(estimates);

        counts.Should().AllBeEquivalentTo(2);
    }
```

Create `tests/DotnetTokenKiller.Application.Tests/UseCases/TrackingWarmUpTests.cs`:

```csharp
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class TrackingWarmUpTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    [Fact]
    public async Task Start_WarmsTheTrackerExactlyOnce()
    {
        await TrackingWarmUp.Start(_tracker, tokenizer: null, CancellationToken.None).WhenReadyAsync();

        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenReadyAsync_TrackerWarmUpFaults_CompletesWithoutThrowing()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));

        var act = () => TrackingWarmUp.Start(_tracker, TokenizerModel.Cl100kBase, CancellationToken.None)
            .WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenReadyAsync_TrackerWarmUpThrowsSynchronously_CompletesWithoutThrowing()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("db locked"));

        var act = () => TrackingWarmUp.Start(_tracker, tokenizer: null, CancellationToken.None).WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenReadyAsync_TokenAlreadyCancelled_CompletesWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled(call.Arg<CancellationToken>()));

        var act = () => TrackingWarmUp.Start(_tracker, TokenizerModel.Cl100kBase, cts.Token).WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void None_IsAlreadyComplete()
    {
        TrackingWarmUp.None.WhenReadyAsync().IsCompletedSuccessfully.Should().BeTrue();
    }
}
```

(The Application test project has implicit `Xunit` usings; `TokenEstimatorTests` compiles without
`using Xunit;`. If the new file does not, add `using Xunit;`.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: build fails: `TokenEstimator` has no `WarmUp`, and `TrackingWarmUp` does not exist.

- [ ] **Step 3: Implement `TokenEstimator.WarmUp`**

Replace the body of `TokenEstimator` from `public static int Estimate` through the end of
`Estimate` with:

```csharp
    public static int Estimate(string text, TokenizerModel model = TokenizerModel.Cl100kBase)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return GetTokenizer(model).CountTokens(text);
    }

    /// <summary>Loads the tokenizer for <paramref name="model"/> if this process has not loaded it yet.</summary>
    /// <param name="model">The tokenizer model to load.</param>
    /// <remarks>
    /// The vocabulary load costs about 110 ms for <c>cl100k_base</c>, once per process. Calling this
    /// on a background thread while a child process runs moves that cost out of the serial path. An
    /// <see cref="Estimate"/> call made while the load is still running waits for that same load
    /// rather than starting another, so counts are identical either way.
    /// </remarks>
    public static void WarmUp(TokenizerModel model = TokenizerModel.Cl100kBase)
    {
        _ = GetTokenizer(model);
    }

    /// <summary>Returns the process-wide tokenizer for <paramref name="model"/>, loading it once.</summary>
    /// <param name="model">The tokenizer model.</param>
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey,TValue})"/> may build
    /// more than one <see cref="Lazy{T}"/> under a race, but every caller receives the one stored, and
    /// a <see cref="Lazy{T}"/> in its default thread-safe mode runs its factory once.
    /// </remarks>
    private static TiktokenTokenizer GetTokenizer(TokenizerModel model)
    {
        return Tokenizers.GetOrAdd(model,
                static m => new Lazy<TiktokenTokenizer>(() => TiktokenTokenizer.CreateForEncoding(ToEncodingName(m))))
            .Value;
    }
```

- [ ] **Step 4: Create `TrackingWarmUp`**

Create `src/DotnetTokenKiller.Application/UseCases/TrackingWarmUp.cs`:

```csharp
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Tracking setup started when a run starts, so it runs while the child process (or the stdin read)
/// does instead of after it.
/// </summary>
/// <remarks>
/// This only moves work earlier. Every exception is discarded here, and the same failure then
/// resurfaces where the run is recorded: <see cref="TokenEstimator.Estimate"/> rethrows a failed
/// load and <see cref="ITracker.RecordAsync"/> retries a failed setup, both inside the catch that
/// already keeps tracking failures away from the user.
/// </remarks>
public sealed class TrackingWarmUp
{
    private readonly Task _ready;

    private TrackingWarmUp(Task ready)
    {
        _ready = ready;
    }

    /// <summary>A warm-up with nothing to do, for runs that are not tracked.</summary>
    public static TrackingWarmUp None { get; } = new(Task.CompletedTask);

    /// <summary>Starts the tracker's setup and, when requested, the tokenizer load, each on the thread pool.</summary>
    /// <param name="tracker">The tracker to warm up.</param>
    /// <param name="tokenizer">
    /// The tokenizer to load, or <see langword="null"/> when the run will not count tokens.
    /// </param>
    /// <param name="cancellationToken">Passed to the tracker's setup.</param>
    /// <returns>A handle whose <see cref="WhenReadyAsync"/> completes once both have finished.</returns>
    public static TrackingWarmUp Start(ITracker tracker, TokenizerModel? tokenizer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        // Task.Run rather than a direct call: SQLite setup begins with a synchronous native library
        // load that would otherwise run on the caller's thread before the first await. The tasks
        // themselves get CancellationToken.None so a cancelled run yields a completed task, never a
        // cancelled one that WhenReadyAsync would rethrow.
        var trackerSetup = Task.Run(async () =>
        {
            try
            {
                await tracker.WarmUpAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Intentional: RecordAsync retries a failed setup inside the tracking catch
            }
        }, CancellationToken.None);

        var tokenizerLoad = tokenizer is { } model
            ? Task.Run(() =>
            {
                try
                {
                    TokenEstimator.WarmUp(model);
                }
                catch
                {
                    // Intentional: Estimate rethrows a failed load inside the tracking catch
                }
            }, CancellationToken.None)
            : Task.CompletedTask;

        return new TrackingWarmUp(Task.WhenAll(trackerSetup, tokenizerLoad));
    }

    /// <summary>Completes when the started setup has finished, whether or not it succeeded. Never throws.</summary>
    /// <returns>A task that always completes successfully.</returns>
    public Task WhenReadyAsync()
    {
        return _ready;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass, including `SavingsBaselineTests` and the new `TokenEstimatorTests` and
`TrackingWarmUpTests`.

- [ ] **Step 6: Commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task4-commit.txt`:

```text
perf: add a tracking warm-up that runs on the thread pool

TokenEstimator.WarmUp loads a vocabulary ahead of the first count, and
TrackingWarmUp starts it and the tracker's setup in the background behind
a WhenReadyAsync that never throws.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs src/DotnetTokenKiller.Application/UseCases/TrackingWarmUp.cs tests/DotnetTokenKiller.Application.Tests/Helpers/TokenEstimatorTests.cs tests/DotnetTokenKiller.Application.Tests/UseCases/TrackingWarmUpTests.cs
git commit -F /tmp/dtk-task4-commit.txt
```

---

### Task 5: Filtered runs and `pipe` start tracking setup when the run starts

**Files:**
- Create: `src/DotnetTokenKiller.Application/UseCases/PreparedRun.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/PipeFilterUseCaseTests.cs`

**Interfaces:**
- Consumes: `TrackingWarmUp.Start`, `TrackingWarmUp.None`, `TrackingWarmUp.WhenReadyAsync()`
  (Task 4); `ITracker.WarmUpAsync` (Task 3).
- Produces:
  - `public sealed record PreparedRun(DtkConfig Config, TrackingWarmUp WarmUp)`
  - `public Task<PreparedRun> FilteredOutputPipeline.BeginAsync(CancellationToken cancellationToken = default)`
  - `public Task<int> FilteredOutputPipeline.ProcessAsync(FilteredOutputRequest request, ITeeSession session, PreparedRun prepared, CancellationToken cancellationToken = default)`
  - The existing `ProcessAsync(FilteredOutputRequest, ITeeSession, CancellationToken = default)`
    keeps its signature and behaviour.

- [ ] **Step 1: Write the failing pipeline tests**

Append to `FilteredOutputPipelineTests` (add `using DotnetTokenKiller.Application.Helpers;`):

```csharp
    private static DtkConfig TrackingOff =>
        DtkConfig.Default with { Tracking = DtkConfig.Default.Tracking with { Enabled = false } };

    [Fact]
    public async Task BeginAsync_TrackingDisabled_DoesNotWarmTheTracker()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(TrackingOff);

        var prepared = await _sut.BeginAsync();
        await prepared.WarmUp.WhenReadyAsync();

        await _tracker.DidNotReceive().WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginAsync_TrackingEnabled_WarmsTheTrackerOnce()
    {
        var prepared = await _sut.BeginAsync();
        await prepared.WarmUp.WhenReadyAsync();

        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithPreparedRun_DoesNotLoadConfigAgain()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var prepared = await _sut.BeginAsync();
        _configProvider.ClearReceivedCalls();

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);

        await _configProvider.DidNotReceive().LoadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithPreparedRun_TracksUnderThePreparedConfig()
    {
        // A run is tracked under the settings it started with, whatever the file says by the end.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(TrackingOff);
        var prepared = new PreparedRun(DtkConfig.Default, TrackingWarmUp.None);

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);

        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TrackerWarmUpThrows_KeepsOutputAndExitCodeAndStillRecords()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var prepared = await sut.BeginAsync();

        var exitCode = await sut.ProcessAsync(Request(exitCode: 3), NullTeeSession.Instance, prepared);

        exitCode.Should().Be(3);
        writer.ToString().Should().Be("filtered");
        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WaitsForTheTrackerWarmUpBeforeRecording()
    {
        // Loaded up front so a pipeline that skipped the wait would reach RecordAsync well inside
        // the delay below, instead of being held up by the vocabulary load and passing by accident.
        TokenEstimator.WarmUp();
        var warmUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).Returns(warmUp.Task);
        var warmUpFinishedWhenRecorded = false;
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                warmUpFinishedWhenRecorded = warmUp.Task.IsCompleted;
                return Task.CompletedTask;
            });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var prepared = await _sut.BeginAsync();

        var processing = _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);
        await Task.Delay(200);
        warmUp.SetResult();
        await processing;

        warmUpFinishedWhenRecorded.Should().BeTrue();
    }
```

- [ ] **Step 2: Write the failing use-case tests**

Append to `FilteredRunUseCaseTests`:

```csharp
    [Fact]
    public async Task RunAsync_StartsTrackingSetupBeforeTheCommandFinishes()
    {
        // The overlap with the child is the whole saving. The fake child waits (bounded) for the
        // warm-up to start, so a use case that starts it only after the child exits fails here.
        var warmUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                warmUpStarted.TrySetResult();
                return Task.CompletedTask;
            });
        var startedWhileChildRan = false;
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var first = await Task.WhenAny(warmUpStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                startedWhileChildRan = first == warmUpStarted.Task;
                return new CommandResult("out", "", 0);
            });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        startedWhileChildRan.Should().BeTrue();
    }
```

Append to `PipeFilterUseCaseTests`:

```csharp
    [Fact]
    public async Task RunAsync_StartsTrackingSetupBeforeStdinIsRead()
    {
        // Setup overlaps a slow producer on the other end of the pipe. The reader waits (bounded)
        // for the warm-up to start, so a use case that starts it only after reading fails here.
        var warmUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                warmUpStarted.TrySetResult();
                return Task.CompletedTask;
            });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var reader = new WarmUpAwareReader(warmUpStarted.Task, "piped input");
        var pipeline = new FilteredOutputPipeline(_tracker, TextWriter.Null, _configProvider);

        await new PipeFilterUseCase(pipeline, _teeService, reader)
            .RunAsync(_filter, "build", 0, new OutputOptions());

        reader.WarmUpStartedBeforeRead.Should().BeTrue();
    }

    /// <summary>A stdin stand-in that records whether tracking setup had started when it was read.</summary>
    private sealed class WarmUpAwareReader(Task warmUpStarted, string content) : TextReader
    {
        public bool WarmUpStartedBeforeRead { get; private set; }

        public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            var first = await Task.WhenAny(warmUpStarted, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            WarmUpStartedBeforeRead = first == warmUpStarted;
            return content;
        }
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: build fails: `FilteredOutputPipeline` has no `BeginAsync`, and `PreparedRun` does not
exist.

- [ ] **Step 4: Create `PreparedRun`**

Create `src/DotnetTokenKiller.Application/UseCases/PreparedRun.cs`:

```csharp
using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>What <see cref="FilteredOutputPipeline.BeginAsync"/> set up when a run started.</summary>
/// <param name="Config">The configuration loaded for this run and used for the rest of it.</param>
/// <param name="WarmUp">The tracking setup started for this run, or <see cref="TrackingWarmUp.None"/>.</param>
public sealed record PreparedRun(DtkConfig Config, TrackingWarmUp WarmUp);
```

- [ ] **Step 5: Add `BeginAsync` and the prepared overload to the pipeline**

In `FilteredOutputPipeline.cs`, replace the existing `ProcessAsync` method's signature block and its
first lines (from the `/// <summary>Filters the request's output` comment through
`var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);`) with:

```csharp
    /// <summary>
    /// Loads the configuration for a run and, when tracking is enabled, starts tracking setup in the
    /// background. Call it as the run starts, before its output exists, and pass the result to
    /// <see cref="ProcessAsync(FilteredOutputRequest, ITeeSession, PreparedRun, CancellationToken)"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded configuration and the started warm-up.</returns>
    public async Task<PreparedRun> BeginAsync(CancellationToken cancellationToken = default)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var warmUp = config.Tracking.Enabled
            ? TrackingWarmUp.Start(tracker, config.Tracking.Tokenizer, cancellationToken)
            : TrackingWarmUp.None;

        return new PreparedRun(config, warmUp);
    }

    /// <summary>Filters the request's output, writes it, and records the run, preparing it first.</summary>
    /// <remarks>
    /// For callers with no earlier point to start setup at: the configuration load and tracking setup
    /// run here, serially, exactly as they did before <see cref="BeginAsync"/> existed.
    /// </remarks>
    /// <param name="request">The output and metadata to process.</param>
    /// <param name="session">
    /// The log opened for this run, finalized here. Callers with nothing to log pass
    /// <see cref="NullTeeSession.Instance"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request's exit code, unchanged.</returns>
    public async Task<int> ProcessAsync(
        FilteredOutputRequest request,
        ITeeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);

        var prepared = await BeginAsync(cancellationToken).ConfigureAwait(false);
        return await ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Filters the request's output, writes it, and records the run.</summary>
    /// <param name="request">The output and metadata to process.</param>
    /// <param name="session">
    /// The log opened for this run, finalized here. Callers with nothing to log pass
    /// <see cref="NullTeeSession.Instance"/>.
    /// </param>
    /// <param name="prepared">What <see cref="BeginAsync"/> returned when this run started.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request's exit code, unchanged.</returns>
    public async Task<int> ProcessAsync(
        FilteredOutputRequest request,
        ITeeSession session,
        PreparedRun prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prepared);

        var config = prepared.Config;
```

The rest of the method body stays as it is, except the tracking call at its end, which becomes:

```csharp
        await TrackIfEnabledAsync(prepared, request, stripped, filtered,
                Stopwatch.GetElapsedTime(request.StartTimestamp), outcome, cancellationToken)
            .ConfigureAwait(false);
```

Replace `TrackIfEnabledAsync` with (seven parameters: S107 allows no more):

```csharp
    private async Task TrackIfEnabledAsync(
        PreparedRun prepared,
        FilteredOutputRequest request,
        string stripped,
        string filtered,
        TimeSpan elapsed,
        RunOutcome outcome,
        CancellationToken cancellationToken)
    {
        var config = prepared.Config;
        if (!config.Tracking.Enabled)
        {
            return;
        }

        try
        {
            // Setup started in BeginAsync has usually finished while the child ran. This never
            // throws; a failed setup resurfaces in Estimate or RecordAsync below, inside this catch.
            await prepared.WarmUp.WhenReadyAsync().ConfigureAwait(false);

            var inputTokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
            var outputTokens = TokenEstimator.Estimate(filtered, config.Tracking.Tokenizer);
            var savedTokens = inputTokens - outputTokens;
            var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                request.CommandSlug,
                Environment.CurrentDirectory,
                new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
                elapsed)
            {
                Success = request.ExitCode == 0,
                Outcome = outcome,
                Source = request.Source
            };

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
```

- [ ] **Step 6: Begin at the start of `FilteredRunUseCase` and `PipeFilterUseCase`**

In `FilteredRunUseCase.RunAsync`, directly after `var startTimestamp = Stopwatch.GetTimestamp();`, add:

```csharp

        // Before the tee session and the child, so the tokenizer load and the SQLite setup run while
        // the child does rather than after it exits. After the timestamp, so the recorded elapsed
        // time still includes the configuration load, as it did when the pipeline loaded it.
        var prepared = await pipeline.BeginAsync(cancellationToken).ConfigureAwait(false);
```

and change its last statement to:

```csharp
        return await pipeline.ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
```

In `PipeFilterUseCase.RunAsync`, directly after `var startTimestamp = Stopwatch.GetTimestamp();`, add:

```csharp

        // Before reading stdin, so setup overlaps a slow producer on the other end of the pipe.
        var prepared = await pipeline.BeginAsync(cancellationToken).ConfigureAwait(false);
```

and change its last statement to:

```csharp
        return await pipeline.ProcessAsync(request, session, prepared, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass, including every pre-existing `FilteredOutputPipelineTests`,
`FilteredRunUseCaseTests`, `PipeFilterUseCaseTests` and `SavingsBaselineTests` test.

- [ ] **Step 8: Build the whole solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: 0 warnings, 0 errors (the pipeline benchmark and integration tests still use the
three-argument `ProcessAsync`).

- [ ] **Step 9: Commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task5-commit.txt`:

```text
perf: start tracking setup when a filtered run or pipe starts

FilteredOutputPipeline.BeginAsync loads config once and starts the
tokenizer load and SQLite setup in the background. dotnet runs begin
before the child launches and pipe before stdin is read; tracking waits
for the warm-up and otherwise behaves exactly as before.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add src/DotnetTokenKiller.Application/UseCases/PreparedRun.cs src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs src/DotnetTokenKiller.Application/UseCases/PipeFilterUseCase.cs tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs tests/DotnetTokenKiller.Application.Tests/UseCases/PipeFilterUseCaseTests.cs
git commit -F /tmp/dtk-task5-commit.txt
```

---

### Task 6: Passthrough runs start tracking setup before the child

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
- Modify: `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs` (remark only)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`

**Interfaces:**
- Consumes: `TrackingWarmUp.Start`, `TrackingWarmUp.None`, `TrackingWarmUp.WhenReadyAsync()`
  (Task 4); `ITracker.WarmUpAsync` (Task 3).
- Produces: no new public API.

- [ ] **Step 1: Write the failing tests**

Append to `PassthroughRunUseCaseTests`:

```csharp
    [Fact]
    public async Task RunAsync_MeasurableSubcommand_StartsTrackerSetupBeforeTheCommandFinishes()
    {
        var warmUpStarted = TrackWarmUpStart();
        var startedWhileChildRan = false;
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var first = await Task.WhenAny(warmUpStarted, Task.Delay(TimeSpan.FromSeconds(5)));
                startedWhileChildRan = first == warmUpStarted;
                return new CommandResult("publish output", "", 0);
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        startedWhileChildRan.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_NonMeasurableSubcommand_StartsTrackerSetupBeforeTheCommandFinishes()
    {
        var warmUpStarted = TrackWarmUpStart();
        var startedWhileChildRan = false;
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var first = await Task.WhenAny(warmUpStarted, Task.Delay(TimeSpan.FromSeconds(5)));
                startedWhileChildRan = first == warmUpStarted;
                return 0;
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        startedWhileChildRan.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_TrackingOffButTeeOn_DoesNotWarmTheTracker()
    {
        var config = DtkConfig.Default with { Tracking = new TrackingConfig(Enabled: false) };
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("publish output", "", 0));

        await _sut.RunAsync(config, "dotnet", PublishArgs);

        await _tracker.DidNotReceive().WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackerWarmUpThrows_StillRecordsAndKeepsTheExitCode()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("publish output", "", 4));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(4);
        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Makes the tracker's warm-up signal the returned task when it starts.</summary>
    private Task TrackWarmUpStart()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            });
        return started.Task;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PassthroughRunUseCaseTests"`
Expected: the two `StartsTrackerSetupBeforeTheCommandFinishes` tests fail (each after its 5-second
bound) with `Expected startedWhileChildRan to be True, but found False`. The other two new tests
may already pass: today nothing calls `WarmUpAsync` at all.

- [ ] **Step 3: Implement**

In `PassthroughRunUseCase.RunAsync`, replace the block from
`if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))` through the closing brace of that `if` with:

```csharp
        if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))
        {
            // Interactive runs record zero tokens and never estimate, so only the tracker needs
            // setting up while the child runs.
            var interactiveWarmUp = StartWarmUp(config.Tracking, tokenizer: null, cancellationToken);

            // Interactive: stdio stays attached, so there is no output to capture or tee.
            var passthroughExit = await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            await interactiveWarmUp.WhenReadyAsync().ConfigureAwait(false);
            await TrackAsync(commandName, null, config.Tracking, stopwatch.Elapsed, passthroughExit,
                    RunOutcome.PassthroughUnmeasured, cancellationToken)
                .ConfigureAwait(false);
            return passthroughExit;
        }

        // Before the log and the child, so setup runs while the child does rather than after it.
        var warmUp = StartWarmUp(config.Tracking, config.Tracking.Tokenizer, cancellationToken);
```

Directly before the measured branch's `await TrackAsync(commandName, result.StdOut + result.StdErr, …`
call (after the comment block that precedes it), add:

```csharp
        // Never throws; a failed setup resurfaces inside TrackAsync's catch.
        await warmUp.WhenReadyAsync().ConfigureAwait(false);
```

Add this method before `FinalizeTeeAsync`:

```csharp
    /// <summary>Starts tracking setup for this run, or nothing when the run will not be recorded.</summary>
    /// <param name="trackingConfig">The tracking configuration, checked for whether recording happens.</param>
    /// <param name="tokenizer">The tokenizer to load, or <see langword="null"/> when no tokens are counted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private TrackingWarmUp StartWarmUp(
        TrackingConfig trackingConfig, TokenizerModel? tokenizer, CancellationToken cancellationToken)
    {
        return tracker is null || !trackingConfig.Enabled
            ? TrackingWarmUp.None
            : TrackingWarmUp.Start(tracker, tokenizer, cancellationToken);
    }
```

In `TrackAsync`, replace the comment
`// A null tracker and a disabled config both mean the same thing — nothing to record — and`
`// this is the single place either one is checked.` with:

```csharp
        // A null tracker and a disabled config both mean the same thing — nothing to record. This
        // and StartWarmUp are the only two places either one is checked.
```

- [ ] **Step 4: Correct the `PassthroughEntryPoint` remark**

In `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs`, replace
`before the child starts, so a killed dtk still leaves a log behind. Tracking, when enabled, opens`
`/// one SQLite connection after the child has already exited.` with:

```csharp
/// before the child starts, so a killed dtk still leaves a log behind. Tracking, when enabled, sets up
/// its SQLite connection (and, for a measured run, loads the tokenizer) on the thread pool while the
/// child runs, and records once the child has exited.
```

Keep the `///` prefix and indentation consistent with the surrounding remark lines.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass.

- [ ] **Step 6: Build the whole solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Write `/tmp/dtk-task6-commit.txt`:

```text
perf: start tracking setup before a passthrough child runs

Measured runs warm the tracker and the tokenizer; interactive runs, which
record zero tokens, warm only the tracker. Nothing starts when there is
no tracker or tracking is off.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs
git commit -F /tmp/dtk-task6-commit.txt
```

---

### Task 7: Verify, measure, and document

**Files:**
- Modify: `CLAUDE.md` (the `cold-start` bullet under `## Benchmarks`)
- Modify: `docs/superpowers/specs/2026-09-13-tracking-path-design.md` (status line)

**Interfaces:**
- Consumes: `artifacts/tracking-path/1-baseline.txt`, `2-tieredpgo.txt`,
  `tracking-off-before.txt` from Tasks 1–2.

- [ ] **Step 1: Verify the branch**

Run each and check the expected result:

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test tests/DotnetTokenKiller.Domain.Tests
dtk dotnet test tests/DotnetTokenKiller.Application.Tests
dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git diff --stat develop -- benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json
```

Expected: build 0 warnings; all three test projects pass (Application includes
`SavingsBaselineTests`); format reports no changes; the `git diff` prints nothing.

- [ ] **Step 2: Measure the final cold start**

Run **Cold start** with `<name>` = `3-final`.
Expected: three sections, no rejected samples; `Runtime config:` includes
`System.Runtime.TieredPGO=false`.

- [ ] **Step 3: Measure tracking off**

Run **Tracking off** with `<name>` = `tracking-off-after`.

- [ ] **Step 4: Check the success criteria**

From the medians in `1-baseline.txt` (B) and `3-final.txt` (F):

1. F's 1000 ms-child overhead is well below F's instant-child overhead (expected: tens of
   milliseconds or more, from the ~150 ms of setup that can now overlap).
2. F's instant-child overhead ≤ B's instant-child overhead + 5 ms.
3. F's pipe median ≤ B's pipe median + 5 ms.

If criterion 1, 2 or 3 fails, **stop**: do not tune code or retry until the numbers pass. Report
all B, `2-tieredpgo` and F medians to the controller. Otherwise continue, and also note the
tracking-off before/after medians (the spec expects roughly 20 ms lower; this is informational,
not a gate).

- [ ] **Step 5: Update CLAUDE.md**

In `CLAUDE.md`, replace the whole `- \`cold-start\` times the built …` bullet (it ends with
`wall-clock 1291.0 ms).`) with the text below, filling each `{…}` from the files named and
`{DATE}` with today's date as `YYYY-MM-DD`. Round to one decimal as the harness prints.

```markdown
- `cold-start` times the built `dtk` binary end to end in three scenarios, 55 dtk spawns each (the
  wrapped scenarios also spawn the fake child alone 55 times): `dtk pipe build` with a fixture on
  stdin, and `dtk dotnet build` wrapping a generated shell-script `dotnet` on the child's `PATH`
  that either exits at once or sleeps 1000 ms first. The wrapped scenarios pair a run of the fake
  child alone with a run of dtk around it on every iteration, alternating which goes first, and
  report the paired difference as dtk's overhead. The instant child is the worst case (nothing for
  background setup to overlap with); the sleeping child is the best case (an idle CPU). For output
  this fixture's size (2.6 KB), a real build's cost lies between them; dtk's per-line tee flush and
  token counting grow with output size, so this bracket says nothing about a much larger build log.
  Every sample must print the build filter's summary line, because the real SDK found on `PATH` by
  mistake also exits 1. The header prints any `DOTNET_*`/`COMPlus_*` variables and the binary's
  `runtimeconfig.json` properties, since both move the figures. The tracking database lives under
  the temp root, tmpfs on the measuring machine, so the ~5.6 ms fsync a tracking `INSERT` costs on
  ext4 is not in these figures. Needs a POSIX shell. Measured {DATE}, before → after starting
  tracking setup in the background and turning `TieredPGO` off: pipe {B pipe median} →
  {F pipe median} ms; wrapped overhead {B instant overhead median} → {F instant overhead median} ms
  (instant child) and {B 1000 ms overhead median} → {F 1000 ms overhead median} ms (1000 ms child,
  wall-clock {F 1000 ms wrapped wall-clock median} ms).
```

- [ ] **Step 6: Mark the spec implemented**

In `docs/superpowers/specs/2026-09-13-tracking-path-design.md`, replace the first line
`**Status:** Approved — plan to follow` with
`**Status:** Implemented — see [the plan](../plans/2026-09-13-tracking-path.md)`.

- [ ] **Step 7: Commit**

Write `/tmp/dtk-task7-commit.txt`, filling the figures as in Step 5:

```text
docs: record the tracking-path cold-start figures

pipe {B pipe median} -> {F pipe median} ms; wrapped overhead
{B instant overhead median} -> {F instant overhead median} ms with an
instant child and {B 1000 ms overhead median} -> {F 1000 ms overhead median} ms
with a 1000 ms child.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

```bash
git add CLAUDE.md docs/superpowers/specs/2026-09-13-tracking-path-design.md
git commit -F /tmp/dtk-task7-commit.txt
```

Report every medians table (B, `2-tieredpgo`, F, tracking off before/after) in the task report.

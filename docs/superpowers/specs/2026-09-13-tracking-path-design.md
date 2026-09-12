**Status:** Approved — plan to follow

## Context

This is the second of four sub-projects on the startup-speed effort (roadmap and scoping
measurements in [the measurement spec](2026-09-12-startup-speed-measurement-design.md)):

1. **Measurement:** done in PR #145.
2. **Tracking path** (this spec).
3. **Native AOT distribution.**
4. **Tokenizer spooling:** deferred until AOT shows what remains.

Measured before this work (Ryzen 5 3600, Linux, `dtk pipe build` over `dotnet_build_errors.txt`,
2.6 KB, JIT build):

- Cold start: 295 ms with tracking on, 216 ms with tracking off.
- Tracking parts, each timed in a separate probe process: tiktoken `cl100k_base` vocabulary load
  ~110 ms once per process; token counting ~190 ms per MB; SQLite open, schema, retention `DELETE`
  and `INSERT` ~39 ms cold.
- `DOTNET_TieredPGO=0`: −9 ms.
- `cold-start` after PR #145: pipe 287.2 ms; wrapped `dtk dotnet build` overhead 285.8 ms with an
  instant fake child and 288.4 ms with a child that sleeps 1000 ms. The two overheads are equal
  because dtk does no work while the child runs. That gap is what this sub-project closes.

## Problem

Every tracked run pays for the tokenizer vocabulary load and the SQLite setup serially, after the
child process has exited, although neither depends on the child's output. A `dotnet build` spends
seconds with dtk idle, waiting on it.

A probe run while designing this (21 fresh processes per variant, medians, database on ext4 unless
stated) broke the SQLite cost down:

| Phase | Cost |
|---|---:|
| `new SqliteConnection(...)`: static init loads the native library | 23.3 ms |
| `Open` | 4.8 ms |
| JIT of the first statement, whichever runs first | ~6.7 ms |
| `CREATE TABLE/INDEX IF NOT EXISTS` | ~1.1 ms, mostly JIT |
| 3 `pragma_table_info` checks | 2.4 ms, mostly parameter-binding JIT the `INSERT` would otherwise pay |
| Retention `DELETE` | 0.19 ms |
| `INSERT`, rollback journal | 8.7 ms, of which ~5.6 ms is fsync (1.4 ms on tmpfs) |
| `INSERT`, WAL with `synchronous=NORMAL` | 3.4 ms |

Three conclusions follow:

- **The statements are not the cost.** Executing them takes about a millisecond; the rest is
  native library loading and JIT. The roadmap's "fewer SQLite statements" ideas (a
  `PRAGMA user_version` gate, once-a-day retention) would save one to three milliseconds, most of
  it JIT that moves elsewhere rather than disappearing.
- **WAL buys nothing end to end.** Microsoft.Data.Sqlite clears its connection pool at process exit,
  which checkpoints the WAL and pays the fsync back: no `-wal` file survives the process, and
  whole-process wall-clock is 96.2 ms in rollback mode against 96.8 ms in WAL mode.
- **Tracking-off runs likely pay for SQLite they never use.** `SqliteTracker` creates its
  connection in a field initializer, and the DI factory builds the tracker whenever
  `FilteredOutputPipeline` is resolved, whether or not tracking is enabled. That is the 23 ms
  native load on every filtered run.

## Solution

Three changes to dtk, one to the harness.

### 1. Background setup

Setup starts when a run starts, concurrently with the child process (or the stdin read), and is
awaited just before tracking needs it.

**Invariant:** background setup only moves work earlier. It never changes what is counted, what is
written, or how dtk exits.

#### Domain

`ITracker` gains `Task WarmUpAsync(CancellationToken cancellationToken = default)`: perform the
setup the first call would otherwise do, so a later `RecordAsync` only inserts. The three
hand-written `StubTracker` classes in `DotnetTokenKiller.Cli.IntegrationTests` and the benchmarks'
`NullTracker` implement it as a completed task; NSubstitute fakes need no change.

#### Infrastructure: `SqliteTracker`

- The connection is created inside `EnsureInitializedAsync`, not in a field initializer, so
  constructing a tracker loads nothing.
- `WarmUpAsync` takes the existing semaphore and calls `EnsureInitializedAsync`. A `RecordAsync`
  that arrives mid-warm-up waits on the semaphore.
- When any step of `EnsureInitializedAsync` throws, it disposes the connection it created and
  clears it before rethrowing. The next call (a `RecordAsync` after a failed warm-up) starts again
  from nothing, rather than reopening a half-initialized connection and throwing "connection
  already open".
- `Dispose`/`DisposeAsync` take the semaphore before disposing the connection, so an initialization
  still running on a background thread (a warm-up that outlived its run, for example when the child
  failed to launch) is never disposed underneath. They tolerate a connection that was never created
  and a second call. An initialization that acquires the semaphore after disposal throws
  `ObjectDisposedException`, which the warm-up discards.
- Schema, migrations and retention are otherwise unchanged; see Out of scope.

#### Application: `TokenEstimator`

`TokenEstimator.WarmUp(TokenizerModel model)` forces the existing per-model `Lazy`. Its default
mode (`ExecutionAndPublication`) is thread-safe: an `Estimate` call made while the load is still in
progress blocks on the same `Lazy`, so the vocabulary loads once and counts are exact. `Estimate`
itself is unchanged, including returning 0 for empty text without loading anything.

#### Application: `TrackingWarmUp`

Shared by the pipeline and the passthrough use case, so the two cannot start or observe setup
differently.

- `static TrackingWarmUp Start(ITracker tracker, TokenizerModel? tokenizer, CancellationToken
  cancellationToken)` starts `Task.Run(() => tracker.WarmUpAsync(cancellationToken))` and, when
  `tokenizer` is not null, `Task.Run(() => TokenEstimator.WarmUp(tokenizer))`. `Task.Run` matters for
  the tracker: the 23 ms native load inside the connection's static initializer is synchronous and
  would otherwise run on the calling thread. Each task catches and discards its own exceptions.
- `static TrackingWarmUp None` is already complete, for runs that do not track.
- `Task WhenReadyAsync()` completes when both tasks have finished and **never throws**.

#### Application: `FilteredOutputPipeline`

- New `Task<PreparedRun> BeginAsync(CancellationToken cancellationToken = default)`. It loads the
  config once and, when `config.Tracking.Enabled`, calls
  `TrackingWarmUp.Start(tracker, config.Tracking.Tokenizer, cancellationToken)`; otherwise it uses
  `TrackingWarmUp.None`.
- `PreparedRun` is a record carrying the loaded `DtkConfig` and its `TrackingWarmUp`.
- New overload `ProcessAsync(FilteredOutputRequest request, ITeeSession session, PreparedRun
  prepared, CancellationToken cancellationToken = default)` uses `prepared.Config` instead of
  loading config again. `TrackIfEnabledAsync` awaits `prepared.WarmUp.WhenReadyAsync()` and then proceeds
  exactly as today, so any real failure resurfaces in `Estimate` or `RecordAsync` and is swallowed at
  the existing single catch site.
- The existing `ProcessAsync(request, session, cancellationToken)` stays, defined as
  `BeginAsync` followed by the new overload. Callers that do not prepare keep today's serial
  behaviour, and existing tests and the pipeline benchmark need no change.

#### Use cases

- **`FilteredRunUseCase`:** calls `pipeline.BeginAsync` first, before opening the tee session and
  launching the child, and passes the result to `ProcessAsync`.
- **`PipeFilterUseCase`:** calls `pipeline.BeginAsync` before `ReadToEndAsync`, so setup overlaps a
  slow producer and the rest of dtk's own work.
- **`PassthroughRunUseCase`:** already receives the loaded config and has no pipeline. When
  `tracker` is not null and tracking is enabled, it starts the tracker warm-up at the top of
  `RunAsync`, and the tokenizer warm-up only on the measured branch (the interactive branch records
  zero tokens and never estimates), both through `TrackingWarmUp.Start`. `TrackAsync` awaits
  `WhenReadyAsync()` before recording.

**Accepted consequence:** config is now read before the child runs rather than after. A
`dtk config set` made while a build is running applies from the next run.

#### Timing

- **Long child:** both warm-ups finish during the child. After it exits, `WhenReadyAsync` returns
  immediately and only token counting and the `INSERT` remain serial.
- **Instant child, or `pipe` from a file:** the main thread reaches tracking first and awaits the
  remainder. The serial cost is at most today's plus a thread hop. The real risk is CPU contention
  with main-thread JIT, which is why the instant scenario is gated as "must not regress".

#### Failure semantics

Each case keeps today's user-visible behaviour:

- **Tokenizer load throws:** discarded by `WhenReadyAsync`. `Estimate` rethrows the exception the
  `Lazy` cached, the existing catch swallows it, and no row is recorded, as today.
- **SQLite warm-up throws** (bad path, locked database, full disk): discarded. `RecordAsync` retries
  initialization once on the main thread and either succeeds or is swallowed. A broken database gets
  one extra attempt, only on the failure path.
- **Child fails to launch** (`dotnet` not on `PATH`): the exception propagates exactly as today. The
  warm-up tasks are left unobserved, which does not crash a .NET process. An interrupted retention
  `DELETE` is protected by SQLite's rollback journal.
- **Cancellation:** the warm-ups receive the run's token; a cancelled warm-up is discarded like a
  fault.
- **Concurrent dtk processes:** the retention `DELETE` now runs during the child rather than after
  it. It holds the same write lock for the same fraction of a millisecond, so contention risk is
  unchanged; Microsoft.Data.Sqlite already retries on `SQLITE_BUSY`.

Unchanged: the recorded elapsed time (still taken when tracking is reached), filter-fault and
raw-tail handling, tee finalization order, `-v`/`-vv` output, and every reader (`gain`, `reset`,
`log`), which keeps lazy initialization with no warm-up.

### 2. `TieredPGO` off

`<TieredPGO>false</TieredPGO>` in `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`. It is
written to `dtk.runtimeconfig.json` as `System.Runtime.TieredPGO: false`, which ships inside the
tool package, so installed users get it too. A one-shot CLI process never runs long enough to
benefit from dynamic PGO's instrumentation tier.

### 3. `PassthroughEntryPoint` documentation

Its remark says tracking "opens one SQLite connection after the child has already exited". It is
updated to describe the background warm-up.

### 4. `cold-start` harness

- **Alternating pair order.** Even iterations run the child alone and then dtk; odd iterations run
  dtk and then the child alone. This removes the 2–4 ms bias from dtk always following an idle
  child.
- **Environment in the header.** Print every `DOTNET_*` and `COMPlus_*` variable in the harness's
  environment (or `none`), and the `configProperties` of the measured binary's
  `runtimeconfig.json`, so a TieredPGO-off build is visible in the output.
- **tmpfs caveat, documented rather than changed.** The hermetic state lives under the temp root,
  which is tmpfs on the measuring machine, so the `INSERT`'s ~5.6 ms ext4 fsync is absent from every
  figure. That cost is serial either way and this sub-project does not change it; CLAUDE.md says so
  next to the numbers.

## Measurement protocol

Each step is a full `cold-start` run, preceded by
`dotnet build src/DotnetTokenKiller.Cli -c Release` and a check of the `Built:` line:

1. **Baseline:** the harness changes only, nothing under `src/` changed.
2. **TieredPGO:** plus change 2.
3. **Final:** plus changes 1 and 3.

Each scenario is compared only with its own baseline figure, never with another scenario.

Separately, a one-off check (not a harness feature) confirms the tracking-off finding: 21 runs of
`dtk pipe build` over the fixture with tracking disabled in a hermetic config, before and after the
lazy connection.

## Success criteria

- The 1000 ms-child overhead median drops well below the instant-child overhead median. The parts
  suggest roughly 288 → 135 ms, but the criterion is the relationship, not that estimate.
- The instant-child overhead and pipe medians do not regress by more than 5 ms against their
  baselines.
- Every measured run still records a tracking row with a positive input-token delta, which
  `cold-start` already enforces.
- `SavingsBaselineTests` pass with the baseline file unchanged, which shows token counts are exact.

## Testing

Test-first, in `DotnetTokenKiller.Application.Tests` and `DotnetTokenKiller.Infrastructure.Tests`
(the CLI integration tests cannot run on the development machine and are gated by CI):

- **`FilteredOutputPipeline`:**
  - `BeginAsync` with tracking disabled never calls `ITracker.WarmUpAsync`.
  - `BeginAsync` with tracking enabled calls `WarmUpAsync` exactly once.
  - `ProcessAsync` with a `PreparedRun` does not call `IConfigProvider.LoadAsync` again.
  - A throwing `WarmUpAsync` still yields the request's exit code, the filtered output, and an
    attempted `RecordAsync`.
  - A tracker whose `RecordAsync` throws after a successful warm-up still leaves the output and exit
    code intact.
- **`FilteredRunUseCase` and `PipeFilterUseCase`:** the warm-up starts before `RunStreamedAsync`, or
  before the stdin read, asserted through the order of calls on the fakes.
- **`PassthroughRunUseCase`:** the tracker is warmed on both branches when tracking is enabled; the
  interactive branch performs no token estimate; a null tracker or disabled tracking warms nothing.
- **`SqliteTracker`:**
  - Disposing a tracker that was never used (so never created a connection) does not throw, both
    synchronously and asynchronously. Whether construction still loads the native library is not
    observable from a unit test; the tracking-off timing check covers it.
  - `WarmUpAsync` then `RecordAsync` persists exactly one row.
  - A warm-up that fails before opening (the database's directory cannot be created), followed by
    the path becoming usable, lets `RecordAsync` succeed.
  - A warm-up that fails after opening (the file is not a SQLite database), followed by the file
    being removed, lets `RecordAsync` succeed.
  - Disposing a tracker while its warm-up is still running does not throw.
- **`TrackingWarmUp`:** `WhenReadyAsync` completes without throwing when the tracker's warm-up
  throws; `Start` calls `WarmUpAsync` exactly once.
- **`TokenEstimator`:** `WarmUp` running concurrently with `Estimate` on the same text returns the
  same count as an `Estimate` with no warm-up.

## Out of scope

- **SQLite statement reduction and WAL.** The probe above shows statements cost about a millisecond
  and WAL no measurable end-to-end time; background setup removes the rest from the serial path.
  Recorded here so the ideas are not re-proposed without new evidence.
- **Starting setup at `Program.cs` entry.** It would overlap Spectre's startup JIT and help the
  instant-child and pipe scenarios, but the CLI layer would have to predict from raw arguments which
  command will track. Worth a separate, measured change only if sub-project 3 shows those paths still
  matter.
- Native AOT, tokenizer spooling or approximate counts, Spectre changes, and filter changes.
- **The rewrite hook's double-prefix bug.** It lives in `.claude/hooks/dotnet-to-dtk.py` and its
  template in `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs`: a command with
  `dtk` glued to a backtick is rewritten to `` `dtk dtk dotnet build` ``. It is unrelated, has been
  reported, and gets its own change.

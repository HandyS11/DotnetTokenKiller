**Status:** Draft, awaiting the owner's review. Written in an autonomous session: the decisions in
"Decisions" were taken without the owner and are the first thing to check. Plan:
[docs/superpowers/plans/2026-09-13-serial-costs-and-windows-aot.md](../plans/2026-09-13-serial-costs-and-windows-aot.md).

## Context

This is the fourth sub-project of the startup-speed effort (roadmap in
[the measurement spec](2026-09-12-startup-speed-measurement-design.md)): measurement (PR #145), the
tracking path (#146), Native AOT (#147) and its Linux and Windows follow-up (#150) are merged into
`develop`. The roadmap's fourth item, "tokenizer spooling", was deferred "until AOT shows what
remains". This spec is what remains.

### What AOT left behind (measured 2026-09-13, 8-core Linux box, tmpfs, medians)

`cold-start`-style loops over the installed linux-x64 AOT package and two local builds of the same
commit:

| Scenario | `any` (JIT) | ReadyToRun | AOT |
|---|---:|---:|---:|
| `dtk --version` | 111.6 ms | 69.8 ms | 13.0 ms |
| `pipe build`, 2.6 KB, tracking on | 229.0 ms | 166.9 ms | 64.9 ms |
| `pipe build`, 2.6 KB, tracking off | 199.6 ms | 135.3 ms | 14.6 ms |
| `pipe build`, 1 MB, tracking on | 375.8 ms | 304.1 ms | 137.0 ms |
| `pipe build`, 1 MB, tracking off | | | 45.0 ms |
| `dtk gain --json` | | | 14.7 ms |

An AOT probe process with the same dependencies split the tracking share (one fresh process per
sample):

| Item | Cost |
|---|---:|
| Process floor (no work) | 5.7 ms |
| ICU load (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` saves it) | 1.9 ms |
| tiktoken `cl100k_base` vocabulary load | 50.9 ms |
| `CountTokens`, per MB of text (linear: 0.19 ms at 2.6 KB, 486 ms at 10 MB) | 48 ms |
| `SqliteConnection` constructor, open, schema, column checks, retention `DELETE` | 2.4 ms |
| `INSERT` on tmpfs / on this box's ext4 disk (the maintainer's ext4 measured 5.6 ms) | 0.12 ms / 11.3 ms |
| Appending one JSON line to a file, either filesystem | 0.06 ms |

Three conclusions:

- **The tokenizer load is the whole 50 ms tracking share, and it is already hidden in real use.**
  `BeginAsync` starts it before the child launches (wrapped runs) or before stdin is read (pipe from
  a live producer). It is serial only for `pipe` from a file and for the harness's instant child.
  Removing it would help synthetic scenarios most; this spec leaves it alone.
- **The costs that stay serial after the child exits are the `INSERT`'s fsync, token counting of
  the raw output, and dtk's own startup.** The fsync is invisible to `cold-start`, whose hermetic
  state lives on tmpfs, so the harness has never measured the largest per-command cost users on a
  real disk pay.
- **Windows pays about 190 ms per command** with the `any` package, even with a long child, because
  the counting and SQLite code is JIT-compiled after the child exits. It is the largest user-facing
  gap left.

### The Windows shim, revisited (2026-09-13)

PR #150 dropped the win-x64 AOT package because the SDK writes a `dtk.cmd` batch file for a tool whose
runner is `executable`: Git Bash cannot run it and cmd re-parses `| & ^ %` in arguments.

SDK 10.0.400's `ShellShimRepository.CreateShim` (dotnet/sdk, `release/10.0.4xx`) has two branches.
For runner `executable` it writes the batch file on Windows and a symlink elsewhere, and never looks
at packaged shims. For runner `dotnet` it first calls `TryGetPackagedShim`, which matches a packaged
shim **by file name only** (`dtk.exe` on Windows, `dtk` elsewhere), copies it verbatim with
`File.Copy`, and sets the execute bit. No format, signature or size check. `ToolPackageInstance`
finds packaged shims under `tools/<tfm>/<rid>/shims/<rid>/` in the RID-specific package's own assets
file, so the mechanism reaches RID-specific packages.

Validated end to end on Linux with SDK 10.0.400:

1. `dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:PublishAot=false
   -p:UseAppHost=false -p:IncludeSymbols=false -p:PackAsToolShimRuntimeIdentifiers=linux-x64` produced
   a package with `Runner="dotnet"`, `EntryPoint="dtk.dll"`, the framework-dependent app under
   `tools/net10.0/linux-x64/`, and a 78 KB apphost at `tools/net10.0/linux-x64/shims/linux-x64/dtk`.
   `UseAppHost=false` matters: `Microsoft.NET.PackTool.targets` sets the runner to `executable` when
   the pack is RID-specific and either `UseAppHost` or `PublishAot` is true.
2. That shim was replaced inside the package by the 17.5 MB AOT `dtk` from the linux-x64 package,
   and a pointer was packed with `-p:ToolPackageRuntimeIdentifiers=linux-x64`.
3. `dotnet tool install DotnetTokenKiller --tool-path … --add-source …` resolved
   `dotnettokenkiller.linux-x64`, and `<tool-path>/dtk` was the AOT binary, byte for byte: 13.3 ms
   for `--version`, 66.6 ms for `pipe build`. `dotnet <store>/…/dtk.dll --version` still ran.

The same package on Windows yields `%USERPROFILE%\.dotnet\tools\dtk.exe` as the native binary. What
the Linux run could not verify: the `.exe` name match, and SQLite. The lone `dtk.exe` in the tools
directory has no `e_sqlite3.dll` beside it, and SQLitePCLRaw ships no static library for Windows.
`SQLitePCLRaw.provider.winsqlite3` (3.0.5) and `SQLitePCLRaw.bundle_winsqlite3` (2.1.11) use the
SQLite that Windows 10 1903 and later ship in System32.

## Problem

- Every tracked command pays an `INSERT` fsync after the child has exited: 5 to 11 ms on the disks
  measured, and unmeasured by the harness.
- Every tracked command counts the raw output after the child has exited: 48 ms per MB, so about
  10 ms for a 200 KB build log and 250 ms for 5 MB of test output.
- Windows x64 has no native package, and the reason it was dropped has a way around it that has not
  been tried on Windows.
- `cold-start` cannot show either of the first two: its database is on tmpfs and its wrapped
  scenarios print 2.6 KB.

## Decisions

Each was taken without the owner. Overrule any of them before the plan runs.

1. **Scope: three independent parts, one plan, three pull requests.** (1) Harness: measure on a real
   disk and with a large output. (2) Tracking hot path: a pending-record journal replaces the
   `INSERT`, and token counting streams while the child runs. (3) Windows x64: Native AOT through
   a packaged shim. The tokenizer load, a Spectre bypass, approximate counts and invariant
   globalization are out of scope (see the end).
2. **Journal, not `PRAGMA synchronous=OFF`.** Both remove the fsync. The journal also removes SQLite
   from the write path entirely (2.4 ms of setup, the native library, the write lock that
   concurrent dtk processes contend for, and the glibc 2.34 failure of the `any` package's writes),
   at the cost of about 150 lines. `synchronous=OFF` is one line but keeps all of that and risks a
   corrupt database on power loss.
3. **Counts stay exact.** Streaming counting must produce the same number as today's whole-text count,
   proven by a test, so `savings-baseline.json` and the parity rows do not move.
4. **Windows: packaged shim carrying the native binary; winsqlite3 in that binary; win-x64 only.**
   Probed on `windows-latest` before anything is merged; ReadyToRun framework-dependent packages are
   the documented fallback if the probe fails. win-arm64 keeps `any`. The managed payload of the
   win-x64 package is not ReadyToRun-compiled (it serves `dotnet tool run` and `dnx` only).
5. **Constants:** the writer folds the journal in the background when it holds 64 or more files;
   counting chunks are at least 64 K chars.

## Solution

### 1. Harness

- **`cold-start [binary] [--state-dir <dir>]`.** `HermeticState.Enter(root)` creates the hermetic
  directory under `<dir>` instead of the temp root. The header prints the state root and the
  filesystem it is on (`DriveInfo.DriveFormat`), beside the existing environment and runtime-config
  lines, so a tmpfs run and a disk run are distinguishable in artifacts.
- **A fourth scenario:** `dotnet build`, child sleeping 1000 ms, printing a generated build log of
  about 1 MB (the `LogCorpusGenerator` build tier nearest that size) instead of the 2.6 KB fixture.
  Same pairing and alternation as scenarios 2 and 3; the summary line it must print is computed the
  same way, through `SavingsScenarios.FilterFor`. It brackets counting and tee cost at a realistic
  size, which the 2.6 KB fixture cannot.
- **Row verification is unchanged** and keeps going through `SqliteTracker.GetSummaryAsync`, which
  after part 2 folds the journal before reading.
- CLAUDE.md's Benchmarks section records one run on the real disk beside the tmpfs figures.

### 2. Tracking hot path

#### 2.1 Pending-record journal

**Invariant:** what `gain`, `--coverage`, `--export`, `log` and `reset` report is unchanged; only
where a record waits between the run and the report changes.

**`PendingRecordJournal`** (Infrastructure, `Tracking/`), owned by `SqliteTracker`, rooted at
`<database directory>/pending/`:

- `WriteAsync(CommandRecord)`: serializes one `PendingRecord` (the flat fields of `CommandRecord`,
  execution time in milliseconds, enums as strings, plus `"Version": 1`) with a source-generated
  `JsonSerializerContext`, and writes it to `<utc ticks>-<pid>-<32 hex>.json` with
  `FileMode.CreateNew`. One file per run: no shared file, so concurrent dtk processes never write to
  the same handle and Windows needs no append semantics. Creating the directory is part of the
  write.
- `Count()`: the number of `*.json` files, zero when the directory is missing.
- `FoldAsync(committed, commit, wait, CancellationToken)`, where `committed(id)` asks the tracker
  whether a fold id is in the database and `commit(ids, records)` inserts, folds exactly once under
  any interruption:
  1. Takes `pending/.lock` with `FileShare.None` (an exclusive `flock` on Unix). With `wait`, it
     retries for up to two seconds and then throws, so a reader never silently reports rows another
     process is inserting as missing; without `wait` (the writer's background fold) a busy lock means
     another process is folding, and it returns at once.
  2. Recovers leftovers: for every `pending/folding-<id>/` directory, `committed(id)` says whether
     fold `<id>` is in the database (the `folds` table below); if it is, the directory is deleted (a
     fold that died after its commit); if not, its files join this fold (a fold that died before it).
  3. Claims: moves every `pending/*.json` into a new `pending/folding-<id>/` with `File.Move`, so
     a writer creating a file at that instant is never half-read.
  4. Parses the claimed files, deleting one that does not parse (a process killed mid-write leaves a
     truncated file; nothing else can) and counting it, and calls `commit(ids, records)` once, with
     the new id and every recovered claim's id. The tracker's commit is one transaction: the
     `INSERT`s, one `INSERT INTO folds (id)` per id (so a recovered claim that dies after this commit
     is recognised by its own id next time), and retention.
  5. Deletes the claimed directories and releases the lock.

  A `commit` that throws leaves the claimed directory, which step 2 folds next time. A process that
  dies at any point releases the lock with its handle; step 2 tells a committed batch from an
  uncommitted one by its id, so nothing is inserted twice and nothing is lost. The `folds` table
  (`id TEXT PRIMARY KEY`, created beside `commands`) grows by one row per fold and is never pruned.

**`SqliteTracker`:**

- `RecordAsync` writes to the journal and touches nothing else: no connection, no statement, no
  fsync. The `:memory:` data source that tests use keeps today's direct `INSERT`, since it has no
  directory to journal into.
- `WarmUpAsync` creates the pending directory and, when `Count()` is at least 64, starts a fold on
  the thread pool (`wait: false`) and does not await it. That fold's `SqliteConnection` setup,
  retention, inserts and fsync run while the child runs. It is best effort: a fold cut short by
  process exit is harmless (see `FoldAsync`), and `RecordAsync` never waits for it.
  `DisposeAsync` waits for an in-flight fold as it waits for an in-flight warm-up today, at most one
  fold's duration (about 15 ms for 64 records), once per 64 runs, and only on the passthrough path,
  which disposes its tracker.
- Every reader (`GetSummaryAsync`, `GetHistoryAsync`, `GetCoverageAsync`) and `CleanupAsync` call
  `FoldAsync` after `EnsureInitializedAsync`, inside the existing semaphore, so what they return
  includes every run that has finished. Retention runs at fold time; it leaves the write path.
- `ResetAsync` deletes the journal's files and claimed directories, and the `folds` rows, as well
  as the `commands` rows.
- The DI factory and `PassthroughEntryPoint` build the tracker exactly as today; the journal is
  internal to it.

**Consequences, all accepted:**

- The database file is created on the first fold, not the first run. `doctor`'s "No data yet" line
  stays true until then.
- The `any` package on glibc older than 2.34 now records runs (the write path has no native code)
  but still cannot report them; the docs say so.
- `dtk gain` on a journal of 64 files pays one transaction with one fsync, about 15 ms, once.
- `SqliteLoaderTests`' positive control moves from `pipe build` to `gain`: a tracked run no longer
  loads SQLite at all, which is the point. The tracking-off assertion stays.
- `smoke-old-glibc.sh` needs no change: `gain --json` folds before it counts.

#### 2.2 Streaming token counting

**Invariant:** the recorded input-token count equals `TokenEstimator.Estimate(AnsiStrip.Strip(raw))`
for the same raw text, where raw is stdout followed by stderr, as today.

**`ChunkedTokenCounter`** (Application, `Helpers/`):

- `Append(ReadOnlySpan<char>)` adds text to a pending buffer. When the buffer holds at least 64 K
  chars, the counter looks for the last **safe cut** in it; if one exists, the text before the cut
  becomes a chunk, `Task.Run(() => TokenEstimator.Estimate(AnsiStrip.Strip(chunk), model))` is
  started for it, and the buffer keeps the rest. Tasks are collected, never awaited here.
- `Finish(string trailing)` starts counting the buffer plus `trailing` as the final chunk;
  `Task<int> TotalAsync()` awaits every chunk task and returns the sum. It throws if any chunk did;
  the caller's existing catch turns that into "no row", as a failed estimate does today. (Two
  members rather than one returning a task: a task read back from a request property would trip
  VSTHRD003 when awaited.)
- The counter is fed stdout only. `Finish(stderr)` appends stderr to the last chunk, so the sum is
  the count of stdout followed by stderr, and no cut has to be justified across the seam.

**Safe cut.** A cut at index `p` of the pending text is safe when all of these hold:

1. `text[p-1]` is `\n` and `p < text.Length`.
2. `text[p]` is not whitespace and not `/`.
3. The text before `p` does not end inside an escape sequence: its last `\x1b`, if any, begins a
   CSI or OSC match that ends at or before `p` (`AnsiStrip.EndsInsideEscapeSequence`, new, using
   the existing generated regexes).

Why this is exact, from the pre-tokenizer patterns in Microsoft.ML.Tokenizers 2.0.0
(`TiktokenTokenizer.cs`): a pre-token can span a newline only through
`(?>\s+)$`, `\s*[\r\n]`, `\s+(?!\S)`, `\s` and ` ?[^\s\p{L}\p{N}]+[\r\n]*` in `cl100k_base`, and
`\s*[\r\n]+`, `\s+(?!\S)`, `\s+` and ` ?[^\s\p{L}\p{N}]+[\r\n/]*` in `o200k_base`. Every one of them
ends at the end of a newline run when a non-whitespace character follows, except the last, which
also swallows a following `/` in `o200k_base`; rule 2 excludes both. The letter, number and
contraction patterns cannot contain a newline (`[^\r\n\p{L}\p{N}]?` is the only prefix they take).
Special tokens contain no newline. A whitespace run that ends at the chunk boundary is matched by
`(?>\s+)$` in the chunk and by `\s*[\r\n]` in the whole text, both as one pre-token of the same
characters, so its BPE is the same. Rule 3 keeps `AnsiStrip` chunk-local: only an OSC sequence can
span lines, and only an unterminated one could be cut. The proof is a test, not this paragraph:
see Testing.

**Wiring:**

- `CountingTextWriter` (Application, `Helpers/`): a `TextWriter` that forwards `WriteLineAsync`,
  `FlushAsync` and `NewLine` to an inner writer and appends each line plus the inner writer's
  `NewLine` to a counter. `FilteredRunUseCase` wraps the stdout sink in it when tracking is enabled
  (`prepared.Config`); stderr keeps the plain tee writer. `ProcessCommandRunner` does not change.
- `FilteredOutputRequest` gains `ChunkedTokenCounter? InputTokenCounter`. `FilteredRunUseCase`
  calls `counter.Finish(result.StdErr)` and passes the counter; `PipeFilterUseCase` reads stdin in
  64 K-char blocks into both a `StringBuilder` and the counter instead of `ReadToEndAsync`, calls
  `counter.Finish(string.Empty)`, and passes it. Neither awaits the total.
- `FilteredOutputPipeline.TrackIfEnabledAsync` awaits `request.InputTokenCounter.TotalAsync()` when
  the counter is present, else calls `Estimate(stripped)` as today. The output count is unchanged (the filtered text is small).
  Both stay inside the existing catch.
- The chunk tasks block on the tokenizer `Lazy` until the warm-up has loaded it; that is the
  existing `ExecutionAndPublication` behaviour and needs nothing new.

**What the serial path keeps:** the last chunk (under 64 K chars plus stderr), the output count, and
the journal write (0.06 ms).

### 3. Windows x64: Native AOT through a packaged shim

**Package shape.** `DotnetTokenKiller.win-x64` is packed on `windows-latest` as a framework-dependent
RID package (`-r win-x64 -p:PublishAot=false -p:UseAppHost=false -p:IncludeSymbols=false
-p:PackAsToolShimRuntimeIdentifiers=win-x64`), so its runner is `dotnet` and its entry point
`dtk.dll`. Its packaged shim, `tools/net10.0/win-x64/shims/win-x64/dtk.exe`, is the Native AOT
binary published in the step before (`dotnet publish -c Release -r win-x64`). On install the SDK
copies that file to `%USERPROFILE%\.dotnet\tools\dtk.exe`, where Git Bash, pwsh and cmd run it
directly. `dotnet tool run dtk`, tool manifests and `dnx` run `dtk.dll` on the runtime, as the
`any` package does today. Windows arm64 and x86 keep `any`.

**Build mechanics.**

- `ToolPackageRuntimeIdentifiers` gains `win-x64`.
- A `UseNativeBinaryAsPackagedShim` target, `AfterTargets="GenerateShimsAssets"`, active when
  `$(DtkPackagedShim)` is set: copies that file over
  `$(PackagedShimOutputRootDirectory)shims/$(_ToolPackShortTargetFrameworkName)/$(RuntimeIdentifier)/$(ToolCommandName).exe`
  and errors if the source or the generated shim is missing. MSBuild cannot read a file's size, so
  `eng/aot/pack-windows.sh` compares the packed shim with the published exe byte for byte (an
  apphost is about 160 KB; a wrong path must not ship a launcher for a dll the shim cannot find).
- `CopyOutputSymbolsToPublishDirectory=false` and no `.xml` files for this RID, as for the AOT RIDs;
  the managed `.pdb`s belong to the `any` symbols package, not here.
- **SQLite.** `DotnetTokenKiller.Infrastructure` references `Microsoft.Data.Sqlite.Core` instead of
  `Microsoft.Data.Sqlite`, so the bundle is chosen by the application: the CLI references
  `SQLitePCLRaw.bundle_e_sqlite3` (2.1.12, what `Microsoft.Data.Sqlite` 10.0.12 resolves today)
  unless `PublishAot` is true and the RID is `win-x64`, where it references
  `SQLitePCLRaw.bundle_winsqlite3` (2.1.11, the newest published). The test and benchmark projects that open a
  database reference `bundle_e_sqlite3` directly. The Linux static link and `fcntl64` shim are
  untouched. The native `dtk.exe` therefore needs no file beside it; `dtk.dll` keeps
  `e_sqlite3.dll` in the package.
- The AOT publish writes an MSBuild file log; `AotWarningLogTests` reads it as it reads a pack log.

**CI.** `aot-package.yml` gains Windows branches (`if: runner.os == 'Windows'`) rather than a new
workflow, and `ci.yml`'s and `publish.yml`'s matrices gain the row
`win-x64 / windows-latest / windows-latest / '' / false / true`:

- pack: build the solution at the version; AOT-publish to `artifacts/aot/win-x64` with a file log;
  pack the RID package with `-p:DtkPackagedShim=<that dtk.exe>`; unzip the package and fail unless
  the shim's SHA-256 equals the published exe's; upload the package, the log and `dtk.pdb` as
  symbols.
- test: install from the local feed through the source-mapped `nuget.config` that
  `fallback-package.yml` already generates; assert the store holds `dotnettokenkiller.win-x64` and
  that `<tools>/dtk.exe` has the published exe's SHA-256; run `AotParityTests` and
  `AotWarningLogTests` with `DTK_AOT_BINARY=<tools>/dtk.exe` and `DTK_AOT_PACK_LOG=<publish log>`;
  run the whole integration suite with `DTK_TEST_BINARY`; run the three-shell smoke from
  `fallback-package.yml` (`command -v dtk` in Git Bash, the verbatim `Name~A|Name~B` argument in all
  three); run `dtk pipe build --exit-code 1` over the fixture with hermetic state and require
  `gain --json` to report one command (this is the winsqlite3 check); run
  `dotnet <store>/…/tools/net10.0/win-x64/dtk.dll --version`; run `dotnet tool uninstall` against
  the tool path and assert the shim is gone, then install again and assert it is back (the SDK's
  `RemoveShim` and `CreateShim` paths).
- `fallback-package.yml`'s Windows row stays: `any` still serves Windows arm64 and x86.
- `publish.yml` derives the RID list from the pointer, so the new package and its symbols are
  gathered and pushed without further change; the release grows by one `.nupkg` and one symbols zip.

**Probe first.** Before any of the above is written for real, a throwaway branch runs the pack and
test steps once through `workflow_dispatch`. It passes when the installed `dtk.exe` is the native
binary, every shell smoke passes, and `gain --json` reports the tracked run. If the SDK does not
copy the packaged shim on Windows, the fallback is a ReadyToRun framework-dependent win-x64
package (measured here: `--version` 112 → 70 ms, pipe 229 → 167 ms), which is the same pack
command with `-p:PublishReadyToRun=true` and no shim replacement; the plan stops for the owner's
decision.

**Docs:** the platform table in `README.md`, `src/DotnetTokenKiller.Cli/README.md` and
`docfx/articles/getting-started.md` gets a Windows x64 row (native binary as the command, managed
app for `dotnet tool run` and `dnx`; needs Windows 10 1903 or later for winsqlite3 and ICU, the same
floor the runtime has); CLAUDE.md's Native AOT section replaces "Windows has no AOT package" with
the packaged-shim explanation and the `DtkPackagedShim` property; `DocsBindingTests` follows.

## Measurement protocol

Every step is a full `cold-start` run against an installed linux-x64 AOT package built from the
commit under test, twice: once with the default tmpfs state and once with `--state-dir` on the
measuring machine's ext4 disk. Each scenario is compared only with its own baseline figure.

1. **Baseline:** part 1 merged, nothing under `src/` changed. Records the on-disk fsync and the
   1 MB scenario for the first time.
2. **Journal:** plus 2.1.
3. **Streaming count:** plus 2.2.
4. **Windows:** a step in the probe run on `windows-latest` times 21 runs each of `dtk --version`
   and `dtk pipe build --exit-code 1` over the fixture, `any` against `win-x64`, and prints the
   medians; runner noise makes them indicative only. `cold-start`'s wrapped scenarios need a POSIX
   shell and are not run there.

## Success criteria

- **Part 1:** the on-disk run's 1000 ms-child overhead is higher than the tmpfs run's by an amount
  consistent with that disk's `INSERT` cost (5 ms or more on ext4), which shows the harness now sees
  it; the 1 MB scenario prints the generated log's summary line on every sample.
- **Part 2.1:** on disk, the 1000 ms-child overhead drops by at least 5 ms and the pipe median does
  not regress by more than 3 ms; a tracked run opens no SQLite connection (the macOS loader test,
  and the database file not existing until the first read on Linux).
- **Part 2.2:** the 1 MB, 1000 ms-child overhead drops by at least 30 ms; no other scenario regresses
  by more than 3 ms; the counting proof test passes for both encodings; every `cold-start` sample
  still records a row with a positive input-token count.
- **Both 2.1 and 2.2:** `savings-baseline.json` unchanged; `AotParityTests` unchanged and green on
  every RID; still exactly two trim or AOT suppressions.
- **Part 3:** the Windows CI row is green with every assertion above; the installed `dtk.exe`
  measures under 30 ms for `--version` on the probe machine; the package is under 30 MB.

## Testing

Test-first, in the library test projects (the CLI integration tests are gated by CI):

- **`PendingRecordJournal`:** a write creates exactly one file that round-trips the record; two
  writers in parallel create distinct files; a fold commits every record once and deletes the
  files; a fold whose commit throws leaves the claimed directory, and the next fold commits it;
  a claimed directory whose id `committed` reports as known is deleted without a second commit; a
  truncated file is skipped, deleted and counted; a second fold while the lock is held waits and
  throws after the timeout with `wait`, and returns at once without it; `Count()` on a missing
  directory is zero.
- **`SqliteTracker`:** `RecordAsync` on a file data source creates no database file and one pending
  file; `GetSummaryAsync` then folds (rows present, files gone) and reports the same totals as a
  direct insert did before this change; `ResetAsync` empties the journal; `CleanupAsync` applies
  retention to folded rows; `WarmUpAsync` creates the directory, folds nothing under the threshold
  and starts a fold at it (threshold injectable); `:memory:` keeps inserting directly; every
  existing `SqliteTrackerTests` case still passes.
- **`AnsiStrip.EndsInsideEscapeSequence`:** false for no ESC, a complete CSI, a complete OSC ending
  in BEL or ST; true for `\x1b[31`, `\x1b]0;title` with no terminator; false when an unterminated
  OSC is followed by a later ESC (the OSC pattern cannot cross an ESC).
- **`ChunkedTokenCounter` proof:** for every corpus fixture, the generator's three tiers of every
  filter, and a hand-written set (runs of spaces, tabs and newlines; punctuation before newlines;
  `/` after newlines; CRLF; CSI and OSC sequences spanning lines; `<|endoftext|>`; text with no
  newline at all; empty text), with the minimum chunk size forced down to 64 chars so hundreds of
  cuts happen, `Finish(trailing)` then `TotalAsync()` equals `Estimate(Strip(text + trailing))` for both
  `Cl100kBase` and `O200kBase`. A failing input tightens the rule; the rule never loosens.
- **`CountingTextWriter`:** forwards lines and flushes to the inner writer unchanged; feeds the
  counter each line plus the inner `NewLine`; `NewLine` is the inner writer's.
- **`FilteredRunUseCase`:** with tracking on, the stdout sink handed to the runner is a
  `CountingTextWriter` over the session writer, the recorded input count equals the whole-text
  estimate of what the runner wrote plus its stderr, and the request carries the counter; with
  tracking off, the session writer is passed unchanged and the request carries none.
- **`PipeFilterUseCase`:** block reading yields the same raw text as `ReadToEndAsync` for an input
  larger than one block; with tracking on the recorded count equals the whole-text estimate.
- **`FilteredOutputPipeline`:** a request with a counter records its total; a counter whose
  estimate throws records nothing and leaves output and exit code intact; a request without one
  estimates as today.
- **`SqliteLoaderTests`:** the positive control runs `gain`.
- **`AotWarningLogTests`:** unchanged; fed the Windows publish log in CI.
- **Harness:** run by hand per the measurement protocol; the fourth scenario's summary-line check is
  its test.
- **Windows:** the CI assertions in section 3.

## Out of scope

- **The tokenizer load** (a precomputed rank table with a vendored BPE counter, or spooling text and
  counting at `gain` time). It is overlapped in real use; both fixes are the largest effort here for
  the smallest everyday gain. Revisit only if `pipe` from a file turns out to be a real workflow.
- **Approximate token counts.** Exactness is a product property.
- **A Spectre.Console.Cli bypass for `dotnet <sub>`** (at most 5 ms of the 13 ms startup) and
  **invariant globalization** (1.9 ms, changes number formatting).
- **ReadyToRun for the win-x64 managed payload** and **win-arm64**. Both are one property or one
  matrix row once the shim route is proven.
- **The `any` package's `gain` on glibc older than 2.34.** Its writes now succeed; its reads still
  need `libe_sqlite3.so`.
- **Reporting the journal in `doctor`.**

**Status:** Designed — see [the plan](../plans/2026-09-12-benchmark-project.md)
**Amended:** 2026-09-12 — split into Benchmarks.Corpus + Benchmarks to preserve the
repository's one-to-one test layering

## Problem

Nothing in this repository measures what dtk costs or what it delivers.

**1. The overhead dtk adds is unmeasured.** Every wrapped command pays for process start,
Spectre.Console.Cli configuration, the DI graph, a JSON config load, `AnsiStrip.Strip` over the
whole log, a filter pass, two tiktoken counts, and a SQLite write. That tax is the price of the
tool, and no number for it exists. A change that doubles it would ship unnoticed.

**2. The savings dtk delivers is unmeasured.** For a token killer, throughput is only half of
"performance": the product metric is the percentage of tokens removed. These regress
independently — a richer summary line costs tokens without costing milliseconds, and
`ExamplesBindingTests` would still pass, because it asserts that documented output *matches* the
filter, not that the filter still compresses.

**3. The existing corpus cannot expose scaling defects.** The 16 fixtures in
`tests/DotnetTokenKiller.Application.Tests/Fixtures/` run from 289 B to 7.7 KB. Real `dotnet
build` output on a large solution is 100 KB to several MB. Each filter runs regexes per line over
the full log; an accidental quadratic would be invisible at 2 KB and crippling at 1 MB.

**4. Optimization has no target.** `FilteredOutputPipeline.TrackIfEnabledAsync` tokenizes the full
raw output *and* the filtered output on every invocation, purely to record a statistic. On a 1 MB
build log that is roughly 250 000 tokens of tiktoken work in the user's critical path. Whether
that dominates the tax, or is noise beside process start, is currently unknowable.

## Solution

Two new projects under a new `/benchmarks/` solution folder in `DotnetTokenKiller.slnx`:
`DotnetTokenKiller.Benchmarks.Corpus`, a plain library owning the corpus, the savings engine and
the committed baseline; and `DotnetTokenKiller.Benchmarks`, the BenchmarkDotNet executable owning
the timing benchmarks.

The two dimensions get deliberately different enforcement, because they have different
determinism. Savings measurement is exactly reproducible — the same fixture through the same
filter yields the same filtered text and the same token count on every machine — so it is
hard-gated. Timings on shared GitHub runners vary by 10–30%, so they are reported, never gated.
Hard-gating a noisy metric produces flaky-red CI, which trains everyone to ignore it.

### Structure

The hard gate must run inside CI's existing `dotnet test` step rather than introduce a second
gating surface. So the assertions live in one new test class,
`tests/DotnetTokenKiller.Application.Tests/Benchmarks/SavingsBaselineTests.cs`. It sits beside
`ExamplesBindingTests`, which already binds a committed artifact to real filter output; this is
the same shape applied to a different artifact.

That test needs the corpus and the savings engine, which constrains how the projects are split.
This repository enforces a strict one-to-one onion layering: every test project references exactly
one src project and nothing else — Application.Tests to Application, Domain.Tests to Domain,
Infrastructure.Tests to Infrastructure, Cli.IntegrationTests to Cli. Four for four. Having
Application.Tests reference the BenchmarkDotNet executable would drag Infrastructure, Cli,
Spectre.Console and BenchmarkDotNet into it transitively, making it the one test project that can
see the whole stack, with nothing preventing a future Application test from depending on
Infrastructure.

The split follows the actual dependency need. The savings engine requires only Application — the
six filters, `TokenEstimator`, and the fixtures. Nothing in it touches Infrastructure or Cli; only
the benchmark classes do.

```
Benchmarks.Corpus  → Application                        (library, no benchmark machinery)
Benchmarks         → Benchmarks.Corpus, Infrastructure   (BenchmarkDotNet exe)
Application.Tests  → Application, Benchmarks.Corpus
```

Cli is not referenced by either. `CliConfigurator` is `internal`, with `InternalsVisibleTo` for
`Cli.IntegrationTests` alone, so the command tree cannot be measured in process at all; the
cold-start harness covers it out of process instead.

Application.Tests gains one reference, to a project that itself references only Application, so
the layering holds and BenchmarkDotNet stays out of the test run entirely.

Two rejected alternatives: source-linking the savings-engine files into Application.Tests with
`<Compile Link>`, which avoids the second project but compiles the same types into two assemblies;
and a hybrid xunit + BenchmarkDotNet executable using `GenerateProgramFile=false`, which keeps
everything in one project at the cost of depending on fragile csproj mechanics.

### Hermetic execution

`dtk` writes to a tracking database, a config file, and a tee directory under the user's home
directory. Benchmarks must never touch the real ones. All three are already overridable:
`DTK_DB_PATH` (`TrackerFactory.cs:24`), `DTK_CONFIG_PATH`, and `DTK_TEE_DIR`. Every benchmark and
the savings engine set all three to paths under a per-run temporary directory, deleted on
teardown. In-process benchmarks additionally use `NullTeeSession.Instance` where a session is
required.

### Corpus

Two sources behind one interface, serving different purposes.

**Real fixtures drive the savings baselines.** The 16 files in
`tests/DotnetTokenKiller.Application.Tests/Fixtures/` are already `EmbeddedResource` items in that
test project, read back through `GetManifestResourceNames`. Benchmarks.Corpus embeds the same
files via an `EmbeddedResource` glob pointing at that directory, so exactly one copy exists in git
and the two projects cannot drift.

**A seeded generator drives throughput and scaling.** `LogCorpusGenerator` emits realistic
MSBuild and VSTest line shapes — `CS####`/`CA####` diagnostics with file, line and column;
project-output arrows; restore chatter; `Passed!`/`Failed!` summaries — at three size tiers:

| Tier | Target size | Purpose |
|---|---|---|
| Small | ~2 KB | Matches today's fixtures; the common interactive case |
| Medium | ~50 KB | A realistic multi-project solution build |
| Large | ~1 MB | Scaling probe; where a quadratic becomes visible |

The generator uses a fixed `Random(seed)`, so a given tier is byte-identical on every machine and
every run. No large files are committed. A scaling curve across the three tiers is the artifact
that reveals non-linear behaviour; no single measurement can.

### Timing benchmarks

All in-process benchmarks carry `[MemoryDiagnoser]`. An allocation regression is as real as a time
regression and shows up earlier.

| Benchmark class | Measures | Regression it catches |
|---|---|---|
| `FilterBenchmarks` | `IOutputFilter.Apply` across all six filters × three tiers | A new regex or per-line pass turning a filter quadratic |
| `AnsiStripBenchmarks` | `AnsiStrip.Strip` on escape-laden and clean input | Whether the common no-escape case still allocates a full copy |
| `TokenEstimatorBenchmarks` | `TokenEstimator.Estimate` per tier, `cl100k_base` vs `o200k_base`, plus `TiktokenTokenizer.CreateForEncoding` cold load | Cost of the one-time vocab load, and of tokenizing twice per run |
| `PipelineBenchmarks` | `FilteredOutputPipeline.ProcessAsync` end to end with an in-memory writer | The combined real cost of one dtk invocation |
| `TrackerBenchmarks` | `SqliteTracker.RecordAsync`, and `GainReportUseCase` aggregation at 100 and 10 000 rows | `dtk gain` degrading as command history accumulates |
| `StartupBenchmarks` | DI container build, `JsonConfigProvider.LoadAsync` | Startup creep as registrations are added |

`TokenEstimator` caches tokenizers in a `private static readonly ConcurrentDictionary`
(`TokenEstimator.cs:10`) that cannot be cleared from outside the type. The cold-load cost is
therefore measured by benchmarking `TiktokenTokenizer.CreateForEncoding` directly, which performs
the same underlying work.

**Cold start is measured out of process.** A `cold-start` mode spawns the published `dtk`
binary as `dtk pipe build --exit-code 0` with a fixture on stdin. `PipeCommand`
(`PipeCommand.cs:35`) reads stdin and routes through `PipeFilterUseCase` into the same
`FilteredOutputPipeline`, so this exercises the entire real path — process start, JIT, DI, config
load, strip, filter, both token counts, SQLite write — without running a `dotnet build` at all.
That matters: per project history, the build-spawning CLI integration tests do not run reliably on
a developer machine, so no benchmark may depend on invoking a real build.

This mode runs 55 iterations, discards the first 5 as warmup, and reports **median and p95** over
the remaining 50. Fifty samples is the floor at which a p95 is worth printing at all; below that it
is one or two observations wearing a statistic's name. It is a hand-written timing loop rather than
a BenchmarkDotNet job, because BenchmarkDotNet would measure its own harness around the spawn;
median and p95 of raw wall-clock is the honest metric for process startup. The mode fails with an
explicit error if the `dtk` binary cannot be located — a benchmark reporting a fast number because
it measured nothing is worse than one that errors.

The executable dispatches on a single bare verb, so the three entry points stay
distinguishable: no arguments runs BenchmarkDotNet's `BenchmarkSwitcher`, `cold-start` runs the
out-of-process timing loop, and `update-baseline` regenerates the savings baseline. Any unknown
argument is rejected with a usage message listing the three.

### The savings gate

`Baselines/savings-baseline.json`, pretty-printed with stable key ordering so pull-request diffs
read cleanly. One entry per scenario, where a scenario is a fixture, the filter it belongs to, and
the exit code it represents. The counts below are illustrative shape only — the real values come
from the first generation run:

```json
{
  "tokenizer": "cl100k_base",
  "tokenizerDataVersion": "2.0.0",
  "scenarios": [
    {
      "id": "build/errors",
      "fixture": "dotnet_build_errors.txt",
      "filter": "build",
      "exitCode": 1,
      "rawBytes": 2517,
      "rawTokens": 612,
      "filteredTokens": 93,
      "savedTokens": 519,
      "savingsPercent": 84.8
    }
  ]
}
```

`rawTokens` and `filteredTokens` are asserted exactly. `savedTokens` and `savingsPercent` are
derived, stored for human readability, and compared with rounding tolerance. The gate covers
`cl100k_base` only, because that is what `DtkConfig` ships as the default; `o200k_base` is covered
on the timing side.

Regeneration is explicit and deliberate:

```bash
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
```

The assertion failure prints a table of only the drifted scenarios, each as `baseline → actual`
with the delta in percentage points, followed by that command. A filter change then surfaces in
review as `build/errors: 84.8% → 79.2%`, which is the entire purpose of the gate.

Exact matching was chosen over a tolerance band or a hand-set floor. A tolerance band lets slow
cumulative erosion pass silently and records no improvements; a floor is a smoke test, not a
regression detector. Exact matching costs a regeneration step whenever a filter changes on
purpose, and buys a reviewable line in the diff every time savings move.

**Dependency-bump interaction.** The `nuget-all` dependabot group bumps
`Microsoft.ML.Tokenizers.Data.Cl100kBase`. If its vocabulary data ever changes, every token count
shifts at once and the gate reddens on a dependency pull request. That is correct behaviour — the
published savings numbers really did move — but it would read as a filter regression. Recording
`tokenizerDataVersion` lets the failure message distinguish the two cases: a version change
reports "tokenizer data changed 2.0.0 → 2.1.0; counts shifted for all 16 scenarios — regenerate",
while unchanged data reports "3 scenarios drifted on unchanged tokenizer — investigate".

### Build friction

`TreatWarningsAsErrors` with `AnalysisLevel=latest-all` applies to both new projects. Three
conflicts, handled at the narrowest scope that works:

1. `GenerateDocumentationFile` is `true` in `Directory.Build.props`, so CS1591 demands XML
   documentation on every public member. The Benchmarks executable sets it to `false`, with a
   comment recording why — justified by the volume of public BenchmarkDotNet members that document
   nothing. Benchmarks.Corpus keeps it `true`: it is real consumed code with a public API that
   Application.Tests depends on, so it holds to the repository's documentation discipline.
2. `[Params]` declared as public mutable fields trips CA1051 and S1104. This is avoided
   outright rather than suppressed: BenchmarkDotNet accepts public properties with setters.
3. CA1822 fires on benchmark methods that touch no instance state. A scoped `.editorconfig`
   section for `benchmarks/DotnetTokenKiller.Benchmarks/**` handles the residue, each rule carrying
   a one-line reason rather than a blanket `NoWarn`. The scope deliberately excludes
   Benchmarks.Corpus, which has no reason to need the relaxation.

`BenchmarkDotNet` 0.15.8 is added to `Directory.Packages.props`, per the repository's central
package management. Its highest `lib` target is `net8.0`, consumed without issue from `net10.0`.

**Toolchain risk.** BenchmarkDotNet's default toolchain generates and compiles a throwaway project
targeting the benchmark project's framework. A BenchmarkDotNet release predating a given TFM
cannot generate a valid project for it, and the failure appears only at run time, not build time.
Whether 0.15.8 handles `net10.0` is therefore the first thing the implementation verifies, with a
single trivial benchmark, before any real benchmark code is written. If it fails, the fallback is
`[SimpleJob(RuntimeMoniker.Net80)]` or an in-process toolchain, and the savings gate — which has
no BenchmarkDotNet dependency at all — is unaffected either way.

BenchmarkDotNet artifacts are written to `artifacts/benchmarks/`; `.gitignore` already covers both
`artifacts/` (line 93) and `BenchmarkDotNet.Artifacts/` (line 88), so no change is needed there.

### CI wiring

- **The gate** rides the existing `dotnet test` step in `ci.yml`. No YAML change.
- **Both new projects compile on every pull request** as part of the solution build. This is
  deliberate: benchmark suites overwhelmingly die by quietly failing to compile.
- **Timings** get `.github/workflows/benchmarks.yml`, `workflow_dispatch` only, ubuntu-only, with
  an optional filter-glob input, uploading BenchmarkDotNet's `*.md`, `*.csv` and `*.json` results
  as artifacts. Never on pull requests: no gating, no noise, no runner minutes.

Two exclusions keep the new projects from distorting existing reporting:

- `sonarqube.yml`: add `benchmarks/**/*` to both `sonar.exclusions` and
  `sonar.coverage.exclusions`. Otherwise a project with no tests covering it drags the reported
  coverage percentage down.
- `stryker-config.json`: exclude `benchmarks/**` from mutation. Application.Tests referencing
  Benchmarks.Corpus would otherwise pull the generator and savings engine into Stryker's mutation
  set, where surviving mutants in a corpus generator are noise rather than signal.

### Testing the benchmark code

The generator and the savings engine are real logic, and get real tests in Application.Tests:

- The same seed produces byte-identical output.
- Each tier lands within tolerance of its target size.
- Generated logs actually parse through their filters and yield non-trivial savings. This is the
  load-bearing one: without it, a generator drifting into unrealistic line shapes would silently
  make every throughput number meaningless while all other tests stayed green.

## Expected findings

The design is judged by whether it surfaces actionable work. The four candidates it should
immediately confirm or refute:

1. **Double tokenization.** `TrackIfEnabledAsync` tokenizes the full raw log and the filtered
   output on every invocation to record one statistic. If `TokenEstimatorBenchmarks` shows this
   dominating `PipelineBenchmarks`, a cheaper raw-side estimate or sampling is a large, safe win.
2. **Tokenizer cold load.** The one-time `cl100k_base` vocabulary load may dominate a small-log
   invocation outright. Visible only in the cold-start numbers.
3. **`AnsiStrip` on clean input.** Most piped output contains no escape sequences. If `Strip`
   allocates a full copy regardless, a fast path is free.
4. **Per-invocation SQLite open**, and `dtk gain` aggregation cost as history accumulates.

## Out of scope

- Benchmarking `dtk dotnet build` against real sample apps. Slow, noisy, requires a warm NuGet
  cache, and does not run reliably on a developer machine.
- Gating timings in CI, at any tolerance.
- Historical trend storage or a published benchmark dashboard. Regenerated baselines in git
  history are the trend record for savings; timing runs are on-demand artifacts.
- Benchmarking the integration and hook-generation code paths, which run once at install time and
  are not in any hot path.

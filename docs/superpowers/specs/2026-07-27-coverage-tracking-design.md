**Status:** Implemented — see [the plan](../plans/2026-07-27-coverage-tracking.md)
**Implements:** §1 and §3 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

## Problem

Two things dtk does are invisible to dtk itself.

**Passthrough runs (§1).** Any dotnet subcommand outside the canonical five falls through
`src/DotnetTokenKiller.Cli/Program.cs:24-28`. That branch runs the child with inherited stdio and
returns its exit code — no filtering, and no tracking. So dtk cannot answer the only question that
matters when choosing the next filter to write: *which uncovered command costs the most tokens?*
The gap analysis nominates `list package` as the leading candidate, but that is a guess, and the
tool that should be able to check it is the one that cannot.

**Degraded filtered runs (§3).** `FilteredRunUseCase.ApplyFilterSafelyAsync` catches every filter
exception and silently returns the raw output. `BuildRawTailFallback` fires when a failed command's
filter produces nothing, and sets a local `usedRawTailFallback` that is never persisted. Both paths
mean a filter is not earning its keep, and neither leaves a trace. rtk exposes exactly this via
`gain --failures`.

Both gaps are the same question — *where is dtk not earning its keep* — so they share one schema
change and one report surface rather than two of each.

## Solution

Add one dimension, `RunOutcome`, to the tracking record.

| Outcome | Meaning | In savings math? |
|---|---|---|
| `Filtered` | Filter ran and produced output | yes |
| `RawTailFallback` | Filter returned empty on a failed command; last 40 lines emitted | yes |
| `FilterFaulted` | Filter threw; raw output emitted | yes |
| `PassthroughMeasured` | No filter; output streamed through and counted | no |
| `PassthroughUnmeasured` | No filter; output not captured | no |

`dtk gain` continues to aggregate only the first three, so today's dashboard numbers do not move.
A new `dtk gain --coverage` reports all five ranked by total raw tokens descending — the
"biggest uncovered prize first" view §1 asks for, with §3's degraded-run visibility folded in.

`--coverage` honours the existing `--days`, `--project`, and `--command` filters, and composes with
`--json`. It does not compose with `--export`: CSV export stays a dump of raw records, gaining an
`outcome` column so the same information is available there without a second export mode. Adding
`outcome` changes `GainCommand.CsvHeader`, which is a pinned constant with existing tests — those
expectations must be updated deliberately, not regenerated.

`PassthroughUnmeasured` rows carry zero tokens and would otherwise tie at the bottom of the
ranking, hiding a frequently-run command that nobody has measured yet. The coverage report
therefore sorts by total input tokens descending, then by run count descending, so the most-run
unmeasured commands surface at the top of the zero-token block rather than in arbitrary order.

`FilterFaulted` and `RawTailFallback` are *nearly* always mutually exclusive: when the filter
throws, `filtered` becomes the raw stripped text, which is normally non-empty, so the raw-tail
branch (which requires `string.IsNullOrWhiteSpace(filtered)`) does not also fire. But when the
captured raw output is itself empty or whitespace on a failed command, both conditions hold at
once — the filter faults *and* `filtered` is whitespace. It is the ordering in the outcome
derivation, faulted checked first, that resolves this in favour of `FilterFaulted`, and that
ordering is deliberate rather than incidental.

## Components

| Layer | Change |
|---|---|
| Domain | `RunOutcome` enum; `CommandRecord.Outcome`; `PassthroughSubcommands`; `CoverageSummary` / `CoverageDetail`; `ITracker.GetCoverageAsync` |
| Infrastructure | `outcome` column + migration; `GetSummaryAsync` passthrough exclusion; `ICommandRunner.RunStreamedAsync` |
| Application | `PassthroughRunUseCase`; `FilteredRunUseCase` sets `Outcome`; `GainReportUseCase.GetCoverageAsync` |
| Cli | `Program.cs` passthrough wiring; `--coverage` flag; `GainDashboardRenderer.RenderCoverage`; `outcome` in the CSV export |

### `PassthroughSubcommands`

A map from subcommand to the set of second tokens that are meaningful for it — not a flat list,
because `list` alone cannot distinguish `list package` (worth filtering) from `list reference`
(not).

```
list     → package, reference
ef       → migrations, database, dbcontext
tool     → list, restore, install, update
sln      → list, add, remove
workload → list, search
nuget    → locals, push, verify
```

A second token absent from its subcommand's value set is dropped, so `dotnet list ./src/Foo.csproj`
records as `list` and never as a path. That is the leak guarantee: a file path, a package name, or
a connection string can never become a `command` value. It is enforced by test, not by convention.

The **measurable** set — subcommands whose output is batch and therefore safe to capture — is
`publish, pack, list, tool, workload, sln, msbuild, ef`. `run`, `watch`, `new`, and every
unrecognised subcommand stay on the inherited-stdio path and record as `PassthroughUnmeasured`.

### `RunStreamedAsync`

A third runner method alongside `RunCapturedAsync` and `RunPassthroughAsync`. It redirects stdout
and stderr, echoes each line to the console as it is read, and accumulates the text for tokenizing
after exit. The user sees output at the same pace as today; the only behavioural change is loss of
ANSI colour, because the child sees a pipe rather than a tty.

Both streams must be read concurrently, as `RunCapturedAsync` already documents — sequential reads
deadlock on large output.

## Data flow

```
dtk dotnet publish -c Release
  → ArgumentPreprocessor.IsPassthrough → true
  → load config (single JSON read)
  → tracking disabled?  → RunPassthroughAsync, no record          [today's behaviour exactly]
  → measurable?         → RunStreamedAsync: echo each line live, accumulate
                          → on exit: tokenize → record PassthroughMeasured
  → not measurable      → RunPassthroughAsync → record PassthroughUnmeasured (0 tokens)
```

The config read must happen before the child starts, because `tracking.enabled = false` is the
escape hatch from capture as well as from recording — a user who turns tracking off gets the
current inherited-stdio path back, colour included. No new config knob is added for this.

The cost this adds ahead of the child is one small JSON file read. The 117 ms startup gap in §9 is
DI container plus Spectre construction, which this path still skips entirely; `Program.cs`
constructs `JsonConfigProvider` and `SqliteTracker` directly and hands them to
`PassthroughRunUseCase`, keeping the decision logic in Application where it is testable and only
the wiring in `Program.cs`.

## Error handling

Recording failures stay swallowed, exactly as `FilteredRunUseCase.TrackIfEnabledAsync` does today.
A broken or locked tracking database must never change the exit code of a `dotnet publish`. The
child's exit code is captured before any recording is attempted, so a throw in the record path
cannot alter what dtk returns. A config-load failure falls back to `DtkConfig.Default`, matching
existing behaviour.

The `outcome` column is added by a pragma-guarded `ALTER TABLE`, following the pattern the `success`
column already established in `SqliteTracker.InitializeSchemaAsync`, including its
`duplicate column` catch for the concurrent-initializer race. Existing rows default to `Filtered`,
which is correct: every row written before this change came from a filtered run.

**Accepted trade-off.** `RunStreamedAsync` accumulates the full output in memory with no cap,
consistent with `RunCapturedAsync`'s existing unbounded `ReadToEnd`. Capping only the new path
would make passthrough token counts silently non-comparable with filtered ones, defeating the
ranking this feature exists to enable. If memory pressure becomes real, it should be addressed for
both paths together.

## Testing

**Domain — the leak guarantee.**
`list package` → `"list package"`; `list ./src/App.csproj` → `"list"`; an unrecognised subcommand →
first token only. The path case is the one that proves argv fragments cannot reach the database.

**Infrastructure — the migration and the exclusion.**
A database created without the `outcome` column gains it on open, and its existing rows read back
as `Filtered`. `GetSummaryAsync` totals are identical before and after passthrough rows are
inserted — this is the "today's numbers do not move" guarantee, and it fails loudly if the
exclusion is ever dropped. Coverage ordering is by total input tokens descending, then run count
descending — the tiebreak needs its own case, since every `PassthroughUnmeasured` row has zero
tokens and a sort without it would order them arbitrarily.

**Application — the outcomes.**
`FilteredRunUseCase` records `Filtered`, `RawTailFallback`, and `FilterFaulted` in their respective
cases. A filter that throws records `FilterFaulted` and still returns the child's exit code.
`PassthroughRunUseCase` records `PassthroughMeasured` with non-zero input tokens for a measurable
subcommand and `PassthroughUnmeasured` with zero for the rest.

**Cli integration — end to end.**
A real unfiltered subcommand run against `samples/` produces a `PassthroughMeasured` row with
non-zero input tokens, and its output reaches the console.

## Out of scope

Filters for any of the newly-tracked subcommands. This change exists to make that choice
data-driven; picking the first one is the next piece of work, informed by what `dtk gain
--coverage` reports after this ships.

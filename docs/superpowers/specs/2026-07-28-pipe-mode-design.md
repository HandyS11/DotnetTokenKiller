**Status:** Implemented — see [the plan](../plans/2026-07-28-pipe-mode.md)
**Implements:** §2 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

## Problem

dtk can only filter what it launches itself. Every filter is already a pure
`Apply(string rawOutput, int exitCode)`, but the only way to reach one is through
`FilteredRunUseCase`, which starts the child process. So three real cases stay unfiltered:

- **CI logs.** A build that ran on a GitHub Actions runner produces the noisiest output dtk will
  ever see, and dtk was not there to run it.
- **Invocations the hook missed.** The PreToolUse hook rewrites `dotnet …` → `dtk dotnet …`, but
  anything invoking the SDK indirectly — a shell script, a Makefile, an MSBuild `Exec` — slips past
  it. `dtk gain --coverage` can now *see* those runs; it still cannot reduce them.
- **Output already on disk.** A log the agent saved before deciding it needed condensing.

rtk covers this with `rtk pipe --filter <name>`. The payoff is broad and the change is small,
because the filters need nothing added to them.

## Solution

A top-level `dtk pipe` command that reads raw output from stdin, applies a named filter, and writes
the condensed result to stdout.

```
dtk pipe <subcommand...> [--exit-code <n>] [-v] [--show-log] [--quiet]
```

The positional tokens are resolved through `DotnetSubcommands.TryMatch`, the same matcher CLI
routing uses. `dtk pipe list package` therefore works without special-casing the one two-token
name, and a subcommand added later becomes pipeable with no change to this command.

### The exit code is passed, never inferred

`IOutputFilter.Apply` documents `exitCode` as "the sole source of truth for the success/failure
verdict". A shell pipe destroys that: in `dotnet build | dtk pipe build`, `$?` is dtk's own status,
not the compiler's. `--exit-code <n>` supplies it explicitly and defaults to `0`.

Inferring the verdict by scanning the text for `error CS####` or `Build FAILED` was rejected. That
is precisely the content-sniffing verdict logic removed in §"test filter verdict invariant"
(commit `1c8981a`), and reintroducing it here would let a filter contradict reality in the one
place the user cannot check.

The honest consequence, stated plainly because callers will hit it: **bash cannot give the
right-hand side of a pipeline its predecessor's status.** `${PIPESTATUS[0]}` is not readable from
within the pipeline that sets it. So the two usable shapes are:

```bash
dotnet build 2>&1 | dtk pipe build                                 # verdict assumed success
dotnet build > b.log 2>&1; dtk pipe build --exit-code $? < b.log    # accurate verdict
```

The plain pipe is the ergonomic form and is honest only about success; the redirect form is
correct. Documentation leads with the second for CI and the first for interactive use.

`dtk pipe` **returns `--exit-code` as its own exit status**, so a CI step wrapping a failed build
still fails red. Without that propagation the command would silently turn every red build green —
a worse failure than not having pipe mode at all.

### Tracking: outcome and source are orthogonal

Piped runs are recorded, and are distinguishable from runs dtk executed itself. They are *not*
distinguished by a new `RunOutcome` value.

`RunOutcome` answers **how the output was produced** — filtered, raw-tail, faulted. Pipe answers
**where the input came from**. Folding the second into the first needs `FilteredFromPipe` ×
`RawTailFallbackFromPipe` × `FilterFaultedFromPipe` to stay complete, and adding only the clean
case would misattribute exactly the faulted piped runs most worth seeing.

So `RunOutcome` is untouched, and `CommandRecord` gains a `RunSource` (`Run` | `Pipe`, default
`Run`) persisted to a `source` column. Every outcome stays reportable per source, and the enum
does not grow combinatorially the next time an input path is added.

Savings from piped runs count in `dtk gain` totals — they are real reductions of real text. The
separation exists so the §6 adoption question stays answerable: a CI log piped in is *not* evidence
the hook is working, and a report that conflates the two cannot tell you so.

`dtk gain --coverage` surfaces the dimension as a `Source` column, so coverage rows group by
command × outcome × source. The visible consequence is that a command both executed and piped now
occupies two rows rather than one; that is the point, and the existing sort (total input tokens
descending, then run count descending) still applies. `--json` and the CSV export carry `source`
for the same reason. Adding it to CSV changes `GainCommand.CsvHeader`, a pinned constant with
existing tests — those expectations are updated deliberately, not regenerated.

The default `dtk gain` dashboard is unchanged: it aggregates across sources, and its totals do not
move.

## Components

| Layer | Change |
|---|---|
| Domain | `RunSource` enum; `CommandRecord.Source`; `CoverageDetail.Source` |
| Infrastructure | `source` column + `EnsureColumnAsync` migration; `source` in reads and coverage grouping |
| Application | `FilteredOutputPipeline` (extracted); `PipeFilterUseCase`; `FilteredRunUseCase` delegates |
| Cli | `PipeCommand` + settings; `CliConfigurator` registration; `TextReader` DI; completion candidate; `Source` in the coverage renderer and the CSV export |

`completion` gains `pipe` as a top-level candidate only. Completing its positional subcommand is
not implemented, consistent with the existing note that multi-token subcommand completion is not.

### `FilteredOutputPipeline`

`FilteredRunUseCase.RunAsync` currently does two jobs: it runs a process, then post-processes what
the process produced. Pipe mode needs only the second. Steps 3–11 of that method — ANSI strip,
`ApplyFilterSafelyAsync`, tee, raw-tail fallback, outcome derivation, glyph normalisation, write,
track — move verbatim into `FilteredOutputPipeline`, which both entry points call.

```csharp
Task<int> ProcessAsync(FilteredOutputRequest request, CancellationToken cancellationToken)

sealed record FilteredOutputRequest(
    IOutputFilter Filter,
    string RawOutput,
    int ExitCode,
    string CommandSlug,
    string DisplayCommandLine,
    RunSource Source,
    OutputOptions Options,
    long StartTimestamp);
```

`OutputOptions` is a record bundling the three display flags both callers already accept —
`VerbosityLevel`, `ShowLogHint`, `Quiet` — including the existing rule that `Quiet` forces the
other two off. Bundling them puts that precedence rule in one place instead of at each call site.

The eight values travel as a `FilteredOutputRequest` rather than as positional parameters because
SonarAnalyzer's S107 fires above seven, and `TreatWarningsAsErrors` turns that into a build failure.

`StartTimestamp` is a `Stopwatch.GetTimestamp()` value taken by the *caller*, not by the pipeline:
the run path's tracked duration must include the child process's time, while the pipe path's must
cover only reading stdin and filtering.

| Caller | Raw text from | Slug from | Source |
|---|---|---|---|
| `FilteredRunUseCase` | `commandRunner.RunCapturedAsync` | `ResolveCommandSlug(command, args)` | `Run` |
| `PipeFilterUseCase` | `TextReader.ReadToEndAsync` | canonical name from `TryMatch` | `Pipe` |

This is the §7 argument applied to behaviour rather than to a list: the raw-tail rule, the
faulted-wins ordering, the glyph rules, and the tracking shape now exist once and cannot drift
between the two paths.

`FilteredRunUseCase` keeps its public signature. It shrinks to: load config, optionally echo the
command line, run the process, delegate. `PipeFilterUseCase` takes an injected `TextReader`,
symmetric with the `TextWriter output` the use cases already receive, so stdin is fake-able in
tests with no console involved.

`displayCommandLine` is a parameter rather than something the pipeline derives, because the two
callers disagree about what it is: the run path has a real command line to quote in
`BuildRawTailFallback`, while the pipe path reconstructs a nominal `dotnet <subcommand>` from the
canonical name.

## Data flow

```
dotnet build > b.log 2>&1; dtk pipe build --exit-code $? < b.log
  → Console.IsInputRedirected false?  → error, exit 1               [never block on a tty]
  → TryMatch(["build"])              → "build", else error + exit 1
  → resolve IOutputFilter by key from the canonical name
  → read stdin to end
  → FilteredOutputPipeline.ProcessAsync(filter, raw, exitCode, "build", "dotnet build", Pipe, …)
      → strip ANSI → apply filter (safely) → tee → raw-tail if empty and exitCode != 0
      → derive outcome → normalise glyphs → write to stdout → record (source = Pipe)
  → return exitCode
```

The filter is resolved from the keyed DI registrations by canonical name, the same keys
`FilterKeys` aliases — so pipe mode cannot address a filter that routing cannot, or vice versa.

## Error handling

| Case | Behaviour |
|---|---|
| Unrecognised subcommand | Error listing `DotnetSubcommands.Ordered`, exit 1 |
| stdin attached to a terminal | Error, exit 1 — never block awaiting EOF |
| Empty stdin, `--exit-code 0` | Filter's normal empty-input output |
| Empty stdin, non-zero `--exit-code` | Existing raw-tail fallback: `✗ dotnet <sub> failed (exit n)` |
| Filter throws | Raw output emitted, recorded `FilterFaulted` with source `Pipe` |
| Tee or tracking throws | Swallowed, exactly as today |

The terminal-stdin guard lives in the Cli layer, where `Console.IsInputRedirected` belongs;
`PipeFilterUseCase` itself only knows about a `TextReader` and stays testable without it.

Tee behaviour is unchanged and still governed by `config.Tee.Mode`, so pipe mode writes a log only
where a filtered run already would. `--show-log` is supported for consistency. A CI job that does
not want files on disk turns tee off through existing configuration rather than through a flag
unique to this command.

## Testing

**Application — the extraction is behaviour-preserving.** The rules currently proven through
`FilteredRunUseCase` move to `FilteredOutputPipeline` tests, including the deliberate ordering
where `FilterFaulted` beats `RawTailFallback` when both conditions hold at once
(`RunAsync_PrefersFilterFaulted_WhenBothConditionsOverlap`). That case must stay pinned through the
move — it is the one rule in the pipeline that is deliberate rather than incidental.
`FilteredRunUseCase` tests reduce to: the process runs, and the pipeline is called with what it
captured.

**Application — pipe specifics.** Multi-token selection (`["list", "package"]` → the list-package
filter). Unrecognised name rejected without consuming stdin. `--exit-code` reaches
`Apply` unchanged and is returned as the command's status. Empty stdin with a non-zero code
produces the raw-tail fallback.

**Infrastructure — the migration.** A database created without `source` gains it on open, and its
existing rows read back as `Run`. This is the counterpart to the `outcome` migration test and
follows the same pragma-guarded pattern, `duplicate column` race included. Coverage grouping splits
one command into two rows when it has both a `Run` and a `Pipe` record, and `GetSummaryAsync`
totals are unaffected by the split — the "default dashboard does not move" guarantee.

**Cli — the guard and the surface.** Non-redirected stdin exits 1 without blocking. A piped sample
build log produces a `source='pipe'` row with non-zero input tokens. Help snapshots accepted for
the new command.

## Out of scope

- **Project-local extensible filters** (the open half of §3). Pipe mode makes the compiled filters
  reachable from more places; it does not make new ones authorable without a PR.
- **Auto-detecting which filter to apply** from the content of stdin. The caller knows what they
  ran; guessing would reintroduce the content-sniffing this design rejects for the verdict.
- **`dtk log` / `dtk last`** (§5), the other item the gap analysis nominates as next. Independent
  of this one and unblocked by it.

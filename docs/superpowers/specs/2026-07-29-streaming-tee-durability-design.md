**Status:** Implemented — see [the plan](../plans/2026-07-29-streaming-tee-durability.md)
**Implements:** §4 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

## Problem

The tee log is written after the child process exits. If dtk does not reach that point, there is no
log — and the runs that fail to reach it are exactly the runs whose output is most wanted.

Two findings sharpen the gap analysis's original statement of this.

**§4 is partly stale.** It claims output is fully buffered. `RunStreamedAsync` already exists
(`ProcessCommandRunner.cs:72`) and streams line by line while accumulating; the coverage-tracking
work added it, and `PassthroughRunUseCase` uses it for measurable passthrough runs. What is still
lost is narrower and more specific:

| Path | Runner | Tee | On kill |
|---|---|---|---|
| Filtered (`build`, `test`, …) | `RunCapturedAsync` — buffered `ReadToEnd` | written after exit | output and tee both lost |
| Passthrough, measurable | `RunStreamedAsync` — streams | **never written at all** | tracking lost |
| Passthrough, interactive | `RunPassthroughAsync` | never written | — |

**dtk's cancellation token never fires in production.** `Program.cs:57` calls `app.RunAsync(args)`
with no token, and Spectre.Console.Cli 0.55 does not reference `CancelKeyPress`, so the token is
`CancellationToken.None`. Every `OperationCanceledException` path in `ProcessCommandRunner` is
reachable only from tests.

The consequence decides the design. Ctrl-C and an agent tool-call timeout **kill the dtk process
outright**; no in-process handler runs. Durability therefore cannot come from a cancellation
handler, however carefully written. It can only come from the log already being on disk when the
kill arrives.

## Solution

Invert the tee from write-after to write-during, expressed as a session with a lifetime.

```csharp
// Domain
public interface ITeeSession : IAsyncDisposable
{
    TextWriter Writer { get; }                        // TextWriter.Null when tee is off
    Task<string?> FinalizeAsync(int exitCode, CancellationToken ct);
}

public interface ITeeService
{
    Task<ITeeSession> BeginAsync(string commandSlug, TeeLogHeader provisional, CancellationToken ct);
    Task DeleteLogsAsync(CancellationToken ct);
}
```

`BeginAsync` creates the file 0600 and writes the header before the child starts; it does not
rotate; see below. Lines append as they arrive. `FinalizeAsync` overwrites the status and exit
fields in place, or deletes the file when the retention rules say it should not be kept, and
rotates in either case.

Whatever has been flushed when dtk dies is what survives. That is the whole guarantee, and it holds
under `SIGKILL`, which nothing else here would.

### Scope

In: the filtered path and the measurable passthrough path — the two that already capture output.
Teeing passthrough also closes the leftover flagged in the `dtk log` design, where passthrough runs
produce no log and are therefore unreachable by `dtk log`.

Out: interactive passthrough (`run`, `watch`), whose stdio is attached straight to the terminal.
Redirecting it through dtk would break colour, TTY detection, and interactive stdin for precisely
the commands that need them.

Also out: a `SIGINT`/`SIGTERM` handler. It would make Ctrl-C finalize cleanly and record a tracking
row, but it is strictly an addition on top of this — under `SIGKILL` the fallback is the partial
file either way. Deferred rather than rejected.

§4 lists a second payoff, a live progress signal on multi-minute builds. That is not pursued here.
On the filtered path, echoing raw lines to the terminal as they arrive is exactly the token cost dtk
exists to remove, so the two goals are in direct opposition; any progress signal worth having would
have to be something other than the raw output, which is a separate design.

`dtk pipe` gains no durability benefit; it reads stdin to completion before it has anything to
write. It moves to the session API for uniformity only (see below).

### The decision inversion

Incremental writing must start before the exit code and the total size are known — but
`TeeMode.Failures` needs the exit code, and the 500-character guard needs the size. Both are
post-hoc decisions that now have to be made in advance.

Resolution: **write provisionally, reconcile on exit.** Always open and append. On clean exit apply
today's rules — if `TeeMode.Failures` and exit 0, or the body is below the size guard, delete the
file. A run that completes normally produces the file it produces today, apart from stream
interleaving (below). A run that is killed leaves its partial log behind, because the reconciliation
that would have deleted it never runs.

One deliberate change of unit: today's guard is `rawOutput.Length < 500`, measured in UTF-16 chars,
because the whole string is in hand. A streaming session counts bytes as it writes them, so the
guard becomes 500 UTF-8 bytes. The two agree for ASCII, which is essentially all build output; the
alternative is retaining a char count solely to reproduce a threshold whose exact value carries no
meaning.

The cost is a file that briefly exists which today's rules would not have kept. It is created 0600
in the same 0700 directory, so this widens no exposure.

### Header v2

A provisional log has no exit code. `# exit:` is parsed with `int.TryParse` and `TryParse` returns
`false` on anything else, so a v1 header cannot express "not finished yet" — it would fail to parse
entirely and the log would list as project-unknown.

`status` and `exit` move last, adjacent and fixed-width, so finalizing is one contiguous overwrite
at a known byte offset:

```
# dtk-log v2
# command: dotnet build DotnetTokenKiller.slnx
# cwd: /home/cloudcli/projects/DotnetTokenKiller
# source: Run
# utc: 2026-07-29T10:11:12.1234567+00:00
# status: running·                       ← 8 chars: "running·" / "complete"
# exit:   -··········                    ← 11 chars: widest int is -2147483648
---
```

Values are `TrimEnd`'d on parse. v1 files parse as `Complete`, so logs written before this change
keep working.

`TeeLogHeader.ExitCode` becomes `int?`, with the invariant **`Status == Running ⟺ ExitCode is
null`** enforced in the record's constructor. Without it, `status` and `exit` are two
representations of one fact and can disagree on disk.

The timestamp's meaning shifts from "when the run completed" to "when the run started". The
filename is built from it, so ordering is unaffected.

### `dtk log` shows incomplete runs

An incomplete log lists like any other, with `incomplete` in place of the exit code and a note above
the body when viewed. Hiding it behind a flag would conceal the log in exactly the situation it was
written for; showing it unmarked would let a truncated build read as a complete one, so the user
would conclude the build produced no further output rather than that dtk was killed.

## Architecture

The tee becomes a sink. `RunStreamedAsync` already takes two `TextWriter` sinks, so `ICommandRunner`
does not change at all and the streaming and deadlock-avoidance plumbing is reused as written.

Two alternatives were rejected. Giving `ICommandRunner` a tee path drags tee config, rotation, and
retention into the process runner and makes it untestable without a filesystem. Decorating
`ICommandRunner` needs the command slug, the `RunSource`, and whether the caller wants terminal
echo — none of which the signature carries, so it forces signature changes anyway while hiding
where the file gets written.

**`TeeAndHintAsync` is removed.** `PipeFilterUseCase` has no stream to follow, but it can use the
same session API: begin, write the whole text, finalize. One write path instead of two, so the
header format and the retention rules cannot drift between them.

**Tee off** returns a null session (`TextWriter.Null`, `FinalizeAsync` → `null`), so no call site
needs a branch.

### Components

| Layer | Change |
|---|---|
| Domain | `TeeLogStatus { Running, Complete }`; `TeeLogHeader` → v2, `ExitCode` → `int?`, invariant enforced; `TryParse` accepts v1 as `Complete`; `ITeeSession` (new); `ITeeService` reshaped |
| Infrastructure | `FileTeeSession` (new) owns the `FileStream`, field offsets, body-byte counter, and retention decision; `FileTeeService` becomes a factory |
| Application | `FilteredRunUseCase` swaps `RunCapturedAsync` for `RunStreamedAsync`; `FilteredOutputPipeline` finalizes the session instead of calling tee; `PassthroughRunUseCase` gains a session; `FanOutTextWriter` (new); `PipeFilterUseCase` moves to the session API |
| Cli | `TeeLogRenderer` / `LogCommand` render `incomplete` and the truncation note |

`RunCapturedAsync` stays — `DoctorUseCase` still uses it.

### Data flow

**Filtered run.** `FilteredRunUseCase` builds the provisional header and calls `BeginAsync`, so the
file exists with its header before the child starts. It passes the session writer as *both* sinks to
`RunStreamedAsync`; lines are ANSI-stripped per line and serialized with a `SemaphoreSlim`, since
the two pumps run concurrently. The pipeline filters as today, then calls `FinalizeAsync(exitCode)`
for the hint. Finalize runs **before** the raw-tail fallback is built, preserving today's ordering
so the hint still embeds in the fallback text.

**Passthrough, measurable.** Sinks become `FanOut(terminal ← raw line, tee ← stripped line)`,
preserving whatever colour reaches the terminal today. The streamed path is taken when **tracking is
enabled or tee is on** — keying it on tracking alone would silently produce no passthrough log for a
user who has tracking off and tee on. The hint is not printed, matching passthrough's existing
silence; the log is reached through `dtk log`.

### Consequence: tee body ordering changes

Today the tee receives `StdOut + StdErr` concatenated. Streamed, it receives them interleaved by
arrival. This is more faithful to what actually happened, but the tee body is no longer
byte-identical to what the filter sees.

## Error handling

The rule that tee failures never surface to the user is unchanged, but streaming introduces a way to
violate it that the old design could not: **a sink that throws inside `PumpAsync` propagates and
fails the whole command.** The session writer therefore catches IO failures, latches itself broken,
and stops writing; the run continues unaffected. `BeginAsync` returns the null session on any
failure, and `FinalizeAsync` swallows and returns `null`.

`MaxFileSizeBytes` stops the append at the cap, cut on a rune boundary as `TruncateToUtf8Bytes` does
today. The in-memory accumulation the filter needs is untouched, so capping the file cannot degrade
filtering.

Rotation does not run when the session opens. It runs inside `FinalizeAsync`, after the keep/discard
decision, on both branches: opening a file for every run and rotating immediately would let a
successful run evict an older *kept* log before this run's own fate was known — under the default
`TeeMode.Failures` with `MaxFiles = 20`, twenty successful builds in a row would silently wipe every
stored failure log. A run killed before `FinalizeAsync` runs leaves its file unrotated, uncounted
against `MaxFiles` until some later run finalizes and sweeps it up alongside its own decision.

A killed run leaves a log but no tracking row, so `dtk gain --coverage` undercounts killed runs.
Closing that requires the signal handler deferred above.

## Testing

The core guarantee is testable without killing anything: **abandon a session without finalizing** —
precisely what a `SIGKILL` leaves behind — then assert `ListAsync` reports it `Running` and
`ReadBodyAsync` returns what was flushed.

- `TeeLogHeader` — v2 round-trip; v1 parses as `Complete`; fixed-width values parse after `TrimEnd`;
  the `Running ⟺ null` invariant rejected when violated; unknown keys still rejected
- `FileTeeSession` — header present before any body; finalize overwrites in place with total file
  length unchanged; deletes on `(Failures, exit 0)` and on a body under the 500-byte guard; truncates
  at the cap on a rune boundary; a broken stream never throws
- `FilteredOutputPipeline` — finalize precedes the raw-tail fallback, so the hint still embeds
- Regression — a normally-completing filtered run's user-visible output is byte-identical to today
- One out-of-process test: launch a slow `dtk dotnet build`, `SIGKILL` it, assert a readable log
  survives. This is the only test that proves the feature end to end, and also the flakiest thing in
  the suite; guarded to Linux and kept to a single case.
- Updates to `FilteredRunUseCaseTests`, `PipeFilterUseCaseTests`, `LogViewUseCaseTests`, and
  `LogIntegrationTests` for the new seam

**Status:** Implemented — see [the plan](../plans/2026-07-28-dtk-log.md)
**Implements:** §5 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

## Problem

dtk condenses a failed build down to the lines that matter. When those lines are not enough, there
is no way to get the rest back.

Tee files hold the full output, but reaching one is harder than it looks:

- The path is printed only with `--show-log`, and only when the raw output exceeded 500 characters.
  An agent that did not pass the flag on the run that failed cannot retroactively obtain it.
- `DotnetListPackageFilter` already tells users to "use `--show-log` for full output" — advice that
  only works if you knew to ask *before* running.
- The files are named `{unixMillis}_{guid}_{slug}.log` in a directory shared by every repo on the
  machine. Finding the right one by hand means sorting a directory listing by an epoch timestamp.

So the agent's cheapest path back to the detail is to re-run a two-minute build it already ran.
Neither dtk nor rtk has anything here, which makes it a cheap differentiator.

## Solution

A top-level `dtk log` command that retrieves a previous run's output from the tee directory.

```
dtk log [<subcommand>] [--list] [--index <n>] [--lines <n>] [--full] [--all]
```

| Invocation | Result |
|---|---|
| `dtk log` | Newest log **for the current project**: header, then the last 100 body lines |
| `dtk log build` | Newest `build` log for the current project |
| `dtk log --list` | Index of available logs instead of content |
| `dtk log --index 3` | The 3rd newest, numbered as `--list` numbers them |
| `dtk log --lines 300` | Widen the window |
| `dtk log --full` | The whole body, no window |
| `dtk log --all` | Every project, not just this one |

Named `log` rather than `last`: the default behaviour *is* `last`, so a separate command would be a
synonym for the zero-argument form, and `--list` / `--index` make it a browser rather than a
one-shot.

### Retrieval is windowed, not a dump

The obvious implementation — print the file — is in direct conflict with dtk's reason for existing.
A representative build log on the author's machine is 19.3 KB; emitting that costs more than
filtering the run saved, and an agent that pays it twice would have been better off re-running.

So `dtk log` bounds its own output by default and makes widening explicit. The view header states
what was withheld, so the caller can decide without guessing:

```
dotnet build MyApp.slnx — exit 1 — 2026-07-28T09:14:02Z
/home/user/.local/share/dtk/tee/1785264642362_…_build.log (19.3 KB, 412 lines)
showing last 100 of 412 lines — --lines N or --full for more
```

The tail is the right default window: compiler and test failures report at the end, and the raw-tail
fallback in `FilteredOutputPipeline` already uses last-40-lines on the same reasoning.

`--list` reports file size but **not** line counts. Counting lines across the index means reading
every file — up to 20 MiB at the configured cap — to render a summary. The selected file is read in
full anyway, so its line count is free and appears in the view header.

### Flag interactions

Spelled out because each combination is reachable from the command line and a plausible reading
exists for both sides:

- `--list` **overrides** `--index`, `--lines`, and `--full`. Asking for the index and for a body at
  once is a contradiction; the index wins rather than erroring, because the index is the cheaper
  and more recoverable of the two answers.
- `--full` **overrides** `--lines`. The explicit "all of it" beats a bounded count.
- `--index` numbers the **same set `--list` would show** under the flags in effect. So
  `dtk log build --index 2` is the 2nd newest `build` log for this project, not the 2nd newest log
  overall that happens to be a build.
- `--index` beyond the available count exits 1 with the count that *is* available, rather than
  silently clamping to the oldest.
- A window larger than the body prints the whole body, and the header says `showing all N lines`
  rather than claiming a truncation that did not happen.

### Scoped to the current project by default

The tee directory is global (`~/.local/share/dtk/tee/`, `MaxFiles = 20`), and filenames record only
a timestamp, a guid, and a subcommand. Nothing identifies the repo.

Without scoping: fail a build in repo A, switch to repo B, run `dtk log`, and you get repo A's
failure with nothing marking it as foreign. An agent reads a stale error from another project and
starts fixing a problem that does not exist in the code in front of it. That failure mode is worse
than the missing feature, because it looks like a real result.

`dtk log` therefore matches on the current working directory by default, and `--all` opts out.

The match is on the **exact** recorded path, compared ordinally and case-insensitively only on
Windows, after normalising away a trailing separator. Not a prefix match: running from
`src/DotnetTokenKiller.Cli` does not surface a log recorded at the repo root. Prefix matching would
be friendlier, but it needs a definition of "the project" that dtk does not have — inferring one
from the nearest `.sln`/`.csproj` is a guess that fails quietly in nested and multi-solution
layouts. Exact matching is the honest version of a rule the caller can predict, and `--all` covers
the case it is too strict for.

### The tee file becomes self-describing

Scoping needs the cwd recorded somewhere. `FileTeeService` gains a header block:

```
# dtk-log v1
# command: dotnet build MyApp.slnx
# cwd: /home/user/projects/DotnetTokenKiller
# exit: 1
# source: Run
# utc: 2026-07-28T09:14:02Z
---
<raw output>
```

`FilteredOutputRequest` already carries `DisplayCommandLine` and `Source`, so the header needs
nothing new threaded through the pipeline — only an added parameter on `ITeeService.TeeAndHintAsync`
carrying the values it does not yet receive.

Two alternatives were rejected:

- **Encoding the metadata in the filename.** Listing would need no file reads, but a full path
  cannot go in a filename, so it would have to be a *hash* of the cwd. A hash answers "is this
  mine?" and nothing else — `--list --all` could not name the project a log came from.
- **A `tee_path` column in the SQLite tracker.** The tracker already holds timestamp, command,
  project path, and success, and `--list` would become a query. But `tracking.enabled` and
  `tee.mode` are independent config knobs: turn tracking off and `dtk log` goes blind while logs
  keep being written, with no error to explain it. Rotation deleting a file would also leave a row
  pointing at nothing.

A header in the file keeps one source of truth, survives the file being copied elsewhere, and
extends without a schema migration. Reading the first 512 bytes of at most 20 files to build an
index is not a cost worth optimising away.

`MaxFileSizeBytes` continues to apply to the **body alone**; the header is written in addition to
that budget. Charging a configured cap for bytes the user did not ask to store would silently shrink
every existing setting.

### Logs written before this version

Files with no header parse as v0: unknown cwd, unknown exit code. They cannot be proven to belong to
the current project, so they are excluded from the scoped default — that exclusion is the whole
point of scoping — and appear under `--all` marked `project: unknown`.

To keep that from reading as data loss on the first upgrade, the empty-state message names them:

```
no logs for this project (4 older logs predate project tracking — see --all)
```

The condition is self-healing: the next filtered run in the project writes a v1 header.

### Empty state

`dtk log` with nothing to show exits **1** with the reason on stderr. This is where the
discoverability gap actually gets closed: with the default `TeeMode.Failures` and the 500-character
floor, a successful or small run leaves **no log at all**, and a bare "not found" would read as a
broken feature. The message states the applicable reason — no tee file was written for a passing
run, tee is disabled, or the output was below the floor.

### `dtk log` records nothing

It runs no dotnet command. Writing a `CommandRecord` for a retrieval would add rows with no raw
output to compare against and corrupt the savings figures `dtk gain` reports.

## Components

Following the existing layering.

| Layer | Addition |
|---|---|
| Domain | `TeeLogHeader` (record + pure `TryParse`), `TeeLogEntry` (path, header, size), `ITeeLogStore` |
| Infrastructure | `FileTeeLogStore`; `TeeDirectoryResolver` extracted from `FileTeeService` |
| Application | `LogViewUseCase` — selection (scope, subcommand, index) and windowing |
| Cli | `LogCommand` + settings, `Formatting/TeeLogRenderer`, `CliConfigurator` registration |

`TeeDirectoryResolver` is the one piece of existing code this touches. The
override → `DTK_TEE_DIR` → config → platform-default chain is currently a private static inside
`FileTeeService`, and the store needs the identical chain. Duplicating it means a divergence writes
logs to one directory and reads them from another — a bug that presents as "my logs vanished". It
moves to a shared type rather than being copied.

`LogViewUseCase` does no I/O of its own: it takes entries from the store and returns the text to
print, so selection and windowing test without a filesystem.

Registering the command also requires updating `CompletionCommand` and the root-help snapshot.
Existing binding tests fail until both are done, which is the intent.

## Testing

- `TeeLogHeader.TryParse` — v1, absent header, malformed header, CRLF line endings, non-ASCII paths,
  a body containing a line that looks like a header delimiter.
- `FileTeeLogStore` — listing order, project scoping, subcommand filtering, legacy files, a
  non-existent tee directory, a directory holding non-`.log` files.
- Round-trip: written by `FileTeeService`, read back by `FileTeeLogStore`. This is what catches the
  two halves of the format drifting apart.
- `LogViewUseCase` — index selection, window shorter/longer than the body, `--full`, empty body.
- `LogCommand` — flag wiring, exit code 1 on empty state, help snapshot.
- Integration: a failing run through the real pipeline, then `dtk log` retrieving it.
- Existing `FileTeeService` tests need updating for the header, including the assertion that
  `MaxFileSizeBytes` still bounds the body alone.

## Out of scope

- **Passthrough runs are not tee'd, and this does not change that.** `PassthroughRunUseCase` streams
  or measures but never calls `ITeeService`, so `publish`, `ef migrations`, and every other
  unfiltered subcommand — the highest-volume output dtk touches — leave no log for `dtk log` to
  find. Fixing it properly depends on the §4 streaming work; recording it here so the gap is not
  mistaken for covered.
- **`--grep`.** On a shell, `dtk log --full | grep` filters before the output reaches the caller, so
  the tokens are never paid. Deferred until coverage data shows it is wanted.
- **`--path`.** The view header already prints the path.
- **Raising `MaxFiles` from 20.** The cap is global across repos, so an afternoon in a second repo
  evicts this one's failure. Rotation is already oldest-first, which is the correct policy; raising
  a storage default under cover of a retrieval feature is a separate decision with its own
  trade-off. Documented as a limitation instead.

# Agent-review deferred work for 0.11 — design

Date: 2026-11-04. Status: draft, awaiting the user's review. Branch: `feat/agent-review-0.11`.

Source: the *Deferred* table of [the 0.10 design](2026-11-02-agent-review-fixes-design.md#deferred-tracked-not-in-010),
which itself comes from the 2026-11-02 review of a month of Claude Code transcripts. The user asked for every item in
that table to be implemented.

Decided by the user (2026-11-04): `dotnet run` is dropped from 0.11; localized `dtk pipe test` output is detected and
redirected to the remedy, not parsed.

Calls made by the author rather than the user (check these): `--output` renamed `--show-output`; `--fail-on-empty` exits
8 and does not treat an all-skipped run as empty; dtk reads its options only on filtered subcommands and `dtk pipe`; on
a filtered run passed through anyway, `--tee`/`--fail-on-empty` are refused and display options are dropped with a
warning; `dtk pipe test` accepts the test options; `--tee` overwrites, is owner-only, fails fast when the file cannot be
created, and makes a command non-auto-approvable; `--max-lines` keeps the head and never counts the status line;
`--names` injects a runner-specific argument only when the runner is known; ef, Stryker and jb own every short option
and `--verbose`/`--quiet`/`--help`; the ef filter covers `ef migrations` and `ef database`; no new subcommand becomes
auto-approvable; `jb cleanupcode` changes are found by hashing files before and after the run; `dtk log` header to
stderr is closed without work.

## Why

0.10 made dtk trustworthy on the commands it already filtered. The review's remaining findings are about what dtk does
not cover yet:

- **Commands it does not filter.** `dotnet tool restore` (56 calls, up to 14K chars), `dotnet ef migrations` (18),
  `dotnet stryker` (36, up to 17K), `dotnet jb cleanupcode` (70) and `dotnet run` (90) all reach the agent raw.
- **Evidence agents need and cannot get.** An agent working to acceptance criteria wants the raw output kept as
  red/green evidence (`--tee`), the names of the tests that passed (`--names`), and the output of a failing test
  (`--output` in the review, `--show-output` here).
- **Control over size and emptiness.** No way to cap a long report (`--max-lines`) or to make a run that matched no
  test fail (`--fail-on-empty`).
- **`dtk pipe` and localized output.** Wrapped runs force `DOTNET_CLI_UI_LANGUAGE=en`; piped output cannot, so a
  French or German `dotnet test` log piped into `dtk pipe test` is not recognized.

**What success looks like:** every command an agent commonly runs in a .NET repository comes back compact, with its
verdict on the last line, and an agent can ask dtk for the evidence it needs (raw log, passed names, failing output)
without bypassing the filter.

## Goals, non-goals, anti-goals

Goals: implement every row of the 0.10 *Deferred* table except `dotnet run` and the `dtk log` header (both closed,
see *Scope*), and ship them together as **0.11.0**.

Non-goals:

- a `dotnet run` filter or any streamed route for long-running, interactive commands (closed, see *Scope*);
- parsing localized test output: `dtk pipe test` recognises it and points to the remedy (Group 4);

- wrapping executables other than `dotnet` (bare `jb`, `dotnet-stryker`, `dotnet-ef`): the hook only matches `dotnet`,
  and the OpenCode/Kilo/Amp/pi plugin bodies, which pre-check `includes("dotnet")`, are byte-pinned;
- filters for other `dotnet tool` verbs (`install`, `update`, `run`) or other `ef` command groups (`dbcontext`);
- localized MSBuild output (`dotnet build` through `dtk pipe`);
- making any new subcommand auto-approvable.

Anti-goals — the work fails even if it "works" when:

- any run's last line is not its status line, or any successful run prints nothing (the 0.10 output contract holds for
  every new filter and every new option);
- a dtk option hijacks a token the wrapped tool or dotnet owns (as `-v` did in 0.10, and as `--output` would);
- a command that was not auto-approved before becomes auto-approved, or an auto-approved command can now write a file
  of the agent's choosing;
- a dtk-only token leaks to the child (MTP exits 5, MSBuild fails with MSB1001), including under `DTK_RAW=1`,
  `--interactive` and machine-readable runs;
- savings on existing *successful* scenarios drop noticeably (same rule as 0.10: any successful-run scenario losing
  more than one percentage point needs a sentence of justification in the PR);
- dtk's returned exit code, the exit code in the tee log header and the tracked success disagree (relevant to
  `--fail-on-empty`);
- an older dtk sharing the tee directory can no longer read a log written by 0.11.

## Hard constraints

Everything in the 0.10 design's *Hard constraints* still applies: the Codex hook command and `timeout` never change;
plugin bodies stay byte-pinned; all rewriting decisions live in `DotnetCommandRewriter`; no new tee header key; no
settings option of a kind `CommandSettingsAotGuardTests` rejects (only `bool`, `int`, `string`, `string[]` — so no
`int?`); changed instruction text means registering the shipped hashes as released (now the **0.10.0** ones, since
0.10.0 has shipped) in `ReleasedCursorRuleHashes`/`ReleasedMarkdownRuleHashes`; any filter output change fails
`SavingsBaselineTests` until `update-baseline` is run; new fixtures are **real** output with the machine path replaced
by `/test/project/root`, never hand-written.

Added for 0.11:

- `FilteredRunUseCase.RunAsync` already has 7 parameters and S107 fails the build: carry new options in
  `OutputOptions` (or a successor type), not as more parameters.
- Every new long option must appear in `docs/articles` (`DocsBindingTests`).
- `SavingsEngineTests.All_CoversEveryFixtureExactlyOnce` requires a scenario for every new fixture, and
  `FixtureCorpusTests` pins the fixture count.
- CLI integration tests copy `Fixtures/*.txt` non-recursively: keep new fixtures flat.

## What real runs showed

Captured on 2026-11-04 (SDK 10.0.403, Linux), with the tools this repository already uses. These facts drive the
filter designs below.

- **`dotnet tool restore`**: on success one `Tool 'x' (version 'v') was restored. Available commands: c` line per
  tool, a blank line and `Restore was successful.`; a missing package prints one line, `Version 1.0.0 of package x is
  not found in NuGet feeds …`, and exits 1. The 14K-char outputs in the review are likely feed failures repeated
  per tool or per source (private feeds on the reviewer's machine); capture one (an unreachable source in a
  `nuget.config`) as a fixture.
- **`dotnet ef`** (dotnet-ef 10.0.14): `Build started...`, `Build succeeded.`, then the result (`Done. To undo this
  action, use 'ef migrations remove'`, `Applying migration '…'.`, the migration list, `Removing migration '…'.`). A
  failed build prints only `Build failed. Use dotnet build to see the errors.` and exits 1: the compile errors are
  never in ef's output. `--verbose` adds ~40 `Using …`/`Finding …` diagnostic lines and the commands it runs.
  `migrations script` writes SQL to stdout. An app with logging configured (a hosted app) also prints its own
  `info: Category[id]` blocks during `database update` — not captured yet; capture it.
- **`dotnet stryker`** (5.0.0): an ASCII banner, `[hh:mm:ss INF]` log lines with full paths, a `Killed/Survived/
  Timeout/Errors` block, `Your <reporter> report has been generated at:` followed by the path **wrapped at 80
  columns**, `Time Elapsed 00:00:12.3552697` and `The final mutation score is 37.50 %`. Survivors are **not** on
  stdout; they are in `StrykerOutput/<timestamp>/reports/mutation-report.json` (mutation-testing-elements schema 2:
  `files.<path>.mutants[]` with `status`, `mutatorName`, `replacement`, `location.start`) when the json reporter is
  on — this repository's `stryker-config.json` enables json, html and dashboard; Stryker's own default is html and
  progress. `--break-at` below the score prints a threshold message and exits 2. An initial build failure prints the
  MSBuild output twice (once unwrapped in an `ERR` line, once wrapped at 80 columns) and exits 1.
- **`dotnet jb cleanupcode`** (2026.2.3.1) on this repository: **64 KB, 724 lines** — 4 banner lines, 314
  `<folder>\<project>\path - Skipping.` lines (fixtures, generated files, NuGet files), `Cleaning up using profile …`,
  then **every file it processed**, changed or not. Two consecutive runs print identical output, and jb rewrites
  every processed file (fresh mtime) even when its content does not change, so neither the output nor file times
  say which files changed. A project with a compile error is reported as `[MsBuild] path (l,c)->(l,c) CSxxxx: …` and
  exits 4; a missing path prints `Warning: Path doesn't exist …` and `No items were found to cleanup.` and exits 3.
- **Short options are taken.** `-v` is `--verbose` in ef, `--version` (the dashboard project version, which takes a
  value) in Stryker and `--version` in jb; Stryker also has `-V`, `-o`, `-O`. dtk takes `-v`/`-q` for itself today.
- **Localized VSTest** (`DOTNET_CLI_UI_LANGUAGE=fr`): every line dtk keys on is translated (`Échoué!  - échec :
  1, réussite : 3, ignorée(s) : 0, total : 4, durée : 25 ms`, `Message d'erreur :`, `Arborescence des appels de
  procédure :`); the restore lines too. Stack frames (`at … in …:line 8`) stay English.
- **Passed test names** appear only with VSTest's `--logger "console;verbosity=normal"` (`  Passed Name [13 ms]`) or
  MTP's `--output Detailed`; dtk detects neither runner today.

## Delivery

One branch, `feat/agent-review-0.11`, one pull request into `develop`, built as five themed groups of commits in the
order below. Each group leaves the suite green and the baseline regenerated if its output changed. Then bump to
0.11.0 and cut the CHANGELOG by hand, as in previous releases.

## Scope

### Group 1 — dtk's output options: `--tee`, `--max-lines`

**Value options in the preprocessor.** dtk's options are bare flags today, so `InsertSeparator` would turn
`dtk dotnet test --tee out.log` into `dotnet test --tee -- out.log` and hand `out.log` to dotnet as a project. Teach
`ArgumentPreprocessor` value options: `--tee <path>`, `--tee=<path>`, `--max-lines <n>`, `--max-lines=<n>` (and
`:`-joined forms if Spectre accepts them). The value moves with its flag ahead of the inserted `--`, and
`StripDtkOptions`/`PassthroughArguments` drop flag *and* value, so `DTK_RAW=1`, `--interactive` and machine-readable
runs never pass either to dotnet. A missing value is a dtk usage error, not a dotnet one. Anything after the user's own
`--` stays untouched.

**Where dtk reads its options at all.** Only on filtered subcommands and `dtk pipe`. A subcommand dtk does not filter
(`tool run`, `run`, `watch`, `list`, `workload`, `dotnet app.dll`, …) gets its arguments verbatim, as today and as the
generated instructions promise: `dotnet tool run x --tee y` hands `--tee y` to `x`. No per-verb allowlist, no option
parser in `PassthroughEntryPoint`.

**A filtered subcommand that is passed through anyway** (`DTK_RAW=1`, `--interactive`, machine-readable output) strips
every dtk option before dotnet sees it, then:

- an option that promises a file or changes the verdict (`--tee`, `--fail-on-empty`) is **refused**: dtk prints one
  line saying which option cannot be honoured and why (e.g. `dtk: --fail-on-empty needs a filtered run, but DTK_RAW=1
  is set`) and exits with dtk's usage-error code without running the child — otherwise `DTK_RAW=1 … --fail-on-empty`
  would be the false green the flag exists to prevent;
- a display option (`--max-lines`, `--names`, `--show-output`) is dropped with one stderr warning, and the run goes
  ahead.

**`--tee <path>`.** Writes the run's raw output to `<path>` in addition to the normal filtered output:

- **Content:** the same body the tee log gets (both streams in arrival order, ANSI stripped), with no header and no
  size cap. It is evidence of the run, so nothing dtk adds goes in it.
- **File handling:** created (or truncated) before the child starts, owner-only on POSIX like tee logs (they can hold
  secrets). If it cannot be created, dtk prints one error line naming the path and the reason, and exits non-zero
  **without running the child**, so an agent never believes evidence was saved when it was not. Parent directories
  are not created (as with shell redirection).
- **Where it applies:** filtered runs and `dtk pipe` (see above for passed-through runs). It does not depend on
  `tee.mode` (works with `Never`).
- **Writer:** the user's file is a secondary writer with its own gate; a failure writing it mid-run (disk full) must
  not fail or truncate the filtered output — report it in one line before the status line.
- **Hook:** `IsAutoApprovable` returns false for any command containing a `--tee` token (fail-closed, as CLAUDE.md
  requires of that method): an auto-approved `dotnet test --tee ~/.bashrc` would otherwise let the hook approve a file
  write that `IsSimpleCommand` deliberately refuses for redirections. `Rewrite` itself is unchanged.

**`--max-lines <n>`.** Caps the filtered body at `n` lines (an `int`, `0` = off, the default):

- keeps the **first** `n` lines of the body (dtk lists the most important item first: the first failure, the first
  error file);
- the status line and the `--show-log` hint are never counted and never cut; a footer goes right before them:
  `… 42 more lines (dtk log)` when this run has a tee log, `… 42 more lines (rerun without --max-lines)` when tee is
  `Never`; numbers in `InvariantCulture`;
- applies to every path through `FilteredOutputPipeline`, including the raw-tail fallback and the filter-faulted raw
  output, where it matters most;
- a filter's own footer (`+N more failures`, `… not shown (dtk log)`) may be cut with the body; that is accepted.
- Tracking records the capped output; the savings baseline is unaffected (the corpus runs without options).

### Group 2 — test options: `--names`, `--show-output`, `--fail-on-empty`

These go on a new `DotnetTestCommandSettings` (the shared `DotnetCommandSettings` serves every subcommand): bump the
settings count in `CommandSettingsAotGuardTests` and the registration in `CliConfigurator`. Passing one of them to
another subcommand is a dtk usage error, not a token passed to dotnet.

`dtk pipe test` accepts all three too (on `PipeCommandSettings`, checked by hand since pipe's subcommand is
positional; `dtk pipe build --names` is the same usage error). Piped text cannot be changed, so `--names` and
`--show-output` use whatever the piped text contains (`--names` prints its `⚠` line when the names are not there), and
`--fail-on-empty` turns pipe's exit code (otherwise `--exit-code`) into 8 on an empty verdict.

**Runner detection (shared).** `--names` needs to know whether the run uses VSTest or Microsoft.Testing.Platform:
MTP when the nearest `global.json` above the working directory has `"test": {"runner": "Microsoft.Testing.Platform"}`,
VSTest otherwise. When detection cannot decide (unreadable or malformed `global.json`), dtk injects nothing. dtk
must never inject an argument for the wrong runner (VSTest's `--logger` under MTP exits 5; MTP's `--output Detailed`
under VSTest is read as an output directory).

**`--names`.** Lists the passed tests, grouped by class, after the failures and before the status line:

```
✓ SampleApp.Tests.SampleTests (3): Addition_ReturnsCorrectResult, List_ContainsExpectedItems, String_IsNotEmpty
✓ SampleApp.Tests.ParserTests (2): Parses_Empty, Parses_Unicode ×4
```

- To get the names, dtk appends `--logger "console;verbosity=normal"` (VSTest; not when the user already passes a
  console logger) or `--output Detailed` (MTP; not when the user already passes `--output`). The test filter already
  parses normal-verbosity VSTest output; MTP's detailed output needs parser support and real fixtures.
- Theory/data-row cases are grouped under their method name (the part before `(`) with a `×N` count.
- MSTest under MTP prints display names without a class; those group under their assembly
  (`✓ Alpha.Tests.dll (12): …`).
- When no names could be read (unknown runner, unrecognised output), print one `⚠ --names: passed test names not in
  the output` line instead of failing silently.
- No cap of its own; `--max-lines` exists for that.

**`--show-output`** (the review's `--output`; renamed because `--output` is already `dotnet build/test -o <dir>` under
VSTest and `--output <Normal|Detailed>` under MTP, the hook copies arguments verbatim, and no value can tell the uses
apart). For each failing test, its captured output — VSTest's `Standard Output Messages:`, MTP's `Standard output`/
`Error output` sections — goes beneath that test's failure entry, indented, at most 20 lines per test with
`… N more lines (dtk log)`. Without the flag the output stays dropped, as today (Reqnroll pending-step extraction is
unchanged). Fix, regardless of the flag: `CollectMessageLines` does not stop at `Standard Output Messages:`, so a
failure with no stack trace folds the test's output into its message.

**`--fail-on-empty`.** When the test verdict is "no test matched" — no assembly reported a single test, passed,
failed or skipped — dtk exits **8** (MTP's own "zero tests ran" code, so both runners agree) and the status line
becomes a failure. A run whose tests were all skipped is **not** empty: tests matched, and the filter already reports
them (`✓ dotnet test: 0 passed, N skipped`); with the flag dtk keeps the child's code for it. Check with a real run
what MTP returns for an all-skipped session and record it in the tests (if it is 8, dtk mirrors it as today). The
empty verdict reads:
`✗ dotnet test: no test matched filter FullyQualifiedName~X (Probe.dll) — --fail-on-empty`. Under MTP, which already
exits 8, only the glyph changes. The filter must report this verdict in a structured way (today it returns only a
string; the contract change is the builder's call), and the effective exit code is decided once, before the tee log is
finalized and the run is tracked, so all three agree. This is the one opt-in place where dtk's exit code differs from
dotnet's; without the flag nothing changes.

### Group 3 — new filters: `tool restore`, `ef`, `stryker`, `jb cleanupcode`

Each one is a registered subcommand (`DotnetSubcommands`, the remarks there are the checklist: filter key, keyed
filter, Spectre command, completion, `dtk pipe`, slugs, docs). The hook then rewrites them automatically; none is
added to `AutoApprovableSubcommands` (so Cursor, which rewrites only auto-approvable commands, keeps passing them
through). Keep the existing `QualifyingVerbs` entries so older coverage rows stay readable.

**Options owned by the tool.** For `ef`, `stryker` and `jb cleanupcode`, every short option and `--verbose`,
`--quiet`, `--help` belong to the tool (ef `-v` = verbose, Stryker `-v <version>`, jb `-v` = version); dtk keeps only
its long, dtk-only options (`--vv`, `--show-log`, `--tee`, `--max-lines`). `tool restore` is a dotnet verb and keeps
today's rules (including the 0.10 `-v <level>` exemption).

**Run context (shared by `stryker` and `jb cleanupcode`).** Both need more than the captured text: Stryker's survivors
are in a report file, and jb's changed files are only knowable by comparing content before and after. Give filters an
optional run context — working directory, run start time, and a before-run hook — without breaking the rule that
every filter is testable from a fixture alone: under `dtk pipe` (no context), a filter prints what the text alone
supports and says what it could not compute.

**`dotnet tool restore`.**

- Success: one line naming the commands the agent can now run: `✓ dotnet tool restore: 4 tools (docfx, jb,
  dotnet-stryker, dtk)`.
- Failure: each error line kept once (identical lines deduplicated, paths shortened as elsewhere), then
  `✗ dotnet tool restore failed (exit 1): 1 of 4 tools not restored` (or the error count when no tool line was
  parsed).
- `--interactive` joins `PassedThroughWhenInteractive` so private-feed authentication keeps a terminal.

**`dotnet ef migrations …` and `dotnet ef database …`** (one filter; `database update` is the usual companion of
`migrations add` and has the same noise).

- Machine-readable runs stay passthrough (extend `IsMachineReadableRun`): any ef command with `--json`, and
  `migrations script` without `-o`/`--output` (SQL on stdout). `ef database drop` without `-f` stays attached, as
  today.
- Dropped: `Build started...`, `Build succeeded.`; app log blocks at `info`/`dbug`/`trce` (a `info: Category[id]` line
  and its indented continuation lines), replaced by one `(N log messages hidden — dtk log)` line; `warn`/`fail`/`crit`
  blocks are kept. With ef's own `-v`/`--verbose`, only the build lines are dropped (the user asked for diagnostics).
- Kept: every result line (`Done.`, `Applying migration '…'.`, `Removing migration '…'.`, the migration list,
  `No migrations were applied…`, error and exception messages).
- Build failure: ef never shows the compile errors, so the status line says how to get them:
  `✗ dotnet ef migrations add failed (exit 1): build failed — run dtk dotnet build <project> to see the errors`
  (with the `--project`/`-p` value when given). dtk does not start a build itself.
- Success: `✓ dotnet ef migrations add: done` (wording: builder's call), with the result lines above it.
- Fixtures from a real EF Core project: add, list, remove, update, a build failure, a runtime error, and a hosted app
  with logging during `database update`. Add a small `samples/SampleApp.Ef` (EF Core SQLite) so they can be
  regenerated; whether `dotnet-ef` also joins the tool manifest for an integration test is the builder's call (CI does
  not run `dotnet tool restore` today; a skip-unless-available test like the Node ones is acceptable).

**`dotnet stryker`.** Moves from attached passthrough to a filtered run. A long run prints nothing until it ends, as
`build` and `test` already do.

```
Survived (3):
  Calc/Calculator.cs: (6,45) Equality → a >= 0 · (8,58) Equality → v <= lo · (8,81) Equality → v >= hi
No coverage: 7 (Calc/Calculator.cs ×7)
✓ dotnet stryker: mutation score 37.50% · 6 killed, 3 survived, 0 timeout, 7 no coverage (12.36s) · report StrykerOutput/2026-11-04.19-31-59/reports/mutation-report.html
```

- Counts and score from stdout (`Killed:`…, `The final mutation score is …`), duration from `Time Elapsed`
  (`DurationFormat`, exact).
- Survivors from the json report: the path printed after `Your json report has been generated at:` (rejoin the
  80-column wrap), else the newest `StrykerOutput/*/reports/mutation-report.json` written after the run started
  (under the working directory or Stryker's `-O`/`--output`). If the html report is verified to embed the same JSON
  reliably, it may be used when no json report exists; otherwise print `survivors: enable the json reporter to list
  them here`. At most 20 survivors, grouped by file, then `… N more (report)`. No-coverage mutants as counts per file.
- Threshold break (exit 2): `✗ dotnet stryker: mutation score 37.50% below break threshold 90% (exit 2) · …`.
- Initial build failure: render the unwrapped MSBuild block through `MsBuildDiagnosticReport` (the grouped layout
  `dtk dotnet build` gives), ending with `✗ dotnet stryker failed (exit 1): initial build failed · 1 error`.
- Dropped: banner, `INF` lines, `Hint:` lines, the report-path wrap, `You can open it …`. Kept: `WRN`/`ERR` lines
  (deduplicated, paths shortened).
- Fixtures: default reporters, json reporter, `--break-at`, build failure, a solution-mode run (as this repository's
  config uses) and a run with timeouts if one can be produced.

**`dotnet jb cleanupcode`.**

```
src/DotnetTokenKiller.Domain/DotnetSubcommands.cs
src/DotnetTokenKiller.Infrastructure/Tracking/TrackerFactory.cs
✓ dotnet jb cleanupcode: 2 files changed (356 processed, 314 skipped)
```

- Changed files: before the child starts, dtk hashes the files under the target's directory (the solution/project
  argument's directory, else the working directory), skipping `bin`, `obj`, `.git` and other hidden directories,
  `node_modules`, `artifacts` and `StrykerOutput`; after it exits, it lists the files whose content changed, relative to
  that directory, at most 30, then `… N more (git status)`. The snapshot is bounded (file count and total bytes, limits
  are the builder's call): over the bound, dtk skips it and says `changed files not computed (tree too large)`.
- No changes: `✓ dotnet jb cleanupcode: no changes (356 processed, 314 skipped)`. Under `dtk pipe`: processed and
  skipped counts only, with `changed files unknown (piped)`.
- Failure (exit 3, 4): keep `Warning:` and `[MsBuild]` lines (deduplicated, paths shortened) and `No items were found
  to cleanup.`, then `✗ dotnet jb cleanupcode failed (exit 4): 1 project load error`.
- Dropped: banner lines, `- Skipping.` lines (counted), the processed-files list (counted).

### Group 4 — localized `dtk pipe test` output

Decided: **detect and redirect, do not parse.** Today a localized run piped into `dtk pipe test` falls back to the raw
tail with `⚠ … output not recognized`, which is honest but does not say why or what to do.

- When the test filter recognises nothing, look for a localized test summary — VSTest's `Passed!`/`Failed!`/
  `Skipped!` line and normal-logger `Test Run …` block, MTP's `Test run summary:` — in any language the SDK ships.
  Take those strings from the SDK's own resource assemblies (the satellite assemblies under the SDK directory), not
  from memory; generating a table from them at development time is fine.
- On a match, keep the raw-tail fallback (`--max-lines` applies) and make the status line name the cause and the
  remedy, e.g. `⚠ dotnet test: output is localized (fr) — run it through dtk dotnet test, or set
  DOTNET_CLI_UI_LANGUAGE=en`. The exact wording is the builder's call; it names the language when the match tells it.
- The verdict glyph follows `--exit-code` as the fallback does today; dtk does not guess pass/fail from text it cannot
  parse.
- Wrapped runs are unchanged (they already force English).

### Group 5 — instructions, docs, release

- Generated skill / AGENTS.md section: the new filtered subcommands (from `DotnetSubcommands.Ordered`), and one line
  each for `--tee` ("keep raw output as evidence"), `--names`, `--show-output`, `--fail-on-empty`, `--max-lines`.
  Register the 0.10.0 hashes as released; regenerate `.github/copilot-instructions.md`.
- docs/articles (every new option, `DocsBindingTests`), README, CLAUDE.md, the CLI README.
- In the 0.10 design's *Deferred* table, mark each row as moved to this design (or closed), so one list stays
  authoritative.
- Example pages are optional for the new filters (`ExamplesBindingTests` guards only build, test, restore and format).

### Closed without work: `dtk log` header to stderr

The row's own note says it is moot once the per-kind slot exists, which 0.10 shipped. Closed; revisit only if an agent
is seen confusing the header with the log body.

### Closed without work: `dotnet run` startup summary

Decided by the user: dropped from 0.11. A filter needs the whole output and the final exit code, while `dotnet run` is
interactive and often never exits (servers, `watch`); fitting it would need a new streamed route (stdin attached,
output passed through line by line, only the pre-start build and hosting lines compacted). `dotnet run` stays an
attached passthrough with the first Ctrl+C left to the child. Revisit only with a concrete transcript case showing
which pre-start output wastes tokens.

## Testing

Golden tests from real runs replayed through the filters, covering at least: tool restore success, missing package and
unreachable feed; every ef fixture listed above; every Stryker fixture listed above; jb cleanupcode with changes,
without changes, exit 3 and exit 4 (with a temp tree for the hashing); MTP `--output Detailed` (MSTest and xUnit v3);
VSTest normal verbosity with theories; failing tests with `ITestOutputHelper`/`Console` output under VSTest and MTP; a
no-match run with and without `--fail-on-empty` under both runners (exit code, tee header and tracked success agree), an
all-skipped run with the flag, localized runs piped into `dtk pipe test` (real runs of the samples with
`DOTNET_CLI_UI_LANGUAGE` set to at least `fr`, `de` and `ja`, pass and fail, VSTest and MTP), and `dtk pipe test
--fail-on-empty`. Preprocessor tests for every value-option form, including `DTK_RAW=1`, `--interactive`,
machine-readable runs and a user `--`; the refused and the warned options on a passed-through run; `dotnet tool run x
--tee y` reaching `x` untouched. Rewriter table tests: `--tee` is never auto-approvable; new subcommands are rewritten.
An AOT parity step that passes `--tee {project}/raw.log --max-lines 5` to a wrapped command (the parity runner already
compares the files written).

## Left to the builder

Wording of status lines marked as such; how the filter reports a structured verdict; the run-context shape; the
snapshot bounds and hash function; whether `dotnet-ef` joins the tool manifest; whether Stryker's html report can be
used; where runner detection lives and whether it also reads `Directory.Build.props`; the `dtk pipe` "Available:" line
width once five subcommands are added.

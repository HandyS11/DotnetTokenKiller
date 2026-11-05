# Agent-review fixes for 0.10 — design

Date: 2026-11-02. Status: approved. Branch: `fix/agent-review-0.10`.

Calls made by the author rather than the user (check these): one `dtk log` slot per subcommand kind, and the newest
run of each kind kept whatever its size; a test run with both compile errors and test results shows both; the
zero-match exit code stays the child's; `DTK_RAW=1` as the variable name, and no `--raw` flag; `-v <level>` left to
dotnet; no numeric savings gate, but a written justification for any successful-run scenario losing more than one
point, and at most three warning codes in a test summary; the old macOS rtk exclusion left in place.

## Why

An external review (2026-11-02) mined a month of Claude Code transcripts (1,364 explicit `dtk` calls, about 160 more
rewritten by `dtk hook claude`) and found that dtk is excellent when a command succeeds and weakest when it fails —
exactly when the output matters. Failing test runs save 37.6% and produce 81% of every token dtk emits; several
failure paths lose information or give a wrong answer, and agents work around dtk with `| tail`, `| grep -v`, `dtk
log` and `rtk proxy`, which in turn hides dtk's status line, deletes failing test names, and reads stale logs.

Every finding was re-checked against v0.9.0 (whose logic `develop` still matches) by reading the source and
reproducing on Linux with SDK 10.0.403. This document carries only what that check confirmed, with the review's
labels (C = critical, H = high, M = medium, L = low) kept for traceability.

**What success looks like:** an agent can trust every dtk answer without a second look. No run prints nothing; no
log is silently the wrong one; a raw-output request is honoured; a failing test or build reaches the agent with its
cause intact and without MSBuild noise. Token savings on failing runs go up as a consequence, not as the goal.

## Goals, non-goals, anti-goals

Goals: fix every confirmed finding listed under *Scope*, ship them together as **0.10.0**.

Non-goals (see *Deferred*, which is the tracked list of later work):

- new filters for other `dotnet` subcommands;
- new output features (`--names`, `--output`, `--tee`, `--max-lines`);
- localized VSTest output through `dtk pipe` (wrapped runs already force `DOTNET_CLI_UI_LANGUAGE=en`).

Anti-goals — the work fails even if it "works" when:

- any successful run prints nothing, or any run's last line is not its status line;
- a command that was not auto-approved before becomes auto-approved (`IsSimpleCommand`/`IsAutoApprovable` stay
  fail-closed and keep reading the original command);
- savings on *successful* runs drop noticeably. There is no numeric gate (the baseline test compares exact
  values); the reviewer reads the `update-baseline` diff, and any successful-run scenario losing more than one
  percentage point needs a sentence of justification in the PR. The build-warnings suffix on test summaries lists at
  most three codes to keep it short;
- an older dtk sharing the tee directory can no longer read a log written by 0.10.

## Hard constraints (from CLAUDE.md and the code)

- The Codex hook command and `timeout` never change; the OpenCode/Kilo/Amp/pi plugin bodies are byte-pinned. All
  rewriting decisions live in `DotnetCommandRewriter.Rewrite`, which every `dtk hook <provider>` already goes through
  (`HookPayloads.TryRewrite`), so no plugin or hook definition needs to change.
- No new tee header key (`TeeLogHeader.TryReadFields` rejects unknown keys).
- No new Spectre settings option of a kind `CommandSettingsAotGuardTests` guards without extending `AotParityTests`.
- Changing generated instruction text (`IntegrationInstructions`, `SharedInstructionArtifacts`) requires adding the
  0.9.0 hashes to `ReleasedCursorRuleHashes`/`ReleasedMarkdownRuleHashes`, or upgrade and uninstall stop
  recognising files written by older releases.
- Any change to a filter's output fails `SavingsBaselineTests`; regenerate with `update-baseline` and keep the diff
  reviewable.
- New test fixtures are **real** `dotnet` output (as the `dotnet_test_mtp_*` fixtures are), with the machine path
  replaced by `/test/project/root`, never hand-written. The existing skipped-only test uses a `Passed!` line VSTest
  never prints — that is how C2 hid; replace it with real output.

## Delivery

One branch, `fix/agent-review-0.10`, one pull request into `develop`, built as four themed groups of commits in the
order below (each group leaves the suite green and the baseline regenerated if its output changed). Then bump to
0.10.0 and cut the CHANGELOG by hand, as in previous releases.

## Scope

### Group 1 — trust: never wrong, never empty, never ignored

**Output contract (C2, H2) — decided: summary last, everywhere.** Every filtered run, for every filter, ends with
exactly one status/summary line, and it is the last line, so `| tail -1` always carries the verdict. A filter that
returns nothing on a successful exit must not produce empty output: the pipeline falls back to the raw tail, with
the status line *after* the tail (today `BuildRawTailFallback`, `FilteredOutputPipeline.cs:199-212`, writes it
first). `--show-log`'s hint, which currently follows the summary, moves before it.

"Exactly one" also means no doubled verdict: today a non-zero exit with only warnings parsed prints both
`✗ dotnet build failed (exit 1)` and the report's own `dotnet build: 0 errors, 2 warnings` header; these merge into
one final line (e.g. `✗ dotnet build failed (exit 1): 0 errors, 2 warnings`). Each filter's summary line is the one
it already has (restore, format, clean, `list package` included), moved to the end; where a filter has none, it gets
one (wording: builder's call). Group 1 applies the rule to the pipeline fallback and the test filter; Group 3
applies it to `MsBuildDiagnosticReport` (build, publish, pack) and every remaining filter, alongside its layout
changes. Moving the summary changes every generated example page (`ExamplesBindingTests`) and the savings baseline:
regenerate both, from real runs, in the same commit as the change.

**Test summaries the parser does not know (C2).** `DotnetTestFilter` only accepts `Passed!`/`Failed!` lines
(`SummaryPattern`, line 491). It must also understand:

- `Skipped!` (VSTest's line for an assembly whose tests were all skipped), in single and multi-project runs — today
  a skipped-only run prints nothing and a `Skipped!` project in a multi-project run is dropped from the counts;
- the normal/detailed console logger's `Test Run Successful.`/`Test Run Failed.` + `Total tests:` block — today it
  prints nothing on success and wrong counts on failure (verification saw `4 failed, 0 passed` for a run that had
  3 passed and 1 skipped);
- `--list-tests`: print the listed test names compactly (one per line, build noise removed) and end with
  `✓ dotnet test --list-tests: N tests` (exact wording: builder's call).

The skipped-only summary reads like `✓ dotnet test: 0 passed, 1 skipped (…)`.

**Compile errors inside `dotnet test` (H1).** When the output has no test summary and `MsBuildDiagnosticReport`
finds errors, render that report (the same grouped layout `dtk dotnet build` gives) ending with a `dotnet test`
status line, instead of the 40-line raw tail. Today 60 errors lose their first 20 and the total. When a run has *both*
compile errors (one project failed to build) and a test summary (other projects ran), show both: the grouped compile
errors, then the test failures, with one failed verdict whose status line names both counts (e.g. `✗ dotnet test:
2 failed, 140 passed · 1 project did not build (3 errors)`). Today the compile errors are dropped silently there.

**Stale `dtk log` (C1).** In the default `tee.mode=Failures`, also keep the newest *passing* run **of each
subcommand kind** (build, test, format, …), one slot per kind: a passing run's log replaces the previous passing log
of the same kind instead of being deleted at finalize (`FileTeeSession.cs:69-80`). One slot per kind, not one
overall, because `dtk log build` must not fall back to an older failed build after a passing build followed by a
passing test. A slot log is recognised by the exit code already in its header (no new key, no file-name change;
Always mode already writes exit-0 logs that older dtk reads). The most recent run of each kind — passing *or*
failing — is kept regardless of the 500-byte minimum size, otherwise a small run still surfaces a stale log; older
logs keep the minimum. Slots count toward `MaxFiles`. Result: `dtk log` always shows the most recent run, and
`dtk log build`/`dtk log test` the most recent of that kind. Concurrent runs racing for a slot: builder's call, but
replacing a slot must never delete a failure log.

**Raw-output bypass (C3).** Today none exists. Honour, in `DotnetCommandRewriter.Rewrite` (so every provider gets
it): `rtk proxy dotnet …` (extend the word-before check at line 233 to `rtk|rtk.exe proxy`) and `command dotnet …`.
Add a runtime switch, `DTK_RAW=1`: when set, `Program` hands the run to the passthrough path (unfiltered, still
tracked like any passthrough), so both `DTK_RAW=1 dotnet test` (after the hook's rewrite) and an exported variable
work. The rewriter may additionally skip a command whose `dotnet` is directly prefixed by `DTK_RAW=1` (builder's
call; behaviour is the same either way). Pin the already-working `\dotnet` and absolute-path forms with tests. Do
**not** treat `env`, `exec`, `sudo`, `time` as opt-outs. None of this may change what is auto-approved.

**`-v <level>` hijack (found during verification).** `ArgumentPreprocessor.cs:24` takes `-v` as dtk's `--verbose`,
so `dtk dotnet test -v q` passes `q` to dotnet as a project path, on every filtered subcommand. When `-v` is followed
by an MSBuild verbosity value (`q`, `m`, `n`, `d`, `diag` and their long forms), leave both tokens to dotnet.

**"No test matched" (M3).** Replace `⚠ dotnet test: 0 tests found (no assembly matched)` with the filter expression
and assembly, e.g. `⚠ dotnet test: no test matched filter FullyQualifiedName~X (Probe.dll)`. The exit code stays
the child's (0 under VSTest): dtk mirrors dotnet's exit code everywhere, and changing it here would be the only place
it lies. (An opt-in `--fail-on-empty` is in *Deferred*.)

**rtk hook coexistence (L4).** `dtk init claude` writes rtk's `exclude_commands=["dotnet"]` to
`$XDG_CONFIG_HOME`/`~/.config/rtk/config.toml` on every OS (`RtkHookCoexistence.cs:300`). **Confirmed
2026-11-02:** `rtk config` on the user's Mac prints `/Users/<user>/Library/Application Support/rtk/config.toml`, so
on macOS the exclusion lands in a file rtk ignores and both hooks rewrite `dotnet build`. Resolve rtk's real config
path: prefer the path `rtk config` prints (`Config: <path>`) when rtk is on `PATH`; otherwise the platform default of
Rust's `dirs::config_dir` — `$XDG_CONFIG_HOME` or `~/.config` on Linux, `~/Library/Application Support` on macOS,
`%APPDATA%` on Windows (the Windows one is inferred, not verified). Have `dtk doctor` warn when rtk's hook is
registered but still rewrites `dotnet` (probe `rtk rewrite "dotnet build"`) and say which file to fix; doctor never
edits rtk's config. An exclusion an older dtk wrote to `~/.config/rtk/config.toml` on macOS is left in place
(harmless, and dtk never edits rtk's config outside init); re-running `dtk init claude` writes the right file.

### Group 2 — test failures keep their cause

- **Nested exceptions (M1).** `CompactMessage` joins lines then cuts at 200 chars (`DotnetTestFilter.cs:14, 473`),
  dropping `---- Inner : msg`. Always keep the innermost `---- Type : message` segment, shortening the outer ones
  first.
- **Assertion diffs (M6).** Keep xUnit's header (`Collections differ`, `Strings differ`, `Values differ`) and the
  mismatch position (`↓ (pos N)` → `at pos N`); collapse the alignment padding in `Expected:`/`Actual:`.
- **Reqnroll pending steps (M1).** Extract the missing step's text from `Standard Output Messages:` instead of the
  truncated generated binding snippet (source-read only so far; needs a real Reqnroll fixture).
- **Build warnings on test runs (M2).** Passing and failing test summaries report compile warnings, reusing
  `MsBuildDiagnosticReport`: `✓ dotnet test: 1675 passed (4 projects, 12s) · 3 build warnings (CS0219×2, CA1863)`.
- **Durations (L1, L2).** All numbers use `CultureInfo.InvariantCulture` — `DotnetTestFilter.cs:343`,
  `MsBuildDiagnosticReport.cs:192`, `DotnetRestoreFilter.cs:165`, `DotnetFormatFilter.cs:112`,
  `TextHelpers.cs:27-28`. Durations print at the precision the source gives: VSTest's `Duration: 1 m 40 s` becomes
  `1m40s`, not `100.00s`; sub-second figures keep two decimals. Exact format: builder's call, one helper for all
  filters.

### Group 3 — one layout for diagnostics

- **Truncation and grouping (M1).** Raise the 120-char message cap (`MsBuildDiagnosticReport.cs:17`) for errors,
  strip help URLs before any cap, shorten fully-qualified type names, and group identical code+message within a file
  into one line with all its positions: `(96,59) (99,59) (111,59) CS1503 ×3: <full message>`. CS0121 must keep both
  overloads; CS1503 its "to 'X'" part.
- **Summary last (H2).** Apply the Group 1 output contract here: the report's `dotnet build: N errors, M warnings`
  header becomes its final line, merged with the failure verdict; same for publish, pack, restore, format, clean and
  `list package`.
- **Layout (L5).** Errors and warnings share one layout (grouped by file, `(line,col) CODE: msg`); `Top codes` only
  when it adds something (more than one code and more diagnostics than lines shown); the project count is present on
  failed builds too and counts a multi-targeted project once.
- **Suppressed-warnings wording (M2).** `6 warnings (3 shown) suppressed (use --vv to see)` becomes e.g.
  `3 more warnings not shown (dtk log)` — `dtk log` holds the failed run and costs far less than `--vv`'s raw dump.
- **Format filter (M5).** Strip the `[…csproj]` suffix (`DotnetFormatFilter.cs:128`), group by file and code
  (`UserAccessStore.cs: WHITESPACE ×3 (21:45, 21:80, 21:103)`), and summarise replacement text (`newline + 28
  spaces`).
- **Negative savings (L7).** `format (ok)` shows `-20` because raw input is empty and the filter writes its own line
  (`FilteredOutputPipeline.cs:261`). Clamp saved tokens at 0 in the calculation (filter output unchanged).

### Group 4 — CLI and instructions

- **`dtk test`/`dtk build` (L3).** In `ArgumentPreprocessor.Normalize`, when `args[0]` is a filtered subcommand
  (`DotnetSubcommands.TryMatch`) and not a dtk verb, prepend `dotnet`. No Spectre change, so no "Did you mean
  'reset'?".
- **80-column wrap (L6).** When stdout is redirected, give the Spectre console a wide `Profile.Width` (doctor,
  gain, log messages); cap `gain`'s rules separately.
- **`dtk config` (L8).** Default the branch to `show`.
- **Doctor duplicates (L9).** Deduplicate checks on the normalised registration path (cwd = home).
- **Generated skill / AGENTS.md section (§6).** Add: the hook already rewrites raw `dotnet` commands; the bypass
  (`\dotnet`, `rtk proxy dotnet`, `DTK_RAW=1`); `dtk log` (latest run, `dtk log build|test`, `--list`); "don't pipe dtk
  output into `tail`/`grep` — the last line is the status"; `dtk pipe`; `-q`. Remove "run `dtk dotnet clean` first
  for a full warning report" (Group 2 makes warnings visible) and demote `--vv` in favour of `dtk log`. Register the
  0.9.0 hashes as released. Update README/docs where they repeat the same advice.

## Testing

Golden tests from real runs replayed through the filters (`dtk pipe <sub> --exit-code N` or the filter tests' own
fixture loader), covering at least: a test project that does not compile (with many errors), passing tests with
build warnings, `--list-tests`, the normal and detailed console loggers (pass and fail), a filter that matches
nothing, a skipped-only assembly alone and in a multi-project run, nested exceptions, multi-line assertion diffs,
Reqnroll pending steps, `fr_FR` culture, and a passing-then-`dtk log` sequence. Hook rewrite cases go in the
rewriter's existing table tests; `-v q` in the preprocessor tests.

## Deferred (tracked; not in 0.10)

| Item | Source | Notes |
|---|---|---|
| Filter for `dotnet tool restore` | review §7 (56 calls, up to 14K chars) | captured today, unfiltered; one line on success → 0.11 (2026-11-04 design) |
| Filter for `dotnet ef migrations …` | §7 (18 calls) | captured today; strip build noise, keep result → 0.11 (2026-11-04 design) |
| Filter for `dotnet stryker` | §7 (36 calls, up to 17K) | attached passthrough today; mutation score + survivors → 0.11 (2026-11-04 design) |
| Filter for `dotnet jb cleanupcode` | §7 (70 calls) | attached passthrough today; changed files only → 0.11 (2026-11-04 design) |
| `dotnet run` startup summary | §7 (90 calls) | attached and long-running; may never fit; closed in the 2026-11-04 design (dropped: no streamed route) |
| `--names` (list passed tests, grouped by class) | M4 | evidence per acceptance criterion → 0.11 (2026-11-04 design) |
| `--output` (test stdout / `ITestOutputHelper` for failures) | M4 | → 0.11 (2026-11-04 design), as `--show-output` |
| `--tee <path>` (raw output to a chosen file, filtered to stdout) | M4 | red/green evidence; strongest candidate for 0.11 → 0.11 (2026-11-04 design) |
| `--max-lines N` with "… N more (dtk log)" footer | H2 | → 0.11 (2026-11-04 design) |
| `--fail-on-empty` for zero matched tests | M3 | opt-in; MTP already exits 8 → 0.11 (2026-11-04 design) |
| `dtk log` header to stderr | C1 option 3 | moot once the slot exists, revisit if needed; closed in the 2026-11-04 design |
| Localized VSTest output through `dtk pipe` | found in verification | wrapped runs unaffected → 0.11 (2026-11-04 design): detected and redirected, not parsed |

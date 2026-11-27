# Field report fixes for 0.14 — design

Date: 2026-11-27. Status: approved by the user 2026-11-27. Branches: `fix/hook-race-and-tail` (items 1 and 3) and
`fix/field-report-output` (items 2, 4, 5 and 6), both off `develop`.

Source: the user's "dtk 0.13.0: field report from three days of agent sessions" (2026-11-23 → 2026-11-26, one
21-project .NET 10 solution on macOS, Claude Code in VS Code, dtk 0.13.0 `osx-arm64` and rtk 1.2.1 both registered
globally as separate `PreToolUse` hooks; about 1,060 dotnet/dtk invocations). Every finding was re-checked against
`develop` at 9b6146f before this was written. The report's suggestions are treated as proposals, not decisions.

Decided by the user (2026-11-27): item 1 is fixed by **refusing a command both hooks would rewrite and handing the
agent the merged command** (approach 1C below), not by folding rtk's registration into dtk's (1A, the user's first
pick, withdrawn once rtk's own warnings came to light). Also decided (2026-11-27): 1C stays even though a re-sent
command loses rtk's automatic approval (*Permissions* below). The docs tell users who rely on that approval which
allow rules to add. "Go on with your pick" covers the rest.

Calls made by the author rather than the user (check these):

- Item 3 also converts an explicit `dtk dotnet … | tail -N` (the report's 25 `… | tail … && next` chains were written
  that way), and puts `--max-lines N` right after the subcommand.
- Item 2 states `0 errors, 0 warnings` only when MSBuild's own summary declared the counts; the test status line adds
  `· 0 build warnings` only when the output shows the run built projects.
- Item 4's grouping key and layout; item 5's layout; item 6's short label for every fallback status line, not just
  `jb`'s.
- The refusal fires only for Claude Code's own payloads, not when Cursor (or Devin) runs Claude Code's hooks.
- Two PRs, released together as 0.14.0. A live Claude Code gate runs first, as a go/no-go for item 1.

## Why

The report found dtk doing its job (936 filtered runs, 7.14M tokens in, 359K out, no wrong verdict), and six places
where it falls short. Two of them lose information an agent needed; the others make the agent spend tokens or distrust
a correct answer:

1. **The rtk and dtk hooks race.** Claude Code runs every matching `PreToolUse` hook in parallel, hands each the
   original command, and applies the `updatedInput` of whichever finishes last (documented: "matching hooks run in
   parallel"; the last-wins behaviour is from anthropics/claude-code#83353, closed without a fix; there is no way to
   chain hooks). For a command with a plain `dotnet …` segment and a segment rtk rewrites, one tool's rewrite is
   lost: dtk's in 38 of the 54 cases the report could tell apart. 36 of the 38 were `dotnet jb cleanupcode …; git
   status`, which lost dtk's list of changed files; one raw `dotnet build --no-incremental` reached the context at
   3.6 KB. Reproduced here with rtk 0.43 and dtk 0.13.0 registered the same way.
2. **A clean build's status line has no counts.** `✓ dotnet build (15 projects, 16.26s)`. Since 0.12 dtk may hide
   warnings in unchanged files, so agents checking a "0 warnings" bar re-read the raw log (`dtk log build --full |
   grep …`, about 15 times in three days).
3. **`| tail -N` stays common, and masks the exit code.** 53% of build/test runs under 0.13 were still piped into
   `tail`/`head` despite the injected rules. 19 failing runs were long enough for `tail` to cut the `FAILURES (n):`
   header and the first failures. The pipe also returns `tail`'s exit code: 25 `dtk dotnet … | tail … && <next>`
   chains, 2 into `git add`.
4. **Identical test failures are listed one by one.** A container-runtime failure failed 3,805 tests with one
   exception at one line; dtk spent its whole failure budget (15 entries, `MaxFailures`; the report's "~70" was
   inferred from the `+N more` count) on copies of it, about 6 KB.
5. **`--names` puts a class's tests on one line**, so `grep` and `--max-lines` cannot work on names.
6. **A fallback status line repeats the whole command.** An unrecognised `jb cleanupcode` error (ReSharper's
   `Unable to find target solution file in path …`) came out at 85 tokens for 29 in, because the fallback's status
   line is built from the full command line (`FilteredOutputPipeline.FallbackStatusLine`).

**What success looks like:** with rtk and dtk both installed, no command run under Claude Code loses either tool's
rewrite; an agent never needs the raw log to learn that a build had no warnings; `… | tail -N` keeps dtk's report
head, status line and dotnet's exit code; one cause of many test failures costs a few lines; `--names` output is
greppable; a fallback's status line stays short.

## Goals

- Under Claude Code, a command that both dtk's and rtk's hooks would rewrite is refused with the merged command as
  the reason; the agent re-sends it and neither hook rewrites it again. Every other command keeps today's reply.
- `dtk doctor` shows whether the dtk on `PATH` handles that case, by probing a mixed command.
- The hook rewrites `dotnet <filtered> … | tail -N` / `| head -N` (and the same after an explicit `dtk dotnet`) to
  `dtk dotnet <filtered> --max-lines N …` without the pipe.
- Build, publish and pack state `0 errors, 0 warnings` on a clean run when MSBuild declared those counts; `dotnet
  test` states `0 build warnings` when it built something.
- The test filter groups failures with the same message and location.
- `--names` prints one test per line under its class.
- Every fallback status line names `dotnet <subcommand>`, not the arguments; the `jb` filter recognises the
  missing-solution error.

## Non-goals

- No change to rtk's registration or to any file rtk owns beyond what `dtk init` already does (`exclude_commands`).
- No fix for the same race in other harnesses that may run rtk's and dtk's hooks side by side (Copilot CLI, Cursor,
  Devin, Droid, Antigravity, Codex). Their multi-hook semantics are unverified; this change records that in
  `docs/articles/harness-verification.md` and goes no further.
- No new agent rule line: the refusal explains itself, and every rule line costs tokens at each session start.
- Not in this change, noted for later: per-project counts on a multi-project `dotnet test` (the report's feature idea);
  a docs note that a direct `sqlite3 tracking.db` read lags until a reader folds the journal; the items still open
  from the 0.13 analysis (`--names` durations, tracking version column, `gain` negative rows, doctor newer-release
  check).

## Anti-goals

- **Never block a command because of dtk's own trouble.** rtk missing, slow, failing or printing something
  unexpected falls back to today's reply, never to a refusal.
- **Never loop.** A refusal is sent only when the merged command is stable under both rewrites, so re-sending it
  cannot be refused again.
- **No new auto-approval.** The Copilot CLI and Cursor gates keep judging the command the agent wrote.
- **Never claim a count dtk did not get.** "0 warnings" on a build comes from MSBuild's own summary, and on a test
  run only from a build the output shows.

## Constraints

- `IsSimpleCommand` and `IsAutoApprovable` stay fail-closed and keep reading the original command (they do today:
  `HookPayloads` passes them `command`, not `rewritten`).
- Codex's hook command and timeout must never change; this change does not touch them.
- Changing a filter's output fails `SavingsBaselineTests` until `update-baseline` runs; the docs examples are
  generated from real runs and guarded by `ExamplesBindingTests`, so they are regenerated too.
- `TreatWarningsAsErrors`, the Sonar parity rules, `CultureInfo.InvariantCulture` for every number a filter prints.
- The native `dtk hook` starts in milliseconds; whatever item 1 adds must be measured and recorded
  (`docs/articles/performance.md`).

## Approaches

### Item 1: the race

- **1A. Fold rtk's entry into dtk's.** `dtk init claude` removes `rtk hook claude` from `settings.json` and registers
  `dtk hook claude --with-rtk`, which runs rtk's rewrite after its own. No extra agent turn. Rejected: rtk then
  reports its hook missing. rtk 0.43 prints `[rtk] /!\ No hook installed — run 'rtk init -g'` in its next command's
  output once a day, i.e. in the agent's context (rtk 1.2.1 can mute that with `[hooks] suppress_hook_warning`), and
  `rtk init --show` says "RTK hook not configured — Run: rtk init -g --auto-patch". rtk recognises only an exact
  `rtk hook claude` command, so a merged entry cannot pass for its own. Anyone following rtk's advice, or its
  upgrade notes, silently brings the race back; only `dtk doctor` would notice. It also cannot fix a global rtk with a
  project-scoped dtk.
- **1B. Return the merged rewrite from dtk's hook and leave both entries.** Still a race: rtk's rtk-only rewrite wins
  part of the time. Rejected.
- **1C. Refuse with the merged command (chosen).** When dtk's hook sees that rtk's hook will also rewrite the command,
  it replies `permissionDecision: "deny"` with the merged command in the reason. Claude Code ranks `deny` above every
  other decision ("precedence is deny > defer > ask > allow"), so the outcome no longer depends on which hook
  finishes last. Works with any rtk version and any mix of scopes, and touches nothing of rtk's. Cost: one extra
  agent turn per mixed command (65 of about 1,060 commands in the report, roughly 150 tokens each). A second cost is
  approval: see *Permissions* under the design.
- **1D. Docs only** (tell agents to write `dtk dotnet …` themselves in mixed commands). Kept as a docs note, not as
  the fix: the rules already ask agents not to pipe into `tail`, and 53% still do.

### Item 3: `| tail -N`

- **Convert to `--max-lines N` (chosen).** Keeps the report's head (where dtk puts what matters), the status line and
  dotnet's exit code; the cap's footer points at `dtk log`.
- **Prefix `set -o pipefail;`.** Fixes the exit code only; `tail` still cuts the report's head. Rejected.
- **Leave it to the rules.** Already tried in 0.13. Rejected.

## Design

### 1. Refusing a command both hooks would rewrite

In `dtk hook claude`'s `PreToolUse` path (`HookPayloads.ReplyToClaude`), for a Claude Code payload, after dtk computes
its rewrite `R1` of the original command `C`. A Claude Code payload means `hook_event_name` is `PreToolUse`,
`tool_name` is `Bash`, and there is no `cursor_version` (that payload keeps Cursor's schema). Devin also runs Claude
Code's hooks and cannot be told apart. Its handling of `deny` is unverified, so `harness-verification.md` lists it
(author's call). The steps:

1. If dtk does not rewrite `C`, reply as today (nothing). rtk's hook alone decides; no race.
2. Is rtk's Claude Code hook registered? Reuse `RtkHookCoexistence.IsRtkHookPresent(projectDirectory)`, with the
   project directory from `CLAUDE_PROJECT_DIR`, else the payload's `cwd`. If not, reply as today.
3. Find `rtk` on `PATH` as an absolute path (`ExecutableSearch`), never a bare `rtk`. None: reply as today.
4. Run `rtk rewrite C` (no shell; `RTK_REWRITE_HOST` removed from its environment, as rtk's own hook script does; a
   short timeout, about 2 s). rtk's protocol: exit 0 or 3 with the rewrite on stdout (0: an allow rule matched, 3:
   ask), 1 no rewrite, 2 a deny rule matched (its hook then passes the command through). Only exit 0 or 3 with
   stdout different from `C` means rtk's hook will rewrite `C`; anything else (1, 2, timeout, crash, empty output)
   means reply as today.
5. Compute the merged command `R2`: `rtk rewrite R1` (exit 0/3 → its stdout; exit 1 → `R1`; anything else → reply as
   today). Check that `R2` is stable: dtk's own rewrite of `R2` is `R2`, and `rtk rewrite R2` returns `R2` or exits
   1. Not stable: reply as today, accepting the race rather than risking a refusal loop.
6. Reply:

   ```json
   {"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny",
     "permissionDecisionReason":"dtk and rtk would each rewrite this command, and Claude Code applies only one hook's rewrite. Run this instead, which carries both:\n<R2>"}}
   ```

   The wording is the builder's, within that sense: say why, give `R2` exactly, keep it short.

**Permissions.** When the user's allow rules cover every command in `C`, `rtk rewrite` exits 0. rtk's hook then
replies `permissionDecision: "allow"`, so the command runs without a prompt. That happens today even when dtk's rewrite
is the one applied, because decisions combine across hooks while `updatedInput` does not. The refusal overrides that
approval. The re-sent `R2` starts with `rtk` where rewritten, and rtk 1.2.1 treats an already-prefixed command as a
no-op and approves nothing (`decision::suppress_identity`). Claude Code then checks the user's own rules against
`R2`, and `Bash(git status:*)` does not match `rtk git status`. An interactive session prompts and a print-mode or
background run is denied; auto and bypass modes are unaffected. The `dtk dotnet …` part is no change: dtk's plain
rewrite is already checked against the user's rules today. The remedy is documented: allow rules for the prefixed
forms the user relies on (`Bash(rtk git status:*)`, `Bash(dtk dotnet:*)`). The docs also warn against a broad
`Bash(rtk:*)`, which would allow `rtk run <anything>`. The refusal's own text stays short and does not repeat this.

This also covers rtk rewriting a `dotnet` segment itself (an rtk config without the `dotnet` exclusion): `R1` has
`dtk dotnet …` there, which rtk leaves alone, so `R2` is still right.

Every rtk call is bounded and fails open. The extra cost falls only on commands dtk rewrites: up to four small
settings reads, then one `rtk rewrite` spawn (three in the refused case). Measure `dtk hook claude` with rtk
registered, for a pure `dotnet build` and a mixed command, and record the figures.

### 1b. Doctor

The existing `rtk coexistence` check (it asks `rtk rewrite "dotnet build"` whether rtk leaves dotnet to dtk) gains a
probe of the installed hook, when rtk's hook is registered and rtk is on `PATH`. It sends the registered `dtk hook
claude` a `PreToolUse` payload for `git status && dotnet build`, and expects a refusal whose reason contains
`dtk dotnet build` and rtk's rewrite of `git status`. If `rtk rewrite "git status"` rewrites nothing on this
machine, that part is skipped. If the reply is a plain rewrite, for example from an older dtk on `PATH`, doctor warns
that mixed commands lose one tool's rewrite and names the remedy (upgrade dtk). The wording is the builder's.

### 3. `| tail -N` and `| head -N` become `--max-lines N`

In `DotnetCommandRewriter.Rewrite` (used by every harness), for a command dtk rewrites, and also for an explicit
`dtk dotnet <sub> …` or `dtk <sub> …` that the rewriter leaves alone today:

- **When.** The first unquoted operator after the command's arguments is `|` or `|&`, into `tail` or `head` with a line
  count and nothing else: `-N`, `-n N`, `-nN`, `--lines=N`, `--lines N`, with N a positive decimal integer. That
  pipeline must end there: end of command, `;`, `&&`, `||`, `&`, a line break or `)`.
- **Rewrite.** Insert `--max-lines N` right after the subcommand words (`dtk dotnet test --max-lines 20 --filter X`),
  before any `--` separator, and drop the `| tail …` segment. Everything else stays, `2>&1` included.
- **Not converted** (the pipe stays, as today): `tail -f`/`-F`, `-n +K`, `head -n -K`, `-c`, a file operand, any
  other option, a further pipe after `tail`/`head`, a command substitution in the arguments, an existing
  `--max-lines`, `--vv` (its raw block is not capped), and every run dtk passes through unfiltered: `--interactive`,
  `-getProperty`/`-getItem`/`-getTargetResult`, `list package --format json`, `--help`/`-h`/`-?`. It also applies only
  to subcommands where dtk reads `--max-lines`. `DTK_RAW=1` and non-filtered subcommands are not rewritten at all
  today, and still are not.
- **Gates.** Unchanged: Copilot CLI still answers `"ask"` for `dotnet test | tail -5`, because `IsSimpleCommand` reads
  the original command, and Cursor still leaves it unrewritten. Tests pin both.
- **Behaviour change, stated in CHANGELOG and docs.** The command now exits with dotnet's code, so `… | tail -5 &&
  git add` stops on a failure; output is the first N lines, the cap's `… K more lines (dtk log)` footer and the status
  line, where `tail` gave the last N.

### 2. Counts on a clean status line

- `MsBuildDiagnosticReport.Render`, no-diagnostic branch (used by build, publish and pack): when MSBuild's summary
  declared the counts (`DeclaredWarnings` is non-null), render `✓ {command}: 0 errors, 0 warnings{context}` (e.g.
  `✓ dotnet build: 0 errors, 0 warnings (15 projects, 16.26s)`), in the same form as the line with warnings.
  Otherwise, for example a quiet build with no summary, render as today.
- `dotnet test`'s success line adds ` · 0 build warnings` when the output shows MSBuild built projects (its `X -> path`
  project-output lines) and no warning was parsed; never on `--no-build` output, a `--list-tests` listing or a
  zero-tests verdict (the same exclusions as `DescribeBuildWarnings` today).
- Docs: the count is what this build reported. MSBuild repeats no warning of a project it found up to date, so a full
  count needs `--no-incremental`. This is true of MSBuild's own summary too.

### 4. Grouping identical test failures

- **Key.** Failures group when their message lines (before compaction) and `SourceRef` are identical, and, under
  `--show-output`, their output lines too. A preset message counts as the message. A group of one renders exactly as
  today.
- **Order** (author's call). Groups of two or more come first, largest first, with ties in order of first appearance.
  Single failures follow in order of first appearance. Otherwise 15 distinct failures printed before the first copy
  of the shared cause would push it into `+N more failures`, the very case this item fixes.
- **Layout** (proposal; the builder may refine it):

  ```text
  FAILURES (3805):
    3749 failed the same way: App.Tests.A.Test1, App.Tests.A.Test2, App.Tests.B.Test3 +3746 more
      Docker.DotNet.DockerApiException : Docker API responded with status code='BadRequest', …
      at tests/App.Testing.Integration/SqlServerFixture.cs:line 29
  ```

  Up to three names; no durations on a group line; a name that failed more than once (several target frameworks)
  shows `×N` once rather than repeating.
- **Budget.** `MaxFailures` (15) counts entries, groups and singles alike; the message budget is shared among the
  entries shown. `+N more failures` counts the tests in the entries not shown.

### 5. `--names`, one test per line

`PassedTestNames.Render`:

```text
✓ Ns.ParserTests (2):
  Parses_Empty
  Parses ×4
```

The class line keeps its count; each method is on its own line, indented two spaces, with `×N` as today. A bare
display name grouped under its assembly gets the same layout.

### 6. Short fallback status lines; the `jb` missing-solution error

- `FilteredOutputPipeline.BuildFallback`/`FallbackStatusLine` name `dotnet` plus the subcommand words dtk matched
  (`dotnet jb cleanupcode`, `dotnet list package`, `dotnet ef migrations`, `dotnet tool restore`), never the
  arguments. This covers all four shapes: failed, unconfirmed `--fail-on-empty`, output not recognised, no output.
  Under `dtk pipe <sub>` the label is the same.
- `DotnetJbCleanupCodeFilter` keeps `Unable to find target solution file in path …` as a recognised line, so that run
  renders as that line plus `✗ dotnet jb cleanupcode failed (exit 1)` (filtered, not `RawTailFallback`).

### Docs

- `CLAUDE.md`: the `PreToolUse` refusal (where it lives, when it fires, that it fails open), the tail/head conversion
  next to `SendsOutputToATextFilterOrFile`, the clean-line counts, the failure grouping, the `--names` layout.
- `docs/articles/ai-agent-setup.md` (rtk coexistence): how mixed commands behave under Claude Code; the
  explicit-prefix note (writing `dtk dotnet …` and `rtk …` yourself avoids the refusal); and the allow rules for the
  prefixed forms, with the `Bash(rtk:*)` warning (*Permissions*).
- `docs/articles/usage.md`: `--max-lines` versus `tail`, the `--names` layout, the build-count caveat.
- `docs/articles/harness-verification.md`: parallel-hook races with rtk are unverified for the other harnesses.
- `docs/articles/performance.md`: the hook figures from item 1.
- `CHANGELOG.md` `[Unreleased]`, one entry per item, each PR carrying its own.

## Verification

- **Gate R (first, go/no-go for item 1).** Real Claude Code, both hooks registered under a scratch `HOME`, not
  `CLAUDE_CONFIG_DIR`. Nothing in dtk reads `CLAUDE_CONFIG_DIR` (init, doctor and `RtkHookCoexistence` all use
  `~/.claude`), and this change keeps that limitation rather than fixing it in the hook alone (author's call). Use a
  sample .NET project and a prompt that runs `dotnet build <sln>; git status --short`. Pass:
  the transcript shows the refusal, the agent re-sends the merged command, and dtk's tracking records the build.
  Run with rtk 0.43 (installed) and rtk 1.2.1 (its release binary in a scratch directory). Run once in print mode with
  allow rules covering `dotnet` and `git status`, and with `Bash(rtk git status:*)` and `Bash(dtk dotnet:*)` added.
  The re-sent command must run, which confirms the documented remedy. If Claude Code does not apply the deny over
  rtk's rewrite, stop and come back to the user.
- Unit tests per item: the refusal's conditions and each fail-open branch (no rtk registration, rtk off `PATH`, exit
  1/2, timeout, unstable merge, Cursor payload); the tail/head forms converted and left alone, and the gates on the
  original command; the clean-line counts with and without a declared summary; grouping keys and layout;
  `--names`; fallback labels; the `jb` pattern.
- Replays: the report's commands (`dotnet jb cleanupcode App.slnx --no-build; git status --short`, `dotnet tool
  restore >/dev/null; dotnet jb cleanupcode …; git status --short`, `dotnet build --no-incremental; git status`)
  through the built `dtk hook claude` with rtk registered.
- `update-baseline` after the output changes, with the diff in the PR; docs examples regenerated; `dtk dotnet test
  DotnetTokenKiller.slnx` green; format verified.

## Left to the builder

- The exact refusal and doctor wording, within the sense above.
- How `ReplyToClaude` gets the project directory and the rtk path (an injected seam, as `DoctorUseCase` does), and the
  rtk timeout's exact value.
- How the rewriter recognises the subcommand words and the pass-through flags for item 3 (reuse
  `DotnetSubcommands`/`PassthroughSubcommands` rules rather than restating them).
- Item 4's exact layout, within the key and budget above; how a group shows `--show-output`.
- Whether `dotnet clean` also takes the item 2 counts (it does if it goes through `Render`; today it does not).
- The order of commits within each PR.

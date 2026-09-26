# Repository review remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the correctness, security, product and repository-health findings of the 2026-09-22 repository review,
on one branch.

**Architecture:** Independent, focused changes across the rewriter (`Application/Integration/Hooks`), the filters
(`Application/Filters`), the tee (`Infrastructure/Tee`), tracking (`Infrastructure/Tracking`), the CLI entry point,
the integrators (`Application/Integration`) and the repository tooling (`.github`, props, docs). Each task leaves the
solution building and its tests passing.

**Tech Stack:** .NET 10, Spectre.Console.Cli 0.55, xunit, FluentAssertions, GitHub Actions.

**Spec:** none — the source is the review summary given to the user on 2026-09-22 (reproduced per task below).

## Global Constraints

- Branch `chore/review-remediation`, worktree `/home/cloudcli/projects/DotnetTokenKiller-remediation`. Work only there.
- **Do not pin GitHub Actions to commit SHAs and do not change any `uses: …@vN` tag** (user instruction). Do not
  change how release tags trigger or version the publish workflow.
- `TreatWarningsAsErrors` with Roslynator, SonarAnalyzer and NetAnalyzers; file-scoped namespaces; `var`; `_camelCase`
  private fields; `Async` suffix; LF, no trailing whitespace, no BOM; 4-space `.cs`, 2-space JSON/YAML/XML.
- No new NuGet packages. The libraries are `IsAotCompatible`: no reflection, use `JsonNode`/source generators.
- NSubstitute cannot proxy the Application assembly's internal interfaces; use hand-written fakes for them.
- Match surrounding code: comment density, XML doc style, naming, test style (xunit `[Fact]`/`[Theory]`,
  FluentAssertions).
- Run dotnet through dtk: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test <test project> --filter …`.
  The hook rewrites literal `dotnet <verb>` in a shell command; to get raw dotnet output use `D=dot; ${D}net …`.
  The build-spawning CLI integration tests (`tests/DotnetTokenKiller.Cli.IntegrationTests`) cannot pass on this
  machine; run the unit test projects and any integration test filter the task names, and say what you could not run.
- If a filter's output changes, `SavingsBaselineTests` fails by design: regenerate with
  `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline` and commit the baseline.
- `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` must pass before each commit.
- Every commit message uses Conventional Commits and ends with
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Update user-facing docs (`README.md`, `docs/articles/*.md`, `CLAUDE.md`) when a task changes documented behavior.

---

### Task 1: Shell-aware command rewriter (security)

**Files:** `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs`,
`tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs`, possibly
`HookPayloads.cs` doc comments.

Problems (confirmed):
1. `IsSimpleCommand` ignores chaining characters inside double quotes, so `dotnet build "$(rm -rf ~)"` and
   ``dotnet build "`id`"`` are "simple", and the Copilot CLI hook (`HookPayloads.cs`, `ReplyToCopilot`) replies
   `permissionDecision: "allow"`, auto-approving arbitrary execution.
2. `Rewrite` never rewrites a `dotnet` invocation inside a double-quoted command substitution:
   `out="$(dotnet build 2>&1)"` stays raw.
3. `Rewrite` rewrites `dotnet build` lines inside heredoc bodies (`cat > s.sh <<'EOF'\ndotnet build\nEOF`), which
   corrupts data. Both quoted (`<<'EOF'`, `<<"EOF"`) and unquoted (`<<EOF`, `<<-EOF`) delimiters: heredoc bodies are
   data for the command reading stdin; never rewrite inside them.
4. An unquoted `#` at a word start begins a comment to end of line; quotes inside it must not toggle quote state
   (`echo 1 # it's fine\ndotnet build` must rewrite the second line; `echo 1 # dotnet build` must not rewrite).

Requirements:
- Replace the per-index `IsInsideQuotes` rescans with a single left-to-right scanner that tracks: single quotes,
  double quotes, `$(`…`)` nesting (a substitution inside double quotes starts a fresh command context where quotes
  and command positions apply again), backticks, `#` comments, and heredoc bodies. It must stay linear time.
- `IsSimpleCommand` returns false whenever the command contains any command substitution (`$(` or a backtick)
  anywhere outside single quotes, any heredoc, or any unquoted chaining character — it must be conservative: when
  unsure, not simple. `$((…))` arithmetic may be treated as not simple (conservative is fine).
- Keep all existing test cases passing, except where an existing case encodes one of the bugs above — change that
  case and say so in the report. Update the class `<remarks>`: it is no longer a behavior-identical port of the
  Python hook; describe the shell constructs it understands.
- Add tests for every example above plus: nested `$(echo "$(dotnet test)")`, `dotnet build '$(x)'` (single-quoted,
  inert → simple and rewritten), an escaped `\$(`, a heredoc followed by a real `dotnet build` after the terminator
  line, `<<-` with tab-indented terminator, and ``dotnet build "`id`"`` → not simple.

### Task 2: Gemini hook must not auto-approve unrelated commands

**Files:** `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs` (`ReplyToGemini`,
`GeminiAllowReply`), its tests, `docs/articles/ai-agent-setup.md` if it describes the reply.

`ReplyToGemini` replies `{"decision":"allow"}` for every shell call, rewritten or not. Research Gemini CLI's
`BeforeTool` hook contract (context7 / official google-gemini/gemini-cli docs `docs/hooks`): determine whether
`"allow"` bypasses the user's confirmation prompt and what a neutral reply (no decision, empty object, or exit 0 with
no output) does, and whether a rewrite can be expressed without `allow`. Then:
- If `allow` bypasses confirmation: change both the no-rewrite and the rewrite replies to the neutral form that
  leaves the user's own permission policy in charge (the Antigravity reply in the same file sets the precedent and
  explains why). If a rewrite cannot be delivered without `allow`, keep `allow` only when
  `DotnetCommandRewriter.IsSimpleCommand(original)` is true (the Copilot precedent), and neutral otherwise.
- If it does not bypass confirmation: change no behavior; add a comment citing the contract that makes `allow` safe.
- Record the evidence (URLs, quoted contract text) in the report. Update tests accordingly.

### Task 3: Parse Microsoft.Testing.Platform `dotnet test` output

**Files:** `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`, its tests, new fixtures in the
test-filter fixture directory used by `DotnetTestFilterTests` (find it), savings corpus/baseline if fixtures are
shared with it.

Confirmed with a real .NET 10.0.301 SDK + MSTest 4.0.2 run using `"test": {"runner": "Microsoft.Testing.Platform"}` in
`global.json`: a passing run through `dtk pipe test` prints **nothing**, and a failing run (1 failed, 1 passed) prints
`dotnet test: 1 failed, 0 passed`. Real output shape (verbatim tail of the failing run):

```
Running tests from /tmp/mtpprobe/Probe/bin/Debug/net10.0/Probe.dll (net10.0|x64)
failed Fails (13ms)
  Assert.AreEqual failed. Expected:<1>. Actual:<2>. 'expected' expression: '1', 'actual' expression: '2'.
  from /tmp/mtpprobe/Probe/bin/Debug/net10.0/Probe.dll (net10.0|x64)
  Assert.AreEqual failed. Expected:<1>. Actual:<2>. 'expected' expression: '1', 'actual' expression: '2'.
    at Probe.Test1.Fails() in /tmp/mtpprobe/Probe/Test1.cs:6
/tmp/mtpprobe/Probe/bin/Debug/net10.0/Probe.dll (net10.0|x64) failed with 1 error(s) (156ms)

Test run summary: Failed!
  total: 2
  failed: 1
  succeeded: 1
  skipped: 0
  duration: 321ms
```

and the passing run ends with `…Probe.dll (net10.0|x64) passed (198ms)`, a blank line, `Test run summary: Passed!`
and the same five indented fields. Requirements:
- Capture real fixtures yourself (all-pass, one-failure, zero-tests via `--filter` matching nothing, and two test
  projects in one solution) from a throwaway MTP project under `/tmp` using the real SDK, as above (MSTest template
  plus `<EnableMSTestRunner>true</EnableMSTestRunner><OutputType>Exe</OutputType>` and the global.json runner
  setting). Normalize machine paths the way existing fixtures do.
- Parse the multi-line `Test run summary:` block (total/failed/succeeded/skipped/duration, durations in ms/s/m),
  the `Zero tests ran` verdict, and don't duplicate the failure message the MTP output prints twice. Keep the existing
  single-line pattern only if a real SDK ever produced it; otherwise replace it and say so.
- Passing MTP run → the usual `✓` summary line; failing → FAILURES block with correct passed count.
- Update the savings baseline if the corpus changes.

### Task 4: Strip ANSI once, in the pipeline

**Files:** `src/DotnetTokenKiller.Application/Filters/*.cs`, `UseCases/FilteredOutputPipeline.cs`,
`UseCases/PipeFilterUseCase.cs`, benchmark savings engine callers, filter tests.

`FilteredOutputPipeline` strips ANSI (`AnsiStrip.Strip`) and then every filter's `Apply` strips again. Make one owner:
first list every caller of each filter's `Apply` (pipeline, pipe use case, benchmarks/savings engine, tests). If every
production caller passes stripped text, remove the per-filter strip, rename the `Apply` parameter so it says the input
is already stripped, and make any caller that did not strip do so. Filter unit tests that feed ANSI input must strip
first or move to pipeline-level tests. Savings baseline must not change (if it does, find out why before updating).

### Task 5: Build diagnostic dedup must not merge distinct projects

**Files:** `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` (`TryAddDiagnosticLine`), its tests.

The dedup key `{shortened file}({line},{col}):{code}:{message}` omits the `[project]` suffix to collapse the same
diagnostic across target frameworks. Two different projects with the same relative-path file (linked
`GlobalUsings.cs`, etc.) and identical diagnostics collapse into one. Include the project path (without the TFM part,
e.g. `[/p/A.csproj::TargetFramework=net8.0]` → `/p/A.csproj`) in the key so cross-TFM duplicates of one project
still collapse but different projects do not. Look at how the `[project]` suffix appears in the existing fixtures
first. Add a regression test for each behavior. Update the baseline if savings move.

### Task 6: Filters for `dotnet publish` and `dotnet pack`

**Files:** `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`, `PassthroughSubcommands.cs`, `FilterKeys.cs`,
`src/DotnetTokenKiller.Application/Filters/` (new filters), `src/DotnetTokenKiller.Cli/Commands/` (new commands and
settings, registration in `Program.cs`/wherever `DotnetBuildCommand` is registered), completion
(`CompletionCommand.cs`), docs (`README.md`, `docs/articles/usage.md`, generated examples if a test demands them),
tests, savings corpus fixtures + baseline.

Model everything on how `build` is wired end to end (grep every place `DotnetBuildCommand`, `FilterKeys.Build` and
`DotnetSubcommands.Build` appear and mirror each). The publish/pack filters reuse `DotnetBuildFilter`'s diagnostic
parsing (extract a shared helper rather than duplicating it) and on success print one summary line with the
diagnostic counts plus the output locations (`X -> /path/publish/` lines and `Successfully created package '…'`
lines), shortened with the existing path helper. On failure, the same error layout as build. Capture real fixtures
from `samples/` with the real SDK (success, warnings, failure). Remove `publish` and `pack` from the passthrough
measurable list if they are there, and keep `--interactive` runs working (check how build treats stdin). Because the
hook rewriter uses `DotnetSubcommands.Sorted`, the hook will now rewrite `dotnet publish`/`dotnet pack`: make sure
rewriter tests reflect that. `CommandSettingsAotGuardTests` must pass (no new option kinds without extending
`AotParityTests`).

### Task 7: Don't capture interactive `dotnet ef` prompts

**Files:** `src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs`, its tests.

Passthrough measures `ef` runs by capturing them with stdin closed. `dotnet ef database drop` prompts for
confirmation unless `--force`/`-f` is given, and `dotnet ef migrations remove` may prompt too. Research which `dotnet ef`
commands prompt (EF Core tools docs) and make `IsMeasurable` return false for those runs unless a non-interactive flag
is present, so they run with the terminal attached. Add tests.

### Task 8: Mark truncated tee logs

**Files:** `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeSession.cs`, `src/DotnetTokenKiller.Domain/Tee/*`
(header), `src/DotnetTokenKiller.Cli/Formatting/TeeLogRenderer.cs`, tests.

When a session's body reaches `MaxFileSizeBytes`, writes stop silently and the log still finalizes as `Complete`.
Reserve room for, and write, one marker line `[dtk: output truncated at <N> bytes]` when the cap is first hit, record
truncation in the header (a new header field, backward compatible with logs that lack it), and make `dtk log` show a
warning for truncated logs like it does for `Running` ones. Tests for the session, the header round-trip (including an
old header without the field), and the renderer.

### Task 9: CLI entry point: encoding guard and Ctrl+C

**Files:** `src/DotnetTokenKiller.Cli/Program.cs`, `PassthroughEntryPoint.cs`, tests.

1. `Console.OutputEncoding = Encoding.UTF8;` runs outside the `try` and throws `IOException` when there is no console
   handle (Windows child process without a console). Guard it so failure is ignored (output encoding stays default).
2. `app.RunAsync(args)` gets no cancellation token, so Ctrl+C never reaches commands and `ProcessCommandRunner`'s
   kill-tree / tee-finalize logic only runs in tests. Wire `Console.CancelKeyPress` (first press: cancel the token and
   set `e.Cancel = true` so dtk can kill the child tree, finalize the tee and record the run; a second press: let the
   process die) and SIGTERM (`PosixSignalRegistration`, AOT-safe) to a `CancellationTokenSource`, and pass the token
   to `app.RunAsync(args, token)` (check the Spectre.Console.Cli 0.55 API) and to the passthrough entry point. The exit
   code after cancellation should be 130 (SIGINT convention) unless the child already reported one — check what the
   commands return on `OperationCanceledException` today and keep it consistent. Read
   `docs/superpowers/specs/2026-07-29-streaming-tee-durability-design.md`'s deferred note first. The `dtk hook` path
   stays untouched. Add tests where feasible (the cancellation plumbing through a command with a fake runner).

### Task 10: Split `SqliteTracker`

**Files:** `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (780 lines) and new files beside it,
DI registration, tests.

Pure refactor, no behavior change: extract schema creation/migration (`InitializeSchemaAsync`, `EnsureColumnAsync`)
into `SqliteSchemaMigrator`, journal fold orchestration (`FoldAsync`, `FoldLockedAsync`, `IsFoldCommittedAsync`,
`CommitFoldAsync`, `FoldInBackgroundAsync`) into `SqliteFoldCoordinator`, and the query execution plus row mappers
(`ExecuteWithFilterAsync`, `ReadSummaryAsync`, `ReadHistoryAsync`, `ReadCoverageAsync`) into `SqliteQueryReader`.
`SqliteTracker` keeps its public surface and delegates. Keep them `internal sealed`, keep the semaphore/connection
ownership in one place, keep AOT safety. All existing tracking tests pass unchanged (only test-internal references
may move). Do not change SQL text.

### Task 11: Windows tree-kill backstop test

**Files:** `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs` (taskkill backstop, currently
`[ExcludeFromCodeCoverage]` and `// Stryker disable all`), a new Windows-only test in the infrastructure test project.

Add a test that runs only on Windows (skip elsewhere, using the same skip mechanism the repo already uses for
platform-specific tests) which starts a command whose shell spawns a long-lived grandchild (e.g. `cmd /c` starting
`powershell -NoProfile -Command Start-Sleep 60` or `ping -n 60 127.0.0.1`), cancels, and asserts that every process
in the tree is gone (capture the grandchild PID via a marker the child prints). It will not run locally; make it
compile and skip cleanly on Linux, and check the workflow that runs infrastructure tests on Windows
(`fallback-package.yml`) actually runs this project — if not, say so in the report rather than rewiring CI.

### Task 12: `dtk init <provider> --uninstall`

**Files:** `src/DotnetTokenKiller.Cli/Commands/InitCommand.cs` + settings, `src/DotnetTokenKiller.Application/Integration/**`
(`IHookIntegrator`, `IntegratorHelpers`, every integrator, `IntegrateUseCase`), tests, docs
(`README.md` uninstall section, `docs/articles/ai-agent-setup.md`).

Today nothing removes dtk's own hook entries, generated plugins, skills or instruction sections; `dotnet tool
uninstall` leaves harnesses calling a missing `dtk`. Add `--uninstall` to `dtk init <provider>` (respecting
`--global`) that removes exactly what that provider's install writes, symmetric with the write path:
- JSON hook registrations: remove only dtk's entries (reuse the existing entry-matching used to detect equivalent
  entries); drop containers left empty; delete the file only if dtk created it and it is now empty (`{}`).
- Generated stamped files (`ArtifactStamping`: OpenCode plugin, skills, hook scripts): delete only when the stamp proves
  dtk generated them and the content was not modified by the user; otherwise leave and report.
- Instruction sections in shared files (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, copilot instructions, etc.): remove
  dtk's marked section only; delete the file if nothing else remains and dtk created it (if that can't be known, keep
  the file).
- TOML (Codex `config.toml`): remove only what dtk added, if anything.
- Report per artifact: Removed / Unchanged (not present) / Kept (modified by user), in the same output style as install.
- Idempotent: a second uninstall reports nothing to remove. Install → uninstall → the tree equals the original
  (test this for every provider with a temp directory).
Study the install path of every integrator first; this is the largest task — keep the shared logic in
`IntegratorHelpers` (or a new sibling helper) rather than per-integrator copies.

### Task 13: CI workflow hygiene

**Files:** `.github/workflows/*.yml`, new `eng/` script or `.github/actions/` composite action.

Do not touch any `uses:` tag or the tag trigger/versioning of `publish.yml`.
- Add `concurrency` (`group: ${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}`,
  `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`) to `ci.yml`, `codeql.yml`, `sonarqube.yml`,
  `mutation-testing.yml`, `benchmarks.yml` as appropriate (never cancel pushes to `develop` or tag builds);
  `publish.yml` gets `concurrency: publish-${{ github.ref }}` without cancel-in-progress.
- Add `timeout-minutes` to jobs that lack one (`ci.yml` build, `codeql.yml`), sized from their normal duration.
- `sonarqube.yml`: fix the two caches (one must be `~/.sonar/cache`), key the scanner cache so the scanner updates
  (e.g. include the month or the scanner version), pass `SONAR_TOKEN`/`SONAR_HOST_URL`/`SONAR_PROJECT_KEY` via `env:`
  instead of `${{ }}` inside `run:`, remove the stale `_bmad` exclusions.
- `codeql.yml`: `build-mode: none` for C#.
- `doc-publish.yml`: add a `pull_request` trigger that builds but does not deploy, narrow `paths` to what docfx
  consumes (`docs/**` minus `docs/superpowers/**`, `README.md`, `src/**`), and don't restore unrelated local tools if
  avoidable.
- Remove dead steps in `ci.yml` (OS info echo; coverage collection whose output nothing consumes — verify first).
- De-duplicate the RID matrix shared by `ci.yml` and `publish.yml`, and the Git Bash / pwsh / cmd smoke-test steps
  shared by `aot-package.yml` and `fallback-package.yml` (an `eng/` script or a local composite action). Keep job
  names and required-check names stable where possible; list any changed check names in the report.
- Validate every workflow with `actionlint` if available (install to /tmp if not), else a YAML parse.

### Task 14: Repository configuration hygiene

**Files:** `Directory.Build.props`, `Directory.Packages.props`, `samples/SampleApp.BadPackage/*.csproj`,
`NuGet.config`, `.github/dependabot.yml`, `.markdownlintignore`, `.github/copilot-instructions.md`,
`.githooks/pre-commit`, new `.github/CODEOWNERS`, new `.github/ISSUE_TEMPLATE/config.yml`.

- `Directory.Build.props` `<Version>0.5.0</Version>` is stale (latest release 0.8.0; publish passes `-p:Version`).
  Set a dev default that can never be mistaken for a release (`0.0.0-dev`) — check nothing (tests, examples, docs,
  update checks) depends on the value first.
- Move `DotnetTokenKiller.DoesNotExist` out of the central `Directory.Packages.props` (use `VersionOverride` in the
  sample project, confirming central package management still accepts it and the sample still fails restore as its
  purpose requires).
- SQLitePCLRaw mixes 2.1.11 (`bundle_winsqlite3`) with 3.x packages: check whether a 3.x `bundle_winsqlite3` exists on
  nuget.org and whether mixing is intentional (read the comments in the props file and git log). Align if safe and it
  builds; otherwise document why in a props comment. Report which.
- `NuGet.config`: add `<clear/>` and `packageSourceMapping` for nuget.org (`*`).
- `dependabot.yml`: split the nuget group so samples / test frameworks / analyzers / runtime are separate groups, and
  group GitHub Actions updates.
- `.markdownlintignore`: forward slashes, drop the removed `_bmad` entry.
- `.github/copilot-instructions.md`: fix the reference to the nonexistent `.github/hooks/dtk-dotnet.json`.
- `.githooks/pre-commit`: replace `grep -P` with portable constructs (works with BSD grep).
- `CODEOWNERS` (`* @HandyS11`), `ISSUE_TEMPLATE/config.yml` (`blank_issues_enabled: false`, a security contact link
  to the repository's security advisories page, consistent with `SECURITY.md`).

### Task 15: Changelog and release notes

**Files:** new `CHANGELOG.md`, new `.github/release.yml`, CLI csproj package metadata, `README.md` link.

- `CHANGELOG.md` in Keep a Changelog format with an `Unreleased` section describing this branch's user-visible changes
  and one section per released tag (`git tag`, `git log <prev>..<tag> --oneline`), written from the Conventional
  Commit subjects (Added/Changed/Fixed), newest first.
- `.github/release.yml` with categories by label (features, fixes, dependencies, other) for GitHub's generated notes.
- Set `PackageReleaseNotes` in the CLI csproj to point at the changelog URL on GitHub (check existing package metadata
  properties first).

### Task 16: Slim `CLAUDE.md`, move measurement history

**Files:** `CLAUDE.md`, new `docs/articles/performance.md`, `docs/articles/toc.yml`.

`CLAUDE.md` is 22 KB, about 60% dated measurement history. Move every dated measurement narrative (Benchmarks
section measurement paragraphs, Native AOT / Static SQLite / Windows shim / `dtk hook` / OpenCode plugin figures)
verbatim into `docs/articles/performance.md` (with a short intro, linked from the docs toc). Leave in `CLAUDE.md` only
instructions, rules and "do not" constraints (e.g. timings are never gated, `tokenizer-load` must stay out of process,
the AOT suppressions, the fcntl64 shim rules, musl RIDs must stay), each with a one-line pointer to
`docs/articles/performance.md` for figures. Add to `CLAUDE.md` short notes for behavior this branch introduced
(publish/pack filters, `init --uninstall`, Ctrl+C handling) where a future agent needs them. Keep all commands. Check
docfx builds if a docfx tool is available (`dotnet tool restore` then `dotnet docfx docs/docfx.json` or as
`doc-publish.yml` does).

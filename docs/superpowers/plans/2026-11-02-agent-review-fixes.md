# Agent-review fixes (0.10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every dtk answer trustworthy on failing runs — never empty, never stale, never ignoring a raw-output
request — and keep the cause of a failing test or build intact, as specified for 0.10.0.

**Architecture:** Changes stay inside the existing filter/pipeline/tee/rewriter seams. `FilteredOutputPipeline` owns
the output contract (summary last, never empty); `MsBuildDiagnosticReport` gains a small public surface the test
filter reuses for compile errors and warnings; the tee keeps one passing log per subcommand kind; every hook bypass
lives in `DotnetCommandRewriter.Rewrite`; `DTK_RAW` routes to the existing passthrough path.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + Verify snapshots, Spectre.Console.Cli, Native AOT.

**Spec:** `docs/superpowers/specs/2026-11-02-agent-review-fixes-design.md` — read it first; this plan argues from
it.

## Global Constraints

- One branch `fix/agent-review-0.10`, one PR into `develop`. Commit after every task; never push without asking.
- `TreatWarningsAsErrors` is on; file-scoped namespaces, `var`, `_camelCase` fields, `…Async` names, LF, no BOM.
- Run commands through `dtk` (`dtk dotnet test <csproj> --filter "FullyQualifiedName~X"`). Prefer a test project
  path over the `.slnx` for filtered runs. For raw output use `\dotnet`.
- Every line any filter writes ends with `\n` (normalise `AppendLine`'s `\r\n`), and the **last line of every
  filtered run is its one status/summary line** (spec, Group 1 output contract).
- All numbers rendered with `CultureInfo.InvariantCulture`.
- **Fixtures are real output.** Capture with raw `\dotnet …` against `samples/*` or a scratch project, replace the
  machine path with `/test/project/root`, save under `tests/DotnetTokenKiller.Application.Tests/Fixtures/`. Never
  hand-write a fixture line. Delete the hand-written `Passed!`-for-skipped-only test input (spec, Hard constraints).
- When a task changes filter output: update the Verify snapshots (`*.verified.txt`), re-render the dtk blocks in
  `samples/examples/*.md` and `README.md` from their own Raw blocks (`ExamplesBindingTests` must pass), and run
  `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline`. Any
  successful-run scenario losing more than one percentage point gets a sentence in the commit body (kept for the PR).
- Never change: the Codex hook command/timeout, the byte-pinned plugin bodies, tee header keys,
  `IsSimpleCommand`/`IsAutoApprovable` results.
- No new Spectre settings option.

## Review Focus

1. **A `dotnet test` run over a solution where one project fails to compile and others pass** — the agent must see
   both the compile errors and the pass/fail counts, ending with one failed status line. Pinned in Task 5.
2. **Two concurrent dtk runs of the same kind finishing at once** (agents run build and test in parallel) — slot
   pruning must never delete the other run's log while it is still `Running`, nor any failing log above the floor.
   Pinned in Task 6.
3. **`--vv` together with the new summary-last rule** — `--vv` prints the raw dump *before* the filtered output, so
   the last line is still the status line. Pinned in Task 1.
4. **Emoji disabled / `NO_COLOR`** — `NormalizeGlyphs` must still turn the new `⚠`/`✗` status lines into `WARN:`/
   `FAIL:`, including the new raw-tail-on-success line. Pinned in Task 1.
5. **A hook command where `rtk proxy` appears but does not run dotnet** (`rtk proxy ls && dotnet build`) — the
   second `dotnet` is still rewritten; only the `dotnet` directly run by `rtk proxy`/`command`/`DTK_RAW=1` is skipped.
   Pinned in Task 7.

---

## Group 1 — trust

### Task 1: Output contract in the pipeline

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs` (`ProcessAsync`,
  `BuildRawTailFallback`)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs`

**Interfaces:**
- Produces: the fallback shapes every later task relies on —
  failed: `<last 40 raw lines>\n✗ {command} failed (exit {N})\n`;
  successful but empty filter output: `<last 40 raw lines>\n⚠ {command}: output not recognized, raw tail shown above\n`.
  The `--show-log` hint (`[full output: …]`) is written **before** the final status line on every path.

- [ ] **Step 1: Write the failing tests** (use the file's existing fake filter/tracker helpers)

```csharp
[Fact] public async Task ProcessAsync_EmptyFilterOutputOnSuccess_WritesRawTailThenWarningLast()
// filter returns "", exit 0, raw "a\nb\n" → output == "a\nb\n⚠ dotnet test: output not recognized, raw tail shown above\n"
[Fact] public async Task ProcessAsync_RawTailFallbackOnFailure_EndsWithStatusLine()
// filter "", exit 1, raw 60 lines → 41 lines, lines[0] == "line 20", last == "✗ dotnet build failed (exit 1)"
[Fact] public async Task ProcessAsync_ShowLogHint_PrecedesStatusLine()
// filter returns "detail\n✓ dotnet build (1 project)\n", ShowLogHint, session hint "[full output: /x.log]"
// → "detail\n[full output: /x.log]\n✓ dotnet build (1 project)\n"
[Fact] public async Task ProcessAsync_VeryVerbose_StatusLineStillLast()
[Fact] public async Task ProcessAsync_EmojiDisabled_RawTailWarningUsesAscii()
// → last line starts with "WARN: "
```

- [ ] **Step 2: Run them, expect FAIL** — `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FilteredOutputPipelineTests"`

- [ ] **Step 3: Implement.** Fallback triggers on `string.IsNullOrWhiteSpace(filtered)` for any exit code
  (`RunOutcome.RawTailFallback` either way). `BuildRawTailFallback(command, exitCode, raw, logHint)` writes tail,
  then hint, then the status line. Insert the hint for filtered output before its last line with a private
  `InsertBeforeLastLine(string text, string line)`. Keep the `FilterFaulted` precedence comment true.

- [ ] **Step 4: Run the class, expect PASS;** then the whole Application test project (other filters' "returns
  empty" tests may now assert the pipeline differently — fix the assertion, not the contract).

- [ ] **Step 5: Commit** `fix: end every filtered run with its status line, never print nothing`

### Task 2: `Skipped!` summaries

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (`SummaryPattern`, `FormatOutput`)
- Create fixtures: `dotnet_test_skipped_only.txt` (one xUnit project, all tests `[Fact(Skip=…)]`),
  `dotnet_test_multiproject_skipped.txt` (one passing project + one skipped-only project)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`

**Interfaces:**
- Produces: skipped-only status `✓ dotnet test: 0 passed, {S} skipped ({P} project[s], {elapsed})` — replaces
  `{S} skipped, 0 executed`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void Apply_SkippedOnlyFixture_ReportsSkippedCount()
// exit 0 → "✓ dotnet test: 0 passed, 2 skipped (1 project, <d>)\n" (assert with StartWith + EndWith(" project, ") free regex)
[Fact] public void Apply_MultiProjectWithSkippedOnlyProject_CountsBothProjects()
// → contains "passed, 2 skipped (2 projects"
```
Delete the test at the hand-written skipped-only input (currently around line 1013) — the fixture replaces it.

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement:** `SummaryPattern` accepts `Passed|Failed|Skipped`; the skipped-only branch renders the
  new status with project count and elapsed.
- [ ] **Step 4: Run `DotnetTestFilterTests`, expect PASS.**
- [ ] **Step 5: Commit** `fix(test): read VSTest's Skipped! summary`

### Task 3: Normal/detailed console logger and `--list-tests`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`
- Create fixtures (real runs of `samples/SampleApp.Tests` or a scratch xUnit project with 3 pass / 1 skip / 1 fail):
  `dotnet_test_logger_normal_pass.txt`, `dotnet_test_logger_detailed_pass.txt`,
  `dotnet_test_logger_detailed_fail.txt`, `dotnet_test_list_tests.txt` (`--list-tests`, two projects)
- Test: `DotnetTestFilterTests.cs`

**Interfaces:**
- Produces: list status `✓ dotnet test --list-tests: {N} tests ({P} project[s])`, preceded by one test name per
  line, no indent, in listed order.

- [ ] **Step 1: Failing tests**

```csharp
[Theory] [InlineData("dotnet_test_logger_normal_pass.txt")] [InlineData("dotnet_test_logger_detailed_pass.txt")]
public void Apply_ConsoleLoggerPass_ReportsCounts(string fixture)   // exit 0 → StartsWith("✓ dotnet test: 4 passed, 1 skipped (1 project")
[Fact] public void Apply_DetailedLoggerFail_ReportsTrueCounts()     // exit 1 → last line StartsWith("dotnet test: 1 failed, 3 passed, 1 skipped")
[Fact] public void Apply_ListTests_PrintsNamesThenCount()          // last line "✓ dotnet test --list-tests: N tests (2 projects)", N == names listed
```
(Adjust counts to the captured fixture; assert the counts the raw output states.)

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Parse the `Test Run Successful.`/`Test Run Failed.` block (`Total tests: N`,
  `Passed: N`, `Failed: N`, `Skipped: N`, `Total time: 1.9929 Seconds`) into the same `ParseState` counts, once per
  block, `ProjectCount++` per block; ignore the per-test `Passed X [1 ms]` lines for counting. A
  `The following Tests are available:` header switches to list mode: collect the indented names until the next
  non-indented line. Record `Total time` as an *exact* duration (see Task 14).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `fix(test): parse the console logger summary and --list-tests`

### Task 4: `MsBuildDiagnosticReport` surface for other filters

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/MsBuildDiagnosticReport.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` (report is internal; tests
  already reach it through the build filter — add an `InternalsVisibleTo`-backed direct test only if one exists)

**Interfaces:**
- Produces (internal, on `MsBuildDiagnosticReport`):
  - `int ErrorTotal { get; }`, `int WarningTotal { get; }` — the existing `_errorTotal`/`_warningTotal`
  - `int FailedProjectCount { get; }` — distinct projects (csproj file name without extension, TFM suffix dropped)
    among error diagnostics; 0 when no error carries a project
  - `IReadOnlyList<(string Code, int Count)> WarningCodes { get; }` — by count desc, then code ordinal
  - `string RenderDiagnostics()` — the grouped body only (errors by file, or warnings when there are no errors),
    no status line, no separator; empty when nothing parsed
  - `Render(command, exitCode, outputs)` keeps its signature; Task 15 changes its shape.

- [ ] **Step 1: Failing tests** on the `dotnet_build_errors.txt` / `dotnet_build_warnings.txt` fixtures:
  `ErrorTotal`, `FailedProjectCount == 1`, `WarningCodes[0]` is the most frequent code, `RenderDiagnostics()` does not
  contain `"dotnet build:"`.
- [ ] **Step 2–4: Fail, implement by extracting the body from `Render`, pass.**
- [ ] **Step 5: Commit** `refactor: expose the diagnostic report's counts and body`

### Task 5: Compile errors inside `dotnet test`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (`Apply`, `FormatOutput`)
- Create fixtures: `dotnet_test_compile_errors.txt` (a test project with ≥ 30 errors, e.g. copy
  `samples/SampleApp.MultiError`'s errors into a scratch test project), `dotnet_test_compile_errors_mixed.txt`
  (solution: one test project fails to compile, another runs with ≥ 1 failing test)
- Test: `DotnetTestFilterTests.cs`

**Interfaces:**
- Consumes: Task 4's `ErrorTotal`, `FailedProjectCount`, `RenderDiagnostics()`.
- Produces: no-summary status `✗ dotnet test: {F} project[s] did not build ({E} error[s])`; mixed status
  `dotnet test: {f} failed, {p} passed[, {s} skipped] · {F} project[s] did not build ({E} error[s]) ({P} project[s], {elapsed})`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void Apply_CompileErrors_RendersGroupedErrorsAndTotal()
// exit 1 → contains no "/test/project/root", no " -> ", no "Determining projects"; last line == "✗ dotnet test: 1 project did not build (E errors)" with E = MSBuild's tally
[Fact] public void Apply_CompileErrorsWithTestResults_ShowsBoth()
// → contains a "FAILURES (" block and a "(N errors)" file group; last line contains "failed," and "did not build"
```

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** In `Apply`, parse `MsBuildDiagnosticReport.Parse(strippedOutput, RootPath)` alongside
  the test parse. When `report.ErrorTotal > 0`: no test summary → body + no-summary status; with a summary → body,
  then the failures block, then the mixed status. Keep the exit code as the verdict source.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `fix(test): render compile errors like dotnet build does`

### Task 6: `dtk log` keeps the latest run of each kind

**Files:**
- Modify: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeSession.cs` (`FinalizeCoreAsync`),
  `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` (new `PruneSuperseded`),
  `src/DotnetTokenKiller.Infrastructure/Tee/TeeSessionPolicy.cs` (add `string Slug = ""`)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeSessionTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/LogIntegrationTests.cs`

**Interfaces:**
- Produces: `internal static void FileTeeService.PruneSuperseded(string teeDir, string slug, string keptFilePath,
  long minBodyBytes, bool dropPassing)` — deletes logs with the same slug and an **older** file-name timestamp than
  `keptFilePath` when (`dropPassing` and header exit code 0) or (complete and body < `minBodyBytes` and not
  truncated). Never deletes a log whose header is still `Running` or that fails to parse.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] FinalizeAsync_PassingRunInFailuresMode_IsKept()               // file exists, header exit 0, hint returned
[Fact] FinalizeAsync_PassingRun_ReplacesOlderPassingLogOfSameSlug()   // older passing "test" log gone
[Fact] FinalizeAsync_PassingRun_KeepsOlderFailingLogOfSameSlug()      // older failing (>500 B) log kept
[Fact] FinalizeAsync_PassingRun_KeepsLogsOfOtherSlugs()               // passing "build" log kept after a "test" run
[Fact] FinalizeAsync_SmallestRun_IsKeptDespiteMinimumSize()           // 10-byte body kept
[Fact] FinalizeAsync_DoesNotDeleteConcurrentRunningLog()              // an older same-slug log still "Running" survives
```
Integration (`LogIntegrationTests`): failing `dtk pipe test --exit-code 1` then passing `dtk pipe test --exit-code 0`
(both with `DTK_TEE_DIR` set), then `dtk log` shows the passing run's header (`exit 0`). If `dtk pipe` opens no tee
session, use `dtk dotnet build` on `samples/SampleApp` instead.

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** In `FinalizeCoreAsync`, stop deleting on (`KeepOnlyOnFailure && exitCode == 0`) and on
  the size floor; finalize the header as today, then `PruneSuperseded(policy.TeeDir, policy.Slug, filePath,
  policy.MinBodyBytes, policy.KeepOnlyOnFailure)` inside the existing best-effort `try`, then `TryRotateFiles()`.
  `FileTeeService.BeginAsync` passes `Slug: TeeLogFileName.Sanitize(commandSlug)`. Header reads use
  `TeeLogHeader.TryParse` on the file's leading text; body size via `TeeLogHeader.StripHeader`.
- [ ] **Step 4: Run Infrastructure tee tests and `LogIntegrationTests`, expect PASS.** Update tests that asserted
  the old deletion (they encode the bug).
- [ ] **Step 5: Commit** `fix(log): keep the latest run of each kind so dtk log is never stale`

### Task 7: Raw-output bypass

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs` (`IsCommandPosition`),
  `src/DotnetTokenKiller.Cli/Program.cs`, `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs`
- Test: the rewriter's existing table tests (find with `rtk proxy grep -rl "DotnetCommandRewriter" tests`),
  `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/PassthroughIntegrationTests.cs`

**Interfaces:**
- Produces: `internal static bool ArgumentPreprocessor.IsRawRequested(string[] args, string? dtkRawValue)` — true
  when `args[0]` is `dotnet` and the value is non-empty and not `0`/`false` (ordinal-ignore-case);
  `internal static string[] ArgumentPreprocessor.StripDtkOptions(string[] dotnetArgs)` — removes dtk-only flags
  (`--vv`, `--show-log`, `-q`, `--quiet`, `--verbose`, and `-v` **not** followed by a verbosity value — Task 8),
  stopping at the user's `--`.

- [ ] **Step 1: Failing tests**

```text
Rewrite cases (input → expected):
  rtk proxy dotnet test X            → unchanged
  rtk.exe proxy dotnet build         → unchanged
  command dotnet test X              → unchanged
  DTK_RAW=1 dotnet test X            → unchanged
  DTK_RAW=0 dotnet test X            → DTK_RAW=0 dtk dotnet test X
  rtk proxy ls && dotnet build       → rtk proxy ls && dtk dotnet build
  env dotnet build / sudo dotnet build / time dotnet build → still rewritten
  \dotnet test X ; /usr/local/share/dotnet/dotnet test X  → unchanged (pin today's behaviour)
Auto-approval: IsSimpleCommand/IsAutoApprovable results for all of the above equal today's.
```
```csharp
[Fact] IsRawRequested_RecognisesOneAndRejectsZero()
[Fact] StripDtkOptions_RemovesDtkFlagsButKeepsUserSeparatorTail()
[Fact] DtkRaw_BuildOfBrokenSample_PrintsRawCompilerLines()   // integration: DTK_RAW=1 dtk dotnet build samples/SampleApp.Broken → output contains "error CS" and an absolute path
```

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `IsCommandPosition` returns false when the previous word is `command`, a
  `DTK_RAW=<v>` assignment with a raw-enabling value, or `proxy` preceded by `rtk`/`rtk.exe`. In `Program.cs`, right
  after the passthrough check: `if (ArgumentPreprocessor.IsRawRequested(args, Environment.GetEnvironmentVariable("DTK_RAW")))`
  → `PassthroughEntryPoint.RunAsync(dotnetCmd, ArgumentPreprocessor.StripDtkOptions(args[1..]), runCancellation)`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: honour rtk proxy, command and DTK_RAW=1 as raw-output requests`

### Task 8: `-v <level>` goes to dotnet

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` (`InsertSeparator`, `StripDtkOptions`)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Theory] [InlineData("q")] [InlineData("quiet")] [InlineData("m")] [InlineData("minimal")] [InlineData("n")]
[InlineData("normal")] [InlineData("d")] [InlineData("detailed")] [InlineData("diag")] [InlineData("diagnostic")] [InlineData("Q")]
public void InsertSeparator_VerbosityValueAfterV_StaysWithDotnet(string level)
// ["dotnet","test","-v",level] → ["dotnet","test","--","-v",level]
[Fact] public void InsertSeparator_BareV_IsStillDtkVerbose()   // ["dotnet","test","-v","--filter","X"] → ["dotnet","test","-v","--","--filter","X"]
```
- [ ] **Step 2–4: Fail, implement (look ahead one token, case-insensitive set), pass.**
- [ ] **Step 5: Commit** `fix(cli): leave -v <level> to dotnet`

### Task 9: "No test matched" names the filter

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (`NoTestsPattern`, `ParseState`,
  `FormatZeroTests`)
- Test: `DotnetTestFilterTests.cs` (fixtures `dotnet_test_zero.txt` exist; snapshot updates)

**Interfaces:**
- Produces: `⚠ dotnet test: no test matched filter {expr} ({a.dll}[, {b.dll}])` and
  `⚠ dotnet test: no test found ({a.dll})`; exit code unchanged.

- [ ] **Step 1: Failing test** — `Apply_ZeroFixture_NamesFilterAndAssembly`: `dotnet_test_zero.txt`, exit 0 →
  `"⚠ dotnet test: no test matched filter FullyQualifiedName~NonExistentTest (DotnetTokenKiller.Domain.Tests.dll)\n"`.
  `dotnet_test_multiproject_partial_match.txt` still reports its real results (existing test stays green).
- [ ] **Step 2–4: Fail; capture the backticked expression and the dll file name (distinct, in order); pass.**
- [ ] **Step 5: Commit** `fix(test): say which filter matched no test`

### Task 10: rtk's real config path, and doctor's rtk check

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/RtkConfigLocator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs` (constructor, path use),
  `src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/RtkConfigLocatorTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/DoctorUseCaseTests.cs`

**Interfaces:**
- Produces:
  - `internal static string RtkConfigLocator.PlatformDefault(OSPlatform os, string home, string? xdgConfigHome, string? appData)`
    — Linux: `$XDG_CONFIG_HOME` (absolute only) or `~/.config`, + `rtk/config.toml`; macOS:
    `~/Library/Application Support/rtk/config.toml`; Windows: `%APPDATA%\rtk\config.toml`.
  - `internal static string? RtkConfigLocator.ParseConfigCommandOutput(string stdout)` — the path after
    `Config:` on its own line, trimmed; null if absent.
  - `internal static Task<string> RtkConfigLocator.ResolveAsync(ICommandRunner runner, CancellationToken ct)` —
    runs `rtk config` when `ExecutableSearch.FindOnProcessPath("rtk")` finds it, else (or on any failure) the
    platform default.
  - `RtkHookCoexistence(ICommandRunner runner)` replaces the parameterless constructor (DI resolves it); the
    `(string userClaudeDir, string rtkConfigPath)` test constructor stays.
  - Doctor check name `rtk coexistence`: present only when an rtk hook is registered
    (`RtkHookCoexistence.IsRtkHookPresent(projectDirectory)`); passes when `rtk rewrite "dotnet build"` prints
    nothing, otherwise a warning: `rtk also rewrites dotnet commands. Add "dotnet" to [hooks].exclude_commands in
    {path}, or run 'dtk init claude' again.`

- [ ] **Step 1: Failing tests** — `PlatformDefault` for the three OSes (macOS case:
  `/Users/u/Library/Application Support/rtk/config.toml`), `ParseConfigCommandOutput("Config: /a b/c.toml\n…")`,
  doctor warning when a fake runner makes `rtk rewrite` print `rtk dotnet build`, no check when no rtk hook.
- [ ] **Step 2–4: Fail, implement, pass.** Doctor never writes rtk's config.
- [ ] **Step 5: Commit** `fix(init): write rtk's exclusion where rtk reads it; doctor warns on a double rewrite`

## Group 2 — test failures keep their cause

### Task 11: Nested exceptions and assertion diffs

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (`CompactMessage`, `MessageMaxLen`)
- Create fixtures: `dotnet_test_nested_exception.txt` (outer message > 250 chars, inner
  `ArgumentException("THE-INNER-CAUSE")`), `dotnet_test_assert_diffs.txt` (xUnit `Assert.Equal` on collections and
  on strings)
- Test: `DotnetTestFilterTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Fact] Apply_NestedException_KeepsInnermostCause()   // contains "---- System.ArgumentException : THE-INNER-CAUSE"
[Fact] Apply_CollectionDiff_KeepsHeaderAndPosition() // contains "Assert.Equal() Failure: Collections differ at pos 0 — Expected: [1, 2, 3], Actual: [9, 2, 3]"
[Fact] Apply_StringDiff_KeepsPosition()              // contains "Strings differ at pos 9"
```
(Use the literal values the captured fixture prints.)
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `MessageMaxLen` becomes 300. The last `---- ` segment is kept whole (itself capped at
  200); the text before it is truncated to the remaining budget. For an `Expected:`/`Actual:` pair: keep the first
  line when it starts with `Assert.` (the header), turn a `↓ (pos N)` line into ` at pos N`, collapse runs of spaces
  after `Expected:`/`Actual:` to one, join as `{header}{ at pos N} — Expected: …, Actual: …`.
- [ ] **Step 4: PASS.  Step 5: Commit** `fix(test): keep the inner exception and the diff position`

### Task 12: Reqnroll pending steps

**Files:**
- Modify: `DotnetTestFilter.cs` (`ParseFailure` / `FindSourceRef` read `Standard Output Messages:`)
- Create fixture: `dotnet_test_reqnroll_pending.txt` from `samples/SampleApp.Tests.Reqnroll` with one undefined step
- Test: `DotnetTestFilterTests.cs`

- [ ] **Step 1: Failing test** `Apply_ReqnrollPendingStep_ShowsStepText`: message is
  `No matching step definition: {step text as printed}` (several steps joined with `; `), and contains no
  `[Binding]`.
- [ ] **Step 2–4: Fail; detect Reqnroll's "No matching step definition found" message and read the step lines from
  the failure's standard output section; pass.**
- [ ] **Step 5: Commit** `fix(test): show a pending Reqnroll step's text`

### Task 13: Build warnings on test summaries

**Files:**
- Modify: `DotnetTestFilter.cs` (pass and failure status lines)
- Create fixture: `dotnet_test_pass_with_warnings.txt` (CS0219 ×2 + CS8602 in a passing test project)
- Test: `DotnetTestFilterTests.cs`

**Interfaces:**
- Consumes: Task 4's `WarningTotal`, `WarningCodes`.
- Produces: suffix ` · {W} build warning[s] ({code}×{n}, {code}[, …])` appended to the status line, at most three
  codes (`×n` only when n > 1), `, …` when more codes exist.

- [ ] **Step 1: Failing test** — exit 0 → `"✓ dotnet test: 2 passed (1 project, <d>) · 3 build warnings (CS0219×2, CS8602)\n"`.
- [ ] **Step 2–4: Fail, implement, pass;** regenerate the `dotnet_test_mtp_all_pass` snapshot (it carries
  MSTEST0032 ×3).
- [ ] **Step 5: Commit** `feat(test): report build warnings on test summaries`

### Task 14: Durations and culture

**Files:**
- Create: `src/DotnetTokenKiller.Application/Helpers/DurationFormat.cs`
- Modify: `DotnetTestFilter.cs:343`, `MsBuildDiagnosticReport.cs:192`, `DotnetRestoreFilter.cs:165`,
  `DotnetFormatFilter.cs:112`, `Helpers/TextHelpers.cs:27-28`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/DurationFormatTests.cs`, plus a `fr-FR` test per filter

**Interfaces:**
- Produces: `internal static string DurationFormat.Format(double milliseconds, bool exact)` — invariant culture;
  `< 1000 ms` → `0.34s`; `exact` and `< 60 s` → `2.34s`; not `exact` and `< 60 s` → `12s`; `≥ 60 s` → `1m40s`
  (seconds rounded; hours as `1h02m03s`).
- The test filter's duration is exact when every parsed part was in `ms` or had a decimal point (MTP, `Total time`),
  not exact for VSTest's whole-unit `Duration: 1 s` / `1 m 40 s`.

- [ ] **Step 1: Failing tests** — `Format(340, true) == "0.34s"`, `Format(1000, false) == "1s"`,
  `Format(100_000, false) == "1m40s"`, `Format(2340, true) == "2.34s"`; with `CultureInfo.CurrentCulture = fr-FR`
  the test and build filters render `0.02s`, never `0,02s`.
- [ ] **Step 2–4: Fail, implement, pass;** regenerate snapshots/examples/baseline (every test scenario's tokens
  move).
- [ ] **Step 5: Commit** `fix: invariant numbers and honest duration precision`

## Group 3 — one layout for diagnostics

### Task 15: Summary last in the MSBuild report

**Files:**
- Modify: `MsBuildDiagnosticReport.cs` (`Render`), `DotnetPublishFilter.cs`, `DotnetPackFilter.cs` (only if they
  assume order)
- Test: `DotnetBuildFilterTests.cs`, `DotnetPublishFilterTests.cs`, `DotnetPackFilterTests.cs`; snapshots;
  `samples/examples/BUILD.md`, `README.md`

**Interfaces:**
- Produces (final line of `Render`): success, nothing to report: `✓ {command}{context}` (unchanged, single line);
  success with warnings: `✓ {command}: 0 errors, {W} warning[s]{context}`; non-zero exit:
  `✗ {command} failed (exit {N}): {E} error[s], {W} warning[s]{context}`. Output lines (publish dirs, packages) sit
  directly above it. No `---` separator.

- [ ] **Step 1: Failing tests** — errors fixture: last line `✗ dotnet build failed (exit 1): …`, exactly one line
  contains `dotnet build`; warnings-only with exit 1: one `✗` line, last.
- [ ] **Step 2–4: Fail, implement, pass;** re-render BUILD.md/README dtk blocks; snapshots; `update-baseline`.
- [ ] **Step 5: Commit** `fix(build): end the diagnostic report with its verdict`

### Task 16: Unified layout, Top codes, project count

**Files:**
- Modify: `MsBuildDiagnosticReport.cs` (`AppendGroupedByCode` → removed, `AppendGroupedByFile`, `AppendTopCodes`,
  project counting)
- Create fixture: `dotnet_build_multitarget_errors.txt` (a two-TFM project with one error) if none covers it
- Test: `DotnetBuildFilterTests.cs`

- [ ] **Step 1: Failing tests**
  - warnings-only output is grouped by file: `{file} ({n} warning[s])` then `  ({line},{col}) {CODE}: {msg}`;
  - `Top codes:` absent when one distinct code; present only when distinct codes ≥ 2 **and** `ErrorTotal` exceeds
    the number of error lines rendered;
  - a failed single-project build's status shows `(1 project, …)`; a two-TFM project counts once.
- [ ] **Step 2–4: Fail, implement, pass.** Project count = distinct names from `X -> …` lines ∪ csproj names in
  diagnostics' `[project]` suffix (TFM part dropped).
- [ ] **Step 5: Commit** `fix(build): one layout for errors and warnings`

### Task 17: Full messages, grouped once

**Files:**
- Modify: `MsBuildDiagnosticReport.cs` (`MessageMaxLen`, `TryAddDiagnosticLine`, `AddSimpleDiagnosticLine`,
  rendering)
- Create fixture: `dotnet_build_long_messages.txt` (CS1503 ×3 on long generic types, a CS0121, an analyzer warning
  with a help URL)
- Test: `DotnetBuildFilterTests.cs`

- [ ] **Step 1: Failing tests**
  - identical code+message in one file render once: `  (96,59) (99,59) (111,59) CS1503 ×3: {full message}`;
  - CS1503 keeps its `to '…'` part; CS0121 keeps both overloads;
  - no `http://`/`https://` in output;
  - inside single-quoted spans `System.`/`Microsoft.` namespace prefixes are dropped
    (`'System.Collections.Generic.IEnumerable<int>'` → `'IEnumerable<int>'`); user namespaces are kept.
- [ ] **Step 2–4: Fail, implement, pass.** Cap: 400 chars for errors, 160 for warnings, applied **after** URL
  stripping (remove `https?://\S+` and an emptied `()`/`[]` around it) and prefix shortening. Dedup key unchanged in
  spirit (rendered message).
- [ ] **Step 5: Commit** `fix(build): print each full message once`

### Task 18: Suppressed-warnings wording

**Files:** `MsBuildDiagnosticReport.cs`; test `DotnetBuildFilterTests.cs`

- [ ] **Step 1: Failing test** — errors plus 6 warnings: a line `6 warnings not shown (dtk log)` (singular
  `1 warning not shown (dtk log)`); no `--vv` and no `(N shown)` in that line.
- [ ] **Step 2–5: Fail, implement, pass, commit** `fix(build): point hidden warnings at dtk log`

### Task 19: Format filter

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs`
- Create fixture: `dotnet_format_whitespace_many.txt` (real `--verify-no-changes` run with ≥ 12 whitespace
  violations over 3 files)
- Test: `DotnetFormatFilterTests.cs`; snapshots; `samples/examples/FORMAT.md`

**Interfaces:**
- Produces: per group `{file}: {CODE} ×{n} ({l}:{c}[ {replacement}], …)` (`×n` only when n > 1); final line
  `✗ dotnet format: {V} violation[s] in {F} file[s]` on non-zero exit (`⚠` on exit 0 with violations); success lines
  unchanged. `internal static string DotnetFormatFilter.SummarizeReplacement(string escaped)`: `\n` → `newline`,
  `\r\n` → `newline`, `\t` → `tab`, runs of `\s` → `{k} space[s]`, joined with ` + `, other text quoted.

- [ ] **Step 1: Failing tests** — no `.csproj]` in output; `SummarizeReplacement("\\n\\s\\s\\s\\s") ==
  "newline + 4 spaces"`; a three-violation group renders on one line; last line is the `✗` status.
- [ ] **Step 2–4: Fail, implement (strip the trailing `[…]` from `rest`; group by file then code), pass.** Drop the
  10-line cap: grouping bounds the size.
- [ ] **Step 5: Commit** `fix(format): group violations by file and code`

### Task 20: Restore, clean and `list package` end with their summary

**Files:** `DotnetRestoreFilter.cs`, `DotnetCleanFilter.cs`, `DotnetListPackageFilter.cs`; their tests, snapshots,
`samples/examples/RESTORE.md`

- [ ] **Step 1: Failing tests** — restore errors: last line `✗ dotnet restore: {n} error[s]`; clean failure: last
  line `✗ dotnet clean: {n} error[s]`; each `list package` report ends with its summary line (move the existing one;
  add `✓ dotnet list package ({n} project[s])` where none exists).
- [ ] **Step 2–5: Fail, implement, pass, regenerate, commit** `fix: summary last for restore, clean and list package`

### Task 21: No negative savings

**Files:** `FilteredOutputPipeline.cs` (`TrackIfEnabledAsync`); test `FilteredOutputPipelineTests.cs`

- [ ] **Step 1: Failing test** — empty raw input, filter writes `✓ dotnet format (nothing to format)` → recorded
  `SavedTokens == 0` and `SavingsPct == 0`.
- [ ] **Step 2–5: `savedTokens = Math.Max(0, input - output)`; pass; commit** `fix(gain): clamp saved tokens at zero`

## Group 4 — CLI and instructions

### Task 22: `dtk test` / `dtk build`

**Files:** `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` (`Normalize`); test `ArgumentPreprocessorTests.cs`,
`CommandDispatchIntegrationTests.cs`

- [ ] **Step 1: Failing tests** — `Normalize(["test","--filter","X"]) == ["dotnet","test","--filter","X"]`;
  `["list","package"]` → prefixed; `["log","build"]`, `["reset"]`, `["gain"]` unchanged; integration: `dtk build
  samples/SampleApp` exits 0 with a `✓ dotnet build` line and no "Did you mean".
- [ ] **Step 2–5: Prefix `dotnet` when `DotnetSubcommands.TryMatch(args, …)` matches and `args[0]` is not
  `dotnet`; pass; commit** `feat(cli): accept dtk test, dtk build and friends`

### Task 23: Doctor and config polish

**Files:** `src/DotnetTokenKiller.Cli/Program.cs` (console width), `CliConfigurator.cs` (config default),
`src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (dedup); tests `CliConfiguratorTests.cs`,
`HookHealthCheckerTests.cs`, a `dtk doctor | cat` integration test

- [ ] **Step 1: Failing tests**
  - `dtk doctor` with redirected stdout and a 150-char tee path prints that path on one line;
  - `dtk config` exits 0 and prints the `show` output;
  - with project directory == home, each Claude hook check appears once.
- [ ] **Step 2–4: Implement:** when `Console.IsOutputRedirected`, give the console `Profile.Width = 4096`
  (cap `gain`'s rules at 80 separately); `config.SetDefaultCommand<ConfigShowCommand>()` in the config branch;
  skip a global installation whose normalised `RegistrationPath` equals an already-checked project one.
- [ ] **Step 5: Commit** `fix(cli): no hard wrap when piped, dtk config shows, doctor dedups`

### Task 24: Generated skill and instructions

**Files:** `src/DotnetTokenKiller.Application/Integration/SharedInstructionArtifacts.cs`,
`IntegrationInstructions.cs` (text + released hashes), `docs/articles/usage.md`, `docs/articles/ai-agent-setup.md`,
`README.md`; tests `SharedInstructionArtifactsTests.cs`, `ReleasedOwnedFileHashesTests`, `SubcommandBindingTests`,
`DocsBindingTests`

- [ ] **Step 1: Failing tests** — rendered skill contains `dtk log`, `DTK_RAW=1`, `\dotnet`, `rtk proxy dotnet`,
  `dtk pipe`, `-q`, and the sentence "Don't pipe dtk output into tail or grep: its last line is the status."; does not
  contain `dtk dotnet clean` first-advice nor `--vv` in the flags table.
- [ ] **Step 2: Rewrite the text** with the points in spec Group 4: the hook already rewrites raw `dotnet`; the bypass
  forms; `dtk log` (latest run per kind, `dtk log build|test`, `--list`); no piping; `dtk pipe`; `-q`; `dtk log`
  instead of `--vv`.
- [ ] **Step 3: Register the 0.9.0 bodies' hashes** in `ReleasedCursorRuleHashes`/`ReleasedMarkdownRuleHashes` (and
  any sibling list the skill uses) following those lists' remarks — compute them from the v0.9.0 tag
  (`git show v0.9.0:…`), labelled `// 0.9.0` or by extending the last range comment if unchanged since 0.7.0.
- [ ] **Step 4: Run the Application and Cli test projects, expect PASS.**
- [ ] **Step 5: Commit** `docs: teach agents dtk log, the bypass and not to pipe`

### Task 25: Changelog and final verification

- [ ] **Step 1:** Add the 0.10 entries under `## [Unreleased]` in `CHANGELOG.md` (Fixed / Added / Changed),
  following the hand-written style of `[0.9.0]`. No version file changes (the tag sets it).
- [ ] **Step 2:** `dtk dotnet build DotnetTokenKiller.slnx` → `✓`; `dtk dotnet test DotnetTokenKiller.slnx` → all
  pass (CLI integration tests per class if the solution run times out); `dtk dotnet format DotnetTokenKiller.slnx
  --no-restore --verify-no-changes` → clean.
- [ ] **Step 3:** Replay the review's repro list end to end with the locally built dtk on `PATH` (passing-then-`dtk
  log`, `--list-tests`, compile error in test, `rtk proxy` hook probe, `-v q`, `fr_FR`) and record the outputs in the
  PR description.
- [ ] **Step 4: Commit** `docs: changelog for 0.10`

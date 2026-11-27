# Field-report output fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A clean build says `0 errors, 0 warnings`, a test run that built says `0 build warnings`, identical test
failures are listed once with their count, `--names` prints one test per line, and a fallback status line names only
the subcommand. The `jb` filter recognises ReSharper's missing-solution error.

**Architecture:** Four independent output changes, in `MsBuildDiagnosticReport`, `DotnetTestFilter`,
`PassedTestNames`, `FilteredOutputPipeline` and `DotnetJbCleanupCodeFilter`. Each one comes with its savings-baseline
regeneration, its docs-example update and its CHANGELOG line.

**Tech Stack:** C# / .NET 10, xUnit v3 + FluentAssertions + Verify snapshots, the benchmarks corpus.

**Spec:** `docs/superpowers/specs/2026-11-27-field-report-0.14-design.md`, items 2, 4, 5 and 6. Branch:
`fix/field-report-output`, off `develop`.

## Global Constraints

- Changing a filter's output fails `SavingsBaselineTests` until you run `dotnet run -c Release --project
  benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline`. Run it in the task that changed the output, and commit
  the diff of `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` with that task.
- `ExamplesBindingTests` runs each `**Raw**` block of `samples/examples/{BUILD,TEST,RESTORE,FORMAT}.md` and of
  `README.md` through the real filter and compares the result with the `**dtk**` block. Update those blocks to the new
  output; there is no generator. Never edit a Raw block.
- Counts the spec calls "declared" come only from MSBuild's own summary. Both its `N Warning(s)` and `N Error(s)`
  lines must be present.
- Every printed number uses `CultureInfo.InvariantCulture`. Repo rules: `TreatWarningsAsErrors`, the Sonar parity
  rules, file-scoped namespaces, `var`, `_camelCase`, LF line endings.
- Build, test and format with `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`
  (`--filter "FullyQualifiedName~ClassName"` for one class), and `dtk dotnet format DotnetTokenKiller.slnx
  --no-restore --verify-no-changes`.
- The hook rewrites `dotnet build` even in text sent through Bash, so write prose with Write/Edit, not `cat <<EOF`.

## Review Focus

1. **A build with no MSBuild summary** (`-v q`, `-clp:NoSummary`, a localized SDK) must not claim zero counts. Task 2
   pins this with `Apply_CleanBuildWithOnlyOneTallyLine_StatesNoCounts` and keeps
   `Apply_NoElapsedNoProjects_NoContextSuffix` as is.
2. **Identical failures read on Windows** (CRLF) must still group. Task 4:
   `Apply_IdenticalFailuresWithCrlf_AreGrouped`.
3. **The same test failing under two target frameworks** is one name with `×2`, not two names. Task 4:
   `Apply_SameTestFailingTwice_IsNamedOnceWithItsCount`.
4. **Existing tests that assume one entry per failure** (the cap and budget tests) must keep testing the cap, not
   quietly start testing grouping. Task 4, Step 3 gives each of their failures its own message.
5. **A fallback under `dtk pipe`** keeps the label `dotnet <subcommand>` it already has. Task 5 runs
   `PipeFilterUseCaseTests` and `PipeIntegrationTests.PipeJbCleanupCode_UnrecognisedFailure_ShowsTheRawTailAsync`
   unchanged.

---

### Task 1: Branch

- [ ] **Step 1:** `git checkout develop && git pull --ff-only && git checkout -b fix/field-report-output`.
- [ ] **Step 2:** Run `git checkout fix/hook-race-and-tail --
  docs/superpowers/specs/2026-11-27-field-report-0.14-design.md
  docs/superpowers/plans/2026-11-27-field-report-output.md`, then `git commit -m "docs: the 0.14 output fixes' spec
  and plan"`. The other branch holds the same spec, byte for byte, so whichever PR merges second sees no conflict.

### Task 2: Counts on a clean build, publish and pack

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/MsBuildDiagnosticReport.cs`: the ctor (:63-99), `Render`'s clean
  branch (:236-248), and `ParsedOutput` (:1080).
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs`, its snapshot
  `Snapshots/DotnetBuildFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`, and
  `MsBuildDiagnosticReportTests.cs`.
- Docs: `samples/examples/BUILD.md:41,69`, `docs/articles/usage.md:34-43`, `CHANGELOG.md`.

**Interfaces:**
- Produces: `public bool DeclaresCounts { get; }` on `MsBuildDiagnosticReport`. It is true when the parse found both
  MSBuild's `N Error(s)` and `N Warning(s)` lines (`DeclaredErrors` and `DeclaredWarnings` both non-null).

- [ ] **Step 1: Write the failing tests.**
  - Set the snapshot `Apply_SuccessFixture_MatchesSnapshot` to `✓ dotnet build: 0 errors, 0 warnings (6 projects,
    2.34s)`. The fixture `dotnet_build_success.txt` carries MSBuild's summary.
  - `Apply_CleanBuildWithMsBuildsSummary_StatesZeroCounts`: the input is `"  App -> /r/bin/App.dll\n\nBuild
    succeeded.\n    0 Warning(s)\n    0 Error(s)\n"`, with exit 0. The output is `"✓ dotnet build: 0 errors, 0
    warnings (1 project)\n"`.
  - `Apply_CleanBuildWithOnlyOneTallyLine_StatesNoCounts`: the same input without the `0 Error(s)` line gives `"✓
    dotnet build (1 project)\n"`.
  - `MsBuildDiagnosticReportTests.Render_CleanRunWithDeclaredCounts_StatesThemBeforeTheContext`. Input: a publish
    output with one `App -> bin/publish/` line and both tally lines; outputs `["  App -> bin/publish/"]`. The result
    ends with `"✓ dotnet publish: 0 errors, 0 warnings (1 project)\n"`.
- [ ] **Step 2: Run them to verify they fail.** `--filter
  "FullyQualifiedName~DotnetBuildFilterTests|FullyQualifiedName~MsBuildDiagnosticReportTests"`. Expected: the new
  tests and the snapshot fail. The inline clean-build tests without a summary still pass.
- [ ] **Step 3: Implement.** Store `DeclaresCounts` from `ParsedOutput`. In the clean branch, when it is true, render
  `$"✓ {command}: 0 errors, 0 warnings{_context}"`; otherwise render as today.
- [ ] **Step 4: Run the class tests, then the whole suite.** Expected: everything passes, except `SavingsBaselineTests`
  (`build/success` moved) and `ExamplesBindingTests` (BUILD.md's two clean examples).
- [ ] **Step 5: Regenerate the baseline and the examples.** Run `update-baseline`. Then set BUILD.md:41 to `✓ dotnet
  build: 0 errors, 0 warnings (1 project, 1.19s)` and :69 to `✓ dotnet build: 0 errors, 0 warnings (2 projects,
  1.34s)`, or to whatever the failing binding test prints, which wins. Re-run the suite: green.
- [ ] **Step 6: Docs.** In `usage.md`'s build section, show the clean form. Add the caveat: the count is what this
  build reported, MSBuild repeats no warning of an up-to-date project, and `--no-incremental` gives a full count. In
  `CHANGELOG.md` `[Unreleased]` `### Changed`, add one entry.
- [ ] **Step 7: Commit.** `git commit -m "feat: state 0 errors, 0 warnings on a clean build that MSBuild counted"`,
  with the attribution line, and the baseline and example diffs included.

### Task 3: `· 0 build warnings` on a passing test run that built

**Files:**
- Modify: `MsBuildDiagnosticReport.cs`: `CountProjects` (:832), and `ProjectOutputPattern` (:1014).
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`: `FormatOutput` (:880-951), the success line
  at :949-950.
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`.
- Docs: `usage.md:74`, `samples/examples/TEST.md` and `README.md`, where the binding test requires it, and
  `CHANGELOG.md`.

**Interfaces:**
- Produces: `public int BuiltProjectCount { get; }` on `MsBuildDiagnosticReport`: how many distinct project names
  MSBuild's `X -> ….dll|exe` lines named. Projects named only in diagnostics do not count.

- [ ] **Step 1: Write the failing tests.**
  - `Apply_PassingRunThatBuiltProjects_StatesZeroBuildWarnings`: a passing VSTest output (copy the shape of an
    existing passing inline test) with the line `"  App.Tests -> /r/bin/App.Tests.dll"`. The status line ends with
    `) · 0 build warnings\n`.
  - `Apply_PassingRunWithoutProjectOutputLines_AddsNoBuildWarningCount`: the `--no-build` shape. The status line is
    today's.
  - `Apply_FailingRunThatBuiltProjects_AddsNoZeroCount`: the failure line is today's.
  - `Apply_ListTestsThatBuiltProjects_AddsNoZeroCount`
  - `Apply_PassingRunWithBuildWarnings_StillCountsThem`: an existing ` · 3 build warnings (…)` case, unchanged.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~DotnetTestFilterTests"`.
- [ ] **Step 3: Implement.** Expose `BuiltProjectCount` from the output names `CountProjects` already gathers. On the
  success line only (:950), when `Warnings()` is empty and `buildReport.BuiltProjectCount > 0`, append ` · 0 build
  warnings`. Leave `DescribeBuildWarnings` and every other caller unchanged.
- [ ] **Step 4: Update expectations.** Existing passing-run tests whose input has `->` lines now end in `· 0 build
  warnings`. Update their expected strings, and nothing else.
- [ ] **Step 5: Run the suite, regenerate the baseline, update the examples.** As in Task 2, Steps 4 and 5. Expected:
  green.
- [ ] **Step 6: Docs and commit.** In `usage.md:74`, describe the zero count and when it is shown, and add a
  `### Changed` entry to the CHANGELOG. Then `git commit -m "feat: state 0 build warnings on a passing test run that
  built"`.

### Task 4: Group identical test failures

**Files:**
- Modify: `DotnetTestFilter.cs`: `AppendFailureBlock` (:1211-1237) and `FormatFailures` (:1156), plus a new private
  grouping step beside them.
- Test: `DotnetTestFilterTests.cs`. Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_identical_failures.txt`,
  and a savings scenario in `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenarios.cs`. Use the same
  fixture location the `test/failures` scenario uses.
- Docs: `usage.md:74`, `samples/examples/TEST.md` and `README.md:61-68` if the binding test requires it, and
  `CHANGELOG.md`.

**Interfaces:**
- Consumes: `FailureInfo(string TestName, string Duration, IReadOnlyList<string> MessageLines, string? PresetMessage,
  string SourceRef, IReadOnlyList<string> Output)` (:1663). `ParseState.Failures` is a `List<FailureInfo>`.
- Produces, private to the filter:
  - `sealed record FailureGroup(IReadOnlyList<FailureInfo> Members)`.
  - `static List<FailureGroup> GroupFailures(IReadOnlyList<FailureInfo> failures, bool showOutput)`. The key is
    `PresetMessage ?? string.Join('\n', MessageLines)`, plus `SourceRef`, plus, when `showOutput`, `string.Join('\n',
    Output)`, compared ordinally. The order: groups of two or more first, by size descending, ties by first
    appearance; then the single failures, in order of appearance.

The layout for a group of n ≥ 2, where `names` are its distinct test names in order of appearance, each with ` ×k`
when it occurs k > 1 times, at most three of them, followed by ` +{d} more` when d further distinct names exist:

```text
  {n} failed the same way: {names}
    {message, compacted as today}
    at {SourceRef}            (only when not empty, as today)
    output: …                 (once, only under --show-output, as today)
```

A group of one renders exactly as today. `MaxFailures` (15) counts entries. The message budget is divided among the
entries shown, exactly as it is divided among failures today. `+N more failures` is `Failures.Count` minus the
members of the entries shown, printed only when positive.

- [ ] **Step 1: Write the failing tests.** Use inline VSTest blocks, as `Apply_MoreThanMaxFailures_ShowsPlusMoreLine`
  (:353) does. Copy the path shortening from `Apply_SummaryReportsMoreFailuresThanParsedHeaders_UsesSummaryCount`
  (:1124).
  - `Apply_IdenticalFailures_AreGroupedWithTheirCountAndFirstNames`. Five failures, `MyTests.T1` to `T5`, all with
    message `boom` at `Test.cs:line 1`, and the summary `Failed: 5`. The output is exactly:
    `"FAILURES (5):\n  5 failed the same way: MyTests.T1, MyTests.T2, MyTests.T3 +2 more\n    boom\n    at
    ../../../path/Test.cs:line 1\n✗ dotnet test: 5 failed, 0 passed (1 project, 0.01s)\n"`.
  - `Apply_LargestGroupComesFirst_ThenSinglesInOrder`. The input order is S1 (`a`), then G1, G2, G3 (`b`), then S2
    (`c`). The output lists the `b` group, then S1, then S2.
  - `[Theory] Apply_FailuresDifferingInMessageOrLocation_StayApart`
  - `Apply_ManyGroups_PlusMoreCountsTheTestsNotShown`. Sixteen distinct messages with two failures each: 15 group
    entries, then `+2 more failures`.
  - `Apply_ShowOutput_GroupsOnlyIdenticalOutputs`
  - `Apply_SameTestFailingTwice_IsNamedOnceWithItsCount`: `  2 failed the same way: MyTests.T1 ×2`.
  - `Apply_IdenticalFailuresWithCrlf_AreGrouped`
  - Add the fixture: real VSTest shape, taken from `dotnet_test_failures.txt`, with 40 failures carrying one
    `Docker.DotNet.DockerApiException` message at one `SqlServerFixture.cs` line, plus 2 distinct failures. Add a
    snapshot test, `Apply_IdenticalFailuresFixture_MatchesSnapshot`, and a savings scenario,
    `test/identical-failures`.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~DotnetTestFilterTests"`.
- [ ] **Step 3: Keep the cap tests testing the cap.** `Apply_MoreThanMaxFailures_ShowsPlusMoreLine`,
  `Apply_ExactlyMaxFailures_NoMoreFailuresLine` and `Apply_FailureMessages_ShareOneBudget` (:2255) generate failures
  that would now group. Give each generated failure its own message (`boom {i}`), so their assertions hold as
  written.
- [ ] **Step 4: Implement** `GroupFailures` and the group layout in `AppendFailureBlock`, as specified above.
- [ ] **Step 5: Run the suite, regenerate the baseline, update the examples.** Expected: `test/identical-failures`
  shows a large saving. Any other scenario that moves has failures sharing a message and location; name them in the
  commit body.
- [ ] **Step 6: Docs and commit.** In `usage.md:74`, describe the grouping, the order and what counts toward the 15,
  and add a `### Changed` entry to the CHANGELOG. Then `git commit -m "feat: list identical test failures once, with
  their count and first names"`.

### Task 5: `--names` one test per line

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/PassedTestNames.cs`: `Render` (:44-53) and the class summary.
- Test: the `Names` context in `DotnetTestFilterTests.cs` (:2582-2790).
- Docs: `usage.md:515-524`, `CHANGELOG.md`.

- [ ] **Step 1: Update the expectations, so they fail.** In every `Apply_Names_*` test, switch to the new layout. For
  example, `Apply_Names_GroupsByClass` expects `"✓ SampleApp.Tests.ParserTests (1):\n  Parses ×4\n✓
  SampleApp.Tests.SampleTests (3):\n  String_IsNotEmpty\n  List_ContainsExpectedItems\n
  Addition_ReturnsCorrectResult\n"`, where every method line is indented two spaces. `(unknown class)` and the
  assembly groups take the same layout.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~DotnetTestFilterTests"`.
- [ ] **Step 3: Implement.** `Render` writes `✓ {Name} ({Count}):\n`, then `  {method}\n` for each method, with
  `Describe` (`×N`) as today. Update the XML summary's example.
- [ ] **Step 4: Run the suite.** Expected: green. No savings scenario runs `--names`.
- [ ] **Step 5: Docs and commit.** In `usage.md:520-524`, give the new example, and add a `### Changed` entry to the
  CHANGELOG. Then `git commit -m "feat: list --names one test per line under its class"`.

### Task 6: Short fallback status lines; the `jb` missing-solution error

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs`: the two `BuildFallback` calls
  (:130, :135).
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetJbCleanupCodeFilter.cs`: `IsKeptLine` (:184-187).
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs` and
  `DotnetJbCleanupCodeFilterTests.cs`.
- Docs: `CHANGELOG.md`.

**Interfaces:**
- Consumes: `FilteredOutputRequest.CommandSlug`, the canonical subcommand such as `jb cleanupcode` (from
  `ResolveCommandSlug`, `FilteredRunUseCase.cs:296`).

- [ ] **Step 1: Write the failing tests.**
  - `[Theory] ProcessAsync_Fallback_NamesTheSubcommandWithoutItsArguments`. Use a request whose `DisplayCommandLine`
    is `dotnet jb cleanupcode App.slnx --profile=P --include=src/a/b.cs --no-build` and whose `CommandSlug` is `jb
    cleanupcode`. It covers the four shapes:
    - failed: `✗ dotnet jb cleanupcode failed (exit 1)`;
    - not recognised on exit 0: `⚠ dotnet jb cleanupcode: output not recognized, raw tail shown above`;
    - no output: `✓ dotnet jb cleanupcode (no output)`;
    - faulted filter: the same failed line.
  - `Apply_SolutionNotFound_KeepsReSharpersMessage`: the input is `"Unable to find target solution file in path
    /repo/sub/App.slnx\n"`, with exit 1. The output contains that line, path-shortened as other kept lines are, and
    ends with `"✗ dotnet jb cleanupcode failed (exit 1)\n"`.
- [ ] **Step 2: Run them to verify they fail.** `--filter
  "FullyQualifiedName~FilteredOutputPipelineTests|FullyQualifiedName~DotnetJbCleanupCodeFilterTests"`.
- [ ] **Step 3: Implement.** Pass `$"dotnet {request.CommandSlug}"` to `BuildFallback` in place of
  `request.DisplayCommandLine`, and leave every other use of `DisplayCommandLine` alone. In `IsKeptLine`, add lines
  starting with `Unable to find target solution file`.
- [ ] **Step 4: Run the suite.** Expected: green, with `PipeFilterUseCaseTests` and `PipeIntegrationTests` unchanged,
  and `SavingsBaselineTests` unchanged, because no scenario uses the fallback or that error.
- [ ] **Step 5: CHANGELOG and commit.** Add a `### Fixed` entry, then `git commit -m "fix: name only the subcommand in
  a fallback status line; recognise jb's missing-solution error"`.

### Task 7: Final verification and PR

- [ ] **Step 0: `CLAUDE.md`.** Add one clause to each paragraph that already covers these subjects. To the `--names`
  sentence: one test per line under its class. To the `MsBuildDiagnosticReport` paragraph: `0 errors, 0 warnings` only
  when MSBuild declared both counts. To the test filter's: identical failures are grouped by message and location,
  largest group first. Commit: `git commit -m "docs: CLAUDE.md notes for the 0.14 output changes"`.
- [ ] **Step 1:** Run `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`, and `dtk
  dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`. Expected: all green, with
  `SavingsBaselineTests` and `ExamplesBindingTests` passing.
- [ ] **Step 2:** Through the built CLI, `dtk pipe build` a clean log, and `dtk pipe test` the identical-failures
  fixture and a `--names` run. Paste the three outputs into the PR body.
- [ ] **Step 3:** Push, and open the PR against `develop`, titled `feat: clearer clean-build and test output from the
  0.14 field report`. The body lists the four items, the savings-baseline diff (scenario, before, after) and the
  outputs above, and ends with the PR attribution line.

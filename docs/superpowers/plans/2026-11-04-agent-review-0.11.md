# Agent-review 0.11 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the 0.10 *Deferred* table as 0.11.0: `--tee`, `--max-lines`, `--names`, `--show-output`,
`--fail-on-empty`, filters for `tool restore`, `ef migrations`/`ef database`, `stryker` and `jb cleanupcode`, and
detection of localized output in `dtk pipe test`.

**Architecture:** Options travel in `OutputOptions` (display, tee, cap) and a new `TestOptions`; filters that need
more than the text implement `IContextualOutputFilter` and receive a `FilterContext` (arguments, working directory,
start time, test options, file changes) and return a `FilterResult` (text plus a "no test matched" verdict the
pipeline turns into exit 8 under `--fail-on-empty`). File I/O stays behind Domain ports implemented in
Infrastructure (raw-output copy, file-content snapshot, artifact reader, test-runner detection), so every filter stays
testable from a fixture alone.

**Tech Stack:** .NET 10, C# 14, Spectre.Console.Cli, xunit + FluentAssertions, System.Text.Json (`JsonDocument`
only — AOT-safe), Native AOT.

**Spec:** `docs/superpowers/specs/2026-11-04-agent-review-0.11-design.md` — read it first; this plan argues from it and
does not repeat its rationale.

## Global Constraints

- Every filtered run's last line is its single status line; no successful run prints nothing.
- No dtk option may take a token the wrapped tool or dotnet owns; no dtk-only token may reach the child, including
  under `DTK_RAW=1`, `--interactive` and machine-readable runs. Anything after the user's own `--` is untouched.
- `IsSimpleCommand`/`IsAutoApprovable` stay fail-closed; nothing new becomes auto-approvable; a command containing
  `--tee` is never auto-approvable.
- dtk's returned exit code, the tee log header's exit code and the tracked `Success` always agree.
- No new tee header key (`TeeLogHeader.TryReadFields` rejects unknown keys).
- Settings options only of type `bool`, `int`, `string`, `string[]` (`CommandSettingsAotGuardTests`); no `int?`.
- `FilteredRunUseCase.RunAsync` stays at ≤ 7 parameters (S107 is a build error).
- New fixtures are real output, machine path replaced by `/test/project/root`, flat in
  `tests/DotnetTokenKiller.Application.Tests/Fixtures/`; each `.txt` fixture gets a `SavingsScenarios.All` entry and
  bumps `FixtureCorpusTests`' pinned count.
- Any filter output change: regenerate the baseline with
  `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline` in the same commit; an
  existing successful scenario losing more than one percentage point needs a justification sentence for the PR.
- Numbers printed by filters use `CultureInfo.InvariantCulture`; durations go through `DurationFormat.Format(ms, exact)`.
- Changed generated instruction text: append the shipped 0.10.0 hashes to
  `IntegrationInstructions.ReleasedCursorRuleHashes`/`ReleasedMarkdownRuleHashes` (once), re-pin the current ones,
  regenerate `.github/copilot-instructions.md`.
- Every new long option appears in `docs/articles` (`DocsBindingTests`).
- Style: file-scoped namespaces, `var`, `_camelCase` fields, `…Async` suffix, `ConfigureAwait(false)` in libraries, LF,
  no BOM; `TreatWarningsAsErrors` with Roslynator/Sonar/NetAnalyzers.
- Commits: conventional (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`), each ending with the session's attribution
  trailer. The pre-commit hook formats staged `.cs` files (`git config core.hooksPath .githooks`).

## Review Focus

1. **A value option with no value** — `dtk dotnet test --tee`, `--tee --filter X`, `--max-lines` last: a dtk usage
   error naming the option; `--filter` is never taken as a path. (Task 3)
2. **`--max-lines` edge values** — `0` (off), a negative or non-numeric value (usage error), a cap larger than the
   body (output unchanged, no footer), `1` (`… N more lines` singular/plural right). (Tasks 3, 4)
3. **CRLF output** — a Windows-captured log (`\r\n`) through `OutputCap` and every new filter: no stray `\r` in the
   output, counts unchanged. (Task 4 and each filter task: one test feeding `fixture.ReplaceLineEndings("\r\n")`.)
4. **Theory names with dots and parentheses** — `Ns.ParserTests.Parses(value: 1.5)` and
   `Ns.PathTests.Joins(path: "a.b/c")` group under `Ns.ParserTests`/`Ns.PathTests` as `Parses ×N`/`Joins ×N`; the class
   is split at the last `.` *before* the first `(`. (Task 9)
5. **Stryker's 80-column wrap** — a report path wrapped inside `file:///…` and a Windows path
   (`file:///C:/Users/…/mutation-report.json`) rejoin to one readable path. (Task 15)

---

## Group 1 — dtk's output options

### Task 1: Carry options in `OutputOptions`, not positional parameters

Pure refactor, no behaviour change; it makes room for the new options under S107.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/OutputOptions.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs:30-37`
- Modify: the 8 `src/DotnetTokenKiller.Cli/Commands/Dotnet*Command.cs` callers
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/OutputDisplaySettings.cs` (`ToOutputOptions`)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs` (update call sites)

**Interfaces:**
- Produces: `public sealed record OutputOptions(int VerbosityLevel = 0, bool ShowLogHint = false, bool Quiet = false,
  string? TeePath = null, int MaxLines = 0)`; `Normalized()` leaves `TeePath`/`MaxLines` unchanged under `Quiet`.
- Produces: `Task<int> FilteredRunUseCase.RunAsync(IOutputFilter filter, string command, IReadOnlyList<string> args,
  OutputOptions options, CancellationToken cancellationToken = default)` (Task 2 inserts `TestOptions? test = null`
  before the token).

- [ ] **Step 1:** Add `OutputOptions_Normalized_KeepsTeePathAndMaxLines_WhenQuiet` to a new
  `tests/DotnetTokenKiller.Application.Tests/UseCases/OutputOptionsTests.cs`:
  `new OutputOptions(1, true, true, "raw.log", 5).Normalized()` equals `new OutputOptions(0, false, true, "raw.log", 5)`.
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~OutputOptionsTests"` — FAIL (no such constructor).
- [ ] **Step 3:** Extend the record; change `RunAsync` to take `OutputOptions`; update the 8 commands to pass
  `settings.ToOutputOptions()`; update existing tests' call sites.
- [ ] **Step 4:** Run `dtk dotnet build DotnetTokenKiller.slnx` then the Application test project — PASS, no other change.
- [ ] **Step 5:** Commit `refactor: pass OutputOptions to FilteredRunUseCase`.

### Task 2: Filter context and the effective exit code

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Filters/FilterContext.cs`, `TestOptions.cs`, `FilterResult.cs`,
  `IContextualOutputFilter.cs`
- Create: `src/DotnetTokenKiller.Domain/Files/FileChangeSet.cs`
- Modify: `src/DotnetTokenKiller.Domain/Execution/ExitCodes.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputRequest.cs`, `FilteredOutputPipeline.cs`,
  `FilteredRunUseCase.cs`, `PipeFilterUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredOutputPipelineTests.cs`

**Interfaces:**
- Produces (Domain.Filters):
  - `public sealed record FilterContext` with init properties `RunSource Source = RunSource.Pipe`,
    `IReadOnlyList<string> Arguments = []` (the child's arguments after the subcommand tokens),
    `string? WorkingDirectory`, `DateTimeOffset? StartedUtc`, `TestOptions Test = TestOptions.None`,
    `FileChangeSet? Changes`; `public static FilterContext Unknown { get; }` (all defaults).
  - `public sealed record TestOptions(bool Names = false, bool ShowOutput = false, bool FailOnEmpty = false)` with
    `public static TestOptions None { get; }`.
  - `public readonly record struct FilterResult(string Output, bool NoTestsMatched = false)`.
  - `public interface IContextualOutputFilter : IOutputFilter { FilterResult Apply(string strippedOutput, int exitCode,
    FilterContext context); }` — implementers make the two-argument `Apply` call this one with `FilterContext.Unknown`.
- Produces (Domain.Files): `public sealed record FileChangeSet(IReadOnlyList<string> ChangedPaths, string?
  NotComputedReason = null)` with `bool Computed => NotComputedReason is null` and
  `static FileChangeSet NotComputed(string reason)`.
- Produces: `ExitCodes.UsageError = 1` (dtk's own usage errors, as `dtk pipe` already returns), `ExitCodes.NoTestsMatched = 8`.
- Produces: `FilteredRunUseCase.RunAsync(IOutputFilter filter, string command, IReadOnlyList<string> args,
  OutputOptions options, TestOptions? test = null, CancellationToken cancellationToken = default)`.
- Produces: `FilteredOutputRequest.Context { get; init; } = FilterContext.Unknown`; `FilteredRunUseCase` fills it
  (`Source = RunSource.Run`, `Arguments` = args after `DotnetSubcommands.TryMatch`'s tokens, `WorkingDirectory` =
  `Environment.CurrentDirectory`, `StartedUtc` = start, `Test` = `test ?? TestOptions.None`); `PipeFilterUseCase`
  gains `TestOptions? test = null` before its token and fills `Source = RunSource.Pipe`, `WorkingDirectory`, `Test`.
- Pipeline rule: a contextual filter is called with the context; `effectiveExitCode = result.NoTestsMatched &&
  request.Context.Test.FailOnEmpty ? ExitCodes.NoTestsMatched : request.ExitCode`, used for `FinalizeTeeAsync`, the
  tracked `Success`, and the return value. The filter is still called with the child's exit code.

- [ ] **Step 1: Write the failing tests** in `FilteredOutputPipelineTests` with a private `FakeContextualFilter`
  returning a fixed `FilterResult` and recording the context it got:
  - `ProcessAsync_PassesContextToContextualFilter` — context with `Arguments = ["--filter", "X"]` reaches the filter.
  - `ProcessAsync_ReturnsNoTestsMatchedExitCode_WhenFailOnEmptyAndVerdictEmpty` — child exit 0, result
    `NoTestsMatched: true`, `Test = new(FailOnEmpty: true)` → returns 8, the fake tee session's `FinalizeAsync` got 8,
    the recorded `CommandRecord.Success` is `false`.
  - `ProcessAsync_KeepsChildExitCode_WhenVerdictEmptyWithoutFailOnEmpty` → returns 0, finalize got 0.
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FilteredOutputPipelineTests"` — FAIL (types missing).
- [ ] **Step 3:** Add the Domain types, the request property and the pipeline rule above; `ApplyFilterSafelyAsync`
  returns `(FilterResult Result, bool Faulted)`.
- [ ] **Step 4:** Run the same filter, then the whole Application test project — PASS.
- [ ] **Step 5:** Commit `feat: give filters a run context and a no-tests verdict`.

### Task 3: Value options, test flags and refusals in the preprocessor

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs`
- Modify: `src/DotnetTokenKiller.Cli/Program.cs:55-75`
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/OutputDisplaySettings.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`

**Interfaces:**
- Produces (preprocessor):
  - `ValueOptions = {"--tee", "--max-lines"}`: in `InsertSeparator` a value option moves together with its value ahead
    of the inserted `--`; `--tee=<v>`/`--max-lines=<v>` move as one token. `StripDtkOptions` drops flag and value.
  - `TestOnlyOptions = {"--names", "--show-output", "--fail-on-empty"}`: partitioned like other dtk flags.
  - `internal static string? MissingOptionValue(string[] args)` — the first value option (not `=`-joined) whose next
    token is absent, or starts with `-` and is not an integer (`--max-lines -1` has a value, which `Validate()` then
    rejects), else `null`.
  - `internal static string? MisplacedTestOption(string[] args)` — the first test-only option on a filtered subcommand
    other than `test`, else `null`.
  - `internal static string? OptionRefusedOnPassthrough(string[] dotnetArgs)` — `--tee` or `--fail-on-empty` (any form)
    before the user's `--`, else `null`.
  - `internal static IReadOnlyList<string> OptionsDroppedOnPassthrough(string[] dotnetArgs)` — `--max-lines`,
    `--names`, `--show-output` present before the user's `--`.
- Program: after `Normalize`, a non-null `MissingOptionValue`/`MisplacedTestOption` prints
  `dtk: --tee needs a value` / `dtk: --names applies only to dotnet test` to stderr and returns
  `ExitCodes.UsageError`. On both passthrough branches (filtered subcommand passed through, and `DTK_RAW`), a non-null
  `OptionRefusedOnPassthrough` prints `dtk: --fail-on-empty needs a filtered run, but DTK_RAW=1 is set` (reason:
  `DTK_RAW=1 is set`, `--interactive is set`, `the output is machine-readable`) and returns `ExitCodes.UsageError`
  without running the child; each dropped option prints `dtk: --max-lines ignored: <same reason>` to stderr.
  Unfiltered subcommands are untouched: these checks run only when `DotnetSubcommands.TryMatch` matches.
- Settings: `[CommandOption("--tee <PATH>")] public string? TeePath`, `[CommandOption("--max-lines <N>")]
  [DefaultValue(0)] public int MaxLines`; `Validate()` returns an error for `MaxLines < 0`;
  `ToOutputOptions()` passes both.

- [ ] **Step 1: Write the failing tests** (table tests in `ArgumentPreprocessorTests`):
  - `InsertSeparator_MovesValueOptionWithItsValue`: `dotnet test --tee out.log --filter X` →
    `dotnet test --tee out.log -- --filter X`; `--tee=out.log` and `--max-lines 5` likewise.
  - `StripDtkOptions_DropsValueOptionAndValue`: `test --tee out.log --max-lines=3 --filter X` → `test --filter X`.
  - `StripDtkOptions_LeavesTokensAfterUserSeparator`: `test -- --tee x` unchanged.
  - `MissingOptionValue_Detects`: `[dotnet, test, --tee]` → `--tee`; `[dotnet, test, --tee, --filter, X]` → `--tee`;
    `[dotnet, test, --max-lines]` → `--max-lines`; `[dotnet, test, --tee=a]` → `null`. (Review Focus 1)
  - `MisplacedTestOption_Detects`: `dotnet build --names` → `--names`; `dotnet test --names` → `null`;
    `dotnet tool run x --names` → `null` (unfiltered).
  - `OptionRefusedOnPassthrough_Detects`: `test --fail-on-empty`, `test --tee=x` → the option; `test -- --tee x` → `null`.
  - `PassthroughArguments_ToolRunKeepsTeeVerbatim`: `dotnet tool run x --tee y` → `tool run x --tee y`.
  - `MaxLines_Negative_IsUsageError` and `MaxLines_NonNumeric_IsUsageError`: run `dtk dotnet test --max-lines -1` /
    `--max-lines abc` through `DtkLauncher` against a fake `dotnet`; exit code ≠ 0, the fake child was never run.
    (Review Focus 2)
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~ArgumentPreprocessorTests"` — FAIL.
- [ ] **Step 3:** Implement the preprocessor members, the Program checks and the two settings properties
  (wired as no-ops for now: Tasks 4–5 consume them).
- [ ] **Step 4:** Run the same filter — PASS; run `CommandSettingsAotGuardTests` — PASS (`string?` and `int` allowed).
- [ ] **Step 5:** Commit `feat: parse dtk value options and refuse them where they cannot apply`.

### Task 4: `--max-lines`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Helpers/OutputCap.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredOutputPipeline.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Helpers/OutputCapTests.cs`, `FilteredOutputPipelineTests.cs`

**Interfaces:**
- Produces: `public static string OutputCap.Truncate(string text, int maxLines, string footerTarget)` — keeps the
  last line (the status line) and the first `maxLines` lines before it, and inserts
  `… {n} more line{s} ({footerTarget})` right before the last line; `maxLines <= 0` or a body of at most `maxLines`
  lines returns `text` unchanged. Output uses `\n` only.
- Pipeline: build the filtered/fallback text **without** the `--show-log` hint, apply `OutputCap.Truncate` with
  `footerTarget = logHint is null ? "rerun without --max-lines" : "dtk log"`, then insert the hint before the last
  line (refactor `BuildFallback` so the hint is added after capping, keeping today's order: tail, hint, status).

- [ ] **Step 1: Write the failing tests**:
  - `Truncate_KeepsHeadAndStatusLine`: 10 body lines + status, `maxLines: 3` → lines 1–3, `… 7 more lines (dtk log)`,
    status.
  - `Truncate_Singular`: 4 body lines, `maxLines: 3` → `… 1 more line (dtk log)`.
  - `Truncate_Unchanged_WhenZeroOrBodyFits` (`0`, and `maxLines` = body length).
  - `Truncate_CrlfInput_YieldsLfOutput` (Review Focus 3): `\r\n` input, no `\r` in output, same counts.
  - Pipeline: `ProcessAsync_MaxLines_CapsFallbackTail_HintStaysBeforeStatus` (raw-tail fallback, `ShowLogHint`,
    `MaxLines = 5`): last line is the status, the line before it the hint, the one before that the footer;
    `ProcessAsync_MaxLines_FooterSaysRerun_WhenNoLog` (a `NullTeeSession`);
    `ProcessAsync_MaxLines_CapsFaultedFilterRawOutput` (a throwing filter, 100 raw lines, `MaxLines = 10`).
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~OutputCapTests|FullyQualifiedName~FilteredOutputPipelineTests"` — FAIL.
- [ ] **Step 3:** Implement `OutputCap` and the pipeline change.
- [ ] **Step 4:** Run the same filter — PASS; run the whole Application test project (existing fallback-order tests must
  still pass).
- [ ] **Step 5:** Commit `feat: cap filtered output with --max-lines`.

### Task 5: `--tee <path>` and its hook rule

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Tee/IRawOutputCopy.cs`, `IRawOutputCopyFactory.cs`, `RawOutputCopyException.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Tee/RawOutputCopyFactory.cs` (and its `RawOutputCopy`)
- Create: `src/DotnetTokenKiller.Application/UseCases/CopyingTeeSession.cs`
- Modify: `FilteredRunUseCase.cs`, `PipeFilterUseCase.cs`, `FilteredOutputPipeline.cs`, `FilteredOutputRequest.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` (register the factory)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs` (`IsAutoApprovable`)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/RawOutputCopyFactoryTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`, `PipeFilterUseCaseTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`

**Interfaces:**
- Produces (Domain.Tee): `public interface IRawOutputCopy : IAsyncDisposable { TextWriter Writer { get; } string?
  Failure { get; } }` — `Writer` is safe for the two concurrent pumps, strips ANSI (`AnsiStrip`), and never throws:
  the first write/flush error is kept in `Failure` and later writes are dropped. `public interface
  IRawOutputCopyFactory { IRawOutputCopy Open(string path); }` throws `RawOutputCopyException` (message names the path
  and the OS reason). The file is created or truncated, owner-only on POSIX (same `UnixCreateMode` as tee logs), no
  parent directories created, relative paths resolved against the working directory.
- Produces (Application): `internal sealed class CopyingTeeSession(ITeeSession inner, IRawOutputCopy copy) :
  ITeeSession` — `Writer` writes to `inner.Writer` then `copy.Writer`; `FinalizeAsync` delegates to `inner`.
- Produces: `FilteredOutputRequest.RawCopyFailure { get; init; }` (`string?`); when set, the pipeline inserts
  `⚠ --tee: could not write <path>: <reason>` before the status line (after capping).
- `FilteredRunUseCase`/`PipeFilterUseCase` take `IRawOutputCopyFactory` in their constructors; with
  `options.TeePath` set they open the copy **before** starting the child (on `RawOutputCopyException`: write
  `✗ dtk: --tee cannot create <path>: <reason>` to the injected output as the run's only line and return
  `ExitCodes.UsageError` without running the child), wrap the session,
  dispose the copy once the child has exited, and set `RawCopyFailure = copy.Failure`. Works with `tee.mode=Never`.
- `IsAutoApprovable(command)` returns `false` when `command` contains `--tee` anywhere (ordinal).

- [ ] **Step 1: Write the failing tests**:
  - `RawOutputCopyFactoryTests`: `Open_CreatesOwnerOnlyFile` (POSIX only, `UnixFileMode.UserRead | UserWrite`),
    `Open_TruncatesExisting`, `Open_Throws_WhenParentMissing` (message contains the path), `Writer_StripsAnsi`,
    `Writer_RecordsFailure_DoesNotThrow` (dispose the underlying stream first, then write).
  - `FilteredRunUseCaseTests.RunAsync_WithTeePath_CopiesRawOutput` (fake runner writes `a`, `b` on stdout, `c` on
    stderr → file holds the three lines; filtered output unchanged);
    `RunAsync_WithUncreatableTeePath_ReturnsUsageErrorWithoutRunningChild`;
    `RunAsync_WithTeePath_WorksWhenTeeModeNever` (a `NullTeeSession` service).
  - `PipeFilterUseCaseTests.RunAsync_WithTeePath_CopiesStdin`.
  - `FilteredOutputPipelineTests.ProcessAsync_RawCopyFailure_PrintsWarningBeforeStatus`.
  - `DotnetCommandRewriterTests`: `IsAutoApprovable("dotnet test --tee out.log")`, `("dotnet test --tee=out.log")`,
    `("dotnet build --tee x")` → `false`; `IsAutoApprovable("dotnet test")` → still `true`; `Rewrite` of
    `dotnet test --tee x` → `dtk dotnet test --tee x`.
- [ ] **Step 2:** Run the four test classes — FAIL.
- [ ] **Step 3:** Implement the port, factory, decorator, use-case wiring and the rewriter rule; register the factory
  as a singleton in `AddInfrastructure`.
- [ ] **Step 4:** Run the classes — PASS. Add a parity step to `ParityCases` that runs a wrapped build with
  `--tee {project}/raw.log --max-lines 5` (the runner compares written files); run
  `CommandSettingsAotGuardTests` and `ParityArrangementTests` — PASS.
- [ ] **Step 5:** Commit `feat: keep raw output in a chosen file with --tee`.

## Group 2 — test options

### Task 6: Test settings, pipe options and wiring

**Files:**
- Create: `src/DotnetTokenKiller.Cli/Commands/Settings/DotnetTestCommandSettings.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs`, `Settings/PipeCommandSettings.cs`,
  `Commands/PipeCommand.cs`, `CliConfigurator.cs:63`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Infrastructure/CommandSettingsAotGuardTests.cs:33` (9 → 10),
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/PipeCommandTests.cs`, help snapshots

**Interfaces:**
- Produces: `internal sealed class DotnetTestCommandSettings : OutputDisplaySettings` with `PositionalArgs`
  (`[CommandArgument(0, "[args]")]`), `[CommandOption("--names")] bool Names`, `[CommandOption("--show-output")] bool
  ShowOutput`, `[CommandOption("--fail-on-empty")] bool FailOnEmpty`, `TestOptions ToTestOptions()`.
- `PipeCommandSettings` gains the same three options and `ToTestOptions()`; `PipeCommand` returns
  `ExitCodes.UsageError` with `--names applies only to dtk pipe test` when one is set and `match.Name !=
  DotnetSubcommands.Test`; otherwise passes `settings.ToTestOptions()` to `PipeFilterUseCase.RunAsync`.
- `DotnetTestCommand` passes `settings.ToTestOptions()` to `FilteredRunUseCase.RunAsync` (runner-specific argument
  injection comes in Task 9).

- [ ] **Step 1:** Write `PipeCommandTests.Pipe_NamesOnBuild_IsUsageError` and
  `Pipe_FailOnEmptyOnTest_PassesTestOptions` (the use case receives `FailOnEmpty: true`); bump the guard count to 10.
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~PipeCommandTests|FullyQualifiedName~CommandSettingsAotGuardTests"` — FAIL.
- [ ] **Step 3:** Implement; accept the changed CLI help snapshots (`--names`, `--show-output`, `--fail-on-empty`
  appear only under `dtk dotnet test --help` and `dtk pipe --help`).
- [ ] **Step 4:** Run the same filter plus `CliConfiguratorTests` — PASS.
- [ ] **Step 5:** Commit `feat: add dotnet test options to dtk dotnet test and dtk pipe`.

### Task 7: `--fail-on-empty`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (implements `IContextualOutputFilter`;
  `FormatZeroTests` at 737)
- Create fixture: `dotnet_test_mtp_all_skipped.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/PipeIntegrationTests.cs`

**Interfaces:**
- Consumes: `FilterContext.Test.FailOnEmpty`, `FilterResult.NoTestsMatched` (Task 2).
- `DotnetTestFilter.Apply(stripped, exitCode, context)` sets `NoTestsMatched` whenever `FormatZeroTests` produces its
  verdict (with or without the flag). With `FailOnEmpty`, that verdict line reads
  `✗ dotnet test: <same description> — --fail-on-empty` instead of `⚠ dotnet test: <description>`. An all-skipped
  run never sets `NoTestsMatched` (today's `TotalSkipped: 0` guard).

- [ ] **Step 1: Capture** the MTP all-skipped fixture: in a scratch dir, `dotnet new mstest -n Skipped`, mark every
  test `[Ignore]`, add `global.json` `{"test":{"runner":"Microsoft.Testing.Platform"}}`, run `dotnet test`, save
  stdout+stderr, replace the machine path with `/test/project/root`; record the exit code in the scenario. Add the
  `SavingsScenarios` entry `test/mtp-all-skipped` and bump `FixtureCorpusTests`.
- [ ] **Step 2: Write the failing tests**:
  - `Apply_FailOnEmpty_VsTestNoMatch_ReportsFailureAndVerdict` (`dotnet_test_zero.txt`, exit 0):
    `Output` ends with `✗ dotnet test: no test matched filter … — --fail-on-empty\n`, `NoTestsMatched` true.
  - `Apply_FailOnEmpty_MtpZero_ReportsFailure` (`dotnet_test_mtp_zero.txt`, exit 8).
  - `Apply_NoFlag_StillReportsVerdict_KeepsWarning` (exit 0, `⚠` line, `NoTestsMatched` true).
  - `Apply_FailOnEmpty_AllSkipped_IsNotEmpty` (`dotnet_test_skipped_only.txt` and the new MTP all-skipped fixture):
    `NoTestsMatched` false, output unchanged from today.
  - `PipeIntegrationTests.PipeTest_FailOnEmpty_ExitsEight` (pipe `dotnet_test_zero.txt` with `--exit-code 0
    --fail-on-empty` → exit 8, last line starts with `✗`).
- [ ] **Step 3:** Run `DotnetTestFilterTests` — FAIL; implement; run again — PASS.
- [ ] **Step 4:** Run `PipeIntegrationTests` (CLI integration, class only) — PASS; regenerate the baseline (only the
  new scenario should appear).
- [ ] **Step 5:** Commit `feat: fail an empty test run on request with --fail-on-empty`.

### Task 8: `--show-output`

**Files:**
- Modify: `DotnetTestFilter.cs` (`FailureInfo` gains `IReadOnlyList<string> Output`; `CollectMessageLines` at 363;
  `ParseFailure`, `ParseMtpFailure`; `AppendFailureBlock` at 859)
- Create fixtures: `dotnet_test_output_vstest_fail.txt`, `dotnet_test_mtp_output_fail.txt`
- Test: `DotnetTestFilterTests.cs`

**Interfaces:**
- Consumes: `FilterContext.Test.ShowOutput`.
- Rendering with the flag, after a failure's `at <source>` line (or its message line when it has none):
  `    output:` then each output line indented six spaces, at most 20, then `      … N more lines (dtk log)`. Output
  comes from VSTest's `Standard Output Messages:` section and MTP's `Standard output`/`Error output` sections.
  Without the flag, output is unchanged from today.
- Fix, independent of the flag: `CollectMessageLines` also stops at `StandardOutputLabelPattern()`.

- [ ] **Step 1: Capture** the fixtures: VSTest — add to a scratch copy of `samples/SampleApp.Tests` a failing xUnit
  test that writes three lines through `ITestOutputHelper` and has no stack trace beyond the assert; MTP — a scratch
  MSTest project (MTP runner in `global.json`) with a failing test that writes through `Console.WriteLine` and
  `TestContext`. Save, anonymize, add scenarios `test/output-vstest-fail` (exit 1) and `test/mtp-output-fail` (exit 2),
  bump the count.
- [ ] **Step 2: Write the failing tests**: `Apply_ShowOutput_VsTest_ListsOutputUnderFailure` (the three lines appear
  indented under the failure, the status line is last); `Apply_ShowOutput_Mtp_ListsOutputUnderFailure`;
  `Apply_ShowOutput_CapsAt20Lines` (a synthetic failure built from the fixture with 25 output lines → 20 lines and
  `… 5 more lines (dtk log)`); `Apply_NoStackTrace_DoesNotFoldOutputIntoMessage` (no flag: the message is the assert
  text only); `Apply_ShowOutput_Crlf` (Review Focus 3).
- [ ] **Step 3:** Run `DotnetTestFilterTests` — FAIL; implement; run — PASS.
- [ ] **Step 4:** Regenerate the baseline; check the diff: only the new scenarios and any scenario whose message
  previously absorbed output (justify each in a note for the PR description).
- [ ] **Step 5:** Commit `feat: show a failing test's output with --show-output`.

### Task 9: Runner detection and `--names`

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Execution/TestRunner.cs`, `ITestRunnerDetector.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Execution/GlobalJsonTestRunnerDetector.cs`
- Create: `src/DotnetTokenKiller.Application/Helpers/TestArgumentInjector.cs`
- Modify: `DotnetTestFilter.cs` (store passed names; render groups), `DotnetTestCommand.cs`,
  `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs`
- Create fixtures: `dotnet_test_names_vstest.txt` (normal logger, with a theory), `dotnet_test_mtp_detailed_pass.txt`,
  `dotnet_test_mtp_detailed_fail.txt`, `dotnet_test_mtp_xunit3_detailed.txt`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/GlobalJsonTestRunnerDetectorTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Helpers/TestArgumentInjectorTests.cs`, `DotnetTestFilterTests.cs`

**Interfaces:**
- Produces: `public enum TestRunner { Unknown, VsTest, MicrosoftTestingPlatform }`; `public interface
  ITestRunnerDetector { TestRunner Detect(string workingDirectory); }`.
- `GlobalJsonTestRunnerDetector`: walks up from `workingDirectory` to the first `global.json` (as the SDK does);
  none → `VsTest`; `test.runner` equal (ordinal-ignore-case) to `Microsoft.Testing.Platform` → MTP; absent `test`
  or another runner → `VsTest`; unreadable or malformed JSON → `Unknown`. `JsonDocument` with comments skipped and
  trailing commas allowed.
- `public static IReadOnlyList<string> TestArgumentInjector.ForNames(IReadOnlyList<string> testArgs, TestRunner
  runner)`: VSTest → appends `--logger` `console;verbosity=normal` unless any `--logger`/`-l` value starts with
  `console`; MTP → appends `--output` `Detailed` unless `--output` is present; `Unknown` → unchanged. Insert before a
  user `--`, never after it.
- `DotnetTestCommand` with `Names` set: `TestArgumentInjector.ForNames(args, detector.Detect(Environment.CurrentDirectory))`.
- Filter with `Test.Names`: collect `Passed <name> [dur]` (VSTest normal/detailed) and MTP detailed passed lines;
  group by class = the part of the name before its first `(`, split at its last `.`; method = the rest before `(`;
  a name with no `.` before `(` groups under the assembly the output attributes it to (the parser's current MTP
  assembly), else under `(unknown class)`. Render, between the failures and the status line, one line per class in
  order of first appearance: `✓ <class> (<count>): <method>, <method> ×<n>` (`×n` when a method name repeats);
  no names read → `⚠ --names: passed test names not in the output`.

- [ ] **Step 1: Capture** the fixtures: VSTest — `samples/SampleApp.Tests` plus a scratch `[Theory]` with four
  `[InlineData]` rows including `1.5` and `"a.b/c"`, run `dotnet test --logger "console;verbosity=normal"`; MTP —
  the scratch MSTest project and a scratch `xunit.v3.mtp-v2` project, `dotnet test --output Detailed`, one passing and
  one failing run. Anonymize, add scenarios, bump the count.
- [ ] **Step 2: Write the failing tests**:
  - Detector: `Detect_NoGlobalJson_IsVsTest`, `Detect_MtpRunner_IsMtp` (in a parent directory),
    `Detect_OtherRunner_IsVsTest`, `Detect_Malformed_IsUnknown`, `Detect_CommentsAndTrailingCommas_Parse`.
  - Injector: `ForNames_VsTest_AppendsNormalConsoleLogger`, `ForNames_VsTest_KeepsUserConsoleLogger`,
    `ForNames_Mtp_AppendsDetailedOutput`, `ForNames_Mtp_KeepsUserOutput`, `ForNames_Unknown_Unchanged`,
    `ForNames_InsertsBeforeUserSeparator` (`["--filter","X","--","RunConfiguration.X=1"]`).
  - Filter: `Apply_Names_GroupsByClass` (VSTest fixture: `✓ SampleApp.Tests.SampleTests (3): …` line),
    `Apply_Names_TheoryWithDotsAndParens` (Review Focus 4: `Parses ×4` under its class),
    `Apply_Names_MtpMsTest_GroupsUnderAssembly`, `Apply_Names_XunitV3`, `Apply_Names_NotInOutput_Warns`
    (`dotnet_test_all_pass.txt` + flag → the `⚠ --names` line, status line still last).
- [ ] **Step 3:** Run the three classes — FAIL; implement; register the detector in `AddInfrastructure`; run — PASS.
- [ ] **Step 4:** Regenerate the baseline (new scenarios only).
- [ ] **Step 5:** Commit `feat: list passed tests by class with --names`.

## Group 3 — new filters

### Task 10: Register the five subcommands and their routing rules

Adds the subcommands with minimal filters (each returns `string.Empty`, so the pipeline's raw-tail fallback answers
until Tasks 11–15 fill them in). One registration pass instead of five, because each one churns the same pinned
tests and the generated instructions.

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`, `Filters/FilterKeys.cs`, `PassthroughSubcommands.cs`
- Create: `src/DotnetTokenKiller.Application/Filters/DotnetToolRestoreFilter.cs`, `DotnetEfFilter.cs`,
  `DotnetStrykerFilter.cs`, `DotnetJbCleanupCodeFilter.cs` (minimal)
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Create: `src/DotnetTokenKiller.Cli/Commands/DotnetToolRestoreCommand.cs`, `DotnetEfMigrationsCommand.cs`,
  `DotnetEfDatabaseCommand.cs`, `DotnetStrykerCommand.cs`, `DotnetJbCleanupCodeCommand.cs`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs`, `ArgumentPreprocessor.cs`, `Commands/CompletionCommand.cs`
  (check), `Commands/PipeCommand.cs` ("Available:" line)
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` (`UsageBody` example lines;
  append the 0.10.0 hashes to both released lists), `.github/copilot-instructions.md` (regenerate)
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenarios.cs` (`FilterFor` arms)
- Test: `SubcommandBindingTests`, `DotnetSubcommandsTests`, `PassthroughSubcommandsTests`, `AiderIntegratorTests`,
  `GitHubCopilotIntegratorTests`, `ReleasedOwnedFileHashesTests`, `ArgumentPreprocessorTests`,
  `SubcommandRegistrationTests`, `PipeIntegrationTests`, CLI help snapshots

**Interfaces:**
- Produces (Domain): `DotnetSubcommands.ToolRestore = "tool restore"`, `EfMigrations = "ef migrations"`,
  `EfDatabase = "ef database"`, `Stryker = "stryker"`, `JbCleanupCode = "jb cleanupcode"`, appended to `Ordered` in
  that order; `public static readonly IReadOnlySet<string> ToolBacked` = {EfMigrations, EfDatabase, Stryker,
  JbCleanupCode}. `FilterKeys` gets the same five names. Both ef keys resolve to `DotnetEfFilter`.
- Produces (filters, final signatures): `DotnetToolRestoreFilter(string? rootPath = null) : IOutputFilter`;
  `DotnetEfFilter(string? rootPath = null) : IContextualOutputFilter`; `DotnetStrykerFilter(IArtifactFileReader? files
  = null, string? rootPath = null) : IContextualOutputFilter` (`IArtifactFileReader` is defined in Task 15 — declare the
  constructor in Task 15; here the class takes only `rootPath`); `DotnetJbCleanupCodeFilter(string? rootPath = null) :
  IContextualOutputFilter, ISnapshotFilter` (`ISnapshotFilter` in Task 13 — likewise).
- Routing (PassthroughSubcommands, all pinned by tests):
  - `IsInteractiveFilteredRun`: also `tool restore … --interactive`, and `ef database drop` without `-f`/`--force`
    (it prompts; a filtered run closes stdin).
  - `IsMachineReadableRun`: also any `ef …` with `--json`, and `ef migrations script` without `-o`/`--output`.
  - New `public static bool IsToolHelpRun(IReadOnlyList<string> dotnetArgs)`: a `ToolBacked` subcommand with `-h`,
    `--help` or `-?` → passed through (the tool's help, unfiltered); `ArgumentPreprocessor.IsPassthrough` includes it.
  - Keep `Measurable` and `QualifyingVerbs` as they are (other `tool`/`ef` verbs stay captured passthrough).
- Preprocessor: for a `ToolBacked` subcommand, the dtk options are only `--vv`, `--show-log`, `--tee`, `--max-lines`;
  `-v`, `--verbose`, `-q`, `--quiet`, `-h`, `--help` go to the tool. `tool restore` keeps today's rules.
- Spectre: branch `tool` → command `restore`; branch `ef` → commands `migrations`, `database`; command `stryker`;
  branch `jb` → command `cleanupcode`; each with a description and one example, like `list package`.
- `AutoApprovableSubcommands` unchanged.

- [ ] **Step 1: Write the failing tests**:
  - `PassthroughSubcommandsTests`: `IsInteractiveFilteredRun(["tool","restore","--interactive"])` true;
    `(["ef","database","drop"])` true, `(["ef","database","drop","-f"])` false;
    `IsMachineReadableRun(["ef","migrations","list","--json"])` true, `(["ef","migrations","script"])` true,
    `(["ef","migrations","script","-o","x.sql"])` false; `IsToolHelpRun(["stryker","--help"])` true,
    `(["jb","cleanupcode","-h"])` true, `(["test","--help"])` false.
  - `ArgumentPreprocessorTests`: `InsertSeparator_ToolBacked_LeavesShortOptionsToTool`:
    `dotnet ef migrations add X -v --show-log` → `dotnet ef migrations --show-log -- add X -v`;
    `dotnet stryker -v 1.2 --max-lines 5` → `dotnet stryker --max-lines 5 -- -v 1.2`;
    `IsPassthrough(["dotnet","stryker","--help"])` true.
  - Update the pinned literals in `SubcommandBindingTests`, `DotnetSubcommandsTests`, `AiderIntegratorTests`,
    `GitHubCopilotIntegratorTests`; `ReleasedOwnedFileHashesTests` expects the 0.10.0 hashes in the released lists.
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests` and the Application test project — FAIL.
- [ ] **Step 3:** Implement the registry, routing, minimal filters, commands, DI, `UsageBody` lines
  (`dtk dotnet tool restore`, `dtk dotnet ef migrations add <Name>`, `dtk dotnet stryker`,
  `dtk dotnet jb cleanupcode <solution>`), the hash lists, `FilterFor` arms; regenerate
  `.github/copilot-instructions.md`; keep the pipe "Available:" error readable (split it over two lines if the
  joined list passes 80 columns, and update `Pipe_UnknownSubcommand_FailsWithKnownListAsync`).
- [ ] **Step 4:** Run the Domain and Application test projects, then the CLI integration classes
  `ArgumentPreprocessorTests`, `SubcommandRegistrationTests`, `PipeIntegrationTests`, `CliConfiguratorTests`,
  `HookIntegrationTests` — PASS (accept help snapshots).
- [ ] **Step 5:** Commit `feat: route tool restore, ef, stryker and jb cleanupcode through dtk`.

### Task 11: `dotnet tool restore` filter

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetToolRestoreFilter.cs`
- Create fixtures: `dotnet_tool_restore_success.txt`, `dotnet_tool_restore_missing_package.txt`,
  `dotnet_tool_restore_unreachable_feed.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetToolRestoreFilterTests.cs`

**Interfaces:** as in Task 10. Output per the spec: success
`✓ dotnet tool restore: 4 tools (docfx, jb, dotnet-stryker, dtk)` (the `Available commands:` names, in order); failure:
each distinct error line once (paths shortened with `TextHelpers.ShortenPathsInLine`), then
`✗ dotnet tool restore failed (exit 1): 1 of 4 tools not restored` when tool lines were parsed, else
`✗ dotnet tool restore failed (exit 1): N errors`.

- [ ] **Step 1: Capture**: success in this repository (`dotnet tool restore`); missing package — a scratch dir whose
  `.config/dotnet-tools.json` names `dotnettokenkiller.doesnotexist` 1.0.0; unreachable feed — the same manifest
  naming two tools plus a `nuget.config` with `<clear/>` and one source `https://127.0.0.1:9/v3/index.json`.
  Anonymize, add scenarios `tool-restore/success` (0), `tool-restore/missing-package` (1),
  `tool-restore/unreachable-feed` (1), bump the count.
- [ ] **Step 2: Write the failing tests**: `Apply_Success_OneLineWithCommands`, `Apply_MissingPackage_KeepsErrorThenStatus`,
  `Apply_UnreachableFeed_DeduplicatesRepeatedErrors` (each distinct line once; status last), `Apply_Crlf` (Review
  Focus 3), `Apply_Empty_ReturnsEmpty`.
- [ ] **Step 3:** Run `DotnetToolRestoreFilterTests` — FAIL; implement; run — PASS.
- [ ] **Step 4:** Regenerate the baseline.
- [ ] **Step 5:** Commit `feat: filter dotnet tool restore`.

### Task 12: `dotnet ef` filter and the EF sample

**Files:**
- Create: `samples/SampleApp.Ef/SampleApp.Ef.csproj`, `Model.cs`, `Program.cs` (a hosted app:
  `Host.CreateApplicationBuilder`, `AddDbContext` on SQLite, console logging at Information)
- Modify: `Directory.Packages.props` (`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`,
  `Microsoft.Extensions.Hosting`, at the current stable 10.0.x)
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs`
- Create fixtures: `dotnet_ef_migrations_add.txt`, `dotnet_ef_migrations_list.txt`, `dotnet_ef_migrations_remove.txt`,
  `dotnet_ef_migrations_add_build_failed.txt`, `dotnet_ef_migrations_add_verbose.txt`, `dotnet_ef_database_update.txt`,
  `dotnet_ef_database_update_logging.txt`, `dotnet_ef_database_update_error.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs`

**Interfaces:**
- Consumes: `FilterContext.Arguments` (verb, `-v`/`--verbose`, `-p`/`--project`).
- Behaviour per the spec: drop `Build started...`/`Build succeeded.`; collapse `info`/`dbug`/`trce` log blocks (a
  `^(info|dbug|trce): .+\[\d+\]$` line and its indented continuations) into one
  `(N log messages hidden — dtk log)` line; keep `warn`/`fail`/`crit` blocks and every other line; with `-v`/`--verbose`
  drop only the two build lines. Status, last:
  - success: `✓ dotnet ef <group> <verb>: done` (e.g. `✓ dotnet ef migrations add: done`); `migrations list` →
    `✓ dotnet ef migrations list: N migrations` (lines that look like `<14 digits>_<Name>`, with `(Pending)` kept);
  - build failure (`Build failed.` line): `✗ dotnet ef migrations add failed (exit 1): build failed — run dtk dotnet
    build <project> to see the errors`, `<project>` = the `-p`/`--project` value, else omitted (`run dtk dotnet build`);
  - other failure: `✗ dotnet ef <group> <verb> failed (exit N)` after the kept lines.

- [ ] **Step 1: Capture** (`PATH="$HOME/.dotnet/tools:$PATH"`, dotnet-ef 10.0.x): in `samples/SampleApp.Ef`, run
  `dotnet ef migrations add Initial`, `migrations list`, `database update` (with the host's logging on → the
  `_logging` fixture; a second update with logging off → plain), `migrations add AddRating --verbose`,
  `migrations remove`; a build failure by adding a `Broken.cs` with a syntax error; a runtime error with
  `database update` against a read-only `app.db` (`chmod 444`). Delete generated `Migrations/` and `app.db` afterwards
  (do not commit them; add them to the sample's `.gitignore`). Anonymize, add scenarios `ef/…` with their exit codes,
  bump the count. `dotnet-ef` is **not** added to the tool manifest and there is no integration test against a real
  `dotnet ef` (CI does not run `dotnet tool restore`); the fixtures and the sample are how the filter is checked and
  regenerated.
- [ ] **Step 2: Write the failing tests**: one per fixture asserting the kept result lines and the exact status line;
  `Apply_Logging_CollapsesInfoBlocks_KeepsWarn`; `Apply_Verbose_KeepsDiagnostics`;
  `Apply_BuildFailed_NamesProjectFromArguments` (context `Arguments = ["migrations","add","X","-p","src/Data"]`);
  `Apply_Crlf`.
- [ ] **Step 3:** Run `DotnetEfFilterTests` — FAIL; implement; run — PASS. Check that `samples/SampleApp.Ef` builds
  (`dtk dotnet build samples/SampleApp.Ef`).
- [ ] **Step 4:** Regenerate the baseline.
- [ ] **Step 5:** Commit `feat: filter dotnet ef migrations and database`.

### Task 13: File-content snapshot for a run

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Files/FileContentSnapshot.cs`, `IFileContentSnapshotter.cs`
- Create: `src/DotnetTokenKiller.Domain/Filters/ISnapshotFilter.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Files/FileContentSnapshotter.cs`
- Modify: `FilteredRunUseCase.cs` (constructor gains `IFileContentSnapshotter`), Infrastructure DI
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Files/FileContentSnapshotterTests.cs`,
  `FilteredRunUseCaseTests.cs`

**Interfaces:**
- Produces: `public sealed class FileContentSnapshot` (root + relative path → hash) with
  `IReadOnlyList<string> ChangedSince(FileContentSnapshot before)` — paths present in both whose hash differs,
  relative to the root, `/`-separated, ordinal-sorted.
- Produces: `public interface IFileContentSnapshotter { FileContentSnapshot? Take(string root, CancellationToken
  cancellationToken = default); }` — `null` when the tree exceeds 20,000 files or 256 MiB; skips directories named
  `bin`, `obj`, `node_modules`, `artifacts`, `StrykerOutput` and any starting with `.`; SHA-256 of each regular file;
  unreadable files are skipped.
- Produces: `public interface ISnapshotFilter { string SnapshotRoot(IReadOnlyList<string> arguments, string
  workingDirectory); }`.
- `FilteredRunUseCase`: when the filter is an `ISnapshotFilter`, take the snapshot before starting the child and again
  after it exits; set `FilterContext.Changes` to `new FileChangeSet(after.ChangedSince(before))`, or
  `FileChangeSet.NotComputed("tree too large")` when either is `null`. `dtk pipe` never sets `Changes`.

- [ ] **Step 1: Write the failing tests**: `Take_HashesFiles_SkipsIgnoredDirectories` (files under `obj/`, `.git/`,
  `node_modules/` absent); `ChangedSince_ListsOnlyContentChanges` (rewrite one file with the same bytes and new mtime,
  change another → only the second); `Take_ReturnsNull_OverFileLimit` (expose the limits as `internal` constants and
  test with a constructor overload taking smaller limits); `RunAsync_SnapshotFilter_PassesChanges` (fake snapshotter,
  fake contextual filter records `context.Changes`).
- [ ] **Step 2:** Run the two classes — FAIL.
- [ ] **Step 3:** Implement and register the snapshotter (singleton).
- [ ] **Step 4:** Run — PASS.
- [ ] **Step 5:** Commit `feat: snapshot file contents around a run for filters that need it`.

### Task 14: `dotnet jb cleanupcode` filter

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetJbCleanupCodeFilter.cs`
- Create fixtures: `dotnet_jb_cleanupcode_success.txt`, `dotnet_jb_cleanupcode_load_error.txt`,
  `dotnet_jb_cleanupcode_missing_path.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetJbCleanupCodeFilterTests.cs`

**Interfaces:**
- Consumes: `ISnapshotFilter`, `FilterContext.Changes` (Task 13).
- `SnapshotRoot`: the directory of the first positional argument that ends in `.sln`, `.slnx`, `.slnf` or `.csproj`
  or is an existing directory (resolved against `workingDirectory`), else `workingDirectory`.
- Counts: `skipped` = lines ending in `- Skipping.` (with or without a reason after it); `processed` = non-empty lines
  after `Cleaning up using profile …`. Output per the spec: changed paths (at most 30, then `… N more (git status)`),
  then `✓ dotnet jb cleanupcode: 2 files changed (356 processed, 314 skipped)` / `no changes (…)`;
  `Changes.Computed == false` → `✓ dotnet jb cleanupcode: 356 processed, 314 skipped · changed files not computed
  (<reason>)`; `Changes == null` (piped) → `… · changed files unknown (piped)`. Failure (exit ≠ 0): kept `Warning:`/`[MsBuild]` lines (distinct, paths shortened) and
  `No items were found to cleanup.`, then `✗ dotnet jb cleanupcode failed (exit 4): N project load errors` (count of
  `[MsBuild]` lines; `exit 3` with none → `✗ dotnet jb cleanupcode failed (exit 3): nothing to clean up`).

- [ ] **Step 1: Capture**: `git archive HEAD | tar -x -C <scratch>`, build it, change whitespace in two `.cs` files
  (e.g. a double space after the first `public`), run `dotnet jb cleanupcode DotnetTokenKiller.slnx
  --profile="Built-in: Reformat & Apply Syntax Style"` (exit 0, ~64 KB) → `_success`; append a block namespace to a
  file-scoped file → `_load_error` (exit 4); `dotnet jb cleanupcode DoesNotExist.csproj` → `_missing_path` (exit 3).
  Anonymize (the scratch root → `/test/project/root`), add scenarios `jb-cleanupcode/…`, bump the count.
- [ ] **Step 2: Write the failing tests**: `Apply_Success_ListsChangedFiles` (context `Changes = new(["src/A.cs",
  "src/B.cs"])` → the two paths, then the status with the fixture's real processed/skipped counts);
  `Apply_NoChanges`; `Apply_Piped_SaysChangedFilesUnknown`; `Apply_TreeTooLarge`; `Apply_CapsAt30Changed`;
  `Apply_LoadError`; `Apply_MissingPath`; `SnapshotRoot_SolutionInSubfolder`, `SnapshotRoot_Directory`,
  `SnapshotRoot_PathWithSpaces`, `SnapshotRoot_None_IsWorkingDirectory`; `Apply_Crlf`.
- [ ] **Step 3:** Run `DotnetJbCleanupCodeFilterTests` — FAIL; implement; run — PASS.
- [ ] **Step 4:** Regenerate the baseline.
- [ ] **Step 5:** Commit `feat: filter dotnet jb cleanupcode to the files it changed`.

### Task 15: `dotnet stryker` filter

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Files/IArtifactFileReader.cs`
- Create: `src/DotnetTokenKiller.Infrastructure/Files/ArtifactFileReader.cs`
- Create: `src/DotnetTokenKiller.Application/Filters/StrykerReport.cs`
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetStrykerFilter.cs`, Application DI (factory registration),
  `SavingsScenarios.FilterFor`
- Create fixtures: `dotnet_stryker_default.txt`, `dotnet_stryker_json_reporter.txt`, `dotnet_stryker_break_at.txt`,
  `dotnet_stryker_build_failed.txt`, `dotnet_stryker_solution.txt`, and `stryker_mutation_report.json` (embed
  `Fixtures/*.json` in the Application test csproj; not a corpus scenario)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetStrykerFilterTests.cs`, `StrykerReportTests.cs`,
  `tests/DotnetTokenKiller.Infrastructure.Tests/Files/ArtifactFileReaderTests.cs`

**Interfaces:**
- Produces: `public interface IArtifactFileReader { string? TryReadText(string path); string? FindNewest(string
  directory, string fileName, DateTimeOffset notBeforeUtc); }` (recursive; `null` when none or unreadable).
- Produces: `public sealed record StrykerMutant(string File, int Line, int Column, string MutatorName, string?
  Replacement, string Status)`; `public static IReadOnlyList<StrykerMutant> StrykerReport.ReadMutants(string json)` —
  mutation-testing-elements schema (`files.<path>.mutants[]`), `JsonDocument` only; invalid JSON → empty list.
- `DotnetStrykerFilter(IArtifactFileReader? files = null, string? rootPath = null)`; DI registers it with a factory
  resolving `IArtifactFileReader`.
- Report lookup: the path after `Your json report has been generated at:` (join the following non-empty lines up to the
  first empty line, strip `file://`, keep a Windows drive form `C:/…`), else, when `context.StartedUtc` is set,
  `files.FindNewest(<-O/--output value or working directory>/StrykerOutput, "mutation-report.json", StartedUtc)`.
  The html report is not used (not verified to embed the JSON); no report → the line
  `survivors: enable the json reporter to list them here`.
- Output per the spec: `Survived (N):` then per file `  <file>: (l,c) <Mutator> → <replacement> · …` (at most 20
  survivors, then `… N more (report)`); `No coverage: N (<file> ×n, …)` (the per-file part only from the report,
  else `No coverage: N`); status
  `✓ dotnet stryker: mutation score 37.50% · 6 killed, 3 survived, 0 timeout, 7 no coverage (12.36s) · report <html
  report path>`; threshold break (exit ≠ 0 with the break message) → `✗ dotnet stryker: mutation score 37.50% below
  break threshold 90% (exit 2) · …`; initial build failure → the unwrapped MSBuild block (from the `ERR … Initial build
  failed` entry up to `Time Elapsed`) rendered with `MsBuildDiagnosticReport.Parse(block, RootPath).RenderDiagnostics()`
  then `✗ dotnet stryker failed (exit 1): initial build failed · N errors`. Kept: `WRN`/`ERR` lines (distinct, without
  the `[hh:mm:ss XXX]` prefix, paths shortened). Score/counts from stdout (`The final mutation score is`, the
  `Killed:`/`Survived:`/`Timeout:` block; no-coverage from the `mutants got status NoCoverage` line); duration from
  `Time Elapsed hh:mm:ss.fffffff` via `DurationFormat.Format(ms, exact: true)`.

- [ ] **Step 1: Capture** (`PATH="$HOME/.dotnet/tools:$PATH"`): a scratch solution `Calc` (classlib with `Add`, `Sub`,
  `IsPositive`, `Describe`, `Clamp`) + `Calc.Tests` (xUnit, three tests), local tool `dotnet-stryker` 5.0.0; from
  `Calc.Tests` run `dotnet stryker` → `_default`; `dotnet stryker --reporter json --reporter html --reporter progress` →
  `_json_reporter` and copy its `mutation-report.json` → `stryker_mutation_report.json`; `--break-at 90` →
  `_break_at` (exit 2); a `Broken.cs` syntax error → `_build_failed` (exit 1); from the solution root with a
  `stryker-config.json` `{"stryker-config":{"solution":"Strk.sln","reporters":["json","progress"]}}` →
  `_solution`. Anonymize, add scenarios `stryker/…`, bump the count.
- [ ] **Step 2: Write the failing tests**: `ReadMutants_ParsesSurvivorsAndNoCoverage` (the JSON fixture: 3 `Survived`
  at (6,45), (8,58), (8,81)); `ReadMutants_InvalidJson_Empty`; `Apply_Default_ScoreCountsAndHint` (no reader);
  `Apply_JsonReporter_ListsSurvivors` (fake reader returning the JSON fixture for the printed path);
  `Apply_WrappedPath_Rejoined` and `Apply_WindowsWrappedPath_Rejoined` (Review Focus 5: the reader is asked for
  exactly `/…/reports/mutation-report.json` and `C:/…/mutation-report.json`);
  `Apply_FallsBackToNewestReport_WhenPathMissing` (context `StartedUtc` set; fake `FindNewest`);
  `Apply_BreakAt`; `Apply_BuildFailed_GroupedErrors`; `Apply_Solution`; `Apply_CapsAt20Survivors`; `Apply_Crlf`;
  `ArtifactFileReaderTests.FindNewest_IgnoresOlderThanStart`.
- [ ] **Step 3:** Run the three classes — FAIL; implement; register `ArtifactFileReader` (singleton) and the filter
  factory; `FilterFor(Stryker)` → `new DotnetStrykerFilter(null, PinnedRoot)`; run — PASS.
- [ ] **Step 4:** Regenerate the baseline.
- [ ] **Step 5:** Commit `feat: filter dotnet stryker to score, counts and survivors`.

## Group 4 — localized output

### Task 16: Detect localized test output in `dtk pipe test`

**Files:**
- Create: `eng/localization/ExtractTestSummaryStrings.cs` (a .NET 10 file-based app: `dotnet run
  eng/localization/ExtractTestSummaryStrings.cs -- <sdk dir>`), printing a C# table
- Create: `src/DotnetTokenKiller.Application/Filters/LocalizedTestSummaryStrings.cs` (the generated table, committed,
  with a header comment naming the generator and the SDK version it read)
- Create: `src/DotnetTokenKiller.Application/Filters/LocalizedTestOutput.cs`
- Modify: `DotnetTestFilter.cs` (the "recognised nothing" path)
- Create fixtures: `dotnet_test_localized_fr_fail.txt`, `_fr_pass.txt`, `_de_fail.txt`, `_ja_fail.txt`,
  `dotnet_test_mtp_localized_fr_fail.txt`, `_de_pass.txt`, `_ja_fail.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/LocalizedTestOutputTests.cs`, `DotnetTestFilterTests.cs`

**Interfaces:**
- Produces: `public static string? LocalizedTestOutput.Detect(string strippedOutput)` — the culture name (`fr`, `de`,
  `ja`, …) of the first line matching a localized VSTest `Passed!`/`Failed!`/`Skipped!` summary, a localized
  normal-logger `Test Run …` line, or a localized MTP `Test run summary:` line; `null` otherwise. English never
  matches (English is parsed normally).
- The table holds, per SDK culture, only the summary-line strings, read from the SDK's own satellite resource
  assemblies (VSTest's console logger strings and the `dotnet test` MTP terminal strings; the generator locates them
  under `<sdk>/<culture>/`).
- `DotnetTestFilter`: when nothing was recognised (it would return `string.Empty`) and `Detect` returns a culture,
  return the raw tail (last 40 lines, as the pipeline fallback does) followed by
  `⚠ dotnet test: output is localized (<culture>) — run it through dtk dotnet test, or set DOTNET_CLI_UI_LANGUAGE=en`
  when the exit code is 0, or `✗ dotnet test failed (exit N): output is localized (<culture>) — …` otherwise.
  Wrapped runs are unaffected.

- [ ] **Step 1:** Write and run the generator against `/usr/share/dotnet/sdk/<version>`; commit its output table. The
  repository's `Directory.Build.props` applies to a file-based app under it (analyzers as errors): relax what it needs
  with `#:property` directives in the script rather than editing the props.
- [ ] **Step 2: Capture** the fixtures: VSTest — `DOTNET_CLI_UI_LANGUAGE=fr|de|ja dotnet test` in
  `samples/SampleApp.Tests` (fails) and `fr` with `--filter "FullyQualifiedName~SampleTests"` (passes); MTP — the
  scratch MSTest project (MTP runner) with one failing test, `fr`, `de` (all passing variant), `ja`. Anonymize, add
  scenarios `test/localized-…` with their exit codes, bump the count.
- [ ] **Step 3: Write the failing tests**: `Detect_French_VsTestFail` → `fr`; `Detect_German`, `Detect_Japanese`,
  `Detect_Mtp_French`; `Detect_English_Null` (`dotnet_test_failures.txt`); `Apply_Localized_TailThenRemedy` (exit 1 →
  last line `✗ dotnet test failed (exit 1): output is localized (fr) — …`); `Apply_Localized_Pass_Warns` (exit 0);
  `Apply_Localized_Crlf`.
- [ ] **Step 4:** Run `LocalizedTestOutputTests|DotnetTestFilterTests` — FAIL; implement; run — PASS; regenerate the
  baseline.
- [ ] **Step 5:** Commit `feat: say when piped test output is localized and how to fix it`.

## Group 5 — instructions, docs, release notes

### Task 17: Generated instructions, docs and changelog

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` (`UsageBody`: one line each for
  `--tee <path>` "keep raw output as evidence", `--names`, `--show-output`, `--fail-on-empty`, `--max-lines N`),
  `SharedInstructionArtifacts.cs` if the skill text repeats the option list; re-pin the current hashes (the 0.10.0
  ones were added in Task 10); regenerate `.github/copilot-instructions.md`
- Modify: `docs/articles/usage.md` (each new option, the new subcommands, the tool-owned short options, the
  passthrough refusal rules), `docs/articles/ai-agent-setup.md` (`--tee` is never auto-approved),
  `docs/articles/architecture.md` (`FilterContext`, snapshot, artifact reader), `README.md`,
  `src/DotnetTokenKiller.Cli/README.md`, `CLAUDE.md` (new architecture notes: tool-owned options, `--tee` and
  `IsAutoApprovable`, the snapshot for `jb cleanupcode`, Stryker's json report, localized detection table and its
  generator)
- Modify: `CHANGELOG.md` `[Unreleased]` (Added: the five options, the four filters, localized detection; Changed:
  `stryker` and `jb cleanupcode` are no longer attached passthrough, `tool restore`/`ef` no longer unfiltered, `--tee`
  commands are not auto-approved; Fixed: a failing test's output no longer folds into its message). The version comes
  from the release tag (`Directory.Build.props`), so there is no bump here; cutting `[Unreleased]` to `[0.11.0]` is a
  separate pull request before tagging, as #188 was for 0.10.0.
- Modify: `docs/superpowers/specs/2026-11-02-agent-review-fixes-design.md` *Deferred* table: each row's Notes gains
  "→ 0.11 (2026-11-04 design)" or "closed in the 2026-11-04 design" (`dotnet run`, `dtk log` header)
- Test: `SubcommandBindingTests`, `AiderIntegratorTests`, `GitHubCopilotIntegratorTests`,
  `ReleasedOwnedFileHashesTests`, `DocsBindingTests`, `SharedInstructionArtifactsTests`

- [ ] **Step 1:** Update the pinned literals/hashes in the tests to the new text first — FAIL.
- [ ] **Step 2:** Edit the instruction text and docs; regenerate `.github/copilot-instructions.md`.
- [ ] **Step 3:** Run the Application test project and `DocsBindingTests` — PASS.
- [ ] **Step 4:** Run `npx markdownlint-cli2` on the changed Markdown if available (config `.markdownlint.json`), else
  check line lengths by eye against neighbouring docs.
- [ ] **Step 5:** Commit `docs: document the 0.11 options and filters`.

### Task 18: Whole-branch verification

- [ ] **Step 1:** `dtk dotnet build DotnetTokenKiller.slnx` — `0 errors, 0 warnings`.
- [ ] **Step 2:** `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` — no changes.
- [ ] **Step 3:** `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests`, `…Application.Tests`,
  `…Infrastructure.Tests` — all pass; `SavingsBaselineTests` green on the regenerated baseline.
- [ ] **Step 4:** CLI integration tests class by class with `--no-build` (see the memory note: the whole project times
  out locally), run in the foreground — all pass.
- [ ] **Step 5:** AOT check: `dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot`, then
  `DTK_AOT_BINARY=$PWD/artifacts/aot/dtk dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter
  "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot"` — pass.
- [ ] **Step 6:** Smoke the AOT binary for real: in the scratch EF sample `artifacts/aot/dtk dotnet ef migrations list`,
  in the scratch Stryker solution `artifacts/aot/dtk dotnet stryker`, in a repo copy `artifacts/aot/dtk dotnet jb
  cleanupcode DotnetTokenKiller.slnx`, and `artifacts/aot/dtk dotnet test samples/SampleApp.Tests --names --tee
  /tmp/…/raw.log --max-lines 8`; each last line is its status line and the tee file holds the raw output.
- [ ] **Step 7:** Collect for the PR description: the baseline diff summary, any successful scenario losing more than one
  point with its justification, and the list of the design's "calls made by the author".

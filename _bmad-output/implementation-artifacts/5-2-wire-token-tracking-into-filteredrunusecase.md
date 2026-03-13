# Story 5.2: Wire Token Tracking into FilteredRunUseCase

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want every filtered command execution automatically tracked with accurate token counts,
So that savings data accumulates passively without any extra steps.

## Acceptance Criteria

1. **Tracking call**: After `FilteredRunUseCase` completes a command execution (success or failure), `ITracker.RecordAsync` is called with:
   - `Command`: the subcommand name (first element of `args`, e.g. `"build"`)
   - `ProjectPath`: `Environment.CurrentDirectory`
   - `InputTokens`: `TokenEstimator.Estimate(stripped)` — estimated token count of ANSI-stripped raw output
   - `OutputTokens`: `TokenEstimator.Estimate(filtered)` — estimated token count of filtered output
   - `SavedTokens`: `InputTokens - OutputTokens`
   - `SavingsPercentage`: `inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0`
   - `ExecutionTime`: wall-clock elapsed time from `Stopwatch`
   - `Timestamp`: `DateTimeOffset.UtcNow` at time of record creation
2. **ANSI stripping**: Both raw and filtered strings are ANSI-stripped before token estimation — `AnsiStrip.Strip` is already called on raw output; filtered output from `IOutputFilter.Apply()` may also contain ANSI codes.
3. **Silent failure**: If `ITracker.RecordAsync` throws any exception, it is caught and swallowed — the filtered output and exit code returned to the caller are unaffected.
4. **Test — `RecordAsync` called with correct parameters**: Application.Tests mock `ITracker` with NSubstitute and verify `RecordAsync` is called once with a `CommandRecord` whose `Command`, `InputTokens`, `OutputTokens`, `SavedTokens`, and `SavingsPercentage` fields match the expected values derived from known output strings.
5. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests green (no regressions).
6. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Pre-Condition / Current State

> **🔍 IMPORTANT: Implementation is largely pre-completed.** As part of the Story 5.1 commits (`7ee5a20`, `78b66fb`), the developer already wired `ITracker` into `FilteredRunUseCase`. The current file at `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` already has full tracking implemented:
>
> - `ITracker tracker` injected via primary constructor
> - `TokenEstimator.Estimate(stripped)` and `TokenEstimator.Estimate(filtered)` for token counts
> - `CommandRecord` built and passed to `tracker.RecordAsync(...)`
> - Entire tracking block wrapped in try/catch (silent failure)
>
> `FilteredRunUseCaseTests.cs` also already has:
>
> - `RunAsync_TrackingThrows_DoesNotSurfaceException` — covers AC #3
> - `RunAsync_CallsFilterWithCombinedStrippedOutput` — covers ANSI stripping behavior
>
> **What remains**: AC #4 — a test that specifically verifies `RecordAsync` is called with a `CommandRecord` whose fields reflect the correct token counts and command name.

## Tasks / Subtasks

- [x] **Task 1**: Verify `FilteredRunUseCase.cs` implementation matches all ACs (AC: #1, #2, #3)
  - [x] Confirm `ITracker` injected in primary constructor
  - [x] Confirm token estimation uses `AnsiStrip.Strip(raw)` result (not raw before stripping)
  - [x] Confirm `filtered` (output of `filter.Apply(stripped)`) is used for output token estimation
  - [x] Confirm `args.Count > 0 ? args[0] : command` is used as `Command` in `CommandRecord`
  - [x] Confirm entire tracking block is inside try/catch

- [x] **Task 2**: Add missing `RecordAsync` parameter verification test (AC: #4)
  - [x] In `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`
  - [x] Add test: `RunAsync_RecordsCorrectTokenCounts_AfterSuccessfulExecution`
    - Set up runner to return `CommandResult("1234567890123456", "", 0)` (16 chars → ~4 tokens)
    - Set up filter to return `"1234"` (4 chars → ~1 token)
    - Call `RunAsync(_filter, "dotnet", ["build"], verbosityLevel: 0)`
    - Use `_tracker.Received(1).RecordAsync(Arg.Is<CommandRecord>(r => r.Command == "build" && r.InputTokens == 4 && r.OutputTokens == 1 && r.SavedTokens == 3), Arg.Any<CancellationToken>())`
  - [x] Add test: `RunAsync_RecordsCorrectCommand_WhenArgsProvided`
    - Verify `CommandRecord.Command == "build"` when args = `["build"]`
  - [x] Add test: `RunAsync_RecordsCorrectCommand_WhenNoArgs_UsesCommandName`
    - Verify `CommandRecord.Command == "dotnet"` when args = `[]`

- [x] **Task 3**: Build and verify (AC: #5, #6)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests green
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current File State

| File | State |
|---|---|
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | **Complete** — `ITracker` injected, `TokenEstimator` called, `CommandRecord` built and recorded, try/catch wrapping — DO NOT refactor |
| `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs` | **Needs parameter-verification test added** — see Task 2 |
| All other files | No changes needed |

### Implementation — Current `FilteredRunUseCase.cs` tracking block

```csharp
// Track: silent — errors never surface
try
{
    var inputTokens = TokenEstimator.Estimate(stripped);
    var outputTokens = TokenEstimator.Estimate(filtered);
    var savedTokens = inputTokens - outputTokens;
    var savingsPct = inputTokens > 0 ? (double)savedTokens / inputTokens * 100.0 : 0.0;

    var record = new CommandRecord(
        Timestamp: DateTimeOffset.UtcNow,
        Command: args.Count > 0 ? args[0] : command,
        ProjectPath: Environment.CurrentDirectory,
        InputTokens: inputTokens,
        OutputTokens: outputTokens,
        SavedTokens: savedTokens,
        SavingsPercentage: savingsPct,
        ExecutionTime: stopwatch.Elapsed);

    await tracker.RecordAsync(record, cancellationToken);
}
catch
{
    // Intentional: tracking errors must not surface to the user
}
```

Note: `stripped` is the ANSI-stripped combined stdout+stderr (`AnsiStrip.Strip(result.StdOut + result.StdErr)`). `filtered` is the output of `filter.Apply(stripped)`. Token estimation operates on already-stripped text — no additional stripping needed.

### TokenEstimator Heuristic

```csharp
// src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs
public static int Estimate(string text) => text.Length / 4;
```

Use this to compute expected token counts in tests. For 16-char input: `16 / 4 = 4`. For 4-char filtered: `4 / 4 = 1`.

### Test Pattern — NSubstitute `Arg.Is<T>` with predicate

```csharp
_tracker.Received(1).RecordAsync(
    Arg.Is<CommandRecord>(r =>
        r.Command == "build" &&
        r.InputTokens == 4 &&
        r.OutputTokens == 1 &&
        r.SavedTokens == 3),
    Arg.Any<CancellationToken>());
```

Or verify by capturing the argument:

```csharp
CommandRecord? captured = null;
_tracker.RecordAsync(Arg.Do<CommandRecord>(r => captured = r), Arg.Any<CancellationToken>())
    .Returns(Task.CompletedTask);

await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

captured.Should().NotBeNull();
captured!.Command.Should().Be("build");
captured.InputTokens.Should().Be(4);
```

Both patterns are valid; the `Arg.Do` pattern is clearer for multi-field assertions.

### Existing Tests in `FilteredRunUseCaseTests.cs`

Current test count: 5 tests. After this story: 7–8 tests.

| Test | Covers |
|---|---|
| `RunAsync_ReturnsExitCodeFromCommand` | Exit code propagation |
| `RunAsync_FilterThrows_FallsBackToRawOutput_StillReturnsExitCode` | Filter silent failure |
| `RunAsync_TrackingThrows_DoesNotSurfaceException` | Tracking silent failure (AC #3) |
| `RunAsync_TeeThrows_DoesNotSurfaceException` | Tee silent failure |
| `RunAsync_CallsFilterWithCombinedStrippedOutput` | ANSI stripping (AC #2) |
| **`RunAsync_RecordsCorrectTokenCounts_AfterSuccessfulExecution`** | **NEW — AC #4 (parameter verification)** |
| **`RunAsync_RecordsCorrectCommand_*`** | **NEW — command name in record** |

### Analyzer Pitfalls (from prior stories)

- **CA1305**: If using `string.Format` or `AppendLine` with format args, pass `CultureInfo.InvariantCulture` — not applicable here but keep in mind.
- **NSubstitute `.Received()`**: Must be called AFTER the act, not before. Pattern: Arrange → Act → Assert with `.Received()`.
- **`ThrowsAsync` vs `Throws`**: Use `ThrowsAsync(new ...)` for `async Task` methods; `Throws(new ...)` for sync methods. `RecordAsync` is `Task`-returning → `ThrowsAsync`.
- **FluentAssertions**: Use `BeLessThanOrEqualTo(n)` not `BeLessOrEqualTo(n)` (FA 8.x). Use `BeApproximately(expected, precision)` for `double` comparisons.
- **`NSubstitute.ExceptionExtensions`**: already imported in `FilteredRunUseCaseTests.cs` — use `ThrowsAsync` from there.

### Project Structure Notes

No new files or directories needed. Only `FilteredRunUseCaseTests.cs` is modified (new tests added).

Clean architecture compliance:

- `Application.Tests` correctly references only `DotnetTokenKiller.Application` (which references `Domain`)
- `ITracker` lives in `DotnetTokenKiller.Domain.Tracking` — already imported

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.2]
- [Source: _bmad-output/project-context.md — Testing Rules, NSubstitute Mocking, Critical Rules]
- [Source: src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs] — current implementation
- [Source: src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs] — `chars / 4` heuristic
- [Source: src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs] — ANSI stripping
- [Source: src/DotnetTokenKiller.Domain/Tracking/ITracker.cs] — interface contract
- [Source: src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs] — record fields
- [Source: tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs] — existing tests to extend
- [Source: _bmad-output/implementation-artifacts/5-1-implement-sqlite-token-tracking.md] — prior story learnings, analyzer pitfalls

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Task 1: Verified `FilteredRunUseCase.cs` — all tracking ACs already implemented in Story 5.1 commits. No changes required.
- Task 2: Added 3 parameter-verification tests to `FilteredRunUseCaseTests.cs` using NSubstitute `Arg.Is<CommandRecord>` predicate pattern with `await _tracker.Received(1).RecordAsync(...)`. Tests verify `Command`, `InputTokens`, `OutputTokens`, `SavedTokens` match expected values derived from `TokenEstimator` (chars/4 heuristic).
- Task 3: Build → 0 errors/0 warnings. Tests → all 180 pass (154 Application, 17 Domain, 8 Infrastructure, 1 Integration). Format → exit 0.

### File List

- `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs` (modified — 3 new tests added)

## Change Log

- 2026-03-13: Added 3 RecordAsync parameter-verification tests to FilteredRunUseCaseTests.cs; all 180 tests pass (claude-sonnet-4-6)

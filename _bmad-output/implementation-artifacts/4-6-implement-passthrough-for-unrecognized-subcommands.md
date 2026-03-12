# Story 4.6: Implement Passthrough for Unrecognized Subcommands

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running any dotnet subcommand not explicitly handled by DTK (e.g., `dtk dotnet new`, `dtk dotnet watch`),
I want the command to run transparently with no filtering applied,
So that DTK never blocks or degrades commands it does not understand.

## Acceptance Criteria

1. **Transparent passthrough**: When `dtk dotnet new console -n MyApp` is run, `dotnet new console -n MyApp` executes in passthrough mode — stdin, stdout, and stderr are inherited directly by the real `dotnet` process (no capture, no filtering).
2. **Exit code preservation**: The exit code returned by `dtk` exactly matches the underlying `dotnet` process exit code.
3. **No filtering applied**: Output goes directly to the terminal unchanged — no `Apply()` call is made.
4. **Verbosity has no effect**: Running with `-v` still invokes passthrough mode (verbosity does not alter passthrough behavior).
5. **`PassthroughRunUseCase` implemented**: `PassthroughRunUseCase` in the Application layer implements passthrough via `ICommandRunner.RunPassthroughAsync`.
6. **Fallback command wired**: The Cli project's dotnet branch routes unrecognized subcommands to `DotnetPassthroughCommand` via `SetDefaultCommand<DotnetPassthroughCommand>()`.
7. **Tracking with zero tokens**: After the passthrough command completes, `ITracker.RecordAsync` is called with: the subcommand name, `Environment.CurrentDirectory` as project path, 0 input tokens, 0 output tokens, 0 saved tokens, 0.0% savings, and wall-clock execution time.
8. **Tracker errors silently swallowed**: If `ITracker.RecordAsync` throws, the exception is caught and suppressed — the passthrough exit code is returned unchanged.
9. **DI registration**: `PassthroughRunUseCase` is registered as `AddTransient<PassthroughRunUseCase>()` in `AddApplication()`.
10. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions).
11. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create `PassthroughRunUseCase` (AC: #1, #2, #3, #5, #7, #8)
  - [x] Create `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
  - [x] Primary constructor injects `ICommandRunner commandRunner` and `ITracker tracker`
  - [x] `RunAsync(string command, IReadOnlyList<string> args, CancellationToken)` method:
    - Start `Stopwatch`
    - Call `await commandRunner.RunPassthroughAsync(command, args, cancellationToken)` → capture `exitCode`
    - Stop stopwatch
    - Silently call `tracker.RecordAsync(...)` with 0 token data (wrap in try/catch)
    - Return `exitCode`
  - [x] `sealed` class (CA1852)
  - [x] File-scoped namespace: `namespace DotnetTokenKiller.Application.UseCases;`

- [x] Task 2: Register `PassthroughRunUseCase` in DI (AC: #9)
  - [x] Add `services.AddTransient<PassthroughRunUseCase>();` after `FilteredRunUseCase` in `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 3: Create `DotnetPassthroughCommand` (AC: #1, #2, #3, #4, #6)
  - [x] Create `src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs`
  - [x] Primary constructor injects `PassthroughRunUseCase passthroughRun`
  - [x] `ExecuteAsync`: build args from `settings.PositionalArgs.Concat(context.Remaining.Raw).ToArray()`, call `await passthroughRun.RunAsync("dotnet", args, cancellationToken)`
  - [x] `sealed class` implements `AsyncCommand<DotnetCommandSettings>`

- [x] Task 4: Wire default command in `Program.cs` (AC: #6)
  - [x] In the `config.AddBranch("dotnet", dotnet => { ... })` block, add `dotnet.SetDefaultCommand<DotnetPassthroughCommand>()` **before** the other `AddCommand` registrations
  - [x] Add `using` for `DotnetPassthroughCommand` if needed (same namespace — no import needed)

- [x] Task 5: Write `PassthroughRunUseCaseTests` (AC: #7, #8, #10)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`
  - [x] Test: `RunAsync_ReturnsCommandRunnerExitCode` — mock `ICommandRunner.RunPassthroughAsync` returning non-zero, verify return value matches
  - [x] Test: `RunAsync_CallsRunPassthroughAsyncWithCorrectArgs` — verify args forwarded to `ICommandRunner.RunPassthroughAsync`
  - [x] Test: `RunAsync_CallsTrackerRecordAsync_WithZeroTokenFields` — verify `RecordAsync` called with `InputTokens=0`, `OutputTokens=0`, `SavedTokens=0`, `SavingsPercentage≈0.0`
  - [x] Test: `RunAsync_TrackerThrows_DoesNotPropagateException` — tracker mock throws, verify no exception propagates and correct exit code is returned

- [x] Task 6: Build and verify (AC: #10, #11)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (170 total: 166 baseline + 4 new)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3, 4.1–4.5)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — all packages pinned |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs` | `RunCapturedAsync` + `RunPassthroughAsync` — MUST NOT change |
| `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` | `RecordAsync`, `GetSummaryAsync`, `GetHistoryAsync`, `CleanupAsync` — MUST NOT change |
| `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs` | Complete record type — MUST NOT change |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` | Complete (story 3.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs` | Complete (story 3.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs` | Complete (story 3.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs` | Complete (story 4.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs` | Complete (story 4.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs` | Complete (story 4.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs` | Complete (story 4.4) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs` | Complete (story 4.5) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `PassthroughRunUseCase` transient added** |
| `src/DotnetTokenKiller.Infrastructure/Tracking/NullTracker.cs` | Complete stub — used in all tests; returns `Task.CompletedTask` for `RecordAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | **Needs `SetDefaultCommand<DotnetPassthroughCommand>()` wired to dotnet branch** |
| `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs` | Complete — DO NOT BREAK |

**Test count baseline**: 166 tests total (after story 4.5). All must continue to pass.

### Architecture Constraints (CRITICAL)

- `PassthroughRunUseCase` lives in the `Application` layer — it references `Domain` interfaces only (`ICommandRunner`, `ITracker`, `CommandRecord`)
- `DotnetPassthroughCommand` lives in the `Cli` layer — it references `Application` (`PassthroughRunUseCase`) and Spectre.Console
- **No filter interface involved** — `IOutputFilter.Apply` is NOT called in passthrough mode; output is inherited by the child process
- `PassthroughRunUseCase` MUST be `sealed` (CA1852)
- `DotnetPassthroughCommand` MUST be `sealed` (CA1852)
- File-scoped namespaces enforced — no braced namespace blocks
- `using System.Diagnostics;` required in `PassthroughRunUseCase` for `Stopwatch`

### Implementation: `PassthroughRunUseCase`

```csharp
namespace DotnetTokenKiller.Application.UseCases;

using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using System.Diagnostics;

public sealed class PassthroughRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker)
{
    public async Task<int> RunAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var exitCode = await commandRunner.RunPassthroughAsync(command, args, cancellationToken);
        stopwatch.Stop();

        try
        {
            var subcommand = args.Count > 0 ? args[0] : command;
            var record = new CommandRecord(
                Timestamp: DateTimeOffset.UtcNow,
                Command: subcommand,
                ProjectPath: Environment.CurrentDirectory,
                InputTokens: 0,
                OutputTokens: 0,
                SavedTokens: 0,
                SavingsPercentage: 0.0,
                ExecutionTime: stopwatch.Elapsed);
            await tracker.RecordAsync(record, cancellationToken);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }

        return exitCode;
    }
}
```

**Key design decisions:**

- `Stopwatch` wraps only `RunPassthroughAsync` — tracking time is wall-clock of the passthrough itself
- Subcommand name for `CommandRecord.Command` is `args[0]` (e.g., `"new"` for `dtk dotnet new`) — matches the convention used in `FilteredRunUseCase`
- `CommandRecord` token fields are all zero (passthrough produces no filtered output)
- Tracker try/catch wraps both record creation and `RecordAsync` call — any exception is silently suppressed

### Implementation: `DotnetPassthroughCommand`

```csharp
namespace DotnetTokenKiller.Cli.Commands;

using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

public sealed class DotnetPassthroughCommand(
    PassthroughRunUseCase passthroughRun) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Concat(context.Remaining.Raw).ToArray();
        return await passthroughRun.RunAsync("dotnet", args, cancellationToken);
    }
}
```

**Key design decisions:**

- `settings.PositionalArgs` captures the first positional arg(s) — for `dtk dotnet new console -n MyApp`, `PositionalArgs = ["new", "console"]`
- `context.Remaining.Raw` captures unparsed args — e.g., `["-n", "MyApp"]`
- Combined: `["new", "console", "-n", "MyApp"]` → forwarded to `dotnet` exactly as-is
- **No verbosity forwarding**: passthrough makes no distinction on verbosity level — the underlying `dotnet` process receives all args unchanged
- `DotnetCommandSettings` already declares `[CommandArgument(0, "[args]")] string[] PositionalArgs` — reuses existing settings type without change

### Updated `Program.cs` — Default Command Wiring

Add `dotnet.SetDefaultCommand<DotnetPassthroughCommand>()` **inside** the dotnet branch:

```csharp
config.AddBranch("dotnet", dotnet =>
{
    dotnet.SetDescription("Run dotnet commands with filtered output");
    dotnet.SetDefaultCommand<DotnetPassthroughCommand>();  // NEW — must be before AddCommand calls
    dotnet.AddCommand<DotnetBuildCommand>("build").WithDescription("Run dotnet build with filtered output");
    dotnet.AddCommand<DotnetTestCommand>("test").WithDescription("Run dotnet test with filtered output");
    dotnet.AddCommand<DotnetRestoreCommand>("restore").WithDescription("Run dotnet restore with filtered output");
    dotnet.AddCommand<DotnetPublishCommand>("publish").WithDescription("Run dotnet publish with filtered output");
    dotnet.AddCommand<DotnetPackCommand>("pack").WithDescription("Run dotnet pack with filtered output");
    dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
    dotnet.AddCommand<DotnetRunCommand>("run").WithDescription("Run dotnet run with filtered output");
    dotnet.AddCommand<DotnetEfCommand>("ef").WithDescription("Run dotnet ef with filtered output");
    dotnet.AddCommand<DotnetFormatCommand>("format").WithDescription("Run dotnet format with filtered output");
    dotnet.AddCommand<DotnetNugetCommand>("nuget").WithDescription("Run dotnet nuget with filtered output");
});
```

**Note**: `StrictParsing = false` is already set globally in `Program.cs`. With `SetDefaultCommand<DotnetPassthroughCommand>()`, Spectre.Console routes any unrecognized subcommand token to `DotnetPassthroughCommand` where it appears as the first element of `PositionalArgs`.

### Updated `DependencyInjection.cs` — Application Project

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddTransient<PassthroughRunUseCase>();  // NEW
    services.AddSingleton<DotnetBuildFilter>();
    services.AddSingleton<DotnetTestFilter>();
    services.AddSingleton<DotnetRestoreFilter>();
    services.AddSingleton<DotnetPublishFilter>();
    services.AddSingleton<DotnetPackFilter>();
    services.AddSingleton<DotnetCleanFilter>();
    services.AddSingleton<DotnetRunFilter>();
    services.AddSingleton<DotnetEfFilter>();
    services.AddSingleton<DotnetFormatFilter>();
    services.AddSingleton<DotnetNugetFilter>();
    return services;
}
```

### Test Implementation

```csharp
namespace DotnetTokenKiller.Application.Tests.UseCases;

using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class PassthroughRunUseCaseTests
{
    private readonly ICommandRunner _commandRunner = Substitute.For<ICommandRunner>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly PassthroughRunUseCase _sut;

    public PassthroughRunUseCaseTests()
    {
        _sut = new PassthroughRunUseCase(_commandRunner, _tracker);
    }

    [Fact]
    public async Task RunAsync_ReturnsCommandRunnerExitCode()
    {
        _commandRunner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var result = await _sut.RunAsync("dotnet", ["new"], CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_ForwardsArgsToCommandRunner()
    {
        string[] args = ["new", "console", "-n", "MyApp"];
        _commandRunner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync("dotnet", args, CancellationToken.None);

        await _commandRunner.Received(1).RunPassthroughAsync("dotnet", args, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CallsTrackerWithZeroTokenFields()
    {
        _commandRunner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync("dotnet", ["watch"], CancellationToken.None);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.InputTokens == 0 &&
                r.OutputTokens == 0 &&
                r.SavedTokens == 0 &&
                r.SavingsPercentage == 0.0 &&
                r.Command == "watch"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackerThrows_DoesNotPropagateException()
    {
        _commandRunner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var act = async () => await _sut.RunAsync("dotnet", ["new"], CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

**Check if NSubstitute is already available**: Look in `Directory.Packages.props` and existing `FilteredRunUseCaseTests.cs` to confirm the mocking library in use. If `NSubstitute` is the existing choice, use the pattern above. If `Moq` is used, adapt the syntax accordingly.

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5–4.5)

- **CA1852** — `PassthroughRunUseCase` and `DotnetPassthroughCommand` MUST be `sealed`
- **CA1050/RCS1110/S3903** — types MUST be in named namespaces (file-scoped satisfies this)
- **`using System.Diagnostics;`** — NOT in implicit usings; must be explicit in `PassthroughRunUseCase`
- **CA1305** — no string interpolations with integer formatting in this use case (no CA1305 trigger), but if you add any `$"...{int}..."`, use `CultureInfo.InvariantCulture`
- **RCS1118** — extract repeated string literals to `const` if used more than once
- **Import ordering** — after `dotnet format`, project usings come before system usings per `.editorconfig`; let `dotnet format` handle this automatically
- **Namespace must match folder path**: `Application/UseCases/PassthroughRunUseCase.cs` → `namespace DotnetTokenKiller.Application.UseCases;`, `Cli/Commands/DotnetPassthroughCommand.cs` → `namespace DotnetTokenKiller.Cli.Commands;`
- **No `async/await` if only one await at end**: Prefer `return await commandRunner.RunPassthroughAsync(...)` if no further awaits after — but here there are two awaits (tracker), so `async Task<int>` is correct

### Mocking Library Verification

Before writing tests, confirm the mocking framework in use by checking `FilteredRunUseCaseTests.cs` — it uses `NSubstitute` or `Moq`. Match that pattern exactly to avoid adding new test dependencies.

### Git Context (Recent Commits)

```sh
79e89d0 Merge branch 'develop' into feat/add-filters-and-passthrough
0996af2 Bump Verify.Xunit from 28.2.0 to 31.12.5 (#8)
7bb7864 Bump Microsoft.Extensions.DependencyInjection from 9.0.0 to 10.0.4 (#7)
ef62a03 Bump Microsoft.CodeAnalysis.NetAnalyzers from 10.0.103 to 10.0.200 (#6)
3ab05d7 Feat: implement DotnetCleanFilter with tests and update DotnetCleanCommand
```

Current branch: `feat/add-filters-and-passthrough`. Stories 4.1–4.4 done; 4.5 in review; 4.6 is this story.

### Previous Story Intelligence (Story 4.5 — NuGet Filter)

Key learnings applied here:

- **`sealed` class pattern**: Same as stories 4.1–4.5; required by CA1852 analyzer
- **File-scoped namespaces**: `namespace X.Y.Z;` — no braced blocks
- **`DependencyInjection.cs` pattern**: Both `FilteredRunUseCase` and `PassthroughRunUseCase` are `AddTransient` (stateful per-request use cases, not singletons)
- **Silent error swallowing**: Tracker calls wrapped in empty `catch { }` — same pattern as `FilteredRunUseCase` — intentional, not lazy
- **FluentAssertions `.Should().NotContain(string, StringComparison.Ordinal)` overload absence**: In story 4.5 it was discovered this overload does not exist in the FA version in use — do NOT use `StringComparison` overload on FA string methods; use plain `NotContain(string)` or `Contain(string)` in tests

### What This Story Does NOT Implement (Scope Guard)

- Any new output filter — passthrough is explicitly filter-free
- Changes to any existing filter class
- SQLite tracking — `NullTracker` is the active `ITracker` for this story; `RecordAsync` completes without persisting (story 5.1)
- The `GainCommand` — story 5.3
- Any changes to existing `AsyncCommand` handlers other than `Program.cs` wiring
- `ITeeService` integration in passthrough — `FilteredRunUseCase` calls tee; passthrough does not (no captured output)

### Project Structure Notes

- All target directories already exist — no new directories required
- New use case: `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
- New command: `src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs`
- Modified DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `PassthroughRunUseCase` transient
- Modified `Program.cs`: add `dotnet.SetDefaultCommand<DotnetPassthroughCommand>()`
- New test file: `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.6]
- [Source: _bmad-output/implementation-artifacts/4-5-implement-dotnet-nuget-filter-with-tests.md] — previous story; same DI pattern, same analyzer pitfalls, same test conventions
- [Source: src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs] — reference for `Stopwatch`, tracker integration, silent error swallowing pattern
- [Source: src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs] — `RunPassthroughAsync` signature
- [Source: src/DotnetTokenKiller.Domain/Tracking/ITracker.cs] — `RecordAsync` signature
- [Source: src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs] — record fields and constructor
- [Source: src/DotnetTokenKiller.Cli/Program.cs] — dotnet branch configuration; `SetDefaultCommand` placement
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs] — reference for `AsyncCommand<DotnetCommandSettings>` implementation pattern
- [Source: src/DotnetTokenKiller.Cli/Commands/Settings/DotnetCommandSettings.cs] — `PositionalArgs` and `Verbose` fields
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — current DI registrations
- [Source: tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs] — mocking library and test pattern to follow

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Implemented `PassthroughRunUseCase` with `Stopwatch`-timed passthrough, zero-token `CommandRecord`, and silent tracker error swallowing.
- `DotnetPassthroughCommand` registered as default command on the dotnet branch via `SetDefaultCommand<DotnetPassthroughCommand>()`.
- DI registration: `AddTransient<PassthroughRunUseCase>()` added after `FilteredRunUseCase` in `AddApplication()`.
- 4 unit tests added: exit code forwarding, args forwarding, zero-token tracking, tracker exception suppression.
- Analyzer pitfall encountered: S1244 (floating-point exact equality) — resolved by using `Math.Abs(r.SavingsPercentage) < 1e-10` in `Arg.Is` predicate.
- Import ordering fixed by `dotnet format` (project usings before system usings per `.editorconfig`).
- Build: 0 errors, 0 warnings. Tests: 170 total (all pass). Format: clean.

### File List

- `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs` (new)
- `src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs` (new)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (modified — added `PassthroughRunUseCase` transient)
- `src/DotnetTokenKiller.Cli/Program.cs` (modified — added `SetDefaultCommand<DotnetPassthroughCommand>()`)

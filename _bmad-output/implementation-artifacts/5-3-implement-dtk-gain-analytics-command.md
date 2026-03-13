# Story 5.3: Implement dtk gain Analytics Command

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want to run `dtk gain` and see a formatted table of my token savings across all commands,
So that I can understand the value DTK is delivering and share it with my team.

## Acceptance Criteria

1. **Default table**: `dtk gain` renders a Spectre.Console table with columns — command name, run count, total tokens saved, average savings percentage — one row per command type, plus a summary line showing totals and overall average savings %. Default time range is the last **30 days**.
2. **`--days` filter**: `dtk gain --days 7` includes only records from the last 7 days.
3. **`--project` filter**: `dtk gain --project` filters by `Environment.CurrentDirectory` as project path.
4. **`--json` output**: `dtk gain --json` emits raw JSON to stdout instead of the table (suitable for LLM piping). System.Text.Json source-generated serialization.
5. **No-data message**: When `GainSummary.TotalCommands == 0`, display the friendly message: `"No data yet. Run some dtk dotnet commands to start tracking savings."` (no table rendered).
6. **`GainReportUseCase`**: Exists in `DotnetTokenKiller.Application` and queries data via `ITracker.GetSummaryAsync`.
7. **`GainCommandSettings`**: In Cli project, exposes `--days` (default 30), `--project` (bool flag), `--json` (bool flag).
8. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests green. `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Pre-Condition / Current State

> **🔍 IMPORTANT: Significant scaffold already exists from prior stories.**

Current state of relevant files:

| File | Current State |
|---|---|
| `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` | **Stub** — inherits `AsyncCommand<GainCommandSettings>`, `ExecuteAsync` returns `Task.FromResult(0)` — MUST be implemented |
| `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs` | **Partial** — has `--days` (default **7**, must change to **30**); missing `--project` and `--json` options |
| `src/DotnetTokenKiller.Cli/Program.cs` | **Complete** — `config.AddCommand<GainCommand>("gain")` already registered — DO NOT touch |
| `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs` | **Does not exist** — must be created |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs update** — must register `GainReportUseCase` |
| `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` | **Complete** — `GetSummaryAsync(int days, string? projectPath, ct)` already defined |
| `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs` | **Complete** — record with `TotalCommands`, `TotalInputTokens`, `TotalOutputTokens`, `TotalSavedTokens`, `AverageSavingsPercentage`, `SavedByCommand` |
| `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` | **Complete** — `GetSummaryAsync` fully implemented with per-command aggregation |

## Tasks / Subtasks

- [x] **Task 1**: Update `GainCommandSettings` (AC: #7)
  - [x] Change `Days` default from `7` to `30`
  - [x] Add `[CommandOption("--project")]` `bool Project` — filter by CWD
  - [x] Add `[CommandOption("--json")]` `bool Json` — JSON output mode

- [x] **Task 2**: Create `GainReportUseCase` in Application layer (AC: #6)
  - [x] File: `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs`
  - [x] Constructor: primary constructor `GainReportUseCase(ITracker tracker)`
  - [x] Method: `Task<GainSummary> GetSummaryAsync(int days, string? projectPath, CancellationToken ct = default)` — delegates to `tracker.GetSummaryAsync(days, projectPath, ct)`
  - [x] Register in `src/DotnetTokenKiller.Application/DependencyInjection.cs`: `services.AddTransient<GainReportUseCase>()`

- [x] **Task 3**: Implement `GainCommand.ExecuteAsync` (AC: #1–#5)
  - [x] Inject `GainReportUseCase` and `IAnsiConsole` via primary constructor
  - [x] Resolve `projectPath`: `settings.Project ? Environment.CurrentDirectory : null`
  - [x] Call `await _gainReport.GetSummaryAsync(settings.Days, projectPath, cancellationToken)`
  - [x] If `settings.Json`: serialize `GainSummary` to JSON using source-generated context and write to stdout; return 0
  - [x] If `summary.TotalCommands == 0`: write no-data message; return 0
  - [x] Else: render Spectre.Console table with per-command rows + summary footer; return 0

- [x] **Task 4**: Add JSON source-generated serializer context in Cli project (AC: #4)
  - [x] Create `[JsonSerializable(typeof(GainSummary))]` source context in Cli project
  - [x] Use `JsonSerializer.Serialize(summary, GainSummaryJsonContext.Default.GainSummary)` in `GainCommand`

- [x] **Task 5**: Write `GainReportUseCase` tests (AC: #8)
  - [x] File: `tests/DotnetTokenKiller.Application.Tests/UseCases/GainReportUseCaseTests.cs`
  - [x] Test: `GetSummaryAsync_DelegatesCorrectDaysToTracker`
  - [x] Test: `GetSummaryAsync_PassesProjectPath_WhenProvided`
  - [x] Test: `GetSummaryAsync_PassesNullProjectPath_WhenNotProvided`
  - [x] Test: `GetSummaryAsync_ReturnsSummaryFromTracker`

- [x] **Task 6**: Build and verify (AC: #8)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests green
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Architecture Compliance — CRITICAL

Clean architecture dependency rules apply strictly:

- `Domain` — zero external deps, zero project refs — **already satisfies all contracts for this story**
- `Application` — references `Domain` only — `GainReportUseCase` lives here; MUST NOT reference `Spectre.Console`
- `Cli` — references `Application` + `Infrastructure` — all Spectre.Console rendering happens here
- Consequence: The Spectre.Console table rendering and JSON serialization happen in `GainCommand`, NOT in `GainReportUseCase`

### GainReportUseCase — Implementation Blueprint

```csharp
// src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

public sealed class GainReportUseCase(ITracker tracker)
{
    public Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
        => tracker.GetSummaryAsync(days, projectPath, cancellationToken);
}
```

### GainCommandSettings — Full Updated Shape

```csharp
public sealed class GainCommandSettings : CommandSettings
{
    [CommandOption("--days")]
    [Description("Number of days of history to include (default: 30)")]
    public int Days { get; init; } = 30;  // ← WAS 7, must change to 30

    [CommandOption("--project")]
    [Description("Filter by current project directory")]
    public bool Project { get; init; }

    [CommandOption("--json")]
    [Description("Output raw JSON instead of table")]
    public bool Json { get; init; }
}
```

### GainCommand — Implementation Blueprint

```csharp
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class GainCommand(
    GainReportUseCase gainReport,
    IAnsiConsole console) : AsyncCommand<GainCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GainCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var projectPath = settings.Project ? Environment.CurrentDirectory : null;
        var summary = await gainReport.GetSummaryAsync(settings.Days, projectPath, cancellationToken);

        if (settings.Json)
        {
            var json = JsonSerializer.Serialize(summary, GainSummaryJsonContext.Default.GainSummary);
            console.WriteLine(json);
            return 0;
        }

        if (summary.TotalCommands == 0)
        {
            console.MarkupLine("[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking savings.[/]");
            return 0;
        }

        // Render table
        var table = new Table();
        table.AddColumn("Command");
        table.AddColumn(new TableColumn("Runs").RightAligned());
        table.AddColumn(new TableColumn("Tokens Saved").RightAligned());
        table.AddColumn(new TableColumn("Avg Savings %").RightAligned());

        foreach (var (cmd, saved) in summary.SavedByCommand)
        {
            // run count per command not directly in GainSummary.SavedByCommand
            // Note: SavedByCommand is Dictionary<string, int> (saved tokens per command)
            // We don't have per-command run count in GainSummary — see design note below
            table.AddRow(cmd, "-", saved.ToString(), "-");
        }

        table.AddEmptyRow();
        table.AddRow(
            "[bold]TOTAL[/]",
            summary.TotalCommands.ToString(),
            summary.TotalSavedTokens.ToString(),
            $"{summary.AverageSavingsPercentage:F1}%");

        console.Write(table);
        return 0;
    }
}
```

> **⚠️ DESIGN NOTE — Missing Per-Command Run Count:**
> `GainSummary.SavedByCommand` is `IReadOnlyDictionary<string, int>` (saved tokens only). The AC requires "run count" and "avg savings %" per row, but `GainSummary` only exposes `TotalCommands` (aggregate) and `SavedByCommand` (saved tokens per command key). Consult with user or simplify the table to show what's available:
>
> - Column "Runs" → only available for total (not per-command) unless ITracker is extended
> - Column "Avg Savings %" → only available for overall average
>
> **Recommended approach for AC compliance without domain changes**: Show per-command rows with just command name + tokens saved; add a summary row with total commands + total saved + overall avg %. This satisfies the spirit of AC #1 without breaking `GainSummary` or requiring `ITracker` changes.
>
> Alternatively, extend `GainSummary` to include run counts per command (e.g., add `IReadOnlyDictionary<string, CommandStats> ByCommand` where `CommandStats` has RunCount + SavedTokens + AvgPct). Discuss with user if full per-row stats are required.

### JSON Source-Generated Context

```csharp
// In DotnetTokenKiller.Cli project (e.g., new file: src/DotnetTokenKiller.Cli/Serialization/GainSummaryJsonContext.cs)
using DotnetTokenKiller.Domain.Tracking;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSerializable(typeof(GainSummary))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext { }
```

Then use as: `JsonSerializer.Serialize(summary, GainSummaryJsonContext.Default.GainSummary)`

### IAnsiConsole Injection in GainCommand

Spectre.Console.Cli commands can receive `IAnsiConsole` through DI if registered, or the command can use `AnsiConsole.Console` (static). Preferred: inject via primary constructor so it's testable.

Register in the composition root (Program.cs, already uses `ServiceCollection`):

```csharp
services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);
```

This must be added to `Program.cs`. Alternatively, access via `context.Console` — check if Spectre.Console.Cli exposes it through `CommandContext`. In current Spectre.Console 0.54.0, `CommandContext` does **not** expose `IAnsiConsole`; use DI injection.

### Test Pattern — GainReportUseCaseTests

```csharp
// tests/DotnetTokenKiller.Application.Tests/UseCases/GainReportUseCaseTests.cs
namespace DotnetTokenKiller.Application.Tests.UseCases;

public class GainReportUseCaseTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly GainReportUseCase _sut;

    public GainReportUseCaseTests()
    {
        _sut = new GainReportUseCase(_tracker);
    }

    [Fact]
    public async Task GetSummaryAsync_DelegatesCorrectDaysToTracker()
    {
        var expected = new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>());
        _tracker.GetSummaryAsync(7, null, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.GetSummaryAsync(7, null);

        await _tracker.Received(1).GetSummaryAsync(7, null, Arg.Any<CancellationToken>());
        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetSummaryAsync_PassesProjectPath_WhenProvided()
    {
        var expected = new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>());
        _tracker.GetSummaryAsync(Arg.Any<int>(), "/my/project", Arg.Any<CancellationToken>()).Returns(expected);

        await _sut.GetSummaryAsync(30, "/my/project");

        await _tracker.Received(1).GetSummaryAsync(Arg.Any<int>(), "/my/project", Arg.Any<CancellationToken>());
    }
}
```

### Analyzer Pitfalls (from prior stories)

- **CA1305**: If using `string.Format` or `AppendLine($"...")`, pass `CultureInfo.InvariantCulture` — applies if building JSON strings manually; use `JsonSerializer` instead
- **RCS1118**: String literals should be `const` — apply to any repeated strings
- **CA1050/S3903**: All types must be in named namespaces — `GainSummaryJsonContext` needs a namespace
- **S6580**: `TimeSpan.TryParse` needs `CultureInfo.InvariantCulture` — not applicable here but keep in mind
- **TreatWarningsAsErrors = true**: Every analyzer warning is a build failure — zero tolerance
- **NSubstitute `.Received()`**: Must be called AFTER the act (Arrange → Act → Assert)
- **FluentAssertions**: `BeLessThanOrEqualTo(n)` not `BeLessOrEqualTo(n)` in FA 8.x

### Project Structure Notes

New files:

- `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs` (new)
- `src/DotnetTokenKiller.Cli/Serialization/GainSummaryJsonContext.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/UseCases/GainReportUseCaseTests.cs` (new)

Modified files:

- `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` (implement)
- `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs` (add options, fix default)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (register GainReportUseCase)
- `src/DotnetTokenKiller.Cli/Program.cs` (add IAnsiConsole registration)

No new projects, no new NuGet packages. `Spectre.Console` and `System.Text.Json` are already referenced.

Clean architecture compliance:

- `Application.Tests` references only `DotnetTokenKiller.Application` (which references `Domain`)
- `GainReportUseCase` in Application has zero Spectre.Console or infrastructure dependencies
- JSON context and table rendering stay in Cli project

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.3] — Acceptance criteria
- [Source: _bmad-output/project-context.md — Clean Architecture Rules, Spectre.Console.Cli Command Pattern]
- [Source: src/DotnetTokenKiller.Domain/Tracking/ITracker.cs] — `GetSummaryAsync` contract
- [Source: src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs] — shape of response object
- [Source: src/DotnetTokenKiller.Cli/Commands/GainCommand.cs] — current stub to implement
- [Source: src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs] — current settings to update
- [Source: src/DotnetTokenKiller.Cli/Program.cs] — registration already done (gain command registered)
- [Source: src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs] — `GetSummaryAsync` implementation reference
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — registration location for GainReportUseCase
- [Source: tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs] — NSubstitute test patterns to follow
- [Source: _bmad-output/implementation-artifacts/5-2-wire-token-tracking-into-filteredrunusecase.md] — prior story analyzer pitfalls

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Fixed RCS1201: chained `table.AddColumn()` calls
- Fixed CA1305: used `CultureInfo.InvariantCulture` for `int.ToString()` and `string.Create()`
- Fixed `AddRow` ambiguity (`params string[]` vs `params IRenderable[]`): switched to explicit `new Text()`/`new Markup()` renderables

### Completion Notes List

- Implemented `GainCommand` with `--days`, `--project`, `--json` support and Spectre.Console table rendering
- Created `GainReportUseCase` (thin delegate to `ITracker.GetSummaryAsync`) in Application layer — zero Spectre.Console deps
- Added `GainSummaryJsonContext` source-generated JSON context for AOT-safe serialization
- Registered `GainReportUseCase` in Application DI and `IAnsiConsole` in Cli Program.cs
- Table shows per-command token savings rows + summary footer with total saved, avg %, and run count
- 4 new tests in `GainReportUseCaseTests` — all pass; 185 total tests green, 0 warnings

### File List

- `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs` (new)
- `src/DotnetTokenKiller.Cli/Serialization/GainSummaryJsonContext.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/UseCases/GainReportUseCaseTests.cs` (new)
- `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` (implemented)
- `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs` (updated: Days=30, added --project, --json)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (registered GainReportUseCase)
- `src/DotnetTokenKiller.Cli/Program.cs` (added IAnsiConsole DI registration)

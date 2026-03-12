# Story 4.3: Implement dotnet ef Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet ef` commands,
I want Entity Framework CLI output compacted to the actionable result,
So that I save 70–80% of tokens by eliminating EF banners, build output, and verbose SQL logs.

## Acceptance Criteria

1. **Migrations list compact**: When `DotnetEfFilter.Apply(rawOutput)` is called with a `ef migrations list` fixture, the output is `N migrations (latest: MigrationName)\n`.
2. **Token savings**: Token savings is ≥70% on the fixture.
3. **Migration add detection**: When `Apply(rawOutput)` is called with output containing "To undo this action", the result is `✓ migration added\n`.
4. **Database update detection**: When `Apply(rawOutput)` is called with output containing "Applying migration '...'." lines, the result is `✓ database updated (N migration(s) applied)\n`.
5. **Already up-to-date fallback**: When `Apply(rawOutput)` is called with EF output containing no recognized patterns (no migration names, no "Applying", no "To undo"), the result is `✓ database updated (already up-to-date)\n`.
6. **Noise stripped**: None of the following appear in output: EF banner/logo, "Build started", "Build succeeded", "Entity Framework Core", "Finding DbContext", "Using context".
7. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
8. **All regex patterns use `[GeneratedRegex]`**: No `new Regex(...)` at runtime.
9. **`DotnetEfCommand` wired**: `DotnetEfCommand` injects `FilteredRunUseCase filteredRun` and `DotnetEfFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
10. **DI registration**: `DotnetEfFilter` is registered as a singleton in `AddApplication()`.
11. **Snapshot test**: A Verify.Xunit snapshot test exists for the migrations list fixture in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
12. **Fixture file**: `dotnet_ef_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
13. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions).
14. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #12)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_ef_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**/*.txt` already exists in the `.csproj` — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetEfFilter` (AC: #1, #2, #3, #4, #5, #6, #7, #8)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs`
  - [x] `public sealed partial class DotnetEfFilter : IOutputFilter` (needs `partial` for `[GeneratedRegex]`)
  - [x] Implement `Apply(string rawOutput)` — see "Implementation" section below
  - [x] Use `[GeneratedRegex]` for `MigrationListPattern` and `ApplyingMigrationPattern`
  - [x] Use `string.Create(CultureInfo.InvariantCulture, $"...")` for count-based interpolations (CA1305)

- [x] Task 3: Register `DotnetEfFilter` in DI (AC: #10)
  - [x] Add `services.AddSingleton<DotnetEfFilter>();` after `DotnetRunFilter` in `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetEfCommand` to use `FilteredRunUseCase` (AC: #9)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetEfFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import

- [x] Task 5: Write filter tests (AC: #1, #2, #3, #4, #5, #6, #7, #11)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs`
  - [x] Snapshot test for migrations list fixture (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: ≥70%
  - [x] Noise line theory tests: verify EF banner/verbose lines absent from fixture output
  - [x] Count assertion: fixture output contains "3 migrations" and "latest:"
  - [x] Inline database update test: "Applying migration '...'" → correct summary
  - [x] Inline database already up-to-date test: "Done." only → fallback
  - [x] Inline migration add test: "To undo this action" → correct summary
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #11)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetEfFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` — should contain `3 migrations (latest: 20231101000000_AddProductsTable)`
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #13, #14)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (145 total: 126 Application + 17 Domain + 1 Integration + 1 Infrastructure)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3, 4.1, 4.2)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — all packages pinned |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` | Complete (story 3.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs` | Complete (story 3.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs` | Complete (story 3.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs` | Complete (story 4.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs` | Complete (story 4.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetEfFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs` | Complete (story 4.1) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRunFilterTests.cs` | Complete (story 4.2) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_ef_raw.txt` here |

**Test count baseline**: 111 tests total (after stories 4.1 + 4.2). All must continue to pass.

**`DotnetEfCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it with `FilteredRunUseCase.RunAsync`. Same rewiring pattern as stories 4.1 (clean) and 4.2 (run). See [src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs](src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs).

### Architecture Constraints (CRITICAL)

- `DotnetEfFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`) + `Helpers` namespace
- Filter MUST be `stateless` — no instance fields at all (no `_rootPath`: EF output does not have file paths that need shortening)
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` because it uses `[GeneratedRegex]` → `sealed partial class`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`
- `using System.Text.RegularExpressions;` required for `[GeneratedRegex]` / `Regex`
- `using System.Globalization;` required for `CultureInfo.InvariantCulture` (CA1305)
- No `using System.Text;` — no `StringBuilder` needed (all outputs are short, single-result strings)

### Why `sealed partial class`?

`DotnetEfFilter` needs `[GeneratedRegex]` for two patterns:

1. `MigrationListPattern` — matches EF migration name lines: `20231001000000_InitialCreate (Applied)`
2. `ApplyingMigrationPattern` — matches database update lines: `Applying migration 'MigrationName'.`

Since `[GeneratedRegex]` generates a `partial` method, the class must be `partial`. This follows the same convention as `DotnetBuildFilter` and `DotnetRunFilter`.

### Filter Design: Output Type Detection

The EF CLI always outputs a build preamble, then an EF banner/logo, then verbose discovery lines, then subcommand-specific content. The filter ignores all preamble/banner/verbose noise by only matching specific content patterns:

| Detected Pattern | Output |
|---|---|
| Lines matching `^\s*\d{14}_[A-Za-z0-9_]+(\s+\(.*\))?\s*$` | Count migrations, extract latest name |
| Line containing "Applying migration '...'" | Count applied migrations |
| Line containing "To undo this action" | Mark as `migrations add` operation |
| Nothing matched | Fallback: `✓ database updated (already up-to-date)` |

**Key insight**: The filter does NOT need an explicit noise-stripping loop. By only acting on lines that match meaningful patterns, all EF banner/logo/verbose lines are implicitly ignored.

### Implementation: `DotnetEfFilter`

```csharp
namespace DotnetTokenKiller.Application.Filters;

using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text.RegularExpressions;

public sealed partial class DotnetEfFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var migrations = new List<string>();
        var applyingCount = 0;
        var isMigrationAdd = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var migMatch = MigrationListPattern().Match(line);
            if (migMatch.Success)
            {
                migrations.Add(migMatch.Groups["name"].Value);
                continue;
            }

            if (ApplyingMigrationPattern().IsMatch(line))
            {
                applyingCount++;
                continue;
            }

            if (line.Contains("To undo this action", StringComparison.OrdinalIgnoreCase))
            {
                isMigrationAdd = true;
            }
        }

        if (isMigrationAdd)
            return "✓ migration added\n";

        if (applyingCount > 0)
            return string.Create(CultureInfo.InvariantCulture, $"✓ database updated ({applyingCount} migration{(applyingCount == 1 ? "" : "s")} applied)\n");

        if (migrations.Count > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{migrations.Count} migration{(migrations.Count == 1 ? "" : "s")} (latest: {migrations[^1]})\n");

        return "✓ database updated (already up-to-date)\n";
    }

    // Matches migration list lines: "20231001000000_InitialCreate (Applied)" or "20231001000000_InitialCreate"
    [GeneratedRegex(@"^\s*(?<name>\d{14}_[A-Za-z0-9_]+)(\s+\(.*\))?\s*$")]
    private static partial Regex MigrationListPattern();

    // Matches database update lines: "Applying migration 'MigrationName'."
    [GeneratedRegex(@"Applying migration '(?<name>[^']+)'")]
    private static partial Regex ApplyingMigrationPattern();
}
```

**Key design decisions:**

- **No explicit noise-stripping loop**: The filter only collects lines that match meaningful patterns. EF banner ASCII art, build output, and verbose "Finding..." lines are all implicitly ignored — they don't match `MigrationListPattern` or `ApplyingMigrationPattern`, and they don't contain "To undo this action".
- **`string.Create(CultureInfo.InvariantCulture, $"...")`**: Required for integer interpolation to satisfy CA1305 analyzer.
- **`migrations[^1]`**: C# 8+ index-from-end syntax to get the last migration name. This is the "latest" migration.
- **`List<string>` without `using System.Collections.Generic;`**: Implicit usings cover this.
- **No `StringBuilder`**: All return paths produce short, single-line strings — no need for `StringBuilder`.
- **`string.Empty` for null/empty**: Consistent with all other filters.
- **`(Applied)` / `(Pending)` stripped**: The `MigrationListPattern` captures only the migration name (group `name`), not the status suffix.

**Note on `migrations add` output**: The EF CLI does NOT include the migration name in the `migrations add` output (only "Done. To undo this action, run 'dotnet ef migrations remove'"). Therefore, the filter returns `✓ migration added\n` without the name, which differs from the aspirational AC in the epics file. This is the correct implementation given real EF CLI behavior.

### Updated `DotnetEfCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetEfCommand(
    FilteredRunUseCase filteredRun,
    DotnetEfFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("ef").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import.

### Updated `DependencyInjection.cs` (Application project)

Add one line after `DotnetRunFilter`. No constructor args → no factory lambda:

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddSingleton<DotnetBuildFilter>(_ => new DotnetBuildFilter());
    services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter());
    services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter());
    services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter());
    services.AddSingleton<DotnetPackFilter>(_ => new DotnetPackFilter());
    services.AddSingleton<DotnetCleanFilter>();
    services.AddSingleton<DotnetRunFilter>();
    services.AddSingleton<DotnetEfFilter>();  // NEW
    return services;
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_ef_raw.txt`:

```sh
Build started...
Build succeeded.
                     _/\__
               ---==/    \\
         ___  ___   |.    \|\
        | __|| __|  |  )   \\\
        | _| | _|   \_/ |  //|\\
        |___||_|       /   \\\/\\
Entity Framework Core .NET Command-line Tools 8.0.0

Finding DbContext classes...
Finding IDesignTimeDbContextFactory implementations...
Finding application service provider...
Finding Microsoft.Extensions.Hosting service provider...
Using context 'AppDbContext'.
20231001000000_InitialCreate (Applied)
20231015000000_AddUsersTable (Applied)
20231101000000_AddProductsTable (Applied)
```

**Expected output** (for snapshot verification — all preamble/banner/verbose stripped, compact summary):

```sh
3 migrations (latest: 20231101000000_AddProductsTable)
```

**Token savings calculation:**

- Fixture: ~680 chars (build preamble ~35 + banner ~270 + blank + verbose discovery ~220 + migration entries ~130)
- Output: ~55 chars
- Savings: (680 - 55) / 680 ≈ 92% ≥ 70% ✓

### Test Implementation

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetEfFilterTests
{
    private readonly DotnetEfFilter _sut = new();

    [Fact]
    public Task Apply_MigrationsListFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_MigrationsListFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, because: "ef filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("Build started")]
    [InlineData("Build succeeded")]
    [InlineData("Entity Framework Core")]
    [InlineData("Finding DbContext")]
    [InlineData("Using context")]
    public void Apply_MigrationsListFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_MigrationsListFixture_ContainsMigrationCount()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        result.Should().Contain("3 migrations");
        result.Should().Contain("latest:");
    }

    [Fact]
    public void Apply_DatabaseUpdate_ReturnsUpdatedSummary()
    {
        const string input = """
            Build started...
            Build succeeded.
            Entity Framework Core .NET Command-line Tools 8.0.0
            Finding DbContext classes...
            Using context 'AppDbContext'.
            Applying migration '20231101000000_AddProductsTable'.
            Done.
            """;
        _sut.Apply(input).Should().Be("✓ database updated (1 migration applied)\n");
    }

    [Fact]
    public void Apply_DatabaseUpdateMultipleMigrations_ReturnsUpdatedSummaryPlural()
    {
        const string input = """
            Build started...
            Build succeeded.
            Entity Framework Core .NET Command-line Tools 8.0.0
            Finding DbContext classes...
            Using context 'AppDbContext'.
            Applying migration '20231101000000_AddProductsTable'.
            Applying migration '20231201000000_AddOrdersTable'.
            Done.
            """;
        _sut.Apply(input).Should().Be("✓ database updated (2 migrations applied)\n");
    }

    [Fact]
    public void Apply_DatabaseUpdateAlreadyUpToDate_ReturnsFallback()
    {
        const string input = """
            Build started...
            Build succeeded.
            Entity Framework Core .NET Command-line Tools 8.0.0
            Finding DbContext classes...
            Using context 'AppDbContext'.
            Done.
            """;
        _sut.Apply(input).Should().Be("✓ database updated (already up-to-date)\n");
    }

    [Fact]
    public void Apply_MigrationAdd_ReturnsMigrationAdded()
    {
        const string input = """
            Build started...
            Build succeeded.
            Entity Framework Core .NET Command-line Tools 8.0.0
            Finding DbContext classes...
            Using context 'AppDbContext'.
            To undo this action, run 'dotnet ef migrations remove'
            """;
        _sut.Apply(input).Should().Be("✓ migration added\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetEfFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

**Test count**: 14 tests (1 snapshot + 1 savings + 5 noise theory + 1 count + 1 db-update-single + 1 db-update-plural + 1 db-already-updated + 1 migration-add + 1 null + 1 empty). New total: 111 + 14 = ~125 tests.

**Important notes on tests:**

- `_sut = new()` — no constructor args (stateless filter, no `rootPath`)
- `Verify(result)` returns `Task` — test method must return `Task` (not `void`), must NOT be `async`
- Inline strings use C# raw string literals (`""" ... """`)
- `Apply_DatabaseUpdateMultipleMigrations_ReturnsUpdatedSummaryPlural` covers the plural form `"migrations"` (not `"migration"`) for N>1

### Verify Snapshot Acceptance Workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetEfFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.received.txt` — should contain `3 migrations (latest: 20231101000000_AddProductsTable)`
4. Accept by renaming: `mv DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.received.txt DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.verified.txt`
5. Re-run tests — all pass
6. Commit the `.verified.txt` file

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5–4.2)

- **CA1852** — `DotnetEfFilter` MUST be `sealed`
- **CA1050/RCS1110/S3903** — type MUST be in named namespace (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **`sealed partial class`** — because of `[GeneratedRegex]`; do NOT forget `partial`
- **`using System.Text.RegularExpressions;`** — NOT in implicit usings; must be explicit
- **`using System.Globalization;`** — NOT in implicit usings; must be explicit (for `CultureInfo.InvariantCulture`)
- **No `using System.Text;`** — NOT needed (no `StringBuilder`)
- **CA1305** — `string.Create(CultureInfo.InvariantCulture, $"...")` for all interpolations containing integer counts; plain string literals (no interpolation) are fine without CultureInfo
- **CA1307/CA1309** — `line.Contains("...", StringComparison.OrdinalIgnoreCase)` — always pass `StringComparison` explicitly
- **Import ordering**: After `dotnet format`, project usings (Helpers, Domain) come before system usings (Globalization, RegularExpressions) per `.editorconfig`
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetEfFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`List<string>` without explicit using**: Covered by implicit usings in .NET 10

### Git Context (Recent Commits)

```sh
c568abc Feat: restore, publish & pack filters (#5)
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
f1dbde2 Feat: test filters (#2)
d534cbd Feat: core & foundation (#1)
4c78002 Scaffold Clean Architecture solution
```

Current branch: `develop`. Epic 4 stories are implemented on feature branches (stories 4.1 and 4.2 are in review status and their files exist on disk as untracked changes).

### Previous Story Intelligence (Story 4.2 — Run Filter)

Key learnings applied here:

- **`sealed partial class` pattern**: Same as story 4.2 (`DotnetRunFilter`); needed for `[GeneratedRegex]`
- **No `rootPath` constructor arg**: EF filter is stateless; `_sut = new()` (no arg)
- **Snapshot directory**: configured globally in `VerifyInit.cs` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: test method must return `Task`, must NOT be `async`
- **`dotnet format` run last**: always run after all tests pass — catches import ordering issues
- **Test class naming**: `DotnetEfFilterTests` in `DotnetTokenKiller.Application.Tests.Filters` namespace
- **`DotnetEfCommand` current state**: `DotnetEfCommand(ICommandRunner commandRunner)` → same rewiring as stories 4.1 and 4.2 before them
- **Fixture savings must exceed threshold**: Target is 92% >> 70%; fixture design uses heavy EF banner/verbose output

### What This Story Does NOT Implement (Scope Guard)

- `DotnetFormatFilter`, `DotnetNugetFilter` — Stories 4.4–4.5
- Passthrough for unrecognized subcommands — Story 4.6
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- `ef migrations remove` command output — not a standard fixture; handled by fallback
- Any other CLI command rewiring besides `DotnetEfCommand`

### Project Structure Notes

- Alignment with unified project structure (paths, modules, naming): no new directories required — all target directories already exist
- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetEfFilter` singleton after `DotnetRunFilter`
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_ef_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.verified.txt`
- Detected conflicts or variances: none — follows established pattern exactly

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.3]
- [Source: _bmad-output/implementation-artifacts/4-2-implement-dotnet-run-filter-with-tests.md] — previous story; same command rewiring pattern, same stateless filter structure
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs] — reference for `sealed partial class` + `[GeneratedRegex]` pattern (stateless)
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs] — reference for `string.Create(CultureInfo.InvariantCulture, ...)` and multiple `[GeneratedRegex]` patterns
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs] — reference for stateless sealed class
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs] — current state (uses `RunPassthroughAsync`)
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — current state (needs `DotnetEfFilter` added)
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init pattern
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design] — filter constraints (stateless, sealed, non-throwing)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

None — implementation completed without issues.

### Completion Notes List

- Implemented `DotnetEfFilter` as `sealed partial class` with two `[GeneratedRegex]` patterns: `MigrationListPattern` (migration list lines) and `ApplyingMigrationPattern` (database update lines). Pattern-match-only design implicitly strips all EF banner/verbose noise.
- Rewired `DotnetEfCommand` from `ICommandRunner.RunPassthroughAsync` to `FilteredRunUseCase.RunAsync` — same pattern as stories 4.1 and 4.2.
- Registered `DotnetEfFilter` as singleton in `AddApplication()`.
- 14 new tests (1 snapshot + 1 savings ≥70% + 5 noise theory + 1 count + 2 db-update + 1 db-fallback + 1 migration-add + 1 null + 1 empty). Actual savings: ~92% >> 70% threshold.
- Snapshot accepted: `DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.verified.txt` → `3 migrations (latest: 20231101000000_AddProductsTable)`.
- Build: 0 errors, 0 warnings. Total tests: 145 (all pass). Format: clean.

### File List

- `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs` (new)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (modified — added `DotnetEfFilter` singleton)
- `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs` (modified — rewired to `FilteredRunUseCase`)
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_ef_raw.txt` (new)
- `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetEfFilterTests.Apply_MigrationsListFixture_MatchesSnapshot.verified.txt` (new)
- `_bmad-output/implementation-artifacts/4-3-implement-dotnet-ef-filter-with-tests.md` (status updated)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (status updated)

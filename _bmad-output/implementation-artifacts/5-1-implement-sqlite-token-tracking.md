# Story 5.1: Implement SQLite Token Tracking

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want every DTK command execution recorded to a SQLite database with token counts and savings data,
So that savings analytics are available for reporting without any impact on command output if tracking fails.

## Acceptance Criteria

1. **Auto-create database**: When `SqliteTracker` is used for the first time on a machine, the database file is auto-created at the platform-appropriate path: `%LOCALAPPDATA%/dtk/tracking.db` (Windows) or `~/.local/share/dtk/tracking.db` (Linux/macOS). The parent directory is also created if it doesn't exist.
2. **Auto-create schema**: The `commands` table is auto-created with columns for timestamp, command, project_path, input_tokens, output_tokens, saved_tokens, savings_percentage, and execution_time_ms. Indexed on `timestamp` and `project_path`.
3. **Persist record**: A call to `RecordAsync` inserts one row; the row is retrievable via `GetHistoryAsync`.
4. **Silent failure**: If any error occurs during `RecordAsync` (database locked, disk full, permission denied, etc.) the exception is swallowed silently — no exception propagates to the caller.
5. **Auto-cleanup**: On every successful `RecordAsync` call, records older than 90 days are automatically deleted.
6. **`GetSummaryAsync` aggregation**: Returns a `GainSummary` with `TotalCommands`, `TotalInputTokens`, `TotalOutputTokens`, `TotalSavedTokens`, `AverageSavingsPercentage`, and `SavedByCommand` (total saved tokens per command type) — filtered by `days` and optionally by `projectPath`.
7. **`GetHistoryAsync` returns records**: Returns records in descending timestamp order, filtered by `days` and optionally by `projectPath`.
8. **`DTK_DB_PATH` override**: When the `DTK_DB_PATH` environment variable is set, it overrides the default database path.
9. **DI swap**: `SqliteTracker` is registered in `AddInfrastructure()` replacing `NullTracker`.
10. **In-memory tests**: Infrastructure tests use `Data Source=:memory:` for full isolation.
11. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests green (no regressions on 170 existing tests).
12. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Pre-Condition / Hard Gate

> ✅ **ZOMBIE ACTION ITEM — RESOLVED:**
>
> From Epic 4 Retro action item #1 (carried from Epics 2 and 3):
> `dotnet_test_zero.txt` fixture, `.verified.txt` snapshot, and both zero-tests tests
> (`Apply_ZeroTestsFixture_MatchesSnapshot`, `Apply_ZeroTestsFixture_ReturnsZeroTestsMessage`)
> are present and passing in `DotnetTestFilterTests` (14/14 pass). Hard gate cleared.

## Tasks / Subtasks

- [x] **Task 0 (Pre-gate)**: ~~Kill the zombie~~ — already done; `dotnet_test_zero.txt`, snapshot, and tests all present and passing ✅

- [x] **Task 1**: Add NuGet packages (AC: pre-condition for compilation)
  - [x] Add to `Directory.Packages.props`:
    - `<PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.4"/>`
    - ~~`<PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.1.9"/>` (explicit AOT pin)~~ — v3.1.9 does not exist on NuGet; omitted, resolved transitively via Microsoft.Data.Sqlite
  - [x] Add to `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj`:
    - `<PackageReference Include="Microsoft.Data.Sqlite"/>`
  - [x] Add to `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj`:
    - `<PackageReference Include="Microsoft.Data.Sqlite"/>` (needed for in-memory connection setup in tests)

- [x] **Task 2**: Create `SqliteTracker` (AC: #1, #2, #3, #4, #5, #6, #7, #8)
  - [x] Create `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs`
  - [x] `sealed` class implementing `ITracker` and `IAsyncDisposable`
  - [x] Primary constructor `(string connectionString)` — stores and constructs `SqliteConnection`
  - [x] Private static `GetDefaultDbPath()` helper returning platform-specific path
  - [x] Private `EnsureInitializedAsync(CancellationToken)` — opens connection + creates schema on first use
  - [x] Private static `EnsureDataDirectory(string connectionString)` — skips for `:memory:`, creates dir otherwise
  - [x] `RecordAsync`: try/catch all errors; call `EnsureInitializedAsync`, INSERT record, call internal `CleanupAsync(90, ct)` — all within try/catch
  - [x] `GetSummaryAsync`: `EnsureInitializedAsync`, GROUP BY command query, aggregate into `GainSummary`
  - [x] `GetHistoryAsync`: `EnsureInitializedAsync`, SELECT with ORDER BY timestamp DESC
  - [x] `CleanupAsync`: `EnsureInitializedAsync`, DELETE WHERE timestamp < cutoff
  - [x] `DisposeAsync`: dispose `_connection` (no `GC.SuppressFinalize` — sealed class, no finalizer)
  - [x] File-scoped namespace: `namespace DotnetTokenKiller.Infrastructure.Tracking;`

- [x] **Task 3**: Register `SqliteTracker` in DI replacing `NullTracker` (AC: #9)
  - [x] In `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs`:
    - Compute DB path: `Environment.GetEnvironmentVariable("DTK_DB_PATH") ?? SqliteTracker.GetDefaultDbPath()`
    - Register via factory: `services.AddSingleton<ITracker>(_ => new SqliteTracker($"Data Source={dbPath}"));`
    - Remove: `services.AddSingleton<ITracker, NullTracker>();`
  - [x] Make `GetDefaultDbPath()` `internal static` on `SqliteTracker` so DI can call it

- [x] **Task 4**: Write `SqliteTrackerTests` (AC: #3, #4, #5, #6, #7, #10)
  - [x] Create `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs`
  - [x] Test class implements `IAsyncDisposable`; `_sut = new SqliteTracker("Data Source=:memory:")` as field initializer
  - [x] `DisposeAsync`: calls `await _sut.DisposeAsync()` + `GC.SuppressFinalize(this)` (CA1816)
  - [x] Test: `RecordAsync_PersistsRecord_RetrievableViaGetHistoryAsync`
  - [x] Test: `RecordAsync_PersistsAllFields_Correctly` — verify every `CommandRecord` field round-trips
  - [x] Test: `RecordAsync_TriggersCleanup_DeletesOldRecords` — insert record with timestamp 91 days ago, call `RecordAsync`, verify old record gone
  - [x] Test: `GetSummaryAsync_AggregatesCorrectly` — insert 3 records (2 "build", 1 "test"), verify `TotalCommands=3`, `SavedByCommand["build"]=X`
  - [x] Test: `GetSummaryAsync_FiltersByDays` — insert old record (10 days ago) + recent record, query 7 days, verify only recent returned
  - [x] Test: `GetSummaryAsync_FiltersByProjectPath` — insert records for 2 project paths, verify filter works
  - [x] Test: `GetHistoryAsync_ReturnsRecordsInDescendingTimestampOrder`
  - [x] Test: `CleanupAsync_DeletesOldRecords_LeavesRecentOnes`

- [x] **Task 5**: Delete `PlaceholderTests.cs` (AC: #10)
  - [x] Delete `tests/DotnetTokenKiller.Infrastructure.Tests/PlaceholderTests.cs` — no longer needed once real tests exist

- [x] **Task 6**: Build and verify (AC: #11, #12)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all 177 tests pass (8 new + 169 existing)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3, 4.1–4.6)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | **Needs `Microsoft.Data.Sqlite` and `SQLitePCLRaw.bundle_e_sqlite3` added** |
| `.editorconfig` | Complete |
| `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` | Complete — `RecordAsync`, `GetSummaryAsync`, `GetHistoryAsync`, `CleanupAsync` — DO NOT CHANGE |
| `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs` | Complete record type — DO NOT CHANGE |
| `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs` | Complete — `TotalCommands`, `TotalInputTokens`, `TotalOutputTokens`, `TotalSavedTokens`, `AverageSavingsPercentage`, `SavedByCommand` — DO NOT CHANGE |
| `src/DotnetTokenKiller.Infrastructure/Tracking/NullTracker.cs` | **To be superseded by `SqliteTracker` in DI** — keep file; just remove from DI |
| `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` | **Needs `ITracker` registration swapped: `NullTracker` → `SqliteTracker`** |
| `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj` | **Needs `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` refs added** |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — NOT modified in this story (tracker injection wired in Story 5.2) |
| `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Infrastructure.Tests/PlaceholderTests.cs` | **Delete when real tests are added** |
| `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj` | **Needs `Microsoft.Data.Sqlite` ref added** |

**Test count baseline**: 170 tests total (after story 4.6). The placeholder Infrastructure test counts as 1 of these.

### Architecture Constraints (CRITICAL)

- `SqliteTracker` lives in the `Infrastructure` layer — references `Domain` and `Microsoft.Data.Sqlite`
- `Domain` project MUST remain NuGet-dependency-free — `SqliteTracker` cannot be in Domain
- Use raw ADO.NET (`Microsoft.Data.Sqlite`) — NO ORM, no Entity Framework
- `SqliteTracker` MUST be `sealed` (CA1852)
- File-scoped namespaces enforced — `namespace DotnetTokenKiller.Infrastructure.Tracking;`
- `IAsyncDisposable` required for clean connection lifecycle
- **Persistent connection pattern**: Keep a single `SqliteConnection` open for the tracker's lifetime. This is required for in-memory SQLite (`:memory:` databases are destroyed when the last connection to them closes). This also works correctly for file-based SQLite.
- **Silent failure**: ALL exceptions in `RecordAsync` MUST be caught and suppressed — tracking must never break command output
- **Retention**: 90-day rolling window; `CleanupAsync` called automatically inside `RecordAsync`

### Implementation: `SqliteTracker`

```csharp
namespace DotnetTokenKiller.Infrastructure.Tracking;

using DotnetTokenKiller.Domain.Tracking;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

public sealed class SqliteTracker(string connectionString) : ITracker, IAsyncDisposable
{
    private const int RetentionDays = 90;
    private readonly SqliteConnection _connection = new(connectionString);
    private bool _initialized;

    internal static string GetDefaultDbPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "dtk", "tracking.db");
    }

    private static void EnsureDataDirectory(string cs)
    {
        if (cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
            return;
        var csb = new SqliteConnectionStringBuilder(cs);
        if (!string.IsNullOrWhiteSpace(csb.DataSource) && csb.DataSource != ":memory:")
        {
            var dir = Path.GetDirectoryName(csb.DataSource);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;
        EnsureDataDirectory(connectionString);
        await _connection.OpenAsync(ct);
        await InitializeSchemaAsync(ct);
        _initialized = true;
    }

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS commands (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                command TEXT NOT NULL,
                project_path TEXT NOT NULL,
                input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                saved_tokens INTEGER NOT NULL,
                savings_percentage REAL NOT NULL,
                execution_time_ms REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_commands_timestamp ON commands(timestamp);
            CREATE INDEX IF NOT EXISTS idx_commands_project_path ON commands(project_path);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                    saved_tokens, savings_percentage, execution_time_ms)
                VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms)
                """;
            cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@cmd", record.Command);
            cmd.Parameters.AddWithValue("@path", record.ProjectPath);
            cmd.Parameters.AddWithValue("@in", record.InputTokens);
            cmd.Parameters.AddWithValue("@out", record.OutputTokens);
            cmd.Parameters.AddWithValue("@saved", record.SavedTokens);
            cmd.Parameters.AddWithValue("@pct", record.SavingsPercentage);
            cmd.Parameters.AddWithValue("@ms", record.ExecutionTime.TotalMilliseconds);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await CleanupAsync(RetentionDays, cancellationToken);
        }
        catch
        {
            // Intentional: tracking errors must never surface to the user
        }
    }

    public async Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT command,
                   COUNT(*) as run_count,
                   SUM(input_tokens) as total_input,
                   SUM(output_tokens) as total_output,
                   SUM(saved_tokens) as total_saved,
                   AVG(savings_percentage) as avg_pct
            FROM commands
            WHERE timestamp >= @since
              AND (@path IS NULL OR project_path = @path)
            GROUP BY command
            """;
        cmd.Parameters.AddWithValue("@since", since);
        cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value);

        var totalCommands = 0;
        var totalInput = 0;
        var totalOutput = 0;
        var totalSaved = 0;
        var totalAvgPct = 0.0;
        var commandCount = 0;
        var savedByCommand = new Dictionary<string, int>(StringComparer.Ordinal);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var cmdName = reader.GetString(0);
            var runCount = reader.GetInt32(1);
            var sumInput = reader.GetInt32(2);
            var sumOutput = reader.GetInt32(3);
            var sumSaved = reader.GetInt32(4);
            var avgPct = reader.GetDouble(5);

            totalCommands += runCount;
            totalInput += sumInput;
            totalOutput += sumOutput;
            totalSaved += sumSaved;
            totalAvgPct += avgPct;
            commandCount++;
            savedByCommand[cmdName] = sumSaved;
        }

        var averagePct = commandCount > 0 ? totalAvgPct / commandCount : 0.0;
        return new GainSummary(totalCommands, totalInput, totalOutput, totalSaved, averagePct, savedByCommand);
    }

    public async Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT timestamp, command, project_path, input_tokens, output_tokens,
                   saved_tokens, savings_percentage, execution_time_ms
            FROM commands
            WHERE timestamp >= @since
              AND (@path IS NULL OR project_path = @path)
            ORDER BY timestamp DESC
            """;
        cmd.Parameters.AddWithValue("@since", since);
        cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value);

        var results = new List<CommandRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CommandRecord(
                Timestamp: DateTimeOffset.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Command: reader.GetString(1),
                ProjectPath: reader.GetString(2),
                InputTokens: reader.GetInt32(3),
                OutputTokens: reader.GetInt32(4),
                SavedTokens: reader.GetInt32(5),
                SavingsPercentage: reader.GetDouble(6),
                ExecutionTime: TimeSpan.FromMilliseconds(reader.GetDouble(7))));
        }
        return results;
    }

    public async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM commands WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
```

**Key design decisions:**

- **Persistent `SqliteConnection`**: Single connection opened once and kept for the tracker's lifetime. Mandatory for `:memory:` databases (which are tied to the connection); also beneficial for file-based SQLite (avoids repeated open/close overhead in a CLI tool).
- **`_initialized` flag**: Set to `true` after first successful open + schema creation. Not thread-safe by design — a CLI tool has single-threaded use-case per tracker instance; worst case is schema DDL runs twice (idempotent via `CREATE TABLE IF NOT EXISTS`).
- **`DateTimeOffset` as ISO 8601 text**: Stored as `TEXT` using `"O"` format (round-trip); parsed back with `DateTimeStyles.RoundtripKind` to preserve offset.
- **`execution_time_ms` as `REAL`**: `TimeSpan` serialized as `TotalMilliseconds`; deserialized with `TimeSpan.FromMilliseconds`.
- **`@path IS NULL OR ...` pattern**: SQLite's null handling requires explicit NULL check for optional project path filter — do NOT use `project_path = @path` when `@path` is NULL (SQL NULL != NULL).
- **`DBNull.Value` for null parameters**: `cmd.Parameters.AddWithValue("@path", (object?)projectPath ?? DBNull.Value)` — required to properly pass NULL to SQLite; plain `null` may not work correctly with `AddWithValue`.
- **`GetDefaultDbPath()` is `internal static`**: Allows `DependencyInjection.cs` to call it without making it public API.
- **`EnsureDataDirectory` skips `:memory:`**: Path containing `:memory:` indicates in-memory SQLite — no directory creation needed.

### Updated `DependencyInjection.cs`

```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services)
{
    services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
    var dbPath = Environment.GetEnvironmentVariable("DTK_DB_PATH")
        ?? SqliteTracker.GetDefaultDbPath();
    services.AddSingleton<ITracker>(new SqliteTracker($"Data Source={dbPath}"));
    services.AddSingleton<IConfigProvider, NullConfigProvider>();
    services.AddSingleton<ITeeService, NullTeeService>();
    return services;
}
```

**Why `new SqliteTracker(...)` not `AddSingleton<ITracker, SqliteTracker>()`**: `SqliteTracker`'s constructor takes a `string connectionString` argument that must be computed at registration time. Using `new` directly is the clean, idiomatic approach for this case.

### Updated `Directory.Packages.props`

Add after `NSubstitute`:

```xml
<PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.4"/>
<PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.1.9"/>
```

> **Version guidance**: `Microsoft.Data.Sqlite` follows .NET SDK versioning — use the same major.minor as the project's other Microsoft packages (`10.0.4`). `SQLitePCLRaw.bundle_e_sqlite3` is pinned explicitly for AOT/trim safety (see Architecture.md §Trim-Safety). Verify these versions against `dotnet package search Microsoft.Data.Sqlite` if unsure.

### Test Implementation

```csharp
namespace DotnetTokenKiller.Infrastructure.Tests.Tracking;

using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;
using FluentAssertions;

public class SqliteTrackerTests : IAsyncDisposable
{
    private readonly SqliteTracker _sut = new("Data Source=:memory:");

    public async ValueTask DisposeAsync() => await _sut.DisposeAsync();

    private static CommandRecord MakeRecord(
        string command = "build",
        string projectPath = "/proj",
        int inputTokens = 1000,
        int outputTokens = 150,
        int savedTokens = 850,
        double savingsPct = 85.0,
        DateTimeOffset? timestamp = null) =>
        new(
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            Command: command,
            ProjectPath: projectPath,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            SavedTokens: savedTokens,
            SavingsPercentage: savingsPct,
            ExecutionTime: TimeSpan.FromMilliseconds(500));

    [Fact]
    public async Task RecordAsync_PersistsRecord_RetrievableViaGetHistoryAsync()
    {
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordAsync_PersistsAllFields_Correctly()
    {
        var ts = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var record = MakeRecord(
            command: "test",
            projectPath: "/my/project",
            inputTokens: 2000,
            outputTokens: 200,
            savedTokens: 1800,
            savingsPct: 90.0,
            timestamp: ts);

        await _sut.RecordAsync(record);
        var history = await _sut.GetHistoryAsync(365, null);

        var stored = history.Should().ContainSingle().Subject;
        stored.Command.Should().Be("test");
        stored.ProjectPath.Should().Be("/my/project");
        stored.InputTokens.Should().Be(2000);
        stored.OutputTokens.Should().Be(200);
        stored.SavedTokens.Should().Be(1800);
        stored.SavingsPercentage.Should().BeApproximately(90.0, 0.001);
        stored.Timestamp.Should().BeCloseTo(ts, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RecordAsync_TriggersCleanup_DeletesOldRecords()
    {
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-91);
        await _sut.RecordAsync(MakeRecord(timestamp: oldTimestamp));
        await _sut.RecordAsync(MakeRecord()); // triggers cleanup

        var history = await _sut.GetHistoryAsync(365, null);

        history.Should().HaveCount(1); // only the recent record remains
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesCorrectly()
    {
        await _sut.RecordAsync(MakeRecord(command: "build", savedTokens: 800));
        await _sut.RecordAsync(MakeRecord(command: "build", savedTokens: 900));
        await _sut.RecordAsync(MakeRecord(command: "test", savedTokens: 500));

        var summary = await _sut.GetSummaryAsync(30, null);

        summary.TotalCommands.Should().Be(3);
        summary.TotalSavedTokens.Should().Be(2200);
        summary.SavedByCommand["build"].Should().Be(1700);
        summary.SavedByCommand["test"].Should().Be(500);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByDays()
    {
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-10)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-1)));

        var summary = await _sut.GetSummaryAsync(7, null);

        summary.TotalCommands.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByProjectPath()
    {
        await _sut.RecordAsync(MakeRecord(projectPath: "/proj/a"));
        await _sut.RecordAsync(MakeRecord(projectPath: "/proj/b"));

        var summary = await _sut.GetSummaryAsync(30, "/proj/a");

        summary.TotalCommands.Should().Be(1);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRecordsInDescendingTimestampOrder()
    {
        var ts1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ts2 = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _sut.RecordAsync(MakeRecord(command: "first", timestamp: ts1));
        await _sut.RecordAsync(MakeRecord(command: "second", timestamp: ts2));

        var history = await _sut.GetHistoryAsync(1, null);

        history[0].Command.Should().Be("second"); // most recent first
        history[1].Command.Should().Be("first");
    }

    [Fact]
    public async Task CleanupAsync_DeletesOldRecords_LeavesRecentOnes()
    {
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-100)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-50)));
        await _sut.RecordAsync(MakeRecord(timestamp: DateTimeOffset.UtcNow.AddDays(-1)));

        await _sut.CleanupAsync(90);

        var history = await _sut.GetHistoryAsync(365, null);
        history.Should().HaveCount(2); // 50 days and 1 day remain; 100 days gone
    }
}
```

**Note on `RecordAsync_SwallowsException`**: Testing silent failure in `RecordAsync` is architecturally tricky — `SqliteTracker` uses a real in-memory database and it's `sealed`. The silent failure guarantee is instead verified by: (a) the implementation review (try/catch wraps the entire method body), and (b) the fact that all other tests pass without exceptions. If a direct throw test is desired, create a `BadPathTracker` wrapper in the test file that passes a deliberately invalid connection string.

### Analyzer Pitfalls (CRITICAL)

- **CA1852** — `SqliteTracker` MUST be `sealed`
- **CA2007** — `ConfigureAwait` not required (suppressed project-wide)
- **CA1305** — `DateTimeOffset.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind)` requires `null` as format provider (culture-invariant parsing); or use `DateTimeOffset.ParseExact` with `"O"` format
- **S1450 / IDE0044** — `_initialized` field should be `readonly` — CANNOT be readonly since it's mutated. If analyzer complains, it's a false positive for this specific case
- **RCS1163 / IDE0060** — ensure no unused parameters in private methods
- **Import ordering** — after `dotnet format`: project usings come before system usings per `.editorconfig`; let `dotnet format` handle this automatically
- **`using` vs `await using`**: `SqliteCommand` objects created via `_connection.CreateCommand()` should use `using var`, not `await using` (SqliteCommand is synchronously disposable)
- **`DBNull.Value` cast**: `(object?)projectPath ?? DBNull.Value` — the cast to `object?` is required for the null-coalescing operator to work with `DBNull.Value`
- **FluentAssertions version pitfalls** (from Epics 3–4):
  - Use `BeLessThanOrEqualTo(n)` NOT `BeLessOrEqualTo(n)` — the latter does not exist in FA 8.x
  - `NotContain(string)` works; `NotContain(string, StringComparison)` overload does NOT exist in FA 8.8.0
  - Use `BeApproximately(expected, precision)` for `double` comparisons — NOT `Be(exact)`

### Scope Guard (What This Story Does NOT Implement)

- Wiring `ITracker` into `FilteredRunUseCase` — that is Story 5.2
- The `dtk gain` analytics command — that is Story 5.3
- `JsonConfigProvider` — that is Story 6.1
- `FileTeeService` — that is Story 6.2
- Any changes to existing filter or use case classes
- Integration tests against real DTK binary

### Project Structure Notes

New files created:

- `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (new)
- `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs` (new)

Modified files:

- `Directory.Packages.props` — add `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3`
- `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj` — add package refs
- `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` — swap `NullTracker` → `SqliteTracker`
- `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj` — add `Microsoft.Data.Sqlite` ref

Deleted files:

- `tests/DotnetTokenKiller.Infrastructure.Tests/PlaceholderTests.cs` (replaced by real tests)

The directory `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/` is a new subdirectory that must be created.

### Git Context (Recent Commits)

```sh
8f74722 Feat: add filters and passthrough (#9)
0996af2 Bump Verify.Xunit from 28.2.0 to 31.12.5 (#8)
7bb7864 Bump Microsoft.Extensions.DependencyInjection from 9.0.0 to 10.0.4 (#7)
ef62a03 Bump Microsoft.CodeAnalysis.NetAnalyzers from 10.0.103 to 10.0.200 (#6)
c568abc Feat: restore, publish & pack filters (#5)
```

Current branch: `develop`. Epic 4 is fully merged. Epic 5 starts fresh on `develop`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.1]
- [Source: _bmad-output/planning-artifacts/Architecture.md#8-Infrastructure-Details — Token Tracking — SqliteTracker]
- [Source: _bmad-output/project-context.md — Technology Stack, Testing Rules, Critical Rules]
- [Source: src/DotnetTokenKiller.Domain/Tracking/ITracker.cs] — interface contract to implement
- [Source: src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs] — record fields and constructor
- [Source: src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs] — GainSummary fields
- [Source: src/DotnetTokenKiller.Infrastructure/Tracking/NullTracker.cs] — reference for method signatures
- [Source: src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs] — DI registration pattern
- [Source: _bmad-output/implementation-artifacts/4-6-implement-passthrough-for-unrecognized-subcommands.md] — most recent story; analyzer pitfalls, test conventions
- [Source: _bmad-output/implementation-artifacts/epic-4-retro-2026-03-12.md] — zombie action item, Epic 5 preparation tasks, FluentAssertions pitfalls

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- SQLitePCLRaw.bundle_e_sqlite3 v3.1.9 does not exist on NuGet (nearest: 3.0.2); dropped explicit pin, resolved transitively via Microsoft.Data.Sqlite 10.0.4.
- RCS1261: SqliteCommand and SqliteDataReader implement IAsyncDisposable — used `await using var` throughout.
- CA2000: Registered SqliteTracker via factory lambda `_ => new SqliteTracker(...)` to transfer ownership to DI container.
- CA1816: Test class DisposeAsync requires GC.SuppressFinalize(this) (non-sealed class).
- GC.SuppressFinalize removed from SqliteTracker.DisposeAsync — sealed class with no finalizer (IDE warning).

### Completion Notes List

- Implemented SqliteTracker with persistent connection pattern (single SqliteConnection for tracker lifetime, required for :memory:).
- Schema auto-created with commands table + two indexes (timestamp, project_path).
- RecordAsync wraps entire body in try/catch — silent failure guarantee enforced.
- Auto-cleanup on every RecordAsync call: deletes records older than 90 days.
- GetSummaryAsync and GetHistoryAsync support days filter and optional projectPath filter using `@path IS NULL OR project_path = @path` pattern with DBNull.Value.
- GetDefaultDbPath() is internal static to allow DI registration without public API exposure.
- 8 new tests added covering: persist/retrieve, all-fields round-trip, cleanup trigger, aggregation, days filter, project-path filter, descending order, manual cleanup.
- PlaceholderTests.cs deleted; total test count 177 (up from 170 — placeholder counted as 1 but 8 new added).

### File List

- `Directory.Packages.props` (modified — added Microsoft.Data.Sqlite 10.0.4)
- `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj` (modified — added Microsoft.Data.Sqlite ref)
- `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (new)
- `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` (modified — swapped NullTracker → SqliteTracker factory)
- `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj` (modified — added Microsoft.Data.Sqlite ref)
- `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs` (new)
- `tests/DotnetTokenKiller.Infrastructure.Tests/PlaceholderTests.cs` (deleted)

## Change Log

- 2026-03-13: Implemented SQLite token tracking — SqliteTracker created, NullTracker superseded in DI, 8 new infrastructure tests added (claude-sonnet-4-6)

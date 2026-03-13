# Story 6.1: Implement JSON Configuration

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want DTK to load and save user configuration from a JSON file with sensible defaults,
so that I can customize tracking, display, and tee behavior without any code changes.

## Acceptance Criteria

1. When no config file exists, `JsonConfigProvider.LoadAsync` returns `DtkConfig.Default` (tracking enabled, 90-day retention, colors enabled, emoji enabled, tee mode "failures", max 20 files, max 1 MB).
2. When a config file exists with partial overrides, the loaded values are merged with defaults — missing fields use defaults, not nulls or zeros.
3. Config file path resolves to: `%APPDATA%/dtk/config.json` (Windows) or `~/.config/dtk/config.json` (Linux/macOS).
4. Invalid JSON (malformed, empty, wrong schema) returns `DtkConfig.Default` without throwing.
5. `System.Text.Json` source-generated serialization used throughout — no runtime reflection, AOT-compatible.
6. `SaveAsync` writes the config to the platform path, creating the directory if needed.
7. `JsonConfigProvider` is registered in DI replacing `NullConfigProvider` in `DependencyInjection.cs`.
8. Infrastructure.Tests cover: no file → defaults, partial file → merged, invalid JSON → defaults, save → readable back.
9. All tests pass. `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] **Task 1**: Create `DtkConfigJsonContext` source-generated serializer context (AC: #5)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/Configuration/DtkConfigJsonContext.cs`
  - [x] Namespace: `DotnetTokenKiller.Infrastructure.Configuration`
  - [x] `[JsonSerializable(typeof(DtkConfig))]` on `internal sealed partial class DtkConfigJsonContext : JsonSerializerContext`

- [x] **Task 2**: Create `JsonConfigProvider` (AC: #1–#6)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/Configuration/JsonConfigProvider.cs`
  - [x] `GetDefaultConfigPath()`: static method using `Environment.SpecialFolder.ApplicationData` + `dtk/config.json`
  - [x] `LoadAsync()`: read file → deserialize with source gen context → return `DtkConfig.Default` on missing file or any exception
  - [x] Merge partial overrides: use `DtkConfig.Default` as base, overlay individual fields from JSON
  - [x] `SaveAsync()`: `Directory.CreateDirectory(directory)`, serialize with source gen context, `File.WriteAllTextAsync`

- [x] **Task 3**: Update `DependencyInjection.cs` to register `JsonConfigProvider` (AC: #7)
  - [x] Replace `services.AddSingleton<IConfigProvider, NullConfigProvider>()` with `services.AddSingleton<IConfigProvider, JsonConfigProvider>()`

- [x] **Task 4**: Write Infrastructure tests (AC: #8, #9)
  - [x] File: `tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs`
  - [x] Namespace: `DotnetTokenKiller.Infrastructure.Tests.Configuration`
  - [x] Implement `IDisposable` — clean up temp directory in `Dispose()`
  - [x] Test: `LoadAsync_ReturnsDefaults_WhenNoFileExists`
  - [x] Test: `LoadAsync_MergesPartialOverrides_WithDefaults`
  - [x] Test: `LoadAsync_MergesPartialOverrides_WhenSubRecordHasMissingValueTypeFields`
  - [x] Test: `LoadAsync_ReturnsDefaults_WhenJsonIsInvalid`
  - [x] Test: `SaveAsync_ThenLoadAsync_RoundTrips`
  - [x] Test: `SaveAsync_CreatesDirectory_WhenNotExisting`

- [x] **Task 5**: Build and verify (AC: #9)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests green
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Pre-Condition / Current State

| File | Current State |
|---|---|
| `src/DotnetTokenKiller.Infrastructure/Configuration/NullConfigProvider.cs` | **Stub** — always returns `DtkConfig.Default`, always no-ops `SaveAsync` — REPLACE with `JsonConfigProvider` |
| `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` | **Needs update** — change `NullConfigProvider` registration to `JsonConfigProvider` |
| `src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs` | **Complete** — records with sensible defaults; DO NOT MODIFY |
| `src/DotnetTokenKiller.Domain/Configuration/IConfigProvider.cs` | **Complete** — `LoadAsync` + `SaveAsync` contract; DO NOT MODIFY |
| `tests/DotnetTokenKiller.Infrastructure.Tests/` | **Existing** — only `SqliteTrackerTests.cs` present; add `Configuration/` subfolder |

### Architecture Compliance — CRITICAL

Clean architecture dependency rules:

- `DotnetTokenKiller.Infrastructure.Configuration` → references `DotnetTokenKiller.Domain.Configuration` only
- `System.Text.Json` is a built-in BCL API — no new NuGet packages needed; NO new entry in `Directory.Packages.props`
- Do NOT reference anything from `Application` or `Cli` layers from Infrastructure
- `JsonConfigProvider` lives in `Infrastructure.Configuration` — implements `IConfigProvider` from `Domain.Configuration`

### DtkConfig Shape (Domain — DO NOT MODIFY)

```csharp
// src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs
public sealed record DtkConfig(TrackingConfig Tracking, DisplayConfig Display, TeeConfig Tee)
{
    public static DtkConfig Default => new(new TrackingConfig(), new DisplayConfig(), new TeeConfig());
}

public sealed record TrackingConfig(bool Enabled = true, int RetentionDays = 90, string? DbPath = null);
public sealed record DisplayConfig(bool Colors = true, bool Emoji = true, int Width = 120);
public sealed record TeeConfig(string Mode = "failures", string? Directory = null, int MaxFiles = 20, long MaxFileSizeBytes = 1_048_576L);
```

### Implementation Blueprint: `DtkConfigJsonContext`

```csharp
// src/DotnetTokenKiller.Infrastructure/Configuration/DtkConfigJsonContext.cs
using DotnetTokenKiller.Domain.Configuration;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Infrastructure.Configuration;

[JsonSerializable(typeof(DtkConfig))]
internal sealed partial class DtkConfigJsonContext : JsonSerializerContext { }
```

### Implementation Blueprint: `JsonConfigProvider`

```csharp
// src/DotnetTokenKiller.Infrastructure/Configuration/JsonConfigProvider.cs
using DotnetTokenKiller.Domain.Configuration;
using System.Text.Json;

namespace DotnetTokenKiller.Infrastructure.Configuration;

public sealed class JsonConfigProvider : IConfigProvider
{
    private readonly string _configPath;

    public JsonConfigProvider() : this(GetDefaultConfigPath()) { }

    // Internal constructor for testability — accepts an arbitrary path
    internal JsonConfigProvider(string configPath)
    {
        _configPath = configPath;
    }

    public static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dtk", "config.json");
    }

    public async Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_configPath))
                return DtkConfig.Default;

            var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
            var loaded = JsonSerializer.Deserialize(json, DtkConfigJsonContext.Default.DtkConfig);
            return loaded is null ? DtkConfig.Default : Merge(loaded);
        }
        catch
        {
            return DtkConfig.Default;
        }
    }

    public async Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config, DtkConfigJsonContext.Default.DtkConfig);
        await File.WriteAllTextAsync(_configPath, json, cancellationToken);
    }

    private static DtkConfig Merge(DtkConfig loaded)
    {
        var defaults = DtkConfig.Default;
        return new DtkConfig(
            new TrackingConfig(
                Enabled: loaded.Tracking?.Enabled ?? defaults.Tracking.Enabled,
                RetentionDays: loaded.Tracking?.RetentionDays ?? defaults.Tracking.RetentionDays,
                DbPath: loaded.Tracking?.DbPath ?? defaults.Tracking.DbPath),
            new DisplayConfig(
                Colors: loaded.Display?.Colors ?? defaults.Display.Colors,
                Emoji: loaded.Display?.Emoji ?? defaults.Display.Emoji,
                Width: loaded.Display?.Width ?? defaults.Display.Width),
            new TeeConfig(
                Mode: loaded.Tee?.Mode ?? defaults.Tee.Mode,
                Directory: loaded.Tee?.Directory ?? defaults.Tee.Directory,
                MaxFiles: loaded.Tee?.MaxFiles ?? defaults.Tee.MaxFiles,
                MaxFileSizeBytes: loaded.Tee?.MaxFileSizeBytes ?? defaults.Tee.MaxFileSizeBytes));
    }
}
```

> **⚠️ CRITICAL — Nullable merge pattern:**
> `DtkConfig`, `TrackingConfig`, `DisplayConfig`, and `TeeConfig` are non-nullable records with value-type properties. The `??` merge above on value types (bool, int, long, string) compiles correctly because: `loaded.Tracking?.Enabled` is `bool?` (nullable access on nullable property access), so `?? defaults.Tracking.Enabled` resolves to `bool`. This pattern is safe as long as `Tracking`, `Display`, `Tee` sub-records are themselves nullable in the JSON deserialization context — which they will be if the JSON file is missing sections. Verify that the source generator context serializes sub-records as nullable by checking the generated code or testing with a partial JSON file.
> **Alternative if `Merge` is too complex:** Use `System.Text.Json` `JsonSerializerOptions` with `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` and a two-pass approach: deserialize into a `DtkConfigDto` with all-nullable fields, then merge. But the simpler approach above should work for this use case.
> **⚠️ WARNING — `loaded.Tracking?.RetentionDays` on a value type:**
> `TeeConfig.MaxFiles` is `int`, not `int?`. If `loaded.Tee` is not null, `loaded.Tee.MaxFiles` is always an `int` (possibly 0 if missing from JSON). The `??` operator won't fire on a non-nullable `int`. Consider using `JsonNumberHandling` or checking `loaded.Tee.MaxFiles == 0` to detect missing. **Simplest safe approach:** Don't merge manually — use `JsonSerializerOptions` with `PropertyNameCaseInsensitive = true` and let JSON nulls/missing fields stay at default values by relying on C# record default parameter values during deserialization. Test with a partial config to verify.
> **Recommended even simpler approach:** Don't implement a custom `Merge` method at all. Instead, configure the source generator to use `DtkConfig.Default` as a fallback by deserializing into the record directly and accepting that missing JSON fields result in the record constructor's default parameter values being used. Since `DtkConfig` uses record positional parameters with defaults (`bool Enabled = true`), the JSON deserializer WILL use those defaults for missing fields. Test this assumption explicitly.

### Implementation Blueprint: Updated `DependencyInjection.cs`

```csharp
// src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs
// Change only this line:
//   BEFORE: services.AddSingleton<IConfigProvider, NullConfigProvider>();
//   AFTER:
services.AddSingleton<IConfigProvider, JsonConfigProvider>();
```

No other changes to `DependencyInjection.cs`.

### Test Blueprint: `JsonConfigProviderTests.cs`

```csharp
// tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Configuration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Configuration;

public class JsonConfigProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-test-{Guid.NewGuid()}");

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    private JsonConfigProvider CreateSut() => new(ConfigPath);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenNoFileExists()
    {
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenJsonIsInvalid()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, "not valid json {{ }}");
        var sut = CreateSut();

        var config = await sut.LoadAsync();

        config.Should().Be(DtkConfig.Default);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var sut = CreateSut();
        var modified = DtkConfig.Default with
        {
            Tracking = new TrackingConfig(Enabled: false, RetentionDays: 30)
        };

        await sut.SaveAsync(modified);
        var loaded = await sut.LoadAsync();

        loaded.Tracking.Enabled.Should().BeFalse();
        loaded.Tracking.RetentionDays.Should().Be(30);
        loaded.Display.Should().Be(DtkConfig.Default.Display);
        loaded.Tee.Should().Be(DtkConfig.Default.Tee);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory_WhenNotExisting()
    {
        var sut = CreateSut();

        await sut.SaveAsync(DtkConfig.Default);

        File.Exists(ConfigPath).Should().BeTrue();
    }
}
```

> **⚠️ No partial-override JSON test?** The AC requires "partial overrides merged with defaults". If the source generator + record defaults handle this automatically (missing JSON fields → default values), then the round-trip test above already validates this. Add an explicit partial-JSON test:
>
> ```csharp
> [Fact]
> public async Task LoadAsync_MergesPartialOverrides_RetentionDaysFromJson_OtherFieldsDefault()
> {
>     Directory.CreateDirectory(_tempDir);
>     // Only override RetentionDays — all other fields missing
>     await File.WriteAllTextAsync(ConfigPath,
>         """{"Tracking":{"RetentionDays":45},"Display":{},"Tee":{}}""");
>     var sut = CreateSut();
>
>     var config = await sut.LoadAsync();
>
>     config.Tracking.RetentionDays.Should().Be(45);
>     config.Tracking.Enabled.Should().BeTrue();   // default preserved
>     config.Display.Colors.Should().BeTrue();     // default preserved
> }
> ```

### Analyzer Pitfalls (from prior stories)

- **CA1305**: Do NOT use `string.Format(...)` or `AppendLine($"...")` without `CultureInfo.InvariantCulture`; use `JsonSerializer` directly instead of any custom string formatting
- **CA1050/S3903**: `DtkConfigJsonContext` MUST be in a named namespace — `namespace DotnetTokenKiller.Infrastructure.Configuration;` — file-scoped
- **RCS1118**: Any repeated string path constants should be `const` or `static readonly`
- **CA1852**: All non-inherited classes must be `sealed` — `JsonConfigProvider` is `sealed`
- **CA2007**: No `ConfigureAwait` required (single-threaded CLI context — suppressed project-wide)
- **CA1062**: Public method argument validation not required (suppressed project-wide)
- **TreatWarningsAsErrors=true**: Every analyzer warning is a build failure — zero tolerance
- **GC.SuppressFinalize(this)**: Required in `Dispose()` if class doesn't define a finalizer (Dispose pattern completeness, avoids CA1816)

### Project Structure Notes

New files to create:

```sh
src/DotnetTokenKiller.Infrastructure/
  Configuration/
    DtkConfigJsonContext.cs     ← new (source-generated JSON context)
    JsonConfigProvider.cs       ← new (replaces NullConfigProvider in DI)
    NullConfigProvider.cs       ← keep (not deleted — NullConfigProvider may still be useful for tests)

tests/DotnetTokenKiller.Infrastructure.Tests/
  Configuration/
    JsonConfigProviderTests.cs  ← new
```

Files to modify:

```sh
src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs  ← change NullConfigProvider → JsonConfigProvider
```

No new NuGet packages. `System.Text.Json` is BCL (built-in). No changes to `Directory.Packages.props`.

**Namespace alignment:**

| File path | Namespace |
|---|---|
| `src/DotnetTokenKiller.Infrastructure/Configuration/DtkConfigJsonContext.cs` | `DotnetTokenKiller.Infrastructure.Configuration` |
| `src/DotnetTokenKiller.Infrastructure/Configuration/JsonConfigProvider.cs` | `DotnetTokenKiller.Infrastructure.Configuration` |
| `tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs` | `DotnetTokenKiller.Infrastructure.Tests.Configuration` |

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 6.1] — Acceptance criteria and story definition
- [Source: _bmad-output/project-context.md#Technology Stack] — `System.Text.Json` source generators required for AOT
- [Source: _bmad-output/project-context.md#Clean Architecture — Dependency Rules] — Infrastructure references Domain only
- [Source: _bmad-output/project-context.md#Testing Rules — Infrastructure.Tests] — Use `Path.GetTempPath()`, always clean up in Dispose
- [Source: _bmad-output/project-context.md#Critical Don't-Miss Rules — AOT/Trim Safety] — `[JsonSerializable]` on context required
- [Source: _bmad-output/project-context.md#Platform Data Paths] — Config: `%APPDATA%/dtk/config.json` (Windows) / `~/.config/dtk/config.json` (Linux/macOS)
- [Source: src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs] — `DtkConfig`, `TrackingConfig`, `DisplayConfig`, `TeeConfig` record shapes
- [Source: src/DotnetTokenKiller.Domain/Configuration/IConfigProvider.cs] — `LoadAsync`/`SaveAsync` contract
- [Source: src/DotnetTokenKiller.Infrastructure/Configuration/NullConfigProvider.cs] — stub to replace in DI
- [Source: src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs] — registration location to update
- [Source: tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs] — test patterns: `IAsyncDisposable`, test class structure

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- CA1063/S3881 fired on `JsonConfigProviderTests` — fixed by marking class `sealed` (consistent with dispose pattern rules)
- Blueprint showed `internal` constructor on `JsonConfigProvider`; changed to `public` — no `[InternalsVisibleTo]` in project, and `SqliteTracker` follows same public constructor pattern

### Completion Notes List

- Implemented `DtkConfigJsonContext` using source-generated `JsonSerializerContext` with `PropertyNameCaseInsensitive = true` (AOT/trim-safe, no reflection)
- Implemented `JsonConfigProvider` with fail-safe `LoadAsync` (catches all exceptions → returns defaults), `Merge` static method handles null sub-records via nullable widening pattern (`TrackingConfig? tracking = loaded.Tracking`)
- Updated DI registration: `NullConfigProvider` → `JsonConfigProvider`; `ITeeService` remains `NullTeeService` (owned by story 6-2)
- Added 6 tests covering: no-file defaults, invalid JSON defaults, partial sub-record merging (top-level and intra-sub-record), round-trip save/load, directory auto-creation
- All tests pass; 0 build warnings; format check passes

### File List

- src/DotnetTokenKiller.Infrastructure/Configuration/DtkConfigJsonContext.cs (new)
- src/DotnetTokenKiller.Infrastructure/Configuration/JsonConfigProvider.cs (new)
- src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs (modified)
- tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs (new)

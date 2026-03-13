# Story 6.2: Implement Tee Output Recovery

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer whose command failed,
I want the full raw output automatically saved to a file with a hint appended to the filtered output,
so that I can read the complete output without re-running the command.

## Acceptance Criteria

1. Given tee mode is "failures" (default) and a command exits with a non-zero exit code and raw output is ≥500 characters, when `FileTeeService.TeeAndHintAsync` is called, then the raw output is saved to a timestamped file `{tee_dir}/{unix_timestamp}_{command_slug}.log` and the method returns a hint string `[full output: /path/to/file.log]` and the hint is appended to the filtered output printed by `FilteredRunUseCase`.

2. Given tee mode is "failures" and a command exits with exit code 0, when `TeeAndHintAsync` is called, then no file is written and `null` is returned.

3. Given tee mode is "never", when `TeeAndHintAsync` is called regardless of exit code, then no file is written and `null` is returned.

4. Given tee mode is "always" and a command exits with exit code 0, when `TeeAndHintAsync` is called, then the raw output is saved to a file and the hint is returned.

5. Given raw output is less than 500 characters, when `TeeAndHintAsync` is called, then no file is written and `null` is returned (output too small to be worth saving).

6. Given the tee directory already contains `maxFiles` (default 20) files, when a new file is written, then the oldest file(s) are deleted first to stay within the limit (oldest = lowest unix_timestamp filename prefix).

7. Given the raw output exceeds `maxFileSize` (default 1 MB = 1,048,576 bytes), when the file is written, then the content is truncated to `maxFileSize` characters before writing.

8. Given any tee I/O error occurs (permission denied, disk full), when `TeeAndHintAsync` is called, then the error is swallowed silently and `null` is returned — the command output is never affected.

9. Command slugs are sanitized: all characters except `[a-zA-Z0-9-]` are replaced with `-`; consecutive hyphens are collapsed to one; leading/trailing hyphens are trimmed.

10. The tee directory defaults to `%LOCALAPPDATA%/dtk/tee/` (Windows) or `~/.local/share/dtk/tee/` (Linux/macOS); overridable via `DTK_TEE_DIR` env var (highest priority) or `TeeConfig.Directory` in config.

11. `FileTeeService` is registered in DI replacing the `NullTeeService` stub.

12. Infrastructure tests use a temp directory; always clean up in `Dispose`.

13. All tests pass. `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] **Task 1**: Create `FileTeeService` (AC: #1–#10)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs`
  - [x] Namespace: `DotnetTokenKiller.Infrastructure.Tee`
  - [x] Public constructor: `FileTeeService(IConfigProvider configProvider)`
  - [x] Public constructor for testability: `FileTeeService(IConfigProvider configProvider, string? teeDirOverride)` — follows `JsonConfigProvider(string configPath)` testability pattern
  - [x] `private static string GetDefaultTeeDir()` using `Environment.SpecialFolder.LocalApplicationData`
  - [x] `private static string GetTeeDir(TeeConfig config, string? override)` — priority: override → env var `DTK_TEE_DIR` → `config.Directory` → platform default
  - [x] `private static string SanitizeSlug(string slug)` using `[GeneratedRegex]` (MANDATORY — project rule: no `new Regex(...)`)
  - [x] Mode check: compare `config.Tee.Mode` case-insensitively to `"failures"`, `"always"`, `"never"`
  - [x] Size guard: return null if `rawOutput.Length < 500`
  - [x] Rotation: `Directory.GetFiles(teeDir)` → sort ascending by name (timestamp prefix ensures correct order) → delete oldest until count < maxFiles
  - [x] Truncation: `rawOutput[..(int)Math.Min(rawOutput.Length, (int)Math.Min(config.Tee.MaxFileSizeBytes, int.MaxValue))]`
  - [x] File path: `{teeDir}/{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{sanitized}.log`
  - [x] Hint: `$"[full output: {filePath}]"`
  - [x] All I/O wrapped in try/catch → return null on any exception

- [x] **Task 2**: Update `DependencyInjection.cs` to register `FileTeeService` (AC: #11)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs`
  - [x] Replace: `services.AddSingleton<ITeeService, NullTeeService>()`
  - [x] With: `services.AddSingleton<ITeeService, FileTeeService>()`
  - [x] `FileTeeService` receives `IConfigProvider` via DI — no factory lambda needed (both are singletons already registered)

- [x] **Task 3**: Write Infrastructure tests (AC: #12, #13)
  - [x] File: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs`
  - [x] Namespace: `DotnetTokenKiller.Infrastructure.Tests.Tee`
  - [x] Implement `IDisposable` — clean up temp directory in `Dispose()`; call `GC.SuppressFinalize(this)` (avoids CA1816)
  - [x] Use nested `FakeConfigProvider(DtkConfig config)` helper class implementing `IConfigProvider` — no NSubstitute needed (NSubstitute is NOT referenced in this test csproj)
  - [x] Use `FileTeeService(configProvider, _tempDir)` public constructor to control tee dir
  - [x] Tests:
    - `TeeAndHintAsync_WritesFileAndReturnsHint_WhenFailuresMode_NonZeroExit`
    - `TeeAndHintAsync_ReturnsNull_WhenFailuresMode_ZeroExit`
    - `TeeAndHintAsync_ReturnsNull_WhenNeverMode`
    - `TeeAndHintAsync_WritesFile_WhenAlwaysMode_ZeroExit`
    - `TeeAndHintAsync_ReturnsNull_WhenOutputTooSmall`
    - `TeeAndHintAsync_DeletesOldestFiles_WhenMaxFilesExceeded`
    - `TeeAndHintAsync_TruncatesContent_WhenOutputExceedsMaxSize`
    - `TeeAndHintAsync_SanitizesSlug_InFileName`

- [x] **Task 4**: Build and verify (AC: #13)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests green (199 total, 8 new)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Pre-Condition / Current State

| File | Current State |
|---|---|
| `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs` | **Complete** — `Task<string?> TeeAndHintAsync(string rawOutput, string commandSlug, int exitCode, CancellationToken)` — DO NOT MODIFY |
| `src/DotnetTokenKiller.Infrastructure/Tee/NullTeeService.cs` | **Stub** — always returns null — KEEP (do not delete, useful in tests) |
| `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` | **Needs update** — change `NullTeeService` → `FileTeeService` |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | **Complete** — already calls `teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode)` and prints the hint — NO CHANGES NEEDED |
| `tests/DotnetTokenKiller.Infrastructure.Tests/` | **Existing** — only `Tracking/` and `Configuration/` subfolders present; add `Tee/` subfolder |

### Architecture Compliance — CRITICAL

- `DotnetTokenKiller.Infrastructure.Tee` → references `DotnetTokenKiller.Domain.Tee` (interface) + `DotnetTokenKiller.Domain.Configuration` (for `IConfigProvider`, `TeeConfig`) only
- **No new NuGet packages needed** — all file I/O uses BCL (`System.IO`, `System.Text.RegularExpressions`)
- `FileTeeService` lives in `Infrastructure.Tee` — implements `ITeeService` from `Domain.Tee`
- Do NOT reference Application or Cli layers from Infrastructure
- `NullTeeService` stays — do not delete it; just change the DI registration

### FilteredRunUseCase Integration — ALREADY DONE

`FilteredRunUseCase` (Application layer) already:

```csharp
// Already in FilteredRunUseCase.cs — DO NOT MODIFY
var commandSlug = args.Count > 0 ? args[0] : command;
var hint = await teeService.TeeAndHintAsync(stripped, commandSlug, result.ExitCode, cancellationToken);
if (hint is not null)
    Console.WriteLine(hint);
```

The `commandSlug` passed is the first arg (e.g., `"build"`, `"test"`, `"restore"`). This is already sanitized-friendly. `FileTeeService.SanitizeSlug` will further clean it.

### DtkConfig / TeeConfig Shape (Domain — DO NOT MODIFY)

```csharp
// src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs
public sealed record TeeConfig(
    string Mode = "failures",    // "failures" | "always" | "never"
    string? Directory = null,    // null → use platform default
    int MaxFiles = 20,
    long MaxFileSizeBytes = 1_048_576L);  // 1 MB
```

### Implementation Blueprint: `FileTeeService`

```csharp
// src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Infrastructure.Tee;

public sealed class FileTeeService : ITeeService
{
    private readonly IConfigProvider _configProvider;
    private readonly string? _teeDirOverride;

    public FileTeeService(IConfigProvider configProvider)
        : this(configProvider, null) { }

    // Internal constructor for testability — pass temp dir to avoid touching real FS
    internal FileTeeService(IConfigProvider configProvider, string? teeDirOverride)
    {
        _configProvider = configProvider;
        _teeDirOverride = teeDirOverride;
    }

    public async Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configProvider.LoadAsync(cancellationToken);
            var teeConfig = config.Tee;

            // Mode check
            var mode = teeConfig.Mode ?? "failures";
            var shouldWrite = mode.Equals("always", StringComparison.OrdinalIgnoreCase)
                || (mode.Equals("failures", StringComparison.OrdinalIgnoreCase) && exitCode != 0);

            if (!shouldWrite)
                return null;

            // Size guard
            if (rawOutput.Length < 500)
                return null;

            var teeDir = GetTeeDir(teeConfig, _teeDirOverride);
            Directory.CreateDirectory(teeDir);

            // Rotate: delete oldest files if at/over limit
            RotateFiles(teeDir, teeConfig.MaxFiles);

            // Truncate content
            var maxChars = (int)Math.Min(teeConfig.MaxFileSizeBytes, int.MaxValue);
            var content = rawOutput.Length > maxChars ? rawOutput[..maxChars] : rawOutput;

            // Write file
            var slug = SanitizeSlug(commandSlug);
            var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{slug}.log";
            var filePath = Path.Combine(teeDir, fileName);
            await File.WriteAllTextAsync(filePath, content, cancellationToken);

            return $"[full output: {filePath}]";
        }
        catch
        {
            // Intentional: tee errors must never surface to the user
            return null;
        }
    }

    private static void RotateFiles(string teeDir, int maxFiles)
    {
        if (maxFiles <= 0)
            return;

        var files = Directory.GetFiles(teeDir).OrderBy(f => f).ToList();
        var excess = files.Count - maxFiles + 1; // +1 to make room for new file
        for (var i = 0; i < excess; i++)
            File.Delete(files[i]);
    }

    private static string GetTeeDir(TeeConfig config, string? teeDirOverride)
    {
        if (teeDirOverride is not null)
            return teeDirOverride;

        var envVar = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        if (!string.IsNullOrEmpty(envVar))
            return envVar;

        if (!string.IsNullOrEmpty(config.Directory))
            return config.Directory;

        return GetDefaultTeeDir();
    }

    private static string GetDefaultTeeDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "dtk", "tee");
    }

    private static string SanitizeSlug(string slug)
    {
        var safe = NonSafeCharRegex().Replace(slug, "-");
        safe = CollapseHyphensRegex().Replace(safe, "-");
        return safe.Trim('-');
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial Regex NonSafeCharRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex CollapseHyphensRegex();
}
```

> **⚠️ CRITICAL — `[GeneratedRegex]` requires `partial` class:** The class must be `partial` for the source generator to work. `public sealed partial class FileTeeService` — add `partial` to the class declaration.
> **⚠️ `OrderBy(f => f)` for rotation:** Since filenames start with unix timestamps (e.g., `1741860000_build.log`), lexicographic sort = chronological sort. This is correct.
> **⚠️ `maxChars` cast:** `teeConfig.MaxFileSizeBytes` is `long`. `int.MaxValue` = 2,147,483,647. Default 1 MB = 1,048,576 — well within range. The `Math.Min(..., int.MaxValue)` pattern prevents overflow on pathological configs.
> **⚠️ Config load per call:** `LoadAsync` is called on every `TeeAndHintAsync`. For a CLI tool (single command), this means one extra disk read per command, which is acceptable. Do NOT cache in a field — it would require locking for thread safety and adds complexity for no benefit in a CLI context.

### Updated DependencyInjection.cs

```csharp
// Change ONLY this line in src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs:
//   BEFORE: services.AddSingleton<ITeeService, NullTeeService>();
//   AFTER:
services.AddSingleton<ITeeService, FileTeeService>();
```

No other changes to DI. `FileTeeService` receives `IConfigProvider` via constructor injection automatically because both are registered as singletons.

### Test Blueprint: `FileTeeServiceTests.cs`

```csharp
// tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

public sealed class FileTeeServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-tee-test-{Guid.NewGuid()}");

    private FileTeeService CreateSut(TeeConfig? teeConfig = null)
    {
        var config = DtkConfig.Default with { Tee = teeConfig ?? new TeeConfig() };
        return new FileTeeService(new FakeConfigProvider(config), _tempDir);
    }

    private static string LargeOutput(int length = 600) => new('x', length);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TeeAndHintAsync_WritesFileAndReturnsHint_WhenFailuresMode_NonZeroExit()
    {
        var sut = CreateSut(new TeeConfig(Mode: "failures"));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", exitCode: 1);

        hint.Should().StartWith("[full output: ").And.EndWith(".log]");
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenFailuresMode_ZeroExit()
    {
        var sut = CreateSut(new TeeConfig(Mode: "failures"));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", exitCode: 0);

        hint.Should().BeNull();
        Directory.Exists(_tempDir).Should().BeFalse();
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenNeverMode()
    {
        var sut = CreateSut(new TeeConfig(Mode: "never"));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", exitCode: 1);

        hint.Should().BeNull();
    }

    [Fact]
    public async Task TeeAndHintAsync_WritesFile_WhenAlwaysMode_ZeroExit()
    {
        var sut = CreateSut(new TeeConfig(Mode: "always"));

        var hint = await sut.TeeAndHintAsync(LargeOutput(), "build", exitCode: 0);

        hint.Should().NotBeNull();
        Directory.GetFiles(_tempDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task TeeAndHintAsync_ReturnsNull_WhenOutputTooSmall()
    {
        var sut = CreateSut(new TeeConfig(Mode: "failures"));

        var hint = await sut.TeeAndHintAsync(new string('x', 499), "build", exitCode: 1);

        hint.Should().BeNull();
    }

    [Fact]
    public async Task TeeAndHintAsync_DeletesOldestFiles_WhenMaxFilesExceeded()
    {
        // Pre-populate tee dir with maxFiles existing files
        Directory.CreateDirectory(_tempDir);
        for (var i = 0; i < 3; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"{i:D10}_old.log"), "old");

        var sut = CreateSut(new TeeConfig(Mode: "always", MaxFiles: 3));

        await sut.TeeAndHintAsync(LargeOutput(), "build", exitCode: 0);

        Directory.GetFiles(_tempDir).Should().HaveCount(3); // stays at maxFiles
    }

    [Fact]
    public async Task TeeAndHintAsync_TruncatesContent_WhenOutputExceedsMaxSize()
    {
        var maxBytes = 100L;
        var sut = CreateSut(new TeeConfig(Mode: "always", MaxFileSizeBytes: maxBytes));

        await sut.TeeAndHintAsync(LargeOutput(600), "build", exitCode: 0);

        var file = Directory.GetFiles(_tempDir).Single();
        var content = await File.ReadAllTextAsync(file);
        content.Length.Should().Be(100);
    }

    // Nested fake — avoids NSubstitute dependency (not referenced in this test csproj)
    private sealed class FakeConfigProvider(DtkConfig config) : IConfigProvider
    {
        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(config);

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
```

> **⚠️ `FakeConfigProvider` uses primary constructor** — `private sealed class FakeConfigProvider(DtkConfig config)` — primary constructors are accepted per project style; the parameter `config` is captured directly.
> **⚠️ DO NOT add NSubstitute to Infrastructure.Tests.csproj** — it's in `Directory.Packages.props` but not referenced in this project. The `FakeConfigProvider` pattern avoids the need.
> **⚠️ Rotation test pre-populates with timestamp-prefixed names** — `$"{i:D10}_old.log"` gives `"0000000000_old.log"` etc., which sort before any real timestamp (current unix timestamp ~1.7B). This ensures the pre-populated files are deleted as "oldest".

### Analyzer Pitfalls (from prior stories)

- **CA1050/S3903**: `FileTeeService` MUST be in named namespace `DotnetTokenKiller.Infrastructure.Tee`
- **CA1852**: `FileTeeService` must be `sealed` (required for non-inherited concrete classes) — AND must be `partial` (required for `[GeneratedRegex]`) → `public sealed partial class FileTeeService`
- **RCS1118**: repeated string literals `"failures"`, `"always"`, `"never"` → make them `private const string` fields
- **CA2007**: No `ConfigureAwait` needed (CLI, suppressed project-wide)
- **CA1062**: No null guards on public method params (suppressed project-wide)
- **TreatWarningsAsErrors=true**: Zero tolerance — every warning = build failure
- **GC.SuppressFinalize(this)**: Required in test class `Dispose()` (CA1816)
- **`[GeneratedRegex]` class must be `partial`**: Add `partial` keyword to the class — forgetting this is a common LLM mistake
- **Async method naming**: `TeeAndHintAsync` already ends in `Async` ✓; helper private methods that are not async should NOT have `Async` suffix

### Project Structure Notes

New files to create:

```sh
src/DotnetTokenKiller.Infrastructure/
  Tee/
    FileTeeService.cs     ← new (replaces NullTeeService in DI)
    NullTeeService.cs     ← keep (do not delete)

tests/DotnetTokenKiller.Infrastructure.Tests/
  Tee/
    FileTeeServiceTests.cs  ← new
```

Files to modify:

```sh
src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs  ← change NullTeeService → FileTeeService
```

No new NuGet packages. No changes to `Directory.Packages.props`. No changes to any `.csproj` file.

**Namespace alignment:**

| File path | Namespace |
|---|---|
| `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` | `DotnetTokenKiller.Infrastructure.Tee` |
| `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs` | `DotnetTokenKiller.Infrastructure.Tests.Tee` |

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 6.2] — Acceptance criteria and story definition
- [Source: _bmad-output/project-context.md#Platform Data Paths] — Tee: `%LOCALAPPDATA%/dtk/tee/` (Windows) / `~/.local/share/dtk/tee/` (Linux/macOS)
- [Source: _bmad-output/project-context.md#Language-Specific Rules] — `[GeneratedRegex]` mandatory; no `new Regex(...)`; sealed + partial class pattern
- [Source: _bmad-output/project-context.md#Clean Architecture — Dependency Rules] — Infrastructure references Domain only
- [Source: _bmad-output/project-context.md#Testing Rules — Infrastructure.Tests] — temp dir, IDisposable cleanup
- [Source: _bmad-output/project-context.md#Tracking & Tee Anti-Patterns] — tee errors must never surface
- [Source: src/DotnetTokenKiller.Domain/Tee/ITeeService.cs] — `ITeeService` contract: `Task<string?> TeeAndHintAsync(...)`
- [Source: src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs] — `TeeConfig` shape (Mode, Directory, MaxFiles, MaxFileSizeBytes)
- [Source: src/DotnetTokenKiller.Infrastructure/Tee/NullTeeService.cs] — stub to keep (not delete)
- [Source: src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs] — registration location: `services.AddSingleton<ITeeService, NullTeeService>()` → change to `FileTeeService`
- [Source: src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs] — already integrates tee; NO changes needed
- [Source: _bmad-output/implementation-artifacts/6-1-implement-json-configuration.md] — test patterns: `IDisposable`, temp dir, `GC.SuppressFinalize`, `FakeConfigProvider` approach
- [Source: tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs] — `internal` constructor testability pattern, `sealed` test class, `IDisposable` cleanup

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Analyzer pitfall: `NeverMode` const was declared but never used (S1144/CA1823) — removed
- Analyzer pitfall: RCS1181 — inline `//` comment on constructor → converted to `///` XML doc with `<param>` elements (RCS1141)
- Analyzer pitfall: RCS1077 — `OrderBy(f => f)` replaced with `.Order()` (.NET 7+)
- Deviation from story blueprint: `internal` constructor changed to `public` — project uses public overloads for testability (see `JsonConfigProvider(string configPath)`), no `InternalsVisibleTo` attribute exists

### Completion Notes List

- Created `FileTeeService` implementing `ITeeService` with full mode/size/rotation/truncation/slug-sanitization logic
- Registered `FileTeeService` in DI replacing the `NullTeeService` stub
- 8 new xUnit tests cover all ACs including slug sanitization (AC #9); all 199 tests pass; build 0 errors 0 warnings; format verified
- Code review fix: DI registration was missing `FileTeeService` — corrected `NullTeeService` → `FileTeeService`
- Code review fix: Added `TeeAndHintAsync_SanitizesSlug_InFileName` test to cover AC #9

### File List

- `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` (new)
- `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` (modified)
- `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs` (new)
- `_bmad-output/implementation-artifacts/6-2-implement-tee-output-recovery.md` (modified)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified)

# Story 4.4: Implement dotnet format Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet format`,
I want format output showing only the count of changed files and duration (or files needing changes in check mode),
So that I save 70–80% of tokens while knowing exactly what was formatted or needs formatting.

## Acceptance Criteria

1. **Fix-mode compact**: When `DotnetFormatFilter.Apply(rawOutput)` is called with a fix-mode fixture (files were formatted), the output is `✓ dotnet format (N files, X.XXs)\n`.
2. **Token savings**: Token savings is ≥70% on the fix-mode fixture.
3. **Check-mode files listed**: When `Apply(rawOutput)` is called with check-mode output (`--verify-no-changes`) containing warning lines, the output begins with `dotnet format: N file(s) need formatting\n` and each file is listed on its own line shortened to project-relative path.
4. **Check-mode truncation**: If more than 20 files need formatting, only 20 are shown followed by `+N more\n`.
5. **Check-mode no changes**: When `Apply(rawOutput)` is called with check-mode output where no warning lines exist, the output is `✓ dotnet format (no changes)\n`.
6. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
7. **All regex patterns use `[GeneratedRegex]`**: No `new Regex(...)` at runtime.
8. **`DotnetFormatCommand` wired**: `DotnetFormatCommand` injects `FilteredRunUseCase filteredRun` and `DotnetFormatFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
9. **DI registration**: `DotnetFormatFilter` is registered as a singleton in `AddApplication()`.
10. **Snapshot test**: A Verify.Xunit snapshot test exists for the fix-mode fixture in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
11. **Fixture file**: `dotnet_format_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
12. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions).
13. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #11)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_format_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**/*.txt` already exists in the `.csproj` — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetFormatFilter` (AC: #1, #2, #3, #4, #5, #6, #7)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs`
  - [x] `public sealed partial class DotnetFormatFilter(string? rootPath = null) : IOutputFilter` (needs `partial` for `[GeneratedRegex]`, needs `rootPath` for path shortening in check mode)
  - [x] Implement `Apply(string rawOutput)` — see "Implementation" section below
  - [x] Use `[GeneratedRegex]` for `FormatCompletePattern`, `FormattedFilePattern`, `WarningFilePattern`
  - [x] Use `string.Create(CultureInfo.InvariantCulture, $"...")` for count-based interpolations (CA1305)

- [x] Task 3: Register `DotnetFormatFilter` in DI (AC: #9)
  - [x] Add `services.AddSingleton<DotnetFormatFilter>();` after `DotnetEfFilter` in `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetFormatCommand` to use `FilteredRunUseCase` (AC: #8)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetFormatFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import

- [x] Task 5: Write filter tests (AC: #1, #2, #3, #4, #5, #6, #10)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetFormatFilterTests.cs`
  - [x] Snapshot test for fix-mode fixture (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: ≥70%
  - [x] Noise line theory tests: verify restore/MSBuild lines absent from fixture output
  - [x] Count assertion: fixture output contains "3 files"
  - [x] Inline check-mode test: warning lines → correct header + file list
  - [x] Inline check-mode no changes test: no warnings, no formatted → `✓ dotnet format (no changes)`
  - [x] Inline truncation test: >20 warning files → only 20 shown + `+N more`
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #10)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetFormatFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` — should contain `✓ dotnet format (3 files, 0.21s)`
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #12, #13)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (baseline 145 + ~12 new)
  - [x] `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0 (verified through integrated DTK)

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3, 4.1, 4.2, 4.3)

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
| `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs` | Complete (story 4.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetFormatFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs` | Complete (story 4.1) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRunFilterTests.cs` | Complete (story 4.2) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs` | Complete (story 4.3) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_format_raw.txt` here |

**Test count baseline**: 145 tests total (after stories 4.1 + 4.2 + 4.3). All must continue to pass.

**`DotnetFormatCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it with `FilteredRunUseCase.RunAsync`. Same rewiring pattern as stories 4.1 (clean), 4.2 (run), and 4.3 (ef).

### Architecture Constraints (CRITICAL)

- `DotnetFormatFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`) + `Helpers` namespace
- Filter has a `rootPath` constructor parameter (like `DotnetBuildFilter`) for path shortening in check mode
- Default `rootPath` is `null` → resolves to `Environment.CurrentDirectory` in constructor
- Filter MUST be `stateless` except for the `_rootPath` field (immutable after construction)
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` because it uses `[GeneratedRegex]` → `sealed partial class`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`
- `using System.Text.RegularExpressions;` required for `[GeneratedRegex]` / `Regex`
- `using System.Globalization;` required for `CultureInfo.InvariantCulture` (CA1305)
- `using System.Text;` required for `StringBuilder` (used for check-mode file listing)

### DI Registration: `DotnetFormatFilter` Has a Default-Null Constructor Parameter

`DotnetFormatFilter(string? rootPath = null)` — the default parameter means DI can resolve it without a factory lambda:

```csharp
services.AddSingleton<DotnetFormatFilter>();
```

At runtime, DI passes no `rootPath` argument → defaults to `null` → filter uses `Environment.CurrentDirectory`.

**This is the same pattern as `DotnetBuildFilter`** — which also registers as `services.AddSingleton<DotnetBuildFilter>();` despite having a constructor parameter.

### Filter Design: Output Type Detection

`dotnet format` always outputs a restore preamble, then per-file action lines, then "Format complete in X.XXs." The filter detects mode from line patterns:

| Detected Pattern | Mode | Output |
|---|---|---|
| Lines matching `formatted.` suffix | Fix mode (files changed) | `✓ dotnet format (N files, X.XXs)\n` |
| Lines matching ` - warning ` pattern | Check mode with issues | `dotnet format: N file(s) need formatting\n` + file list |
| Neither pattern found | Check mode, no issues | `✓ dotnet format (no changes)\n` |

**Key insight**: The filter detects duration from "Format complete in X.XXs." — a separate regex. Duration is included in fix-mode output but not in check-mode or no-changes output.

**File deduplication**: In check mode, `dotnet format` may emit the same file multiple times for different warnings. Files are deduplicated (first occurrence wins) while preserving order using a `HashSet<string>` + `List<string>` pair.

**Path shortening**: In check mode, file paths are shortened relative to `_rootPath` using `TextHelpers.ShortenPath`. In fix mode, only the count is shown — no path shortening needed.

### Implementation: `DotnetFormatFilter`

```csharp
namespace DotnetTokenKiller.Application.Filters;

using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class DotnetFormatFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFilesShown = 20;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var formattedCount = 0;
        var warningFiles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duration = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var durationMatch = FormatCompletePattern().Match(line);
            if (durationMatch.Success)
            {
                duration = durationMatch.Groups["duration"].Value;
                continue;
            }

            if (FormattedFilePattern().IsMatch(line))
            {
                formattedCount++;
                continue;
            }

            var warningMatch = WarningFilePattern().Match(line);
            if (warningMatch.Success)
            {
                var path = warningMatch.Groups["path"].Value.Trim();
                if (seen.Add(path))
                    warningFiles.Add(path);
            }
        }

        if (warningFiles.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet format: {warningFiles.Count} file{(warningFiles.Count == 1 ? "" : "s")} need formatting");
            var shown = warningFiles.Count > MaxFilesShown ? warningFiles.GetRange(0, MaxFilesShown) : warningFiles;
            foreach (var file in shown)
                sb.AppendLine(TextHelpers.ShortenPath(file, _rootPath));
            if (warningFiles.Count > MaxFilesShown)
                sb.AppendLine(CultureInfo.InvariantCulture, $"+{warningFiles.Count - MaxFilesShown} more");
            return sb.ToString();
        }

        if (formattedCount > 0)
        {
            var ctx = BuildContext(formattedCount, duration);
            return $"✓ dotnet format{ctx}\n";
        }

        return "✓ dotnet format (no changes)\n";
    }

    private static string BuildContext(int fileCount, string duration)
    {
        var fileStr = string.Create(CultureInfo.InvariantCulture, $"{fileCount} file{(fileCount == 1 ? "" : "s")}");
        return string.IsNullOrEmpty(duration)
            ? $" ({fileStr})"
            : $" ({fileStr}, {duration})";
    }

    // Matches: "Format complete in 0.21s."
    [GeneratedRegex(@"Format complete in (?<duration>\d+\.\d+s)\.")]
    private static partial Regex FormatCompletePattern();

    // Matches fix-mode lines: "/path/to/file.cs formatted."
    [GeneratedRegex(@"formatted\.\s*$")]
    private static partial Regex FormattedFilePattern();

    // Matches check-mode warning lines: "/path/to/file.cs - warning IDE0055: ..."
    [GeneratedRegex(@"^(?<path>.+?)\s+-\s+warning\s+")]
    private static partial Regex WarningFilePattern();
}
```

**Key design decisions:**

- **`rootPath` constructor parameter with default `null`**: Same pattern as `DotnetBuildFilter`. DI registers without factory; tests pass explicit path to control snapshot content.
- **`HashSet<string>` + `List<string>` for deduplication**: Preserves order while deduplicating repeated file warnings.
- **`warningFiles.GetRange(0, MaxFilesShown)`**: Avoids LINQ for truncation — matches style of existing filters.
- **`string.Create(CultureInfo.InvariantCulture, $"...")`**: Satisfies CA1305 for integer interpolations.
- **`sb.AppendLine(CultureInfo.InvariantCulture, $"...")`**: Satisfies CA1305 for `StringBuilder` appends with counts.
- **`FormattedFilePattern` checks suffix only**: Avoids capturing a full path — we only need the count in fix mode, not the paths.
- **No `[GeneratedRegex]` on `FormattedFilePattern` with timeout**: Patterns use default timeout; line-level matching means no catastrophic backtracking risk.
- **`string.Empty` for null/empty**: Consistent with all other filters.

### Updated `DotnetFormatCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetFormatCommand(
    FilteredRunUseCase filteredRun,
    DotnetFormatFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("format").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import.

### Updated `DependencyInjection.cs` (Application project)

Add one line after `DotnetEfFilter`. No constructor args → no factory lambda:

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddSingleton<DotnetBuildFilter>();
    services.AddSingleton<DotnetTestFilter>();
    services.AddSingleton<DotnetRestoreFilter>();
    services.AddSingleton<DotnetPublishFilter>();
    services.AddSingleton<DotnetPackFilter>();
    services.AddSingleton<DotnetCleanFilter>();
    services.AddSingleton<DotnetRunFilter>();
    services.AddSingleton<DotnetEfFilter>();
    services.AddSingleton<DotnetFormatFilter>();  // NEW
    return services;
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_format_raw.txt`:

```sh
  Determining projects to restore...
  All projects are up-to-date for restore.
/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs formatted.
/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs formatted.
/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs formatted.
Format complete in 0.21s.
```

**Expected output** (for snapshot verification — restore preamble stripped, compact summary):

```sh
✓ dotnet format (3 files, 0.21s)
```

**Token savings calculation:**

- Fixture: ~280 chars (restore preamble ~65 + 3 long absolute path lines ~185 + duration line ~25)
- Output: ~30 chars (`✓ dotnet format (3 files, 0.21s)\n`)
- Savings: (280 - 30) / 280 ≈ 89% ≥ 70% ✓

### Test Implementation

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetFormatFilterTests
{
    private readonly DotnetFormatFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, because: "format filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("Determining projects")]
    [InlineData("All projects are up-to-date")]
    [InlineData("DotnetBuildFilter.cs formatted")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_SuccessFixture_ContainsFileCount()
    {
        var fixture = LoadFixture("dotnet_format_raw.txt");
        var result = _sut.Apply(fixture);
        result.Should().Contain("3 files");
    }

    [Fact]
    public void Apply_CheckModeWithWarnings_ReturnsFileList()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        const string input = """
            Determining projects to restore...
            All projects are up-to-date for restore.
            /repo/src/Foo.cs - warning IDE0055: Fix formatting.
            /repo/src/Bar.cs - warning WHITESPACE: Fix whitespace.
            Format complete in 0.15s.
            """;
        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 2 files need formatting");
        result.Should().Contain("src/Foo.cs");
        result.Should().Contain("src/Bar.cs");
    }

    [Fact]
    public void Apply_CheckModeWithWarnings_DeduplicatesFiles()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        const string input = """
            /repo/src/Foo.cs - warning IDE0055: Fix formatting.
            /repo/src/Foo.cs - warning WHITESPACE: Fix whitespace.
            /repo/src/Bar.cs - warning IDE0055: Fix formatting.
            Format complete in 0.10s.
            """;
        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 2 files need formatting");
        result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("Foo.cs"))
            .Should().Be(1, because: "duplicate files must be deduplicated");
    }

    [Fact]
    public void Apply_CheckModeTruncation_ShowsMaxFilesAndRemainder()
    {
        const string rootPath = "/repo";
        var sut = new DotnetFormatFilter(rootPath);
        var lines = Enumerable.Range(1, 25)
            .Select(i => $"/repo/src/File{i:D2}.cs - warning IDE0055: Fix formatting.")
            .ToList();
        var input = string.Join('\n', lines) + "\nFormat complete in 0.50s.\n";

        var result = sut.Apply(input);
        result.Should().StartWith("dotnet format: 25 files need formatting");
        result.Should().Contain("+5 more");
        result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("File") && l.Contains(".cs"))
            .Should().Be(20, because: "only 20 files should be shown");
    }

    [Fact]
    public void Apply_CheckModeNoChanges_ReturnsNoChanges()
    {
        const string input = """
            Determining projects to restore...
            All projects are up-to-date for restore.
            Format complete in 0.10s.
            """;
        _sut.Apply(input).Should().Be("✓ dotnet format (no changes)\n");
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
        var assembly = typeof(DotnetFormatFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

**Test count**: ~12 tests (1 snapshot + 1 savings + 3 noise theory + 1 count + 1 check-mode files + 1 dedup + 1 truncation + 1 no-changes + 1 null + 1 empty). New total: 145 + 12 = ~157 tests.

**Important notes on tests:**

- `_sut = new()` — no constructor args for default (fix-mode tests don't need path shortening)
- Check-mode tests use `new DotnetFormatFilter("/repo")` to control path shortening
- `Verify(result)` returns `Task` — test method must return `Task` (not `void`), must NOT be `async`
- Inline strings use C# raw string literals (`""" ... """`)
- `Enumerable.Range` for truncation test — implicit usings cover `System.Linq`

### Verify Snapshot Acceptance Workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetFormatFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt` — should contain `✓ dotnet format (3 files, 0.21s)`
4. Accept by renaming: `mv DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
5. Re-run tests — all pass
6. Commit the `.verified.txt` file

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5–4.3)

- **CA1852** — `DotnetFormatFilter` MUST be `sealed`
- **CA1050/RCS1110/S3903** — type MUST be in named namespace (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **`sealed partial class`** — because of `[GeneratedRegex]`; do NOT forget `partial`
- **`using System.Text.RegularExpressions;`** — NOT in implicit usings; must be explicit
- **`using System.Globalization;`** — NOT in implicit usings; must be explicit (for `CultureInfo.InvariantCulture`)
- **`using System.Text;`** — NOT in implicit usings; must be explicit (for `StringBuilder`)
- **CA1305** — `string.Create(CultureInfo.InvariantCulture, $"...")` for all interpolations containing integer counts; `sb.AppendLine(CultureInfo.InvariantCulture, $"...")` for `StringBuilder` appends
- **CA1307/CA1309** — use `StringComparison.Ordinal` in `HashSet<string>` constructor and `StringComparer.Ordinal` where applicable
- **RCS1201** — chain consecutive `sb.AppendLine(...).AppendLine(...)` if possible (or use separate statements; both compile)
- **Import ordering**: After `dotnet format`, project usings (Helpers, Domain) come before system usings (Globalization, Text, RegularExpressions) per `.editorconfig`
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetFormatFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`List<string>` and `HashSet<string>` without explicit using**: Covered by implicit usings in .NET 10
- **`Enumerable.Range` in tests**: Covered by implicit usings; no explicit `using System.Linq;` needed in test file

### Git Context (Recent Commits)

```sh
c568abc Feat: restore, publish & pack filters (#5)
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
f1dbde2 Feat: test filters (#2)
d534cbd Feat: core & foundation (#1)
4c78002 Scaffold Clean Architecture solution
```

Current branch: `develop`. Epic 4 stories 4.1–4.3 exist on disk as untracked/modified files (in review, not yet committed via PR).

### Previous Story Intelligence (Story 4.3 — EF Filter)

Key learnings applied here:

- **`sealed partial class` pattern**: Same as stories 4.2 and 4.3; needed for `[GeneratedRegex]`
- **Constructor parameter `rootPath`**: Same as `DotnetBuildFilter`; needed for path shortening in check mode; default `null` → `Environment.CurrentDirectory`
- **`_sut = new()` in most tests**: Works because `rootPath` defaults to `null`; use `new DotnetFormatFilter("/repo")` only in path-shortening tests
- **Snapshot directory**: Configured globally in `VerifyInit.cs` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: Test method must return `Task`, must NOT be `async`
- **`dotnet format` run last**: Always run after all tests pass — catches import ordering issues
- **Test class naming**: `DotnetFormatFilterTests` in `DotnetTokenKiller.Application.Tests.Filters` namespace
- **`DotnetFormatCommand` current state**: `DotnetFormatCommand(ICommandRunner commandRunner)` → same rewiring as stories 4.1–4.3 before them
- **Fixture savings must exceed threshold**: Target ~89% >> 70%; long absolute path lines drive savings

### What This Story Does NOT Implement (Scope Guard)

- `DotnetNugetFilter` — Story 4.5
- Passthrough for unrecognized subcommands — Story 4.6
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- Any other CLI command rewiring besides `DotnetFormatCommand`

### Project Structure Notes

- All target directories already exist — no new directories required
- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetFormatFilter` singleton after `DotnetEfFilter`
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_format_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetFormatFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.4]
- [Source: _bmad-output/implementation-artifacts/4-3-implement-dotnet-ef-filter-with-tests.md] — previous story; same command rewiring pattern, same analyzer pitfalls
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs] — reference for `rootPath` constructor parameter + `sealed partial class` + `[GeneratedRegex]` + `string.Create(CultureInfo.InvariantCulture, ...)`
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs] — reference for `sealed partial class` + `[GeneratedRegex]` (stateless filter without rootPath)
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs] — reference for `sealed class` pattern (no rootPath)
- [Source: src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs] — `ShortenPath(absolutePath, rootPath)` — used in check-mode output
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs] — current state (uses `RunPassthroughAsync`)
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — current state (needs `DotnetFormatFilter` added)
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init pattern
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design] — filter constraints (stateless, sealed, non-throwing)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Fixed `RCS1124: Inline local variable` on `shown` variable in `DotnetFormatFilter` — inlined into `foreach` expression.
- Fixed `CA1307` on `string.Contains` calls in test lambdas — added `StringComparison.Ordinal`.

### Completion Notes List

- Implemented `DotnetFormatFilter` with fix-mode (compact summary), check-mode (file list with deduplication + truncation), and no-changes mode.
- All 3 `[GeneratedRegex]` patterns use source-generated regex (CA1018 / perf).
- `DotnetFormatCommand` rewired from `ICommandRunner.RunPassthroughAsync` to `FilteredRunUseCase.RunAsync` — same pattern as stories 4.1–4.3.
- 12 new tests (snapshot + savings + 3 noise theory + count + check-mode files + dedup + truncation + no-changes + null + empty). Total: 157 tests.
- Snapshot `DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt` accepted: `✓ dotnet format (3 files, 0.21s)`.
- Build: 0 errors, 0 warnings. Format: exit 0.

### File List

- `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs` (new)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (modified — added `DotnetFormatFilter` singleton)
- `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs` (modified — rewired to `FilteredRunUseCase`)
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_format_raw.txt` (new)
- `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetFormatFilterTests.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetFormatFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt` (new)

## Change Log

- 2026-03-12: Implemented `DotnetFormatFilter` with fix-mode/check-mode/no-changes output, wired `DotnetFormatCommand` to `FilteredRunUseCase`, registered filter in DI, added 12 tests (157 total). Story moved to review.

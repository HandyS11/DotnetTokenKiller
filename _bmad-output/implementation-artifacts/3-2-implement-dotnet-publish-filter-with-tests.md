# Story 3.2: Implement dotnet publish Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet publish`,
I want publish output filtered to show only the output path and any errors,
So that I save 80–85% of tokens and immediately know where the published output landed.

## Acceptance Criteria

1. **Success (published output)**: When `DotnetPublishFilter.Apply(rawOutput)` is called with a fixture containing a successful publish, the output is a single line: `✓ dotnet publish → {relative-publish-path} (N projects, X.XXs)`, where the publish path is the last `→` line ending with `/publish/` (or containing `/publish/`) shortened to project-relative using forward slashes, N is the count of `→ .dll` output lines, and X.XX is the elapsed time parsed from the `Time Elapsed ...` line. Token savings ≥80%.
2. **Success (publish path shortened)**: The publish output path is shortened from absolute to project-relative using `TextHelpers.ShortenPath` with forward slashes (e.g., `src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/`).
3. **Failure (build errors)**: When `Apply(rawOutput)` is called with a fixture containing build errors, the output uses the same diagnostic grouping format as `DotnetBuildFilter`: errors grouped by file, count header, top codes listed.
4. **Noise removal**: None of the following appear in output: "MSBuild version", "Determining projects to restore", "All projects are up-to-date for restore", individual `Restored ...` lines, "Build succeeded", "Build FAILED", `0 Warning(s)` / `0 Error(s)` count lines, "Time Elapsed" line itself, `.dll` / `.exe` redirect lines.
5. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
6. **`[GeneratedRegex]`**: All regex patterns in `DotnetPublishFilter` use `[GeneratedRegex]` attributes on `private static partial` methods; class is `partial`.
7. **`DotnetPublishCommand` wired**: `DotnetPublishCommand` injects `FilteredRunUseCase filteredRun` and `DotnetPublishFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
8. **DI registration**: `DotnetPublishFilter` is registered as a singleton in `AddApplication()`.
9. **Snapshot test**: A Verify.Xunit snapshot test exists for the success scenario in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
10. **Fixture file**: `dotnet_publish_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
11. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 84 tests).
12. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #10)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_publish_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**` already exists in the `.csproj` from story 1.5 — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetPublishFilter` (AC: #1, #2, #3, #4, #5, #6)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs`
  - [x] `public sealed partial class DotnetPublishFilter(string? rootPath = null) : IOutputFilter`
  - [x] Implement `Apply(string rawOutput)` — see "Precise Implementation" section below
  - [x] All regex patterns via `[GeneratedRegex]` on `private static partial` methods

- [x] Task 3: Register `DotnetPublishFilter` in DI (AC: #8)
  - [x] Add `services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter())` to `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetPublishCommand` to use `FilteredRunUseCase` (AC: #7)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetPublishFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection

- [x] Task 5: Write filter tests (AC: #1, #2, #4, #5, #9)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs`
  - [x] Snapshot test for success scenario (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: success ≥80%
  - [x] Noise line tests: verify none of the noise patterns appear in success output
  - [x] Error test: verify error diagnostic grouping format
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #9)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetPublishFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` for correctness
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #11, #12)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 84 + new filter tests)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — `Verify.Xunit 28.2.0` already added |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — **use as reference for diagnostic grouping and noise patterns** |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` | Complete (story 3.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetPublishFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_publish_raw.txt` here |
| `.github/workflows/quality-gate.yml` | Complete (story 1.6) |

**Test count baseline**: 84 tests (17 Domain + 65 Application + 1 Infrastructure + 1 Integration). All must continue to pass.

**`DotnetPublishCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it (same rewiring pattern as `DotnetRestoreCommand` in story 3.1).

### Architecture Constraints (CRITICAL)

- `DotnetPublishFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`)
- Helpers are in `Application.Helpers` namespace — use them: `AnsiStrip.Strip`, `TextHelpers.ShortenPath`, `TextHelpers.Truncate`
- Filter MUST be `stateless` — `_rootPath` is readonly; no mutable instance fields
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` (required for `[GeneratedRegex]` partial methods)
- All regex patterns MUST use `[GeneratedRegex]` — `new Regex(...)` at runtime is FORBIDDEN (AOT constraint)
- `[GeneratedRegex]` methods must be `private static partial Regex MethodName()`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- Path shortening uses `TextHelpers.ShortenPath(absolutePath, _rootPath)` (same pattern as all other filters)
- `using System.Text.RegularExpressions` is NOT in implicit usings — must be explicit
- `using System.Globalization` is NOT in implicit usings — must be explicit
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`

### Precise Implementation: `DotnetPublishFilter`

The publish filter reuses the same diagnostic handling as `DotnetBuildFilter` for the failure path, and adds publish-path extraction for the success path. Do NOT inherit from `DotnetBuildFilter` (it's sealed) — copy the diagnostic patterns and methods inline.

#### Algorithm

1. ANSI-strip and split by `\n`
2. For each line:
   - If it matches `PublishOutputPattern` (→ .../publish/) → capture as `publishPath` (last match wins)
   - If it matches `ProjectOutputPattern` (→ .dll or .exe) → `projectCount++`
   - If it matches `TimeElapsedPattern` → extract elapsed from `TimeSpanValuePattern`
   - If it matches `DiagnosticPattern` → add to diagnostics (deduplicated by file+line+col+code)
   - Otherwise → silently skip (noise)
3. If errors exist → output diagnostic grouping (same format as DotnetBuildFilter)
4. If no errors → `✓ dotnet publish → {shortPath} ({N} project[s], {elapsed})\n`
   - If no publish path found → `✓ dotnet publish ({N} project[s], {elapsed})\n`
   - If no elapsed → omit elapsed from context
   - If no project count and no elapsed → `✓ dotnet publish\n`

#### Class skeleton

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed partial class DotnetPublishFilter(string? rootPath = null) : IOutputFilter
{
    private const string Separator = "---";
    private const int MessageMaxLen = 120;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectCount = 0;
        var elapsed = string.Empty;
        var publishPath = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Capture publish output path (last match wins)
            var publishMatch = PublishOutputPattern().Match(line);
            if (publishMatch.Success)
            {
                publishPath = TextHelpers.ShortenPath(publishMatch.Groups["path"].Value.Trim(), _rootPath);
                continue;
            }

            // Count project compile output lines (.dll / .exe)
            if (ProjectOutputPattern().IsMatch(line))
            {
                projectCount++;
                continue;
            }

            // Parse elapsed time
            var elapsedMatch = TimeElapsedPattern().Match(line);
            if (elapsedMatch.Success)
            {
                elapsed = FormatElapsed(elapsedMatch.Value);
                continue;
            }

            // Parse diagnostic lines (errors / warnings)
            var diagMatch = DiagnosticPattern().Match(line);
            if (!diagMatch.Success)
                continue;

            var key = $"{diagMatch.Groups["file"].Value}({diagMatch.Groups["line"].Value},{diagMatch.Groups["col"].Value}):{diagMatch.Groups["code"].Value}";
            if (!seen.Add(key))
                continue;

            diagnostics.Add(new Diagnostic(
                TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), _rootPath),
                Line: diagMatch.Groups["line"].Value,
                Col: diagMatch.Groups["col"].Value,
                Level: diagMatch.Groups["level"].Value,
                Code: diagMatch.Groups["code"].Value,
                Message: TextHelpers.Truncate(diagMatch.Groups["message"].Value.Trim(), MessageMaxLen)));
        }

        var errors = diagnostics.Where(d => d.Level == "error").ToList();
        var warnings = diagnostics.Where(d => d.Level == "warning").ToList();

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet publish: {errors.Count} error{(errors.Count == 1 ? "" : "s")}, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}")
              .AppendLine(Separator);
            AppendGroupedByFile(sb, errors);
            AppendTopCodes(sb, errors);
            if (warnings.Count > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} suppressed (use -v to see)");
            return sb.ToString();
        }

        if (warnings.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet publish: 0 errors, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}{BuildContext(projectCount, elapsed, publishPath)}")
              .AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
            return sb.ToString();
        }

        var context = BuildContext(projectCount, elapsed, publishPath);
        return $"✓ dotnet publish{context}\n";
    }

    private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);

    private static string BuildContext(int projectCount, string elapsed, string publishPath)
    {
        var pathPart = string.IsNullOrEmpty(publishPath) ? string.Empty : $" → {publishPath}";
        var countPart = projectCount > 0
            ? $"{projectCount} project{(projectCount == 1 ? "" : "s")}"
            : string.Empty;
        var timePart = string.IsNullOrEmpty(elapsed) ? string.Empty : elapsed;

        var details = string.Join(", ", new[] { countPart, timePart }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(details)
            ? pathPart
            : $"{pathPart} ({details})";
    }

    private static string FormatElapsed(string timeElapsedLine)
    {
        var match = TimeSpanValuePattern().Match(timeElapsedLine);
        if (!match.Success)
            return string.Empty;

        if (TimeSpan.TryParse(match.Value, CultureInfo.InvariantCulture, out var ts))
            return $"{ts.TotalSeconds:F2}s";

        return string.Empty;
    }

    private static void AppendGroupedByCode(StringBuilder sb, List<Diagnostic> diags)
    {
        foreach (var group in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{group.Key} ({items.Count}x)");
            foreach (var d in items)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {d.File}:{d.Line} — {d.Message}");
        }
    }

    private static void AppendGroupedByFile(StringBuilder sb, List<Diagnostic> errors)
    {
        foreach (var group in errors.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{group.Key} ({items.Count} error{(items.Count == 1 ? "" : "s")})");
            foreach (var d in items)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  ({d.Line},{d.Col}) {d.Code}: {d.Message}");
        }
    }

    private static void AppendTopCodes(StringBuilder sb, List<Diagnostic> errors)
    {
        var topCodes = errors
            .GroupBy(d => d.Code)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        if (topCodes.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Top codes: {string.Join(", ", topCodes)}");
    }

    // "  MyProject -> /path/to/publish/" — captures the publish output directory
    [GeneratedRegex(@"^\s+\S+ -> (?<path>.+/publish[/\\]?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishOutputPattern();

    // "  MyProject -> /path/to/bin/Debug/net10.0/MyProject.dll"
    [GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$")]
    private static partial Regex ProjectOutputPattern();

    // Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
    [GeneratedRegex(@"^\s*(?<file>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<message>[^\[]+?)(?:\s*\[.+?\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    // Matches "Time Elapsed HH:MM:SS.ff"
    [GeneratedRegex(@"Time Elapsed \d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeElapsedPattern();

    // Extracts the HH:MM:SS.ff value from a Time Elapsed line
    [GeneratedRegex(@"\d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeSpanValuePattern();
}
```

#### Updated `DotnetPublishCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetPublishCommand(
    FilteredRunUseCase filteredRun,
    DotnetPublishFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("publish").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` and `ICommandRunner using` — `FilteredRunUseCase` holds it.

#### Updated `DependencyInjection.cs` (Application project)

Add one line — singleton registration for `DotnetPublishFilter`. Match the existing pattern:

```csharp
services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter());  // ADD THIS
```

Full file after change:

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddSingleton<DotnetBuildFilter>(_ => new DotnetBuildFilter());
        services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter());
        services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter());
        services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter());  // NEW
        return services;
    }
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_publish_raw.txt`:

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Domain -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/bin/Release/net10.0/DotnetTokenKiller.Domain.dll
  DotnetTokenKiller.Application -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/bin/Release/net10.0/DotnetTokenKiller.Application.dll
  DotnetTokenKiller.Infrastructure -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/bin/Release/net10.0/DotnetTokenKiller.Infrastructure.dll
  DotnetTokenKiller.Cli -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Release/net10.0/DotnetTokenKiller.Cli.dll
  DotnetTokenKiller.Cli -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.27
```

**Expected output** (for snapshot verification):

```sh
✓ dotnet publish → src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/ (4 projects, 3.27s)
```

- 4 `.dll` lines → N = 4
- Publish path: `/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/` → `src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/`
- Time Elapsed 00:00:03.27 → 3.27s
- Token savings: fixture ~560 chars, output ~80 chars → ~86% ≥80% ✓

### Test Implementation Patterns

#### Loading embedded fixture files (same pattern as stories 1.5, 2.1, 3.1)

```csharp
private static string LoadFixture(string resourceName)
{
    var assembly = typeof(DotnetPublishFilterTests).Assembly;
    var fullName = assembly.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(fullName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

#### Test class structure (Verify.Xunit v28 — static API)

**CRITICAL**: Do NOT use `[UsesVerify]` attribute or inherit from `VerifyBase`. Use static `Verifier.Verify()` directly (v28 requirement). The `VerifyInit.cs` already configures `UseProjectRelativeDirectory("Snapshots")` and `IgnoreStackTrace()` globally.

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetPublishFilterTests
{
    private readonly DotnetPublishFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_publish_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast80Percent()
    {
        var fixture = LoadFixture("dotnet_publish_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(80.0, because: "publish filter should achieve ≥80% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Build succeeded")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_publish_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_BuildError_ShowsErrorGroupingFormat()
    {
        const string input = """
            MSBuild version 17.11.9+a69bbaaf5 for .NET
              /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs(5,1): error CS0001: Type or namespace 'Foo' not found [/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]
            Build FAILED.
                1 Error(s)
            Time Elapsed 00:00:01.00
            """;
        var result = _sut.Apply(input);
        result.Should().StartWith("dotnet publish: 1 error");
        result.Should().Contain("CS0001");
        result.Should().Contain("Top codes:");
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
        var assembly = typeof(DotnetPublishFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

#### Verify snapshot acceptance workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetPublishFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetPublishFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt` — should contain `✓ dotnet publish → src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/ (4 projects, 3.27s)`
4. Accept by renaming: `mv *.received.txt *.verified.txt`
5. Re-run tests — all pass
6. Commit `.verified.txt` file

**File to commit in Snapshots folder**:

- `DotnetPublishFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5, 2.1, 3.1)

- **CA1852** — `DotnetPublishFilter` MUST be `sealed`
- **`[GeneratedRegex]`** — Class MUST be `partial`; methods declared as `private static partial Regex MethodName()`; missing `partial` = build error
- **CA1305** — `sb.AppendLine($"...")` with format args: use `sb.AppendLine(CultureInfo.InvariantCulture, $"...")`
- **CA1305 on TryParse** — Use `TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts)` (3-param overload)
- **RCS1201** — Chain consecutive `sb.AppendLine(...).AppendLine(...)` when writing header + separator
- **CA1050/RCS1110/S3903** — All types MUST be in named namespaces (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **RCS1118** — Repeated string literals: use `const string Separator = "---"` (already in skeleton)
- **IDE0290** — Primary constructors preferred; `DotnetPublishCommand` uses primary constructor pattern
- **using System.Text.RegularExpressions** — NOT in implicit usings; must be explicit
- **`using System.Globalization`** — NOT in implicit usings; must be explicit
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetPublishFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`Diagnostic` record**: declared as `private sealed record` inside the filter class — matches pattern from `DotnetBuildFilter` and `DotnetTestFilter`
- **`string.Join` with LINQ**: `string.Join(", ", someList)` is analyzer-safe
- **`List<T>.Where(...).ToList()`**: fine here; if analyzer suggests `FindAll`, use it
- **`using` import ordering**: After `dotnet format`, project usings come before system usings per `.editorconfig` — the skeleton above already shows correct order

### Git Context (Recent Commits)

```sh
66a43f0 Merge branch 'develop' into feat/restore-publish-pack-filters
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
6adf543 Feat: update sprint status and mark dotnet restore filter implementation as completed
e0b29c1 Feat: enhance DotnetRestoreFilter to support F# projects and improve line splitting logic
d323922 Feat: implement DotnetRestoreFilter with tests and integrate into CLI command
```

We are on branch `feat/restore-publish-pack-filters` — this branch is already set up for stories 3.1, 3.2, and 3.3.

### Key Learnings from Stories 2.1 and 3.1 (Applied Here)

- **`sealed record` inside filter**: `private sealed record Diagnostic(...)` inside the class — do the same
- **`List<T>.Where(...).ToList()` vs `FindAll`**: Use `FindAll` if the analyzer complains (RCS1201)
- **Snapshot directory**: Configured globally in `VerifyInit.cs` via `[ModuleInitializer]` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: Test method must return `Task` (not `void`) and must NOT be async — just `return Verify(result)`
- **Fixture absolute paths**: Fixtures use `/home/handys11/Dev/DotnetTokenKiller/...` absolute paths so that `TextHelpers.ShortenPath` produces reliable relative paths
- **`dotnet format` run last**: Always run `dotnet format --verify-no-changes` after all tests pass — catches import ordering issues
- **`DotnetRestoreFilter` enhanced**: Story e0b29c1 enhanced `DotnetRestoreFilter` to support F# projects (`.fsproj`) and improved line splitting — if copying patterns from restore, be aware of this
- **Duration parsing fix**: Story 992e65f fixed `DotnetTestFilter` duration parsing to support multiple time units — `DotnetPublishFilter` uses `TimeSpan.TryParse` on the `HH:MM:SS.ff` format (from "Time Elapsed" line), which is different and doesn't need this fix

### What This Story Does NOT Implement (Scope Guard)

- `DotnetPackFilter` — Story 3.3
- `DotnetCleanFilter`, `DotnetRunFilter`, etc. — Epic 4
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- Any other CLI command rewiring besides `DotnetPublishCommand`

### Project Structure Notes

- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetPublishFilter` singleton
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_publish_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetPublishFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
- No new directories to create — all target directories already exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/implementation-artifacts/3-1-implement-dotnet-restore-filter-with-tests.md] — previous story patterns and learnings
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs] — diagnostic grouping reference (copy AppendGroupedByFile, AppendTopCodes, DiagnosticPattern, TimeElapsedPattern)
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs] — general filter structure reference
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- S3358: Nested ternary in BuildContext — extracted projectSuffix variable to fix

### Completion Notes List

- Implemented DotnetPublishFilter (sealed partial, [GeneratedRegex], IOutputFilter) with success path showing publish path, project count and elapsed, and failure path with diagnostic grouping matching DotnetBuildFilter format
- Fixed S3358 analyzer error: extracted nested ternary in BuildContext to separate variable
- Wired DotnetPublishCommand to FilteredRunUseCase (replaced ICommandRunner.RunPassthroughAsync)
- Registered DotnetPublishFilter as singleton in AddApplication()
- All 93 tests pass (74 Application + 17 Domain + 1 Infrastructure + 1 Integration); 0 regressions
- dotnet build: 0 errors, 0 warnings; dotnet format --verify-no-changes: clean
- Snapshot output: ✓ dotnet publish → src/DotnetTokenKiller.Cli/bin/Release/net10.0/publish/ (4 projects, 3.27s)
- Token savings: ~86% (above ≥80% threshold)

### File List

- tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_publish_raw.txt (new)
- src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs (new)
- src/DotnetTokenKiller.Application/DependencyInjection.cs (modified)
- src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs (modified)
- tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs (new)
- tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetPublishFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt (new)

### Change Log

- 2026-03-10: Implemented DotnetPublishFilter with tests; wired DotnetPublishCommand to FilteredRunUseCase; registered singleton in DI; accepted Verify snapshot

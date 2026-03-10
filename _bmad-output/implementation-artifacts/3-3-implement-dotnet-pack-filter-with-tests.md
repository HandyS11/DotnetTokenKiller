# Story 3.3: Implement dotnet pack Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet pack`,
I want pack output filtered to show only the generated .nupkg filename,
So that I save 85–90% of tokens and immediately know what package was produced.

## Acceptance Criteria

1. **Success (.nupkg extracted)**: When `DotnetPackFilter.Apply(rawOutput)` is called with a fixture containing a successful pack, the output is a single line: `✓ dotnet pack → MyProject.1.0.0.nupkg (N projects, X.XXs)`, where the .nupkg filename is extracted from the `Successfully created package` line, N is the count of `→ .dll` or `→ .exe` output lines, and X.XX is the elapsed time parsed from the `Time Elapsed ...` line. Token savings ≥85%.
2. **Failure (build errors)**: When `Apply(rawOutput)` is called with a fixture containing build errors, the output uses the same diagnostic grouping format as `DotnetBuildFilter`: errors grouped by file, count header, top codes listed.
3. **Noise removal**: None of the following appear in output: "MSBuild version", "Determining projects to restore", "All projects are up-to-date for restore", individual `Restored ...` lines, "Build succeeded", "Build FAILED", `0 Warning(s)` / `0 Error(s)` count lines, "Time Elapsed" line itself, `.dll` / `.exe` redirect lines.
4. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
5. **`[GeneratedRegex]`**: All regex patterns in `DotnetPackFilter` use `[GeneratedRegex]` attributes on `private static partial` methods; class is `partial`.
6. **`DotnetPackCommand` wired**: `DotnetPackCommand` injects `FilteredRunUseCase filteredRun` and `DotnetPackFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
7. **DI registration**: `DotnetPackFilter` is registered as a singleton in `AddApplication()`.
8. **Snapshot test**: A Verify.Xunit snapshot test exists for the success scenario in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
9. **Fixture file**: `dotnet_pack_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
10. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 93 tests).
11. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #9)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_pack_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**` already exists in the `.csproj` from story 1.5 — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetPackFilter` (AC: #1, #2, #3, #4, #5)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs`
  - [x] `public sealed partial class DotnetPackFilter(string? rootPath = null) : IOutputFilter`
  - [x] Implement `Apply(string rawOutput)` — see "Precise Implementation" section below
  - [x] All regex patterns via `[GeneratedRegex]` on `private static partial` methods

- [x] Task 3: Register `DotnetPackFilter` in DI (AC: #7)
  - [x] Add `services.AddSingleton<DotnetPackFilter>(_ => new DotnetPackFilter())` to `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetPackCommand` to use `FilteredRunUseCase` (AC: #6)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetPackFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection

- [x] Task 5: Write filter tests (AC: #1, #3, #4, #8)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPackFilterTests.cs`
  - [x] Snapshot test for success scenario (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: success ≥85%
  - [x] Noise line tests: verify none of the noise patterns appear in success output
  - [x] Error test: verify error diagnostic grouping format
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #8)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetPackFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` for correctness
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #10, #11)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 93 + new filter tests)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1, 3.2)

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
| `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs` | Complete (story 3.2) — **closest reference for pack filter pattern** |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetPackFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs` | Complete (story 3.2) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_pack_raw.txt` here |
| `.github/workflows/quality-gate.yml` | Complete (story 1.6) |

**Test count baseline**: 93 tests (17 Domain + 74 Application + 1 Infrastructure + 1 Integration). All must continue to pass.

**`DotnetPackCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it (same rewiring pattern as `DotnetRestoreCommand` in story 3.1 and `DotnetPublishCommand` in story 3.2).

### Architecture Constraints (CRITICAL)

- `DotnetPackFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`)
- Helpers are in `Application.Helpers` namespace — use them: `AnsiStrip.Strip`, `TextHelpers.Truncate`
- Filter MUST be `stateless` — `_rootPath` is readonly; no mutable instance fields
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` (required for `[GeneratedRegex]` partial methods)
- All regex patterns MUST use `[GeneratedRegex]` — `new Regex(...)` at runtime is FORBIDDEN (AOT constraint)
- `[GeneratedRegex]` methods must be `private static partial Regex MethodName()`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- The `.nupkg` filename (not path) is extracted — use `Path.GetFileName` or a regex that captures just the filename
- `using System.Text.RegularExpressions` is NOT in implicit usings — must be explicit
- `using System.Globalization` is NOT in implicit usings — must be explicit
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`

### Precise Implementation: `DotnetPackFilter`

The pack filter is structurally identical to `DotnetPublishFilter` (story 3.2) — copy it as the starting point, then adapt:

- Replace `publishPath` extraction (regex for `→ .../publish/`) with `nupkgFilename` extraction (regex for "Successfully created package '...'")
- Replace `TextHelpers.ShortenPath` (not needed — we extract just the filename, not a path)
- Replace "dotnet publish" output labels with "dotnet pack"
- Remove the `_rootPath` field from the primary algorithm (still keep it for diagnostic path shortening)

#### Key Difference from Publish Filter

| Publish | Pack |
|---|---|
| Extracts path ending in `/publish/` | Extracts `.nupkg` filename from `Successfully created package` line |
| Uses `TextHelpers.ShortenPath` on the path | Uses `Path.GetFileName` (or regex group) on the full path |
| Output: `✓ dotnet publish → src/.../publish/` | Output: `✓ dotnet pack → MyProject.1.0.0.nupkg` |

#### Algorithm

1. ANSI-strip and split by `\n`
2. For each line:
   - If it matches `PackOutputPattern` (`Successfully created package '...'`) → capture `nupkgFilename` (last match wins — use `Path.GetFileName` on the captured path)
   - If it matches `ProjectOutputPattern` (`→ .dll` or `→ .exe`) → `projectCount++`
   - If it matches `TimeElapsedPattern` → extract elapsed from `TimeSpanValuePattern`
   - If it matches `DiagnosticPattern` → add to diagnostics (deduplicated by file+line+col+code)
   - Otherwise → silently skip (noise)
3. If errors exist → output diagnostic grouping (same format as DotnetBuildFilter / DotnetPublishFilter)
4. If no errors → `✓ dotnet pack → {nupkgFilename} ({N} project[s], {elapsed})\n`
   - If no .nupkg filename found → `✓ dotnet pack ({N} project[s], {elapsed})\n`
   - If no elapsed → omit elapsed from context
   - If no project count and no elapsed → `✓ dotnet pack\n`

#### Class Skeleton

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed partial class DotnetPackFilter(string? rootPath = null) : IOutputFilter
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
        var nupkgFilename = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Capture .nupkg filename (last match wins)
            var packMatch = PackOutputPattern().Match(line);
            if (packMatch.Success)
            {
                nupkgFilename = Path.GetFileName(packMatch.Groups["path"].Value.Trim().TrimEnd('\''));
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

        var errors = diagnostics.FindAll(d => d.Level == "error");
        var warnings = diagnostics.FindAll(d => d.Level == "warning");

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet pack: {errors.Count} error{(errors.Count == 1 ? "" : "s")}, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}")
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
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet pack: 0 errors, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}{BuildContext(projectCount, elapsed, nupkgFilename)}")
              .AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
            return sb.ToString();
        }

        var context = BuildContext(projectCount, elapsed, nupkgFilename);
        return $"✓ dotnet pack{context}\n";
    }

    private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);

    private static string BuildContext(int projectCount, string elapsed, string nupkgFilename)
    {
        var namePart = string.IsNullOrEmpty(nupkgFilename) ? string.Empty : $" → {nupkgFilename}";
        var projectSuffix = projectCount == 1 ? "" : "s";
        var countPart = projectCount > 0 ? $"{projectCount} project{projectSuffix}" : string.Empty;
        var timePart = string.IsNullOrEmpty(elapsed) ? string.Empty : elapsed;

        var details = string.Join(", ", new[] { countPart, timePart }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(details)
            ? namePart
            : $"{namePart} ({details})";
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

    // "Successfully created package '/path/to/MyProject.1.0.0.nupkg'."
    [GeneratedRegex(@"Successfully created package '(?<path>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex PackOutputPattern();

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

#### Updated `DotnetPackCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetPackCommand(
    FilteredRunUseCase filteredRun,
    DotnetPackFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("pack").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` and `ICommandRunner using` — `FilteredRunUseCase` holds it.

#### Updated `DependencyInjection.cs` (Application project)

Add one line — singleton registration for `DotnetPackFilter`. Match the existing pattern:

```csharp
services.AddSingleton<DotnetPackFilter>(_ => new DotnetPackFilter());  // ADD THIS
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
        services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter());
        services.AddSingleton<DotnetPackFilter>(_ => new DotnetPackFilter());  // NEW
        return services;
    }
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_pack_raw.txt`:

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Domain -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/bin/Release/net10.0/DotnetTokenKiller.Domain.dll
  DotnetTokenKiller.Application -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/bin/Release/net10.0/DotnetTokenKiller.Application.dll
  DotnetTokenKiller.Infrastructure -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/bin/Release/net10.0/DotnetTokenKiller.Infrastructure.dll
  DotnetTokenKiller.Cli -> /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Release/net10.0/DotnetTokenKiller.Cli.dll
  Successfully created package '/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Release/DotnetTokenKiller.Cli.1.0.0.nupkg'.
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.13
```

**Expected output** (for snapshot verification):

```sh
✓ dotnet pack → DotnetTokenKiller.Cli.1.0.0.nupkg (4 projects, 4.13s)
```

- 4 `.dll` lines → N = 4
- `.nupkg` filename: `DotnetTokenKiller.Cli.1.0.0.nupkg` (via `Path.GetFileName` on the `Successfully created package` path)
- Time Elapsed 00:00:04.13 → 4.13s
- Token savings: fixture ~740 chars, output ~57 chars → ~92% ≥85% ✓

### Test Implementation Patterns

#### Loading embedded fixture files (same pattern as stories 1.5, 2.1, 3.1, 3.2)

```csharp
private static string LoadFixture(string resourceName)
{
    var assembly = typeof(DotnetPackFilterTests).Assembly;
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

public class DotnetPackFilterTests
{
    private readonly DotnetPackFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(85.0, because: "pack filter should achieve ≥85% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Build succeeded")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_pack_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_BuildError_ShowsErrorGroupingFormat()
    {
        const string input = """
            MSBuild version 17.11.9+a69bbaaf5 for .NET
              /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs(5,1): error CS0001: Type or namespace 'Foo' not found [/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]
            Build FAILED.
                1 Error(s)
            Time Elapsed 00:00:01.00
            """;
        var result = _sut.Apply(input);
        result.Should().StartWith("dotnet pack: 1 error");
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
        var assembly = typeof(DotnetPackFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

#### Verify snapshot acceptance workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetPackFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetPackFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt` — should contain `✓ dotnet pack → DotnetTokenKiller.Cli.1.0.0.nupkg (4 projects, 4.13s)`
4. Accept by renaming: `mv *.received.txt *.verified.txt`
5. Re-run tests — all pass
6. Commit `.verified.txt` file

**File to commit in Snapshots folder**:

- `DotnetPackFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5, 2.1, 3.1, 3.2)

- **CA1852** — `DotnetPackFilter` MUST be `sealed`
- **`[GeneratedRegex]`** — Class MUST be `partial`; methods declared as `private static partial Regex MethodName()`; missing `partial` = build error
- **CA1305** — `sb.AppendLine($"...")` with format args: use `sb.AppendLine(CultureInfo.InvariantCulture, $"...")`
- **CA1305 on TryParse** — Use `TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts)` (3-param overload)
- **RCS1201** — Chain consecutive `sb.AppendLine(...).AppendLine(...)` when writing header + separator
- **CA1050/RCS1110/S3903** — All types MUST be in named namespaces (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **RCS1118** — Repeated string literals: use `const string Separator = "---"` (already in skeleton)
- **IDE0290** — Primary constructors preferred; `DotnetPackCommand` uses primary constructor pattern
- **using System.Text.RegularExpressions** — NOT in implicit usings; must be explicit
- **`using System.Globalization`** — NOT in implicit usings; must be explicit
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetPackFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`Diagnostic` record**: declared as `private sealed record` inside the filter class — matches pattern from `DotnetBuildFilter`, `DotnetTestFilter`, `DotnetPublishFilter`
- **`using` import ordering**: After `dotnet format`, project usings come before system usings per `.editorconfig` — the skeleton above already shows correct order
- **S3358** — Avoid nested ternaries: if needed, extract intermediate variable (same fix as story 3.2)
- **`Path.GetFileName`** — In `System.IO` which IS in implicit usings; no explicit `using` needed

### Git Context (Recent Commits)

```sh
42f07b5 feat: add DotnetPublishFilter and integrate into CLI command
66a43f0 Merge branch 'develop' into feat/restore-publish-pack-filters
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
6adf543 Feat: update sprint status and mark dotnet restore filter implementation as completed
e0b29c1 Feat: enhance DotnetRestoreFilter to support F# projects and improve line splitting logic
```

We are on branch `feat/restore-publish-pack-filters` — this branch is already set up for stories 3.1, 3.2, and 3.3.

### Key Learnings from Stories 3.1 and 3.2 (Applied Here)

- **`DotnetPublishFilter` is the closest reference** — copy and adapt it (swap publish-path extraction for .nupkg filename extraction)
- **`sealed record Diagnostic` inside filter**: `private sealed record Diagnostic(...)` inside the class — do the same
- **`FindAll` vs `Where(...).ToList()`**: Use `FindAll` to avoid potential RCS1201 analyzer complaints
- **Snapshot directory**: Configured globally in `VerifyInit.cs` via `[ModuleInitializer]` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: Test method must return `Task` (not `void`) and must NOT be async — just `return Verify(result)`
- **Fixture absolute paths**: Fixtures use `/home/handys11/Dev/DotnetTokenKiller/...` absolute paths so that `TextHelpers.ShortenPath` produces reliable relative paths in diagnostic tests
- **`dotnet format` run last**: Always run `dotnet format --verify-no-changes` after all tests pass — catches import ordering issues
- **S3358 nested ternary fix from 3.2**: Extract intermediate variable (e.g. `projectSuffix`) to avoid nested ternary warning
- **`string.Join` with LINQ**: `string.Join(", ", someList)` is analyzer-safe
- **`Path.GetFileName` note**: `TrimEnd('\'')` may be needed to strip trailing quote if the regex captures it — alternative: use a regex group that ends before the closing quote

### What This Story Does NOT Implement (Scope Guard)

- `DotnetCleanFilter`, `DotnetRunFilter`, `DotnetEfFilter`, `DotnetFormatFilter`, `DotnetNugetFilter` — Epic 4
- Passthrough command for unrecognized subcommands — Story 4.6
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- Any other CLI command rewiring besides `DotnetPackCommand`

### Project Structure Notes

- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetPackFilter` singleton
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_pack_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPackFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetPackFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
- No new directories to create — all target directories already exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.3]
- [Source: _bmad-output/implementation-artifacts/3-2-implement-dotnet-publish-filter-with-tests.md] — closest previous story; pack filter is structurally identical, swap publish-path extraction for nupkg filename extraction
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs] — direct implementation reference (copy and adapt)
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs] — diagnostic grouping reference
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init
- [Source: tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs] — test structure reference

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Implemented `DotnetPackFilter` — structurally identical to `DotnetPublishFilter`, adapted to extract `.nupkg` filename via `PackOutputPattern` regex + `Path.GetFileName`
- Registered `DotnetPackFilter` singleton in `AddApplication()`
- Rewired `DotnetPackCommand` from `ICommandRunner.RunPassthroughAsync` to `FilteredRunUseCase.RunAsync` with primary constructor injection
- 9 new tests added (snapshot, savings gate, 4 noise line theory, error grouping, null/empty safety)
- Verify snapshot accepted: `✓ dotnet pack → DotnetTokenKiller.Cli.1.0.0.nupkg (4 projects, 4.13s)` (~92% savings, ≥85% threshold met)
- Total test count: 102 (up from 93) — 0 regressions
- Build: 0 errors, 0 warnings; `dotnet format --verify-no-changes` → exit 0

### File List

- src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs (new)
- src/DotnetTokenKiller.Application/DependencyInjection.cs (modified)
- src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs (modified)
- tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPackFilterTests.cs (new)
- tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_pack_raw.txt (new)
- tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetPackFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt (new)

### Change Log

- 2026-03-10: Implemented DotnetPackFilter with tests; wired DotnetPackCommand to FilteredRunUseCase; registered singleton in DI; accepted Verify snapshot

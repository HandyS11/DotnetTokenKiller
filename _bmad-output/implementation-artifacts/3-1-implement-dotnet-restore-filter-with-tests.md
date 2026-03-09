# Story 3.1: Implement dotnet restore Filter with Tests

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet restore`,
I want restore output filtered to a compact one-line summary,
So that I save 90–95% of tokens while still seeing the project count, timing, and any NuGet errors.

## Acceptance Criteria

1. **Success (restored projects)**: When `DotnetRestoreFilter.Apply(rawOutput)` is called with a fixture containing individual `Restored ...` lines, the output is a single line: `✓ dotnet restore (N projects, X.XXs)`, where N is the count of `Restored .csproj` lines and X.XX is the total duration (sum of all "in N ms" values), and token savings ≥90%.
2. **Success (up-to-date count)**: Project count includes "N of M projects are up-to-date for restore" (adds N to the count), combined with any `Restored` lines.
3. **Success (all up-to-date)**: When "All projects are up-to-date for restore." appears and no `Restored` lines exist, the output is `✓ dotnet restore (all up-to-date)`.
4. **Failure (NuGet errors)**: When `Apply(rawOutput)` is called with a fixture containing a NU-prefixed error, the output begins with `dotnet restore: N error(s)`, and each NuGet error is shown on its own line as `NU1101: <message> (<project-relative-path>)`.
5. **NuGet project path shortened**: The affected project path is shortened to project-relative using forward slashes (e.g., `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj`).
6. **Noise removal**: None of the following appear in output: "MSBuild version", "Determining projects to restore...", "Writing assets file to disk.", "NuGet Config files used", individual package download progress lines (`GET https://...`).
7. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
8. **`[GeneratedRegex]`**: All regex patterns in `DotnetRestoreFilter` use `[GeneratedRegex]` attributes on `private static partial` methods; class is `partial`.
9. **`DotnetRestoreCommand` wired**: `DotnetRestoreCommand` injects `FilteredRunUseCase filteredRun` and `DotnetRestoreFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
10. **DI registration**: `DotnetRestoreFilter` is registered as a singleton in `AddApplication()`.
11. **Snapshot test**: A Verify.Xunit snapshot test exists for the success scenario in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
12. **Fixture file**: `dotnet_restore_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
13. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 74 tests).
14. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [ ] Task 1: Create fixture file as embedded resource (AC: #12)
  - [ ] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_restore_raw.txt` — see "Fixture File Content" section below
  - [ ] Directory and `EmbeddedResource` glob already exist from story 1.5 — no `.csproj` changes needed

- [ ] Task 2: Implement `DotnetRestoreFilter` (AC: #1, #2, #3, #4, #5, #6, #7, #8)
  - [ ] Create `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs`
  - [ ] `public sealed partial class DotnetRestoreFilter(string? rootPath = null) : IOutputFilter`
  - [ ] Implement `Apply(string rawOutput)` — see "Precise Implementation" section below
  - [ ] All regex patterns via `[GeneratedRegex]` on `private static partial` methods

- [ ] Task 3: Register `DotnetRestoreFilter` in DI (AC: #10)
  - [ ] Add `services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter())` to `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [ ] Task 4: Wire `DotnetRestoreCommand` to use `FilteredRunUseCase` (AC: #9)
  - [ ] Update `src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs`
  - [ ] Inject `FilteredRunUseCase filteredRun` and `DotnetRestoreFilter filter` via primary constructor
  - [ ] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [ ] Remove old `ICommandRunner commandRunner` injection

- [ ] Task 5: Write filter tests (AC: #1, #4, #5, #6, #7, #11)
  - [ ] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs`
  - [ ] Snapshot test for success scenario (Verify.Xunit — static `Verifier.Verify()`)
  - [ ] Savings gate test: success ≥90%
  - [ ] Noise line tests: verify none of the noise patterns appear in success output
  - [ ] NuGet error test: error count header + code shown in output
  - [ ] Edge case: `Apply(null!)` → no throw, returns non-null
  - [ ] Edge case: `Apply("")` → no throw, returns non-null

- [ ] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #11)
  - [ ] Run `dotnet test --filter "FullyQualifiedName~DotnetRestoreFilterTests"` → first run fails (no `.verified.txt`)
  - [ ] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` for correctness
  - [ ] Rename `.received.txt` → `.verified.txt`
  - [ ] Re-run tests → all snapshot tests pass

- [ ] Task 7: Build and verify (AC: #13, #14)
  - [ ] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [ ] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 74 + new filter tests)
  - [ ] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — `Verify.Xunit 28.2.0` already added in 1.5 |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — **use as reference for pattern/style** |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — **use as reference for simple success format** |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetRestoreFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` | Complete (74 total tests) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_restore_raw.txt` here |
| `.github/workflows/quality-gate.yml` | Complete (story 1.6) |

**Test count baseline**: 74 tests (17 Domain + 55 Application + 1 Infrastructure + 1 Integration). All must continue to pass.

**`DotnetRestoreCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it (same rewiring pattern as `DotnetTestCommand` in story 2.1).

### Architecture Constraints (CRITICAL)

- `DotnetRestoreFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`)
- Helpers are in `Application.Helpers` namespace — use them: `AnsiStrip.Strip`, `TextHelpers.ShortenPath`, `TextHelpers.Truncate`
- Filter MUST be `stateless` — `_rootPath` is readonly; no mutable instance fields
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` (required for `[GeneratedRegex]` partial methods)
- All regex patterns MUST use `[GeneratedRegex]` — `new Regex(...)` at runtime is FORBIDDEN (AOT constraint)
- `[GeneratedRegex]` methods must be `private static partial Regex MethodName()`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- Path shortening uses `TextHelpers.ShortenPath(absolutePath, _rootPath)` (same pattern as `DotnetBuildFilter` and `DotnetTestFilter`)
- `using System.Text.RegularExpressions` is NOT in implicit usings — must be explicit
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`

### Precise Implementation: `DotnetRestoreFilter`

The restore filter is simpler than the build filter — it doesn't need an explicit `IsNoiseLine` helper. Instead, it only collects the specific lines it cares about (Restored, up-to-date, NuGet errors) and ignores everything else (noise lines fall through all pattern checks and are silently dropped).

#### Class skeleton

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed partial class DotnetRestoreFilter(string? rootPath = null) : IOutputFilter
{
    private const int MessageMaxLen = 200;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var errors = new List<NuGetError>();
        var restoredCount = 0;
        var upToDateCount = 0;
        var allUpToDate = false;
        double totalDurationMs = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // "  Restored /path/Project.csproj (in 123 ms)."
            var restoredMatch = RestoredPattern().Match(line);
            if (restoredMatch.Success)
            {
                restoredCount++;
                if (double.TryParse(restoredMatch.Groups["ms"].Value, CultureInfo.InvariantCulture, out var ms))
                    totalDurationMs += ms;
                continue;
            }

            // "All projects are up-to-date for restore."
            if (AllUpToDatePattern().IsMatch(line))
            {
                allUpToDate = true;
                continue;
            }

            // "3 of 5 projects are up-to-date for restore."
            var partialMatch = PartialUpToDatePattern().Match(line);
            if (partialMatch.Success)
            {
                if (int.TryParse(partialMatch.Groups["count"].Value, CultureInfo.InvariantCulture, out var count))
                    upToDateCount = count;
                continue;
            }

            // "/path/proj.csproj : error NU1101: message" (project path before error code)
            var errorProjFirstMatch = NuGetErrorProjectFirstPattern().Match(line);
            if (errorProjFirstMatch.Success)
            {
                var proj = TextHelpers.ShortenPath(errorProjFirstMatch.Groups["proj"].Value.Trim(), _rootPath);
                errors.Add(new NuGetError(
                    errorProjFirstMatch.Groups["code"].Value,
                    TextHelpers.Truncate(errorProjFirstMatch.Groups["message"].Value.Trim(), MessageMaxLen),
                    proj));
                continue;
            }

            // "error NU1101: message [/path/proj.csproj]" (standard NuGet error format)
            var errorStdMatch = NuGetErrorStandardPattern().Match(line);
            if (errorStdMatch.Success)
            {
                var projRaw = errorStdMatch.Groups["proj"].Value.Trim();
                var proj = string.IsNullOrEmpty(projRaw)
                    ? string.Empty
                    : TextHelpers.ShortenPath(projRaw, _rootPath);
                errors.Add(new NuGetError(
                    errorStdMatch.Groups["code"].Value,
                    TextHelpers.Truncate(errorStdMatch.Groups["message"].Value.Trim(), MessageMaxLen),
                    proj));
                continue;
            }
        }

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet restore: {errors.Count} error{(errors.Count == 1 ? "" : "s")}");
            foreach (var e in errors)
            {
                if (string.IsNullOrEmpty(e.Project))
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message}");
                else
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message} ({e.Project})");
            }
            return sb.ToString();
        }

        var totalProjects = restoredCount + upToDateCount;

        if (totalProjects == 0 && allUpToDate)
            return "✓ dotnet restore (all up-to-date)\n";

        if (totalProjects == 0)
            return string.Empty;

        var elapsed = $"{totalDurationMs / 1000.0:F2}s";
        return $"✓ dotnet restore ({totalProjects} project{(totalProjects == 1 ? "" : "s")}, {elapsed})\n";
    }

    private sealed record NuGetError(string Code, string Message, string Project);

    // "  Restored /path/Project.csproj (in 123 ms)."
    [GeneratedRegex(@"^\s+Restored .+\.csproj \(in (?<ms>[\d.]+) ms\)")]
    private static partial Regex RestoredPattern();

    // "All projects are up-to-date for restore."
    [GeneratedRegex(@"All projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex AllUpToDatePattern();

    // "3 of 5 projects are up-to-date for restore."
    [GeneratedRegex(@"(?<count>\d+) of \d+ projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex PartialUpToDatePattern();

    // "/path/proj.csproj : error NU1101: message here"
    [GeneratedRegex(@"^\s*(?<proj>\S+\.csproj)\s*:\s*error\s+(?<code>NU\d+):\s+(?<message>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorProjectFirstPattern();

    // "error NU1101: message text [/path/proj.csproj]"
    [GeneratedRegex(@"error\s+(?<code>NU\d+):\s+(?<message>[^\[]+)(?:\s*\[(?<proj>[^\]]+)\])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorStandardPattern();
}
```

#### Updated `DotnetRestoreCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetRestoreCommand(
    FilteredRunUseCase filteredRun,
    DotnetRestoreFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("restore").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` dependency — `FilteredRunUseCase` holds it.

#### Updated `DependencyInjection.cs` (Application project)

Add one line — singleton registration for `DotnetRestoreFilter`. Match the existing `DotnetBuildFilter` and `DotnetTestFilter` registration pattern:

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
        services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter());  // ADD THIS
        return services;
    }
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_restore_raw.txt`:

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj (in 123 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj (in 456 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj (in 234 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj (in 89 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj (in 67 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj (in 301 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Infrastructure.Tests/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj (in 45 ms).
  Writing assets file to disk. Path: /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Cli.IntegrationTests/obj/project.assets.json
  Restored /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj (in 112 ms).
```

**Expected output** (for snapshot verification):

```sh
✓ dotnet restore (8 projects, 1.43s)
```

Total duration = 123+456+234+89+67+301+45+112 = 1427 ms → 1.43s. Token savings ≈ 98% ✓

### Test Implementation Patterns

#### Loading embedded fixture files (same pattern as stories 1.5, 2.1)

```csharp
private static string LoadFixture(string resourceName)
{
    var assembly = typeof(DotnetRestoreFilterTests).Assembly;
    var fullName = assembly.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(fullName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

#### Test class structure (Verify.Xunit v28 — static API)

**CRITICAL**: Do NOT use `[UsesVerify]` attribute or inherit from `VerifyBase`. Use static `Verifier.Verify()` directly (v28 requirement). The `VerifyInit.cs` already configures `UseProjectRelativeDirectory("Snapshots")` and `IgnoreStackTrace()` globally via `[ModuleInitializer]`.

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetRestoreFilterTests
{
    private readonly DotnetRestoreFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, because: "restore filter should achieve ≥90% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Determining projects to restore")]
    [InlineData("Writing assets file to disk")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_restore_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_NuGetError_ShowsErrorCountAndCode()
    {
        const string input = """
            MSBuild version 17.11.9+a69bbaaf5 for .NET
              Determining projects to restore...
              /home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj : error NU1101: Unable to find package 'NonExistent.Package'. No packages exist with this id in source(s): nuget.org
            """;
        var result = _sut.Apply(input);
        result.Should().StartWith("dotnet restore: 1 error");
        result.Should().Contain("NU1101:");
        result.Should().Contain("src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj");
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
        var assembly = typeof(DotnetRestoreFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

#### Verify snapshot acceptance workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetRestoreFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetRestoreFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt` — should contain `✓ dotnet restore (8 projects, 1.43s)`
4. Accept by renaming: `mv *.received.txt *.verified.txt`
5. Re-run tests — all pass
6. Commit `.verified.txt` file

**File to commit in Snapshots folder**:

- `DotnetRestoreFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5 and 2.1)

- **CA1852** — `DotnetRestoreFilter` MUST be `sealed`
- **`[GeneratedRegex]`** — Class MUST be `partial`; methods declared as `private static partial Regex MethodName()`; missing `partial` = build error
- **CA1305** — `sb.AppendLine($"...")` with format args on numerics: use `sb.AppendLine(CultureInfo.InvariantCulture, $"...")`; already handled by using CultureInfo overload
- **CA1305 on TryParse** — Use `double.TryParse(s, CultureInfo.InvariantCulture, out var ms)` and `int.TryParse(s, CultureInfo.InvariantCulture, out var count)` — the 3-param overload with IFormatProvider
- **RCS1201** — Chain consecutive `sb.AppendLine(...).AppendLine(...)` when writing the error header; in this filter the foreach loop makes chaining harder — just keep separate calls
- **CA1050/RCS1110/S3903** — All types MUST be in named namespaces (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **RCS1118** — String literals that repeat: `MessageMaxLen` is already `const int`; the `" error"` / `" errors"` pluralization strings are distinct and short, no extraction needed
- **IDE0290** — Primary constructors preferred; `DotnetRestoreCommand` uses primary constructor pattern — do the same
- **using System.Text.RegularExpressions** — NOT in implicit usings; must be explicit
- **`using System.Globalization`** — NOT in implicit usings; must be explicit
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetRestoreFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`NuGetError` record**: declared as `private sealed record` inside the filter class — matches `FailureInfo` pattern from `DotnetTestFilter`
- **`double.TryParse` 3-param**: `double.TryParse(string, IFormatProvider, out double)` is available in .NET 7+ (target is net10.0, so ✓)

### Git Context (Recent Commits)

### Key Learnings from Story 2.1 (DotnetTestFilter)

- **`sealed record` inside filter**: `private sealed record FailureInfo(...)` inside the class — follow same for `NuGetError`
- **`List<T>.Find` vs `FirstOrDefault`**: Use `List<T>.Find` if needed (analyzer-preferred for `List<string>`)
- **Snapshot directory**: Configured globally in `VerifyInit.cs` via `[ModuleInitializer]` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: Test method must return `Task` (not `void`) and must NOT be async — just `return Verify(result)`
- **Fixture shortening**: Fixtures use absolute paths `/home/handys11/Dev/DotnetTokenKiller/...` so that `TextHelpers.ShortenPath` produces reliable relative paths
- **`dotnet format` run last**: Always run `dotnet format --verify-no-changes` after all tests pass — catches import ordering issues

### What This Story Does NOT Implement (Scope Guard)

- `DotnetPublishFilter` — Story 3.2
- `DotnetPackFilter` — Story 3.3
- `DotnetCleanFilter`, `DotnetRunFilter`, etc. — Epic 4
- `SqliteTracker` — Story 5.1 (still using `NullTracker`)
- `JsonConfigProvider` — Story 6.1 (still using `NullConfigProvider`)
- `FileTeeService` — Story 6.2 (still using `NullTeeService`)
- Any other CLI command rewiring besides `DotnetRestoreCommand`

### Project Structure Notes

- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetRestoreFilter` singleton
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_restore_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetRestoreFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
- No new directories to create — all target directories already exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.1]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#10. Testing Strategy — Filter Testing Pattern]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/implementation-artifacts/2-1-implement-dotnet-test-filter-with-tests.md] — previous story patterns and learnings
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs] — reference implementation style
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs] — simpler success-format reference
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init (UseProjectRelativeDirectory + IgnoreStackTrace)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List

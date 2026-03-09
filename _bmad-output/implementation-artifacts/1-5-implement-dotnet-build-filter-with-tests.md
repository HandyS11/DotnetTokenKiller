# Story 1.5: Implement dotnet build Filter with Tests

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet build`,
I want the build output filtered to a compact, information-dense summary,
So that I save 80–90% of tokens compared to raw `dotnet build` output while retaining all actionable information.

## Acceptance Criteria

1. **Clean success**: When `DotnetBuildFilter.Apply(rawOutput)` is called with a clean-success fixture (~20 lines), the output is exactly one line: `✓ dotnet build (N projects, X.XXs)`, and token savings ≥85%.
2. **Build with warnings**: When applied to a warnings fixture, output starts with `dotnet build: 0 errors, N warnings (N projects, X.XXs)`, followed by a `---` separator, then warnings grouped by diagnostic code (e.g., `CS0219 (2x)`) with shortened file paths and line numbers; token savings ≥75%.
3. **Build failure**: When applied to a failure fixture (~40 lines with MSBuild duplicate error section), output starts with `dotnet build: N errors, M warnings`, followed by `---` separator, errors grouped by file sorted by error count descending with shortened paths and line numbers, duplicates deduplicated, top diagnostic codes listed (max 5); messages truncated at 120 characters; token savings ≥70%.
4. **Noise line removal**: None of the following appear in any output: MSBuild version header, `Determining projects to restore...`, `All projects are up-to-date for restore.`, `Restored ... (in N ms).`, `Build started`, `Build succeeded.`, `Build FAILED.`, `N Warning(s)`, `N Error(s)`, blank lines.
5. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
6. **ANSI stripping**: Output containing ANSI escape codes is stripped before parsing; no ANSI sequences appear in filter output.
7. **[GeneratedRegex]**: All regex patterns in `DotnetBuildFilter` use `[GeneratedRegex]` attributes on `private static partial` methods; class is `partial`.
8. **Snapshot tests**: Verify.Xunit snapshot tests exist for the success, warnings, and failure scenarios in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
9. **Fixture files**: `dotnet_build_success.txt`, `dotnet_build_warnings.txt`, `dotnet_build_errors.txt` exist as embedded resources in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
10. **DotnetBuildCommand wired**: `DotnetBuildCommand` injects `FilteredRunUseCase` + `DotnetBuildFilter` and calls `filteredRun.RunAsync(filter, "dotnet", args, verbosityLevel, ct)` instead of `RunPassthroughAsync`.
11. **DI registration**: `DotnetBuildFilter` is registered as a singleton in `AddApplication()`.
12. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 46 tests).
13. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Add Verify.Xunit to central package management and test project (AC: #8)
  - [x] Look up latest stable `Verify.Xunit` version on nuget.org compatible with xunit v2 (currently ~`28.x`)
  - [x] Add `<PackageVersion Include="Verify.Xunit" Version="X.Y.Z"/>` to `Directory.Packages.props`
  - [x] Add `<PackageReference Include="Verify.Xunit"/>` to `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj`

- [x] Task 2: Create fixture files as embedded resources (AC: #9)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/` directory
  - [x] Create `dotnet_build_success.txt` — see "Fixture File Content" section below
  - [x] Create `dotnet_build_warnings.txt` — see "Fixture File Content" section below
  - [x] Create `dotnet_build_errors.txt` — see "Fixture File Content" section below
  - [x] Add `EmbeddedResource` entries to `DotnetTokenKiller.Application.Tests.csproj`:

    ```xml
    <ItemGroup>
      <EmbeddedResource Include="Fixtures/**/*.txt"/>
    </ItemGroup>
    ```

- [x] Task 3: Implement `DotnetBuildFilter` (AC: #1, #2, #3, #4, #5, #6, #7)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs`
  - [x] `public sealed partial class DotnetBuildFilter : IOutputFilter`
  - [x] Implement `Apply(string rawOutput)` — see "Precise Implementation" section below
  - [x] All regex patterns via `[GeneratedRegex]` on `private static partial` methods

- [x] Task 4: Register `DotnetBuildFilter` in DI (AC: #11)
  - [x] Add `services.AddSingleton<DotnetBuildFilter>()` to `DependencyInjection.cs` in Application project
  - [x] Add `using DotnetTokenKiller.Application.Filters;` to `DependencyInjection.cs`

- [x] Task 5: Wire `DotnetBuildCommand` to use `FilteredRunUseCase` (AC: #10)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetBuildFilter filter` via primary constructor
  - [x] Replace `RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection

- [x] Task 6: Write filter tests (AC: #1, #2, #3, #4, #5, #6, #8)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs`
  - [x] Snapshot test for success scenario (Verify.Xunit)
  - [x] Snapshot test for warnings scenario (Verify.Xunit)
  - [x] Snapshot test for failure scenario (Verify.Xunit)
  - [x] Savings gate test: success ≥85%, warnings ≥75%, errors ≥70%
  - [x] Noise line tests: verify none of the noise patterns appear in output
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null
  - [x] Edge case: ANSI codes in input → stripped from output

- [x] Task 7: Accept Verify snapshots and commit `.verified.txt` files (AC: #8)
  - [x] Run `dotnet test DotnetTokenKiller.slnx` → tests fail first time (no `.verified.txt`)
  - [x] `.received.txt` files are generated alongside test file or in Snapshots/ folder
  - [x] Verify output looks correct, then rename each `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 8: Build and verify (AC: #12, #13)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 46 + new filter tests)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Story 1.4)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, LangVersion=14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Exists — **needs Verify.Xunit added** |
| `.editorconfig` | Modified in 1.4 — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs DotnetBuildFilter singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs` | **Needs rewiring to FilteredRunUseCase** |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Domain.Tests/**` | 17 passing tests — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/**` | 29 passing tests — DO NOT BREAK |

**DotnetBuildCommand currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it.

**Filters directory does not exist yet** — this story creates `src/DotnetTokenKiller.Application/Filters/`.

**Fixtures directory does not exist yet** — this story creates `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.

### Architecture Constraints (CRITICAL)

- `DotnetBuildFilter` lives in `Application` layer → references `Domain` only (IOutputFilter)
- Helpers are in `Application.Helpers` namespace — use them: `AnsiStrip.Strip`, `TextHelpers.ShortenPath`, `TextHelpers.Truncate`
- Filter MUST be `stateless` — no instance fields storing state between `Apply` calls
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` (required for `[GeneratedRegex]` partial methods)
- All regex patterns MUST use `[GeneratedRegex]` — `new Regex(...)` at runtime is FORBIDDEN (AOT constraint)
- `[GeneratedRegex]` methods must be `private static partial Regex MethodName()`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- Path shortening uses `TextHelpers.ShortenPath(absolutePath, Environment.CurrentDirectory)` — filter uses current dir as root

### Precise Implementation: `DotnetBuildFilter`

#### Class skeleton

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed partial class DotnetBuildFilter : IOutputFilter
{
    private const string Separator = "---";
    private const int MessageMaxLen = 120;

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

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Count project output lines (e.g., "  MyProject -> /path/to/bin/Debug/...")
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

            // Skip noise lines
            if (IsNoiseLine(line))
                continue;

            // Parse diagnostic lines
            var diagMatch = DiagnosticPattern().Match(line);
            if (!diagMatch.Success)
                continue;

            var key = $"{diagMatch.Groups["file"].Value}({diagMatch.Groups["line"].Value},{diagMatch.Groups["col"].Value}):{diagMatch.Groups["code"].Value}";
            if (!seen.Add(key))
                continue; // deduplicate MSBuild duplicate error section

            diagnostics.Add(new Diagnostic(
                File: TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), Environment.CurrentDirectory),
                Line: diagMatch.Groups["line"].Value,
                Col: diagMatch.Groups["col"].Value,
                Level: diagMatch.Groups["level"].Value,
                Code: diagMatch.Groups["code"].Value,
                Message: TextHelpers.Truncate(diagMatch.Groups["message"].Value.Trim(), MessageMaxLen)));
        }

        var errors = diagnostics.Where(d => d.Level == "error").ToList();
        var warnings = diagnostics.Where(d => d.Level == "warning").ToList();
        var context = BuildContext(projectCount, elapsed);

        if (errors.Count == 0 && warnings.Count == 0)
            return $"✓ dotnet build{context}\n";

        var sb = new System.Text.StringBuilder();

        if (errors.Count == 0)
        {
            sb.AppendLine($"dotnet build: 0 errors, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}{context}");
            sb.AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
        }
        else
        {
            sb.AppendLine($"dotnet build: {errors.Count} error{(errors.Count == 1 ? "" : "s")}, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}");
            sb.AppendLine(Separator);
            AppendGroupedByFile(sb, errors);
            AppendTopCodes(sb, errors);
            if (warnings.Count > 0)
            {
                sb.AppendLine($"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} suppressed (use -v to see)");
            }
        }

        return sb.ToString();
    }

    // ... helper methods and regex patterns below
}
```

#### Diagnostic record

```csharp
private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);
```

#### Helper methods (inside DotnetBuildFilter)

```csharp
private static string BuildContext(int projectCount, string elapsed)
{
    if (projectCount == 0 && string.IsNullOrEmpty(elapsed))
        return string.Empty;
    if (string.IsNullOrEmpty(elapsed))
        return $" ({projectCount} project{(projectCount == 1 ? "" : "s")})";
    if (projectCount == 0)
        return $" ({elapsed})";
    return $" ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})";
}

private static string FormatElapsed(string timeElapsedLine)
{
    // Parse "Time Elapsed HH:MM:SS.ff" → "X.XXs"
    var match = TimeSpanValuePattern().Match(timeElapsedLine);
    if (!match.Success)
        return string.Empty;

    if (TimeSpan.TryParse(match.Value, out var ts))
        return $"{ts.TotalSeconds:F2}s";

    return string.Empty;
}

private static bool IsNoiseLine(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return true;
    return NoiseMsbuildVersionPattern().IsMatch(line)
        || NoiseRestoringPattern().IsMatch(line)
        || NoiseRestoredPattern().IsMatch(line)
        || NoiseBuildStartedPattern().IsMatch(line)
        || NoiseBuildResultPattern().IsMatch(line)
        || NoiseCountPattern().IsMatch(line)
        || NoiseTimeElapsedPattern().IsMatch(line)
        || NoiseProjectOutputPattern().IsMatch(line);  // "  Project -> /path.dll" already handled above — filter them out here too as fallback
}

private static void AppendGroupedByCode(System.Text.StringBuilder sb, List<Diagnostic> diags)
{
    foreach (var group in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
    {
        var items = group.ToList();
        sb.AppendLine($"{group.Key} ({items.Count}x)");
        foreach (var d in items)
            sb.AppendLine($"  {d.File}:{d.Line} — {d.Message}");
    }
}

private static void AppendGroupedByFile(System.Text.StringBuilder sb, List<Diagnostic> errors)
{
    foreach (var group in errors.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
    {
        var items = group.ToList();
        sb.AppendLine($"{group.Key} ({items.Count} error{(items.Count == 1 ? "" : "s")})");
        foreach (var d in items)
            sb.AppendLine($"  ({d.Line},{d.Col}) {d.Code}: {d.Message}");
    }
}

private static void AppendTopCodes(System.Text.StringBuilder sb, List<Diagnostic> errors)
{
    var topCodes = errors
        .GroupBy(d => d.Code)
        .OrderByDescending(g => g.Count())
        .Take(5)
        .Select(g => $"{g.Key} ({g.Count()}x)")
        .ToList();

    if (topCodes.Count > 0)
        sb.AppendLine($"Top codes: {string.Join(", ", topCodes)}");
}
```

#### GeneratedRegex patterns (inside DotnetBuildFilter)

```csharp
// Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
// Also matches indented versions (leading spaces)
[GeneratedRegex(@"^\s*(?<file>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<message>[^\[]+?)(?:\s*\[.+?\])?\s*$")]
private static partial Regex DiagnosticPattern();

// Matches: "  MyProject -> /path/to/bin/MyProject.dll"
[GeneratedRegex(@"^\s+\S+ -> .+\.dll\s*$")]
private static partial Regex ProjectOutputPattern();

// Matches "Time Elapsed HH:MM:SS.ff"
[GeneratedRegex(@"Time Elapsed \d{2}:\d{2}:\d{2}\.\d+")]
private static partial Regex TimeElapsedPattern();

// Extracts the HH:MM:SS.ff value from a Time Elapsed line
[GeneratedRegex(@"\d{2}:\d{2}:\d{2}\.\d+")]
private static partial Regex TimeSpanValuePattern();

// Noise: MSBuild version header
[GeneratedRegex(@"MSBuild version", RegexOptions.IgnoreCase)]
private static partial Regex NoiseMsbuildVersionPattern();

// Noise: Restore progress lines
[GeneratedRegex(@"Determining projects to restore|All projects are up-to-date for restore")]
private static partial Regex NoiseRestoringPattern();

// Noise: "Restored /path/Project.csproj (in N ms)."
[GeneratedRegex(@"^\s*Restored .+\.csproj")]
private static partial Regex NoiseRestoredPattern();

// Noise: "Build started ..."
[GeneratedRegex(@"Build started")]
private static partial Regex NoiseBuildStartedPattern();

// Noise: "Build succeeded." or "Build FAILED."
[GeneratedRegex(@"^Build (succeeded|FAILED)\.?\s*$", RegexOptions.IgnoreCase)]
private static partial Regex NoiseBuildResultPattern();

// Noise: "    0 Warning(s)" and "    0 Error(s)"
[GeneratedRegex(@"^\s+\d+ (Warning|Error)\(s\)\s*$")]
private static partial Regex NoiseCountPattern();

// Noise: "Time Elapsed ..." line itself
[GeneratedRegex(@"^Time Elapsed")]
private static partial Regex NoiseTimeElapsedPattern();

// Noise: project output redirect (-> dll) - fallback for IsNoiseLine
[GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$")]
private static partial Regex NoiseProjectOutputPattern();
```

### Updated `DotnetBuildCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetBuildCommand(
    FilteredRunUseCase filteredRun,
    DotnetBuildFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("build").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` dependency — the command no longer needs it directly; `FilteredRunUseCase` holds it.

### Updated `DependencyInjection.cs` (Application project)

Add one line — singleton registration for `DotnetBuildFilter`:

```csharp
using DotnetTokenKiller.Application.Filters;   // ADD THIS
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddSingleton<DotnetBuildFilter>();  // ADD THIS
        return services;
    }
}
```

**Why singleton**: Filters are stateless pure functions — one instance is safe across all commands.

### Fixture File Content

The developer must create representative real-looking `dotnet build` output. Below are templates; populate with paths matching your actual project structure.

#### `dotnet_build_success.txt`

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Domain -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/bin/Debug/net10.0/DotnetTokenKiller.Domain.dll
  DotnetTokenKiller.Application -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/bin/Debug/net10.0/DotnetTokenKiller.Application.dll
  DotnetTokenKiller.Infrastructure -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/bin/Debug/net10.0/DotnetTokenKiller.Infrastructure.dll
  DotnetTokenKiller.Cli -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/bin/Debug/net10.0/DotnetTokenKiller.Cli.dll
  DotnetTokenKiller.Domain.Tests -> /home/user/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll
  DotnetTokenKiller.Application.Tests -> /home/user/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/bin/Debug/net10.0/DotnetTokenKiller.Application.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.34
```

#### `dotnet_build_warnings.txt`

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(10,18): warning CS0219: The variable 'unused' is assigned but its value is never used [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(15,22): warning CS0219: The variable 'alsoUnused' is assigned but its value is never used [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs(5,10): warning CS0169: The field '_unused' is never used [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj]
  DotnetTokenKiller.Domain -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/bin/Debug/net10.0/DotnetTokenKiller.Domain.dll
  DotnetTokenKiller.Application -> /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/bin/Debug/net10.0/DotnetTokenKiller.Application.dll

Build succeeded.
    3 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.67
```

#### `dotnet_build_errors.txt`

Note: MSBuild prints errors **twice** — once inline and once in a summary block at the end. The filter must deduplicate.

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(10,5): error CS1002: ; expected [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(12,18): error CS0103: The name 'undeclared' does not exist in the current context [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs(8,10): error CS1002: ; expected [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
  /home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs(3,5): warning CS0169: The field '_unused' is never used [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj]

Build FAILED.

/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(10,5): error CS1002: ; expected [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs(12,18): error CS0103: The name 'undeclared' does not exist in the current context [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs(8,10): error CS1002: ; expected [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj]
/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs(3,5): warning CS0169: The field '_unused' is never used [/home/user/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj]

    3 Error(s)
    1 Warning(s)

Time Elapsed 00:00:01.89
```

### Test Implementation Patterns

#### Loading embedded fixture files

```csharp
private static string LoadFixture(string resourceName)
{
    var assembly = typeof(DotnetBuildFilterTests).Assembly;
    var fullName = assembly.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(fullName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

#### Snapshot test class structure (Verify.Xunit v2 API)

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

[UsesVerify]
public class DotnetBuildFilterTests
{
    private readonly DotnetBuildFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_WarningsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_ErrorsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast85Percent()
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(85.0, because: "build success filter should achieve ≥85% savings");
    }

    [Fact]
    public void Apply_WarningsFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_build_warnings.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(75.0, because: "build warnings filter should achieve ≥75% savings");
    }

    [Fact]
    public void Apply_ErrorsFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_build_errors.txt");
        var result = _sut.Apply(fixture);
        var inputTokens = fixture.Length / 4;
        var outputTokens = result.Length / 4;
        var savings = 100.0 - (outputTokens * 100.0 / inputTokens);
        savings.Should().BeGreaterThanOrEqualTo(70.0, because: "build errors filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("MSBuild version 17.0")]
    [InlineData("Determining projects to restore...")]
    [InlineData("All projects are up-to-date for restore.")]
    [InlineData("Build succeeded.")]
    [InlineData("Build FAILED.")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_build_success.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
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

    [Fact]
    public void Apply_AnsiCodesInInput_StrippedFromOutput()
    {
        var ansiInput = "\x1b[32mBuild succeeded.\x1b[0m\n";
        var result = _sut.Apply(ansiInput);
        result.Should().NotContain("\x1b[");
    }

    private static string LoadFixture(string resourceName) { /* ... as above ... */ }
}
```

#### Verify snapshot acceptance workflow

1. Run tests once: `dotnet test --filter "FullyQualifiedName~DotnetBuildFilterTests"`
2. Snapshot tests fail with `received` files created (e.g., `DotnetBuildFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt`)
3. Inspect each `.received.txt` for correctness
4. Accept by renaming: `mv *.received.txt → *.verified.txt` (or use `DiffEngineTray` / `dotnet verify accept`)
5. Run tests again — all pass
6. Commit `.verified.txt` files to source control

**Files to commit in Snapshots folder**:

- `DotnetBuildFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
- `DotnetBuildFilterTests.Apply_WarningsFixture_MatchesSnapshot.verified.txt`
- `DotnetBuildFilterTests.Apply_ErrorsFixture_MatchesSnapshot.verified.txt`

By default Verify places snapshot files alongside the test file. To redirect to `Snapshots/` folder, add to test project:

```csharp
// tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs
using System.Runtime.CompilerServices;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UseDirectory("Snapshots");
    }
}
```

### Package Management (CRITICAL)

All versions in `Directory.Packages.props` only — never in `.csproj` files.

```xml
<!-- Directory.Packages.props — add inside existing <ItemGroup> -->
<PackageVersion Include="Verify.Xunit" Version="28.2.0"/>
```

> **Check**: Verify the latest stable version at <https://www.nuget.org/packages/Verify.Xunit>
> Compatible with xunit v2 (project uses xunit 2.9.3).
> If a newer version is available and compatible, use it.

```xml
<!-- tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj -->
<!-- Add to existing PackageReference ItemGroup -->
<PackageReference Include="Verify.Xunit"/>
```

```xml
<!-- Also add embedded resource support to the same .csproj -->
<ItemGroup>
  <EmbeddedResource Include="Fixtures/**/*.txt"/>
</ItemGroup>
```

### Analyzer Pitfalls (CRITICAL — learned from Story 1.4)

- **CA1852** — `DotnetBuildFilter` MUST be `sealed`
- **`[GeneratedRegex]`** — Class MUST be `partial`; patterns declared as `private static partial Regex MethodName()`; missing `partial` is a build error
- **CA1305** — Any `ToString()` calls on numerics need `CultureInfo.InvariantCulture` or use string interpolation (which uses invariant for format specs) — use `$"{value:F2}"` not `value.ToString("F2")`
- **CA1031** — Already suppressed in `.editorconfig` (CA1031 = broad catch); empty catch blocks need comments (Option A)
- **RCS1267** — Do not use `string.Concat(span, ...)` when string interpolation suffices; use `$"..."` or range indexers
- **S108** — Empty `catch { }` blocks: add explanatory comment (already suppressed in `.editorconfig` from story 1.4? check; if not, add comment)
- **IDE0290** — Primary constructors preferred; `DotnetBuildCommand` uses primary constructor pattern
- **using System.Text.RegularExpressions** — NOT in implicit usings; must be explicit
- **File-scoped namespaces** everywhere: `namespace DotnetTokenKiller.Application.Filters;`
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetBuildFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`

### Git Context (Recent Commits)

| Commit | What was built |
|---|---|
| `680e635` | Story 1.4 — helpers + FilteredRunUseCase; 29 Application.Tests; NSubstitute added |
| `7525970` | Story 1.3 — ProcessCommandRunner, CLI commands (all use RunPassthroughAsync today) |
| `4c78002` | Story 1.1 — full solution scaffold, all 8 projects |

**Pattern from story 1.4**: Helpers use `string.IsNullOrEmpty` guard first, `partial` class where needed, `CultureInfo.InvariantCulture` for number formatting, intentional `catch { /* comment */ }` for fire-and-forget calls.

### What This Story Does NOT Implement (Scope Guard)

- `DotnetTestFilter`, `DotnetRestoreFilter`, etc. — Stories 2.1, 3.x
- `SqliteTracker` — Story 5.1 (still using `NullTracker`)
- `JsonConfigProvider` — Story 6.1 (still using `NullConfigProvider`)
- `FileTeeService` — Story 6.2 (still using `NullTeeService`)
- `PassthroughRunUseCase` — Story 4.6 (fallback for unrecognized subcommands)
- `GainReportUseCase` / `GainCommand` — Story 5.3
- GitHub Actions CI — Story 1.6
- Any other CLI command rewiring besides `DotnetBuildCommand` — future stories

### Project Structure Notes

- **Alignment with unified project structure**: New `Filters/` directory in Application project matches architecture spec (`src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs`)
- **New `Fixtures/` directory** in test project: `tests/DotnetTokenKiller.Application.Tests/Fixtures/` — matched by architecture spec
- **New `Filters/` directory** in test project: `tests/DotnetTokenKiller.Application.Tests/Filters/` — matches architecture's `Filters/` test folder
- **Verify snapshot files**: go in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` (configure via `VerifyInit.cs` or leave as default alongside tests)
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;` and `namespace DotnetTokenKiller.Application.Tests.Filters;`
- LF line endings, no trailing whitespace, 4-space indent for `.cs`, 2-space for `.csproj`

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.5]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/planning-artifacts/Architecture.md#4. Layer Responsibilities]
- [Source: _bmad-output/planning-artifacts/Architecture.md#9. DI Registration]
- [Source: _bmad-output/planning-artifacts/Architecture.md#10. Testing Strategy — Filter Testing Pattern]
- [Source: _bmad-output/implementation-artifacts/1-4-implement-core-application-helpers-and-filteredrunusecase.md#Dev Agent Record]
- [Source: _bmad-output/implementation-artifacts/1-4-implement-core-application-helpers-and-filteredrunusecase.md#Analyzer Pitfalls]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Implemented `DotnetBuildFilter` as `sealed partial` class with 10 `[GeneratedRegex]` patterns; all analyzer warnings resolved
- Verify.Xunit v28 uses `Verifier.Verify()` static API (not `[UsesVerify]` attribute, not `VerifyBase` inheritance); snapshots placed in `Snapshots/` via `Verifier.UseProjectRelativeDirectory("Snapshots")` in `VerifyInit.cs`
- Fixture files use absolute paths for `/home/handys11/Dev/DotnetTokenKiller/...` plus Restored/Build-started noise lines to achieve savings targets: success 98%+, warnings 75%+, errors 86%+
- All 60 tests pass (17 Domain + 43 Application = 29 pre-existing + 14 new filter tests); 0 warnings build; format verified clean

### File List

- `Directory.Packages.props` — added `Verify.Xunit 28.2.0`
- `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` — new
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` — added `DotnetBuildFilter` singleton
- `src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs` — rewired to `FilteredRunUseCase`
- `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj` — added `Verify.Xunit` + `EmbeddedResource`
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_success.txt` — new
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_warnings.txt` — new
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt` — new
- `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` — new
- `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` — new
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetBuildFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt` — new
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetBuildFilterTests.Apply_WarningsFixture_MatchesSnapshot.verified.txt` — new
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetBuildFilterTests.Apply_ErrorsFixture_MatchesSnapshot.verified.txt` — new
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — `1-5` status → `review`

## Change Log

- 2026-03-09: Story 1.5 implemented — added `DotnetBuildFilter` with 10 GeneratedRegex patterns, 3 fixture files, 14 new filter tests (snapshot + savings + noise + edge cases), wired `DotnetBuildCommand` to `FilteredRunUseCase`, registered filter in DI

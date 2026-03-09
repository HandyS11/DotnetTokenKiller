# Story 2.1: Implement dotnet test Filter with Tests

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet test`,
I want test output filtered to show only failures and a compact summary,
So that I save 90–95% of tokens on passing runs and immediately see what failed without scrolling through noise.

## Acceptance Criteria

1. **All-pass (single project)**: When `DotnetTestFilter.Apply(rawOutput)` is called with an all-pass fixture (~30 lines, single test project), the output is a single line: `✓ dotnet test: N passed (1 project, X.XXs)`, and token savings ≥90%.
2. **All-pass (multi-project)**: When applied to a multi-assembly pass fixture, the output aggregates across all assemblies: `✓ dotnet test: N passed (M projects, X.XXs)`.
3. **Failures present**: When applied to a failure fixture with 2 failed tests, the output begins with `FAILURES (2):`, each failure is listed with: test name, duration in ms, compacted error message (single line, max 200 chars), and source file + line number from the first stack frame containing a `.cs` file reference. The output ends with a summary line: `dotnet test: 2 failed, 40 passed (1 project, X.XXs)`. Token savings ≥70%.
4. **Failure cap**: When more than 15 test failures are present, only the first 15 are shown followed by `+N more failures`.
5. **xUnit Assert.Equal compaction**: When a multi-line xUnit `Assert.Equal` failure message contains Expected/Actual on separate lines, it is compacted to: `Expected: "X", Actual: "Y"`.
6. **Zero tests**: When a zero-tests-found scenario is passed to `Apply`, the output is `✓ dotnet test: 0 tests found`.
7. **Noise removal**: None of the following appear in any output: MSBuild version header, copyright notice, test runner header (`Test run for ...`), `Starting test execution, please wait...`, `A total of N test files matched the specified pattern.`, build preamble lines (`Determining projects to restore...`, `Restored ...`, `All projects are up-to-date for restore.`, `Project -> /path/dll` build output).
8. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
9. **[GeneratedRegex]**: All regex patterns in `DotnetTestFilter` use `[GeneratedRegex]` attributes on `private static partial` methods; class is `partial`.
10. **DotnetTestCommand wired**: `DotnetTestCommand` injects `FilteredRunUseCase filteredRun` and `DotnetTestFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
11. **DI registration**: `DotnetTestFilter` is registered as a singleton in `AddApplication()`.
12. **Snapshot tests**: Verify.Xunit snapshot tests exist for the all-pass and failure scenarios in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
13. **Fixture files**: `dotnet_test_all_pass.txt` and `dotnet_test_failures.txt` exist as embedded resources in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
14. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 60 tests).
15. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [ ] Task 1: Create fixture files as embedded resources (AC: #13)
  - [ ] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_all_pass.txt` — see "Fixture File Content" section below
  - [ ] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_failures.txt` — see "Fixture File Content" section below
  - [ ] Fixtures directory already exists (created in story 1.5); `EmbeddedResource` glob already covers `Fixtures/**/*.txt`

- [ ] Task 2: Implement `DotnetTestFilter` (AC: #1, #2, #3, #4, #5, #6, #7, #8, #9)
  - [ ] Create `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`
  - [ ] `public sealed partial class DotnetTestFilter(string? rootPath = null) : IOutputFilter`
  - [ ] Implement `Apply(string rawOutput)` — see "Precise Implementation" section below
  - [ ] All regex patterns via `[GeneratedRegex]` on `private static partial` methods
  - [ ] Implement `CompactErrorMessage` helper for xUnit Assert.Equal multi-line compaction

- [ ] Task 3: Register `DotnetTestFilter` in DI (AC: #11)
  - [ ] Add `services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter())` to `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [ ] Task 4: Wire `DotnetTestCommand` to use `FilteredRunUseCase` (AC: #10)
  - [ ] Update `src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs`
  - [ ] Inject `FilteredRunUseCase filteredRun` and `DotnetTestFilter filter` via primary constructor
  - [ ] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [ ] Remove old `ICommandRunner commandRunner` injection

- [ ] Task 5: Write filter tests (AC: #1, #2, #3, #4, #5, #6, #7, #8, #12)
  - [ ] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`
  - [ ] Snapshot test for all-pass scenario (Verify.Xunit — static `Verifier.Verify()`)
  - [ ] Snapshot test for failure scenario (Verify.Xunit)
  - [ ] Savings gate test: all-pass ≥90%, failures ≥70%
  - [ ] Noise line tests: verify none of the noise patterns appear in all-pass output
  - [ ] Edge case: `Apply(null!)` → no throw, returns non-null
  - [ ] Edge case: `Apply("")` → no throw, returns non-null

- [ ] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #12)
  - [ ] Run `dotnet test --filter "FullyQualifiedName~DotnetTestFilterTests"` → tests fail first time (no `.verified.txt`)
  - [ ] `.received.txt` files generated in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
  - [ ] Inspect each `.received.txt` for correctness, then rename → `.verified.txt`
  - [ ] Re-run tests → all snapshot tests pass

- [ ] Task 7: Build and verify (AC: #14, #15)
  - [ ] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [ ] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 60 + new filter tests)
  - [ ] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6)

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
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — **use as reference pattern** |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs DotnetTestFilter singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs` | **Needs rewiring to FilteredRunUseCase** |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` | Complete (60 total tests) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add new `.txt` fixtures here |
| `.github/workflows/quality-gate.yml` | Complete (story 1.6) |

**Test count baseline**: 60 tests (17 Domain + 43 Application). All must continue to pass.

**DotnetTestCommand currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it.

### Architecture Constraints (CRITICAL)

- `DotnetTestFilter` lives in `Application` layer → references `Domain` only (IOutputFilter)
- Helpers are in `Application.Helpers` namespace — use them: `AnsiStrip.Strip`, `TextHelpers.ShortenPath`, `TextHelpers.Truncate`
- Filter MUST be `stateless` — `_rootPath` is readonly; no mutable instance fields
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` (required for `[GeneratedRegex]` partial methods)
- All regex patterns MUST use `[GeneratedRegex]` — `new Regex(...)` at runtime is FORBIDDEN (AOT constraint)
- `[GeneratedRegex]` methods must be `private static partial Regex MethodName()`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- Path shortening uses `TextHelpers.ShortenPath(absolutePath, _rootPath)` (same pattern as `DotnetBuildFilter`)
- `using System.Text.RegularExpressions` is NOT in implicit usings — must be explicit
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`

### Precise Implementation: `DotnetTestFilter`

#### Class skeleton

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed partial class DotnetTestFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFailures = 15;
    private const int MessageMaxLen = 200;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var failures = new List<FailureInfo>();
        var totalPassed = 0;
        var totalFailed = 0;
        var projectCount = 0;
        double totalDurationMs = 0;
        var zeroTestsFound = false;

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            // Parse summary line: "Passed! - Failed: 0, Passed: 17, ..., Duration: 89 ms - File.dll"
            var summaryMatch = SummaryPattern().Match(line);
            if (summaryMatch.Success)
            {
                totalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
                totalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
                totalDurationMs += double.Parse(summaryMatch.Groups["duration"].Value, CultureInfo.InvariantCulture);
                projectCount++;
                i++;
                continue;
            }

            // Zero tests pattern: total 0 in summary or "No test matches"
            if (NoTestsPattern().IsMatch(line))
            {
                zeroTestsFound = true;
                i++;
                continue;
            }

            // Failed test header: "  Failed TestName [12 ms]"
            var failedHeaderMatch = FailedTestHeaderPattern().Match(line);
            if (failedHeaderMatch.Success)
            {
                var testName = failedHeaderMatch.Groups["name"].Value.Trim();
                var duration = failedHeaderMatch.Groups["duration"].Value;
                i++;

                // Skip "Error Message:" label
                if (i < lines.Length && ErrorMessageLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
                    i++;

                // Collect message lines until "Stack Trace:" or next failed test or summary
                var msgLines = new List<string>();
                while (i < lines.Length)
                {
                    var current = lines[i].TrimEnd('\r');
                    if (StackTraceLabelPattern().IsMatch(current)
                        || FailedTestHeaderPattern().IsMatch(current)
                        || SummaryPattern().IsMatch(current))
                        break;
                    var trimmed = current.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        msgLines.Add(trimmed);
                    i++;
                }

                // Skip "Stack Trace:" label
                if (i < lines.Length && StackTraceLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
                    i++;

                // Find first stack frame with a .cs file reference
                var sourceRef = string.Empty;
                while (i < lines.Length)
                {
                    var current = lines[i].TrimEnd('\r');
                    if (FailedTestHeaderPattern().IsMatch(current) || SummaryPattern().IsMatch(current))
                        break;
                    if (string.IsNullOrEmpty(sourceRef))
                    {
                        var frameMatch = StackFrameFilePattern().Match(current);
                        if (frameMatch.Success)
                            sourceRef = $"{TextHelpers.ShortenPath(frameMatch.Groups["file"].Value, _rootPath)}:line {frameMatch.Groups["line"].Value}";
                    }
                    i++;
                }

                failures.Add(new FailureInfo(testName, duration, CompactMessage(msgLines), sourceRef));
                continue;
            }

            i++;
        }

        // Zero tests: either explicit no-tests pattern or all summaries showed 0
        if (zeroTestsFound || (projectCount > 0 && totalPassed == 0 && totalFailed == 0))
            return "✓ dotnet test: 0 tests found\n";

        if (projectCount == 0)
            return string.Empty;

        var elapsed = $"{totalDurationMs / 1000.0:F2}s";

        if (totalFailed == 0)
            return $"✓ dotnet test: {totalPassed} passed ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})\n";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"FAILURES ({totalFailed}):");

        var shown = failures.Count <= MaxFailures ? failures : failures.Take(MaxFailures).ToList();
        foreach (var f in shown)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {f.TestName} [{f.Duration} ms]");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {f.Message}");
            if (!string.IsNullOrEmpty(f.SourceRef))
                sb.AppendLine(CultureInfo.InvariantCulture, $"    at {f.SourceRef}");
        }

        if (failures.Count > MaxFailures)
            sb.AppendLine(CultureInfo.InvariantCulture, $"+{failures.Count - MaxFailures} more failures");

        sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet test: {totalFailed} failed, {totalPassed} passed ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})");

        return sb.ToString();
    }

    private sealed record FailureInfo(string TestName, string Duration, string Message, string SourceRef);

    private static string CompactMessage(List<string> lines)
    {
        if (lines.Count == 0)
            return string.Empty;

        // xUnit Assert.Equal multi-line: detect "Expected: ..." and "Actual: ..." on separate lines
        var expectedLine = lines.Find(l => l.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase));
        var actualLine = lines.Find(l => l.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase));
        if (expectedLine != null && actualLine != null)
            return TextHelpers.Truncate($"{expectedLine}, {actualLine}", MessageMaxLen);

        return TextHelpers.Truncate(string.Join(" ", lines), MessageMaxLen);
    }

    // Summary: "Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 89 ms - File.dll"
    // Also matches "Failed! - Failed: 2, ..."
    [GeneratedRegex(@"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+\d+,\s+Total:\s+\d+,\s+Duration:\s+(?<duration>[\d.]+)\s+ms", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

    // "  Failed FullyQualifiedTestName [12 ms]"
    [GeneratedRegex(@"^\s+Failed\s+(?<name>.+?)\s+\[(?<duration>\d+)\s+ms\]\s*$")]
    private static partial Regex FailedTestHeaderPattern();

    // Stack frame with CS file: "   at Class.Method() in /path/to/File.cs:line 42"
    [GeneratedRegex(@"in (?<file>.+\.cs):line (?<line>\d+)")]
    private static partial Regex StackFrameFilePattern();

    // "  Error Message:"
    [GeneratedRegex(@"^\s+Error Message:\s*$")]
    private static partial Regex ErrorMessageLabelPattern();

    // "  Stack Trace:"
    [GeneratedRegex(@"^\s+Stack Trace:\s*$")]
    private static partial Regex StackTraceLabelPattern();

    // Zero tests: "No test matches the given testcase filter"
    [GeneratedRegex(@"No test matches the given testcase filter|No test is available", RegexOptions.IgnoreCase)]
    private static partial Regex NoTestsPattern();
}
```

#### Updated `DotnetTestCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetTestCommand(
    FilteredRunUseCase filteredRun,
    DotnetTestFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("test").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` dependency — `FilteredRunUseCase` holds it.

#### Updated `DependencyInjection.cs` (Application project)

Add one line — singleton registration for `DotnetTestFilter`. The `_ => new DotnetTestFilter()` pattern matches the existing `DotnetBuildFilter` registration:

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
        services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter());  // ADD THIS
        return services;
    }
}
```

### Fixture File Content

Create representative real-looking `dotnet test` output. These use absolute paths so `TextHelpers.ShortenPath` produces predictable shortened values in tests.

#### `dotnet_test_all_pass.txt`

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Domain.Tests -> /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll

Test run for /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll (.NETCoreApp,Version=v10.0)
Microsoft (R) Test Execution Command Line Tool Version 17.11.9 (x64)
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17, Duration: 89 ms - DotnetTokenKiller.Domain.Tests.dll (net10.0)
```

#### `dotnet_test_failures.txt`

Note: this fixture has 2 failed tests — one with a long FluentAssertions message and one with a multi-line xUnit `Assert.Equal` failure.

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Application.Tests -> /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/bin/Debug/net10.0/DotnetTokenKiller.Application.Tests.dll

Test run for /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/bin/Debug/net10.0/DotnetTokenKiller.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Microsoft (R) Test Execution Command Line Tool Version 17.11.9 (x64)
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

  Failed DotnetTokenKiller.Application.Tests.Filters.DotnetBuildFilterTests.Apply_SuccessFixture_SavingsAtLeast85Percent [12 ms]
  Error Message:
   Expected the actual value to be greater than or equal to 85.0 because build success filter should achieve ≥85% savings, but found 70.0.
  Stack Trace:
     at DotnetTokenKiller.Application.Tests.Filters.DotnetBuildFilterTests.Apply_SuccessFixture_SavingsAtLeast85Percent() in /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs:line 43
     at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)

  Failed DotnetTokenKiller.Application.Tests.Helpers.TokenEstimatorTests.Estimate_KnownText_ReturnsExpected [5 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
   Expected: 42
   Actual:   38
  Stack Trace:
     at DotnetTokenKiller.Application.Tests.Helpers.TokenEstimatorTests.Estimate_KnownText_ReturnsExpected() in /home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/Helpers/TokenEstimatorTests.cs:line 15
     at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)

Failed!  - Failed:     2, Passed:    41, Skipped:     0, Total:    43, Duration: 234 ms - DotnetTokenKiller.Application.Tests.dll (net10.0)
```

### Test Implementation Patterns

#### Loading embedded fixture files

```csharp
private static string LoadFixture(string resourceName)
{
    var assembly = typeof(DotnetTestFilterTests).Assembly;
    var fullName = assembly.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(fullName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

#### Test class structure (Verify.Xunit v28 — static API)

**CRITICAL**: Do NOT use `[UsesVerify]` attribute or inherit from `VerifyBase`. Use static `Verifier.Verify()` directly (this is what v28 requires). The `VerifyInit.cs` already configures `UseProjectRelativeDirectory("Snapshots")` and `IgnoreStackTrace()` globally via `[ModuleInitializer]`.

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetTestFilterTests
{
    private readonly DotnetTestFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_AllPassFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_FailuresFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_AllPassFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, because: "test all-pass filter should achieve ≥90% savings");
    }

    [Fact]
    public void Apply_FailuresFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, because: "test failures filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("MSBuild version 17.11")]
    [InlineData("Microsoft (R) Test Execution")]
    [InlineData("Copyright (c) Microsoft")]
    [InlineData("Starting test execution, please wait...")]
    [InlineData("A total of 1 test files matched")]
    [InlineData("Determining projects to restore")]
    public void Apply_AllPassFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
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

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetTestFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

#### Verify snapshot acceptance workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetTestFilterTests"`
2. Snapshot tests fail; `.received.txt` files appear in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect each `.received.txt` for correctness
4. Accept by renaming: `mv *.received.txt *.verified.txt` (or use `dotnet verify accept`)
5. Re-run tests — all pass
6. Commit `.verified.txt` files

**Files to commit in Snapshots folder**:

- `DotnetTestFilterTests.Apply_AllPassFixture_MatchesSnapshot.verified.txt`
- `DotnetTestFilterTests.Apply_FailuresFixture_MatchesSnapshot.verified.txt`

### Analyzer Pitfalls (CRITICAL — accumulated from Epic 1)

- **CA1852** — `DotnetTestFilter` MUST be `sealed`
- **`[GeneratedRegex]`** — Class MUST be `partial`; methods declared as `private static partial Regex MethodName()`; missing `partial` = build error
- **CA1305** — Any `ToString()` or `AppendLine($"...")` on numerics: use `sb.AppendLine(CultureInfo.InvariantCulture, $"...")`; this is already handled by using `CultureInfo.InvariantCulture` overload of `AppendLine`
- **RCS1201** — Chain consecutive `sb.AppendLine(...).AppendLine(...)` if the analysis suggests it; however checking the existing `DotnetBuildFilter`, it uses single `AppendLine` calls with chaining where possible — follow the same pattern
- **S6580** — `TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out ts)` (this filter does not use TimeSpan directly; it divides totalDurationMs / 1000.0 instead — no issue)
- **CA1050/RCS1110/S3903** — All types MUST be in named namespaces (already covered with file-scoped `namespace DotnetTokenKiller.Application.Filters;`)
- **RCS1118** — String literals that repeat should be `const` (the filter has few repeated strings; use `const` for `MaxFailures` and `MessageMaxLen` which are already int constants)
- **IDE0290** — Primary constructors preferred; `DotnetTestCommand` uses primary constructor pattern — do the same
- **using System.Text.RegularExpressions** — NOT in implicit usings; must be explicit
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetTestFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **List.Find vs LINQ FirstOrDefault**: `List<T>.Find` is analyzer-preferred over `LINQ.FirstOrDefault` for `List<string>`; use it in `CompactMessage`
- **int.Parse overflow warning**: use `int.Parse(value, CultureInfo.InvariantCulture)` — explicit culture required by CA1305

### Git Context (Recent Commits)

| Commit | What was built |
|---|---|
| `d534cbd` | Feat: core & foundation (PR #1 — stories 1.1–1.6 merged) |
| `4c78002` | Story 1.1 — full solution scaffold, 8 projects |

**Current branch**: `develop`. All Epic 1 stories are merged. Start this story on a fresh feature branch off `develop`.

### What This Story Does NOT Implement (Scope Guard)

- `DotnetRestoreFilter`, `DotnetPublishFilter`, `DotnetPackFilter` — Stories 3.1–3.3
- `DotnetCleanFilter`, `DotnetRunFilter`, etc. — Epic 4
- `SqliteTracker` — Story 5.1 (still using `NullTracker`)
- `JsonConfigProvider` — Story 6.1 (still using `NullConfigProvider`)
- `FileTeeService` — Story 6.2 (still using `NullTeeService`)
- Any other CLI command rewiring besides `DotnetTestCommand` — future stories
- Multi-OS CI matrix — Story 7.2

### Project Structure Notes

- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`
- New fixtures (in existing directory): `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_all_pass.txt`, `dotnet_test_failures.txt`
- New snapshots (generated then committed): `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetTestFilterTests.Apply_AllPassFixture_MatchesSnapshot.verified.txt`, `...Apply_FailuresFixture_MatchesSnapshot.verified.txt`
- No new directories to create — all target directories already exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.1]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#10. Testing Strategy — Filter Testing Pattern]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/implementation-artifacts/1-5-implement-dotnet-build-filter-with-tests.md] — reference implementation pattern
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init (UseProjectRelativeDirectory + IgnoreStackTrace)
- [Source: tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs] — test class pattern

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List

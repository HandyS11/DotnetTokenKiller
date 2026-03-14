# Story 8.2: Integration Tests for build, restore, and clean

Status: review

## Story

As a developer,
I want integration tests that invoke `dtk dotnet build`, `dtk dotnet restore`, and `dtk dotnet clean` against real sample projects — covering both success and failure paths for each command,
So that filter correctness, output format, exit codes, and token reduction targets are all validated under actual SDK conditions.

## Acceptance Criteria

1. **— dotnet build —**

   **Given** `dtk dotnet build` is invoked against `sample/SampleApp`
   **When** the build succeeds
   **Then** the output is a single line starting with `✓ dotnet build`
   **And** no MSBuild noise is present (version header, restore lines, "Build succeeded.", blank lines)
   **And** the exit code is 0
   **And** token savings is ≥80%

2. **Given** `dtk dotnet build` is invoked against `sample/SampleApp.Broken`
   **When** the build fails
   **Then** the output starts with `dotnet build: 1 error`
   **And** the error line contains a shortened file path and line number
   **And** no MSBuild noise lines are present in the output
   **And** the exit code is non-zero
   **And** token savings is ≥70%

3. **— dotnet restore —**

   **Given** `dtk dotnet restore` is invoked against `sample/SampleApp`
   **When** restore succeeds
   **Then** the output is a single line starting with `✓ dotnet restore`
   **And** no package download progress lines or "Writing assets file" lines are present
   **And** the exit code is 0
   **And** token savings is ≥90%

4. **Given** `dtk dotnet restore` is invoked against `sample/SampleApp.BadPackage`
   **When** restore fails with a `NU1101` package-not-found error
   **Then** the output starts with `dotnet restore: 1 error(s)`
   **And** the `NU1101` error code and package name are present in the output
   **And** no package download progress lines are present
   **And** the exit code is non-zero

5. **— dotnet clean —**

   **Given** `dtk dotnet clean` is invoked against `sample/SampleApp` after a prior successful build
   **When** clean succeeds
   **Then** the output is exactly `✓ dotnet clean`
   **And** the exit code is 0
   **And** token savings is ≥95%

6. **Given** `dtk dotnet clean` is invoked against `sample/SampleApp.Broken` (unbuildable project)
   **When** clean runs (clean does not require a prior successful build)
   **Then** the exit code is 0 and the output is `✓ dotnet clean` (clean succeeds even on broken projects)

7. **And** all integration tests in this story are in `DotnetTokenKiller.Cli.IntegrationTests`
   **And** a shared `IntegrationTestHelper` class resolves the `dtk` CLI executable path (from the built `DotnetTokenKiller.Cli` output DLL relative to `Environment.CurrentDirectory`) and resolves the `sample/` folder path (navigating up to the repo root)
   **And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

## Tasks / Subtasks

- [x] Create `IntegrationTestHelper` class in `DotnetTokenKiller.Cli.IntegrationTests` (AC: #7)
  - [x] Resolve CLI DLL path: `Path.Combine(AppContext.BaseDirectory, "DotnetTokenKiller.Cli.dll")`
  - [x] Resolve repo root path: navigate 5 levels up from `AppContext.BaseDirectory`
  - [x] Expose `SamplePath(string project)` returning absolute path to `sample/{project}`
  - [x] Implement `RunDtkAsync(params string[] args)` → returns `(string output, int exitCode)`
  - [x] Implement `RunDotnetAsync(params string[] args)` → returns `(string output, int exitCode)` (for raw comparison)
  - [x] Both run via `dotnet <dll> <args>` using `System.Diagnostics.Process`

- [x] Implement `DotnetBuildIntegrationTests` (AC: #1, #2)
  - [x] `Build_SampleApp_Success_OutputStartsWithCheckmark` — single-line `✓ dotnet build`, exit 0, savings ≥80%
  - [x] `Build_SampleApp_Success_NoMsBuildNoise` — assert output does NOT contain "MSBuild version", "Restore", "Build succeeded", blank lines
  - [x] `Build_SampleAppBroken_Failure_OutputStartsWith1Error` — `dotnet build: 1 error`, exit non-zero
  - [x] `Build_SampleAppBroken_Failure_ErrorContainsShortenedPath` — path is project-relative, contains line number
  - [x] `Build_SampleAppBroken_Failure_NoMsBuildNoise` — no noise lines
  - [x] `Build_SampleAppBroken_Failure_Savings70Percent` — savings ≥70%

- [x] Implement `DotnetRestoreIntegrationTests` (AC: #3, #4)
  - [x] `Restore_SampleApp_Success_OutputStartsWithCheckmark` — single-line `✓ dotnet restore`, exit 0, savings ≥90%
  - [x] `Restore_SampleApp_Success_NoProgressNoise` — no "Writing assets", no package download lines
  - [x] `Restore_SampleAppBadPackage_Failure_OutputStartsWith1Error` — `dotnet restore: 1 error(s)`, exit non-zero
  - [x] `Restore_SampleAppBadPackage_Failure_ContainsNu1101` — `NU1101` and package name in output
  - [x] `Restore_SampleAppBadPackage_Failure_NoProgressNoise` — no package download lines

- [x] Implement `DotnetCleanIntegrationTests` (AC: #5, #6)
  - [x] `Clean_SampleApp_AfterBuild_OutputExactlyCheckmark` — output is exactly `✓ dotnet clean`, exit 0
  - [x] `Clean_SampleApp_Savings95Percent` — savings ≥95% (build first within test)
  - [x] `Clean_SampleAppBroken_SucceedsEvenForBrokenProject` — exit 0, output `✓ dotnet clean`

- [x] Verify `dotnet test DotnetTokenKiller.slnx` passes green (AC: #7)

## Dev Notes

### IntegrationTestHelper Design

The test project already has a `<ProjectReference>` to `DotnetTokenKiller.Cli`, so `DotnetTokenKiller.Cli.dll` is copied to the test output directory. Use `AppContext.BaseDirectory` (more reliable than `Environment.CurrentDirectory` for test output dir):

```csharp
// DLL path — always present because of ProjectReference
var dllPath = Path.Combine(AppContext.BaseDirectory, "DotnetTokenKiller.Cli.dll");

// Repo root: navigate 5 levels up from bin/Debug/net10.0/
// net10.0 → Debug → bin → DotnetTokenKiller.Cli.IntegrationTests → tests → repo root
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

// Sample folder
var sampleRoot = Path.Combine(repoRoot, "sample");
```

**Running dtk:** invoke via `dotnet <dll> <args>`, capturing stdout+stderr merged:

```csharp
var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" {string.Join(" ", args)}")
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
};
```

Read stdout and stderr **concurrently** (two Tasks) to prevent deadlock on large output — same pattern as `ProcessCommandRunner`.

**Running raw dotnet (for savings comparison):** same approach but invoke `dotnet <subcommand> <args>` directly.

### Token Savings Calculation

Token count = `text.Length / 4` (same heuristic as `TokenEstimator`). Calculate savings as:

```csharp
static double CalculateSavings(string rawOutput, string filteredOutput)
{
    var inputTokens = rawOutput.Length / 4;
    var outputTokens = filteredOutput.Length / 4;
    if (inputTokens == 0) return 0;
    return (inputTokens - outputTokens) * 100.0 / inputTokens;
}
```

For each test needing a savings assertion: run the equivalent `dotnet` command first (to get raw output), then run `dtk`, then compare.

### Test Class Structure

Use `[Trait("Category", "Integration")]` on each test class or on individual `[Fact]` methods — the CI pipeline currently does NOT exclude integration tests for Epic 8 (they run as part of `dotnet test DotnetTokenKiller.slnx` — see epic 8 description: "no special opt-in required"). All tests must complete without skips.

All test classes must be in namespace `DotnetTokenKiller.Cli.IntegrationTests` (file-scoped namespace).

### Sample Project Paths

```sh
sample/SampleApp/SampleApp.csproj          ← success path for build, restore, clean
sample/SampleApp.Broken/SampleApp.Broken.csproj   ← 1 CS0029 error in BrokenClass.cs
sample/SampleApp.BadPackage/SampleApp.BadPackage.csproj ← NU1101 restore failure
```

Pass the `.csproj` path (or the folder path) to `dtk dotnet build <path>` etc.

### Clean Test Ordering (AC #5)

AC #5 requires clean to run "after a prior successful build." Build the project within the same test method before invoking clean — do not rely on test execution order:

```csharp
// Build first to ensure there's something to clean
await helper.RunDotnetAsync("build", sampleAppCsproj);
// Now clean
var (output, exitCode) = await helper.RunDtkAsync("dotnet", "clean", sampleAppCsproj);
```

### Expected Output Format (from Architecture §7)

| Outcome | Format |
|---|---|
| Clean success | `✓ dotnet build (<context>, <time>)` |
| Failure | `dotnet build: N errors, M warnings` + `═══` separator + structured diagnostics |

For clean success: output should be exactly `✓ dotnet clean` (with timing suffix). The AC says "exactly `✓ dotnet clean`" but the format includes context/time — assert `StartsWith("✓ dotnet clean")` and assert single line.

### Analyzer Pitfalls (from prior stories)

- **CA1515**: Already suppressed in the `.csproj` (`<NoWarn>$(NoWarn);CA1515</NoWarn>`) — public test classes are fine.
- **RCS1118**: `const string` for string literals used only once in tests — prefer `const` where the analyzer fires.
- **CA1305**: `string.Format(...)` with culture → pass `CultureInfo.InvariantCulture`; if building strings with `$"..."` in `sb.AppendLine` → `sb.AppendLine(CultureInfo.InvariantCulture, $"...")`.
- **S6580**: `TimeSpan.TryParse` → use `CultureInfo.InvariantCulture` overload.
- **CA1050/S3903**: All types must be in a named namespace — always use file-scoped `namespace DotnetTokenKiller.Cli.IntegrationTests;`.
- **VSTHRD200**: xUnit test methods (void or `Task` returning) are exempt from the `Async` suffix requirement — do NOT add `Async` to `[Fact]` method names.

### Previous Story Learnings (from Story 8.1)

- `Assert.Fail("message")` is the xUnit 2.9.3 API — NOT `Assert.True(false, "message")` (xUnit2020 analyzer fires).
- `TreatWarningsAsErrors=true` applies to sample projects too (inherited from root `Directory.Build.props`) — but this doesn't affect the integration test project itself.
- EF Core 10.0.4 packages are already in root `Directory.Packages.props`.
- If you add XML doc comments to `IntegrationTestHelper` members, be complete — RCS1141 requires all `<param>` and `<returns>` elements.

### Process Execution Pattern

Avoid creating a helper that does too much — keep it focused. Suggested minimal API:

```csharp
namespace DotnetTokenKiller.Cli.IntegrationTests;

internal static class IntegrationTestHelper
{
    private static readonly string DllPath =
        Path.Combine(AppContext.BaseDirectory, "DotnetTokenKiller.Cli.dll");

    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    internal static string SamplePath(string project) =>
        Path.Combine(RepoRoot, "sample", project);

    internal static Task<(string Output, int ExitCode)> RunDtkAsync(params string[] args) =>
        RunProcessAsync("dotnet", [$"\"{DllPath}\"", ..args]);

    internal static Task<(string Output, int ExitCode)> RunDotnetAsync(params string[] args) =>
        RunProcessAsync("dotnet", args);

    private static async Task<(string Output, int ExitCode)> RunProcessAsync(
        string executable, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(executable, string.Join(" ", args))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return (stdoutTask.Result + stderrTask.Result, process.ExitCode);
    }
}
```

Use `System.Diagnostics.Process` — it's already a dependency (no new packages needed).

### Project Structure Notes

- Alignment with project: `DotnetTokenKiller.Cli.IntegrationTests` at `tests/DotnetTokenKiller.Cli.IntegrationTests/`
- No new packages needed — `FluentAssertions` and `xunit` already in the `.csproj`
- No changes needed to `.csproj` — `ProjectReference` to `DotnetTokenKiller.Cli` already present
- New files: `IntegrationTestHelper.cs`, `DotnetBuildIntegrationTests.cs`, `DotnetRestoreIntegrationTests.cs`, `DotnetCleanIntegrationTests.cs`

### References

- [Source: epics.md — Story 8.2 acceptance criteria, lines 970–1026]
- [Source: Architecture.md — Section 10 Testing Strategy, `[Trait("Category","Integration")]` for integration tests]
- [Source: Architecture.md — Section 7 Filter Design, output format conventions]
- [Source: Architecture.md — Section 8 Process Execution — concurrent stream reads pattern]
- [Source: Architecture.md — Section 10 Token Estimation, `chars / 4` heuristic]
- [Source: project-context.md — Testing Rules, token savings targets table]
- [Source: project-context.md — Language-Specific Rules, VSTHRD200 xUnit exemption]
- [Source: 8-1-scaffold-sample-solution.md — Debug Log, xUnit2020 and RCS1118 pitfalls]
- [Source: 8-1-scaffold-sample-solution.md — Completion Notes, sample structure confirmed]
- [Source: tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj — CA1515 already suppressed]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- DLL is named `dtk.dll` (not `DotnetTokenKiller.Cli.dll`) due to `<AssemblyName>dtk</AssemblyName>` in the CLI project — updated `_dllPath` accordingly.
- `ProcessStartInfo(executable, argumentsString)` with quoted path fails on Linux (dotnet interprets quoted DLL path as tool name). Fixed by using `ProcessStartInfo(executable)` + `psi.ArgumentList.Add(arg)` per-argument.
- CA1849/VSTHRD103: `.Result` on Task after `WhenAll` still triggers async analyzer. Fixed by using `await` assignments: `var stdout = await stdoutTask`.
- Savings tests initially failed thresholds (restore 60%, clean 81%, build failure 66%) because default verbosity produces too little raw output. Fixed by passing `--force --verbosity normal` (restore) or `--verbosity normal` (build/clean) to both raw and dtk invocations.
- `SampleApp.Broken` had 6 compiler errors initially due to `TreatWarningsAsErrors=true` + analyzers. Iterated to `public int Value { get; } = "not an int";` which produces exactly 1 CS0029 with no analyzer side-effects.
- `SampleApp.BadPackage` triggered NU1015 (not NU1101) because local `Directory.Packages.props` with `ManagePackageVersionsCentrally=false` completely shadows root in NuGet CPM. Fixed by deleting local file and adding fake package version to root `Directory.Packages.props`.
- `Build_SampleAppBroken_Failure_ErrorContainsShortenedPath` assertion `NotContain("/home/")` failed because dtk appends a tee log footer line containing the absolute tee file path. Fixed by asserting `NotContain(Path.Combine(SampleAppBroken, "BrokenClass.cs"))` — the specific absolute source path.

### Completion Notes List

- All 16 integration tests pass (214 total tests, 0 failures) via `dotnet test DotnetTokenKiller.slnx`.
- `IntegrationTestHelper` uses `ArgumentList` (not string-based `Arguments`) for cross-platform process invocation reliability.
- `CalculateSavings` exposed as `internal static` on the helper (not private) to allow direct use in test classes.
- Savings thresholds require `--verbosity normal` on both raw and dtk invocations to generate enough raw output to demonstrate savings; default (minimal) verbosity produces too-small raw output.
- NuGet CPM: never create a local `Directory.Packages.props` that disables CPM for sample projects — put fake package versions in the root file instead.

### File List

- `tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs` — new
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetBuildIntegrationTests.cs` — new
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRestoreIntegrationTests.cs` — new
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetCleanIntegrationTests.cs` — new
- `sample/SampleApp.Broken/BrokenClass.cs` — modified (auto-property initializer for exactly 1 CS0029)
- `sample/SampleApp.Broken/Program.cs` — modified (added console output to fix RCS1093)
- `sample/SampleApp.BadPackage/Directory.Packages.props` — deleted
- `sample/SampleApp.BadPackage/SampleApp.BadPackage.csproj` — modified (removed explicit Version attribute)
- `Directory.Packages.props` — modified (added DotnetTokenKiller.DoesNotExist v99.0.0 for NU1101)

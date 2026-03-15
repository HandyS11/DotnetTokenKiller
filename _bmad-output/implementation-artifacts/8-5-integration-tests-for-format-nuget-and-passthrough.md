# Story 8.5: Integration Tests for format, nuget, and passthrough

Status: review

## Story

As a developer,
I want integration tests that invoke `dtk dotnet format`, `dtk dotnet nuget`, and passthrough for unrecognized subcommands — covering both success and failure paths for each,
so that the remaining filters and passthrough mode are fully validated end-to-end.

## Acceptance Criteria

1. **— dotnet format success (no changes) —**

   **Given** `dtk dotnet format --verify-no-changes` is invoked against `sample/SampleApp` (correctly formatted)
   **When** format finds no issues
   **Then** the output is `✓ dotnet format (no changes)`
   **And** the exit code is 0

2. **— dotnet format failure (files need formatting) —**

   **Given** the test copies `sample/SampleApp` to a temp directory, writes a minimal `.editorconfig` into it with `trim_trailing_whitespace = true`, injects trailing whitespace on a line of `Program.cs`, then invokes `dtk dotnet format --verify-no-changes` against the temp copy
   **When** format detects files needing changes
   **Then** the output starts with `dotnet format: 1 files need formatting`
   **And** the filename of the modified file appears in the output (shortened to a relative path)
   **And** the exit code is non-zero
   **And** the temp directory is deleted in test teardown regardless of test outcome

3. **— dotnet nuget locals list success —**

   **Given** `dtk dotnet nuget locals all --list` is invoked
   **When** nuget executes successfully
   **Then** progress bar characters and HTTP method indicator lines (`PUT`, `GET`, `Created`, `OK` + URL) are absent from the output
   **And** at least one NuGet cache location line (e.g., `http-cache:`) is present
   **And** the exit code is 0

4. **— dotnet nuget push failure —**

   **Given** `dtk dotnet nuget push nonexistent.nupkg --source https://api.nuget.org/v3/index.json` is invoked
   **When** the push fails because the file does not exist
   **Then** the output contains an error message (e.g., file not found)
   **And** the exit code is non-zero

5. **— passthrough success —**

   **Given** `dtk dotnet tool list` is invoked (unrecognized subcommand, passthrough mode)
   **When** the command executes
   **Then** the raw output from `dotnet tool list` is returned (no filtering — DotnetNugetFilter is NOT applied)
   **And** the exit code is 0

6. **— passthrough failure —**

   **Given** `dtk dotnet totally-invalid-xyz123` is invoked (unrecognized subcommand, passthrough mode)
   **When** dotnet exits with a non-zero code
   **Then** the raw error output is returned unchanged
   **And** the exit code exactly matches what the underlying `dotnet` command returns

7. **And** all integration tests in this story are in `DotnetTokenKiller.Cli.IntegrationTests`
   **And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

## Tasks / Subtasks

- [x] Implement `DotnetFormatIntegrationTests` class (AC: #1, #2, #7)
  - [x] `Format_SampleApp_VerifyNoChanges_OutputIsNoChanges` — `✓ dotnet format (no changes)`, exit 0 (AC: #1)
  - [x] `Format_TempCopy_WithTrailingWhitespace_OutputStartsWithFilesNeedFormatting` — starts with `dotnet format: 1 file need formatting`, exit non-zero (AC: #2)
  - [x] `Format_TempCopy_WithTrailingWhitespace_FilenameInOutput` — modified filename appears shortened in output (AC: #2)
  - [x] `Format_TempCopy_TempDirDeletedInTeardown` — verified via `finally` block in each test that uses a temp dir (AC: #2)

- [x] Implement `DotnetNugetIntegrationTests` class (AC: #3, #4, #7)
  - [x] `Nuget_LocalsList_Success_ContainsCacheLocation` — `http-cache:` in output, exit 0 (AC: #3)
  - [x] `Nuget_LocalsList_Success_NoHttpNoiseLines` — no `PUT`/`GET`/`Created`/`OK` URL lines in output (AC: #3)
  - [x] `Nuget_Push_NonexistentFile_Failure_OutputContainsError` — error message present, exit non-zero (AC: #4)

- [x] Implement `DotnetPassthroughIntegrationTests` class (AC: #5, #6, #7)
  - [x] `Passthrough_ToolList_Success_RawOutputPassedThrough` — `dotnet tool list` output, exit 0 (AC: #5)
  - [x] `Passthrough_InvalidSubcommand_Failure_ExitCodeNonZero` — error output, exit non-zero (AC: #6)

- [x] Verify `dotnet test DotnetTokenKiller.slnx` passes green (AC: #7)

## Dev Notes

### What Already Exists — Do NOT Recreate

- `IntegrationTestHelper` in `tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs` is complete and stable. Do NOT modify. It provides:
  - `RunDtkAsync(params string[] args)` — invokes `dotnet dtk.dll <args>` via `ArgumentList`
  - `RunDotnetAsync(params string[] args)` — invokes `dotnet <args>` directly
  - `SamplePath(string project)` — resolves `sample/<project>` from repo root
  - `CalculateSavings(string rawOutput, string filteredOutput)` — `chars/4` heuristic

- All prior integration test classes (Build, Restore, Clean, Test, Publish, Pack, Run) — follow their style exactly. Do NOT modify them.

### Filter Output Formats (exact — from source code analysis)

**DotnetFormatFilter** (`src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs`):

Success (no changes, `--verify-no-changes` on a well-formatted project):

```sh
✓ dotnet format (no changes)
```

(emitted when `warningFiles.Count == 0` AND `formattedCount == 0`)

Success (files formatted, fix mode — not `--verify-no-changes`):

```sh
✓ dotnet format (N files, Xs)
```

Failure (`--verify-no-changes` detects files that need changes):

```sh
dotnet format: N file(s) need formatting
sample/SampleApp/Program.cs
```

(WarningFilePattern: `^\s*(?<path>.+?)\s+-\s+warning\s+` — strips the `- warning IDE0055: ...` suffix, keeps the path)

**DotnetNugetFilter** (`src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs`):

`locals all --list` (passthrough of non-noise lines):

```sh
http-cache: /home/user/.local/share/NuGet/http-cache
global-packages: /home/user/.nuget/packages/
temp: /tmp/NuGet
plugins-cache: /home/user/.local/share/NuGet/plugin-cache
```

(all non-HTTP-noise, non-empty lines pass through; `http-cache:` line is NOT noise — it's a cache location line)

HTTP noise stripped (matched by `HttpNoisePattern`):

```sh
  PUT https://...
  GET https://...
  Created https://...
  OK https://...
```

Push preamble stripped (matched by `PushPreamblePattern`):

```sh
Pushing Foo.1.0.0.nupkg to 'https://...'
```

Push failure (error message passes through — not HTTP noise, not push preamble):

```sh
error: 'nonexistent.nupkg' does not exist.
```

**DotnetPassthroughCommand** (`src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs`):

Raw output, no filtering — uses `PassthroughRunUseCase` which calls `ICommandRunner.RunPassthroughAsync`.
Args: `settings.PositionalArgs.Concat(context.Remaining.Raw)` (no `"dotnet"` prefix prepended — the exe is already `dotnet`).

### Critical: Spectre.Console Arg Forwarding

From Stories 8.3/8.4: Spectre.Console 0.53.1 with `StrictParsing = false` silently drops unrecognized option flags (args starting with `--` that aren't defined on the command). Only args after `--` (the Spectre.Console separator) end up in `context.Remaining.Raw`.

**Format — `--verify-no-changes` must go after `--`:**

```csharp
// --verify-no-changes is NOT a defined option on DotnetCommandSettings,
// so it must be forwarded via -- to land in context.Remaining.Raw.
// DotnetFormatCommand: settings.PositionalArgs.Prepend("format").Concat(context.Remaining.Raw)
// Result: dotnet format <SampleApp> --verify-no-changes
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "format", SampleApp, "--", "--verify-no-changes");
```

**Nuget — `--list` and `--source` must go after `--`:**

```csharp
// DotnetNugetCommand: settings.PositionalArgs.Prepend("nuget").Concat(context.Remaining.Raw)
// Result: dotnet nuget locals all --list
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "nuget", "locals", "all", "--", "--list");

// Result: dotnet nuget push nonexistent.nupkg --source https://api.nuget.org/v3/index.json
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "nuget", "push", "nonexistent.nupkg",
    "--", "--source", "https://api.nuget.org/v3/index.json");
```

**Passthrough — use unrecognized POSITIONAL subcommand words (not flags):**

`dtk dotnet --version` is NOT a reliable passthrough test: Spectre.Console intercepts `--version` at the app level (registered via `config.SetApplicationVersion(...)`) and prints DTK's version — it never reaches `DotnetPassthroughCommand`.

Use positional words instead (captured in `settings.PositionalArgs`):

```csharp
// DotnetPassthroughCommand: settings.PositionalArgs.Concat(context.Remaining.Raw)
// Result: dotnet tool list (exit 0, raw output)
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "tool", "list");

// Result: dotnet totally-invalid-xyz123 (exit non-zero, raw output)
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "totally-invalid-xyz123");
```

### Format Temp Dir Test — Setup Details

The `--verify-no-changes` failure path requires a temp copy with a deliberate formatting violation. Steps:

```csharp
var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
try
{
    // 1. Copy SampleApp directory to temp location
    CopyDirectory(SampleApp, tempDir);

    // 2. Write minimal .editorconfig so trim_trailing_whitespace is enforced
    //    (dotnet format uses editorconfig rules from the project directory upward;
    //     without one, trailing whitespace may not be detected)
    File.WriteAllText(Path.Combine(tempDir, ".editorconfig"),
        "[root = true]\n\n[*.cs]\ntrim_trailing_whitespace = true\n");

    // 3. Inject trailing whitespace into Program.cs
    var programCs = Path.Combine(tempDir, "Program.cs");
    File.AppendAllText(programCs, "   "); // trailing whitespace at end of last line

    // 4. Run dtk dotnet format on the temp .csproj
    var tempCsproj = Path.Combine(tempDir, "SampleApp.csproj");
    var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
        "dotnet", "format", tempCsproj, "--", "--verify-no-changes");

    // 5. Assertions...
}
finally
{
    if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, recursive: true);
}
```

CopyDirectory helper (add as a private static method in the test class):

```csharp
private static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source))
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var dir in Directory.GetDirectories(source))
        CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
}
```

Note: The DotnetFormatFilter creates the path using `TextHelpers.ShortenPath(file, _rootPath)` where `_rootPath` defaults to `Environment.CurrentDirectory`. In integration tests, `Environment.CurrentDirectory` is the test bin/Debug/net10.0 dir. The shortened path will be a relative path from there — just assert that `Program.cs` appears anywhere in the output (the filename always appears even if full path shortening differs for temp dirs).

### Test Class Structure (follow existing style exactly)

```csharp
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetFormatIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    [Fact]
    public async Task Format_SampleApp_VerifyNoChanges_OutputIsNoChanges()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "format", SampleApp, "--", "--verify-no-changes");

        exitCode.Should().Be(0);
        output.Trim().Should().Be("✓ dotnet format (no changes)");
    }

    // ... temp dir tests with try/finally
}
```

Note: `SampleApp` is `static readonly` (not `const`) — `IntegrationTestHelper.SamplePath(...)` is a method call.

### Nuget Locals Output Format

`dotnet nuget locals all --list` outputs lines like (Linux):

```sh
http-cache: /home/user/.local/share/NuGet/http-cache
global-packages: /home/user/.nuget/packages/
temp: /tmp/NuGet
plugins-cache: /home/user/.local/share/NuGet/plugin-cache
```

These lines do NOT match `HttpNoisePattern` (which requires `PUT|GET|Created|OK` prefix) and are NOT push preamble — they pass through the filter unchanged. Assert `output.Should().Contain("http-cache:")`.

The exit code for `nuget locals all --list` is 0 even if some cache dirs don't exist.

### Nuget Push Failure — Local Error (No Network)

`dotnet nuget push nonexistent.nupkg --source https://api.nuget.org/v3/index.json` fails LOCALLY with:

```sh
error: 'nonexistent.nupkg' does not exist.
```

This happens before any network call — no auth or internet required. Exit code is 1. The `error:` line is NOT matched by HttpNoisePattern or PushPreamblePattern, so it passes through the filter.

Assert `output.Should().NotBeNullOrWhiteSpace()` and `exitCode.Should().NotBe(0)`. The exact error wording may vary by SDK version — avoid asserting the exact string.

### Project Structure Notes

- Three new files only:
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetFormatIntegrationTests.cs`
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetNugetIntegrationTests.cs`
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPassthroughIntegrationTests.cs`
- No new packages needed — `FluentAssertions` and `xunit` already in `.csproj`
- No changes to `.csproj`, `IntegrationTestHelper`, sample projects, or any existing test files

### Analyzer Pitfalls (accumulated from Stories 8.1–8.4)

- **VSTHRD200**: xUnit test methods are exempt from `Async` suffix — do NOT name methods `...Async`
- **CA1515**: Already suppressed in the integration test `.csproj` — public test classes are fine
- **RCS1118**: Prefer `const string` for string literals used once — but `SamplePath(...)` is a method call, so `static readonly` is correct; `".editorconfig"` content strings should be `const`
- **CA1050/S3903**: File-scoped namespace always: `namespace DotnetTokenKiller.Cli.IntegrationTests;`
- **CA1305**: If string interpolation in `AppendLine`, use `CultureInfo.InvariantCulture` overload
- **xUnit2020**: Use `Assert.Fail("msg")` — NOT `Assert.True(false, "msg")`
- **S3604**: Private static helpers (like `CopyDirectory`) go inside the class that uses them, not standalone

### References

- [Source: epics.md — Story 8.5 acceptance criteria]
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs — exact output format: `WarningFilePattern`, `FormattedFilePattern`, `FormatCompletePattern`; `warningFiles.Count > 0` path vs. `formattedCount > 0` path vs. no-changes path]
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs — `HttpNoisePattern` strips `PUT|GET|Created|OK https://...`; `PushPreamblePattern` strips push preamble; `isPush`, `isClear` paths; all other non-empty lines pass through]
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs — `settings.PositionalArgs.Prepend("format").Concat(context.Remaining.Raw)`]
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs — `settings.PositionalArgs.Concat(context.Remaining.Raw)` with no "dotnet" prepend in args (dotnet is the exe)]
- [Source: src/DotnetTokenKiller.Cli/Program.cs — `dotnet.SetDefaultCommand<DotnetPassthroughCommand>()`; `config.SetApplicationVersion(version)` intercepts `--version` at app level]
- [Source: tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs — helper API]
- [Source: tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetBuildIntegrationTests.cs — style reference]
- [Source: 8-3-integration-tests-for-test.md — Spectre.Console drops unknown option flags; use `--` separator]
- [Source: 8-4-integration-tests-for-publish-pack-and-run.md — `--verbosity normal` for savings tests; double `--` for flag forwarding]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- .NET 10 `dotnet format` output format changed from older SDKs: violations use `path(line,col): error WHITESPACE:` not `path - warning IDE0055:`, and `Format complete in NNNNms.` not `Format complete in N.NNs.`. Updated `DotnetFormatFilter` with SDK-10 compatible patterns (`WarningFilePatternSdk10`, `FormattedFilePatternSdk10`).
- `dotnet format --verify-no-changes` with no violations produces EMPTY stdout/stderr (default verbosity). Filter correctly returns empty for empty input. Success test uses `--verbosity normal` to get non-empty output that the filter processes to `✓ dotnet format (no changes)`.
- `SetDefaultCommand<DotnetPassthroughCommand>()` in Spectre.Console 0.53.1 does NOT route unrecognized positional subcommand words to the default command — Spectre.Console fails with "Unknown command" exit 255. Fixed by pre-intercepting unrecognized `dtk dotnet <word>` in `Program.cs` before `app.RunAsync(args)`.
- Format temp dir test requires `Directory.Build.props` with `TargetFramework=net10.0` in the temp dir so `dotnet format` can resolve the project without a restore failure.

### Completion Notes List

- Implemented `DotnetFormatIntegrationTests` (4 tests): success path uses `--verbosity normal` for non-empty output; failure path includes `Directory.Build.props` in temp dir for proper SDK resolution.
- Implemented `DotnetNugetIntegrationTests` (3 tests): locals list success/noise, push failure via local file-not-found.
- Implemented `DotnetPassthroughIntegrationTests` (2 tests): `dotnet tool list` success (exit 0), `dotnet totally-invalid-xyz123` failure (exit 1).
- Fixed `DotnetFormatFilter` to handle .NET 10 SDK output formats: added `WarningFilePatternSdk10` and `FormattedFilePatternSdk10`.
- Fixed passthrough routing in `Program.cs`: pre-intercept unrecognized `dotnet <subcommand>` before Spectre.Console fails on them.
- All 247 tests pass (17 Domain + 22 Infrastructure + 159 Application + 49 Integration).

### File List

- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetFormatIntegrationTests.cs` (new)
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetNugetIntegrationTests.cs` (new)
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPassthroughIntegrationTests.cs` (new)
- `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs` (modified — added SDK-10 patterns)
- `src/DotnetTokenKiller.Cli/Program.cs` (modified — passthrough pre-intercept)

## Change Log

- 2026-03-15: Implemented story — 3 new test classes (9 tests), DotnetFormatFilter SDK-10 compatibility, passthrough routing fix in Program.cs. All 247 tests pass.

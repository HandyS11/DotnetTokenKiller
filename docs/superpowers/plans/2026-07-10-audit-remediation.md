# Audit Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix every finding from the 2026-07-10 project audit (report: <https://claude.ai/code/artifact/1fe49fc1-d049-48fa-b7a0-ee41356016b9>), ordered by user-visible impact.

**Architecture:** Seven independently shippable phases. Phase 0 makes the product loop actually run (hook protocol). Phase 1 kills the "✓ on failure / silent failure" class structurally (exit code into filters + English-forced child output). Phases 2–6 repair parsers, CLI, infrastructure, integrators, and CI/repo hygiene. Phase 7 hardens tests that would have caught all of this. Each phase = one branch + PR against `develop`.

**Tech Stack:** net10.0, Spectre.Console(.Cli), xunit + FluentAssertions + Verify.Xunit 28, Microsoft.Data.Sqlite, Python 3 (hook scripts), GitHub Actions.

## Global Constraints

- `TreatWarningsAsErrors` is ON. Known analyzer traps: CA1305 (`sb.AppendLine(CultureInfo.InvariantCulture, $"...")`), RCS1201 (chain consecutive `AppendLine`), S6580 (pass `CultureInfo.InvariantCulture` to `TryParse`), CA1050/RCS1110/S3903 (every type in a named namespace, tests included), RCS1118 (prefer `const` for literals).
- Build/test through dtk: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`. Single test: `dtk dotnet test --filter "FullyQualifiedName~<Class>.<Method>"`.
- Package versions live ONLY in `Directory.Packages.props`; `.csproj` files omit versions.
- File-scoped namespaces, `var` preferred, `_camelCase` private fields, async methods end in `Async`, LF only, no BOM, 4-space C# indent.
- Snapshot tests: Verify.Xunit v28 — static `Verifier.Verify(result)`, snapshots in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`. **Filter output changes WILL break `.verified.txt` files.** Review each diff deliberately, then accept by copying `.received.txt` over `.verified.txt`. Never blind-accept.
- Line numbers below are from the audit (commit `f2e5125`); treat as approximate — always locate by the quoted code, not the number.
- Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before each commit (or rely on the pre-commit hook).
- Commit style: `fix|feat|refactor|test|ci|docs: <summary>`.

---

## Phase 0 — Ship-stopper: make the hook protocol valid

*The whole product loop is dead until this ships. Smallest possible diff, fastest release.*

### Task 1: Fix the repo's own hook script

**Files:**

- Modify: `.claude/hooks/dotnet-to-dtk.py`

**Interfaces:**

- Produces: hook stdout schema `{"hookSpecificOutput": {"hookEventName": "PreToolUse", "updatedInput": {"command": <rewritten>}}}` — Task 2 must generate the identical schema.

- [ ] **Step 1: Rewrite the script**

Replace the entire file content with:

```python
#!/usr/bin/env python3
"""Claude Code PreToolUse hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

Reads the Bash tool input from stdin (JSON with a "command" field) and, when a
qualifying dotnet command is found, emits the PreToolUse `updatedInput` payload
so Claude Code executes the rewritten command. Prints nothing when no rewrite
is needed.
"""

import json
import re
import sys

_DTK_SUBCOMMANDS = ("build", "clean", "format", "restore", "test")

_PATTERN = re.compile(r"\bdotnet\s+(" + "|".join(_DTK_SUBCOMMANDS) + r")\b")

# Characters that may legitimately precede the `dotnet` token at a command
# boundary. Anything else (a slash, a quote, a letter) means we are inside a
# path, a string literal, or another word — do not rewrite.
_BOUNDARY_CHARS = " \t;&|({`\n"


def _inside_quotes(command: str, index: int) -> bool:
    """Best-effort check: is `index` inside an unclosed ' or " region?"""
    return (command.count('"', 0, index) % 2 == 1) or (command.count("'", 0, index) % 2 == 1)


def rewrite(command: str) -> str:
    """Prefix matching `dotnet <sub>` invocations with `dtk`, unless already prefixed."""

    def _replace(match: re.Match) -> str:
        start = match.start()
        if start > 0 and command[start - 1] not in _BOUNDARY_CHARS:
            return match.group(0)  # path like /usr/lib64/dotnet/dotnet or ./dotnet
        if _inside_quotes(command, start):
            return match.group(0)  # e.g. git commit -m "fix dotnet build"
        preceding = command[:start].rstrip()
        last_token = preceding.split()[-1] if preceding else ""
        if last_token in ("dtk", "dtk.exe"):
            return match.group(0)
        return f"dtk dotnet {match.group(1)}"

    return _PATTERN.sub(_replace, command)


def main() -> None:
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return

    tool_input = payload.get("tool_input", {})
    command = tool_input.get("command", "")

    if not command:
        return

    rewritten = rewrite(command)

    if rewritten != command:
        tool_input["command"] = rewritten
        print(json.dumps({
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "updatedInput": tool_input,
            }
        }))
    # No output on the no-change path: Claude Code proceeds normally.


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Verify all behaviors from the shell**

Run each line; compare against the expected output exactly:

```bash
H=.claude/hooks/dotnet-to-dtk.py
echo '{"tool_input":{"command":"dotnet build X.slnx"}}' | python3 $H
# -> {"hookSpecificOutput": {"hookEventName": "PreToolUse", "updatedInput": {"command": "dtk dotnet build X.slnx"}}}
echo '{"tool_input":{"command":"dotnet format X.slnx"}}' | python3 $H
# -> rewritten (format is now covered)
echo '{"tool_input":{"command":"dtk dotnet test"}}' | python3 $H
# -> (no output)
echo '{"tool_input":{"command":"git commit -m \"fix dotnet build output\""}}' | python3 $H
# -> (no output — quoted string untouched)
echo '{"tool_input":{"command":"/usr/lib64/dotnet/dotnet test"}}' | python3 $H
# -> (no output — path untouched)
echo '{"tool_input":{"command":"cd src && dotnet build && dotnet test"}}' | python3 $H
# -> both occurrences rewritten
echo '{"tool_input":{"command":"dotnet run --project x"}}' | python3 $H
# -> (no output — run not in the set)
```

- [ ] **Step 3: End-to-end proof in a live Claude Code session**

In a fresh Claude Code session in this repo, run a Bash `dotnet clean --help` and confirm `~/.local/share/dtk/tracking.db` mtime updates (`stat -c '%y' ~/.local/share/dtk/tracking.db`).

- [ ] **Step 4: Commit**

```bash
git add .claude/hooks/dotnet-to-dtk.py
git commit -m "fix: emit valid PreToolUse updatedInput schema from Claude hook"
```

### Task 2: Fix the hook template shipped by `dtk integrate claude`

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (hook template string, lines ~76–132; the broken print is at ~125)
- Modify: `src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs` (lines ~44–104 — extract shared `rewrite()` logic)
- Create: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs`

**Interfaces:**

- Produces: `internal static class HookScriptTemplates` with `internal static string ClaudeHook { get; }` and `internal static string GeminiHook { get; }` — both built from one shared Python `rewrite()` body (identical to Task 1's), differing only in the output JSON section (Claude: `hookSpecificOutput.updatedInput`; Gemini: keep its existing, already-correct `hookSpecificOutput.tool_input`).

- [ ] **Step 1: Write the failing test**

Add to `ClaudeCodeIntegratorTests.cs` (follow the file's existing arrange/act style for running the integrator into a temp dir):

```csharp
[Fact]
public async Task IntegrateAsync_WritesHookEmittingUpdatedInputSchemaAsync()
{
    var hookPath = await IntegrateIntoTempDirAndGetHookPathAsync(); // reuse the test class's existing helper pattern
    var script = await File.ReadAllTextAsync(hookPath);

    script.Should().Contain("hookSpecificOutput");
    script.Should().Contain("updatedInput");
    script.Should().NotContain("\"decision\"");
    script.Should().Contain("format"); // subcommand set includes format
}
```

- [ ] **Step 2: Run it — expect FAIL** (`NotContain("\"decision\"")` and/or `Contain("updatedInput")` fail)

```bash
dtk dotnet test --filter "FullyQualifiedName~ClaudeCodeIntegratorTests"
```

- [ ] **Step 3: Implement**

Create `HookScriptTemplates.cs` holding the shared Python body (Task 1's script verbatim for Claude; Gemini variant swaps only the `print(...)` block for its existing `hookSpecificOutput.tool_input` shape). Point both integrators' file-write calls at these properties and delete their inline duplicates.

- [ ] **Step 4: Execute the generated scripts against real payloads**

After `dtk integrate claude` into a temp dir, pipe the Step-2 shell cases from Task 1 through the generated file — same expected outputs. Run the Gemini integrator test suite too:

```bash
dtk dotnet test --filter "FullyQualifiedName~GeminiCliIntegratorTests"
```

- [ ] **Step 5: Full test run + commit**

```bash
dtk dotnet test DotnetTokenKiller.slnx
git add -A src/ tests/
git commit -m "fix: ship valid Claude hook schema; dedupe hook script templates"
```

### Task 3 (local machine, not repo): rtk coexistence

**Files:**

- Modify: `~/.config/rtk/config.toml` (user's machine — NOT committed)

- [ ] **Step 1:** Set `exclude_commands = ["dotnet"]` under `[hooks]`.
- [ ] **Step 2:** Verify: `rtk hook check "dotnet build foo.sln"` → `No rewrite for: dotnet build foo.sln`; `rtk hook check "git status"` → still rewrites.
- [ ] **Step 3 (optional product follow-up, backlog):** teach `dtk integrate claude` to detect an `rtk hook claude` entry in the merged Claude settings and print the `exclude_commands` suggestion.

---

## Phase 1 — Trust: no filter may ever claim success on failure

### Task 4: Pass the exit code into filters; raw-tail fallback

**Files:**

- Modify: `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` (~line 8)
- Modify: all five `src/DotnetTokenKiller.Application/Filters/Dotnet*Filter.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` (~lines 58–66)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`, all five `tests/.../Filters/Dotnet*FilterTests.cs`

**Interfaces:**

- Produces: `string Apply(string rawOutput, int exitCode);` on `IOutputFilter` (breaking change, update every implementor + test caller). `FilteredRunUseCase` post-rule: `exitCode != 0 && string.IsNullOrWhiteSpace(filtered)` ⇒ emit `✗ {command} failed (exit {exitCode})` + last 40 raw lines + tee hint.

- [ ] **Step 1: Write the failing tests**

```csharp
// FilteredRunUseCaseTests.cs
[Fact]
public async Task RunAsync_FailedCommandWithUnparsedOutput_EmitsRawTailAsync()
{
    // Arrange a fake ICommandRunner returning ExitCode=1 and output the filter won't match,
    // e.g. "MSBUILD : error MSB1009: Project file does not exist."
    var output = await RunUseCaseAsync(exitCode: 1, rawOutput: "MSBUILD : error MSB1009: Project file does not exist.");

    output.Should().NotBeNullOrWhiteSpace();      // the old behavior returned ""
    output.Should().Contain("MSB1009");           // the raw tail must surface the reason
    output.Should().Contain("exit 1");
}

// DotnetCleanFilterTests.cs
[Fact]
public void Apply_ProjectNameContainingFailed_WithZeroExit_ReportsSuccess()
{
    var raw = "  FailedRequestTests -> /repo/bin/Debug/FailedRequestTests.dll\nBuild succeeded.";
    var result = new DotnetCleanFilter().Apply(raw, exitCode: 0);
    result.Should().StartWith("✓");
}

[Fact]
public void Apply_LocalizedFailure_WithNonZeroExit_DoesNotReportSuccess()
{
    var raw = "Échec de la génération.";           // French SDK: no English keywords at all
    var result = new DotnetCleanFilter().Apply(raw, exitCode: 1);
    result.Should().NotContain("✓");
}
```

- [ ] **Step 2: Run — expect compile FAIL** (signature doesn't exist yet), then behavioral FAIL after mechanical signature update.

- [ ] **Step 3: Implement**

1. Change the interface: `string Apply(string rawOutput, int exitCode);`
2. Mechanically thread `exitCode` through all five filters; inside each, replace text-based success detection with: success ⇔ `exitCode == 0` (keep text parsing for *content*, never for the verdict). The `✓` branch requires `exitCode == 0`; any non-zero exit routes to the error-rendering branch even if zero diagnostics parsed.
3. In `FilteredRunUseCase` after filtering, add the fallback (private helper):

```csharp
private static string BuildRawTailFallback(string command, int exitCode, string rawOutput, string? logHint)
{
    var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var tail = string.Join('\n', lines.TakeLast(40));
    var sb = new StringBuilder();
    sb.AppendLine(CultureInfo.InvariantCulture, $"✗ {command} failed (exit {exitCode})")
      .AppendLine(tail);
    if (logHint is not null)
    {
        sb.AppendLine(logHint);
    }
    return sb.ToString();
}
```

- [ ] **Step 4: Run the full Application test suite; review snapshot diffs deliberately**

```bash
dtk dotnet test --filter "FullyQualifiedName~DotnetTokenKiller.Application.Tests"
```

- [ ] **Step 5: Commit**

```bash
git add -A src/ tests/
git commit -m "fix: filters receive exit code; failed runs can never print success or emit nothing"
```

### Task 5: Force English child-process output

**Files:**

- Modify: `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs` (both `RunCapturedAsync` ~line 17 and `RunPassthroughAsync` setup)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task RunCapturedAsync_SetsEnglishCliLanguageOnChildAsync()
{
    // Child echoes the env var back (cross-platform via dotnet's own host is overkill;
    // use /usr/bin/env on Linux, matching this file's existing platform-conditional test style):
    var result = await _runner.RunCapturedAsync("printenv", ["DOTNET_CLI_UI_LANGUAGE"], CancellationToken.None);
    result.StdOut.Trim().Should().Be("en");
}
```

- [ ] **Step 2: Run — expect FAIL** (empty output, exit 1).

- [ ] **Step 3: Implement** — where `ProcessStartInfo` is built (captured path only is not enough; do both paths):

```csharp
psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
```

Also set, in the captured path (fixes the Windows code-page mojibake finding at the same spot):

```csharp
psi.StandardOutputEncoding = Encoding.UTF8;
psi.StandardErrorEncoding = Encoding.UTF8;
```

- [ ] **Step 4: Run + commit**

```bash
dtk dotnet test --filter "FullyQualifiedName~ProcessCommandRunnerTests"
git add -A src/ tests/
git commit -m "fix: force en-US CLI output and UTF-8 pipe encoding for child processes"
```

---

## Phase 2 — Parser repairs (one task per filter, fixtures first)

*Pattern for every task in this phase: add a real-world fixture line the current regex misses → failing assertion → widen the regex → snapshot review → commit. All fixture examples below were verified failures in the audit.*

### Task 6: DotnetBuildFilter regexes

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` (`DiagnosticPattern` ~252, `SimpleDiagnosticPattern` ~257, dead noise patterns ~296–302)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` + `Fixtures/dotnet_build_errors.txt`

- [ ] **Step 1: Failing tests**

```csharp
[Theory]
[InlineData("/repo/Tests.cs(10,5): warning xUnit1013: Public method should be marked as test [/repo/T.csproj]", "xUnit1013")]
[InlineData("/repo/App.cs(3,9): error CS0029: Cannot implicitly convert type 'string[]' to 'int' [/repo/A.csproj]", "'string[]' to 'int'")]
[InlineData("C:\\Program Files (x86)\\proj\\App.cs(1,1): error CS1002: ; expected [C:\\p\\A.csproj]", "CS1002")]
public void Apply_HardDiagnosticShapes_AreCapturedInFull(string line, string mustContain)
{
    var result = new DotnetBuildFilter().Apply($"{line}\nBuild FAILED.\n", exitCode: 1);
    result.Should().Contain(mustContain);
    result.Should().NotContain("✓");
}
```

- [ ] **Step 2: Run — expect FAIL** on all three rows.

- [ ] **Step 3: Fix the patterns**

- Code group: `[A-Z]+\d+` → `[A-Za-z]+\d+` (both patterns).
- File group: `[^()]+` → match lazily up to the `(line,col)` anchor instead of banning parens: `(?<file>.+?)\((?<line>\d+),(?<col>\d+)\)`.
- Message group: `[^\[]+?` → capture greedily and anchor the optional project suffix at end-of-line: `(?<message>.+?)(?:\s\[(?<project>[^\]]+)\])?$`.
- Delete the unreachable `NoiseProjectOutputPattern` / `NoiseTimeElapsedPattern` (~296–302) and the `IsNoiseLine` branches that reference them.
- While in the file: show project-count/elapsed context on the error path too (audit: dropped only there, ~140 vs ~134), and fix the misleading hint at ~148 to say `-v -v` (it becomes accurate after Task 11 repairs the flag).

- [ ] **Step 4: Run tests, review snapshot diffs, accept intentionally. Commit**

```bash
git commit -am "fix: build filter parses mixed-case codes, bracketed messages, and parenthesised paths"
```

### Task 7: DotnetTestFilter — modern durations, MTP summary, zero-project guard, skipped runs

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (`FailedTestHeaderPattern` ~250, `SummaryPattern` ~245, `FormatOutput` ~167, skipped-run message ~162, duration parse ~153)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` + new fixture `Fixtures/dotnet_test_mtp_failures.txt`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void Apply_SlowFailingTest_KeepsFailureDetail()
{
    var raw = "  Failed MyTests.SlowTest [1 s]\n  Error Message:\n   Expected 1 but was 2.\nFailed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - MyTests.dll";
    var result = new DotnetTestFilter().Apply(raw, exitCode: 1);
    result.Should().Contain("SlowTest").And.Contain("Expected 1 but was 2");
}

[Fact]
public void Apply_DotNet9MtpSummary_IsParsed()
{
    var raw = "failed MyTests.T1 (12ms)\nTest summary: total: 10, failed: 1, succeeded: 9, skipped: 0, duration: 2.3s";
    var result = new DotnetTestFilter().Apply(raw, exitCode: 1);
    result.Should().Contain("failed").And.NotBeNullOrWhiteSpace();
}

[Fact]
public void Apply_FailuresParsedButNoSummary_StillReportsFailures()
{
    var raw = "  Failed MyTests.T1 [15 ms]\n  Error Message:\n   boom";  // test host crashed before summary
    var result = new DotnetTestFilter().Apply(raw, exitCode: 1);
    result.Should().Contain("T1").And.Contain("boom");                   // old code returned ""
}

[Fact]
public void Apply_AllSkipped_ReportsSkippedNotZeroFound()
{
    var raw = "Passed!  - Failed:     0, Passed:     0, Skipped:     5, Total:     5, Duration: 10 ms - T.dll";
    var result = new DotnetTestFilter().Apply(raw, exitCode: 0);
    result.Should().Contain("5 skipped").And.NotContain("0 tests found");
}
```

- [ ] **Step 2: Run — expect all four FAIL.**

- [ ] **Step 3: Implement**

- Duration in failure header: `\[(?<dur>[\d.]+ (?:ms|s|m(?: \d+ s)?))\]` (accept `ms`, `s`, `m … s`).
- Add a second summary regex for MTP: `^Test summary: total: (?<total>\d+), failed: (?<failed>\d+), succeeded: (?<passed>\d+), skipped: (?<skipped>\d+)` and a failure-line regex `^failed (?<name>\S+)`.
- `FormatOutput` ~167: remove the `ProjectCount == 0 → return ""` early-out; if failures exist, always render them; with Task 4 in place a non-zero exit can no longer return empty anyway.
- Skipped-run message ~162: when `Total > 0 && Passed == 0 && Failed == 0`, render `✓ dotnet test: {Skipped} skipped, 0 executed` instead of "0 tests found".

- [ ] **Step 4: Snapshot review, full filter suite, commit**

```bash
dtk dotnet test --filter "FullyQualifiedName~DotnetTestFilterTests"
git commit -am "fix: test filter handles second-scale durations, MTP summaries, crashed hosts, skipped-only runs"
```

### Task 8: DotnetRestoreFilter — durations ≥ 1 s and MSB error codes

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` (`RestoredPattern` ~153, error patterns ~165–170)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Theory]
[InlineData("  Restored /repo/App.csproj (in 1.02 sec).")]
[InlineData("  Restored /repo/App.csproj (in 1 min 5 sec).")]
public void Apply_SlowRestore_CountsProject(string line)
{
    var result = new DotnetRestoreFilter().Apply(line + "\n", exitCode: 0);
    result.Should().Contain("1 project");   // old code: empty string
}

[Fact]
public void Apply_MsbError_IsSurfaced()
{
    var raw = "MSBUILD : error MSB1009: Project file does not exist.";
    var result = new DotnetRestoreFilter().Apply(raw, exitCode: 1);
    result.Should().Contain("MSB1009");     // old code: empty string
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement** — duration group: `\(in (?<dur>[\d.]+ (?:ms|sec)|(?:\d+ min)(?: \d+(?:\.\d+)? sec)?)\)`; error pattern: widen `NU\d+` to `(?:NU|MSB)\d+`. Drop the per-project elapsed *sum* (audit: overstates wall clock under parallel restore) — report project count only, or the max.

- [ ] **Step 4: Snapshot review + commit**

```bash
git commit -am "fix: restore filter parses second-scale durations and MSB-class failures"
```

### Task 9: Cross-filter duration polish (build/test elapsed multi-part)

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` (~153, summary `Duration:` parse), `DotnetBuildFilter.cs` (`Time Elapsed` ~265 — verify it already handles `hh:mm:ss.ff`; fix only if not)
- Test: extend the Theory rows in each filter's test file with `Duration: 1 m 2 s` expecting the full duration echoed, not `1 m`.

- [ ] **Step 1–4:** same TDD cycle; commit `fix: parse multi-part test durations`.

---

## Phase 3 — CLI correctness

### Task 10: `gain --json` / `--export csv` bypass Spectre wrapping

**Files:**

- Modify: `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` (~54 CSV, ~64 JSON)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs`

**Interfaces:**

- Consumes: the DI-registered `TextWriter` singleton (`Application/DependencyInjection.cs` ~28 registers `Console.Out`) — inject it into `GainCommand`'s constructor alongside the existing dependencies.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task Gain_Json_SurvivesNarrowConsoleAsync()
{
    // Use the existing TestConsole harness but force Width = 80 (the redirect default),
    // seed the tracker with a record whose command string is > 80 chars.
    var (output, _) = await RunGainAsync("--json", consoleWidth: 80, seedLongCommand: true);
    var act = () => JsonDocument.Parse(output);
    act.Should().NotThrow();   // old behavior: newline injected inside a JSON string
}

[Fact]
public async Task Gain_ExportCsv_HeaderIsSingleLineAsync()
{
    var (output, _) = await RunGainAsync("--export", "csv", consoleWidth: 80, seedLongCommand: true);
    output.Split('\n')[0].Should().Contain(",");           // complete header row
    output.Should().NotContain("\n\n");                    // no wrap artifacts
}
```

- [ ] **Step 2: Run — expect FAIL** (JSON parse throws).

- [ ] **Step 3: Implement** — for `--json` and `--export`, write via the injected `TextWriter` (`_output.WriteLine(json)`); keep `IAnsiConsole` only for the human-formatted table path. Tests inject a `StringWriter` in place of `Console.Out`.

- [ ] **Step 4: Run + commit** `fix: machine-readable gain output no longer wrapped at console width`

### Task 11: `--` handling and the `-v` flag

**Files:**

- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` (`InsertSeparator` ~74, the `args.Contains("--")` skip ~79)
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/DotnetCommandSettings.cs` (~11–12, `bool[] Verbose`)
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` (verbosity consumption ~84)
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (SKILL.md flags table ~55) and `docfx/articles/usage.md` if it documents `-v`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`, `DotnetTestIntegrationTests.cs`

- [ ] **Step 1: Failing tests** (these are the audit's exact verified failures)

```csharp
[Theory]
// user args in                                             -> what dotnet must receive
[InlineData("dotnet test --filter Category=Unit -- RunConfiguration.X=1", "test --filter Category=Unit -- RunConfiguration.X=1")]
[InlineData("dotnet test -- RunConfiguration.MaxCpuCount=4",              "test -- RunConfiguration.MaxCpuCount=4")]
[InlineData("dotnet restore --bogusopt bogusvalue -- proj.csproj",        "restore --bogusopt bogusvalue -- proj.csproj")]
public void Preprocess_UserSeparator_ForwardsEverythingVerbatim(string input, string expectedForwarded) { /* assert via the preprocessor's output array joined */ }

[Fact]
public void VerboseFlag_SingleV_ParsesAsLevel1() { /* run `dtk dotnet build -v proj` through the CommandApp harness; assert no parse error and verbosity==1 */ }

[Fact]
public void VerboseFlag_DoubleV_ParsesAsLevel2() { /* `-v -v` => 2 — or `--vv` per implementation choice below */ }
```

- [ ] **Step 2: Run — expect FAIL** (`Option 'verbose' is defined but no value has been provided`, dropped `--filter`).

- [ ] **Step 3: Implement**

- `InsertSeparator`: delete the `args.Contains("--")` early-out. Always partition dtk-owned flags (`-v`, `--vv`, `--verbose`, `--show-log`, `-q`, `--quiet`) from the head of the arg list, **stopping at the first user `--`**; emit `[sub] + dtkFlags + ["--"] + everythingElseIncludingUserSeparator`. Spectre consumes only the first `--`; `Remaining.Raw` then carries the user's `--` through verbatim.
- Replace `bool[] Verbose` with two flags: `[CommandOption("-v|--verbose")] bool Verbose` and `[CommandOption("--vv")] bool VeryVerbose`; compute `VerbosityLevel = VeryVerbose ? 2 : Verbose ? 1 : 0`. Update `FilteredRunUseCase` to take the int. Update SKILL.md/docs tables (`-v` = echo command, `--vv` = raw output dump).

- [ ] **Step 4: Run the full CLI integration suite + commit**

```bash
dtk dotnet test --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests"
git commit -am "fix: user -- separators forwarded verbatim; repair verbosity flags"
```

### Task 12: CLI small fixes (help, casing, completions)

**Files:**

- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` (`DtkOptions` set; casing ~63,77), `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs` (~21, ~81–86, ~143–146, ~180)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`, `ArgumentPreprocessorTests.cs`

- [ ] **Step 1: Failing tests** — (a) all four completion scripts contain `format`; (b) `dtk dotnet build --help` is routed to help (add `--help`/`-h` to `DtkOptions` so it stays in front of `--` and Spectre renders dtk's help instead of executing a filtered build); (c) `dtk DOTNET BUILD x` either works or fails with a *consistent* message — pick: lowercase `args[0]`/`args[1]` during preprocessing so Spectre routing always sees canonical casing.
- [ ] **Step 2–3:** Red → implement (completion lists should be generated from `ArgumentPreprocessor.KnownSubcommands` — single source of truth, per the comment at ~32 that today is a lie).
- [ ] **Step 4:** `git commit -am "fix: help routing, casing, format in shell completions"`

---

## Phase 4 — Infrastructure

### Task 13: Tee hint returns the full path

**Files:**

- Modify: `src/DotnetTokenKiller.Infrastructure/Tee/FileTeeService.cs` (~68)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tee/FileTeeServiceTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task TeeAsync_HintContainsAnOpenablePathAsync()
{
    var hint = await _service.TeeAsync("raw output", "build", CancellationToken.None);
    var path = ExtractPathFromHint(hint);          // strip "[full output: " / "]"
    File.Exists(path).Should().BeTrue();           // old hint: bare filename, File.Exists false from any other cwd
}
```

- [ ] **Step 2–3:** Red → change `Path.GetFileName(filePath)` to `filePath`.
- [ ] **Step 4:** `git commit -am "fix: tee hint prints full log path so agents can open it"`

### Task 14: SQLite — schema, race, cleanup, totals

**Files:**

- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` (CREATE TABLE ~197–227, cleanup counter ~61–66, history ~106–114, int sums ~281–290, timestamp normalization ~50)
- Modify: `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs`, `CommandGainDetail.cs` (`int` → `long` token totals)
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public async Task InitializeAsync_TwoTrackersOneFreshFile_NeitherThrowsAsync()
{
    var dbPath = Path.Combine(_tempDir, "race.db");
    using var a = new SqliteTracker(dbPath);
    using var b = new SqliteTracker(dbPath);
    var act = () => Task.WhenAll(a.RecordAsync(MakeRecord(), default), b.RecordAsync(MakeRecord(), default));
    await act.Should().NotThrowAsync();            // old: duplicate column name: success
}

[Fact]
public async Task RecordAsync_FirstInsertOfProcess_PurgesExpiredRowsAsync()
{
    await SeedRowOlderThanRetentionAsync();
    using var tracker = new SqliteTracker(_dbPath); // fresh instance = fresh process, the real CLI shape
    await tracker.RecordAsync(MakeRecord(), default);
    (await tracker.GetHistoryAsync(default)).Should().NotContain(r => r.Timestamp < RetentionCutoff);
    // old: cleanup required 50 inserts on ONE instance — structurally impossible in a one-shot CLI
}

[Fact]
public async Task LegacyDbWithoutSuccessColumn_IsMigratedAsync()
{
    await CreateLegacySchemaByHandAsync(_dbPath);   // CREATE TABLE without `success` via raw SqliteConnection
    using var tracker = new SqliteTracker(_dbPath);
    var act = () => tracker.RecordAsync(MakeRecord(), default);
    await act.Should().NotThrowAsync();
}
```

- [ ] **Step 2: Run — expect FAIL** (race test is timing-dependent; loop it 20× in the test to make the failure reliable before the fix).

- [ ] **Step 3: Implement**

1. Add `success` to `CREATE TABLE` so fresh DBs never migrate.
2. Migration: only when the pragma says the column is missing; wrap the `ALTER` in try/catch swallowing `SqliteException` whose message contains `duplicate column` (the losing racer is fine — the column exists).
3. Delete the `_insertsSinceCleanup` counter; call `CleanupAsync` once from `EnsureInitializedAsync` (a single `DELETE ... WHERE timestamp < cutoff` per process is cheap).
4. Store timestamps as `record.Timestamp.ToUniversalTime().ToString("O")`.
5. Read sums via `GetInt64`; widen the domain records to `long` (mechanical ripple through `GainReportUseCase` + tests).
6. Add `LIMIT` support to `GetHistoryAsync` (default 500) and pass it from `gain --history`.

- [ ] **Step 4: Full infra suite + commit**

```bash
dtk dotnet test --filter "FullyQualifiedName~SqliteTrackerTests"
git commit -am "fix: sqlite schema includes success column; per-process retention cleanup; long totals"
```

### Task 15: Process runner and config provider hardening

**Files:**

- Modify: `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs` (stdin ~17–22, `KillProcess` ~87–100)
- Modify: `src/DotnetTokenKiller.Infrastructure/Configuration/JsonConfigProvider.cs` (`SaveAsync` ~54–65, blanket catches ~27–30/47–50)
- Modify: `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` (~27–30 connection string)
- Modify: `src/DotnetTokenKiller.Application/UseCases/ConfigSetUseCase.cs` (~180 `ParseEnum`)
- Test: `ProcessCommandRunnerTests.cs`, `JsonConfigProviderTests.cs`, `ConfigSetUseCaseTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
// ProcessCommandRunnerTests
[Fact]
public async Task RunCapturedAsync_ChildReadingStdin_TerminatesInsteadOfHangingAsync()
{
    var task = _runner.RunCapturedAsync("cat", [], CancellationToken.None); // cat waits for stdin forever if inherited
    var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
    done.Should().Be(task);   // with stdin redirected+closed, cat sees EOF and exits immediately
}

// JsonConfigProviderTests
[Fact]
public async Task SaveAsync_IsAtomic_NoTornFileOnOverwriteAsync()
{
    await _provider.SaveAsync(MakeConfig(), default);
    // assert write goes through a temp file + File.Move(overwrite:true): after save, no *.tmp remains and file parses
    Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    (await _provider.LoadAsync(default)).Should().NotBeNull();
}

// ConfigSetUseCaseTests
[Fact]
public async Task Set_EmptyEnumValue_FailsWithFriendlyErrorAsync()
{
    var act = () => _useCase.RunAsync("tracking.tokenizer", "", default);
    await act.Should().ThrowAsync<FormatException>();   // old: IndexOutOfRangeException
}
```

- [ ] **Step 2–3:** Red → implement:

- `psi.RedirectStandardInput = true;` then `process.StandardInput.Close();` right after start (captured path only — passthrough keeps inherited stdio).
- `KillProcess`: catch `(InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)`.
- `SaveAsync`: write to `path + ".tmp"`, then `File.Move(tmp, path, overwrite: true)`.
- Catches in load/save/tee: `catch (Exception ex) when (ex is not OperationCanceledException)`.
- Connection string via `new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()`; normalize the three env-var reads (`DTK_DB_PATH`, `DTK_CONFIG_PATH`, `DTK_TEE_DIR`) to one shared `IsNullOrWhiteSpace`+`Trim` helper.
- `ParseEnum`: guard `string.IsNullOrEmpty(value)` → `FormatException` before `value[0]`.

- [ ] **Step 4:** full infra + application suites, commit `fix: stdin EOF for captured children, atomic config save, robust kill/catch paths`

### Task 16: Doctor consistency

**Files:**

- Modify: `src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs` (~53–63, ~80–84)
- Modify: expose the default-path logic from `SqliteTracker` (~156) and `FileTeeService` (~145) as `public static` members consumed by DoctorCommand (kills the triplication)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/DoctorCommandTests.cs`

- [ ] **Steps:** failing test — fresh temp HOME: doctor reports missing DB dir as "will be created" (pass), not failure; paths reported by doctor equal the ones the tracker/tee actually use. Implement → run → commit `fix: doctor shares real default paths and stops false-alarming fresh installs`.

---

## Phase 5 — Integrators

### Task 17: Aider YAML merge

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Integration/AiderIntegrator.cs` (~65–68)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/AiderIntegratorTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task Integrate_ExistingReadKey_IsMergedNotShadowedAsync()
{
    await File.WriteAllTextAsync(_confPath, "read: [CONVENTIONS.md]\n");
    await _integrator.IntegrateAsync(_context, default);
    var text = await File.ReadAllTextAsync(_confPath);
    CountTopLevelKeys(text, "read").Should().Be(1);         // old: 2 keys, last-wins drops CONVENTIONS.md
    text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
}
```

- [ ] **Step 2–3:** Red → implement: parse the existing file line-wise; if a top-level `read:` exists, append the dtk file to that list (handle both `read: [a, b]` flow style and `read:\n  - a` block style — cover both in tests); only append a new `read:` key when none exists.
- [ ] **Step 4:** commit `fix: aider integration merges into existing read: key instead of shadowing it`

### Task 18: Integrator consistency batch

**Files:**

- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (~76–81 section-append vs force, ~96–98 missing end-marker, ~210 force-skip), `IntegrateCommandBase.cs` (~41 exception type, ~59–61 false hint), `ClaudeCodeIntegrator.cs`/`GeminiCliIntegrator.cs` (~16/18 hook command string), the 6 pasted instruction blocks → one shared constant
- Test: `IntegratorHelpersTests.cs`, `IntegrateCommandTests.cs`

- [ ] **Step 1: Failing tests** — (a) `--force` never prints "skipped … (use --force to overwrite)"; (b) deleting only the `<!-- /dtk -->` end marker + re-integrate preserves user content after the begin marker (old: truncated); (c) section-append without `--force` on an existing user file reports what it did honestly ("updated" only when it actually should modify — decide: skip-unless-force, matching `WriteFileAsync`'s contract, and assert that); (d) hook command registered as `python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py` (and document the Windows `python` caveat in the integrator's console output).
- [ ] **Step 2–3:** Red → implement; also: extract the 6× instructions markdown into one `IntegrationInstructions.Markdown` constant; change `IntegrateUseCase.RunAsync`'s unknown-provider throw to `InvalidOperationException` to match the catch in `IntegrateCommandBase` (or catch `ArgumentException` — pick one, test it via a deliberately unknown provider name).
- [ ] **Step 4:** commit `fix: honest force/skip semantics, marker-loss safety, robust hook registration path`

### Task 19 (optional, refactor): collapse the 7 `*IntegrateCommand` shells

**Files:**

- Create: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs` (single command, provider argument validated against `IntegrateUseCase.AvailableProviders` — which finally gets a production caller)
- Delete: the 7 per-provider command files; update `Program.cs` registrations; keep `dtk integrate claude` CLI syntax identical (provider as argument, not subcommand — verify help output).
- Test: `IntegrateCommandTests.cs` — every provider name routes; unknown name lists available providers.

- [ ] **Steps:** failing routing test → implement → full CLI suite → commit `refactor: single integrate command with provider argument`. *Skip if Phase-5 time is constrained; nothing depends on it.*

---

## Phase 6 — CI & repo hygiene (no C# — single PR of config diffs)

### Task 20: Workflow fixes

**Files:**

- Modify: `.github/workflows/ci.yml`, `codeql.yml`, `mutation-testing.yml`, `publish.yml`, `sonarqube.yml`, `doc-publish.yml`
- Delete: `.github/workflows/quality-gate.yml`
- Modify: `.github/dependabot.yml`

- [ ] **Step 1:** In every workflow's `actions/setup-dotnet@v4` step add:

```yaml
        with:
          global-json-file: global.json
```

- [ ] **Step 2:** Delete `quality-gate.yml` (fully redundant with ci.yml; if the TRX upload or the `Category!=Integration` fast lane is wanted, move that single step into ci.yml instead — decide in PR).
- [ ] **Step 3:** `doc-publish.yml`: add `branches: [develop]` to the `on.push` trigger; remove the leftover "per Constitution I" comment (~line 37); replace `dotnet tool install -g docfx` with `dotnet tool restore` + `dotnet docfx` (pins 2.78.5 from `dotnet-tools.json`).
- [ ] **Step 4:** `codeql.yml`: add `pull_request: { branches: [develop] }` trigger.
- [ ] **Step 5:** `dependabot.yml`: add grouping:

```yaml
    groups:
      nuget-all:
        patterns: ["*"]
```

- [ ] **Step 6:** Validate all edited workflows parse: `for f in .github/workflows/*.yml; do python3 -c "import yaml,sys; yaml.safe_load(open('$f'))" || echo "BROKEN: $f"; done`
- [ ] **Step 7:** Commit `ci: pin SDK from global.json everywhere, drop redundant quality-gate, scope docs deploys, group dependabot`

### Task 21: Repo file hygiene

**Files:**

- Modify: `.gitignore` (strip BOM), `Directory.Build.props` (version + NU190x), `.gitattributes` (duplicate `*.config text`, `*.ps1` eol), `global.json` (`allowPrerelease`), `.vscode/extensions.json`, `samples/README.md`, `docfx/articles/getting-started.md`, `CLAUDE.md`

- [ ] **Step 1:** Strip the BOM: `sed -i '1s/^\xEF\xBB\xBF//' .gitignore` then verify `od -c .gitignore | head -1` shows no `357 273 277`.
- [ ] **Step 2:** `Directory.Build.props`: bump `<Version>` to the current release line (0.5.0 — keep publish.yml's tag-time sed as the release source of truth, but stop lying locally). Remove `NU1901;NU1902;NU1903;NU1904` from `NoWarn`; run `dtk dotnet restore DotnetTokenKiller.slnx` and fix/waiver any real audit hits it surfaces (pin transitive versions in `Directory.Packages.props` if needed).
- [ ] **Step 3:** `global.json`: set `"allowPrerelease": false`.
- [ ] **Step 4:** `.gitattributes`: delete the duplicate `*.config text` line; decide `*.ps1` policy (recommend: drop the `eol=crlf` override, matching `.editorconfig`'s `lf`; PowerShell Core is LF-safe) — then renormalize: `git add --renormalize .`
- [ ] **Step 5:** `.vscode/extensions.json`: remove `formulahendry.dotnet-test-explorer`.
- [ ] **Step 6:** Docs parity: add `examples/FORMAT.md` to the `samples/README.md` table; add `dtk dotnet format` to `docfx/articles/getting-started.md`'s Supported Commands table; CLAUDE.md already claims format — becomes true after Phase 0.
- [ ] **Step 7:** Document the `.claude/skills` symlink caveat for Windows contributors in `CONTRIBUTING.md` (`git config core.symlinks true` + Developer Mode), or replace symlinks with copies if Windows contributors matter more than dedupe.
- [ ] **Step 8:** Build + full test to prove NU190x removal doesn't break `TreatWarningsAsErrors`, then commit `chore: repo hygiene — BOM, version, audit warnings, gitattributes, docs parity`

---

## Phase 7 — Test hardening (locks in everything above)

### Task 22: The missing high-value tests

**Files:**

- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Filters/LargeOutputStressTests.cs`, `tests/DotnetTokenKiller.Infrastructure.Tests/Configuration/JsonConfigProviderTests.cs`

- [ ] **Step 1: Large-output pipe test** (the runner has a comment calling this CRITICAL; nothing verifies it):

```csharp
[Fact]
public async Task RunCapturedAsync_MegabyteOnBothStreams_DoesNotDeadlockAsync()
{
    // generate ~1MB on stdout AND stderr concurrently
    var script = "head -c 1048576 /dev/zero | tr '\\0' 'a'; head -c 1048576 /dev/zero | tr '\\0' 'b' 1>&2";
    var task = _runner.RunCapturedAsync("bash", ["-c", script], CancellationToken.None);
    var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
    done.Should().Be(task);
    (await task).StdOut.Length.Should().BeGreaterThan(1_000_000);
}
```

- [ ] **Step 2: Cancellation actually kills the child** — start `bash -c "sleep 30"`, cancel after 200 ms, assert `OperationCanceledException` AND that no `sleep 30` process with the recorded PID survives (`kill -0 <pid>` fails / `Process.GetProcessById` throws).
- [ ] **Step 3: `JsonConfigProvider.Validate` coverage** — Theory over the clamps: `RetentionDays < 1`, `Width < 40`, `MaxFiles < 1`, `MaxFileSizeBytes <= 0` (and decide: `0` should be rejected, not "write empty log + lie in the hint").
- [ ] **Step 4: Missing-executable path** — `RunCapturedAsync("definitely-not-a-real-binary-xyz", ...)` asserts a friendly exception (wrap the `Win32Exception` in the runner with the command name in the message — small prod change in `ProcessCommandRunner.cs:28`).
- [ ] **Step 5:** Run everything, then mutation spot-check the touched filters: `dotnet stryker` per `stryker-config.json` (accept score movement; don't chase 100%).
- [ ] **Step 6:** Commit `test: large-output, kill-verification, config validation, missing-binary coverage`

---

## Sequencing & tracking

| Phase | Ships | Depends on | PR |
|-------|-------|-----------|----|
| 0 | Working hook (repo + shipped) + rtk coexistence | — | `fix/hook-protocol` |
| 1 | Exit-code truth + English output | — (merge before 2!) | `fix/filter-exit-codes` |
| 2 | Parser repairs | Phase 1 (signature) | `fix/filter-parsers` |
| 3 | CLI correctness | — | `fix/cli-args-output` |
| 4 | Infrastructure | — | `fix/infra-hardening` |
| 5 | Integrators | Phase 0 (templates) | `fix/integrators` |
| 6 | CI/hygiene | — | `ci/hygiene` |
| 7 | Test hardening | Phases 1–4 | `test/hardening` |

Definition of done per phase: `dtk dotnet build DotnetTokenKiller.slnx` clean, `dtk dotnet test DotnetTokenKiller.slnx` green, `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` clean, snapshot diffs reviewed intentionally, PR against `develop`.

**Explicitly out of scope** (audit findings judged not worth the change): surrogate-pair edge in `Truncate`, tee byte-vs-char truncation beyond the `0`-rejection above, `CommandGainDetail` recursive-record redesign, ANSI private-mode sequences, tee log file permissions (document instead), `DependencyInjection.cs` file rename, redundant `[JsonSerializable]` attribute, reflection-based private-static tests. Revisit only if they bite.

**Deferred decision — dead config surface:** `display.colors` and `display.width` are settable/validated/persisted but consumed by no rendering code. Decide during Phase 3: either wire them into the human-readable `gain` table rendering, or remove them from `DtkConfig` + `config set` with a release note. Don't leave them lying.

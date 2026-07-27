# Filter Verdict Invariant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `DotnetTestFilter` reporting "0 tests found" when tests actually ran, and add regression tests locking the same invariant across all five filters.

**Architecture:** A single guard in `DotnetTestFilter.FormatOutput` currently lets a per-assembly "no tests" signal override accumulated results. The fix requires that no positive evidence exists before any "nothing happened" verdict is emitted, and downgrades the genuine zero case from `✓` to `⚠`. `FilteredRunUseCase.NormalizeGlyphs` learns the new glyph. The other four filters get one invariant test each.

**Tech Stack:** net10.0, xunit, FluentAssertions, NSubstitute, Verify (snapshot testing), Stryker (mutation testing), Spectre.Console.Cli.

**Spec:** [`docs/superpowers/specs/2026-07-27-test-filter-verdict-invariant-design.md`](../specs/2026-07-27-test-filter-verdict-invariant-design.md)

## Global Constraints

- Target framework `net10.0`; `TreatWarningsAsErrors` is on — every analyzer warning breaks the build.
- File-scoped namespaces; `var` preferred; private fields `_camelCase`; async methods end in `Async`.
- LF line endings only, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Package versions live in `Directory.Packages.props` only — never add a version to a `.csproj`. **No new packages are needed by this plan.**
- All filter output strings end with an explicit `\n`, never `Environment.NewLine`.
- Exit codes are never altered by a filter. `FilteredRunUseCase` returns `result.ExitCode` verbatim; this plan does not touch that.

### Running tests — read this before Task 1

`dtk dotnet test DotnetTokenKiller.slnx --filter …` **is the bug this plan fixes** and cannot be trusted to prove RED — it reports `0 tests found` with exit 0 regardless. Always target the test `.csproj` directly:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~TestName"
```

Full suite (no `--filter`, so unaffected by the bug):

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

### Accepting a Verify snapshot

A `Verify(...)` test fails on first run and writes `<TestClass>.<TestName>.received.txt` beside the `.verified.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`. Accept it by replacing the verified file:

```bash
cd tests/DotnetTokenKiller.Application.Tests/Snapshots
mv DotnetTestFilterTests.<TestName>.received.txt DotnetTestFilterTests.<TestName>.verified.txt
```

Never hand-edit a `.verified.txt` to match code — run the test and accept its output, so the file always reflects real filter output.

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Condense `dotnet test` output | Fix the verdict guard (Task 2) |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Run, filter, track, print | Map `⚠` → `WARN:` (Task 1) |
| `tests/…/Fixtures/dotnet_test_multiproject_partial_match.txt` | Real captured multi-project output | New (Task 2) |
| `tests/…/Filters/DotnetTestFilterTests.cs` | `test` filter tests | New tests + 2 updated assertions (Task 2) |
| `tests/…/UseCases/FilteredRunUseCaseTests.cs` | Use-case tests | New glyph tests (Task 1) |
| `tests/…/Filters/Dotnet{Restore,Format,Build,Clean}FilterTests.cs` | Sibling filter tests | One invariant test each (Task 3) |
| `tests/…/Snapshots/*.verified.txt` | Snapshot baselines | One updated, one new (Task 2) |

Tasks are ordered so the repository is green and internally consistent at every commit. Task 1 comes first so no commit exists in which a `⚠` can reach a `NO_COLOR` user unmapped.

---

### Task 1: Teach glyph normalization the warning glyph

`⚠` does not appear anywhere in the codebase yet. `NormalizeGlyphs` maps only `✓` and `✗`, so without this task the `NO_COLOR` / `display.emoji = false` paths would leak a raw glyph once Task 2 lands.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs:140-147`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: the contract `⚠` → `WARN:` applied to all filter output when emoji are disabled or `NO_COLOR` is set. Task 2 relies on this mapping existing.

- [ ] **Step 1: Write the two failing tests**

Append to `FilteredRunUseCaseTests.cs`, inside the existing class. `_runner`, `_tracker`, `_teeService`, `_filter`, and `BuildArgs` are existing members of that class — do not redeclare them.

```csharp
    [Fact]
    public async Task RunAsync_ReplacesWarnGlyph_WhenDisplayEmojiDisabled()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        var config = DtkConfig.Default with
        {
            Display = new DisplayConfig(Emoji: false)
        };
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("⚠ dotnet test: 0 tests found\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        var output = writer.ToString();
        output.Should().NotContain("⚠");
        output.Should().Contain("WARN: dotnet test: 0 tests found");
    }

    [Fact]
    public async Task RunAsync_NoColorEnvVar_ReplacesWarnGlyphWithWarn()
    {
        // Kills the string mutation "⚠" → "" on the NO_COLOR branch.
        var saved = Environment.GetEnvironmentVariable("NO_COLOR");
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        try
        {
            await using var writer = new StringWriter();
            var configProvider = Substitute.For<IConfigProvider>();
            configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
            var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

            _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult("output", "", 0));
            _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("⚠ nothing matched\n");
            _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns((string?)null);

            await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

            var output = writer.ToString();
            output.Should().NotContain("⚠");
            output.Should().Contain("WARN: nothing matched");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", saved);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~WarnGlyph"
```

Expected: 2 tests, both FAIL. The failure is on `output.Should().NotContain("⚠")` — the glyph passes through unmapped.

- [ ] **Step 3: Add the mapping**

In `FilteredRunUseCase.NormalizeGlyphs`, extend the replacement chain:

```csharp
        return filtered
            .Replace("✓", "ok:", StringComparison.Ordinal)
            .Replace("✗", "FAIL:", StringComparison.Ordinal)
            .Replace("⚠", "WARN:", StringComparison.Ordinal);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~WarnGlyph"
```

Expected: 2 tests, both PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs \
        tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs
git commit -m "feat: normalize the warning glyph to WARN: when emoji are disabled"
```

---

### Task 2: Fix the test-filter verdict guard

The bug itself. `state.ZeroTestsFound` is set by any single per-assembly "No test matches…" line and OR'd ahead of the accumulated totals, so one non-matching assembly discards every other assembly's results.

**Files:**
- Create: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_multiproject_partial_match.txt`
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs:266-271`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` (add tests; update lines 72, 330, 596)
- Modify: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetTestFilterTests.Apply_ZeroTestsFixture_MatchesSnapshot.verified.txt`
- Create: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetTestFilterTests.Apply_MultiProjectPartialMatch_MatchesSnapshot.verified.txt`

**Interfaces:**
- Consumes: the `⚠` → `WARN:` mapping from Task 1.
- Produces: two output strings later tasks and docs may reference —
  `"⚠ dotnet test: 0 tests found (no assembly matched)\n"` (explicit no-match pattern seen) and
  `"⚠ dotnet test: 0 tests found\n"` (all-zero summary, no explicit pattern).

- [ ] **Step 1: Create the fixture**

This is the real captured output from the reproduction, with absolute paths rewritten to `/test/project/root` to match the existing fixtures and the `_sut` root. Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_multiproject_partial_match.txt` with exactly this content:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  DotnetTokenKiller.Domain -> /test/project/root/src/DotnetTokenKiller.Domain/bin/Debug/net10.0/DotnetTokenKiller.Domain.dll
  DotnetTokenKiller.Infrastructure -> /test/project/root/src/DotnetTokenKiller.Infrastructure/bin/Debug/net10.0/DotnetTokenKiller.Infrastructure.dll
  DotnetTokenKiller.Application -> /test/project/root/src/DotnetTokenKiller.Application/bin/Debug/net10.0/DotnetTokenKiller.Application.dll
  DotnetTokenKiller.Domain.Tests -> /test/project/root/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll
Test run for /test/project/root/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  DotnetTokenKiller.Infrastructure.Tests -> /test/project/root/tests/DotnetTokenKiller.Infrastructure.Tests/bin/Debug/net10.0/DotnetTokenKiller.Infrastructure.Tests.dll
  DotnetTokenKiller.Cli -> /test/project/root/src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll
Test run for /test/project/root/tests/DotnetTokenKiller.Infrastructure.Tests/bin/Debug/net10.0/DotnetTokenKiller.Infrastructure.Tests.dll (.NETCoreApp,Version=v10.0)
  DotnetTokenKiller.Application.Tests -> /test/project/root/tests/DotnetTokenKiller.Application.Tests/bin/Debug/net10.0/DotnetTokenKiller.Application.Tests.dll
Test run for /test/project/root/tests/DotnetTokenKiller.Application.Tests/bin/Debug/net10.0/DotnetTokenKiller.Application.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.
  DotnetTokenKiller.Cli.IntegrationTests -> /test/project/root/tests/DotnetTokenKiller.Cli.IntegrationTests/bin/Debug/net10.0/DotnetTokenKiller.Cli.IntegrationTests.dll
Test run for /test/project/root/tests/DotnetTokenKiller.Cli.IntegrationTests/bin/Debug/net10.0/DotnetTokenKiller.Cli.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
No test matches the given testcase filter `FullyQualifiedName~CopilotCliHook_ReusesSharedRewriteCore` in /test/project/root/tests/DotnetTokenKiller.Domain.Tests/bin/Debug/net10.0/DotnetTokenKiller.Domain.Tests.dll

A total of 1 test files matched the specified pattern.
No test matches the given testcase filter `FullyQualifiedName~CopilotCliHook_ReusesSharedRewriteCore` in /test/project/root/tests/DotnetTokenKiller.Infrastructure.Tests/bin/Debug/net10.0/DotnetTokenKiller.Infrastructure.Tests.dll


Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 52 ms - DotnetTokenKiller.Application.Tests.dll (net10.0)
No test matches the given testcase filter `FullyQualifiedName~CopilotCliHook_ReusesSharedRewriteCore` in /test/project/root/tests/DotnetTokenKiller.Cli.IntegrationTests/bin/Debug/net10.0/DotnetTokenKiller.Cli.IntegrationTests.dll
```

Check whether the fixtures are listed explicitly in `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj` (e.g. an `EmbeddedResource` or `Content` item per file). If they are enumerated one by one, add the new file the same way; if a wildcard covers the `Fixtures/` directory, nothing is needed. Confirm with:

```bash
grep -n "Fixtures" tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj
```

- [ ] **Step 2: Write the failing regression test**

Append to `DotnetTestFilterTests.cs`. `_sut` (constructed with `/test/project/root`) and `LoadFixture` are existing members.

The expected duration is `0.05s`: the summary reports `52 ms`, and `FormatOutput` renders `TotalDurationMs / 1000.0` with `F2`.

```csharp
    [Fact]
    public void Apply_MultiProjectPartialMatch_ReportsPassedCount()
    {
        // Regression: in a multi-project run, a per-assembly "No test matches" line must not
        // discard another assembly's real results. Ground truth for this fixture is 1 passed.
        var fixture = LoadFixture("dotnet_test_multiproject_partial_match.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().Be("✓ dotnet test: 1 passed (1 project, 0.05s)\n");
    }

    [Fact]
    public Task Apply_MultiProjectPartialMatch_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_multiproject_partial_match.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }
```

- [ ] **Step 3: Run to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~MultiProjectPartialMatch"
```

Expected: both FAIL. `Apply_MultiProjectPartialMatch_ReportsPassedCount` fails with actual `"✓ dotnet test: 0 tests found\n"` — that string *is* the bug. The snapshot test fails because no `.verified.txt` exists yet.

- [ ] **Step 4: Fix the guard**

In `DotnetTestFilter.FormatOutput`, replace these lines:

```csharp
        // Genuine "nothing to run": explicit no-tests pattern or an all-zero summary.
        var zeroTestsSignal = state.ZeroTestsFound || state is { ProjectCount: > 0, TotalPassed: 0 };
        if (exitCode == 0 && zeroTestsSignal)
        {
            return "✓ dotnet test: 0 tests found\n";
        }
```

with:

```csharp
        // A "nothing ran" verdict requires that no assembly produced evidence of a test.
        // ZeroTestsFound is per-assembly: in a multi-project run, one assembly matching nothing must
        // never override another's real results. Stated in full rather than relying on the earlier
        // skipped-only and failure branches, so the guard survives reordering and each clause is
        // individually mutation-testable.
        var noTestEvidence = state is { TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0 }
                             && state.Failures.Count == 0;
        if (exitCode == 0 && noTestEvidence && (state.ZeroTestsFound || state.ProjectCount > 0))
        {
            return state.ZeroTestsFound
                ? "⚠ dotnet test: 0 tests found (no assembly matched)\n"
                : "⚠ dotnet test: 0 tests found\n";
        }
```

- [ ] **Step 5: Run to verify the regression test passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~MultiProjectPartialMatch_ReportsPassedCount"
```

Expected: PASS.

- [ ] **Step 6: Accept the new snapshot**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~MultiProjectPartialMatch_MatchesSnapshot"
cd tests/DotnetTokenKiller.Application.Tests/Snapshots
mv DotnetTestFilterTests.Apply_MultiProjectPartialMatch_MatchesSnapshot.received.txt \
   DotnetTestFilterTests.Apply_MultiProjectPartialMatch_MatchesSnapshot.verified.txt
cd -
```

Verify the accepted file contains exactly `✓ dotnet test: 1 passed (1 project, 0.05s)` — if it says `0 tests found`, Step 4 was not applied.

- [ ] **Step 7: Update the two assertions that pin the old string**

These break by design — the message deliberately changed. Update them; do not weaken them to `Contain`.

`DotnetTestFilterTests.cs:72` in `Apply_ZeroTestsFixture_ReturnsZeroTestsMessage` — the fixture contains an explicit "No test matches" line, so the parenthetical applies:

```csharp
        _sut.Apply(fixture, exitCode: 0).Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
```

`DotnetTestFilterTests.cs:330` in `Apply_ZeroTestsFromAllSummariesZero_ReturnsZeroTestsMessage` — an all-zero summary with no explicit pattern, so no parenthetical. This pair is what distinguishes the two branches:

```csharp
        result.Should().Be("⚠ dotnet test: 0 tests found\n");
```

`DotnetTestFilterTests.cs:596` in `Apply_NoTestsPattern_SetsZeroTestsFlag` asserts `Contain("0 tests found")` and still passes unchanged. Strengthen it anyway so it pins the parenthetical branch:

```csharp
        result.Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
```

- [ ] **Step 8: Update the existing snapshot**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~Apply_ZeroTestsFixture_MatchesSnapshot"
cd tests/DotnetTokenKiller.Application.Tests/Snapshots
mv DotnetTestFilterTests.Apply_ZeroTestsFixture_MatchesSnapshot.received.txt \
   DotnetTestFilterTests.Apply_ZeroTestsFixture_MatchesSnapshot.verified.txt
cd -
```

The file should now read `⚠ dotnet test: 0 tests found (no assembly matched)`.

- [ ] **Step 9: Add clause-killing tests for the new guard**

Each test kills one mutant of the new condition. Append to `DotnetTestFilterTests.cs`:

```csharp
    [Fact]
    public void Apply_NoTestsLineWithPassedSummary_PrefersPassedCount()
    {
        // Kills the mutant that drops `noTestEvidence` from the guard: a passing summary alongside a
        // no-match line must report the pass.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll\n" +
            "Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 10 ms - A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 3 passed (1 project, 0.01s)\n");
    }

    [Fact]
    public void Apply_NoTestsLineWithSkippedSummary_ReportsSkipped()
    {
        // Kills the mutant that drops the TotalSkipped clause: skipped tests are evidence that tests
        // exist, so "0 tests found" must not win.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll\n" +
            "Passed!  - Failed:     0, Passed:     0, Skipped:     4, Total:     4, Duration: 10 ms - A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("4 skipped").And.NotContain("0 tests found");
    }

    [Fact]
    public void Apply_NoTestsLineOnly_ReportsZeroWithNoAssemblyMatched()
    {
        // Kills the mutant that drops `state.ZeroTestsFound` from the disjunction: with no summary at
        // all, ProjectCount is 0, so only ZeroTestsFound can produce the verdict.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
    }
```

- [ ] **Step 10: Run the whole test-filter suite**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~DotnetTestFilterTests"
```

Expected: all PASS. If `Apply_SkippedOnly…` (around line 1014) or any savings-percentage test fails, stop and report — that indicates the guard changed a path this plan did not intend to touch.

- [ ] **Step 11: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs \
        tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs \
        tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_test_multiproject_partial_match.txt \
        tests/DotnetTokenKiller.Application.Tests/Snapshots/
git commit -m "fix: stop a per-assembly no-match from masking real test results

DotnetTestFilter OR'd the sticky ZeroTestsFound flag ahead of the accumulated
totals, so in a multi-project run one assembly matching no tests discarded every
other assembly's results and reported a success. A zero verdict now requires that
no assembly produced evidence of a test, and the genuine zero case warns instead
of claiming success."
```

---

### Task 3: Lock the invariant across the four sibling filters

Each of the remaining filters has a "nothing happened" path. Reading them suggests all four already comply; these tests are the regression net that keeps it true. Each input pairs a "nothing happened" marker with real parsed evidence and asserts the evidence wins.

**Files:**
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetFormatFilterTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs`

**Interfaces:**
- Consumes: the invariant established in Task 2. No production code changes in this task.
- Produces: nothing consumed by later tasks.

**If any test fails,** that filter violates the invariant. Do not weaken the test — stop, report which filter and its actual output, and treat the fix as a follow-up task using Task 2's guard as the model.

- [ ] **Step 1: Restore — an "all up-to-date" marker must not suppress counted work**

`_sut` is `new DotnetRestoreFilter("/test/project/root")`. Append to `DotnetRestoreFilterTests.cs`:

```csharp
    [Fact]
    public void Apply_AllUpToDateMarkerWithRestoredProject_ReportsProjectCount()
    {
        // Invariant: the "all up-to-date" verdict is only valid when no project work was counted.
        const string input = "  All projects are up-to-date for restore.\n" +
                             "  Restored /test/project/root/src/App.csproj (in 123 ms).";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet restore (1 project, 0.12s)\n");
    }
```

- [ ] **Step 2: Format — a completion line must not suppress a violation**

`_sut` is `new DotnetFormatFilter("/test/project/root")`. Append to `DotnetFormatFilterTests.cs`:

```csharp
    [Fact]
    public void Apply_ViolationWithFormatCompleteLine_ReportsViolation()
    {
        // Invariant: "nothing to format" is only valid when no violation was parsed.
        const string input =
            "/test/project/root/src/App.cs(1,1): error WHITESPACE: Fix whitespace formatting.\n" +
            "Format complete in 2345ms.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be(
            "dotnet format: 1 violation\nsrc/App.cs(1,1): error WHITESPACE: Fix whitespace formatting.\n");
        result.Should().NotContain("nothing to format");
    }
```

- [ ] **Step 3: Build — a zero-count MSBuild summary must not suppress a diagnostic**

`_sut` is `new DotnetBuildFilter("/test/project/root")`. Append to `DotnetBuildFilterTests.cs`:

```csharp
    [Fact]
    public void Apply_WarningWithZeroCountSummaryLines_ReportsWarning()
    {
        // Invariant: a clean "✓ dotnet build" verdict is only valid when no diagnostic was parsed.
        // The trailing MSBuild count lines are noise and must not override the parsed warning.
        const string input =
            "/test/project/root/src/App.cs(9,13): warning CS0168: The variable 'x' is declared but never used\n" +
            "    0 Error(s)\n" +
            "    0 Warning(s)";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("CS0168");
        result.Should().Contain("1 warning");
        result.Should().NotStartWith("✓ dotnet build");
    }
```

- [ ] **Step 4: Clean — a noise count line must not swallow a real error**

`_sut` is `new DotnetCleanFilter()` (no root argument). Append to `DotnetCleanFilterTests.cs`:

```csharp
    [Fact]
    public void Apply_ErrorWithNoiseCountSummary_KeepsError()
    {
        // Invariant: the "N Error(s)" summary is stripped as noise, but the real error survives.
        const string input = "/test/project/root/src/App.csproj : error MSB3231: Unable to remove directory.\n" +
                             "    6 Error(s)";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("MSB3231");
        result.Should().NotContain("6 Error(s)");
    }
```

- [ ] **Step 5: Run all four**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~Apply_AllUpToDateMarkerWithRestoredProject_ReportsProjectCount|FullyQualifiedName~Apply_ViolationWithFormatCompleteLine_ReportsViolation|FullyQualifiedName~Apply_WarningWithZeroCountSummaryLines_ReportsWarning|FullyQualifiedName~Apply_ErrorWithNoiseCountSummary_KeepsError"
```

Expected: 4 tests, all PASS. If an assertion on an exact string fails only on formatting detail (a duration or a shortened path rendering differently than written here), correct the expected string to the filter's actual output — but only after confirming the *evidence* survived. A failure where the "nothing happened" verdict won is a real invariant violation, not a formatting mismatch.

- [ ] **Step 6: Commit**

```bash
git add tests/DotnetTokenKiller.Application.Tests/Filters/
git commit -m "test: lock the no-false-negative-verdict invariant across all filters"
```

---

### Task 4: Verify end-to-end and retire the workaround

Unit tests prove the filter; this task proves the shipped binary and closes out the documented workaround.

**Files:**
- No source changes expected. Possible: `CLAUDE.md` if the verification contradicts it.

**Interfaces:**
- Consumes: Tasks 1-3 complete and committed.
- Produces: the verified claim that `dtk dotnet test <slnx> --filter` works.

- [ ] **Step 1: Full suite, format check, and analyzers**

```bash
dtk dotnet test DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

Expected: all tests pass; format reports no changes needed. `TreatWarningsAsErrors` means the test run itself proves the analyzers are clean.

- [ ] **Step 2: Build the local CLI and reproduce against it**

The global `dtk` on `PATH` is a *different, older* build and will not show your fix. Put the freshly built binary first on `PATH`.

Keep `dtk` a bare token, exactly as written below. The rewrite hook skips a `dotnet <sub>` whose preceding token is exactly `dtk`, but a *path* ending in `/dtk` is not that token — invoking the binary by path yields a double-rewritten `.../dtk dtk dotnet test …`.

```bash
dtk dotnet build src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj
PATH="$PWD/src/DotnetTokenKiller.Cli/bin/Debug/net10.0:$PATH" dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliHook_ReusesSharedRewriteCore"
```

Expected: `✓ dotnet test: 1 passed (1 project, …)`. Before the fix this printed `✓ dotnet test: 0 tests found`.

- [ ] **Step 3: Confirm the genuine zero case warns**

```bash
PATH="$PWD/src/DotnetTokenKiller.Cli/bin/Debug/net10.0:$PATH" dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~ThisTestDoesNotExistAnywhere"
```

Expected: `⚠ dotnet test: 0 tests found (no assembly matched)`. Check the exit code with `echo $?` — it must match what `dotnet` itself returned (`0` for a no-match run), proving the warning did not change the verdict.

- [ ] **Step 4: Check the mutation score has not regressed**

```bash
dtk dotnet tool restore
dotnet stryker --config-file stryker-config.json
```

Compare against the badge baseline on `develop`. If the new guard introduced surviving mutants, add a clause-killing test in the style of Task 2 Step 9 rather than adding a Stryker exclusion.

- [ ] **Step 5: Retire the obsolete memory**

`~/.claude/projects/-home-cloudcli-projects-DotnetTokenKiller/memory/dtk-test-filter-quirk.md` documents a workaround this fix removes. Delete the file and remove its line from that directory's `MEMORY.md`. Do this only after Step 2 passes.

- [ ] **Step 6: Commit any remaining changes**

```bash
git status --short
```

If Steps 1-4 required no edits, there is nothing to commit — say so rather than creating an empty commit.

---

## Self-Review

**Spec coverage.** Design §"The fix" → Task 2 Step 4. §"Glyph plumbing" → Task 1. §"Invariant audit" (all four filters, with the per-filter inputs the spec specifies) → Task 3 Steps 1-4. §Testing new fixture and snapshot → Task 2 Steps 1, 6. §Testing pinned assertions → Task 2 Step 7. §Testing clause-killing tests → Task 2 Step 9. §Completion Criteria 1-2 → Task 4 Steps 2-3; 3 → Task 3; 4 → Task 4 Steps 1, 4; 5 → Task 4 Step 5. No spec requirement is unaddressed.

**Correction to the spec.** The spec lists three pinned assertions requiring update. Only two actually break: `DotnetTestFilterTests.cs:596` asserts `Contain("0 tests found")`, which still passes after the change. Task 2 Step 7 strengthens it deliberately rather than repairing it. The spec's "three" is an over-count, not a missed requirement.

**Type consistency.** `noTestEvidence` is the only new local. The two output strings are written identically in Task 2 Steps 4, 7, 9 and Task 4 Step 3. Existing members referenced without redeclaration: `_sut`, `LoadFixture`, `_runner`, `_tracker`, `_teeService`, `_filter`, `BuildArgs`. `ParseState` members used (`TotalPassed`, `TotalFailed`, `TotalSkipped`, `Failures`, `ZeroTestsFound`, `ProjectCount`) all exist on the record at `DotnetTestFilter.cs:410-417`.

**Known soft spot.** Task 3's exact-string assertions for restore and format were derived by reading the regexes and formatters, not by running them. Step 5 tells the implementer how to distinguish a formatting mismatch (correct the expected string) from a real invariant violation (stop and report). The `Contain`-based assertions in Steps 3-4 are deliberately looser for the same reason.

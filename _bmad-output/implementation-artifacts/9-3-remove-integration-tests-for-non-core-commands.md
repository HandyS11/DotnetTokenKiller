# Story 9.3: Remove Integration Tests for Non-Core Commands

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer maintaining DotnetTokenKiller,
I want the integration test classes for `publish`/`pack`/`run`, `format`/`nuget`/`passthrough`, and `ef` deleted,
So that the integration test suite only covers the four retained commands.

## Acceptance Criteria

1. **Given** the integration test project is inspected **When** all test classes are listed **Then** test classes exist only for `build`, `restore`, `clean`, and `test` commands — no test files reference `publish`, `pack`, `run`, `ef`, `format`, `nuget`, or passthrough.

2. **Given** `dotnet test DotnetTokenKiller.slnx --filter "Category=Integration"` is run **When** the integration test run completes **Then** all integration tests pass and no tests reference deleted command names.

3. **Given** `dotnet build DotnetTokenKiller.slnx` is run **When** the build completes **Then** there are zero errors and zero warnings (TreatWarningsAsErrors is enabled).

4. **Given** the `tests/DotnetTokenKiller.Cli.IntegrationTests/Fixtures/` directory is inspected (if it exists) **When** fixture files are listed **Then** no fixture files remain that support deleted commands.

## Tasks / Subtasks

- [x] Task 1 — Delete integration test classes for story 8-4 scope (publish, pack, run) (AC: #1)
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPublishIntegrationTests.cs`
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPackIntegrationTests.cs`
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRunIntegrationTests.cs`

- [x] Task 2 — Delete integration test classes for story 8-5 scope (format, nuget, passthrough) (AC: #1)
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetFormatIntegrationTests.cs`
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetNugetIntegrationTests.cs`
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPassthroughIntegrationTests.cs`

- [x] Task 3 — Delete integration test class for story 8-6 scope (ef) (AC: #1)
  - [x] Delete `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetEfIntegrationTests.cs`

- [x] Task 4 — Remove any ef-specific test boilerplate (AC: #1)
  - [x] Verify no `ISkippableFact` / `SkippableFact` / EF-specific boilerplate remains in retained test files
  - [x] Result: No such boilerplate found — retained tests use standard xUnit `[Fact]` only

- [x] Task 5 — Remove fixture data files for deleted commands (AC: #4)
  - [x] Verify `tests/DotnetTokenKiller.Cli.IntegrationTests/Fixtures/` — no fixtures directory exists; nothing to delete

- [x] Task 6 — Verify build and integration tests pass (AC: #2, #3)
  - [x] Run `dotnet build DotnetTokenKiller.slnx` — confirm zero errors, zero warnings
  - [x] Run `dotnet test DotnetTokenKiller.slnx` — confirm all tests pass (including integration)

## Dev Notes

### ⚠️ Batch Implementation Note

**This story's changes were applied as part of the Story 9-1 batch session** (commit `b71b6c1 Remove obsolete filter tests and integration tests for Dotnet commands`). The dev notes in Story 9-1 describe it as:

> "Story 9-3 partial batch: Deleted 7 failing integration test files (Publish, Pack, Run, Ef, Format, Nuget, Passthrough). Stories 9-2 and 9-3 should be marked done in sprint-status."

All tasks above are marked complete. **No implementation work is needed — verify and update sprint status.**

---

### Scope of This Story (9-3 Only)

**Strictly scoped to:**

- 7 integration test class files in `tests/DotnetTokenKiller.Cli.IntegrationTests/`
- Any ef-specific test boilerplate (`ISkippableFact`/`SkippableFact`)
- Fixture data files for deleted commands (none existed)

**Out of scope for 9-3:**

- Application filter files or unit tests — Story 9-1 (done)
- CLI command classes and `PassthroughRunUseCase` — Story 9-2 (done)
- Sample projects (`sample/SampleApp.EfCore/`, non-core fixtures) — Story 9-4
- Planning artifact updates — Story 9-5

---

### Current State of Integration Test Project

After the batch deletions, the retained source files in `tests/DotnetTokenKiller.Cli.IntegrationTests/` are:

```sh
DotnetBuildIntegrationTests.cs       ← retained (core command)
DotnetTestIntegrationTests.cs        ← retained (core command)
DotnetRestoreIntegrationTests.cs     ← retained (core command)
DotnetCleanIntegrationTests.cs       ← retained (core command)
IntegrationTestHelper.cs             ← retained (shared helper)
VersionTests.cs                      ← retained (version smoke test)
```

All 7 deleted files are gone from the repository.

---

### Integration Test Architecture

- **Test framework:** xUnit with `[Fact]` attributes
- **Trait:** `[Trait("Category", "Integration")]` on each test class (enables `--filter "Category=Integration"`)
- **Helper:** `IntegrationTestHelper` provides `RunDtkAsync`, `RunDotnetAsync`, and `CalculateSavings` — a static utility class in the same namespace
- **Sample path resolution:** `IntegrationTestHelper.SamplePath(project)` resolves to `{repo-root}/sample/{project}/` using `AppContext.BaseDirectory` + 5 levels up
- **Process execution:** Real `dotnet dtk.dll` invocations via `Process.Start` — full end-to-end with stdout/stderr capture
- **No mocking** — integration tests run real processes against the sample projects

**Key pattern in retained tests:**

```csharp
[Trait("Category", "Integration")]
public class DotnetBuildIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    [Fact]
    public async Task Build_SampleApp_Success_OutputStartsWithCheckmark()
    {
        var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync("build", SampleApp);
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "build", SampleApp);

        exitCode.Should().Be(0);
        // ... assertions
        var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
        savings.Should().BeGreaterThanOrEqualTo(80.0);
    }
}
```

---

### Testing Standards

- Framework: xUnit + FluentAssertions
- Integration tests use `[Trait("Category", "Integration")]` for filtering
- No snapshot testing in integration tests — assertions are direct string checks
- No `ISkippableFact` in any of the retained test files

---

### Code Style Constraints

- `TreatWarningsAsErrors` is enabled — zero warnings required
- File-scoped namespaces (`namespace DotnetTokenKiller.Cli.IntegrationTests;`)
- Unused `using` statements cause analyzer warnings — verify no orphaned imports after deletions (not applicable since files were wholesale deleted)
- Run `dotnet format DotnetTokenKiller.slnx --no-restore` after edits if uncertain

---

### Project Structure Notes

**Files DELETED (this story — all 7 are gone):**

```sh
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPublishIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPackIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRunIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetFormatIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetNugetIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPassthroughIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetEfIntegrationTests.cs
```

**No fixture files** existed (no `Fixtures/` directory was present in the integration test project).

**Files RETAINED UNCHANGED:**

```sh
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetBuildIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTestIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRestoreIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetCleanIntegrationTests.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs
tests/DotnetTokenKiller.Cli.IntegrationTests/VersionTests.cs
```

### References

- Story 9-1 file (batch implementation context): [_bmad-output/implementation-artifacts/9-1-remove-non-core-application-filters.md](_bmad-output/implementation-artifacts/9-1-remove-non-core-application-filters.md)
- Story 9-2 file: [_bmad-output/implementation-artifacts/9-2-remove-passthrough-use-case-and-cli-commands.md](_bmad-output/implementation-artifacts/9-2-remove-passthrough-use-case-and-cli-commands.md)
- Sprint Change Proposal: [_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md) — Section 4, Section 5 (execution order)
- Epics file: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md#story-93-remove-integration-tests-for-non-core-commands) — Epic 9, Story 9.3 definition

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ Batch implementation: All 9-3 changes applied during Story 9-1 dev session as part of batch commit `b71b6c1`.
- ✅ Task 1 complete: `DotnetPublishIntegrationTests.cs`, `DotnetPackIntegrationTests.cs`, `DotnetRunIntegrationTests.cs` deleted.
- ✅ Task 2 complete: `DotnetFormatIntegrationTests.cs`, `DotnetNugetIntegrationTests.cs`, `DotnetPassthroughIntegrationTests.cs` deleted.
- ✅ Task 3 complete: `DotnetEfIntegrationTests.cs` deleted.
- ✅ Task 4 complete: No `ISkippableFact` / ef-specific boilerplate found in retained files — standard `[Fact]` throughout.
- ✅ Task 5 complete: No `Fixtures/` directory existed — nothing to delete.
- ✅ Task 6 complete: `dotnet build` — 0 errors, 0 warnings. `dotnet test` — all pass (including integration).

### File List

**Deleted:**

- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPublishIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPackIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRunIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetFormatIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetNugetIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPassthroughIntegrationTests.cs
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetEfIntegrationTests.cs

**No files modified.**

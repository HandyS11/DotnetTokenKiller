# Story 9.4: Remove Non-Core Sample Projects and Fixtures

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer maintaining DotnetTokenKiller,
I want the `SampleApp.EfCore` sample project removed from the repository and from the sample solution file,
So that the `/sample` folder only contains projects needed to test the four retained commands (`build`, `test`, `restore`, `clean`).

## Acceptance Criteria

1. **Given** the `sample/` directory is inspected after deletions **When** the project list is reviewed **Then** `SampleApp.EfCore` is absent — only `SampleApp`, `SampleApp.Tests`, `SampleApp.Broken`, and `SampleApp.BadPackage` remain.

2. **Given** `sample/DotnetTokenKiller.Sample.slnx` is inspected **When** the solution file is read **Then** no `SampleApp.EfCore` project reference appears — only references to `SampleApp`, `SampleApp.Tests`, `SampleApp.Broken`, and `SampleApp.BadPackage`.

3. **Given** `dotnet build sample/DotnetTokenKiller.Sample.slnx` is run **When** the build completes **Then** there are zero errors and zero warnings.

4. **Given** `dotnet test DotnetTokenKiller.slnx` is run **When** the test run completes **Then** all tests pass with zero failures and zero regressions.

## Tasks / Subtasks

- [x] Task 1 — Delete `sample/SampleApp.EfCore/` directory entirely (AC: #1)
  - [x] Delete `sample/SampleApp.EfCore/SampleApp.EfCore.csproj`
  - [x] Delete `sample/SampleApp.EfCore/SampleItem.cs`
  - [x] Delete `sample/SampleApp.EfCore/SampleDbContext.cs`
  - [x] Delete `sample/SampleApp.EfCore/SampleDbContextFactory.cs`
  - [x] Delete `sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.cs`
  - [x] Delete `sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.Designer.cs`
  - [x] Delete `sample/SampleApp.EfCore/Migrations/SampleDbContextModelSnapshot.cs`
  - [x] Confirm `bin/` and `obj/` subdirectories are also removed (standard git delete removes tracked files; untracked build artifacts in `bin/obj` are ignored by git)

- [x] Task 2 — Update `sample/DotnetTokenKiller.Sample.slnx` (AC: #2)
  - [x] Remove the line `<Project Path="SampleApp.EfCore/SampleApp.EfCore.csproj" />` from the solution file
  - [x] Verify the remaining solution file contains exactly 3 project references: `SampleApp`, `SampleApp.Broken`, `SampleApp.BadPackage` (note: `SampleApp.Tests` may or may not appear — verify current state)

- [x] Task 3 — Verify build and tests pass (AC: #3, #4)
  - [x] Run `dotnet build sample/DotnetTokenKiller.Sample.slnx` — confirm zero errors, zero warnings
  - [x] Run `dotnet test DotnetTokenKiller.slnx` — confirm all tests pass

## Dev Notes

### This Story Requires Actual Implementation

Unlike stories 9-1, 9-2, and 9-3 which were batch-implemented earlier, **story 9-4 has NOT been implemented yet**. `sample/SampleApp.EfCore/` still exists on disk and `DotnetTokenKiller.Sample.slnx` still references it. This is a genuine implementation task.

---

### Current State of `sample/DotnetTokenKiller.Sample.slnx`

```xml
<Solution>
  <Project Path="SampleApp/SampleApp.csproj" />
  <Project Path="SampleApp.Tests/SampleApp.Tests.csproj" />
  <Project Path="SampleApp.EfCore/SampleApp.EfCore.csproj" />  ← DELETE THIS LINE
</Solution>
```

**Target state after edit:**

```xml
<Solution>
  <Project Path="SampleApp/SampleApp.csproj" />
  <Project Path="SampleApp.Tests/SampleApp.Tests.csproj" />
</Solution>
```

**Note:** `SampleApp.Broken` and `SampleApp.BadPackage` are NOT in the solution file (they are fixture-only projects used directly by path in integration tests). Confirm this is still the case before editing.

---

### Source Files to Delete from `sample/SampleApp.EfCore/`

```sh
sample/SampleApp.EfCore/SampleApp.EfCore.csproj
sample/SampleApp.EfCore/SampleItem.cs
sample/SampleApp.EfCore/SampleDbContext.cs
sample/SampleApp.EfCore/SampleDbContextFactory.cs
sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.cs
sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.Designer.cs
sample/SampleApp.EfCore/Migrations/SampleDbContextModelSnapshot.cs
```

The `bin/` and `obj/` subdirectories are not tracked in git (they're in `.gitignore`), so only the 7 source files above need to be explicitly deleted. Git will clean up the empty directory.

---

### `SampleApp.EfCore.csproj` — Package Reference Note

The project references `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design` without version numbers:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite"/>
<PackageReference Include="Microsoft.EntityFrameworkCore.Design">...</PackageReference>
```

The root `Directory.Packages.props` does **not** include these EF packages — they were only used by `SampleApp.EfCore`. After deleting this project, no cleanup of `Directory.Packages.props` is needed (no entries to remove).

---

### Retained Sample Projects (Do Not Touch)

| Project | Used By | Retained |
|---|---|---|
| `sample/SampleApp/` | build, test, restore, clean integration tests | ✅ |
| `sample/SampleApp.Tests/` | test integration tests (IntentionallyFailingTests) | ✅ |
| `sample/SampleApp.Broken/` | build and restore failure tests | ✅ |
| `sample/SampleApp.BadPackage/` | restore failure test | ✅ |
| `sample/SampleApp.EfCore/` | ef integration tests (deleted in 9-3) | ❌ DELETE |

---

### Note on `SampleApp/Program.cs`

`sample/SampleApp/Program.cs` contains a `--fail` argument handler with a comment:

```csharp
// Program.cs — handles --fail arg for integration test coverage
if (args.Contains("--fail"))
{
    Console.WriteLine("App starting...");
    Environment.Exit(1);
}
Console.WriteLine("Hello from SampleApp!");
```

This was originally used by the `dtk dotnet run` integration tests (story 8-4, now deleted). The `--fail` handler is harmless to retained integration tests and doesn't break anything. Removing it is **optional** cleanup, not required by this story's ACs. If removed, also remove the comment.

---

### Integration Test Architecture (for context)

The integration tests use `IntegrationTestHelper.SamplePath(project)` to resolve sample project paths:

```csharp
private static readonly string _repoRoot =
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

internal static string SamplePath(string project) =>
    Path.Combine(_repoRoot, "sample", project);
```

No retained integration test references `SampleApp.EfCore` directly. After deleting the project, no changes to `IntegrationTestHelper.cs` are required.

---

### `.slnx` File Format

`DotnetTokenKiller.Sample.slnx` uses the new XML-based Visual Studio solution file format (introduced with VS 2022 / .NET 8+ tooling). It is a simple XML file with `<Solution>` and `<Project>` elements — no GUIDs, no configuration sections. Editing it is a straightforward XML line deletion.

---

### Code Style Constraints

- `TreatWarningsAsErrors` is enabled in the main solution — deleting the project won't affect this
- The sample projects inherit from root `Directory.Build.props` but are built via `sample/DotnetTokenKiller.Sample.slnx` (separate from `DotnetTokenKiller.slnx`)
- No `.editorconfig` formatting is needed for directory deletions
- After updating the `.slnx` XML file, no auto-formatting concerns (it's not a `.cs` file)

---

### Project Structure Notes

**Directory to DELETE (entire tree):**

```sh
sample/SampleApp.EfCore/              ← delete entire directory
  SampleApp.EfCore.csproj
  SampleItem.cs
  SampleDbContext.cs
  SampleDbContextFactory.cs
  Migrations/
    20260314161654_InitialCreate.cs
    20260314161654_InitialCreate.Designer.cs
    SampleDbContextModelSnapshot.cs
```

**File to EDIT:**

```sh
sample/DotnetTokenKiller.Sample.slnx  ← remove SampleApp.EfCore <Project> line
```

**Files RETAINED UNCHANGED:**

```sh
sample/SampleApp/
sample/SampleApp.Tests/
sample/SampleApp.Broken/
sample/SampleApp.BadPackage/
```

### References

- Epics file: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md#story-94-remove-non-core-sample-projects-and-fixtures) — Epic 9, Story 9.4 definition
- Sprint Change Proposal: [_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md) — Section 4 (technical impact), Section 5 (execution order)
- Story 9-3 (integration test removal): [_bmad-output/implementation-artifacts/9-3-remove-integration-tests-for-non-core-commands.md](_bmad-output/implementation-artifacts/9-3-remove-integration-tests-for-non-core-commands.md)
- Sample solution file: [sample/DotnetTokenKiller.Sample.slnx](sample/DotnetTokenKiller.Sample.slnx)
- EfCore project (to delete): [sample/SampleApp.EfCore/SampleApp.EfCore.csproj](sample/SampleApp.EfCore/SampleApp.EfCore.csproj)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

_None._

### Completion Notes List

- Deleted all 7 tracked source files from `sample/SampleApp.EfCore/` using `git rm`. The `bin/` and `obj/` subdirectories were untracked (in `.gitignore`) and not present in git history.
- Removed the `<Project Path="SampleApp.EfCore/SampleApp.EfCore.csproj" />` line from `sample/DotnetTokenKiller.Sample.slnx`. The remaining solution references only `SampleApp` and `SampleApp.Tests` (confirmed: `SampleApp.Broken` and `SampleApp.BadPackage` were never in the slnx — they are fixture-only).
- `dotnet build sample/DotnetTokenKiller.Sample.slnx`: 0 errors, 0 warnings.
- `dotnet test DotnetTokenKiller.slnx`: 149 tests passed (Domain: 17, Infrastructure: 22, Application: 87, Integration: 23), 0 failures, 0 regressions.
- No changes to `Directory.Packages.props` needed — EF packages were not centralised there.

### File List

**Deleted:**

- `sample/SampleApp.EfCore/SampleApp.EfCore.csproj`
- `sample/SampleApp.EfCore/SampleItem.cs`
- `sample/SampleApp.EfCore/SampleDbContext.cs`
- `sample/SampleApp.EfCore/SampleDbContextFactory.cs`
- `sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.cs`
- `sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.Designer.cs`
- `sample/SampleApp.EfCore/Migrations/SampleDbContextModelSnapshot.cs`

**Modified:**

- `sample/DotnetTokenKiller.Sample.slnx`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/9-4-remove-non-core-sample-projects-and-fixtures.md`

## Change Log

- 2026-03-15: Deleted `sample/SampleApp.EfCore/` project tree (7 source files) and removed its reference from `sample/DotnetTokenKiller.Sample.slnx`. All tests pass.

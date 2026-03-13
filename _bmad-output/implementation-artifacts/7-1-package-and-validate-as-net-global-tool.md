# Story 7.1: Package and Validate as .NET Global Tool

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a .NET developer,
I want to install DTK with a single `dotnet tool install` command,
So that I can immediately use `dtk` in any terminal without manual binary placement.

## Acceptance Criteria

1. **Pack succeeds**: Running `dotnet pack src/DotnetTokenKiller.Cli -c Release -o ./nupkg` produces a `.nupkg` file in `./nupkg/` without errors.
2. **Tool installs**: Running `dotnet tool install -g --add-source ./nupkg DotnetTokenKiller` installs successfully.
3. **Version check**: `dtk --version` outputs the package version (`0.1.0`).
4. **End-to-end smoke test**: `dtk dotnet build` runs `dotnet build`, filters output, and returns the correct exit code in a .NET project directory.
5. **NuGet metadata complete**: `DotnetTokenKiller.Cli.csproj` includes `Description`, `PackageTags`, `PackageLicenseExpression=MIT`, and `PackageReadmeFile=README.md`.
6. **README packaged**: `README.md` is included in the `.nupkg` (via `<None Pack="true" PackagePath="\" />`).
7. **README content**: `README.md` at the repository root contains installation instructions (`dotnet tool install -g DotnetTokenKiller`) and basic usage examples.
8. **All tests green**: `dotnet test DotnetTokenKiller.slnx` passes before packaging.

## Tasks / Subtasks

- [x] Task 1: Add `PackageReadmeFile` and include README in pack (AC: #5, #6)
  - [x] Add `<PackageReadmeFile>README.md</PackageReadmeFile>` to `DotnetTokenKiller.Cli.csproj` `<PropertyGroup>`
  - [x] Add `<None Include="../../README.md" Pack="true" PackagePath="\" />` inside an `<ItemGroup>` in the same `.csproj`
  - [x] Note: path `../../README.md` is correct — the `.csproj` is at `src/DotnetTokenKiller.Cli/`; repo root is two levels up

- [x] Task 2: Expand README.md with installation and usage instructions (AC: #7)
  - [x] Add installation section: `dotnet tool install -g DotnetTokenKiller`
  - [x] Add usage section showing key commands: `dtk dotnet build`, `dtk dotnet test`, `dtk dotnet restore`, `dtk gain`
  - [x] Keep it concise — this is a NuGet README, not a full manual

- [x] Task 3: Verify all tests pass (AC: #8)
  - [x] Run `dotnet test DotnetTokenKiller.slnx` — all tests must pass
  - [x] Run `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror` — 0 errors, 0 warnings

- [x] Task 4: Pack and validate (AC: #1–#4)
  - [x] Run `dotnet pack src/DotnetTokenKiller.Cli -c Release -o ./nupkg`
  - [x] Confirm `./nupkg/DotnetTokenKiller.0.1.0.nupkg` (or similar) is produced
  - [x] Run `dotnet tool install -g --add-source ./nupkg DotnetTokenKiller`
  - [x] Run `dtk --version` and confirm it outputs `0.1.0`
  - [x] Run `dtk dotnet build` in the repo root — confirm it executes and filters output
  - [x] Run `dotnet tool uninstall -g DotnetTokenKiller` to clean up

## Dev Notes

### Current Repository State (Before This Story)

| File / Property | Current State |
|---|---|
| `PackAsTool` | `true` ✓ already set in Story 1.1 |
| `ToolCommandName` | `dtk` ✓ already set |
| `PackageId` | `DotnetTokenKiller` ✓ already set |
| `Version` | `0.1.0` ✓ already set |
| `Description` | ✓ already set: "A .NET CLI proxy that reduces LLM token usage by filtering dotnet command output" |
| `PackageTags` | ✓ already set: `dotnet;cli;llm;tokens;productivity` |
| `PackageLicenseExpression` | `MIT` ✓ already set |
| `PackageReadmeFile` | ❌ **MISSING — must add** |
| README.md | exists but minimal (2 lines only) — **must expand** |
| All tests | passing |
| `quality-gate.yml` | exists at `.github/workflows/quality-gate.yml` |

### csproj Changes Required

The `.csproj` file at `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` needs exactly two additions:

**In the existing `<PropertyGroup>`**, add after `PackageLicenseExpression`:

```xml
<PackageReadmeFile>README.md</PackageReadmeFile>
```

**In a new or existing `<ItemGroup>`**, add:

```xml
<None Include="../../README.md" Pack="true" PackagePath="\" />
```

> **Why `../../README.md`?** The `.csproj` is at `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`. The repository root (where `README.md` lives) is two directories up. The `PackagePath="\"` places it at the root of the `.nupkg`.

### README.md Content Guidance

The README is minimal (just title + one sentence). It needs to grow to include:

1. A brief project description (already there)
2. **Installation** section — `dotnet tool install -g DotnetTokenKiller`
3. **Usage** section — key commands with brief descriptions
4. Optionally: token reduction percentages per command (great marketing for NuGet)

Do not over-engineer the README. A clean, concise README is preferred.

### Architecture Context — Distribution (§11)

The architecture document (§11) specifies:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>dtk</ToolCommandName>
<PackageId>DotnetTokenKiller</PackageId>
```

All three are already in the `.csproj`. This story completes the metadata and validates end-to-end.

The architecture also notes Native AOT as a secondary phase — **do NOT enable `PublishAot=true`** in this story. That is a future enhancement not part of Story 7.1.

### Pack Command Details

```bash
# Pack (produces nupkg in ./nupkg/ folder at repo root)
dotnet pack src/DotnetTokenKiller.Cli -c Release -o ./nupkg

# Install from local source
dotnet tool install -g --add-source ./nupkg DotnetTokenKiller

# Verify
dtk --version   # should output: 0.1.0

# Smoke test (run from any directory with a .NET project, or repo root)
dtk dotnet build

# Cleanup after validation
dotnet tool uninstall -g DotnetTokenKiller
```

### Important: No CI Changes in This Story

Story 7.1 does **NOT** modify `.github/workflows/quality-gate.yml` or `ci.yml`. The CI changes (adding `dotnet format`) are exclusively in Story 7.2.

### Analyzer Compliance

- `TreatWarningsAsErrors` is active — any XML element added to `.csproj` must be valid MSBuild syntax.
- No C# code changes in this story, so no Roslynator/SonarAnalyzer issues expected.
- `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` should still pass (no `.cs` changes).

### Previous Story Intelligence (from Epic 6)

The most recent completed epic (6) implemented JSON configuration. Key learnings applicable here:

- The project builds cleanly with `TreatWarningsAsErrors` — keep it that way.
- The `feat/json-config` branch was the last feature branch merged; current branch is `feat/json-config` still (check before committing).
- Use the pre-commit hook: it auto-formats staged `.cs` files and validates `.csproj`/`.props` files.
  - Install with: `git config core.hooksPath .githooks`
  - The hook validates `.csproj` files, so the `PackageReadmeFile` addition must be valid XML.

### Git Intelligence (Recent Commits)

| Commit | Summary |
|---|---|
| `318d201` | Feat: add json configuration (#11) — last merged PR |
| `a5510e8` | Feat: token saving analytics (#10) |
| `8f74722` | Feat: add filters and passthrough (#9) |

Current branch: `feat/json-config` — create a new branch for this story (e.g., `feat/global-tool-package`).

### Project Structure Notes

Files to modify:

- `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` — add `PackageReadmeFile` property + README `<None>` item
- `README.md` — expand with installation and usage content

Files NOT to touch:

- Any `.cs` source files
- `Directory.Build.props` / `Directory.Packages.props`
- `.github/workflows/quality-gate.yml` (Story 7.2 handles that)
- Any test files

Temporary artifacts (do not commit):

- `./nupkg/` folder — add to `.gitignore` if not already there (check first)

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 7.1]
- [Source: _bmad-output/planning-artifacts/Architecture.md#11. Build and Distribution]
- [Source: _bmad-output/planning-artifacts/Architecture.md#2. Technology Stack]
- [Source: src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj] (current state — PackageReadmeFile missing)
- [Source: README.md] (current state — minimal, needs expansion)
- [Source: _bmad-output/implementation-artifacts/1-6-set-up-minimal-ci-quality-gate.md] (CI context — quality-gate.yml structure)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

None.

### Completion Notes List

- Added `<PackageReadmeFile>README.md</PackageReadmeFile>` to `DotnetTokenKiller.Cli.csproj` `<PropertyGroup>` (after `PackageLicenseExpression`).
- Added `<None Include="../../README.md" Pack="true" PackagePath="\" />` in a new `<ItemGroup>` in the `.csproj` to include README in the `.nupkg`.
- Expanded `README.md` with Installation and Usage sections covering all supported commands and analytics (`dtk gain`).
- Added `config.SetApplicationVersion("0.1.0")` to `Program.cs` to satisfy AC #3 (`dtk --version`). Story said "no .cs changes" but this was required by an explicit AC — it is a single-line packaging concern, not business logic.
- Local validations: build (0 errors, 0 warnings), tests (199 passed, 0 failed), pack (produces `DotnetTokenKiller.0.1.0.nupkg`), install → `dtk --version` → `0.1.0`, smoke test `dtk dotnet build` → filters output correctly, uninstalled.
- `.gitignore` already contains `*.nupkg` — no changes needed.

### File List

- `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (modified — added `PackageReadmeFile`, `<None Pack>` item; fixed XML formatting)
- `src/DotnetTokenKiller.Cli/Program.cs` (modified — version read dynamically from `AssemblyInformationalVersionAttribute`)
- `README.md` (modified — expanded with installation, usage; corrected configuration example to real `DtkConfig` structure)
- `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj` (modified — added project reference to Cli, added `Spectre.Console.Testing`)
- `tests/DotnetTokenKiller.Cli.IntegrationTests/VersionTests.cs` (created — asserts CLI assembly carries a valid informational version)

## Change Log

- 2026-03-13: Implemented Story 7.1 — added NuGet packaging metadata (`PackageReadmeFile`, README `<None Pack>` item), expanded README.md, added `SetApplicationVersion` to Program.cs. Pack produces `DotnetTokenKiller.0.1.0.nupkg`; `dtk --version` outputs `0.1.0`; end-to-end smoke test passes. 199 tests, 0 failures.
- 2026-03-13: Code review fixes — version now read dynamically from `AssemblyInformationalVersionAttribute` (eliminates dual-hardcoding drift risk); corrected README config example (was showing nonexistent `verbosity` field, now shows real `DtkConfig` structure); fixed csproj XML formatting; added `VersionTests` to integration test project. 200 tests, 0 failures.

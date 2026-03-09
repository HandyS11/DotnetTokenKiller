# Story 1.6: Set Up Minimal CI Quality Gate

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a maintainer of DotnetTokenKiller,
I want a basic GitHub Actions pipeline running on every push from day one,
So that build failures and test regressions are caught automatically throughout all development, not just at the end.

## Acceptance Criteria

1. **Workflow file location**: `.github/workflows/quality-gate.yml` exists in the repository.
2. **Trigger**: The workflow runs on every `push` (all branches) and every `pull_request`.
3. **Runner**: The job runs on `ubuntu-latest`.
4. **dotnet setup**: The `actions/setup-dotnet@v4` step is used without a `dotnet-version` input. The action automatically reads `global.json` (which pins `sdk.version: 10.0.103` with `allowPrerelease: true`) to resolve the correct SDK. `include-prerelease` is not a valid input for this action.
5. **Build step**: Runs `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror`; pipeline fails if this step fails.
6. **Test step**: Runs `dotnet test DotnetTokenKiller.slnx --no-build --logger trx`; pipeline fails if this step fails.
7. **No format step**: `dotnet format --verify-no-changes` is NOT included in this story (deferred to Story 7.2).
8. **Restore step**: A `dotnet restore DotnetTokenKiller.slnx` step precedes the build step.
9. **Local tests green**: `dotnet test DotnetTokenKiller.slnx` passes locally (60 tests) before this story is merged.
10. **Coexistence**: The existing `.github/workflows/ci.yml` is left untouched — `quality-gate.yml` is an additional, separate file.

## Tasks / Subtasks

- [x] Task 1: Create `.github/workflows/quality-gate.yml` (AC: #1–#9)
  - [x] File path: `.github/workflows/quality-gate.yml`
  - [x] Set `name:` to `Quality Gate`
  - [x] Set `on:` to trigger on `push:` and `pull_request:` (all branches; no branch filter)
  - [x] Add `permissions: contents: read` (principle of least privilege)
  - [x] Single job: `quality-gate` on `ubuntu-latest`
  - [x] Step 1 — Checkout: `uses: actions/checkout@v4`
  - [x] Step 2 — Setup .NET: `uses: actions/setup-dotnet@v4` (no `with:` block; action reads `global.json` automatically)
  - [x] Step 3 — Restore: `run: dotnet restore DotnetTokenKiller.slnx`
  - [x] Step 4 — Build: `run: dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror`
  - [x] Step 5 — Test: `run: dotnet test DotnetTokenKiller.slnx --no-build --logger trx`
  - [x] Step 6 — Upload TRX: `uses: actions/upload-artifact@v4` with `if: always()`, name `test-results`, path `**/*.trx`, `retention-days: 7`
  - [x] Do NOT add a `dotnet format` step

- [x] Task 2: Verify local state (AC: #9, #10)
  - [x] Run `dotnet test DotnetTokenKiller.slnx` locally — confirm all 60 tests pass (17 Domain + 43 Application)
  - [x] Run `dotnet build DotnetTokenKiller.slnx` locally — confirm 0 errors, 0 warnings
  - [x] Confirm `.github/workflows/ci.yml` is unchanged

## Dev Notes

### Current Repository State (After Story 1.5)

| File | State |
|---|---|
| `.github/workflows/ci.yml` | **EXISTS — DO NOT MODIFY** (pre-existing richer pipeline) |
| `.github/workflows/quality-gate.yml` | **DOES NOT EXIST — this story creates it** |
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `TreatWarningsAsErrors`, net10.0, C#14 |
| `Directory.Packages.props` | Includes `Verify.Xunit 28.2.0` (added in 1.5) |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) |
| All other src/** | Complete — do not modify |
| All tests/** | 60 passing tests — do not break |

### Critical Discovery: ci.yml Already Exists

**`.github/workflows/ci.yml` already exists** with a more comprehensive pipeline (ubuntu + windows matrix, format check, coverage collection). This was created before the BMAD story system.

- **DO NOT delete or modify `ci.yml`** — it's a working pipeline that covers other scenarios.
- **Create `quality-gate.yml` as a NEW, separate file** — this is the "official" story-1.6 pipeline.
- Story 7.2 (`expand-ci-cd-quality-gate-pipeline-to-full-quality-enforcement`) will reference `quality-gate.yml` by name and add `dotnet format` to it.
- The two workflows can coexist: `ci.yml` is the richer pipeline; `quality-gate.yml` is the BMAD-tracked minimal gate.

### Exact Workflow File Content

```yaml
name: Quality Gate

permissions:
  contents: read

on:
  push:
  pull_request:

jobs:
  quality-gate:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4

      - name: Restore
        run: dotnet restore DotnetTokenKiller.slnx

      - name: Build
        run: dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror

      - name: Test
        run: dotnet test DotnetTokenKiller.slnx --no-build --logger trx

      - name: Upload TRX results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/*.trx'
          retention-days: 7
```

> **Why no `dotnet-version` on setup-dotnet?** — `actions/setup-dotnet@v4` reads `global.json` automatically when no `dotnet-version` input is provided. `global.json` pins `sdk.version: 10.0.103` with `allowPrerelease: true`, so the correct pre-release SDK is resolved without any extra inputs. `include-prerelease` is not a valid input for this action.
> **Why no branch filter on `push`?** — The epic spec says "on every push" to any branch. No `branches:` filter means every push (all branches) triggers the gate. This is intentional — catches regressions everywhere, not just on main/develop.
> **Why `-warnaserror` on build?** — Architecture §11 specifies it; also matches `Directory.Build.props` which sets `TreatWarningsAsErrors`. The flag ensures the CI build fails on any warning, consistent with local builds.
> **Why `--logger trx`?** — TRX is MSTest/xunit's XML result format, compatible with GitHub Actions artifact viewers. Combined with the `upload-artifact` step, test results are preserved even on failure for post-mortem inspection.
> **Why upload with `if: always()`?** — Test results are most valuable when tests fail. Without `always()`, the upload step would be skipped on test failure, defeating its purpose.

### Architecture Constraints

- Architecture §11 defines the CI/CD pipeline steps in this order: (1) format, (2) build, (3) test, (4) upload TRX. Story 1.6 defers step 1 (format) to Story 7.2 per explicit AC.
- Architecture §2: SDK version resolved from `global.json` (`10.0.103`), matching the `net10.0` TFM.
- Architecture §11: `ubuntu-latest` — single platform for the minimal gate (Story 7.2 may expand).

### Previous Story Intelligence (from Story 1.5)

- **60 tests total** (17 Domain + 43 Application); all must pass in CI.
- **0 warnings build** — `TreatWarningsAsErrors` is active; `-warnaserror` on CI build is consistent.
- **No new packages added in 1.5** beyond `Verify.Xunit 28.2.0`; restore should be straightforward.
- `VerifyInit.cs` uses `Verifier.UseProjectRelativeDirectory("Snapshots")` — snapshot `.verified.txt` files are committed; CI will not try to auto-accept them (it runs headless with no diff engine).

### Git Context (Recent Commits)

| Commit | What was built |
|---|---|
| `133ff9b` | Story 1.5 — `DotnetBuildFilter` + 14 filter tests + Verify snapshots |
| `680e635` | Story 1.4 — helpers + `FilteredRunUseCase`; 29 Application.Tests; NSubstitute |
| `7525970` | Story 1.3 — `ProcessCommandRunner`, CLI commands (passthrough) |
| `4c78002` | Story 1.1 — full solution scaffold, 8 projects |

### Scope Guard — What This Story Does NOT Do

- Does NOT add `dotnet format --verify-no-changes` (deferred to Story 7.2)
- Does NOT add a Windows or macOS matrix (ci.yml already covers multi-OS)
- Does NOT add code coverage collection (ci.yml already covers that)
- Does NOT modify or delete `ci.yml`
- Does NOT package or publish the tool (Epic 7)
- Does NOT implement any C# code changes

### Project Structure Notes

- New file: `.github/workflows/quality-gate.yml`
- No C# files are created or modified in this story
- `.github/workflows/` directory already exists (contains `ci.yml` and `codeql.yml`)
- YAML: 2-space indent; LF line endings; no trailing whitespace (matches project editorconfig)

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.6]
- [Source: _bmad-output/planning-artifacts/Architecture.md#11. Build and Distribution — CI/CD Quality Gate Pipeline]
- [Source: _bmad-output/planning-artifacts/Architecture.md#2. Technology Stack]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 7.2] (references quality-gate.yml by name)
- [Source: .github/workflows/ci.yml] (existing pipeline — do not modify)
- [Source: global.json] (dotnet SDK 10.0.103, allowPrerelease: true)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

None.

### Completion Notes List

- Created `.github/workflows/quality-gate.yml`: checkout, setup-dotnet@v4 (reads global.json automatically; `include-prerelease` is not a valid action input), restore, build (-warnaserror), test (--logger trx), upload TRX artifact (if: always(), retention-days: 7).
- No `dotnet format` step added (deferred to Story 7.2).
- Local build: 0 errors, 0 warnings. Local tests: 60 passed (17 Domain + 43 Application).
- `ci.yml` left untouched (confirmed via git diff).

### File List

- `.github/workflows/quality-gate.yml` (created)

## Change Log

- 2026-03-09: Created `.github/workflows/quality-gate.yml` — minimal CI quality gate with restore, build, test, and TRX upload steps. All 60 tests pass locally.
- 2026-03-09: Corrected AC #4 and Dev Notes — `include-prerelease` is not a valid input for `actions/setup-dotnet@v4`; action reads `global.json` automatically when no `dotnet-version` is specified.

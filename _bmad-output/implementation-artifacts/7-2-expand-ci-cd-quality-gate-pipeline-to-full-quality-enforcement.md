# Story 7.2: Expand CI/CD Quality Gate Pipeline to Full Quality Enforcement

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a maintainer of DotnetTokenKiller,
I want the existing GitHub Actions pipeline (from Story 1.6) expanded to enforce full code quality including formatting,
So that no PR can merge with formatting violations, build warnings, or failing tests.

## Acceptance Criteria

1. **Format step added**: `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` runs as the **first** step after restore, before build.
2. **Step order**: Pipeline executes in order — (1) format, (2) build `--warnaserror`, (3) test `--logger trx`.
3. **Pipeline fails on violation**: If any step fails, the pipeline fails and blocks merge.
4. **Same file**: Changes are made to the existing `.github/workflows/quality-gate.yml` (NOT a new workflow file).
5. **SDK version pinned**: The `actions/setup-dotnet@v4` step specifies `dotnet-version: '10.0.x'`.
6. **TRX upload preserved**: The existing `Upload TRX results` step remains intact with `if: always()`.
7. **Tests green locally**: `dotnet test DotnetTokenKiller.slnx` passes before pushing the pipeline change.

## Tasks / Subtasks

- [x] Task 1: Pin .NET SDK version in setup step (AC: #5)
  - [x] Add `with: dotnet-version: '10.0.x'` to the `Setup .NET` step in `quality-gate.yml`

- [x] Task 2: Add format enforcement step (AC: #1, #2, #3)
  - [x] Insert a new `Format` step between `Restore` and `Build`:

    ```yaml
    - name: Format
      run: dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
    ```

  - [x] Confirm step order: Restore → Format → Build → Test → Upload TRX

- [x] Task 3: Verify locally before push (AC: #7)
  - [x] Run `dotnet test DotnetTokenKiller.slnx` — all 199 tests must pass
  - [x] Run `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` — must exit 0 (no violations)
  - [x] Run `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror` — 0 errors, 0 warnings

## Dev Notes

### Current State of `quality-gate.yml` (Before This Story)

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
        uses: actions/setup-dotnet@v4         # ← NO dotnet-version specified — must add

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

**Two changes required:**

1. Add `with: dotnet-version: '10.0.x'` to `Setup .NET`
2. Insert a `Format` step between `Restore` and `Build`

### Target State of `quality-gate.yml` (After This Story)

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
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore DotnetTokenKiller.slnx

      - name: Format
        run: dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes

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

### Reference: Existing `ci.yml` Already Has Formatting

The `ci.yml` workflow (which runs on ubuntu+windows matrix) already enforces formatting:

```yaml
- name: Check formatting
  run: dotnet format --no-restore --verify-no-changes
```

This story mirrors that pattern in `quality-gate.yml`, using the `.slnx` file argument to be explicit. The `dotnet format` command without arguments would also work (it discovers `.slnx`/`.sln` automatically), but the explicit form is preferred for consistency with the other steps in `quality-gate.yml`.

### Why `--no-restore` on Format?

The `Restore` step runs first, so `--no-restore` avoids a redundant restore. All other steps in `quality-gate.yml` also use `--no-restore` / `--no-build` for the same reason.

### Why `dotnet-version: '10.0.x'`?

Without this pin, `actions/setup-dotnet@v4` uses the .NET version from the `global.json` if present, or the latest available on the runner. Pinning to `10.0.x` ensures the pipeline always uses net10.0 (the target framework declared in all `.csproj` files and `CLAUDE.md`). This prevents silent breakage if the runner's default SDK changes.

### No C# Code Changes

This story touches only `.github/workflows/quality-gate.yml`. No `.cs`, `.csproj`, `.props`, or test files are modified.

### Previous Story Intelligence (Story 7.1)

Story 7.1 (review status) completed:

- Added `PackageReadmeFile` + README `<None Pack>` item to `DotnetTokenKiller.Cli.csproj`
- Expanded `README.md` with installation and usage sections
- Added `config.SetApplicationVersion("0.1.0")` to `Program.cs`
- 199 tests passing, build clean (0 warnings)
- Produced `DotnetTokenKiller.0.1.0.nupkg` and validated `dtk --version` → `0.1.0`

The codebase is formatting-clean right now — `dotnet format --verify-no-changes` exits 0. This story's format gate will immediately be green on the first run.

### Git Intelligence (Recent Commits)

| Commit | Summary |
|---|---|
| `46af1f1` | Feat: update project files to include README and set application version (Story 7.1) |
| `318d201` | Feat: add json configuration (#11) |
| `a5510e8` | Feat: token saving analytics (#10) |

Current branch: `feat/dotnet-tool`. This story's change can be committed directly to this branch or in a sub-branch — follow existing conventions (PR per story).

### Analyzer Compliance

No C# code is touched, so no Roslynator/SonarAnalyzer issues are possible. YAML has no build-time analysis. The pre-commit hook only validates `.cs` and `.csproj`/`.props` files — YAML edits bypass it.

### Project Structure Notes

**File to modify:**

- `.github/workflows/quality-gate.yml` — add `dotnet-version` pin + `Format` step

**Files NOT to touch:**

- `.github/workflows/ci.yml` — already has format checking; do not merge or change
- `.github/workflows/codeql.yml` — unrelated security scanning workflow
- Any `.cs`, `.csproj`, `.props`, test files

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 7.2]
- [Source: .github/workflows/quality-gate.yml] (current state before this story)
- [Source: .github/workflows/ci.yml] (reference — has format step pattern to mirror)
- [Source: _bmad-output/implementation-artifacts/1-6-set-up-minimal-ci-quality-gate.md] (original quality-gate.yml creation context)
- [Source: _bmad-output/implementation-artifacts/7-1-package-and-validate-as-net-global-tool.md] (previous story — 199 tests green, build clean)
- [Source: CLAUDE.md] (target framework: net10.0; format command syntax)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Added `with: dotnet-version: '10.0.x'` to the `Setup .NET` step in `quality-gate.yml` to pin the SDK version and prevent silent breakage if the runner default changes.
- Inserted a `Format` step (`dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`) between `Restore` and `Build`, mirroring the pattern already used in `ci.yml`.
- Final pipeline step order: Checkout → Setup .NET → Restore → Format → Build → Test → Upload TRX.
- `Upload TRX results` step preserved intact with `if: always()`.
- Local validations: `dotnet format --verify-no-changes` exits 0; `dotnet build --warnaserror` → 0 errors, 0 warnings; `dotnet test` → 199 passed, 0 failed.

### File List

- `.github/workflows/quality-gate.yml` (modified — added `dotnet-version: '10.0.x'` pin, added `Format` step)

## Change Log

- 2026-03-13: Implemented Story 7.2 — pinned SDK version to `10.0.x` in `Setup .NET` step and added `Format` step (`dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`) between `Restore` and `Build` in `quality-gate.yml`. All 199 tests pass, build 0 warnings, format clean.

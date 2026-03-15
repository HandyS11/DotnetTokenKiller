# Sprint Change Proposal — Command Scope Reduction

**Date:** 2026-03-15
**Prepared by:** Bob (Scrum Master) via Correct Course workflow
**Project:** DotnetTokenKiller
**Approval required from:** HandyS11 (Project Lead)

---

## Section 1: Issue Summary

### Problem Statement

During the Epic 8 retrospective, the team performed a structured analysis of all eleven implemented `dotnet` sub-command filters against their real-world LLM value. The finding was unambiguous: **six of ten filters do not justify their maintenance cost** because the commands they wrap already produce compact, low-noise output, have version-dependent output formats, or represent niche workflows that LLM agents do not meaningfully invoke.

### Context

All code was already implemented and integration-tested before this discovery surfaced. The integration testing itself (Epic 8) was what created the conditions to see the problem clearly: by writing real SDK-backed tests, the team directly observed which filters produced meaningful savings and which did not.

### Discovery Evidence (from epic-8-retro-2026-03-15.md)

| Command | Verdict | Reason |
|---|---|---|
| `build` | ✅ Keep | MSBuild noise is massive, consistent, and always present |
| `restore` | ✅ Keep | Package download progress is enormous noise |
| `clean` | ✅ Keep | Deterministic, always safe to compress to `✓` |
| `test` | ✅ Keep | xUnit/MSTest output is verbose; failure structure adds real value |
| `publish` | ❌ Remove | Output already compact; savings required inflated baseline to demonstrate |
| `pack` | ❌ Remove | Same as publish — modest gains don't justify maintenance cost |
| `run` | ❌ Remove | Stripping 3 preamble lines isn't worth the complexity |
| `format` | ❌ Remove | Output already small; SDK-10 format changes made filter brittle |
| `nuget` | ❌ Remove | `locals` output is tiny; `push` is niche; not a real LLM workflow |
| `ef` | ❌ Remove | Delegates to a third-party tool with version-dependent output we don't control |
| passthrough | ❌ Remove | Only existed to handle the commands we're now deleting |

**Timing note:** The tool is pre-release (not yet published to NuGet.org). This is the correct moment to make breaking scope changes with zero user impact.

---

## Section 2: Impact Analysis

### Epic Impact

| Epic | Impact | Detail |
|---|---|---|
| Epic 1 — Build Filter | ✅ None | Fully retained |
| Epic 2 — Test Filter | ✅ None | Fully retained |
| Epic 3 — Restore/Publish/Pack | ⚠️ Partial | Retain Story 3-1 (restore); remove 3-2 (publish) and 3-3 (pack) |
| Epic 4 — Clean/Run/EF/Format/NuGet/Passthrough | ⚠️ Partial | Retain Story 4-1 (clean); remove 4-2, 4-3, 4-4, 4-5, 4-6 |
| Epic 5 — Analytics | ✅ None | Still needed; now tracks 4 commands instead of 10 |
| Epic 6 — Config & Tee | ✅ None | Fully retained |
| Epic 7 — Distribution | ✅ None | Fully retained; smaller binary is a benefit |
| Epic 8 — Integration Tests | ⚠️ Partial | Remove integration tests for removed commands |

### Story Impact

Stories retroactively removed from scope:

| Story ID | Description | Action |
|---|---|---|
| 3-2 | `dotnet publish` filter with tests | Remove code + tests |
| 3-3 | `dotnet pack` filter with tests | Remove code + tests |
| 4-2 | `dotnet run` filter with tests | Remove code + tests |
| 4-3 | `dotnet ef` filter with tests | Remove code + tests |
| 4-4 | `dotnet format` filter with tests | Remove code + tests |
| 4-5 | `dotnet nuget` filter with tests | Remove code + tests |
| 4-6 | Passthrough for unrecognized subcommands | Remove code + tests |
| 8-4 | Integration tests for publish, pack, run | Remove entire story file + test class |
| 8-5 | Integration tests for format, nuget, passthrough | Remove entire story file + test class |
| 8-6 | Integration tests for ef | Remove entire story file + test class |

### Artifact Conflicts

| Artifact | Conflict | Required Update |
|---|---|---|
| PRD.md | FR4, FR5, FR7–FR11 describe removed commands | Remove those FRs; update MVP scope and FR Coverage Map |
| Architecture.md | Solution structure lists all 10 filter files; PassthroughRunUseCase | Remove from file listing; update Application layer diagram |
| Epics.md | Epic 3 and Epic 4 descriptions cover removed stories | Rewrite epic goals; mark removed stories as deleted |
| sprint-status.yaml | Tracks removed stories as "done" | Stories remain done but are marked as retroactively out of scope |

### Technical Impact

| Area | Impact |
|---|---|
| Application/Filters | Delete 6 filter classes + their test counterparts |
| Application/UseCases | Delete `PassthroughRunUseCase.cs` + tests |
| Cli (DI registration) | Remove DI registrations for 6 filters + passthrough use case |
| Cli (routing/command classes) | Remove 6 command classes (PublishCommand, PackCommand, RunCommand, EfCommand, FormatCommand, NugetCommand) + passthrough command |
| Infrastructure/Tests | No infrastructure changes needed |
| sample/ | Remove `SampleApp.EfCore` project; remove pack/publish/run/format/nuget/ef test fixtures |
| Integration test suite | Remove test classes for deleted commands; remove `ISkippableFact` skippable boilerplate for ef |

**Secondary benefit:** Binary size decreases; startup footprint slightly reduced; fewer regex compilations at startup.

---

## Section 3: Recommended Approach

### Recommended Path: Direct Adjustment with New Cleanup Epic

**Option evaluated:** Create a new Epic 9 scoped entirely to deletion and artifact cleanup. This is the cleanest approach because:

- All deleted code was tested and "done" — no rollback of other epics needed
- The cleanup is bounded and low-risk (deletion only)
- Planning artifacts need updating to reflect the narrowed scope as permanent record

**Effort:** Low–Medium
**Risk:** Low (all deletions; no new logic introduced)
**Timeline impact:** One sprint (5–7 stories)

### Why Not Rollback Epics 3 & 4?

The completed epic/story status accurately reflects what was built and tested. Rolling back the tracking status would lose historical record. Instead, we add Epic 9 to perform the deletion as first-class work with its own stories — this is preferable for traceability.

---

## Section 4: Detailed Change Proposals

---

### 4.1 PRD Changes

**File:** `_bmad-output/planning-artifacts/PRD.md`

#### 4.1.1 — Remove Functional Requirements FR4, FR5, FR7–FR11

**OLD (Requirements Inventory):**

```sh
FR4: The system shall provide a `dtk dotnet publish` command...
FR5: The system shall provide a `dtk dotnet pack` command...
FR7: The system shall provide a `dtk dotnet run` command...
FR8: The system shall provide a `dtk dotnet ef` command...
FR9: The system shall provide a `dtk dotnet format` command...
FR10: The system shall provide a `dtk dotnet nuget` command...
FR11: The system shall support passthrough mode for any unrecognized `dotnet` subcommand...
```

**NEW:** Remove all seven FR entries entirely. Renumber is NOT required — omitting them is sufficient and preserves traceability (gaps in FR numbering are acceptable).

**Rationale:** These commands were removed from scope based on Epic 8 retrospective analysis.

---

#### 4.1.2 — Update MVP Scope

**OLD:**

```sh
All `dotnet` subcommand filters (build, test, restore, publish, pack, clean, run, ef, format, nuget),
passthrough for unrecognized subcommands, `dtk gain` analytics, persistent command tracking,
JSON configuration, tee output recovery.
```

**NEW:**

```sh
Core `dotnet` subcommand filters (build, test, restore, clean), `dtk gain` analytics, persistent
command tracking, JSON configuration, tee output recovery.
```

**Rationale:** Reflects the four core high-noise commands that are the product's real value proposition.

---

#### 4.1.3 — Update FR Coverage Map

**OLD (FR Coverage Map section):**

```sh
FR4: Epic 3 — `dotnet publish` filter
FR5: Epic 3 — `dotnet pack` filter
FR7: Epic 4 — `dotnet run` filter
FR8: Epic 4 — `dotnet ef` filter
FR9: Epic 4 — `dotnet format` filter
FR10: Epic 4 — `dotnet nuget` filter
FR11: Epic 4 — Passthrough for unrecognized subcommands
```

**NEW:** Remove those seven lines entirely from the FR Coverage Map.

---

### 4.2 Architecture Changes

**File:** `_bmad-output/planning-artifacts/Architecture.md`

#### 4.2.1 — Remove From Application/Filters Listing in Solution Structure

**OLD (solution structure under Application/Filters):**

```sh
├── Filters/DotnetBuildFilter.cs
├── Filters/DotnetTestFilter.cs
├── Filters/DotnetRestoreFilter.cs
├── Filters/DotnetPublishFilter.cs
├── Filters/DotnetPackFilter.cs
├── Filters/DotnetCleanFilter.cs
├── Filters/DotnetRunFilter.cs
├── Filters/DotnetEfFilter.cs
├── Filters/DotnetFormatFilter.cs
├── Filters/DotnetNugetFilter.cs
```

**NEW:**

```sh
├── Filters/DotnetBuildFilter.cs
├── Filters/DotnetTestFilter.cs
├── Filters/DotnetRestoreFilter.cs
├── Filters/DotnetCleanFilter.cs
```

---

#### 4.2.2 — Remove PassthroughRunUseCase From UseCases Listing

**OLD:**

```sh
├── UseCases/FilteredRunUseCase.cs
├── UseCases/PassthroughRunUseCase.cs
├── UseCases/GainReportUseCase.cs
```

**NEW:**

```sh
├── UseCases/FilteredRunUseCase.cs
├── UseCases/GainReportUseCase.cs
```

---

### 4.3 Epics Document Changes

**File:** `_bmad-output/planning-artifacts/epics.md`

#### 4.3.1 — Rewrite Epic 3 Goal

**OLD:**

```md
### Epic 3: Restore, Publish & Pack Filters

Users can run `dtk dotnet restore`, `dtk dotnet publish`, and `dtk dotnet pack` and receive
one-line summaries instead of verbose MSBuild output, with meaningful output paths shown for
publish and pack operations.
**FRs covered:** FR3, FR4, FR5
```

**NEW:**

```md
### Epic 3: Restore Filter

Users can run `dtk dotnet restore` and receive a one-line summary instead of verbose NuGet
package download output — eliminating the majority of package resolution noise from LLM context.
**FRs covered:** FR3

> **Note:** Stories 3-2 (publish) and 3-3 (pack) were removed from scope by Sprint Change
> Proposal 2026-03-15 following Epic 8 retrospective analysis. The corresponding code was
> deleted in Epic 9.
```

---

#### 4.3.2 — Rewrite Epic 4 Goal

**OLD:**

```md
### Epic 4: Remaining Filters & Passthrough Coverage

Every `dotnet` subcommand works with DTK — `clean`, `run`, `ef`, `format`, and `nuget` all
produce compact output, and any unrecognized subcommand silently passes through with exit code
preserved. The filter suite is now complete.
**FRs covered:** FR6, FR7, FR8, FR9, FR10, FR11
```

**NEW:**

```md
### Epic 4: Clean Filter

Users can run `dtk dotnet clean` and receive a single success marker or targeted error lines
instead of the full MSBuild clean output — the highest token reduction ratio of any filter (~95%).
**FRs covered:** FR6

> **Note:** Stories 4-2 (run), 4-3 (ef), 4-4 (format), 4-5 (nuget), and 4-6 (passthrough)
> were removed from scope by Sprint Change Proposal 2026-03-15 following Epic 8 retrospective
> analysis. The corresponding code was deleted in Epic 9.
```

---

#### 4.3.3 — Add New Epic 9 Entry

At the end of the epic list, add:

```markdown
### Epic 9: Command Scope Reduction (Cleanup)

Remove all non-core sub-command filters and their associated code, tests, CLI commands,
DI registrations, sample projects, and integration tests. Update PRD, Architecture, and
Epics documentation to reflect the final 4-command scope. Leave the codebase clean with
zero dead code.

**FRs covered:** N/A (deletion epic)
**Scope basis:** Sprint Change Proposal 2026-03-15
```

---

### 4.4 Sprint Status Changes

**File:** `_bmad-output/implementation-artifacts/sprint-status.yaml`

Add Epic 9 entries for the cleanup stories:

```yaml
  epic-9: in-progress
  9-1-remove-non-core-application-filters: backlog
  9-2-remove-passthrough-use-case-and-cli-commands: backlog
  9-3-remove-integration-tests-for-non-core-commands: backlog
  9-4-remove-non-core-sample-projects-and-fixtures: backlog
  9-5-update-planning-artifacts-prd-architecture-epics: backlog
```

---

## Section 5: Implementation Handoff

### Change Scope Classification: **Moderate**

Rationale: Requires backlog creation (Epic 9 stories) and planning artifact updates across 3 documents, but all changes are deletions — no architectural re-thinking needed. Product Owner and Scrum Master coordination required to update artifacts before dev work begins.

### Recommended Story Execution Order

| Order | Story | Responsible Agent | Notes |
|---|---|---|---|
| 1st | 9-2 Remove passthrough use case & CLI commands | dev (Amelia) | Remove routing before filters, avoids dangling references |
| 2nd | 9-1 Remove non-core application filters | dev (Amelia) | Core deletion — 6 filter files + unit tests |
| 3rd | 9-3 Remove integration tests for non-core commands | dev (Amelia) | Remove stories 8-4, 8-5, 8-6 test classes + fixtures |
| 4th | 9-4 Remove non-core sample projects and fixtures | dev (Amelia) | Remove `SampleApp.EfCore`, ef/run/format/nuget fixtures |
| 5th | 9-5 Update planning artifacts | tech-writer (Paige) / sm (Bob) | Apply all changes from Section 4 of this proposal |

### Success Criteria

- [ ] `dotnet build` succeeds with zero warnings after all deletions
- [ ] `dotnet test` passes all remaining unit tests (target: ~199 tests, no regressions)
- [ ] Integration tests for build, restore, clean, test continue to pass
- [ ] No references to removed filter classes anywhere in the codebase
- [ ] `dtk --help` shows only: `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `gain`
- [ ] PRD, Architecture, and Epics documents reflect the 4-command scope
- [ ] Binary is smaller than pre-Epic-9 build (measurable in CI artifact size)

### Artifacts Produced

- [x] Sprint Change Proposal (this document) — `_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md`
- [ ] Epic 9 stories (created by Scrum Master via `bmad-bmm-create-story`)
- [ ] Updated PRD, Architecture, Epics (updated in Story 9-5)
- [ ] Updated sprint-status.yaml (updated as stories complete)

---

## Approval

**Status:** Awaiting approval from HandyS11

**Options:**

- ✅ **Approve** — proceed to Sprint Planning to generate Epic 9 stories
- ✏️ **Edit** — revise specific sections
- ❌ **Reject** — abandon course correction

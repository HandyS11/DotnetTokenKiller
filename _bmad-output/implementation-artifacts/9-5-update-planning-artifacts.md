# Story 9.5: Update Planning Artifacts

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a project maintainer,
I want PRD, Architecture, and Epics documents updated to reflect the final 4-command scope,
So that all planning artifacts accurately represent what the tool actually does post-cleanup.

## Acceptance Criteria

1. **Given** `_bmad-output/planning-artifacts/PRD.md` is reviewed after updates **When** the Requirements Inventory section is read **Then** FR4, FR5, FR7, FR8, FR9, FR10, and FR11 do not appear **And** the MVP Scope mentions only `build`, `test`, `restore`, `clean`.

2. **Given** `_bmad-output/planning-artifacts/PRD.md` is reviewed after updates **When** the FR Coverage Map section is read **Then** FR4, FR5, FR7, FR8, FR9, FR10, and FR11 entries are absent.

3. **Given** `_bmad-output/planning-artifacts/Architecture.md` is reviewed after updates **When** the solution structure listing is read **Then** only `DotnetBuildFilter.cs`, `DotnetTestFilter.cs`, `DotnetRestoreFilter.cs`, and `DotnetCleanFilter.cs` appear in the Filters listing **And** `PassthroughRunUseCase.cs` does not appear anywhere in the document.

4. **Given** `_bmad-output/planning-artifacts/epics.md` is reviewed after updates **When** Epic 3 and Epic 4 overviews are read **Then** Epic 3 describes only the restore filter (FR3 only) **And** Epic 4 describes only the clean filter (FR6 only) **And** removal notes for the deleted stories reference Sprint Change Proposal 2026-03-15.

## Tasks / Subtasks

- [x] Task 1 — Update `PRD.md` Requirements Inventory (AC: #1)
  - [x] Delete the FR4 line (dotnet publish command)
  - [x] Delete the FR5 line (dotnet pack command)
  - [x] Delete the FR7 line (dotnet run command)
  - [x] Delete the FR8 line (dotnet ef command)
  - [x] Delete the FR9 line (dotnet format command)
  - [x] Delete the FR10 line (dotnet nuget command)
  - [x] Delete the FR11 line (passthrough for unrecognized subcommands)
  - [x] Update MVP Scope description (~line 48) to reference only `build`, `test`, `restore`, `clean` and remove passthrough mention

- [x] Task 2 — Update `PRD.md` FR Coverage Map (AC: #2)
  - [x] Remove FR4, FR5 entries from the coverage map (Epic 3 rows)
  - [x] Remove FR7, FR8, FR9, FR10, FR11 entries from the coverage map (Epic 4 rows)

- [x] Task 3 — Update `Architecture.md` solution structure listing (AC: #3)
  - [x] Remove `│   │   ├── UseCases/PassthroughRunUseCase.cs` line from directory tree
  - [x] Remove `DotnetPublishFilter.cs` line from Filters directory tree
  - [x] Remove `DotnetPackFilter.cs` line from Filters directory tree
  - [x] Remove `DotnetRunFilter.cs` line from Filters directory tree
  - [x] Remove `DotnetEfFilter.cs` line from Filters directory tree
  - [x] Remove `DotnetFormatFilter.cs` line from Filters directory tree
  - [x] Remove `DotnetNugetFilter.cs` line from Filters directory tree
  - [x] Remove `PassthroughRunUseCase` from Application layer description text (~line 127)
  - [x] Remove `publish`, `pack`, `run`, `ef`, `format`, `nuget` CLI routing table rows from command flow section (~lines 156–163)
  - [x] Remove `<other> → PassthroughRunUseCase` fallback routing entry
  - [x] Remove `PassthroughRunUseCase → (transient)` from DI registration section (~line 293)

- [x] Task 4 — Update `epics.md` Epic 3 and Epic 4 overviews (AC: #4)
  - [x] Rewrite Epic 3 heading/overview to "Restore Filter" covering FR3 only (remove "Publish & Pack" from title and description)
  - [x] Add a removal note under Epic 3 after Story 3.1 documenting that Stories 3.2 and 3.3 were removed per Sprint Change Proposal 2026-03-15
  - [x] Rewrite Epic 4 heading/overview to "Clean Filter" covering FR6 only (remove "Remaining Filters & Passthrough Coverage" framing)
  - [x] Add a removal note under Epic 4 after Story 4.1 documenting that Stories 4.2–4.6 were removed per Sprint Change Proposal 2026-03-15
  - [x] Update FR Coverage Map table in epics.md (lines ~88–96) to remove FR4, FR5, FR7–FR11 entries

## Dev Notes

### This Story Is Documentation-Only

No C# code is modified. No `.csproj`, `.cs`, or `.slnx` files change. This story edits three markdown files in `_bmad-output/planning-artifacts/`. Build and tests remain unaffected — there is no need to run `dotnet build` or `dotnet test` to validate this story. Validation is purely by reading the updated documents against the ACs.

---

### Epic 9 Execution Context

This is the final story in Epic 9 (Command Scope Reduction). All prior stories have already been implemented:

| Story | What was removed | Status |
|---|---|---|
| 9-1 | 6 filter classes + unit tests from Application layer | done |
| 9-2 | PassthroughRunUseCase + 6 CLI commands + DI registrations | done |
| 9-3 | Integration test classes for non-core commands (publish/pack/run, format/nuget/passthrough, ef) | done |
| 9-4 | `sample/SampleApp.EfCore/` project directory + slnx reference | done |
| **9-5** | **Planning artifact updates (this story)** | ready-for-dev |

The codebase now contains only the four core filters. This story aligns the written specifications with that reality.

---

### Exact Changes Required Per File

#### `_bmad-output/planning-artifacts/PRD.md`

**Line ~48 — MVP Scope update:**

Current text (approximate):

```sh
All `dotnet` subcommand filters (build, test, restore, publish, pack, clean, run, ef, format, nuget), passthrough for unrecognized subcommands, `dtk gain` analytics, persistent command tracking, JSON configuration, tee output recovery.
```

Target text:

```sh
Core `dotnet` subcommand filters (`build`, `test`, `restore`, `clean`), `dtk gain` analytics, persistent command tracking, JSON configuration, tee output recovery.
```

**Lines ~112–128 — Requirements Inventory deletions:**

Delete the following FR lines entirely:

- `FR4: The system shall provide a \`dtk dotnet publish\` command...`
- `FR5: The system shall provide a \`dtk dotnet pack\` command...`
- `FR7: The system shall provide a \`dtk dotnet run\` command...`
- `FR8: The system shall provide a \`dtk dotnet ef\` command...`
- `FR9: The system shall provide a \`dtk dotnet format\` command...`
- `FR10: The system shall provide a \`dtk dotnet nuget\` command...`
- `FR11: The system shall support passthrough mode...`

After deletion, FR numbering in text can remain sparse (FR3, FR6, FR12–FR24) — do not renumber, as renumbering would break cross-references in the epic epic.

**FR Coverage Map — delete rows:**

```sh
FR4: Epic 3 — `dotnet publish` filter
FR5: Epic 3 — `dotnet pack` filter
FR7: Epic 4 — `dotnet run` filter
FR8: Epic 4 — `dotnet ef` filter
FR9: Epic 4 — `dotnet format` filter
FR10: Epic 4 — `dotnet nuget` filter
FR11: Epic 4 — Passthrough for unrecognized subcommands
```

---

#### `_bmad-output/planning-artifacts/Architecture.md`

**Directory tree block — Filters section (~lines 68–74), delete 6 filter lines:**

```sh
│   │   ├── Filters/DotnetPublishFilter.cs
│   │   ├── Filters/DotnetPackFilter.cs
│   │   ├── Filters/DotnetRunFilter.cs
│   │   ├── Filters/DotnetEfFilter.cs
│   │   ├── Filters/DotnetFormatFilter.cs
│   │   ├── Filters/DotnetNugetFilter.cs
```

**Directory tree block — UseCases (~line 63), delete 1 line:**

```sh
│   │   ├── UseCases/PassthroughRunUseCase.cs
```

**Application layer description (~line 127), update bullet:**

Current:

```sh
- **Use cases**: `FilteredRunUseCase`, `PassthroughRunUseCase`, `GainReportUseCase`
```

Target:

```sh
- **Use cases**: `FilteredRunUseCase`, `GainReportUseCase`
```

**CLI command routing table (~lines 156–163), delete 6 filter rows + passthrough fallback:**

```sh
│   ├── publish [args...]    → DotnetPublishCommand → FilteredRunUseCase + DotnetPublishFilter
│   ├── pack [args...]       → DotnetPackCommand    → FilteredRunUseCase + DotnetPackFilter
│   ├── run [args...]        → DotnetRunCommand     → FilteredRunUseCase + DotnetRunFilter
│   ├── ef [args...]         → DotnetEfCommand      → FilteredRunUseCase + DotnetEfFilter
│   ├── format [args...]     → DotnetFormatCommand  → FilteredRunUseCase + DotnetFormatFilter
│   ├── nuget [args...]      → DotnetNugetCommand   → FilteredRunUseCase + DotnetNugetFilter
│   └── <other> [args...]    → (fallback)           → PassthroughRunUseCase
```

**DI registration section (~line 293), delete:**

```sh
PassthroughRunUseCase → (transient)
```

---

#### `_bmad-output/planning-artifacts/epics.md`

**Epic 3 heading (~line 124):**

Current: `## Epic 3: Restore, Publish & Pack Filters`
Target: `## Epic 3: Restore Filter`

**Epic 3 overview text (~line 125–128):**

Current text describes FR3, FR4, FR5.
Target: describe FR3 only. Add a note that Stories 3-2 and 3-3 were removed per Sprint Change Proposal 2026-03-15.

Suggested addition after Story 3.1 section:

```markdown
> **Note (Sprint Change Proposal 2026-03-15):** Stories 3.2 (dotnet publish) and 3.3 (dotnet pack) were removed during Epic 9 scope reduction. Their filter implementations have been deleted from the codebase.
```

**Epic 4 heading (~line 129):**

Current: `## Epic 4: Remaining Filters & Passthrough Coverage`
Target: `## Epic 4: Clean Filter`

**Epic 4 overview text:**

Update to describe FR6 only. Add a note that Stories 4-2 through 4-6 were removed per Sprint Change Proposal 2026-03-15.

Suggested addition after Story 4.1 section:

```markdown
> **Note (Sprint Change Proposal 2026-03-15):** Stories 4.2 (dotnet run), 4.3 (dotnet ef), 4.4 (dotnet format), 4.5 (dotnet nuget), and 4.6 (passthrough) were removed during Epic 9 scope reduction. Their filter implementations have been deleted from the codebase.
```

**FR Coverage Map section in epics.md (lines ~88–96) — delete rows:**

```sh
FR4: Epic 3 — `dotnet publish` filter
FR5: Epic 3 — `dotnet pack` filter
FR7: Epic 4 — `dotnet run` filter
FR8: Epic 4 — `dotnet ef` filter
FR9: Epic 4 — `dotnet format` filter
FR10: Epic 4 — `dotnet nuget` filter
FR11: Epic 4 — Passthrough for unrecognized subcommands
```

---

### Important Constraints

- **Do not renumber FRs.** FR4, FR5, FR7–FR11 should simply be absent — sparse numbering is fine. Renumbering creates cross-reference drift.
- **Do not modify Epic 3 Stories 3.1–3.3 content** (other than the note addition) — the story content itself is historical record.
- **Do not delete Epic 4 story sections** — add removal notes only; the story content is historical record.
- **Epics.md has two FR Coverage Map sections** — one in the Requirements Inventory (~line 88) and one embedded in each epic overview. Both need FR4/5/7–11 rows removed.
- These are markdown files — use the Edit tool for precise targeted changes, not whole-file rewrites, to minimize diff noise.

---

### Project Structure Notes

All files being modified are in:

```sh
_bmad-output/planning-artifacts/
  PRD.md
  Architecture.md
  epics.md
```

No `src/`, `tests/`, `sample/`, or `_bmad/` files are touched.

### References

- Epic 9 Story 9.5 definition: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md#story-95-update-planning-artifacts) — lines 1368–1410
- Sprint Change Proposal 2026-03-15: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md` — Section 4 (technical impact)
- Story 9-4 (last preceding story): [_bmad-output/implementation-artifacts/9-4-remove-non-core-sample-projects-and-fixtures.md](_bmad-output/implementation-artifacts/9-4-remove-non-core-sample-projects-and-fixtures.md)
- PRD: [_bmad-output/planning-artifacts/PRD.md](_bmad-output/planning-artifacts/PRD.md)
- Architecture: [_bmad-output/planning-artifacts/Architecture.md](_bmad-output/planning-artifacts/Architecture.md)
- Epics: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- **PRD.md**: Updated MVP Scope line to reference only `build`, `test`, `restore`, `clean`. Deleted FR4, FR5, FR7, FR8, FR9, FR10, FR11 lines from Requirements Inventory. No FR Coverage Map section existed in PRD.md — AC #2 already satisfied (entries were absent).
- **Architecture.md**: Removed `PassthroughRunUseCase.cs` from UseCases directory tree. Removed 6 non-core filter files (`DotnetPublishFilter.cs`, `DotnetPackFilter.cs`, `DotnetRunFilter.cs`, `DotnetEfFilter.cs`, `DotnetFormatFilter.cs`, `DotnetNugetFilter.cs`) from Filters directory tree. Removed corresponding non-core command files from Cli/Commands directory tree. Updated Application layer description to remove `PassthroughRunUseCase`. Removed 6 non-core CLI routing rows and `PassthroughRunUseCase` fallback from command tree. Removed `PassthroughRunUseCase → (transient)` from DI registration.
- **epics.md**: Rewrote Epic 3 to "Restore Filter" (FR3 only). Rewrote Epic 4 to "Clean Filter" (FR6 only). Added Sprint Change Proposal 2026-03-15 removal notes after Story 3.1 and Story 4.1. Removed FR4, FR5, FR7–FR11 from FR Coverage Map.
- No C# code was modified. Build and tests were not run (documentation-only story as specified in Dev Notes).

### File List

**Modified:**

- `_bmad-output/planning-artifacts/PRD.md`
- `_bmad-output/planning-artifacts/Architecture.md`
- `_bmad-output/planning-artifacts/epics.md`

## Change Log

- 2026-03-15: Updated PRD.md, Architecture.md, and epics.md to reflect final 4-command scope (build, test, restore, clean). Removed all references to publish, pack, run, ef, format, nuget, and passthrough from planning artifacts.

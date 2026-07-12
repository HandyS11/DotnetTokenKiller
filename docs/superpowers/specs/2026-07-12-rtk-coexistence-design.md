# rtk-aware `dtk integrate claude` — Design

**Date:** 2026-07-12
**Status:** Approved (brainstorming)
**Branch:** feature work off `develop`

## Problem

On a machine that runs **both** the rtk PreToolUse hook (`rtk hook claude`, registered in the
user-global `~/.claude/settings.json`) and dtk's own `dotnet → dtk dotnet` PreToolUse hook, both
hooks fire on every `Bash` command and both try to rewrite `dotnet …`. Claude Code runs PreToolUse
hooks in parallel and the last writer wins — the docs explicitly warn against two hooks modifying
the same input and call the ordering non-deterministic. Hook ordering therefore cannot resolve this.

The only robust fix is **single ownership**: each command has exactly one owner. rtk supports this
directly via `~/.config/rtk/config.toml`:

```toml
[hooks]
exclude_commands = ["dotnet"]
```

With `dotnet` excluded, rtk's hook returns "no rewrite" for `dotnet …`, leaving dtk as the sole
owner of build/test/restore/clean/format, while `rtk dotnet …` remains available for manual use and
rtk keeps owning everything else (`git`, `grep`, `ls`, …).

This design makes `dtk integrate claude` perform that reconciliation automatically, turning a manual
recipe into a product feature for every rtk + dtk user.

## Scope & Decisions

Confirmed with the user during brainstorming:

1. **Auto-write**, not advise-only: dtk merges `"dotnet"` into rtk's config itself.
2. **Skip if already excluded**: if `[hooks].exclude_commands` already contains `"dotnet"`, do
   nothing and stay silent.
3. **Scan user + project settings**: detect the rtk hook across `~/.claude/settings.json`,
   `~/.claude/settings.local.json`, and the project's `.claude/settings.json` +
   `.claude/settings.local.json`.

Out of scope: touching rtk's `transparent_prefixes` (dtk's own hook already checks the preceding
token and rtk already leaves `dtk dotnet …` alone, so it is unnecessary); any change to the shipped
hook schema (already emits `hookSpecificOutput.updatedInput`); the rest of Roadmap 06 (#2–#8).

## Approach for the TOML edit

**Chosen: Tomlyn with a minimal, format-preserving edit.**

- Add [Tomlyn](https://github.com/xoofx/Tomlyn) via central package management.
- Parse the existing config to **detect** state (does `[hooks].exclude_commands` exist? does it
  already contain `"dotnet"`?).
- **Write** with the smallest possible change so the user's hand-written foreign config (comments,
  unrelated keys, formatting) survives:
  - File missing → write a clean minimal document (`[hooks]\nexclude_commands = ["dotnet"]\n`).
  - `[hooks].exclude_commands` exists without `dotnet` → insert `"dotnet"` into that array.
  - `[hooks]` exists without the key → add the `exclude_commands` line under the header.
  - No `[hooks]` table → append a `[hooks]` section.

Rejected alternatives:

- **Hand-rolled string/regex merge (no dependency)** — fragile on multi-line arrays, inline tables,
  and comments; editing a *foreign* global config incorrectly is user-hostile.
- **Tomlyn model round-trip (`TomlTable` → serialize)** — simplest, but discards the user's comments
  and formatting on rewrite. Unacceptable for a config dtk does not own.

## Components

### 1. `RtkHookCoexistence` (new — `src/DotnetTokenKiller.Application/Integration`)

Single responsibility: given a project directory, detect the rtk hook and reconcile rtk's config.

- **Constructor** takes the user `~/.claude` directory and the `~/.config/rtk/config.toml` path.
  Real values are computed from `Environment.GetFolderPath`/`Environment.SpecialFolder.UserProfile`
  (or `HOME`); tests inject temp paths so no test ever reads or writes the real machine's config.
- **Detect**: read each of the four candidate settings files that exist, parse JSON tolerantly, and
  return `true` if any `PreToolUse` hook entry has a `command` string that invokes rtk (a `rtk`
  token followed by `hook`). Missing or malformed files are skipped, not fatal.
- **Reconcile** (only when detected): read the rtk config; if `dotnet` is already excluded, return a
  silent no-op. Otherwise merge `"dotnet"` in (creating table/key/file as needed), and report the
  config path plus a one-line explanatory Note. On malformed or unwritable config, do **not** throw:
  return an advisory Note carrying the manual recipe instead (graceful degradation to advice).

### 2. `IntegrationResult` / `IntegrationContext`

Add a `Notes` collection (advisory strings) alongside the existing Created/Updated/Skipped lists.

- The rtk config path flows through the existing **Created**/**Updated** lists (it renders as an
  absolute path because it is outside the project directory — desirable for a global file).
- A **Note** explains *why* a file outside the project was touched, e.g.
  *"Detected an rtk hook; excluded `dotnet` in ~/.config/rtk/config.toml so dtk owns dotnet
  commands. `rtk dotnet …` still works for manual use."*

### 3. `ClaudeCodeIntegrator`

After writing its own three artifacts, invoke `RtkHookCoexistence` and funnel its outcome
(created/updated path, notes) into the `IntegrationContext`. The collaborator is supplied via the
constructor; DI provides the real-paths default. Existing behavior when no rtk hook is present is
unchanged.

### 4. `IntegrateCommand`

After printing the created/updated/skipped file lines, render each `result.Notes` entry as an
advisory line (e.g. cyan). No note ⇒ no extra output — the common (non-rtk) case looks identical to
today.

### 5. DI + packages

- `DependencyInjection.cs`: register `ClaudeCodeIntegrator` with a `RtkHookCoexistence` built from
  real paths.
- `Directory.Packages.props` + `DotnetTokenKiller.Application.csproj`: add Tomlyn.

## Error Handling

| Situation | Behavior |
|---|---|
| No rtk hook detected | No config read, no write, no output. |
| rtk hook present, `dotnet` already excluded | Silent no-op (per decision #2). |
| rtk hook present, `dotnet` not excluded | Merge `"dotnet"`; report Created/Updated + Note. |
| rtk config malformed / unwritable | No throw; integration still succeeds; advisory Note with the manual recipe (path + snippet). |
| A settings file is malformed JSON | Skip that file during detection; never fail the integration. |

## Testing

**New `RtkHookCoexistenceTests`:**

- Hook in user `settings.json` / user `settings.local.json` / project settings / absent.
- Config missing → created with `[hooks].exclude_commands = ["dotnet"]`.
- Config has `[hooks]` without the key → key added.
- Config has `exclude_commands` array without `dotnet` → `dotnet` appended.
- Config already contains `dotnet` → no-op (file byte-unchanged), no Note.
- Existing comments / unrelated keys preserved after the edit.
- Malformed config → advisory Note, no throw, integration succeeds.
- Malformed settings JSON → detection skips it, no throw.

**Update `ClaudeCodeIntegratorTests`:** construct the SUT with **isolated temp** user `~/.claude` and
rtk config paths so no existing test reads or writes the real machine. Add a case: rtk hook present ⇒
result includes the rtk config in Created/Updated and a Note.

**`IntegrateCommand` rendering:** a result carrying a Note prints the advisory line; a result with no
Notes prints nothing extra.

## Files Touched

**New:**
- `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs`
- `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs`

**Modified:**
- `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs` (add `Notes`)
- `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs` (add `Notes`, pass through)
- `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (invoke coexistence)
- `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs` (render Notes)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (register real paths)
- `Directory.Packages.props` + `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj` (Tomlyn)
- `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs` (isolate paths, add rtk case)

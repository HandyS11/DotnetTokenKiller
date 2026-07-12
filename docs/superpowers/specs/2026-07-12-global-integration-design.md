# Global integration (`dtk integrate --global`) + SonarQube cleanup — Design

Date: 2026-07-12
Branch: `develop`

## Problem

DotnetTokenKiller is already distributed as a global .NET tool (`dotnet tool install -g`),
but its **AI-assistant integration** (`dtk integrate <provider>`) only writes artifacts into
the **current project directory** (`.claude/`, `.gemini/`, `.aider.conf.yml`, …). To match the
Rust Token Killer workflow — install once, works in every repository — the integration must be
installable into the user's **home** configuration so the hook fires across all projects without
per-repo setup.

Separately, the SonarQube `develop` quality gate is **failing** on `new_violations` (5 open code
smells). This design also cleans those up so the gate returns to green, and the new feature code
must not introduce new violations.

## Goals

- Add an opt-in `-g|--global` flag to `dtk integrate` that installs artifacts into the user's
  home config directory instead of a project.
- Keep **local (per-project) integration as the default** — fully backward compatible.
- Support `--global` for the providers that have a real home config home: **claude, gemini,
  aider**. Repository-scoped providers (copilot, cursor, windsurf, jetbrains) reject `--global`
  with a clear message.
- Resolve the 5 SonarQube code smells and keep the new code violation-free with ≥80% new-line
  coverage (the gate thresholds).

## Non-goals (YAGNI)

- No global **uninstall** command; existing `dtk reset` is unchanged.
- No `dtk doctor` global-integration detection.
- No inventing a home location for repo-only providers.
- No change to the local integration behavior or output.

## Decisions (confirmed with the user)

1. **Scope model**: new `--global` flag; **local stays the default**. Backward compatible.
2. **Global providers**: claude, gemini, aider. Others error out on `--global`.
3. **Global hook command root**: `$HOME`-rooted, e.g.
   `python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py` (portable across the user's machine, readable
   in `settings.json`).

## CLI surface

- New option on `IntegrateCommandSettings`: `-g|--global` (bool).
- `--global` and `--dir` are **mutually exclusive**: global ignores the project directory, so
  passing both is a usage error (exit 1) with a clear message.
- `--force` composes with either scope unchanged.
- Unsupported-provider path: `dtk integrate copilot --global` returns exit 1 with, e.g.:
  > `Error: Provider 'copilot' is repository-scoped and has no global config. Run
  > 'dtk integrate copilot' inside a project.`

## Architecture

### The local/global difference

Only two things change between scopes for a given provider:

|          | base directory              | hook command root                                             |
|----------|-----------------------------|--------------------------------------------------------------|
| local    | project dir (`$dir/.claude`)| env-var: `python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/…`      |
| global   | home dir (`~/.claude`)      | `$HOME`-absolute: `python3 "$HOME"/.claude/hooks/…`           |

The env-var root (`$CLAUDE_PROJECT_DIR`, `$GEMINI_PROJECT_DIR`) resolves to the *current project*,
which is wrong for a home-installed script — so global must root the command at `$HOME` instead.

### Changes by layer

Chosen approach: a **capability interface**, not a signature change to `IProviderIntegrator`.
Threading a scope parameter through `IProviderIntegrator.IntegrateAsync` would churn every
integrator and dozens of existing test call sites for the 4 providers that will never support
global. Instead, the existing `IntegrateAsync(directory, force, ct)` keeps its exact meaning
(local, project-scoped), and global is added as a separate opt-in capability that only the three
capable providers implement. This follows interface segregation and keeps existing tests untouched.

**Domain (`DotnetTokenKiller.Domain.Integration`)**

- Add a new interface:

  ```csharp
  public interface IGlobalIntegrator
  {
      Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken);
  }
  ```

- `IProviderIntegrator` is unchanged. No `IntegrationScope` enum (YAGNI — the CLI's `bool Global`
  plus `integrator is IGlobalIntegrator` fully covers routing and the capability check).

**Home resolution & testability**

- Add one small `internal sealed class HomePaths` in the Application layer with:
  - a **public parameterless ctor** resolving the real home
    (`Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`), and
  - an **internal ctor** taking an override home directory for tests.
- This mirrors the exact seam already used by `RtkHookCoexistence` (public default ctor +
  internal ctor taking `userClaudeDir`). Registered in DI; injected into the three global-capable
  integrators so tests can point it at a temp directory. Exposes resolved paths:
  `ClaudeDir` (`~/.claude`), `GeminiDir` (`~/.gemini`), `AiderConfPath` (`~/.aider.conf.yml`),
  `AiderInstructionsPath` (`~/.aider-dtk-instructions.md`), and `Home`.

**Integrators**

- `ClaudeCodeIntegrator`, `GeminiCliIntegrator`, `AiderIntegrator` additionally implement
  `IGlobalIntegrator`:
  - Take `HomePaths` via constructor for global path resolution.
  - `IntegrateGlobalAsync` mirrors the local flow but writes under the home base dir and uses the
    `$HOME`-rooted hook-command constant (local flow keeps its existing env-var-rooted constant).
  - The global hook **command** is a compile-time constant that quotes only the `$HOME` env var,
    mirroring the local `"$CLAUDE_PROJECT_DIR"/…` convention (e.g.
    `python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py`); only the **file write paths** and aider's
    `read:` path are resolved from `HomePaths`.
- `GitHubCopilotIntegrator`, `CursorIntegrator`, `WindsurfIntegrator`, `JetBrainsAiIntegrator` are
  unchanged and do **not** implement `IGlobalIntegrator`.

**`IntegrateUseCase`**

- Add `RunGlobalAsync(providerName, force, ct)`. It resolves the integrator, and if it is not an
  `IGlobalIntegrator`, throws a friendly `InvalidOperationException` naming the provider.
  Otherwise it calls `IntegrateGlobalAsync`. Existing `RunAsync` (local) is unchanged.
- `IntegrateCommand` already maps `InvalidOperationException` to exit 1, so the CLI reports it
  cleanly.

**`IntegrateCommand`**

- Read `settings.Global`; reject the `--global` + `--dir` combination (exit 1).
- When global, call `RunGlobalAsync` and echo home-relative paths in the created/updated/skipped
  output.

### Per-provider global specifics

- **claude** → `~/.claude/{settings.json, hooks/dotnet-to-dtk.py, skills/dotnet-token-killer/SKILL.md}`;
  hook command `python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py`; `PreToolUse`/`Bash`.
- **gemini** → `~/.gemini/{settings.json, hooks/dotnet-to-dtk.py, GEMINI.md}`;
  hook command `python3 "$HOME"/.gemini/hooks/dotnet-to-dtk.py`; `BeforeTool`/`run_shell_command`.
- **aider** → `~/.aider.conf.yml` + `~/.aider-dtk-instructions.md`; the `read:` entry uses the
  **absolute** path to `~/.aider-dtk-instructions.md` (a global conf cannot rely on a cwd-relative
  path resolving). The existing external-`read:`-key merge logic still applies.

Note: `MergeJsonSettingsAsync`'s `DeriveLegacyCommand` derives the pre-env-var relative form
structurally from any `"$VAR"/…`-shaped command; the `$HOME`-rooted global command follows the same
shape, so legacy-entry detection/replacement continues to work for global settings too.

## SonarQube cleanup (separate track)

All 5 are on `develop`; fixing them returns the gate to green.

| Rule  | Severity | Location                          | Fix                                                        |
|-------|----------|-----------------------------------|------------------------------------------------------------|
| S3776 | CRITICAL | `FilteredRunUseCase.cs:34`        | Extract helper method(s) to drop cognitive complexity 17→≤15 |
| S3776 | CRITICAL | `IntegratorHelpers.cs:174` (`MergeJsonSettingsAsync`) | Extract root-parse + hooks/event-node resolution into helpers (16→≤15) |
| S1192 | MINOR    | `ArgumentPreprocessor.cs:72`      | Named constant for `"dotnet"` literal (×5)                  |
| S1192 | MINOR    | `DotnetTestFilter.cs:87`          | Named constant for `"duration"` literal (×4)                |
| S1192 | MINOR    | `RtkHookCoexistence.cs:91`        | Named constant for `"hooks"` literal (×4)                   |

`IntegratorHelpers` is also touched by the feature; the complexity refactor and the feature edits
will be reconciled in the same file carefully (behavior-preserving extractions, covered by existing
tests).

## Testing (TDD)

- Per global-capable integrator: tests mirroring the existing local tests but pointed at a temp
  "home" via the `HomePaths` internal ctor. Assert:
  - correct home paths for each artifact,
  - `$HOME`-rooted hook command in `settings.json`,
  - merge / skip / `--force` behavior identical to local,
  - aider `read:` uses the absolute instructions path.
- `IntegrateUseCase`/`IntegrateCommand`: `--global` on a repo-only provider → exit 1 with the
  expected message; `--global` + `--dir` → exit 1.
- Existing local-integration tests must remain green unchanged (scope defaults to `Local`).
- Refactored smell sites keep their existing test coverage; add cases if a refactor exposes an
  untested branch, to hold new-line coverage ≥80%.

## Docs

- `README.md`: document `dtk integrate <provider> --global` under **AI Agent Setup**; note which
  providers support it.
- Update the managed skill/instruction text where it references installation, if needed.

## Rollout / sequencing

1. SonarQube cleanup track (independent, low risk) — can land first to green the gate.
2. `IGlobalIntegrator` interface + `HomePaths` seam.
3. Global-capable integrators (claude, gemini, aider) + `RunGlobalAsync` guard + CLI `--global` flag.
4. Docs.

The global claude/gemini flow reuses the existing `RtkHookCoexistence.ReconcileAsync`, passing
`HomePaths.Home` as the directory so the user-level rtk hook is still reconciled in global mode.

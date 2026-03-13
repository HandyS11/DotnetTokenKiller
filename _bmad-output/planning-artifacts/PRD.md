---
workflowType: 'prd'
workflow: 'edit'
classification:
  domain: 'developer-tooling'
  projectType: 'cli-tool'
  complexity: 'moderate'
inputDocuments: []
stepsCompleted: ['step-e-01-discovery', 'step-e-02-review', 'step-e-03-edit']
lastEdited: '2026-03-14'
editHistory:
  - date: '2026-03-14'
    changes: 'Full BMAD restructure; aligned dtk gain spec to implementation; added integration test requirements; added Journey 4; fixed FR12/FR24 implementation leakage; fixed NFR3/NFR11 measurability'
---

# DotnetTokenKiller — Product Requirements Document

## Executive Summary

DotnetTokenKiller (`dtk`) is a .NET CLI proxy that intercepts `dotnet` command output and returns compressed, information-dense summaries — eliminating 60–95% of tokens consumed by LLM-assisted development workflows (Claude Code, GitHub Copilot, Cursor, etc.).

**Differentiator:** Built natively in C# on .NET 10, distributed as a NuGet global tool — the only token-reduction proxy targeting the .NET ecosystem natively (inspired by [RTK](https://github.com/rtk-ai/rtk) for Rust).

**Target users:**

- .NET developers using AI coding assistants where CLI output fills context windows
- Development teams running CI/CD pipelines where LLM context is metered or billed
- Developers who want cleaner, machine-readable `dotnet` CLI output

## Success Criteria

SC1: Each command filter achieves its stated token reduction target (≥60% minimum), verified by automated tests against real fixture data.

SC2: Startup time is imperceptible to LLM agents: <150ms for the global tool.

SC3: Exit code returned to caller exactly matches the underlying `dotnet` process exit code on 100% of invocations.

SC4: Tool installs and runs on Windows, macOS, and Linux via `dotnet tool install -g DotnetTokenKiller` without platform-specific setup.

SC5: Tracking, tee, and configuration errors never appear in command output — silent failure in all infrastructure paths.

SC6: `dtk gain` produces correct token savings data aggregated across all tracked commands within the configured history window.

## Product Scope

### MVP

All `dotnet` subcommand filters (build, test, restore, publish, pack, clean, run, ef, format, nuget), passthrough for unrecognized subcommands, `dtk gain` analytics, persistent command tracking, JSON configuration, tee output recovery.

### Growth

Native AOT single binaries (<15 MB per platform) for <15ms startup. GitHub Releases CI/CD pipeline.

### Vision

Community-contributed filter plugins. Extended language/ecosystem support (non-`dotnet` commands).

### Out of Scope

- Non-`dotnet` commands (no `npm`, `cargo`, `git` support)
- Real-time streaming output filtering
- GUI or web interface
- Non-English CLI output

## User Journeys

### Journey 1: LLM Agent Running Build and Test

1. LLM agent invokes `dtk dotnet build` instead of `dotnet build`
2. DTK executes `dotnet build`, captures full output, strips restore/compile noise
3. Agent receives 1–5 line summary (errors + summary line) instead of 15–40 lines
4. On failure, structured error output with file paths, error codes, and counts
5. Agent invokes `dtk dotnet test`; receives failures-only + aggregated suite summary instead of 30–80+ lines
6. Agent proceeds with precise, actionable output — no context window waste

### Journey 2: Developer Reviewing Token Savings

1. Developer runs `dtk gain` after a day of LLM-assisted coding
2. DTK queries the tracking database for the last 30 days
3. Table displays per-command token savings; footer shows total runs and overall average
4. Developer uses `dtk gain --days 7 --project` to scope to current project this week
5. Developer uses `dtk gain --json` to pipe savings data to a script or share with team

### Journey 3: Developer Using dtk for Package and Restore Workflows

1. Developer runs `dtk dotnet restore` before a build; receives a single summary line instead of package download noise
2. Developer runs `dtk dotnet publish` to produce a release artifact; receives output path and any errors — no restore chatter
3. Developer runs `dtk dotnet pack` to produce a NuGet package; receives the `.nupkg` path only
4. Developer runs `dtk dotnet clean` to reset build outputs; receives `✓ dotnet clean` confirmation
5. Developer runs `dtk dotnet run` to start a local app; build preamble is stripped, application output preserved
6. Developer runs `dtk dotnet format --verify-no-changes`; receives list of files needing formatting or a clean pass confirmation
7. For any unrecognized subcommand (e.g., `dtk dotnet watch`), DTK passes through output unchanged with exit code preserved

### Journey 4: CI/CD Pipeline Usage

1. Pipeline replaces `dotnet test` with `dtk dotnet test` in build script
2. DTK runs tests, returns failures-only output on failure or single summary line on success
3. Pipeline receives correct exit code — non-zero on failure, zero on success
4. Log output is compact and machine-readable; no filter errors ever corrupt the log
5. On unexpected failure, tee saves full raw output to timestamped file for debugging

## Functional Requirements

### Command Filters

FR1: The system shall provide a `dtk dotnet build` command that intercepts `dotnet build` output and filters it to strip restore/compile noise while keeping errors, warnings, and summary (80–90% token reduction).

FR2: The system shall provide a `dtk dotnet test` command that intercepts `dotnet test` output and filters it to show only failures and aggregated suite summary (90–95% token reduction).

FR3: The system shall provide a `dtk dotnet restore` command that intercepts `dotnet restore` output and compacts it to a one-line summary (90–95% token reduction).

FR4: The system shall provide a `dtk dotnet publish` command that intercepts `dotnet publish` output and strips restore noise while keeping the output path and any errors (80–85% token reduction).

FR5: The system shall provide a `dtk dotnet pack` command that intercepts `dotnet pack` output and strips compile noise while keeping the .nupkg path (85–90% token reduction).

FR6: The system shall provide a `dtk dotnet clean` command that intercepts `dotnet clean` output and reduces it to a success marker or error lines (95%+ token reduction).

FR7: The system shall provide a `dtk dotnet run` command that intercepts `dotnet run` output and strips only the build preamble, preserving actual application output (60–80% token reduction).

FR8: The system shall provide a `dtk dotnet ef` command that intercepts `dotnet ef` output and compacts migration/database status messages (70–80% token reduction).

FR9: The system shall provide a `dtk dotnet format` command that intercepts `dotnet format` output and shows only files changed or needing changes (70–80% token reduction).

FR10: The system shall provide a `dtk dotnet nuget` command that intercepts `dotnet nuget` output and strips progress bars while keeping results (75–85% token reduction).

### Core Behaviour

FR11: The system shall support passthrough mode for any unrecognized `dotnet` subcommand (e.g., `dtk dotnet new`, `dtk dotnet watch`) with no filtering applied but exit code preserved.

FR12: The system shall track every command execution in a persistent tracking database recording: timestamp, command, project path, input tokens, output tokens, saved tokens, savings percentage, and execution time.

FR13: The system shall provide a `dtk gain` command that displays token savings analytics. In table mode, each row shows: Command | Tokens Saved. The footer row shows total run count, total tokens saved, and average savings percentage. Options: `--days N` (history window, default 30), `--project` (filter by current working directory), `--json` (output raw JSON).

FR14: The system shall preserve the exit code of the underlying `dotnet` process exactly.

FR15: The system shall forward all arguments after the subcommand name to the real `dotnet` process unchanged.

FR16: The system shall fall back to raw unfiltered output if the filter throws an exception, never producing empty or broken output.

FR17: The system shall support verbosity flags (`-v`/`--verbose`) on all dotnet subcommands: level 1 shows the command being run, level 2 shows raw output before filtering and timing details.

### Infrastructure

FR18: The system shall provide tee output recovery: on command failure, optionally save the full raw output to a timestamped file and append a one-line hint to the filtered output.

FR19: The system shall load and save user configuration as JSON, with configurable tracking (enabled/days/db path), display (colors/emoji/width), and tee (mode/directory/max files/max size) settings.

FR20: The system shall strip ANSI escape codes from captured output before filtering and token estimation.

FR21: The system shall shorten absolute file paths to project-relative paths in all filter output.

FR22: The system shall be packaged and distributed as a .NET Global Tool installable via `dotnet tool install -g DotnetTokenKiller` with command name `dtk`.

FR23: The system shall auto-clean tracking records older than 90 days on every write operation.

FR24: The system shall estimate token counts using a token estimation heuristic for savings calculation.

### Integration Testing

FR25: The integration test suite shall include fixture .NET projects (covering build, test, and restore scenarios) and invoke the `dtk` CLI end-to-end against them to verify filter output correctness and that token reduction targets are met under real conditions.

## Non-Functional Requirements

NFR1: **Performance** — Startup time must be imperceptible: <150ms for the global tool, <15ms for Native AOT binary.

NFR2: **Token Savings** — Each filter must achieve its stated token reduction target. A hard quality gate of ≥60% savings must pass for every filter against real fixture data in automated tests.

NFR3: **Reliability** — Tracking and tee errors must never surface to the user or affect command output (silent failure). DTK must never suppress exit codes or omit error lines present in the underlying `dotnet` output.

NFR4: **Testability** — Domain and Application layers must be fully unit-testable without any real I/O. All infrastructure is behind interfaces. The integration test project must exercise the CLI end-to-end against fixture .NET projects to validate real filter behaviour.

NFR5: **Single Responsibility** — Each filter module must handle exactly one subcommand.

NFR6: **Cross-Platform** — The tool must run on Windows, macOS, and Linux without platform-specific code in the Domain or Application layers.

NFR7: **AOT Compatibility** — All regex patterns must use `[GeneratedRegex]` source generators. JSON serialization must use source-generated contexts. Required for Native AOT publishing.

NFR8: **Memory** — <30 MB for the global tool, <10 MB for the Native AOT binary.

NFR9: **Binary Size** — Native AOT binaries must be <15 MB per platform.

NFR10: **Exit Code Fidelity** — The exit code returned to the caller must exactly match the underlying `dotnet` process exit code for CI/CD compatibility.

NFR11: **Overhead** — The tool must not add more than 50ms of wall-clock overhead compared to running `dotnet` directly, as measured by timing the same command with and without `dtk` wrapping.

## Output Format Conventions

All filters follow consistent output conventions:

**Success (no issues):**

```sh
✓ dotnet <subcommand> (<context>, <time>)
```

**Success with warnings:**

```sh
dotnet <subcommand>: 0 errors, N warnings (<context>, <time>)
---
  <grouped details>
```

**Failure (errors):**

```sh
dotnet <subcommand>: N errors, M warnings (<context>)
---
<structured error details>
```

**Tee hint (on failure with raw output saved):**

```sh
[full output: ~/.local/share/dtk/tee/1234567890_build.log]
```

**Key UX Rules:**

- Use `✓` prefix for success, no prefix for failures
- Use `---` separator for detail sections
- Shorten absolute paths to project-relative with forward slashes
- Truncate long messages (120 chars for diagnostics, 200 chars for test error messages)
- Group errors/warnings by file, sorted by error count descending
- Limit displayed items (max 15 test failures, max 5 top error codes, max 20 format files)

## Configuration

Config stored at:

- Windows: `%APPDATA%/dtk/config.json`
- Linux/macOS: `~/.config/dtk/config.json`

| Section | Setting | Default |
|---|---|---|
| Tracking | enabled | true |
| Tracking | historyDays | 90 |
| Tracking | dbPath | (platform default) |
| Display | colors | true |
| Display | emoji | true |
| Display | maxWidth | (terminal width) |
| Tee | mode | "failures" |
| Tee | maxFiles | 20 |
| Tee | maxFileSizeBytes | 1048576 (1 MB) |
| Tee | directory | (platform default) |

## Distribution

**Primary:** .NET Global Tool via NuGet

```sh
dotnet tool install -g DotnetTokenKiller
dotnet tool update -g DotnetTokenKiller
```

**Secondary (future):** Native AOT single binaries via GitHub Releases for users who need <15ms startup.

**Recommended rollout:**

- Phase 1 (MVP): Global tool — fastest to ship
- Phase 2: Native AOT publish profile
- Phase 3: GitHub Releases + CI/CD release pipeline

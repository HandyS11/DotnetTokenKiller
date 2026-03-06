---
project: DotnetTokenKiller
version: 0.1.0
status: approved
source: todo/01-OVERVIEW.md, todo/03-CLI-PARSING.md, todo/04-CORE-INFRASTRUCTURE.md
---

# DotnetTokenKiller — Product Requirements Document

## 1. Problem Statement

LLM-assisted development workflows (e.g., Claude Code, Copilot) consume large amounts of context tokens reading `dotnet` CLI output. A typical `dotnet build` produces 15–40 lines of MSBuild noise for what is ultimately a one-line result. A `dotnet test` run produces 30–80+ lines even when all tests pass. This inflates token costs, slows LLM response times, and clutters context windows — reducing the quality of AI assistance.

## 2. Product Vision

DotnetTokenKiller (CLI command: `dtk`) is a .NET CLI proxy that intercepts `dotnet` command output and applies intelligent filtering to reduce LLM token consumption by 60–95%. It sits transparently between the LLM and the terminal, forwarding commands to the real `dotnet` CLI and returning compressed, information-dense summaries.

Inspired by [RTK (Rust Token Killer)](https://github.com/rtk-ai/rtk), DTK is built natively in C# with .NET 10, distributed as a NuGet global tool, and targets the .NET developer ecosystem exclusively.

## 3. Target Users

- **.NET developers** using AI coding assistants (Claude Code, GitHub Copilot, Cursor, etc.)
- **Development teams** running CI/CD pipelines where LLM context is monitored or billed
- **Any developer** who wants cleaner, more readable `dotnet` CLI output

## 4. Scope

### In Scope

DTK covers the following `dotnet` CLI subcommands:

| DTK Command | Wraps | Filtering Strategy | Expected Savings |
|---|---|---|---|
| `dtk dotnet build` | `dotnet build` | Strip restore/compile noise, keep errors + summary | 80–90% |
| `dtk dotnet test` | `dotnet test` | Failures only + aggregated suite summary | 90–95% |
| `dtk dotnet restore` | `dotnet restore` | Compact: "✓ restored N packages (Xs)" | 90–95% |
| `dtk dotnet publish` | `dotnet publish` | Strip restore noise, keep output path + errors | 80–85% |
| `dtk dotnet pack` | `dotnet pack` | Strip compile noise, keep .nupkg path | 85–90% |
| `dtk dotnet clean` | `dotnet clean` | "✓ dotnet clean" | 95%+ |
| `dtk dotnet run` | `dotnet run` | Strip build preamble, preserve app output | 60–80% |
| `dtk dotnet ef` | `dotnet ef` | Compact migration/DB status | 70–80% |
| `dtk dotnet format` | `dotnet format` | Files changed only | 70–80% |
| `dtk dotnet nuget` | `dotnet nuget` | Strip progress, keep results | 75–85% |
| `dtk dotnet <other>` | any subcommand | Passthrough (no filtering) | 0% |
| `dtk gain` | — | Token savings analytics dashboard | — |

### Out of Scope

- Non-`dotnet` commands (no `npm`, `cargo`, `git` support — use RTK for those)
- Real-time streaming output filtering
- GUI or web interface
- Multi-language support (English output only targeted initially)

## 5. Functional Requirements

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

### Core Behavior

FR11: The system shall support passthrough mode for any unrecognized `dotnet` subcommand (e.g., `dtk dotnet new`, `dtk dotnet watch`) with no filtering applied but exit code preserved.

FR12: The system shall track every command execution in a SQLite database recording: timestamp, command, project path, input tokens, output tokens, saved tokens, savings percentage, and execution time.

FR13: The system shall provide a `dtk gain` command that displays token savings analytics via a Spectre.Console rich table with `--days`, `--project`, and `--json` options.

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

FR24: The system shall estimate token counts using the `chars / 4` heuristic for savings calculation.

## 6. Non-Functional Requirements

NFR1: **Performance** — Startup time must be imperceptible: <150ms for the global tool, <15ms for Native AOT binary.

NFR2: **Token Savings** — Each filter must achieve its stated token reduction target. A hard quality gate of ≥60% savings must pass for every filter against real fixture data in automated tests.

NFR3: **Reliability** — Tracking and tee errors must never surface to the user or affect command output (silent failure). DTK must never make output worse than running `dotnet` directly.

NFR4: **Testability** — Domain and Application layers must be fully unit-testable without any real I/O. All infrastructure is behind interfaces.

NFR5: **Single Responsibility** — Each filter module must handle exactly one subcommand.

NFR6: **Cross-Platform** — The tool must run on Windows, macOS, and Linux without platform-specific code in the Domain or Application layers.

NFR7: **AOT Compatibility** — All regex patterns must use `[GeneratedRegex]` source generators. JSON serialization must use source-generated contexts. Required for Native AOT publishing.

NFR8: **Memory** — <30 MB for the global tool, <10 MB for the Native AOT binary.

NFR9: **Binary Size** — Native AOT binaries must be <15 MB per platform.

NFR10: **Exit Code Fidelity** — The exit code returned to the caller must exactly match the underlying `dotnet` process exit code for CI/CD compatibility.

NFR11: **Overhead** — The tool must not consume more than a negligible amount of additional CPU time compared to running `dotnet` directly.

## 7. User Experience

### Output Format Conventions

All filters follow consistent output conventions:

**Success (no issues):**

```sh
✓ dotnet <subcommand> (<context>, <time>)
```

**Success with warnings:**

```sh
dotnet <subcommand>: 0 errors, N warnings (<context>, <time>)
═══════════════════════════════════════
  <grouped details>
```

**Failure (errors):**

```sh
dotnet <subcommand>: N errors, M warnings (<context>)
═══════════════════════════════════════
<structured error details>
```

**Tee hint (on failure with raw output saved):**

```sh
[full output: ~/.local/share/dtk/tee/1234567890_build.log]
```

### Key UX Rules

- Use `✓` prefix for success, no prefix for failures
- Use `═══` separator for detail sections
- Shorten absolute paths to project-relative with forward slashes
- Truncate long messages (120 chars for diagnostics, 200 chars for test error messages)
- Group errors/warnings by file, sorted by error count descending
- Limit displayed items (max 15 test failures, max 5 top error codes, max 20 format files)

## 8. Analytics: `dtk gain`

The `dtk gain` command renders a Spectre.Console table with:

| Column | Description |
|---|---|
| Command | `dotnet build`, `dotnet test`, etc. |
| Runs | Number of executions |
| Tokens Saved | Total tokens eliminated |
| Avg Savings % | Average reduction per run |

Summary line: total tokens saved + overall average savings %.

Options:

- `--days N` — time range (default: 30)
- `--project` — filter by current working directory
- `--json` — output raw JSON for LLM/tooling consumption

## 9. Configuration

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

## 10. Distribution

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

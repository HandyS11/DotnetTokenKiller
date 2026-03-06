# 04 — Core Infrastructure

## Overview

The infrastructure spans two layers in the Clean Architecture:

- **Domain layer** — defines contracts (interfaces) and value objects
- **Infrastructure layer** — implements those contracts with real I/O (SQLite, files, processes)
- **Application layer** — contains helpers (token estimation, ANSI stripping, text formatting)

| System | Domain Contract | Infrastructure Implementation |
|---|---|---|
| Token Tracking | `ITracker` | `SqliteTracker` |
| Configuration | `IConfigProvider` | `JsonConfigProvider` |
| Tee Output | `ITeeService` | `FileTeeService` |
| Command Execution | `ICommandRunner` | `ProcessCommandRunner` |

---

## 1. Token Tracking System

### Purpose

Record every command execution with timestamps, token counts, savings percentages, and execution time. Provides data for the `dtk gain` analytics command. Maintains a 90-day retention window with automatic cleanup.

### Domain Contract (`ITracker`)

Defines the tracking operations:
- **Record** — save a command execution with input/output token counts and timing
- **GetSummary** — retrieve aggregated savings data for a time range
- **GetHistory** — retrieve recent command records
- **Cleanup** — remove records older than retention period

### Domain Entities

- **`CommandRecord`** — represents a single tracked command execution: timestamp, original command, DTK command, project path, input tokens, output tokens, saved tokens, savings percentage, execution time
- **`GainSummary`** — aggregated analytics: total commands, total tokens saved, average savings, time range, per-command breakdown

### Infrastructure Implementation (`SqliteTracker`)

Uses `Microsoft.Data.Sqlite` to persist tracking data:

- Database location follows platform conventions: `%LOCALAPPDATA%/dtk/tracking.db` (Windows), `~/.local/share/dtk/tracking.db` (Linux/macOS)
- Overridable via `DTK_DB_PATH` environment variable
- Schema: single `commands` table with indexed `timestamp` and `project_path` columns
- Auto-creates schema on first use
- 90-day retention — old records cleaned on every write
- **Silent failure** — tracking errors never affect the user's command output

### TimedExecution Helper

Lives in the Application layer. A convenience wrapper that:

1. Starts a `Stopwatch` when created
2. Estimates token counts for input/output text
3. Records the execution via `ITracker` when complete
4. Catches and silently swallows any tracking errors

---

## 2. Configuration System

### Purpose

User-configurable settings for tracking, display, and tee behavior. Stored as JSON in the user's config directory.

### Domain Contract (`IConfigProvider`)

- **Load** — read configuration, return defaults if file doesn't exist
- **Save** — persist configuration to disk

### Domain Value Objects (`DtkConfig`)

Configuration is modeled as a hierarchy of value objects:

- **`TrackingConfig`** — enabled (bool), history days (int), database path override
- **`DisplayConfig`** — colors (bool), emoji (bool), max width (int)
- **`TeeConfig`** — enabled (bool), mode ("never" / "failures" / "always"), directory override, max files, max file size

All have sensible defaults so DTK works out-of-the-box with zero configuration.

### Infrastructure Implementation (`JsonConfigProvider`)

- Config path: `%APPDATA%/dtk/config.json` (Windows), `~/.config/dtk/config.json` (Linux/macOS)
- Uses `System.Text.Json` with source-generated serialization for AOT compatibility
- Auto-creates directory structure if missing
- Returns defaults when no config file exists

---

## 3. Tee Output Recovery

### Purpose

When a command fails, save the full raw output to a file so the user (or LLM) can re-read it without re-running the command. Returns a one-line hint pointing to the saved file.

### Domain Contract (`ITeeService`)

- **TeeAndHint** — given raw output, command slug, and exit code, optionally save to file and return a hint string (or null)

### Behavior Rules

| Mode | Exit Code 0 | Exit Code ≠ 0 |
|---|---|---|
| `never` | No tee | No tee |
| `failures` (default) | No tee | Save + return hint |
| `always` | Save | Save + return hint |

Additional rules:
- Skip tee if raw output is less than 500 characters (not worth saving)
- Limit saved files to `maxFiles` (default 20), deleting oldest first
- Truncate individual files to `maxFileSize` (default 1 MB)
- Sanitize command names for safe filenames

### Infrastructure Implementation (`FileTeeService`)

- Tee directory: `%LOCALAPPDATA%/dtk/tee/` (Windows), `~/.local/share/dtk/tee/` (Linux/macOS)
- Overridable via `DTK_TEE_DIR` environment variable or config
- File naming: `{unix_timestamp}_{command_slug}.log`
- Returns hint: `[full output: /path/to/file.log]`
- **Silent failure** — tee errors never affect command output

---

## 4. Command Execution

### Purpose

Run external processes (`dotnet build`, `dotnet test`, etc.), capture stdout/stderr, and return the exit code.

### Domain Contract (`ICommandRunner`)

Two modes:
- **RunCapturedAsync** — capture stdout and stderr as strings, return `CommandResult` (stdout, stderr, exit code)
- **RunPassthroughAsync** — inherit stdin/stdout/stderr (for passthrough commands), return just exit code

### Domain Value Object (`CommandResult`)

Simple record: `StdOut` (string), `StdErr` (string), `ExitCode` (int).

### Infrastructure Implementation (`ProcessCommandRunner`)

Uses `System.Diagnostics.Process`:

- `RunCapturedAsync`: redirects stdout/stderr, reads both streams concurrently (to avoid deadlock), waits for exit
- `RunPassthroughAsync`: inherits all I/O streams, waits for exit

---

## 5. Application Helpers

These live in the Application layer (not Infrastructure) because they are pure functions with no I/O dependencies.

### `TokenEstimator`

Estimates token count from text using the `chars / 4` heuristic (same approach used by RTK). This is a fast approximation, not an exact tokenizer count.

### `AnsiStrip`

Removes ANSI escape codes from text using a `[GeneratedRegex]` pattern. Essential for accurate token counting since ANSI codes add characters that aren't visible tokens.

### `TextHelpers`

Utility functions:
- **Truncate** — shorten strings with `...` suffix
- **FormatTokens** — human-readable token counts (`1.2K`, `3.5M`)
- **ShortenPath** — convert absolute paths to project-relative paths for readability

---

## Key Design Decisions

| Concern | Decision | Rationale |
|---|---|---|
| **Config format** | JSON | No extra dependency, System.Text.Json is built-in, AOT-compatible with source generators |
| **Tracking DB** | SQLite | Proven, single-file, no server needed, same as RTK |
| **Error handling** | Silent failure for tracking/tee | These features should never break the user's workflow |
| **Process I/O** | Concurrent stream reads | Prevents deadlock when both stdout and stderr have large output |
| **All contracts in Domain** | Interfaces + value objects | Enables unit testing Application layer without any real I/O |

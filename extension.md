# DTK Command Expansion Analysis

This document analyses every candidate `dotnet` subcommand that could be added to
DotnetTokenKiller (dtk), with exhaustive pros/cons for each, and a cross-cutting
readiness assessment of the current architecture.

**Date**: 2026-03-26
**Current supported commands**: `build`, `clean`, `format`, `restore`, `test`

---

## Table of Contents

1. [Architecture Recap](#architecture-recap)
2. [Cross-Cutting Readiness Assessment](#cross-cutting-readiness-assessment)
3. [Candidate Commands](#candidate-commands)
   - [dotnet format](#dotnet-format) ⭐ Highest priority
   - [dotnet publish](#dotnet-publish)
   - [dotnet pack](#dotnet-pack)
   - [dotnet msbuild](#dotnet-msbuild)
   - [dotnet tool restore](#dotnet-tool-restore)
   - [dotnet run](#dotnet-run)
   - [dotnet watch](#dotnet-watch)
   - [dotnet new](#dotnet-new)
   - [dotnet nuget](#dotnet-nuget)
   - [dotnet workload](#dotnet-workload)
4. [Required Changes Per Command](#required-changes-per-command)
5. [Architecture Changes Required](#architecture-changes-required)
6. [Prioritised Recommendation](#prioritised-recommendation)

---

## Architecture Recap

Understanding where changes are needed requires a clear picture of how the current
pipeline works:

```sh
dtk dotnet <subcommand> [args]
        │
        ▼
ArgumentPreprocessor.IsPassthrough()
  → If subcommand NOT in KnownSubcommands  → RunPassthroughAsync()  (no capture, no tracking)
  → If subcommand IS in KnownSubcommands   → Spectre CLI dispatch
        │
        ▼
DotnetXxxCommand  (Spectre AsyncCommand)
  → Injects:  FilteredRunUseCase  +  [FromKeyedServices(FilterKeys.Xxx)] IOutputFilter
        │
        ▼
FilteredRunUseCase.RunAsync()
  1. ICommandRunner.RunCapturedAsync()     → captures stdout + stderr (fully buffered)
  2. AnsiStrip.Strip()                     → removes ANSI escape sequences
  3. IOutputFilter.Apply(rawOutput)        → condenses to actionable signal
  4. Write filtered output to console
  5. ITeeService.TeeAndHintAsync()         → optionally save raw log to file
  6. ITracker.RecordAsync()               → persist token statistics to SQLite
  7. return exit code
```

**Key files to change when adding any new command:**

| File | What to add |
|---|---|
| `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs` | New string constant |
| `src/DotnetTokenKiller.Application/Filters/DotnetXxxFilter.cs` | New filter class |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | Keyed DI registration |
| `src/DotnetTokenKiller.Cli/Commands/DotnetXxxCommand.cs` | New CLI command class |
| `src/DotnetTokenKiller.Cli/Program.cs` | Spectre command registration |
| `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` | Add to `KnownSubcommands` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/` | Filter unit tests + fixtures |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/` | End-to-end test |
| `docfx/articles/` + `README.md` + `SKILL.md` | Documentation |

---

## Cross-Cutting Readiness Assessment

### What the architecture already handles well

| Scenario | Handled? | Notes |
|---|---|---|
| Filter returns empty string | ✅ Yes | `FilteredRunUseCase` writes nothing; tracking records 0 tokens |
| Empty raw output (process produces nothing) | ✅ Yes | `TokenEstimator.Estimate("")` → 0; savings % guard against div-by-zero |
| Filter throws an exception | ✅ Yes | Catch-all silently falls back to raw output |
| Tracking database error | ✅ Yes | Swallowed; command still returns correct exit code |
| Exit code propagation | ✅ Yes | Preserved exactly through all layers |
| ANSI codes in output | ✅ Yes | `AnsiStrip.Strip()` applied before every filter |
| Output with absolute paths | ✅ Yes | `TextHelpers.ShortenPath()` available for any filter to use |
| NO_COLOR environment variable | ✅ Yes | Post-filter emoji replacement already in `FilteredRunUseCase` |
| Cancellation (Ctrl+C) | ✅ Yes | Process is killed via `CancellationToken` registration |
| Very large output (100KB+) | ✅ Yes | Concurrent stdout/stderr reads prevent deadlock |
| Keyed DI for new filter | ✅ Yes | Just add `services.AddKeyedTransient<IOutputFilter, NewFilter>(key)` |
| New CLI command scaffold | ✅ Yes | Copy any existing `DotnetXxxCommand.cs` — 15 lines of boilerplate |

### What the architecture does NOT handle yet

| Gap | Severity | Affects |
|---|---|---|
| **`IOutputFilter.Apply(string)` receives no exit code** | ⚠️ High | `format`, `pack`, `publish` on success produce minimal/empty output, but filter cannot distinguish exit 0 vs exit 1 when both produce empty stderr |
| **No "synthesise success message" mechanism** | ⚠️ High | `format` exits 0 with zero bytes of output — dtk would be completely silent even though it ran |
| **Fully buffered capture only** | ⚠️ High | `watch`, `run` are long-lived streaming processes; buffering to end-of-process is wrong |
| **No subcommand argument routing** | ⚠️ Medium | `dotnet tool restore` requires handling `tool restore` as a two-token subcommand, not just `tool` |
| **Passthrough does not track** | ℹ️ Low | Commands that stay in passthrough mode are invisible to `dtk gain` |
| **`FilteredRunUseCase` hard-codes the success-message in each filter** | ℹ️ Low | Each filter independently formats `✓ dotnet xxx` — no central "did the command succeed?" hook |

---

## Candidate Commands

---

### `dotnet format`

**Description**: Applies editorconfig/whitespace/style rules to source files (or verifies with `--verify-no-changes`).

#### Output behaviour (confirmed by measurement)

| Scenario | stdout bytes | exit code |
|---|---|---|
| Nothing to format (`--no-restore`, no verbosity flag) | **0** | 0 |
| Nothing to format (`--verbosity diagnostic`) | ~5 700 chars | 0 |
| Files formatted | Formatted file list | 0 |
| `--verify-no-changes`, violations found | File list + diagnostics | 1+ |

#### Token savings potential

- Default run (no verbosity): 0 input tokens → 0 savings. The tool is already silent.
- Diagnostic verbosity run: ~1 400 tokens input → possible savings of 90%+.
- **The real problem**: silence ≠ savings. The user gets zero confirmation that `format` ran at all.
  An LLM agent running `dtk dotnet format` in a loop cannot tell whether it executed or was skipped.

#### Pros of adding support

- Completes the "inner loop" set: build → test → format is the full CI core.
- A filter can synthesise `✓ dotnet format (nothing to format)` on empty-output-exit-0, providing
  crucial positive confirmation that the command ran.
- `--verify-no-changes` output (file paths + diagnostics) can be condensed in the same way as
  build warnings.
- Diagnostic/verbose runs produce significant noise (all config file paths — `Project X is using
  configuration from C:\Program Files\dotnet\sdk\...`); these could be stripped to 90%+ savings.
- Token tracking for format is currently impossible (passthrough → no record); adding it surfaces
  format frequency in `dtk gain`.

#### Cons / challenges

- **The empty-output problem requires an architecture change**. `IOutputFilter.Apply(string)` only
  receives the raw output string. An empty string on exit 0 and an empty string on exit 1 look
  identical to the filter. Three possible fixes:
  1. **Extend the interface**: `string Apply(string rawOutput, int exitCode)` — breaking change,
     all 4 existing filters need updating.
  2. **Add an exit-code parameter to `FilteredRunUseCase`**: inject exit code into the filter as
     a context object rather than changing the interface.
  3. **Heuristic**: treat "empty output + exit 0 = success". This works for format because format
     *does* echo changed file names on stdout when it makes changes. Empty stdout on exit 0 reliably
     means "nothing changed". Caveat: fragile if Microsoft changes format's output behaviour.
- `dotnet format` is a separate tool from MSBuild; its output format is completely different from
  the other four filters. A brand-new `DotnetFormatFilter` is required.
- `--verify-no-changes` is used in CI differently from a plain `format` run. Users may want dtk
  to handle both. The filter would need to detect which mode it's in (the args are passed through
  to dotnet but not visible inside `IOutputFilter.Apply()`).
- Format can be slow on large solutions (6–8 s for this repo); users may want the elapsed time
  in the `✓` confirmation.
- Unlike build or test, format has no concept of "N errors" — violations are just files-to-change.
  The output schema is fundamentally different.

#### Verdict: HIGH VALUE — requires one small architecture change (exit code in filter context)

---

### `dotnet publish`

**Description**: Compiles, links, and writes self-contained or framework-dependent deployment
artefacts to a publish directory.

#### Output behaviour (confirmed by measurement)

| Scenario | stdout | bytes |
|---|---|---|
| Success, single project, incremental | ~7 lines (restore + build arrows + publish arrow) | ~704 chars |
| Success, full rebuild | Same pattern but more project arrows | ~1–3 KB |
| Failure (compile errors) | Identical format to `dotnet build` errors | 1–5 KB |

#### Token savings potential

- ~30–85 % depending on project count and whether errors are present.
- The raw output is nearly identical to `dotnet build` output — restore lines, project arrows,
  then a `→ publish/` line at the end.

#### Pros of adding support

- Extremely common in CI pipelines; agents frequently run `dotnet publish` as the deployment step.
- **The `DotnetBuildFilter` can be reused almost entirely** — publish output is a superset:
  same MSBuild diagnostics, same project arrows, just an additional `publish/` output line.
- A `DotnetPublishFilter` could delegate to build-filter logic and append the publish path:
  `✓ dotnet publish → src/Foo/bin/Release/net10.0/publish/ (X.XXs)`.
- No architecture changes required.
- Exit code semantics are identical to build (0 = success, non-zero = errors) — no empty-output
  ambiguity.

#### Cons / challenges

- Publish typically runs after a restore; the "All projects are up-to-date for restore" line
  needs stripping (same as build filter already does).
- Publish output includes both the `.dll` arrow AND the `publish/` directory arrow — two lines
  per project instead of one. Minor but must be accounted for.
- Self-contained publish (`--self-contained`) or AOT publish produces significantly more noise
  (native compilation steps, IL trim warnings). The filter would need patterns for these.
- `dotnet publish` is often parameterised (`-r win-x64 --self-contained`) in ways that change
  the output structure. More complex test matrix required.
- Risk of filter regression: if publish output is treated as "close enough to build", edge cases
  (trim warnings, ReadyToRun output) could slip through and cause either over-filtering (losing
  important messages) or under-filtering (noise remains).

#### Verdict: HIGH VALUE — largely reuses existing build filter logic, no architecture changes

---

### `dotnet pack`

**Description**: Compiles the project and creates a NuGet package (`.nupkg` + `.snupkg`).

#### Output behaviour (confirmed by measurement)

| Scenario | stdout | bytes |
|---|---|---|
| Success, single project | Build arrows + "Successfully created package 'path.nupkg'" | ~1 070 chars |
| Success, project + symbols | Same + symbols package line | ~1 200 chars |
| Failure (compile error) | Same as build errors | 1–5 KB |

#### Token savings potential

- Modest on success (output is already short) — ~40–70 % savings.
- Large on failure with many errors — ~80–90 % savings.

#### Pros of adding support

- Consistent with the "every CI pipeline stage is covered" philosophy.
- **Minimal new filter code required**: output is build output + two "Successfully created package"
  lines at the end. Again, `DotnetBuildFilter` logic can be composed.
- The package path (`D:\...\.nupkg`) should be shortened with `TextHelpers.ShortenPath()` — a
  small but clean improvement.
- No architecture changes required.
- Complete and stable output format; packing has not changed its output schema in years.

#### Cons / challenges

- Pack is infrequent in the inner dev loop; token savings are correspondingly rare. Most devs
  pack on release only.
- When packing without prior build (`--no-build` not specified), pack triggers a full build
  internally. The output volume grows proportionally. The filter must handle the merged output.
- NU warning codes (e.g. NU5128 "No supported target frameworks") appear during pack but not
  during build. The new filter needs NuGet-specific warning patterns.
- Symbol packages (`.snupkg`) need separate handling. Filtering by pattern must not accidentally
  suppress the symbol package path.
- Low usage frequency compared to build/test means the filter will see fewer real-world test
  cases before production use.

#### Verdict: MEDIUM VALUE — low effort, but limited frequency means lower ROI

---

### `dotnet msbuild`

**Description**: Passes arguments directly to MSBuild; used for custom targets, property
inspection, and advanced build scenarios.

#### Output behaviour

| Scenario | Output | Notes |
|---|---|---|
| Standard build target | Identical to `dotnet build` | Same MSBuild engine |
| Custom target | Arbitrary lines from `<Message>` tasks | Unpredictable structure |
| `/p:` property dump | Key=value pairs | Structured but custom |

#### Token savings potential

- When used as a build alias: same as `dotnet build` — high savings.
- When used for custom targets: unknown — output is user-defined.

#### Pros of adding support

- Some users and CI systems call `dotnet msbuild` instead of `dotnet build` (e.g. when passing
  MSBuild-specific switches like `/m:4 /bl`).
- The build filter's diagnostic patterns (`(file.cs(L,C): error CS...)`) work identically.
- Could reuse `DotnetBuildFilter` by aliasing the filter key: `FilterKeys.MsBuild = "msbuild"`.

#### Cons / challenges

- **Unpredictable output from custom targets is a serious risk**. If a user calls
  `dtk dotnet msbuild /t:GenerateDocumentation`, the filter may silently swallow output that the
  user needs.
- The MSBuild binary log flag (`/bl`) produces a `.binlog` file and minimal console output —
  confusing in combination with dtk's `--show-log`.
- Complex argument structure: MSBuild switches use `/` prefix (e.g. `/p:`, `/t:`, `/m:`),
  whereas the ArgumentPreprocessor separator logic was designed for `--` style arguments.
- Line count and noise level vary drastically based on verbosity (`/v:minimal` vs `/v:detailed`).
- Rarely used in the normal dev loop; most users reach for `dotnet build` instead.

#### Verdict: LOW VALUE — high risk of silent output loss on custom targets; passthrough is safer

---

### `dotnet tool restore`

**Description**: Restores local .NET tools defined in `dotnet-tools.json` (the manifest).

#### Output behaviour

| Scenario | Output | bytes |
|---|---|---|
| Tools already restored | `Tool 'jb' (version '...' ) was restored.` ×N | ~100–400 chars |
| First restore | Same + download progress | 1–5 KB |
| No manifest | Error message | ~100 chars |

#### Token savings potential

- Modest: output is already compact on success. Savings of ~40–60 %.
- More significant when tools must be downloaded (NuGet restore lines mixed in).

#### Pros of adding support

- **Architecture challenge**: the subcommand is `tool restore`, a two-word command. The current
  `KnownSubcommands` set and CLI command registration model handles single words only. The
  ArgumentPreprocessor checks `args[1]` against the set — adding `"tool"` would intercept ALL
  tool subcommands (`install`, `list`, `run`, `uninstall`, `update`), not just `restore`.
- `dotnet tool restore` is commonly run at the start of CI pipelines to install `jb`, `nbgv`, etc.
  Token savings here would be seen in CI logs.

#### Cons / challenges

- **Two-word subcommand requires architecture change**: the ArgumentPreprocessor,
  `KnownSubcommands` set, and Spectre CLI tree all assume a single-word subcommand. Supporting
  `tool restore` means either:
  - Expanding passthrough detection to check `args[1] + args[2]` combinations, or
  - Intercepting `tool` as a word and then dispatching sub-subcommands via a nested Spectre command.
  Either approach adds meaningful complexity.
- If `tool` is partially intercepted, `dtk dotnet tool install` would silently fall through to
  passthrough, which is confusing ("why does tool restore get filtered but tool install doesn't?").
- `dotnet tool restore` is already fast and its output is minimal; token savings are marginal
  compared to the implementation cost.
- The filter must handle tool version conflicts and missing manifest errors gracefully.

#### Verdict: LOW VALUE vs HIGH COST — architecture change not justified by marginal savings

---

### `dotnet run`

**Description**: Builds (if needed) and executes the project's entry point. The process output
IS the application's output.

#### Output behaviour

| Scenario | Output |
|---|---|
| Normal execution | Build noise (if rebuild needed) + **app's own stdout/stderr** |
| Build failure | Error diagnostics (same as build) |

#### Token savings potential

- **Zero or negative** for the app's output — filtering would destroy user data.
- Potentially positive for the build preamble, but separating "build noise" from "app output"
  is not reliably possible.

#### Pros of adding support

- The build preamble (restore + compile lines) before the app starts could theoretically be
  stripped.
- CI pipelines running integration tests via `dotnet run` would see consistent output.

#### Cons / challenges

- **Fundamental architecture incompatibility**. `RunCapturedAsync` buffers ALL output until the
  process exits. For `dotnet run`, that means buffering the entire app execution — potentially
  blocking indefinitely on web servers or interactive apps.
- **App output must not be filtered**. There is no reliable delimiter between MSBuild output
  (before the app starts) and app output (after it starts). The app could write anything to
  stdout, including lines that match build diagnostic patterns.
- Interactive apps (those reading from stdin) would deadlock because stdin is not redirected in
  captured mode.
- Exit codes from `dotnet run` reflect the app's exit code, not a build success/failure. Saving
  exit code 1 as "failure" in tracking is misleading when it means "app exited with error".
- Supporting `dotnet run` properly would require a completely different execution model:
  streaming capture with pass-through for app lines after the first blank line, for example.
  This is a significant new feature, not a filter.

#### Verdict: NOT SUITABLE — fundamental incompatibility with the capture-then-filter model

---

### `dotnet watch`

**Description**: Starts a file watcher that rebuilds and re-runs the project whenever source
files change. Long-lived, interactive process.

#### Output behaviour

- Continuous, streaming, multi-cycle output.
- Each file save triggers a new build+run cycle.
- Output is interleaved with ANSI colours and cursor-control sequences.
- Never exits until the user presses Ctrl+C.

#### Pros of adding support

- None that are viable within the current architecture.

#### Cons / challenges

- **All the same problems as `dotnet run`, compounded**. The process never exits, so
  `RunCapturedAsync` would block forever — the terminal would appear frozen.
- Watch output is inherently interactive; buffering it defeats the purpose.
- Hot-reload messages, rebuild cycles, and app output are all interleaved — no static filter
  can reliably separate them.
- Token savings are irrelevant here: this is a developer-facing interactive tool, not something
  an LLM agent would typically call.

#### Verdict: NOT SUITABLE — incompatible with any reasonable filtering model

---

### `dotnet new`

**Description**: Scaffolds new projects, files, or solution components from templates.

#### Output behaviour

| Scenario | Output | bytes |
|---|---|---|
| Success | `The template "Console App" was created successfully.` + post-actions | ~200 chars |
| Failure (name conflict) | Error message | ~100 chars |
| Template list (`--list`) | Table of installed templates | 3–10 KB |

#### Token savings potential

- On creation: output is already very compact — near-zero savings (< 20 %).
- On `--list`: moderate savings possible by stripping the table formatting.

#### Pros of adding support

- `dotnet new --list` output (template table) can be quite verbose and could be condensed.
- LLM agents scaffolding projects would receive confirmation.

#### Cons / challenges

- The **creation output is already minimal** — dtk would add overhead (tracking, tee) for almost
  no token benefit.
- `dotnet new --list` is a query command, not a build command. The semantics differ from build/test.
- `dotnet new` subcommands (`install`, `uninstall`, `search`, `list`) have diverse output formats
  requiring separate filter logic per sub-action.
- Post-actions (git init, npm install prompts) can make the process interactive — another
  buffering problem.
- Template output is already designed to be human-readable and compact; filtering it offers
  little improvement.

#### Verdict: LOW VALUE — output is already compact; savings negligible

---

### `dotnet nuget`

**Description**: NuGet-specific commands: push, delete, list, add source, enable/disable source.

#### Output behaviour

| Scenario | Output | bytes |
|---|---|---|
| `nuget push` success | Package URL + confirmation | ~100 chars |
| `nuget push` failure | HTTP error, Auth error | ~200 chars |
| `nuget list source` | Formatted source table | ~500 chars |

#### Token savings potential

- Very low. Output is already compact for all common sub-actions.

#### Pros of adding support

- `nuget push` in CI pipelines could have its output normalised.

#### Cons / challenges

- Same two-word subcommand routing problem as `tool restore`.
- Output is already minimal; savings would be negligible.
- `nuget push` involves network I/O and authentication — failures produce varied error messages
  that a filter might accidentally suppress.
- Multiple sub-subcommands with inconsistent output formats.

#### Verdict: LOW VALUE — output too small to warrant filtering

---

### `dotnet workload`

**Description**: Manages optional .NET SDK workloads (install, update, restore for MAUI,
WASM, etc.).

#### Output behaviour

| Scenario | Output | bytes |
|---|---|---|
| `workload restore` (nothing to do) | `No workloads to install.` | ~30 chars |
| `workload install` (downloading) | Progress bars, download stats | 5–50 KB |
| `workload update` | Similar to install | 5–50 KB |

#### Token savings potential

- High on install/update (progress noise), but only during workload changes (rare).
- Near-zero otherwise.

#### Pros of adding support

- Workload installs produce large progress-bar outputs that would compress well.

#### Cons / challenges

- Workload management is an administrative action, not part of the daily dev loop. Most CI
  images have workloads pre-installed.
- Same two-word subcommand problem.
- Workload install is semi-interactive (may prompt for admin elevation on some OS/CI combos).
- The output format changes between SDK versions — maintaining patterns would be fragile.
- Very low frequency of use means filter quality would be hard to maintain.

#### Verdict: LOW VALUE — too infrequent and architecturally complex

---

## Required Changes Per Command

| Command | Architecture change? | New filter? | Effort estimate |
|---|---|---|---|
| `format` | ⚠️ Yes — exit-code context in filter | Yes | Medium |
| `publish` | No | Yes (builds on build filter) | Low–Medium |
| `pack` | No | Yes (builds on build filter) | Low |
| `msbuild` | No | Reuse build filter | Very Low (risky) |
| `tool restore` | ⚠️ Yes — two-word subcommand routing | Yes | High |
| `run` | ⚠️ Yes — streaming execution model | N/A | Very High (new feature) |
| `watch` | ⚠️ Yes — streaming execution model | N/A | Very High (new feature) |
| `new` | No | Yes | Low (but low value) |
| `nuget` | ⚠️ Yes — two-word subcommand routing | Yes | Medium (low value) |
| `workload` | ⚠️ Yes — two-word subcommand routing | Yes | Medium (low value) |

---

## Architecture Changes Required

### Change 1 — Pass exit code to the filter (needed for `format`)

**Problem**: `IOutputFilter.Apply(string rawOutput)` cannot distinguish empty-on-success
from empty-on-failure. `dotnet format` produces zero bytes of output when nothing needs
formatting (exit 0), and zero bytes when it encounters a fatal error before loading the
workspace (also exit 0 in some edge cases).

**Option A — Extend the interface (breaking change)**:

```csharp
// Domain/Filters/IOutputFilter.cs
public interface IOutputFilter
{
    string Apply(string rawOutput, int exitCode);  // was: string Apply(string rawOutput)
}
```

All 4 existing filters get an ignored `int exitCode` parameter. Clean and explicit.
Breaking change for any external consumer of the interface.

**Option B — FilterContext value object (non-breaking with adapter)**:

```csharp
public sealed record FilterContext(string RawOutput, int ExitCode, TimeSpan Elapsed);

public interface IOutputFilter
{
    string Apply(string rawOutput);                     // keep for backward compat
    string Apply(FilterContext context) => Apply(context.RawOutput);  // default impl
}
```

New filters override the context overload; old filters remain unchanged. `FilteredRunUseCase`
calls `Apply(context)`. Default `Apply(FilterContext)` delegates to `Apply(string)`.

**Option C — Heuristic in filter only (no interface change)**:
The `DotnetFormatFilter` treats empty raw output as "nothing to format" and returns
`✓ dotnet format (nothing to format)`. This is valid because format always writes changed
file paths to stdout when it modifies files; an empty stdout reliably signals no-op.
**Risk**: fragile if Microsoft changes format behaviour in a future SDK version.

**Recommendation**: Option B for maximum forward-compatibility, or Option C for minimum
change if this remains format-only.

---

### Change 2 — Two-word subcommand routing (needed for `tool restore`, `nuget`, `workload`)

**Problem**: `ArgumentPreprocessor.KnownSubcommands` and the Spectre CLI tree assume
single-word subcommands. `dotnet tool restore` is two words.

**Option A — Intercept the outer word (`tool`) and dispatch sub-subcommands via nested Spectre branches**:

```sh
dtk dotnet tool restore
dtk dotnet tool install   ← passthrough (no filter)
dtk dotnet tool list      ← passthrough (no filter)
```

Spectre supports nested command branches. `DotnetToolCommand` becomes a branch with
`DotnetToolRestoreCommand` as a leaf. Clean but requires all `tool` sub-subcommands to be
declared, even if most are passthroughs.

**Option B — Compound key in `KnownSubcommands`**:
Check `args[1] + " " + args[2]` for two-word commands. More surgical, less refactoring.
Ambiguous edge cases when `args` is short.

**Recommendation**: Not worth doing for `tool restore`, `nuget`, or `workload` given the
low value/high cost ratio identified above.

---

### Change 3 — Streaming execution model (needed for `run`, `watch`)

**Problem**: `ICommandRunner.RunCapturedAsync()` reads stdout and stderr to end-of-process
before returning. This is fundamentally incompatible with long-lived or interactive processes.

A streaming model would require a new interface:

```csharp
public interface IStreamingCommandRunner
{
    IAsyncEnumerable<OutputLine> StreamAsync(string command, IReadOnlyList<string> args, ...);
}
```

And a new filter type that processes lines one at a time, accumulating state.

This is a **substantial architectural extension**, not an incremental change. `FilteredRunUseCase`
would need a parallel `StreamingRunUseCase` or a significant refactor.

**Recommendation**: Not worth doing until there is a concrete use-case driving it. `dotnet run`
and `dotnet watch` are not typical LLM-agent commands.

---

## Prioritised Recommendation

### Priority 1: `dotnet format` — implement with architecture Change 1 (Option C for now)

**Rationale**: This is the most requested missing command in AI-agent workflows. Format is run
after every code change. Without dtk support, agents get zero confirmation that format ran;
with dtk support they get `✓ dotnet format (nothing to format)` or a list of files that were
changed. Use the heuristic "empty output + exit 0 = success" (Option C) as a v1 and upgrade
to Option B once `publish` or another command also needs exit-code context.

**Changes required**:

1. `FilterKeys.cs` — add `public const string Format = "format";`
2. `DotnetFormatFilter.cs` — new filter:
   - Empty raw output → `✓ dotnet format (nothing to format)`
   - Non-empty raw output on exit 0 → list formatted files with workspace-relative paths
   - Non-empty raw output on exit 1 (needs the heuristic or Option B) → `✗ dotnet format:
     N files would be changed [--verify-no-changes]`
3. `DependencyInjection.cs` — register keyed service
4. `DotnetFormatCommand.cs` — new CLI command (15 lines)
5. `Program.cs` — register in Spectre tree
6. `ArgumentPreprocessor.cs` — add `"format"` to `KnownSubcommands`
7. Filter unit tests with fixtures for: no-op, formatted files, verify-no-changes violation

---

### Priority 2: `dotnet publish` — implement without architecture changes

**Rationale**: Very commonly used in CI. Almost zero new code required — the build filter's
diagnostic patterns apply verbatim. The only new logic is extracting the publish output path
and formatting `✓ dotnet publish → relative/path/ (X.XXs)`.

**Changes required**: all the standard 7-file boilerplate, plus a `DotnetPublishFilter` that
extends/composes `DotnetBuildFilter` logic.

---

### Priority 3: `dotnet pack` — implement after publish

**Rationale**: Same output structure as publish. Once publish filter is done, pack filter is
a minimal delta (replace publish arrow detection with package file detection). Lower frequency
than publish but non-trivial token savings on solutions with many errors.

---

### Deferred: everything else

- `msbuild` — risky (custom target output), low priority
- `tool restore` — architecture cost not justified
- `run` / `watch` — incompatible with core architecture; new feature
- `new` / `nuget` / `workload` — low value

---

## Summary Table

| Command | Token savings potential | Effort | Architecture change | Recommendation |
|---|---|---|---|---|
| **format** | High (confirmation value) | Medium | Minor (heuristic only) | **Implement — Priority 1** |
| **publish** | High | Low–Medium | None | **Implement — Priority 2** |
| **pack** | Medium | Low | None | **Implement — Priority 3** |
| msbuild | Medium (risky) | Very Low | None | Defer — too risky |
| tool restore | Low | High | Yes (routing) | Defer |
| new | Negligible | Low | None | Skip |
| nuget | Negligible | Medium | Yes (routing) | Skip |
| workload | Low (occasional) | Medium | Yes (routing) | Skip |
| run | **Incompatible** | Very High | Yes (streaming) | Skip |
| watch | **Incompatible** | Very High | Yes (streaming) | Skip |

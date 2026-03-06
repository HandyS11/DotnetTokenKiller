# 05 — Filtering Strategies

## Filtering Taxonomy

DTK uses several filtering strategies depending on the subcommand. Each strategy targets a specific type of output noise:

| Strategy | Used By | Technique | Reduction |
|---|---|---|---|
| **Stats Extraction** | `dotnet build`, `dotnet restore` | Count/aggregate, drop details | 80–95% |
| **Error Only** | `dotnet run`, `dotnet build` (errors) | Keep stderr, drop stdout noise | 60–80% |
| **Failure Focus** | `dotnet test` | Show only failing tests | 90–95% |
| **Grouping by Pattern** | `dotnet build` (warnings) | Group MSBuild warnings by code | 80–90% |
| **Progress Filtering** | `dotnet restore`, `dotnet nuget` | Strip progress bars | 85–95% |

---

## Filter Interface (Domain Layer)

The `IOutputFilter` interface lives in `DotnetTokenKiller.Domain.Filters`. It defines a pure function contract:

- **Input**: raw command output (combined stdout + stderr)
- **Output**: filtered, token-optimized string
- **Constraints**: must be stateless and side-effect-free — no I/O, no config reads, no console writes

This purity makes filters trivially testable: pass in a fixture string, assert on the result.

---

## FilteredRunUseCase (Application Layer)

The central orchestrator in the Application layer. Every `dotnet` subcommand flows through it:

1. **Start timer** — begin tracking execution time
2. **Execute command** — via `ICommandRunner.RunCapturedAsync`, capture stdout + stderr
3. **Combine output** — merge stdout and stderr into a single raw string
4. **Apply filter** — call `IOutputFilter.Apply(raw)` with the appropriate filter
5. **Tee raw output** — via `ITeeService`, optionally save full output on failure
6. **Print filtered output** — write to console
7. **Track token savings** — via `ITracker`, record input/output token counts
8. **Propagate exit code** — return the underlying process exit code unchanged

### Fail-Safe Behavior

If the filter throws an exception, the use case catches it and falls back to raw output. This is critical:

- The user never sees broken/empty output
- CI/CD pipelines don't silently lose information
- Filter bugs are discoverable via `-v` (verbose mode shows the error) but non-breaking

### Verbose Mode

When verbosity > 0:
- **Level 1 (debug)**: show the command being executed
- **Level 2 (trace)**: show raw output before filtering, filter errors, timing details

---

## Regex Best Practices

### Source-Generated Regex

All regex patterns use `.NET [GeneratedRegex]` attributes for compile-time source generation. This provides:

- **Zero runtime compilation cost** — patterns are compiled into executable code at build time
- **AOT compatibility** — no runtime reflection or JIT compilation needed
- **Zero-allocation matching** — source-generated matchers avoid string allocations where possible

### Pattern Organization

Each filter class declares its regex patterns as `private static partial` methods with the `[GeneratedRegex]` attribute. Common patterns (like MSBuild diagnostic lines) can be shared in a helper class.

Key patterns used across filters:

| Pattern Purpose | Example Match |
|---|---|
| MSBuild diagnostic | `File.cs(15,13): error CS1002: ; expected [Project.csproj]` |
| Build result | `Build succeeded.` / `Build FAILED.` |
| Time elapsed | `Time Elapsed 00:00:02.54` |
| Project output | `  MyProject -> D:\path\to\output.dll` |
| Restore line | `  Restored D:\path\Project.csproj (in 234 ms).` |
| Test summary | `Passed!  - Failed: 0, Passed: 42, ...` |

---

## Line-by-Line Parsing Pattern

Most filters iterate through the raw output line by line, maintaining minimal state to:

- **Skip noise lines** — MSBuild version, "Determining projects to restore...", blank lines
- **Detect sections** — error blocks, test failure blocks, summary lines
- **Aggregate data** — count projects, group errors by file/code, sum test results
- **Extract key info** — output paths, elapsed time, package names

The parser switches between states (e.g., "in error block", "in test failure") and accumulates structured data before formatting the final output.

---

## Output Formatting Conventions

All filters follow consistent output conventions:

### Success (no issues)
```
✓ dotnet <subcommand> (<context>, <time>)
```
Example: `✓ dotnet build (4 projects, 8.12s)`

### Success with warnings
```
dotnet <subcommand>: 0 errors, N warnings (<context>, <time>)
═══════════════════════════════════════
  <grouped details>
```

### Failure (errors)
```
dotnet <subcommand>: N errors, M warnings (<context>)
═══════════════════════════════════════
<structured error details>
```

### Key formatting rules

- Use `✓` for success, no prefix for failures
- Use `═══` separator for structured detail sections
- Shorten absolute paths to project-relative paths
- Truncate long messages (120 chars max)
- Group errors/warnings by file, sorted by error count descending
- Limit displayed items (e.g., max 15 test failures, max 5 top error codes)

---

## Fail-Safe Pattern (Critical)

Every filter invocation must be wrapped in error handling. If a filter crashes:

1. Log the error to stderr (if verbose mode)
2. Return the raw output unchanged
3. Continue with tracking and exit code propagation

This ensures DTK is **transparent** — it should never make things worse than running `dotnet` directly.

---

## Path Shortening

All filters shorten file paths for readability:

- Convert absolute paths to project-relative: `D:\projects\MyProject\Controllers\HomeController.cs` → `Controllers/HomeController.cs`
- Use forward slashes consistently (even on Windows) for LLM-friendly output
- Fall back to filename only if relative path computation fails

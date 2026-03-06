# 06 — `dotnet build` Filter Specification

## Location

`DotnetTokenKiller.Application.Filters.DotnetBuildFilter` — implements `IOutputFilter` from the Domain layer.

## MSBuild Output Analysis

The `dotnet build` command produces verbose output that is highly compressible. The filter must handle three scenarios: clean success, success with warnings, and failure with errors.

### Raw Output (Success) — typical ~15–20 lines

```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Debug\net10.0\MyProject.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.54
```

### Raw Output (Errors) — typical ~30–40 lines

```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
D:\projects\MyProject\Controllers\HomeController.cs(15,13): error CS1002: ; expected [D:\projects\MyProject\MyProject.csproj]
D:\projects\MyProject\Controllers\HomeController.cs(20,9): error CS0103: The name 'foo' does not exist in the current context [D:\projects\MyProject\MyProject.csproj]
D:\projects\MyProject\Models\User.cs(8,5): warning CS0169: The field 'User._name' is never used [D:\projects\MyProject\MyProject.csproj]

Build FAILED.

D:\projects\MyProject\Controllers\HomeController.cs(15,13): error CS1002: ; expected [D:\projects\MyProject\MyProject.csproj]
D:\projects\MyProject\Controllers\HomeController.cs(20,9): error CS0103: The name 'foo' does not exist in the current context [D:\projects\MyProject\MyProject.csproj]
    2 Warning(s)
    2 Error(s)

Time Elapsed 00:00:01.87
```

**Important**: MSBuild prints errors twice — once inline during build, and again in the summary section after "Build FAILED." The filter must deduplicate.

### Raw Output (Large Solution with NuGet Restore) — typical ~30+ lines

```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
  Restored D:\projects\Solution\Api\Api.csproj (in 234 ms).
  Restored D:\projects\Solution\Core\Core.csproj (in 189 ms).
  Restored D:\projects\Solution\Data\Data.csproj (in 310 ms).
  Restored D:\projects\Solution\Tests\Tests.csproj (in 245 ms).
  Core -> D:\projects\Solution\Core\bin\Debug\net10.0\Core.dll
  Data -> D:\projects\Solution\Data\bin\Debug\net10.0\Data.dll
  Api -> D:\projects\Solution\Api\bin\Debug\net10.0\Api.dll
  Tests -> D:\projects\Solution\Tests\bin\Debug\net10.0\Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:08.12
```

---

## Filtering Strategy

### Success (no errors, no warnings)

**Input**: ~20 lines of noise
**Output**: `✓ dotnet build (4 projects, 8.12s)`
**Savings**: ~90%

### Success with Warnings

**Input**: ~30 lines
**Output**:
```
dotnet build: 0 errors, 3 warnings (4 projects, 8.12s)
═══════════════════════════════════════
  CS0169 (2x)
    Models/User.cs(8,5)
    Models/Order.cs(12,9)
  CS0414 (1x)
    Services/Cache.cs(23,5)
```
**Savings**: ~80%

### Failure (errors)

**Input**: ~40 lines (with duplicated error section)
**Output**:
```
dotnet build: 2 errors, 1 warning (4 projects)
═══════════════════════════════════════
Top codes: CS1002 (1x), CS0103 (1x)

Controllers/HomeController.cs (2 errors)
  L15: CS1002 ; expected
  L20: CS0103 The name 'foo' does not exist in the current context

Models/User.cs (1 warning)
  L8: CS0169 The field 'User._name' is never used
```
**Savings**: ~75%

---

## Parsing Requirements

### Regex Patterns Needed

All patterns use `[GeneratedRegex]` for compile-time generation:

| Pattern | Purpose | Example Match |
|---|---|---|
| MSBuild diagnostic | Parse error/warning lines | `File.cs(15,13): error CS1002: ; expected [Proj.csproj]` |
| Build result | Detect success/failure | `Build succeeded.` / `Build FAILED.` |
| Time elapsed | Extract build duration | `Time Elapsed 00:00:02.54` |
| Project output | Count built projects | `  MyProject -> D:\path\output.dll` |
| Restored line | Identify restore noise | `  Restored D:\path\Proj.csproj (in 234 ms).` |

### Data to Extract

For each diagnostic line, parse:
- **File path** (shortened to project-relative)
- **Line number** and column
- **Severity** (`error` or `warning`)
- **Diagnostic code** (CS, MSB, NU, CA, IDE, SA, NETSDK prefixes)
- **Message text**

### Aggregation

- Count projects built (from `→` output lines)
- Count unique errors and warnings (deduplicated)
- Group diagnostics by file, then sort by error count descending
- Extract top 5 diagnostic codes
- Parse elapsed time

---

## Lines to Skip (Noise)

These MSBuild output lines provide zero value to an LLM:

| Line Pattern | Why Skip |
|---|---|
| `MSBuild version 17.9.0+...` | Version info, never actionable |
| `Determining projects to restore...` | Progress noise |
| `All projects are up-to-date for restore.` | No action needed |
| `Restored ... (in N ms).` | Restore details (handled by restore filter) |
| `Build succeeded.` | Redundant (exit code tells us) |
| `Build FAILED.` | Redundant (errors tell us) |
| Duplicate error section after "Build FAILED" | MSBuild prints errors twice |
| `N Warning(s)` / `N Error(s)` | We count these ourselves |
| Blank lines | Padding |

---

## MSBuild Diagnostic Codes

All follow the same `file(line,col): severity CODE: message [project]` format:

| Prefix | Source | Examples |
|---|---|---|
| `CS` | C# compiler | CS1002, CS0103, CS0169 |
| `MSB` | MSBuild | MSB3277 (assembly conflict), MSB4181 |
| `NU` | NuGet | NU1605 (package downgrade), NU1701 |
| `CA` | Code Analysis | CA1062, CA2000 |
| `IDE` | IDE analyzers | IDE0051, IDE0060 |
| `SA` | StyleCop | SA1101, SA1200 |
| `NETSDK` | .NET SDK | NETSDK1045, NETSDK1004 |

---

## Edge Cases

| Scenario | Expected Behavior |
|---|---|
| Empty input | Return empty/minimal output, do not crash |
| No MSBuild output (e.g., SDK error) | Pass through the raw error message |
| ANSI escape codes in output | Strip before parsing |
| Multi-targeting (net10.0 + net8.0) | Aggregate across all TFMs |
| Very long error messages | Truncate at 120 characters |
| Non-English locale output | Best-effort parsing, fall back to raw on failure |

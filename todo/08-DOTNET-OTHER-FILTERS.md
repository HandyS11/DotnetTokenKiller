# 08 — Other `dotnet` Filter Specifications

This document covers all remaining `dotnet` subcommand filters beyond `build` and `test`. All filters live in `DotnetTokenKiller.Application.Filters` and implement `IOutputFilter` from the Domain layer.

---

## `dotnet restore`

### Raw Output
```
  Determining projects to restore...
  Writing assets file to disk. Path: D:\projects\MyProject\obj\project.assets.json
  Restored D:\projects\MyProject\MyProject.csproj (in 1.23 s).
  Restored D:\projects\Api\Api.csproj (in 890 ms).
  3 of 5 projects are up-to-date for restore.
```

### Filtered Output (Success)
```
✓ dotnet restore (5 projects, 2.12s)
```

### Filtered Output (Failure)
```
dotnet restore: 1 error
═══════════════════════════════════════
NU1101: Unable to find package 'Newtonsoft.Json.Fake'. No packages exist with this id in source(s): nuget.org
  → Api/Api.csproj
```

### Parsing Strategy

- Count restored projects (from `Restored ...` lines) and up-to-date projects (from `N of M projects are up-to-date`)
- Parse total restore time from individual restore durations
- Detect NuGet errors by `NU` prefix codes
- **Noise to strip**: `Determining projects to restore`, `Writing assets file to disk`, `NuGet Config files used`, individual package download lines, progress indicators

---

## `dotnet publish`

### Raw Output
```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Release\net10.0\MyProject.dll
  MyProject -> D:\projects\MyProject\bin\Release\net10.0\publish\
```

### Filtered Output
```
✓ dotnet publish → bin/Release/net10.0/publish/ (1 project, 3.45s)
```

### Parsing Strategy

- The **publish output path** is the most valuable piece of information — extract from the last `→` line containing "publish"
- Strip all restore and compile noise
- Shorten path to project-relative
- On build errors: reuse the same diagnostic parsing logic as the build filter

---

## `dotnet pack`

### Raw Output
```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Release\net10.0\MyProject.dll
  Successfully created package 'D:\projects\MyProject\bin\Release\MyProject.1.0.0.nupkg'.
```

### Filtered Output
```
✓ dotnet pack → MyProject.1.0.0.nupkg (1 project, 2.34s)
```

### Parsing Strategy

- Extract `.nupkg` filename from `Successfully created package` line
- Strip compilation noise
- On error: show build errors

---

## `dotnet clean`

### Raw Output
```
MSBuild version 17.9.0+... for .NET
  Determining projects to restore...
Build started 1/15/2026 10:30:00 AM.
```

### Filtered Output
```
✓ dotnet clean
```

### Parsing Strategy

Simplest possible filter — discard everything, return success marker or extract error lines if any contain "error" (case-insensitive). Limit displayed errors to 5.

---

## `dotnet run`

### Strategy

`dotnet run` executes the application. The output is the **application itself**, so the filter only strips the **build noise prefix**, not the application output.

### Raw Output
```
  Determining projects to restore...
  All projects are up-to-date for restore.
  MyProject -> D:\projects\MyProject\bin\Debug\net10.0\MyProject.dll
Hello, World!
Application started on port 5000
```

### Filtered Output
```
Hello, World!
Application started on port 5000
```

### Parsing Strategy

- Skip known build preamble lines: `Determining projects to restore`, `All projects are up-to-date`, `Restored ...`, `MSBuild version`, `Build started`, and project output (`→ .dll`) lines
- Keep everything else unchanged — it's the application's actual output
- If nothing remains after stripping preamble, return `✓ dotnet run completed`

---

## `dotnet ef` (Entity Framework)

### Supported Subcommands

| Subcommand | Raw Output | Filtered Output |
|---|---|---|
| `ef migrations list` | Verbose migration list with timestamps | Compact: `3 migrations (latest: 20260115_AddUsers)` |
| `ef migrations add` | Build output + scaffold message | `✓ migration added: 20260115_AddUsers` |
| `ef database update` | Build + SQL execution log | `✓ database updated (3 migrations applied)` |
| `ef database drop` | Confirmation message | Pass through (interactive) |

### Parsing Strategy

- Detect the EF subcommand from output patterns (migration names, SQL logs, etc.)
- Strip "Build started", "Build succeeded" preamble
- Strip Entity Framework CLI banner/logo lines
- Compact depending on the detected subcommand
- **Key noise**: EF CLI prints a banner, build output, and verbose SQL when `--verbose` is used — strip everything except the actionable result

---

## `dotnet format`

### Raw Output (Fix Mode)
```
  Formatting code files in workspace 'D:\projects\MyProject\MyProject.sln'.
  D:\projects\MyProject\Controllers\HomeController.cs
  D:\projects\MyProject\Models\User.cs
  D:\projects\MyProject\Services\OrderService.cs
  Format complete in 1.23s, 3 files formatted.
```

### Filtered Output (Fix Mode)
```
✓ dotnet format (3 files, 1.23s)
```

### Filtered Output (Check Mode — `--verify-no-changes`)
```
dotnet format: 3 files need formatting
  Controllers/HomeController.cs
  Models/User.cs
  Services/OrderService.cs
```

### Parsing Strategy

- Extract file paths from output lines (lines starting with absolute paths)
- Shorten to project-relative paths
- Detect fix vs check mode from `Format complete` presence
- Limit displayed files to 20 (with "+N more" overflow)
- If no files changed, return `✓ dotnet format (no changes)`

---

## `dotnet nuget`

### Key Subcommands

| Subcommand | Filtered Output |
|---|---|
| `nuget push` | `✓ nuget push succeeded` |
| `nuget delete` | `✓ nuget delete succeeded` |
| `nuget list source` | Compact source list |
| `nuget locals all --clear` | `✓ nuget locals cleared` |

### Parsing Strategy

- Detect the subcommand result from known output phrases: `Your package was pushed`, `was deleted successfully`, `local resources have been cleared`
- Strip progress bars and download indicators
- For source listing, compact into a minimal format
- Fall through to raw output (with noise stripped) for unrecognized nuget subcommands

---

## Passthrough for Unrecognized Subcommands

Any `dotnet` subcommand not explicitly handled gets passthrough treatment:

- Run in passthrough mode (inherit stdin/stdout/stderr)
- No filtering applied — output goes directly to the terminal
- Token usage is still tracked for analytics
- Exit code is preserved

This covers: `dotnet new`, `dotnet add`, `dotnet remove`, `dotnet tool`, `dotnet watch`, `dotnet sln`, etc.

---

## Summary Table

| Filter | Success Output | Error Handling | Expected Savings |
|---|---|---|---|
| `restore` | `✓ dotnet restore (N projects, Xs)` | Show NU error codes + affected project | 90–95% |
| `publish` | `✓ dotnet publish → path/ (Xs)` | Reuse build diagnostic parsing | 80–85% |
| `pack` | `✓ dotnet pack → .nupkg (Xs)` | Reuse build diagnostic parsing | 85–90% |
| `clean` | `✓ dotnet clean` | Show error lines (max 5) | 95%+ |
| `run` | Strip build preamble, keep app output | Pass through errors | 30–60% |
| `ef` | Compact migration/DB status | Show EF errors | 70–80% |
| `format` | `✓ dotnet format (N files)` | List files needing changes | 70–80% |
| `nuget` | `✓ nuget <action> succeeded` | Show error details | 75–85% |

### Shared Patterns

Several filters reuse the same logic:
- **Build preamble stripping** — `restore`, `publish`, `pack`, `run`, `ef` all need to skip MSBuild noise
- **Diagnostic parsing** — `publish` and `pack` can delegate to build filter logic for error handling
- **Path shortening** — all filters shorten absolute paths to project-relative

Consider extracting these into shared helper methods in the Application layer.

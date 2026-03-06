# 10 — Build, Distribution & Performance

## Overview

DTK ships primarily as a **.NET Global Tool** for simple installation. For performance-critical use, **Native AOT** publishing produces a single binary with fast startup. Both options target `net10.0`.

Key dependencies that affect build/distribution:
- **Spectre.Console.Cli** — AOT-compatible command-line framework
- **Microsoft.Data.Sqlite** — native interop, requires bundled SQLite library for AOT
- **System.Text.Json** with source generators — AOT-compatible configuration

---

## Distribution Option 1: .NET Global Tool (Primary)

The simplest path. Users install via:

```powershell
dotnet tool install -g DotnetTokenKiller
```

### Global Tool Configuration

The CLI project (`DotnetTokenKiller.Cli`) is configured as a packable global tool:

| Property | Value |
|---|---|
| `PackAsTool` | `true` |
| `ToolCommandName` | `dtk` |
| `PackageId` | `DotnetTokenKiller` |
| `TargetFramework` | `net10.0` |
| `Description` | .NET Token Killer — minimize LLM token consumption for dotnet CLI output |
| `PackageTags` | `cli;llm;tokens;dotnet;tool` |
| `PackageLicenseExpression` | `MIT` |
| `PackageReadmeFile` | `README.md` |

The `ToolCommandName` stays `dtk` for short command-line usage, while the NuGet package uses the full `DotnetTokenKiller` name.

### Pack & Publish Workflow

```powershell
# Pack locally
dotnet pack src/DotnetTokenKiller.Cli -c Release -o ./nupkg

# Test local install
dotnet tool install -g --add-source ./nupkg DotnetTokenKiller

# Verify
dtk --version
dtk dotnet build

# Publish to NuGet
dotnet nuget push ./nupkg/DotnetTokenKiller.0.1.0.nupkg --api-key <KEY> --source https://api.nuget.org/v3/index.json
```

### Trade-offs

| Aspect | Details |
|---|---|
| Easy install | `dotnet tool install -g DotnetTokenKiller` |
| Auto-update | `dotnet tool update -g DotnetTokenKiller` |
| Cross-platform | Works wherever .NET SDK is installed |
| Startup time | ~80–150ms (JIT warmup) |
| Dependency | Requires .NET 10 SDK |

---

## Distribution Option 2: Native AOT (Performance)

For minimal startup time and standalone deployment.

### AOT Configuration

The CLI project adds an AOT publish profile with these properties:

| Property | Value | Purpose |
|---|---|---|
| `PublishAot` | `true` | Enable Native AOT compilation |
| `StripSymbols` | `true` | Reduce binary size |
| `OptimizationPreference` | `Size` | Smaller output |
| `InvariantGlobalization` | `true` | Remove ICU dependency |
| `TrimMode` | `link` | Aggressive dead-code elimination |

### AOT Compatibility Requirements

All code in the solution must be AOT-safe:

| Pattern | AOT Status | Notes |
|---|---|---|
| `[GeneratedRegex]` | Safe | Compile-time source generation |
| `new Regex(...)` at runtime | Unsafe | Generates trimming warnings |
| `System.Text.Json` + `JsonSerializerContext` | Safe | Source-generated serialization |
| `JsonSerializer.Deserialize<T>(json)` | Unsafe | Reflection-based |
| `Microsoft.Data.Sqlite` | Safe | Native interop, needs bundled SQLite |
| `Spectre.Console.Cli` | Safe | No reflection for command resolution |

The project must include `SQLitePCLRaw.bundle_e_sqlite3` to bundle the native SQLite library for AOT scenarios.

### Publish Commands

```powershell
# Windows x64
dotnet publish src/DotnetTokenKiller.Cli -c Release -r win-x64 -o publish/win-x64

# Linux x64
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o publish/linux-x64

# macOS ARM64
dotnet publish src/DotnetTokenKiller.Cli -c Release -r osx-arm64 -o publish/osx-arm64
```

### Expected Binary Sizes

| Platform | Estimated Size | Notes |
|---|---|---|
| Windows x64 | ~8–12 MB | Includes .NET runtime + SQLite native |
| Linux x64 | ~8–12 MB | Similar |
| macOS ARM64 | ~8–12 MB | Similar |

---

## Distribution Option 3: Single-File (Middle Ground)

Bundles the .NET runtime into one file but uses JIT:

| Property | Value |
|---|---|
| `PublishSingleFile` | `true` |
| `SelfContained` | `true` |
| `PublishTrimmed` | `true` |
| `PublishReadyToRun` | `true` |

| Aspect | Details |
|---|---|
| Startup | ~50–80ms (R2R pre-compilation) |
| Size | ~15–25 MB |
| Dependencies | None (self-contained) |

Useful if Native AOT causes compatibility issues or as an interim step.

---

## Performance Targets

| Metric | Global Tool | Native AOT |
|---|---|---|
| Startup time | <150ms | <15ms |
| Memory | <30 MB | <10 MB |
| Binary size | N/A (tool install) | <15 MB |
| Token savings | 60–90% | 60–90% |

### Benchmarking

Use `hyperfine` for startup comparison:

```powershell
hyperfine 'dtk dotnet build --no-restore' 'dotnet build --no-restore' --warmup 3
```

Memory measurement:
- **Windows**: `(Get-Process -Name dtk).WorkingSet64 / 1MB`
- **Linux/macOS**: `/usr/bin/time -l dtk dotnet build`

---

## CI/CD: Release Pipeline

The release pipeline builds Native AOT binaries for all platforms and publishes the NuGet global tool. Triggered on version tags (`v*`).

### Build Matrix

| OS | RID | Artifact |
|---|---|---|
| `windows-latest` | `win-x64` | `dtk.exe` |
| `ubuntu-latest` | `linux-x64` | `dtk` |
| `macos-latest` | `osx-arm64` | `dtk` |

### Pipeline Steps

1. **Build job** (per platform):
   - Checkout → Setup .NET 10 → Restore → Test → Publish Native AOT → Upload artifact
2. **Release job** (after all builds succeed):
   - Download artifacts → Generate SHA-256 checksums → Create GitHub Release with binaries
3. **NuGet job** (after all builds succeed):
   - Pack global tool → Push to NuGet.org

All jobs use `dotnet-version: '10.0.x'`.

---

## CI/CD: Quality Gate Pipeline

Runs on every push and pull request:

1. **Format check** — `dotnet format --verify-no-changes`
2. **Build** — `dotnet build --no-restore -warnaserror`
3. **Test** — `dotnet test --no-build --logger trx`
4. **Upload** — test results as pipeline artifact

This ensures every PR maintains code style, zero warnings, and passing tests.

---

## Install Script

For quick binary installs from GitHub Releases (bypasses NuGet):

The install script (`install.ps1`) detects the OS, downloads the correct binary from the latest GitHub Release, and places it in `~/.dotnet/tools`. It supports `$Version` parameter for pinning a specific release.

For Linux/macOS, a `curl | sh` one-liner can also be provided.

---

## Recommended Rollout

| Phase | Strategy | Why |
|---|---|---|
| **Phase 1: MVP** | .NET Global Tool | Fastest to ship, `dotnet tool install -g DotnetTokenKiller` |
| **Phase 2: Performance** | Add Native AOT publish profile | For users who need <15ms startup |
| **Phase 3: Distribution** | GitHub Releases + NuGet | Binary + tool package for all platforms |

Start with the global tool for maximum reach in the .NET ecosystem. Add Native AOT as an optimization once filters are stable and proven.

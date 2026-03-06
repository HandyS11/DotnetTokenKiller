# 02 — Project Structure (Clean Architecture)

## Solution Layout

The solution follows **Clean Architecture** with four projects organized by responsibility. Dependencies flow inward: CLI → Application → Domain, and Infrastructure → Domain.

```sh
DotnetTokenKiller/
├── DotnetTokenKiller.sln
├── Directory.Build.props              # Shared build properties (.NET 10, C# 14)
├── Directory.Packages.props           # Central package management
├── global.json                        # SDK version pinning
├── NuGet.config                       # Package sources
│
├── src/
│   ├── DotnetTokenKiller.Domain/              # Core domain — zero external dependencies
│   │   ├── DotnetTokenKiller.Domain.csproj
│   │   ├── Filters/
│   │   │   └── IOutputFilter.cs               # Filter contract
│   │   ├── Tracking/
│   │   │   ├── ITracker.cs                    # Tracking contract
│   │   │   ├── CommandRecord.cs               # Recorded command entity
│   │   │   └── GainSummary.cs                 # Aggregated savings entity
│   │   ├── Configuration/
│   │   │   ├── IConfigProvider.cs             # Config contract
│   │   │   └── DtkConfig.cs                   # Configuration value objects
│   │   ├── Execution/
│   │   │   ├── ICommandRunner.cs              # Process execution contract
│   │   │   └── CommandResult.cs               # Execution result value object
│   │   └── Tee/
│   │       └── ITeeService.cs                 # Tee output contract
│   │
│   ├── DotnetTokenKiller.Application/         # Use cases & filter implementations
│   │   ├── DotnetTokenKiller.Application.csproj
│   │   ├── UseCases/
│   │   │   ├── FilteredRunUseCase.cs          # Generic "run + filter + track" orchestrator
│   │   │   ├── PassthroughRunUseCase.cs       # Unrecognized subcommands
│   │   │   └── GainReportUseCase.cs           # Token savings analytics
│   │   ├── Filters/
│   │   │   ├── DotnetBuildFilter.cs
│   │   │   ├── DotnetTestFilter.cs
│   │   │   ├── DotnetRestoreFilter.cs
│   │   │   ├── DotnetPublishFilter.cs
│   │   │   ├── DotnetPackFilter.cs
│   │   │   ├── DotnetCleanFilter.cs
│   │   │   ├── DotnetRunFilter.cs
│   │   │   ├── DotnetEfFilter.cs
│   │   │   ├── DotnetFormatFilter.cs
│   │   │   └── DotnetNugetFilter.cs
│   │   └── Helpers/
│   │       ├── TokenEstimator.cs              # Token count heuristic
│   │       ├── AnsiStrip.cs                   # ANSI escape code removal
│   │       └── TextHelpers.cs                 # Truncate, format tokens, etc.
│   │
│   ├── DotnetTokenKiller.Infrastructure/      # External concerns (I/O, DB, config files)
│   │   ├── DotnetTokenKiller.Infrastructure.csproj
│   │   ├── Tracking/
│   │   │   └── SqliteTracker.cs               # SQLite-based ITracker implementation
│   │   ├── Configuration/
│   │   │   └── JsonConfigProvider.cs          # JSON file-based IConfigProvider
│   │   ├── Execution/
│   │   │   └── ProcessCommandRunner.cs        # System.Diagnostics.Process ICommandRunner
│   │   ├── Tee/
│   │   │   └── FileTeeService.cs              # File-based ITeeService
│   │   └── DependencyInjection.cs             # Service registration for DI
│   │
│   └── DotnetTokenKiller.Cli/                 # Spectre.Console entry point
│       ├── DotnetTokenKiller.Cli.csproj
│       ├── Program.cs                         # Entry point + CommandApp configuration
│       ├── Commands/
│       │   ├── DotnetBuildCommand.cs          # dtk dotnet build
│       │   ├── DotnetTestCommand.cs           # dtk dotnet test
│       │   ├── DotnetRestoreCommand.cs        # dtk dotnet restore
│       │   ├── DotnetPublishCommand.cs        # dtk dotnet publish
│       │   ├── DotnetPackCommand.cs           # dtk dotnet pack
│       │   ├── DotnetCleanCommand.cs          # dtk dotnet clean
│       │   ├── DotnetRunCommand.cs            # dtk dotnet run
│       │   ├── DotnetEfCommand.cs             # dtk dotnet ef
│       │   ├── DotnetFormatCommand.cs         # dtk dotnet format
│       │   ├── DotnetNugetCommand.cs          # dtk dotnet nuget
│       │   ├── GainCommand.cs                 # dtk gain (analytics)
│       │   └── Settings/
│       │       ├── DotnetCommandSettings.cs   # Shared settings for dotnet subcommands
│       │       └── GainCommandSettings.cs     # Settings for gain command
│       └── Infrastructure/
│           └── TypeRegistrar.cs               # Spectre.Console DI integration
│
├── tests/
│   ├── DotnetTokenKiller.Domain.Tests/        # Domain unit tests
│   │   └── DotnetTokenKiller.Domain.Tests.csproj
│   ├── DotnetTokenKiller.Application.Tests/   # Filter & use case tests
│   │   ├── DotnetTokenKiller.Application.Tests.csproj
│   │   ├── Filters/
│   │   │   ├── DotnetBuildFilterTests.cs
│   │   │   ├── DotnetTestFilterTests.cs
│   │   │   └── ...
│   │   ├── Fixtures/                          # Real command output captures
│   │   │   ├── dotnet_build_success.txt
│   │   │   ├── dotnet_build_errors.txt
│   │   │   ├── dotnet_test_all_pass.txt
│   │   │   ├── dotnet_test_failures.txt
│   │   │   ├── dotnet_restore_raw.txt
│   │   │   └── ...
│   │   └── Snapshots/                         # Verify.NET snapshot files
│   │       └── ...verified.txt
│   ├── DotnetTokenKiller.Infrastructure.Tests/ # Infrastructure integration tests
│   │   ├── DotnetTokenKiller.Infrastructure.Tests.csproj
│   │   └── Tracking/
│   │       └── SqliteTrackerTests.cs
│   └── DotnetTokenKiller.Cli.IntegrationTests/ # End-to-end CLI tests
│       ├── DotnetTokenKiller.Cli.IntegrationTests.csproj
│       └── DotnetBuildIntegrationTests.cs
│
├── scripts/
│   ├── test-all.ps1                   # PowerShell smoke test suite
│   └── benchmark.ps1                  # Performance benchmarks
│
└── docs/
    └── tracking.md                    # Token tracking documentation
```

---

## Clean Architecture Layer Responsibilities

### Domain Layer (`DotnetTokenKiller.Domain`)

The innermost layer. Contains:

- **Interfaces (contracts)**: `IOutputFilter`, `ITracker`, `ICommandRunner`, `ITeeService`, `IConfigProvider`
- **Entities**: `CommandRecord`, `GainSummary`
- **Value objects**: `CommandResult`, `DtkConfig` (configuration models)

**Rules**: No NuGet dependencies. No references to other projects. Pure C# only.

### Application Layer (`DotnetTokenKiller.Application`)

Contains the business logic:

- **Use cases**: `FilteredRunUseCase` (the core "run → filter → tee → track" orchestration), `GainReportUseCase`
- **Filter implementations**: All `IOutputFilter` implementations live here (build, test, restore, etc.)
- **Helpers**: Token estimation, ANSI stripping, text formatting

**References**: Domain only.

### Infrastructure Layer (`DotnetTokenKiller.Infrastructure`)

Implements Domain interfaces with real I/O:

- `SqliteTracker` implements `ITracker`
- `ProcessCommandRunner` implements `ICommandRunner`
- `FileTeeService` implements `ITeeService`
- `JsonConfigProvider` implements `IConfigProvider`

**References**: Domain only. Contains all NuGet dependencies for external concerns.

### CLI Layer (`DotnetTokenKiller.Cli`)

The composition root and entry point:

- Spectre.Console `CommandApp` configuration
- Command classes that delegate to Application use cases
- DI container setup (wires Infrastructure implementations to Domain interfaces)
- `TypeRegistrar` / `TypeResolver` for Spectre.Console DI integration

**References**: Application, Infrastructure, Domain (composition root).

---

## Project Dependencies

```
DotnetTokenKiller.Cli
  ├── DotnetTokenKiller.Application
  │   └── DotnetTokenKiller.Domain
  └── DotnetTokenKiller.Infrastructure
      └── DotnetTokenKiller.Domain
```

### NuGet Dependencies by Project

| Project | Dependencies | Purpose |
|---|---|---|
| **Domain** | *(none)* | Pure contracts and entities |
| **Application** | *(none)* | Business logic, references Domain only |
| **Infrastructure** | `Microsoft.Data.Sqlite` | SQLite tracking |
| **Cli** | `Spectre.Console.Cli` | CLI framework, command parsing, rendering |

### Built-in .NET 10 Capabilities (No NuGet Needed)

- `System.Text.RegularExpressions` — `[GeneratedRegex]` for source-generated compiled patterns
- `System.Diagnostics.Process` — process execution
- `System.Text.Json` — JSON config serialization (source-generated)
- `System.IO` — file I/O, tee output
- `System.Security.Cryptography` — hashing if needed

---

## `Directory.Build.props` (Shared Build Properties)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
  </PropertyGroup>
</Project>
```

---

## CLI Project: `DotnetTokenKiller.Cli.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>DotnetTokenKiller.Cli</RootNamespace>
    <AssemblyName>dtk</AssemblyName>

    <!-- .NET Global Tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>dtk</ToolCommandName>
    <PackageId>DotnetTokenKiller</PackageId>
    <Version>0.1.0</Version>
    <Description>Dotnet Token Killer — Minimize LLM token consumption for .NET CLI output</Description>

    <!-- Trimming for smaller binaries -->
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console.Cli" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DotnetTokenKiller.Application\DotnetTokenKiller.Application.csproj" />
    <ProjectReference Include="..\DotnetTokenKiller.Infrastructure\DotnetTokenKiller.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

---

## Test Projects

All test projects target `net10.0` and use the same test stack:

| Package | Purpose |
|---|---|
| `xunit` | Test framework |
| `Verify.Xunit` | Snapshot testing |
| `FluentAssertions` | Expressive assertions |
| `NSubstitute` | Mocking Domain interfaces |
| `Microsoft.NET.Test.Sdk` | Test runner infrastructure |

The Application test project references `DotnetTokenKiller.Application` and uses real filter implementations against fixture files. Infrastructure tests may use real SQLite in-memory databases. CLI integration tests run the actual `dtk` binary against sample projects.

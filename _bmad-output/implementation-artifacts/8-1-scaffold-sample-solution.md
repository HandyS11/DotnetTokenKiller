# Story 8.1: Scaffold Sample Solution

Status: review

## Story

As a developer,
I want a `/sample` folder containing small .NET projects that cover every `dtk` command scenario including failure paths,
So that integration tests have a stable, realistic foundation to run against without any runtime file injection.

## Acceptance Criteria

1. **Given** the repository root contains a `sample/` folder with the following structure:

   ```sh
   sample/
   ├── DotnetTokenKiller.Sample.slnx
   ├── SampleApp/                  # console app — build, run, publish, pack, clean, restore (success)
   ├── SampleApp.Tests/            # xunit project — test (success + failure)
   ├── SampleApp.EfCore/           # EF Core project — ef (success + failure)
   ├── SampleApp.Broken/           # project with deliberate compile error — build, publish, pack (failure)
   └── SampleApp.BadPackage/       # project referencing a non-existent NuGet package — restore (failure)
   ```

   **When** `dotnet build sample/DotnetTokenKiller.Sample.slnx` is run
   **Then** `SampleApp`, `SampleApp.Tests`, and `SampleApp.EfCore` build without errors
   **And** `SampleApp.Broken` and `SampleApp.BadPackage` are intentionally broken and expected to fail — they are NOT in the sample solution (excluded via omission from `DotnetTokenKiller.Sample.slnx`)
   **And** all buildable projects inherit `net10.0`, `LangVersion=14`, `Nullable=enable`, and `TreatWarningsAsErrors=true` from the root `Directory.Build.props` with no `<TargetFramework>` or `<LangVersion>` overrides in their `.csproj` files

2. **Given** `SampleApp` is a console application
   **When** its source is inspected
   **Then** `Program.cs` prints at least one line to stdout (to exercise `dtk dotnet run` output preservation)
   **And** `Program.cs` checks for a `--fail` argument and calls `Environment.Exit(1)` when present
   **And** it is configured with `<PackageId>SampleApp</PackageId>` and `<Version>1.0.0</Version>` (to exercise `dtk dotnet pack`)

3. **Given** `SampleApp.Tests` is an xunit test project
   **When** its source is inspected
   **Then** it contains at least 3 passing tests and exactly 1 intentionally failing test (`Assert.True(false, "Intentional failure")`)
   **And** the failing test is in a class named `IntentionallyFailingTests`

4. **Given** `SampleApp.EfCore` is a class library project
   **When** its source is inspected
   **Then** it references `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design`
   **And** it contains a `SampleDbContext` with at least one `DbSet<>` entity
   **And** it contains exactly one committed EF migration named `InitialCreate` (files committed, not generated at test time)

5. **Given** `SampleApp.Broken` is a console application
   **When** its source is inspected
   **Then** it contains a `.cs` file with a deliberate syntax error (e.g., `int x = "not an int";`) causing exactly 1 `CS`-prefixed compiler error with a file path and line number

6. **Given** `SampleApp.BadPackage` is a class library project
   **When** its source is inspected
   **Then** its `.csproj` references a NuGet package that does not exist (e.g., `<PackageReference Include="DotnetTokenKiller.DoesNotExist" Version="99.0.0" />`)
   **And** `<RestoreLockedMode>false</RestoreLockedMode>` is NOT set so restore actually attempts and fails with a `NU1101` error

7. **And** the `sample/` folder is NOT added to `DotnetTokenKiller.slnx`
8. **And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

## Tasks / Subtasks

- [x] Create `sample/` folder structure at repo root (AC: #1)
  - [x] Create `sample/DotnetTokenKiller.Sample.slnx` referencing only SampleApp, SampleApp.Tests, SampleApp.EfCore
  - [x] Add `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design` to root `Directory.Packages.props`

- [x] Scaffold `SampleApp` console app (AC: #2)
  - [x] Create `sample/SampleApp/SampleApp.csproj` (OutputType=Exe, no TF/LangVersion overrides, PackageId+Version set)
  - [x] Create `sample/SampleApp/Program.cs` with stdout output + `--fail` → `Environment.Exit(1)` logic

- [x] Scaffold `SampleApp.Tests` xunit project (AC: #3)
  - [x] Create `sample/SampleApp.Tests/SampleApp.Tests.csproj` (IsTestProject=true, refs xunit + FluentAssertions)
  - [x] Create test classes with ≥3 passing tests + exactly 1 failing test in `IntentionallyFailingTests`
  - [x] Add `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `coverlet.collector` references (all already in root Directory.Packages.props)

- [x] Scaffold `SampleApp.EfCore` class library (AC: #4)
  - [x] Create `sample/SampleApp.EfCore/SampleApp.EfCore.csproj` (no TF override, refs EF Core packages)
  - [x] Create `SampleDbContext.cs` with at least one `DbSet<>` entity
  - [x] Run `dotnet ef migrations add InitialCreate --project sample/SampleApp.EfCore` and commit migration files

- [x] Scaffold `SampleApp.Broken` console app (AC: #5)
  - [x] Create `sample/SampleApp.Broken/SampleApp.Broken.csproj` (standalone, NOT in sample slnx)
  - [x] Create a `.cs` file with exactly 1 deliberate compile error (type mismatch)

- [x] Scaffold `SampleApp.BadPackage` class library (AC: #6)
  - [x] Create `sample/SampleApp.BadPackage/SampleApp.BadPackage.csproj` (standalone, NOT in sample slnx)
  - [x] Reference non-existent package with explicit `Version="99.0.0"` inline (not from central management)

- [x] Verify solution integrity (AC: #7, #8)
  - [x] Confirm `DotnetTokenKiller.slnx` does NOT reference anything under `sample/`
  - [x] Run `dotnet build sample/DotnetTokenKiller.Sample.slnx` and confirm it succeeds
  - [x] Run `dotnet test DotnetTokenKiller.slnx` and confirm all tests still pass

## Dev Notes

### Critical Architecture Constraints

**Root `Directory.Build.props` applies to `sample/` projects automatically** because it sits in an ancestor directory. This means:

- `TreatWarningsAsErrors=true` applies to all sample projects
- All analyzer packages (Roslynator, SonarAnalyzer, Microsoft.CodeAnalysis.NetAnalyzers) are referenced in every sample project
- `GenerateDocumentationFile=true` applies — but `CS1591` is already suppressed via `NoWarn`
- `CA1515` ("Consider making public types internal") may fire on simple sample types. Suppress in `.csproj` if needed: `<NoWarn>$(NoWarn);CA1515</NoWarn>`
- `ImplicitUsings=enable` — `System`, `System.Collections.Generic`, etc. are available without explicit using

**Solution isolation**: `sample/DotnetTokenKiller.Sample.slnx` is a completely separate solution. It is intentionally NOT added to `DotnetTokenKiller.slnx`. This means `dotnet build DotnetTokenKiller.slnx` will never touch the sample folder.

### Central Package Management for EF Core

The root `Directory.Packages.props` uses `ManagePackageVersionsCentrally=true`. EF Core packages (`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`) are not currently listed there. You **must** add them to the root `Directory.Packages.props` before referencing them in `SampleApp.EfCore.csproj`.

Use the latest stable EF Core version compatible with net10.0. As of 2026-03, EF Core 9.x (9.0.x) is the latest stable release for net9.0 (compatible with net10.0 via TFM compatibility), and EF Core 10.0 previews may be available. Check NuGet for the latest stable: `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design`.

**`SampleApp.BadPackage` exception**: This project intentionally has a bad package reference. Since the package version must be explicit (not from central management), you have two options:

1. Add a `Directory.Packages.props` inside `sample/SampleApp.BadPackage/` that sets `ManagePackageVersionsCentrally=false`, then use explicit `Version=` attribute in the `PackageReference`
2. Or add the fake package to root `Directory.Packages.props` with the bad version `99.0.0`

Option 1 is cleaner (keeps the fake version isolated). Create `sample/SampleApp.BadPackage/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

### EF Migrations — Must Be Committed

EF migrations for `SampleApp.EfCore` **must be committed to the repo** (not generated at test time). To generate them:

1. First ensure `dotnet-ef` tool is installed: `dotnet tool install --global dotnet-ef`
2. The `SampleApp.EfCore` project needs a startup project with DI or a `IDesignTimeDbContextFactory<SampleDbContext>` (simplest approach for a class library)
3. Run: `dotnet ef migrations add InitialCreate --project sample/SampleApp.EfCore --startup-project sample/SampleApp.EfCore`
4. Alternatively implement `IDesignTimeDbContextFactory<SampleDbContext>` in the EfCore project to avoid needing a startup project

The migration generates: `Migrations/InitialCreate.cs`, `Migrations/<timestamp>_InitialCreate.cs`, and `Migrations/SampleDbContextModelSnapshot.cs`. **Commit all three files.**

### `SampleApp` — Deliberate Test-Friendliness

`Program.cs` must be simple and deterministic:

```csharp
// Program.cs — handles --fail arg for integration test coverage
if (args.Contains("--fail"))
{
    Console.WriteLine("App starting...");
    Environment.Exit(1);
}
Console.WriteLine("Hello from SampleApp!");
```

The `--fail` check enables `dtk dotnet run` failure path integration tests (Story 8.4).

### `SampleApp.Tests` — Deterministic Failure for Integration Tests

The failing test class name `IntentionallyFailingTests` is load-bearing — integration tests in Story 8.3 use `--filter "FullyQualifiedName!~IntentionallyFailing"` to exclude it deterministically.

```csharp
namespace SampleApp.Tests;

public class IntentionallyFailingTests
{
    [Fact]
    public void AlwaysFails() => Assert.True(false, "Intentional failure");
}
```

### `SampleApp.Broken` — Single Deterministic Error

The compile error must be exactly 1. A type mismatch is reliable:

```csharp
// BrokenClass.cs
namespace SampleApp.Broken;

public class BrokenClass
{
    public static void Run()
    {
        int x = "not an int"; // CS0029 — cannot implicitly convert
    }
}
```

Do NOT use `Program.cs` for the error (top-level statements behave differently with analyzers). Use a regular class file.

### Project Structure Notes

```sh
sample/
├── DotnetTokenKiller.Sample.slnx        # Solution with only: SampleApp, SampleApp.Tests, SampleApp.EfCore
├── SampleApp/
│   ├── SampleApp.csproj                 # OutputType=Exe, PackageId=SampleApp, Version=1.0.0
│   └── Program.cs
├── SampleApp.Tests/
│   ├── SampleApp.Tests.csproj           # IsTestProject=true
│   ├── SampleTests.cs                   # ≥3 passing tests
│   └── IntentionallyFailingTests.cs     # exactly 1 failing test
├── SampleApp.EfCore/
│   ├── SampleApp.EfCore.csproj          # Sdk=Microsoft.NET.Sdk (class lib)
│   ├── SampleDbContext.cs
│   ├── SampleItem.cs                    # or whatever entity
│   └── Migrations/
│       ├── <timestamp>_InitialCreate.cs
│       └── SampleDbContextModelSnapshot.cs
├── SampleApp.Broken/
│   ├── Directory.Packages.props         # Only needed if ManagePackageVersionsCentrally override needed
│   ├── SampleApp.Broken.csproj          # OutputType=Exe, standalone (NOT in sample.slnx)
│   └── BrokenClass.cs                  # int x = "not an int";
└── SampleApp.BadPackage/
    ├── Directory.Packages.props         # ManagePackageVersionsCentrally=false
    ├── SampleApp.BadPackage.csproj      # class lib with bad PackageReference (explicit Version=99.0.0)
    └── Class1.cs                        # minimal placeholder
```

### `.slnx` Format for Sample Solution

The project uses the new `.slnx` XML solution format (same as `DotnetTokenKiller.slnx`). Only the three buildable projects go in it:

```xml
<Solution>
  <Project Path="SampleApp/SampleApp.csproj" />
  <Project Path="SampleApp.Tests/SampleApp.Tests.csproj" />
  <Project Path="SampleApp.EfCore/SampleApp.EfCore.csproj" />
</Solution>
```

### References

- [Source: epics.md — Story 8.1 acceptance criteria, lines 913–967]
- [Source: Architecture.md — Section 10 Testing Strategy, line 310: `[Trait("Category","Integration")]` for integration tests]
- [Source: Directory.Build.props — TreatWarningsAsErrors=true, TargetFramework=net10.0, LangVersion=14, Nullable=enable]
- [Source: Directory.Packages.props — Central package management; xunit 2.9.3, FluentAssertions 8.8.0, coverlet.collector 8.0.0 already present]
- [Source: DotnetTokenKiller.slnx — sample/ must NOT be added here]
- [Source: global.json — SDK 10.0.200 with allowPrerelease=true]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- RCS1141: Added `<param>` (and `<returns>`) XML doc elements to `SampleDbContext` constructor and `SampleDbContextFactory.CreateDbContext` — required by Roslynator with `GenerateDocumentationFile=true`
- RCS1118: Changed `var result` and `var value` to `const int` / `const string` in `SampleTests.cs` — analyzer requires `const` for variables with literal-initializable values
- xUnit2020: Changed `Assert.True(false, "Intentional failure")` to `Assert.Fail("Intentional failure")` — xUnit 2.9.3 analyzer disallows the former pattern; `Assert.Fail` is the correct API
- EF Core 10.0.4 packages resolve successfully; `dotnet-ef` 10.0.3 tool generates migrations with a harmless tool-version warning
- `IDesignTimeDbContextFactory<SampleDbContext>` in `SampleDbContextFactory.cs` enables migrations without a separate startup project

### Completion Notes List

- All 7 task groups completed; `sample/DotnetTokenKiller.Sample.slnx` references only the 3 buildable projects
- EF Core 10.0.4 packages added to root `Directory.Packages.props`; migrations generated and committed (3 files)
- `SampleApp.BadPackage` isolates `ManagePackageVersionsCentrally=false` via its own `Directory.Packages.props`
- `SampleApp.Broken` has exactly 1 CS0029 compile error in `BrokenClass.cs`; valid `Program.cs` top-level statement prevents CS5001
- `IntentionallyFailingTests.AlwaysFails` uses `Assert.Fail("Intentional failure")` — load-bearing class name for Story 8.3 test filtering
- `dotnet build sample/DotnetTokenKiller.Sample.slnx` → succeeded (0 warnings, 0 errors)
- `dotnet test DotnetTokenKiller.slnx` → 200 tests passed, 0 failed, 0 regressions

### File List

- Directory.Packages.props (modified — added EF Core 10.0.4 packages)
- sample/DotnetTokenKiller.Sample.slnx (new)
- sample/SampleApp/SampleApp.csproj (new)
- sample/SampleApp/Program.cs (new)
- sample/SampleApp.Tests/SampleApp.Tests.csproj (new)
- sample/SampleApp.Tests/SampleTests.cs (new)
- sample/SampleApp.Tests/IntentionallyFailingTests.cs (new)
- sample/SampleApp.EfCore/SampleApp.EfCore.csproj (new)
- sample/SampleApp.EfCore/SampleItem.cs (new)
- sample/SampleApp.EfCore/SampleDbContext.cs (new)
- sample/SampleApp.EfCore/SampleDbContextFactory.cs (new)
- sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.cs (new — generated)
- sample/SampleApp.EfCore/Migrations/20260314161654_InitialCreate.Designer.cs (new — generated)
- sample/SampleApp.EfCore/Migrations/SampleDbContextModelSnapshot.cs (new — generated)
- sample/SampleApp.Broken/SampleApp.Broken.csproj (new)
- sample/SampleApp.Broken/Program.cs (new)
- sample/SampleApp.Broken/BrokenClass.cs (new)
- sample/SampleApp.BadPackage/Directory.Packages.props (new)
- sample/SampleApp.BadPackage/SampleApp.BadPackage.csproj (new)
- sample/SampleApp.BadPackage/Class1.cs (new)

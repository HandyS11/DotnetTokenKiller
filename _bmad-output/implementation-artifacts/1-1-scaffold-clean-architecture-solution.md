# Story 1.1: Scaffold Clean Architecture Solution

Status: done

## Story

As a developer,
I want the DotnetTokenKiller solution scaffolded with the correct Clean Architecture project structure and shared build configuration,
so that all subsequent stories have a consistent, compilable foundation to build upon.

## Acceptance Criteria

1. `dotnet build DotnetTokenKiller.slnx` produces zero errors and zero warnings across all four src projects: `DotnetTokenKiller.Domain`, `DotnetTokenKiller.Application`, `DotnetTokenKiller.Infrastructure`, `DotnetTokenKiller.Cli`
2. Project references follow the Clean Architecture dependency rule: Cli → Application + Infrastructure, Application → Domain, Infrastructure → Domain, Domain has zero project references
3. `Directory.Build.props` sets `TargetFramework=net10.0`, `LangVersion=14`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all` (or equivalent `AnalysisLevel=latest` + `AnalysisMode=All`)
4. `Directory.Packages.props` exists with `ManagePackageVersionsCentrally=true` and all NuGet versions defined there — `.csproj` files never specify versions
5. The Cli project is configured as a global tool: `PackAsTool=true`, `ToolCommandName=dtk`, `PackageId=DotnetTokenKiller`
6. Four test projects exist mirroring the four src projects, each referencing `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
7. `dotnet test DotnetTokenKiller.slnx` runs successfully with zero tests and zero failures

## Tasks / Subtasks

- [x] Task 1: Audit and update `Directory.Build.props` (AC: #3)
  - [x] Add `LangVersion>14</LangVersion>` property
  - [x] Change `AnalysisLevel` from `latest` to `latest-all` (removed redundant `AnalysisMode=All`)
  - [x] Add `NoWarn` entries for all suppressed diagnostics: `CA2007`, `CA1062`, `IDE0058`, `CS1591`, `VSTHRD200`, `T0043`
  - [x] Verify `TreatWarningsAsErrors`, `Nullable=enable`, `ImplicitUsings=enable`, `GenerateDocumentationFile=true` are present

- [x] Task 2: Create `src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj` (AC: #1, #2)
  - [x] Zero NuGet `<PackageReference>` entries
  - [x] Zero `<ProjectReference>` entries
  - [x] Verify build outputs no warnings

- [x] Task 3: Create `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj` (AC: #1, #2)
  - [x] `<ProjectReference>` to `DotnetTokenKiller.Domain` only
  - [x] Zero NuGet runtime package references (analyzers from Directory.Build.props only)

- [x] Task 4: Create `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj` (AC: #1, #2)
  - [x] `<ProjectReference>` to `DotnetTokenKiller.Domain` only
  - [x] No Application or Cli references

- [x] Task 5: Create `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (AC: #1, #2, #5)
  - [x] `<ProjectReference>` to Application AND Infrastructure
  - [x] `<PackageReference Include="Spectre.Console.Cli" />` (no version — from Directory.Packages.props)
  - [x] `<PackageReference Include="Spectre.Console" />` (no version)
  - [x] `<PackAsTool>true</PackAsTool>`
  - [x] `<ToolCommandName>dtk</ToolCommandName>`
  - [x] `<PackageId>DotnetTokenKiller</PackageId>`
  - [x] `<AssemblyName>dtk</AssemblyName>` (so the binary is named `dtk`)
  - [x] Minimal `Program.cs` that compiles — uses `await app.RunAsync(args)` (S6966 requires async variant)

- [x] Task 6: Create four test projects (AC: #6, #7)
  - [x] `tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj`
  - [x] `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj`
  - [x] `tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj`
  - [x] `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj`
  - [x] Each test project references: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `coverlet.collector`
  - [x] Each test project references its counterpart src project (except IntegrationTests — no Cli project ref needed at this stage)
  - [x] Suppress `CA1515` (internal types in test projects) per-project via `<NoWarn>$(NoWarn);CA1515</NoWarn>`

- [x] Task 7: Update `DotnetTokenKiller.slnx` to include all 8 projects (AC: #1)
  - [x] Add all four src projects
  - [x] Add all four test projects
  - [x] Use `.slnx` XML format

- [x] Task 8: Final verification (AC: #1, #7)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → 0 tests, 0 failures, exit code 0
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → no format violations

## Dev Notes

### Critical: What Already Exists

The following files **already exist** in the repository root — do NOT recreate them:

| File | State | Action Required |
|---|---|---|
| `DotnetTokenKiller.slnx` | Exists but **empty** (`<Solution></Solution>`) | Update — add all 8 projects |
| `Directory.Build.props` | Exists, partially configured | Update — add `LangVersion=14`, fix `AnalysisLevel`, add suppressed diagnostics |
| `Directory.Packages.props` | Exists, packages defined | Update — may need additional suppression entries |
| `global.json` | Exists, pins SDK `10.0.103` | No changes needed |
| `.editorconfig` | Exists | No changes needed |

**Do NOT touch** `global.json`, `.editorconfig` — these are already correct.

### Directory.Build.props — Required Updates

The existing file needs these specific changes:

1. **Add** `<LangVersion>14</LangVersion>` inside the `<PropertyGroup>`
2. **Change** `<AnalysisLevel>latest</AnalysisLevel>` → `<AnalysisLevel>latest-all</AnalysisLevel>` (and remove `<AnalysisMode>All</AnalysisMode>` since `latest-all` implies it) OR keep both — either works
3. **Add** these `<NoWarn>` suppressions (append to existing `$(NoWarn)`):
   - `CA2007` — ConfigureAwait not required (single-threaded CLI app)
   - `CA1062` — public method argument validation not required
   - `IDE0058` — expression results can be discarded without `_ =`
   - `CS1591` — missing XML doc comments not required (GenerateDocumentationFile=true but no docs needed)
   - `VSTHRD200` — async suffix not required on xUnit test methods
   - `T0043` — primary constructors not mandatory

Merge approach for NoWarn: `<NoWarn>$(NoWarn);NU1901;NU1902;NU1903;NU1904;CA2007;CA1062;IDE0058;CS1591;VSTHRD200;T0043</NoWarn>`

### DotnetTokenKiller.slnx Format

The `.slnx` format is a simplified XML solution file. The correct format for 8 projects:

```xml
<Solution>
  <Project Path="src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj" />
  <Project Path="src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj" />
  <Project Path="src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj" />
  <Project Path="src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj" />
  <Project Path="tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj" />
  <Project Path="tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj" />
  <Project Path="tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj" />
  <Project Path="tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj" />
</Solution>
```

### Minimal Program.cs for CLI

Story 1.1 only needs a compilable entry point. The full CLI wiring comes in Story 1.3. Use a minimal Spectre.Console.Cli bootstrap:

```csharp
using Spectre.Console.Cli;

var app = new CommandApp();
return await app.RunAsync(args);
```

This is enough to compile and pass `dotnet build`. Do NOT add `TypeRegistrar`, commands, or DI yet — those are Story 1.3.

> **Note:** Use `RunAsync` not `Run` — SonarAnalyzer S6966 requires awaiting the async variant. The synchronous `Run(args)` will fail the build.

### csproj File Templates

**Domain project** (must have ZERO external package refs):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DotnetTokenKiller.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

(Inherits all build settings from Directory.Build.props)

**Application project**:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DotnetTokenKiller.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotnetTokenKiller.Domain\DotnetTokenKiller.Domain.csproj" />
  </ItemGroup>
</Project>
```

**Infrastructure project** (no Spectre, no Application reference):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DotnetTokenKiller.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotnetTokenKiller.Domain\DotnetTokenKiller.Domain.csproj" />
  </ItemGroup>
</Project>
```

**CLI project** (composition root):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>DotnetTokenKiller.Cli</RootNamespace>
    <AssemblyName>dtk</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>dtk</ToolCommandName>
    <PackageId>DotnetTokenKiller</PackageId>
    <Version>0.1.0</Version>
    <Description>A .NET CLI proxy that reduces LLM token usage by filtering dotnet command output</Description>
    <PackageTags>dotnet;cli;llm;tokens;productivity</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Spectre.Console" />
    <PackageReference Include="Spectre.Console.Cli" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotnetTokenKiller.Application\DotnetTokenKiller.Application.csproj" />
    <ProjectReference Include="..\..\src\DotnetTokenKiller.Infrastructure\DotnetTokenKiller.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

**Test project template** (all four use this pattern, adjust project ref path):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DotnetTokenKiller.Domain.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CA1515</NoWarn>  <!-- internal test classes are fine -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotnetTokenKiller.Domain\DotnetTokenKiller.Domain.csproj" />
  </ItemGroup>
</Project>
```

Notes on test projects:

- `Domain.Tests` refs `Domain` only
- `Application.Tests` refs `Application` (which transitively includes Domain)
- `Infrastructure.Tests` refs `Infrastructure` (which transitively includes Domain)
- `Cli.IntegrationTests` refs `Cli` (all layers) AND adds `Spectre.Console.Testing`

**Application.Tests** also needs `Spectre.Console.Testing`:

```xml
<PackageReference Include="Spectre.Console.Testing" />
```

(Already in `Directory.Packages.props`)

### Directory Layout to Create

```
src/
  DotnetTokenKiller.Domain/
    DotnetTokenKiller.Domain.csproj
  DotnetTokenKiller.Application/
    DotnetTokenKiller.Application.csproj
  DotnetTokenKiller.Infrastructure/
    DotnetTokenKiller.Infrastructure.csproj
  DotnetTokenKiller.Cli/
    DotnetTokenKiller.Cli.csproj
    Program.cs
tests/
  DotnetTokenKiller.Domain.Tests/
    DotnetTokenKiller.Domain.Tests.csproj
  DotnetTokenKiller.Application.Tests/
    DotnetTokenKiller.Application.Tests.csproj
  DotnetTokenKiller.Infrastructure.Tests/
    DotnetTokenKiller.Infrastructure.Tests.csproj
  DotnetTokenKiller.Cli.IntegrationTests/
    DotnetTokenKiller.Cli.IntegrationTests.csproj
```

No source `.cs` files needed in src projects yet (except Program.cs in Cli). Empty project + csproj is enough for Story 1.1.

### Package Management Rules (CRITICAL)

- `Directory.Packages.props` already has all needed package versions for this story
- **NEVER add `Version="..."` to `<PackageReference>` in any `.csproj`** — versions come from `Directory.Packages.props` only
- Packages already in `Directory.Packages.props`: `Spectre.Console`, `Spectre.Console.Cli`, `Spectre.Console.Testing`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `coverlet.collector`
- Packages NOT yet in `Directory.Packages.props` (needed in later stories, do NOT add now): `Microsoft.Data.Sqlite`, `Verify.Xunit`, `NSubstitute`

### Project Path Conventions in ProjectReference

Use forward slashes consistently. From `tests/DotnetTokenKiller.Domain.Tests/` to `src/DotnetTokenKiller.Domain/`:

- `..\..\src\DotnetTokenKiller.Domain\DotnetTokenKiller.Domain.csproj` (Windows backslash — both styles work on .NET)
- OR `../../src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj` (forward slash — preferred for cross-platform)

### Potential Build Failures to Watch For

1. **CS1591 warnings** — If `GenerateDocumentationFile=true` is set globally and any public API has no XML docs, this fires as an error. The `CS1591` suppression in `NoWarn` must be present before creating any source files with public types.

2. **Analyzer warnings as errors** — Roslynator and SonarAnalyzer may fire on even minimal code. Ensure `NoWarn` entries are correct before adding any `.cs` files.

3. **Empty projects may need a dummy namespace** — Some analyzers may warn if a project has no source files at all. If needed, add a minimal placeholder (e.g., an empty namespace declaration file), but prefer not to.

4. **CA1515 in test projects** — xunit test classes are typically `internal` by default with the `IsTestProject=true` setting. The `CA1515` analyzer requires types to be internal, which conflicts with xunit's expectations. Suppress `CA1515` in test project `.csproj` files.

### Project Structure Notes

- Architecture strictly enforces inner-layer isolation: `Domain` MUST NOT reference any NuGet package or project — this is validated by inspection
- This story is the foundation for all subsequent stories — any dependency rule violation here cascades to all later stories
- The `Cli.IntegrationTests` project will require the real `dtk` binary to be installed; for this story it just needs to compile (no test methods yet)

### References

- Architecture: [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- Architecture: [Source: _bmad-output/planning-artifacts/Architecture.md#4. Layer Responsibilities]
- Architecture: [Source: _bmad-output/planning-artifacts/Architecture.md#9. DI Registration]
- Architecture: [Source: _bmad-output/planning-artifacts/Architecture.md#11. Build and Distribution]
- Project Context: [Source: _bmad-output/project-context.md#Framework-Specific Rules — Clean Architecture]
- Project Context: [Source: _bmad-output/project-context.md#Code Quality & Style Rules]
- Project Context: [Source: _bmad-output/project-context.md#Suppressed Diagnostics]
- Epic 1 Story 1.1: [Source: _bmad-output/planning-artifacts/epics.md#Story 1.1: Scaffold Clean Architecture Solution]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- SonarAnalyzer S6966 fired on `app.Run(args)` — fixed by switching to `await app.RunAsync(args)` (async entry point, compiler generates async Main)

### Completion Notes List

- Updated `Directory.Build.props`: added `LangVersion=14`, changed `AnalysisLevel` to `latest-all`, removed `AnalysisMode=All` (implied), added NoWarn suppressions CA2007/CA1062/IDE0058/CS1591/VSTHRD200/T0043
- Created 4 src projects: Domain (zero deps), Application (→ Domain), Infrastructure (→ Domain), Cli (→ Application + Infrastructure, global tool config)
- Created 4 test projects: each with xunit/FluentAssertions/Microsoft.NET.Test.Sdk/coverlet; Application.Tests + Cli.IntegrationTests also include Spectre.Console.Testing; CA1515 suppressed per test project
- `Program.cs` uses `await app.RunAsync(args)` not `app.Run(args)` — SonarAnalyzer S6966 requires the async variant
- Updated `DotnetTokenKiller.slnx` with all 8 projects in slnx XML format
- All verification passed: `dotnet build` 0 errors 0 warnings, `dotnet test` 0 tests 0 failures exit 0, `dotnet format --verify-no-changes` exit 0

### File List

- Directory.Build.props (modified)
- DotnetTokenKiller.slnx (modified)
- src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj (created)
- src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj (created)
- src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj (created)
- src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj (created)
- src/DotnetTokenKiller.Cli/Program.cs (created)
- tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj (created)
- tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj (created)
- tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj (created)
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj (created, then fixed: removed Spectre.Console.Testing)
- _bmad-output/implementation-artifacts/1-1-scaffold-clean-architecture-solution.md (Dev Notes: corrected Program.cs example to use await RunAsync, added S6966 warning note)

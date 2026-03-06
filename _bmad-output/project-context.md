---
project_name: 'DotnetTokenKiller'
user_name: 'HandyS11'
date: '2026-03-06'
sections_completed: ['technology_stack', 'language_rules', 'framework_rules', 'testing_rules', 'code_quality', 'workflow_rules', 'critical_rules']
status: complete
rule_count: 55
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

- Runtime: .NET 10.0 (`net10.0`), C# 14
- CLI Framework: Spectre.Console.Cli 0.53.1 + Spectre.Console 0.54.0
- Testing: xunit 2.9.3, Spectre.Console.Testing 0.54.0
- Assertions: FluentAssertions 8.8.0
- Snapshot testing: Verify.Xunit (add to Directory.Packages.props before use)
- Mocking: NSubstitute (add to Directory.Packages.props before use)
- Database: Microsoft.Data.Sqlite (add to Directory.Packages.props before use)
- Serialization: System.Text.Json (built-in, use source generators for AOT)
- Process execution: System.Diagnostics.Process (built-in)
- Regex: System.Text.RegularExpressions with [GeneratedRegex] (built-in)
- Analyzers (build-time only): Roslynator 4.15.0, SonarAnalyzer.CSharp 10.20.0, Microsoft.CodeAnalysis.NetAnalyzers 10.0.103, Microsoft.VisualStudio.Threading.Analyzers 17.14.15
- Package management: Central via Directory.Packages.props — .csproj files NEVER specify versions

### Language-Specific Rules

- **File-scoped namespaces**: Always use `namespace Foo;` — never block-scoped `namespace Foo { }`
- **`var` everywhere**: Use `var` for all local variable declarations — enforced by editorconfig
- **Private fields**: Prefix with underscore — `_camelCase` (e.g., `_tracker`, `_config`)
- **Private static fields**: Also `_camelCase` (NOT `s_camelCase` — editorconfig maps both to same style)
- **Async methods**: MUST end in `Async` — enforced as a warning; xUnit test methods are exempt (VSTHRD200 suppressed)
- **Interfaces**: Prefix `I` — `IPascalCase` (e.g., `IOutputFilter`, `ITracker`)
- **Type parameters**: Prefix `T` — `TPascalCase` (e.g., `TSettings`, `TResult`)
- **Primary constructors**: Preferred (`csharp_style_prefer_primary_constructors = true`) but T0043 is suppressed so both styles are accepted
- **`[GeneratedRegex]`**: ALL regex patterns must use `[GeneratedRegex]` attribute on `private static partial` methods — never `new Regex(...)` or compiled options at runtime
- **`System.Text.Json` source generators**: All JSON serialization must use `[JsonSerializable]` source-generated contexts — required for AOT compatibility
- **Nullable reference types**: Enabled project-wide — always annotate nullability correctly; no `!` suppressions without justification
- **`TreatWarningsAsErrors=true`**: Every analyzer warning is a build error — code must compile with zero warnings
- **XML documentation**: `GenerateDocumentationFile=true` but CS1591 is suppressed — do not add XML docs unless asked; do not leave `///` stubs
- **Suppress diagnostics in code**: Never suppress with `#pragma warning disable` — fix the root cause or add to `.editorconfig`
- **`readonly` fields**: Prefer `readonly` where possible — `dotnet_style_readonly_field = true:warning`
- **`static` local functions**: Prefer `static` on local functions when they don't capture — `csharp_prefer_static_local_function = true:warning`

### Framework-Specific Rules

#### Clean Architecture — Dependency Rules (CRITICAL)

- **Dependency direction**: Domain ← Application ← CLI; Infrastructure ← Domain only
  - `Domain` project: ZERO external NuGet dependencies, ZERO project references
  - `Application` project: references `Domain` only — no Infrastructure, no Spectre.Console
  - `Infrastructure` project: references `Domain` only
  - `Cli` project: references `Application` + `Infrastructure` (composition root only)
- **Interfaces live in Domain**: All contracts (`IOutputFilter`, `ITracker`, `ICommandRunner`, `ITeeService`, `IConfigProvider`) are defined in `DotnetTokenKiller.Domain`
- **Implementations live in Infrastructure or Application**: Infra implements Domain interfaces with real I/O; Application implements business logic (filters, use cases, helpers)
- **Helpers are pure**: `TokenEstimator`, `AnsiStrip`, `TextHelpers` live in Application — pure functions with zero I/O and zero domain interface dependencies

#### Spectre.Console.Cli — Command Pattern

- **Commands are thin adapters**: Command classes only translate Spectre settings → use case call → return exit code; NO business logic in command classes
- **`AsyncCommand<TSettings>`**: All commands inherit `AsyncCommand<TSettings>`, not `Command<TSettings>`
- **DI via TypeRegistrar**: Commands receive dependencies via constructor injection through the `TypeRegistrar`/`TypeResolver` pattern — never use `new` inside commands
- **Remaining arguments**: Unrecognized flags/args must be captured and forwarded verbatim to the underlying `dotnet` process — DTK must NEVER consume dotnet CLI flags
- **Settings base class**: All `dotnet` subcommands share `DotnetCommandSettings` — add shared options (verbosity, remaining args) there, not per-command

#### IOutputFilter — Filter Contract

- **Pure functions only**: Filters must be stateless and side-effect-free — no I/O, no console writes, no config reads, no `static` mutable state
- **Never throw to caller**: Wrap all internal logic in try/catch; return raw input on failure (fail-safe)
- **Input is combined stdout + stderr**: Filters receive merged output as a single string, not separate streams
- **Output format conventions**:
  - Success (no issues): `✓ dotnet <subcommand> (<context>, <time>)`
  - Success with warnings: header line + `═══` separator + grouped details
  - Failure: `dotnet <subcommand>: N errors, M warnings (<context>)` + `═══` separator + structured details
- **Path shortening**: Always convert absolute paths to project-relative; use forward slashes on all platforms

#### FilteredRunUseCase — Orchestration

- **Exit code always propagated**: The underlying `dotnet` process exit code must always be returned unchanged — never hardcode 0
- **Fail-safe filter wrapping**: If `IOutputFilter.Apply()` throws, catch silently, log if verbose, return raw output
- **Tracking is silent**: `ITracker` errors must never surface to the user — wrap in try/catch and swallow
- **Tee is silent**: `ITeeService` errors must never surface — same rule
- **Concurrent stream reads**: `ProcessCommandRunner` must read stdout and stderr concurrently (two tasks) to prevent deadlock on large output

### Testing Rules

#### Test Project Boundaries

- **Domain.Tests**: Unit tests for entities and value objects only — no mocks needed (pure C#)
- **Application.Tests**: Filter and use case tests — mock Domain interfaces with NSubstitute; NO real I/O; fixture files as embedded resources
- **Infrastructure.Tests**: SQLite tests use `Data Source=:memory:` — never a real file; config tests use `Path.GetTempPath()` directories; always clean up in `Dispose`
- **Cli.IntegrationTests**: Require real `dtk` binary installed; mark with `[Trait("Category", "Integration")]`; skipped in standard CI run

#### Filter Testing — Mandatory Checklist (per filter)

Every new filter MUST have ALL of the following:

1. Fixture file from real `dotnet` output (embedded resource in test project)
2. Snapshot test via `Verify(output)` — `.verified.txt` must be committed
3. Token savings test: assert `savings >= 60` where `savings = 100 - (outputTokens / inputTokens * 100)`
4. Empty string input test — must not throw, must return non-null
5. Malformed/non-dotnet input test — return raw or best-effort
6. ANSI escape code input test — strip before processing
7. Unicode content test — no crashes

#### Token Savings Targets (hard gate)

| Filter | Minimum Savings |
|---|---|
| `dotnet build` success | 85% |
| `dotnet build` errors | 70% |
| `dotnet test` all pass | 90% |
| `dotnet test` failures | 70% |
| `dotnet restore` | 90% |
| `dotnet clean` | 95% |
| `dotnet publish` | 80% |
| All others | 60% |

#### Snapshot Workflow

- Run `dotnet test` → creates `.received.txt` for new snapshots
- Run `dotnet verify accept` to approve → creates `.verified.txt`
- **Commit `.verified.txt` files** — they are the baseline, not generated artifacts
- Snapshot files live alongside test files (not in a separate folder)

#### xUnit Conventions

- `[Fact]` for single-case tests; `[Theory]` + `[InlineData]` for parameterized (edge cases across all filters)
- Async test methods do NOT need `Async` suffix (VSTHRD200 suppressed)
- Test class names: `{Subject}Tests` (e.g., `DotnetBuildFilterTests`)
- No `CA1515` (internal classes) in test projects — suppressed

#### NSubstitute Mocking

- Mock only Domain interfaces (`ICommandRunner`, `ITracker`, `ITeeService`, `IConfigProvider`)
- Never mock concrete classes or Application helpers
- Use `Substitute.For<IInterface>()` — never partial mocks

### Code Quality & Style Rules

#### Formatting (enforced by `dotnet format` + editorconfig)

- **Indentation**: 4 spaces for `.cs`; 2 spaces for `.csproj`, `.props`, `.json`, `.yaml`, `.xml`, `.md`
- **Line endings**: LF only — never CRLF
- **Max line length**: 120 characters
- **No trailing whitespace**, no BOM, final newline required on all files
- **Object/collection initializers**: always chop (one member per line)

#### Naming (enforced as warnings → build errors)

| Symbol | Convention | Example |
|---|---|---|
| Types, namespaces | PascalCase | `DotnetBuildFilter` |
| Interfaces | `I` + PascalCase | `IOutputFilter` |
| Type parameters | `T` + PascalCase | `TSettings` |
| Methods, properties, events | PascalCase | `ApplyFilter` |
| Private/protected fields | `_camelCase` | `_tracker` |
| Local variables, parameters | camelCase | `rawOutput` |
| Constants (public) | PascalCase | `MaxFileSize` |
| Async methods | PascalCase + `Async` | `RunCapturedAsync` |

#### Code Organization

- **Namespace matches folder**: file path must match namespace (`dotnet_style_namespace_match_folder = true`)
- **Usings outside namespace**: `csharp_using_directive_placement = outside_namespace`
- **No `this.` qualification** on any member type
- **Prefer language keywords**: `int` not `Int32`, `string` not `String`
- **Pattern matching over casts**: prefer `is`, switch expressions, extended property patterns
- **Expression-bodied properties/accessors**: preferred; methods: not enforced

#### Suppressed Diagnostics (do not re-enable without discussion)

- `CA2007` — ConfigureAwait not required (single-threaded CLI)
- `CA1062` — public method argument validation not required
- `IDE0058` — expression results can be discarded without `_ =`
- `CS1591` — missing XML doc comments not required
- `VSTHRD200` — async suffix not required on xUnit test methods
- `T0043` — primary constructors not mandatory

#### Pre-commit Gate

Install once: `git config core.hooksPath .githooks`

Runs automatically on commit:

- `dotnet format DotnetTokenKiller.slnx --no-restore` (formats staged `.cs` files)
- Validates `.csproj`/`.props` files

### Development Workflow Rules

#### Solution & Build

- **Solution file**: `DotnetTokenKiller.slnx` (`.slnx` format, not `.sln`) — always pass to `dotnet` commands
- **Build**: `dotnet build DotnetTokenKiller.slnx`
- **Test**: `dotnet test DotnetTokenKiller.slnx`
- **Single test**: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`
- **Format**: `dotnet format DotnetTokenKiller.slnx --no-restore`
- **Verify format**: `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`

#### Package Management

- All versions in `Directory.Packages.props` — never in `.csproj`
- To add a package: add `<PackageVersion Include="..." Version="..."/>` to `Directory.Packages.props`, then `<PackageReference Include="..."/>` (no version) in the `.csproj`
- Three packages not yet added that will be needed: `Microsoft.Data.Sqlite`, `Verify.Xunit`, `NSubstitute`

#### Git

- Main branch: `develop`
- Pre-commit hook: `.githooks/` — install with `git config core.hooksPath .githooks`
- Hook auto-formats staged `.cs` files and validates `.csproj`/`.props`

#### Distribution

- Binary name: `dtk` (`<AssemblyName>dtk</AssemblyName>`, `<ToolCommandName>dtk</ToolCommandName>`)
- Install: `dotnet tool install -g DotnetTokenKiller`
- `<PublishTrimmed>true</PublishTrimmed>` + `<TrimMode>link</TrimMode>` — all code must be trim-safe

#### Platform Data Paths

| Data | Windows | Linux/macOS |
|---|---|---|
| Tracking DB | `%LOCALAPPDATA%/dtk/tracking.db` | `~/.local/share/dtk/tracking.db` |
| Config | `%APPDATA%/dtk/config.json` | `~/.config/dtk/config.json` |
| Tee output | `%LOCALAPPDATA%/dtk/tee/` | `~/.local/share/dtk/tee/` |
| DB override | `DTK_DB_PATH` env var | same |
| Tee override | `DTK_TEE_DIR` env var | same |

### Critical Don't-Miss Rules

#### Architecture Anti-Patterns (NEVER do these)

- **Never add NuGet packages to `DotnetTokenKiller.Domain`** — it must remain dependency-free
- **Never reference Infrastructure from Application** — use Domain interfaces only
- **Never put business logic in CLI commands** — commands are adapters, not use cases
- **Never consume `dotnet` CLI flags in DTK** — all args after the subcommand are forwarded verbatim
- **Never write `new Regex(...)` at runtime** — use `[GeneratedRegex]` partial methods only
- **Never use `JsonSerializer` without source-generated context** — breaks AOT/trimming
- **Never suppress warnings with `#pragma warning disable`** — fix the cause or update `.editorconfig`

#### Filter Anti-Patterns

- **Never make a filter stateful** — filters must be safe to reuse; no mutable instance fields
- **Never let a filter throw** — every `Apply()` must catch all exceptions and return raw input as fallback
- **Never write to console inside a filter** — output goes through the use case only
- **Never assume ANSI codes are absent** — always strip before line counting or token estimation
- **Never hardcode absolute paths in output** — always shorten to project-relative with forward slashes

#### Tracking & Tee Anti-Patterns

- **Never let tracking errors reach the user** — `ITracker` calls must be in try/catch with silent failure
- **Never let tee errors reach the user** — same rule for `ITeeService`
- **Token estimation**: use `chars / 4` heuristic — never a real tokenizer

#### Process Execution

- **Never read stdout and stderr sequentially** — two concurrent `Task.Run` reads to prevent deadlock
- **Never modify the exit code** — return `process.ExitCode` exactly as received
- **Passthrough mode**: inherit all I/O streams, never capture

#### Package Compatibility Notes

- `Verify.Xunit`: also requires the `Verify` base package — verify xunit 2.9.3 compatibility
- `NSubstitute`: use 5.x+ for .NET 10 support
- `Microsoft.Data.Sqlite`: use 6.x+ for .NET 10 era

#### AOT / Trim Safety

- Every class serialized via `System.Text.Json` needs `[JsonSerializable(typeof(...))]` on a `JsonSerializerContext`
- Types resolved via reflection will be trimmed — use source generators or `[DynamicallyAccessedMembers]`
- Spectre.Console.Cli uses reflection for command registration — verify trim compatibility when AOT publishing

---

## Usage Guidelines

**For AI Agents:**

- Read this file before implementing any code in this project
- Follow ALL rules exactly as documented — many are enforced as build errors
- When in doubt, prefer the more restrictive option
- Update this file if new patterns emerge during implementation

**For Humans:**

- Keep this file lean and focused on agent needs
- Update when technology stack or conventions change
- Remove rules that become obvious over time

Last Updated: 2026-03-06

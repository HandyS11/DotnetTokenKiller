# Story 1.3: Implement Process Execution and CLI Entry Point

Status: review

## Story

As a developer using `dtk`,
I want the Spectre.Console CLI entry point wired up with dependency injection and process execution,
so that `dtk dotnet build` routes to the correct command handler and `dotnet build` is actually executed with all arguments forwarded.

## Acceptance Criteria

1. `dtk dotnet build --configuration Release` executes `dotnet build --configuration Release` as a subprocess with all arguments forwarded verbatim
2. The exit code returned by `dtk` exactly matches the exit code of the underlying `dotnet` process
3. `ProcessCommandRunner` implements `ICommandRunner` and reads stdout and stderr **concurrently** to prevent deadlock
4. `TypeRegistrar` and `TypeResolver` in the Cli project bridge Spectre.Console.Cli with `Microsoft.Extensions.DependencyInjection`
5. All Domain interfaces are registered with placeholder stub Infrastructure implementations so the app starts without runtime errors
6. `DotnetCommandSettings` base class captures `-v`/`--verbose` flag (levels 0–2 via count) and remaining args are accessed via `context.Remaining.Raw`
7. `dtk --help` displays the command tree without errors
8. `dtk dotnet --help` displays all registered dotnet subcommands
9. `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
10. `dotnet test DotnetTokenKiller.slnx` → all existing tests pass

## Tasks / Subtasks

- [x] Task 1: Add `Microsoft.Extensions.DependencyInjection` package (AC: #4, #5)
  - [x] Add `<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0"/>` to `Directory.Packages.props`
  - [x] Add `<PackageReference Include="Microsoft.Extensions.DependencyInjection"/>` to `DotnetTokenKiller.Cli.csproj`
  - [x] Add `<PackageReference Include="Microsoft.Extensions.DependencyInjection"/>` to `DotnetTokenKiller.Infrastructure.csproj`

- [x] Task 2: Create `ProcessCommandRunner` (AC: #1, #2, #3)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs`
  - [x] Implement `ICommandRunner.RunCapturedAsync` — redirects stdout+stderr, reads both **concurrently** via `Task.WhenAll`, then awaits exit
  - [x] Implement `ICommandRunner.RunPassthroughAsync` — inherits all I/O streams (no redirect), awaits exit, returns exit code

- [x] Task 3: Create stub Infrastructure implementations (AC: #5)
  - [x] `src/DotnetTokenKiller.Infrastructure/Tracking/NullTracker.cs` — implements `ITracker`, all methods are no-ops
  - [x] `src/DotnetTokenKiller.Infrastructure/Configuration/NullConfigProvider.cs` — implements `IConfigProvider`, `LoadAsync` returns `DtkConfig.Default`
  - [x] `src/DotnetTokenKiller.Infrastructure/Tee/NullTeeService.cs` — implements `ITeeService`, returns `null`

- [x] Task 4: Create `DependencyInjection.cs` in Infrastructure (AC: #5)
  - [x] File: `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs`
  - [x] Static extension method `AddInfrastructure(this IServiceCollection)` registering all four Domain interface → implementation bindings as singletons

- [x] Task 5: Create `TypeRegistrar` and `TypeResolver` in Cli (AC: #4)
  - [x] `src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs` — implements `ITypeRegistrar`, wraps `IServiceCollection`, `Build()` returns a `TypeResolver`
  - [x] `src/DotnetTokenKiller.Cli/Infrastructure/TypeResolver.cs` — implements `ITypeResolver` + `IDisposable`, wraps `IServiceProvider`

- [x] Task 6: Create command settings (AC: #6)
  - [x] `src/DotnetTokenKiller.Cli/Commands/Settings/DotnetCommandSettings.cs` — base settings with `bool[] Verbose` for `-v` flag and `string[] PositionalArgs` for positional forwarding
  - [x] `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs` — settings with `int Days` for `--days` option (default 7)

- [x] Task 7: Create all dotnet subcommand stubs (AC: #1, #7, #8)
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetRunCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs`
  - [x] `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs`
  - [x] Each command: `AsyncCommand<DotnetCommandSettings>`, combines `settings.PositionalArgs + context.Remaining.Raw`, calls `ICommandRunner.RunPassthroughAsync`

- [x] Task 8: Create `GainCommand` stub (AC: #7)
  - [x] `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` — `AsyncCommand<GainCommandSettings>`, returns `Task.FromResult(0)` for now

- [x] Task 9: Rewrite `Program.cs` (AC: #4, #7, #8)
  - [x] Build `ServiceCollection`, call `AddInfrastructure()`, create `TypeRegistrar` (via alias to avoid Spectre conflict), create `CommandApp(registrar)`
  - [x] Configure `AddBranch("dotnet", ...)` with all 10 subcommands
  - [x] Configure `AddCommand<GainCommand>("gain")`
  - [x] Set app name to `"dtk"` via `config.SetApplicationName("dtk")`
  - [x] Configure `StrictParsing = false` so unknown dotnet flags pass to `Remaining`

- [x] Task 10: Build and verify (AC: #9, #10)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all existing 17 tests pass
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0
  - [x] Manual smoke test: `dtk --help` shows command tree
  - [x] Manual smoke test: `dtk dotnet --help` shows all 10 subcommands
  - [x] Manual smoke test: `dtk dotnet build DotnetTokenKiller.slnx --no-restore` → build succeeded

## Dev Notes

### Critical: Current Repository State (After Stories 1.1 + 1.2)

All files below already exist and must NOT be modified (except `Program.cs` which is rewritten):

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — all 8 projects registered |
| `Directory.Build.props` | Complete — `net10.0`, `LangVersion=14`, `TreatWarningsAsErrors=true` |
| `Directory.Packages.props` | Exists — **needs `Microsoft.Extensions.DependencyInjection` added** |
| `src/DotnetTokenKiller.Cli/Program.cs` | Exists — minimal stub, **fully rewritten in this story** |
| `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` | Exists — has Spectre.Console refs + Application + Infrastructure project refs |
| `src/DotnetTokenKiller.Domain/**` | Complete — all 9 Domain contracts and value objects from Story 1.2 |
| `tests/DotnetTokenKiller.Domain.Tests/**` | 17 passing tests — must NOT be broken |

**This story adds new files to**:

- `src/DotnetTokenKiller.Infrastructure/` (ProcessCommandRunner + 3 stubs + DependencyInjection.cs)
- `src/DotnetTokenKiller.Cli/` (TypeRegistrar, TypeResolver, all command classes, all settings, rewritten Program.cs)

**Do NOT modify**:

- Any Domain files from Story 1.2
- `Directory.Build.props`
- Any test files from Story 1.2

### Package Management (CRITICAL)

`Microsoft.Extensions.DependencyInjection` is not yet in `Directory.Packages.props`. Add it **without** a version in `.csproj` files — only in `Directory.Packages.props`:

```xml
<!-- Directory.Packages.props — add inside existing <ItemGroup> -->
<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0"/>
```

```xml
<!-- DotnetTokenKiller.Cli.csproj — add to existing <ItemGroup> with other PackageReferences -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
```

```xml
<!-- DotnetTokenKiller.Infrastructure.csproj — add new <ItemGroup> -->
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
</ItemGroup>
```

### Exact File Structure to Create

```sh
src/DotnetTokenKiller.Infrastructure/
  Execution/
    ProcessCommandRunner.cs
  Tracking/
    NullTracker.cs
  Configuration/
    NullConfigProvider.cs
  Tee/
    NullTeeService.cs
  DependencyInjection.cs

src/DotnetTokenKiller.Cli/
  Infrastructure/
    TypeRegistrar.cs
    TypeResolver.cs
  Commands/
    Settings/
      DotnetCommandSettings.cs
      GainCommandSettings.cs
    DotnetBuildCommand.cs
    DotnetTestCommand.cs
    DotnetRestoreCommand.cs
    DotnetPublishCommand.cs
    DotnetPackCommand.cs
    DotnetCleanCommand.cs
    DotnetRunCommand.cs
    DotnetEfCommand.cs
    DotnetFormatCommand.cs
    DotnetNugetCommand.cs
    GainCommand.cs
```

### Precise Implementation Signatures

#### `ProcessCommandRunner.cs`

```csharp
namespace DotnetTokenKiller.Infrastructure.Execution;

using System.Diagnostics;
using DotnetTokenKiller.Domain.Execution;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        // CRITICAL: Read both streams concurrently — sequential reads deadlock on large output
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdOutTask, stdErrTask);
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(stdOutTask.Result, stdErrTask.Result, process.ExitCode);
    }

    public async Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(command) { UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {command}");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
```

**Key points:**

- `UseShellExecute = false` required for both — enables `ArgumentList` and process control
- `ArgumentList.Add(arg)` (not `Arguments` string) — safe for args with spaces; no shell escaping needed
- In passthrough mode: do NOT redirect any streams — inherit parent console I/O directly
- `Process.Start` can return null — throw explicitly with descriptive message to satisfy nullable analysis

#### `NullTracker.cs`

```csharp
namespace DotnetTokenKiller.Infrastructure.Tracking;

using DotnetTokenKiller.Domain.Tracking;

public sealed class NullTracker : ITracker
{
    public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<GainSummary> GetSummaryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)
        => Task.FromResult(new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>()));

    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CommandRecord>>(Array.Empty<CommandRecord>());

    public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

#### `NullConfigProvider.cs`

```csharp
namespace DotnetTokenKiller.Infrastructure.Configuration;

using DotnetTokenKiller.Domain.Configuration;

public sealed class NullConfigProvider : IConfigProvider
{
    public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DtkConfig.Default);

    public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

#### `NullTeeService.cs`

```csharp
namespace DotnetTokenKiller.Infrastructure.Tee;

using DotnetTokenKiller.Domain.Tee;

public sealed class NullTeeService : ITeeService
{
    public Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
```

#### `DependencyInjection.cs`

```csharp
namespace DotnetTokenKiller.Infrastructure;

using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
        services.AddSingleton<ITracker, NullTracker>();
        services.AddSingleton<IConfigProvider, NullConfigProvider>();
        services.AddSingleton<ITeeService, NullTeeService>();
        return services;
    }
}
```

**Note:** Stories 5.1 (`SqliteTracker`), 6.1 (`JsonConfigProvider`), and 6.2 (`FileTeeService`) replace the three Null stubs by updating this method. Do NOT skip ahead to real implementations.

#### `TypeRegistrar.cs`

```csharp
namespace DotnetTokenKiller.Cli.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation)
        => services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation)
        => services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory)
        => services.AddSingleton(service, _ => factory());
}
```

#### `TypeResolver.cs`

```csharp
namespace DotnetTokenKiller.Cli.Infrastructure;

using Spectre.Console.Cli;

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type)
        => type is null ? null : provider.GetService(type);

    public void Dispose()
    {
        if (provider is IDisposable disposable)
            disposable.Dispose();
    }
}
```

#### `DotnetCommandSettings.cs`

```csharp
namespace DotnetTokenKiller.Cli.Commands.Settings;

using System.ComponentModel;
using Spectre.Console.Cli;

public class DotnetCommandSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    [Description("Increase verbosity (use -v for level 1, -v -v for level 2)")]
    public bool[] Verbose { get; init; } = [];
}
```

**Verbosity level = `Verbose.Length`** (0, 1, or 2). `FilteredRunUseCase` in Story 1.4 will consume this. Do NOT add `RemainingArgs` to settings — remaining dotnet args come from `context.Remaining.Raw`.

#### `GainCommandSettings.cs`

```csharp
namespace DotnetTokenKiller.Cli.Commands.Settings;

using System.ComponentModel;
using Spectre.Console.Cli;

public sealed class GainCommandSettings : CommandSettings
{
    [CommandOption("--days")]
    [Description("Number of days of history to include")]
    public int Days { get; init; } = 7;
}
```

**Must not be empty** — S2094 (SonarAnalyzer) fires on classes with no members. `Days` is the real option from Story 5.3.

#### Dotnet Subcommand Pattern (all 10 commands follow this)

```csharp
namespace DotnetTokenKiller.Cli.Commands;

using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Execution;
using Spectre.Console.Cli;

public sealed class DotnetBuildCommand(ICommandRunner commandRunner) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings)
    {
        var args = new[] { "build" }.Concat(context.Remaining.Raw).ToArray();
        return await commandRunner.RunPassthroughAsync("dotnet", args);
    }
}
```

**Repeat for all 10 subcommands** — change `"build"` to `"test"`, `"restore"`, `"publish"`, `"pack"`, `"clean"`, `"run"`, `"ef"`, `"format"`, `"nuget"`. Each file has one class, one namespace.

#### `GainCommand.cs`

```csharp
namespace DotnetTokenKiller.Cli.Commands;

using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

public sealed class GainCommand : AsyncCommand<GainCommandSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, GainCommandSettings settings)
        => Task.FromResult(0);
}
```

**Stub only** — Story 5.3 implements `GainReportUseCase` and rewrites this command.

#### `Program.cs` (full rewrite)

```csharp
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

var services = new ServiceCollection();
services.AddInfrastructure();

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("dtk");

    // Allow unknown dotnet flags to pass through to context.Remaining.Raw
    config.Settings.StrictParsing = false;

    config.AddBranch("dotnet", dotnet =>
    {
        dotnet.SetDescription("Run dotnet commands with filtered output");
        dotnet.AddCommand<DotnetBuildCommand>("build").WithDescription("Run dotnet build with filtered output");
        dotnet.AddCommand<DotnetTestCommand>("test").WithDescription("Run dotnet test with filtered output");
        dotnet.AddCommand<DotnetRestoreCommand>("restore").WithDescription("Run dotnet restore with filtered output");
        dotnet.AddCommand<DotnetPublishCommand>("publish").WithDescription("Run dotnet publish with filtered output");
        dotnet.AddCommand<DotnetPackCommand>("pack").WithDescription("Run dotnet pack with filtered output");
        dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
        dotnet.AddCommand<DotnetRunCommand>("run").WithDescription("Run dotnet run with filtered output");
        dotnet.AddCommand<DotnetEfCommand>("ef").WithDescription("Run dotnet ef with filtered output");
        dotnet.AddCommand<DotnetFormatCommand>("format").WithDescription("Run dotnet format with filtered output");
        dotnet.AddCommand<DotnetNugetCommand>("nuget").WithDescription("Run dotnet nuget with filtered output");
    });

    config.AddCommand<GainCommand>("gain").WithDescription("Show token savings analytics");
});

return await app.RunAsync(args);
```

**CRITICAL — `config.Settings.StrictParsing = false`**: Without this, Spectre.Console.Cli throws on unknown options like `--configuration`. This setting routes unrecognized args to `context.Remaining.Raw` so commands can forward them to `dotnet`. Verify with: `dotnet run --project src/DotnetTokenKiller.Cli -- dotnet build --configuration Release`.

### Architecture Compliance Rules (CRITICAL)

- **Infrastructure references Domain only** — `DependencyInjection.cs` + all implementations reference only Domain contracts and Microsoft.Extensions.DependencyInjection
- **Cli references Application + Infrastructure** — both project refs already exist in `DotnetTokenKiller.Cli.csproj`; do not add any cross-layer refs
- **Commands are thin adapters** — NO business logic in command classes; they only translate Spectre settings → use case/runner call → return exit code
- **`AsyncCommand<TSettings>`** — all commands inherit this, NOT `Command<TSettings>` (sync version)
- **Exit code always propagated** — never hardcode 0 in dotnet subcommands; return the value from `RunPassthroughAsync` directly
- **No `new` inside commands** — all dependencies via constructor injection through TypeRegistrar/TypeResolver

### Argument Forwarding Strategy

`context.Remaining.Raw` is the authoritative source for all dotnet args that DTK does not own. This works when `StrictParsing = false`. The pattern:

```csharp
var args = new[] { "build" }.Concat(context.Remaining.Raw).ToArray();
return await commandRunner.RunPassthroughAsync("dotnet", args);
```

This constructs: `dotnet build --configuration Release --no-restore ...` from `["build"] + ["--configuration", "Release", "--no-restore"]`.

**Do NOT** use `psi.Arguments` (string) — use `psi.ArgumentList.Add(arg)` in `ProcessCommandRunner` to safely handle args with spaces.

### Known Analyzer Pitfalls (from Story 1.2 + general)

- **S6966** — always await async methods; do NOT use `.Result` or `.GetAwaiter().GetResult()` on Tasks
- **CA1068** — `CancellationToken` must be the last parameter (all signatures above already comply)
- **RCS1124** — single-use local variables get inlined; e.g., if `args` is used only once, the compiler flags it — but since `args` is passed to a method, it won't trigger
- **CA1852** — types that are not inherited should be `sealed` — all command and implementation classes are `sealed`
- **S2094** — no empty classes; `GainCommandSettings` has `Days` property to satisfy this
- **IDE0290** — primary constructors preferred (both `TypeRegistrar(IServiceCollection services)` and `ProcessCommandRunner` above use them)
- **`using Xunit;`** — not needed here (no test files in this story), but remember for future stories
- **`using System.Diagnostics;`** — NOT covered by implicit usings; must be explicit in `ProcessCommandRunner.cs`

### What This Story Does NOT Implement (Scope Guard)

- **`FilteredRunUseCase`** — Story 1.4; commands use `RunPassthroughAsync` directly for now
- **`AnsiStrip`, `TokenEstimator`, `TextHelpers`** — Story 1.4
- **`DotnetBuildFilter`** — Story 1.5
- **`SqliteTracker`** — Story 5.1 (replaces `NullTracker`)
- **`JsonConfigProvider`** — Story 6.1 (replaces `NullConfigProvider`)
- **`FileTeeService`** — Story 6.2 (replaces `NullTeeService`)
- **`GainReportUseCase`** — Story 5.3 (rewrites `GainCommand`)
- **GitHub Actions CI** — Story 1.6
- **`PassthroughRunUseCase`** — Story 1.4 (fallback for unrecognized subcommands; NOT needed yet)
- **Infrastructure.Tests** — no tests added in this story; `ProcessCommandRunner` is tested implicitly via smoke test

### Project Structure Notes

- Namespace must match folder path exactly: `src/DotnetTokenKiller.Cli/Commands/Settings/DotnetCommandSettings.cs` → `namespace DotnetTokenKiller.Cli.Commands.Settings;`
- `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` → `namespace DotnetTokenKiller.Infrastructure;`
- `src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs` → `namespace DotnetTokenKiller.Cli.Infrastructure;`
- File-scoped namespaces everywhere (`;` not `{ }`)
- LF line endings, no trailing whitespace, 4-space indent

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.3]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4 (next — FilteredRunUseCase)]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/planning-artifacts/Architecture.md#4. Layer Responsibilities]
- [Source: _bmad-output/planning-artifacts/Architecture.md#6. Core Data Flow — FilteredRunUseCase]
- [Source: _bmad-output/planning-artifacts/Architecture.md#8. Infrastructure Details — Process Execution]
- [Source: _bmad-output/planning-artifacts/Architecture.md#9. DI Registration]
- [Source: _bmad-output/project-context.md#Framework-Specific Rules — Spectre.Console.Cli]
- [Source: _bmad-output/project-context.md#Clean Architecture — Dependency Rules]
- [Source: _bmad-output/project-context.md#Critical Don't-Miss Rules]
- [Source: _bmad-output/implementation-artifacts/1-2-implement-core-domain-contracts-and-value-objects.md#Dev Agent Record]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- CA1849/VSTHRD103: `.Result` on completed tasks after `Task.WhenAll` — fixed by using `await stdOutTask` / `await stdErrTask` instead
- CA1819: `bool[]` property in `DotnetCommandSettings` — suppressed in `.editorconfig` (required by Spectre.Console.Cli multi-occurrence flag API)
- CA1515: Public types in executable project — suppressed in `.editorconfig` (Spectre.Console.Cli requires public settings types for reflection-based option parsing)
- `TypeRegistrar` naming conflict: Our class conflicts with Spectre.Console.Cli's internal `TypeRegistrar` — resolved using `using` alias `DtkTypeRegistrar` in `Program.cs`
- `AsyncCommand<TSettings>.ExecuteAsync` in Spectre.Console.Cli 0.53.1 requires third `CancellationToken` parameter (not documented in older examples)
- Positional args (e.g. `DotnetTokenKiller.slnx`) not captured by `context.Remaining.Raw` — Spectre treats them as sub-commands; fixed by adding `[CommandArgument(0, "[args]")] string[] PositionalArgs` to `DotnetCommandSettings`
- Import ordering: blank lines between `using` groups violate `dotnet_separate_import_directive_groups = false` — auto-fixed by `dotnet format`

### Completion Notes List

- Added `Microsoft.Extensions.DependencyInjection 9.0.0` to `Directory.Packages.props` and both `Cli` and `Infrastructure` `.csproj` files
- Implemented `ProcessCommandRunner` with concurrent stdout/stderr reading via `Task.WhenAll` to prevent deadlock on large output
- Created `NullTracker`, `NullConfigProvider`, `NullTeeService` stubs in Infrastructure (will be replaced in Stories 5.1, 6.1, 6.2)
- Created `DependencyInjection.cs` static extension class in Infrastructure registering all four Domain interfaces as singletons
- Created `TypeRegistrar` / `TypeResolver` in `DotnetTokenKiller.Cli.Infrastructure` bridging Spectre.Console.Cli with MSDI
- Created `DotnetCommandSettings` with `bool[] Verbose` (verbosity counting) and `string[] PositionalArgs` (positional arg forwarding)
- Created `GainCommandSettings` with `int Days` property (non-empty to avoid S2094)
- Created all 10 dotnet subcommand stubs and `GainCommand` stub using `AsyncCommand<TSettings>` pattern
- Rewrote `Program.cs` — ServiceCollection, AddInfrastructure, CommandApp with TypeRegistrar, all branches registered, `StrictParsing = false`
- `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
- `dotnet test DotnetTokenKiller.slnx` → 17/17 tests pass (no regressions)
- `dotnet format --verify-no-changes` → clean
- Smoke tests: `dtk --help`, `dtk dotnet --help` (all 10 subcommands listed), `dtk dotnet build DotnetTokenKiller.slnx --no-restore` (build succeeded with forwarded args)

### File List

- Directory.Packages.props (modified — added Microsoft.Extensions.DependencyInjection 9.0.0)
- .editorconfig (modified — suppressed CA1515, CA1819)
- src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj (modified — added Microsoft.Extensions.DependencyInjection reference)
- src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj (modified — added Microsoft.Extensions.DependencyInjection reference)
- src/DotnetTokenKiller.Cli/Program.cs (rewritten)
- src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs (created)
- src/DotnetTokenKiller.Cli/Infrastructure/TypeResolver.cs (created)
- src/DotnetTokenKiller.Cli/Commands/Settings/DotnetCommandSettings.cs (created)
- src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetRunCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs (created)
- src/DotnetTokenKiller.Cli/Commands/GainCommand.cs (created)
- src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs (created)
- src/DotnetTokenKiller.Infrastructure/Tracking/NullTracker.cs (created)
- src/DotnetTokenKiller.Infrastructure/Configuration/NullConfigProvider.cs (created)
- src/DotnetTokenKiller.Infrastructure/Tee/NullTeeService.cs (created)
- src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs (created)

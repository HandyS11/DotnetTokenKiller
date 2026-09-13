**Status:** Design approved 2026-09-13; implementation plan to follow

## Context

This is the third of four sub-projects on the startup-speed effort (roadmap and scoping
measurements in [the measurement spec](2026-09-12-startup-speed-measurement-design.md)):

1. **Measurement:** done in PR #145.
2. **Tracking path:** done in PR #146 ([spec](2026-09-13-tracking-path-design.md)).
3. **Native AOT distribution** (this spec).
4. **Tokenizer spooling:** deferred until AOT shows what remains.

After sub-project 2, `cold-start` medians for the JIT build (Ryzen 5 3600, Linux) are: pipe
230.2 ms; wrapped `dtk dotnet build` overhead 228.2 ms with an instant fake child and 185.5 ms with
a child that sleeps 1000 ms.

### Probe results (2026-09-13, throwaway copy of `develop` at 1541465, SDK 10.0.400)

**Runtime.** A plain `PublishAot=true` build compiles but fails at startup with "Could not get
settings type for command of type 'DotnetTokenKiller.Cli.Commands.DotnetBuildCommand'". Adding
`<TrimmerRootAssembly Include="dtk"/>` and `<TrimmerRootAssembly Include="Spectre.Console.Cli"/>`
fixes it.

**Behaviour.** A 49-step script ran every command family under the JIT build and the rooted AOT
build, each in its own hermetic state, and diffed the results. The steps covered:

- `--version` and `--help`
- `pipe` over all six filters' fixtures, including `--vv --show-log`
- `gain` with `--json`, `--coverage`, `--export csv`, `--days` and `--command`
- `log`, `config show/set` (including an invalid value) and `doctor`
- all four completions
- all eight `integrate` providers into a project, twice for `claude`, plus `--global` for `claude`
  and `copilot-cli` with an rtk hook and rtk TOML config present
- passthrough and an unknown command
- wrapped `dotnet restore/build/list package/clean` against a sample project, one of them broken
- `reset --force --all`

Output, exit codes, tracking rows (identical token counts) and every file written were identical,
once paths and timings were normalized. The one other difference seen, in a wrapped `restore`, was
environmental: a solution build run in between had changed the sample's restore state. It did not
reproduce when the two runs went back to back. The recorded elapsed time of a pipe run drops from
about 45 ms to about 5 ms.

**Timings** (median of 21 fresh processes, hermetic state on tmpfs, low load; indicative only, not
`cold-start` figures):

| Build | `--version` | pipe, tracking on | pipe, tracking off |
|---|---:|---:|---:|
| JIT (`develop`) | 113.6 ms | 269.7 ms | 206.8 ms |
| Native AOT (rooted) | 12.3 ms | 64.5 ms | 14.0 ms |

Under AOT, tracking (tokenizer load, counting and SQLite) is about 50 of the 65 ms. The binary is
16 MB, with a separate 34 MB `dtk.dbg`. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` saves about
2 ms.

**Native SQLite.** The AOT publish output carries `libe_sqlite3.so`, which is loaded dynamically:
`LD_DEBUG=files` shows no `e_sqlite3` lines with tracking off and 7 with it on. The tracking-off
regression probe from sub-project 2 therefore keeps working.

**Trim and AOT warnings** from the rooted publish:

| Code | Site | Cause |
|---|---|---|
| IL2026, IL3050 | `CopilotCliIntegrator.BuildHookJson` | `JsonArray.Add<JsonObject>` via a collection initializer |
| IL2026, IL3050 | `IntegratorHelpers.WriteHookAndSettingsAsync` | same |
| IL2026, IL3050 | `IntegratorHelpers.MergeJsonSettingsAsync` | `hookArray.Add(hookEntry)` binds to `Add<JsonObject>` |
| IL2026, IL3050 | `RtkHookCoexistence.ReconcileRtkConfigAsync`, `ConfigTextExcludesDotnet` | reflection-based `TomlSerializer.TryDeserialize<TomlTable>` |
| IL2067 | `TypeRegistrar.Register` | `services.AddSingleton(Type, Type)` |
| IL3050 | `Program.cs`, `new CommandApp(registrar)` | Spectre.Console.Cli marks the constructor `[RequiresDynamicCode]` |
| IL2104, IL3053, IL3000 | inside `Spectre.Console.Cli.dll` | the library's own reflection, and `Assembly.Location` in `CommandModel.GetApplicationFile` |

A second probe applied the fixes described below, together with `PublishAot=true` in the CLI csproj
and `IsAotCompatible=true` on the three libraries:

- The whole solution builds under JIT with `TreatWarningsAsErrors` and no errors.
- An AOT publish reports exactly IL2104, IL3053 and IL3000, all from `Spectre.Console.Cli`.
- The 49-step comparison stays identical.

### Spectre.Console.Cli and AOT

Spectre.Console.Cli 0.55.0 is the latest stable release. Its maintainers annotate `CommandApp` with
`[RequiresDynamicCode("Spectre.Console.Cli relies on reflection. Use during trimming and AOT
compilation is not supported and may result in unexpected behaviors.")]`, and the upstream AOT issue
(spectreconsole/spectre.console.cli#6) closed without a plan.

Its source uses dynamic code in these places:

- `MakeGenericType` / `MakeGenericMethod` over command and settings types for branches
- `MultiMap<,>` for dictionary options
- `Array.CreateInstance` for array options
- `TypeDescriptor.GetConverter` for option values
- `Activator.CreateInstance` for settings and for types the resolver returns null for

dtk's settings properties are only `bool`, `int`, `string` and `string[]`. It has no dictionary
options, value-type arrays, custom converters or pair deconstructors. Every generic instantiation it
triggers is over reference types, which Native AOT shares.

## Problem

dtk pays 110–270 ms of JIT start-up on every invocation, before and around the work it does. Native
AOT removes most of it, but three things stand in the way:

- dtk ships as a single framework-dependent .NET tool package.
- Its code has trim- and AOT-unsafe calls that nothing checks.
- Its CLI framework declares AOT unsupported.

## Decisions

- **Spectre.Console.Cli stays, under one contained exception.** Replacing it with an AOT-safe parser
  (System.CommandLine, ConsoleAppFramework) would rewrite every command, settings class, help text,
  completion script and generated doc page, more work than the rest of this sub-project. Staying on
  ReadyToRun instead (~219 ms against ~65 ms) forgoes most of the gain. The exception is limited to
  one call site and one assembly's publish warnings, and it is guarded by tests (see 1.3).
- **AOT RIDs:** `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`. Everything else gets the
  framework-dependent `any` fallback. Rule: a RID ships only if CI runs its binary natively on every
  PR.
- **AOT coverage in CI:** the existing process-spawning integration suite runs against the AOT binary
  on linux-x64 and win-x64, and a new JIT-vs-AOT parity test class runs on all four RIDs.
- **Symbols:** the `any` package keeps its `.snupkg`. The AOT RID packages carry no symbols. Each
  RID's native symbols are attached to the GitHub Release.

## Solution

### 1. Trim and AOT safety

#### 1.1 Build properties

- **`src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`:**
  - `<PublishAot>true</PublishAot>`. Besides enabling AOT publish, it turns on the AOT, trim and
    single-file Roslyn analyzers in every normal build.
  - `<TrimmerRootAssembly Include="dtk"/>` and `<TrimmerRootAssembly Include="Spectre.Console.Cli"/>`,
    with a comment explaining why (Spectre binds settings and commands by reflection).
  - `ToolPackageRuntimeIdentifiers` (see 2).
  - `<TieredPGO>false</TieredPGO>` stays: it has no effect under AOT, but it still applies to the
    `any` fallback.
- **Domain, Application, Infrastructure csproj files:** `<IsAotCompatible>true</IsAotCompatible>`.
  With `TreatWarningsAsErrors`, a trim- or AOT-unsafe call in any layer fails the normal JIT build,
  locally and in CI.

#### 1.2 Fixes

- **`JsonArray`:** in `CopilotCliIntegrator.BuildHookJson` and
  `IntegratorHelpers.WriteHookAndSettingsAsync`, build the array with the
  `JsonArray(params JsonNode?[])` constructor instead of a collection initializer. In
  `IntegratorHelpers.MergeJsonSettingsAsync`, call `hookArray.Add((JsonNode)hookEntry)` so the
  non-generic overload binds. The JSON produced is unchanged.
- **Tomlyn:** a source-generated context in the Application layer,

  ```csharp
  [TomlSerializable(typeof(TomlTable))]
  internal sealed partial class RtkTomlContext : TomlSerializerContext;
  ```

  Both `RtkHookCoexistence` call sites use
  `TomlSerializer.TryDeserialize(text, RtkTomlContext.Default, out TomlTable? model)`. Parsing
  behaviour is unchanged.
- **`TypeRegistrar.Register`:**
  - It stops calling `services.AddSingleton(Type, Type)`, and so registers nothing itself.
  - Every command class is registered once in DI with a generic `AddSingleton<TCommand>()`, from a
    single method beside `CliConfigurator` that lists all fifteen commands.
  - Types Spectre registers that are not in DI (settings classes, `DefaultPairDeconstructor`) fall
    back to Spectre's own `Activator.CreateInstance`, which the rooting covers; they are all
    parameterless.
  - `RegisterInstance` and `RegisterLazy` are unchanged, since neither uses reflection.
  - The XML doc on `Register` says why it no longer registers.

#### 1.3 The contained exception

- **The suppression.** `new CommandApp(registrar)` moves into a single factory method
  `SpectreCommandApp.Create(ITypeRegistrar registrar)` in `DotnetTokenKiller.Cli.Infrastructure`. It
  carries the repository's only trim or AOT suppression:
  `[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = …)]`. The justification
  names what makes it safe: the two rooted assemblies, the settings-type guard and the parity tests.
- **Settings-type guard.** A test reflects over every `CommandSettings` subclass in the `dtk`
  assembly. It fails if any property is not `bool`, `int`, `string` or `string[]`, or if a settings
  class or property carries `[TypeConverter]` or `[PairDeconstructor]`. Those are the Spectre paths
  proven under AOT; adding another means extending the parity tests first.
- **DI guard.** A test builds the real `CommandApp` configuration and checks that every command type
  in the configured tree resolves from the service collection. A command added to `CliConfigurator`
  without a registration fails here, not at a user's prompt.
- **Publish warnings.**
  - The AOT publish emits IL2104, IL3053 and IL3000, all from `Spectre.Console.Cli`. With
    `TreatWarningsAsErrors` they fail the publish.
  - The CLI project keeps exactly those three codes as warnings (not errors) for the publish step
    only. Normal builds keep treating IL3000 in dtk's own code as an error, through the single-file
    analyzer.
  - The plan settles the MSBuild mechanism with a probe: `WarningsNotAsErrors` scoped to publish, or
    the ILCompiler's own switch. The chosen mechanism must leave any other IL warning an error.
  - A CI script (`eng/check-aot-warnings.ps1`) reads the pack log and fails unless the IL warnings
    are exactly those three codes, each attributed to `Spectre.Console.Cli`.

### 2. Packaging

The .NET 10 SDK's RID-specific tool packages
(https://learn.microsoft.com/en-us/dotnet/core/tools/rid-specific-tools), in the "hybrid" form: AOT
RID packages plus a framework-dependent `any` fallback. The CLI csproj sets
`<ToolPackageRuntimeIdentifiers>linux-x64;linux-arm64;osx-arm64;win-x64;any</ToolPackageRuntimeIdentifiers>`.
It lives in the csproj because passing a list with `-p:` on the command line collapses it into a
single RID.

Every release produces six packages with the same version:

| Package | Command | Contents |
|---|---|---|
| `DotnetTokenKiller` (pointer, type `DotnetTool`) | `dotnet pack -c Release -p:IncludeSymbols=false`, on any OS | `tools/any/any/DotnetToolSettings.xml` listing the five RID packages, README, icon |
| `DotnetTokenKiller.{linux-x64,linux-arm64,osx-arm64,win-x64}` (type `DotnetToolRidPackage`) | `dotnet pack -c Release -r <rid> -p:IncludeSymbols=false`, on that OS | the native `dtk` executable and `libe_sqlite3`, nothing else |
| `DotnetTokenKiller.any` (type `DotnetToolRidPackage`) | `dotnet pack -c Release -r any -p:PublishAot=false` | today's framework-dependent tool (`dtk.dll`, `.deps.json`, `.runtimeconfig.json`, apphost), plus `.snupkg` |

Facts this relies on (verified by pack-and-install experiments on SDK 10.0.400):

- **Symbols.** `IncludeSymbols=true` with `SymbolPackageFormat=snupkg`, which the csproj sets today,
  makes the pointer and AOT RID packs fail with NU5017. Hence `-p:IncludeSymbols=false` on those
  packs; the `any` pack keeps symbols.
- **What a RID pack carries by default.** It includes the stripped-symbols file (`.dbg`). The
  publish output also contains `.pdb`, `.xml` documentation and `libe_sqlite3.a`. The plan settles by
  probe how to exclude them all from the RID packages.
- **Installing.** `dotnet tool install -g DotnetTokenKiller` on a listed RID installs the pointer and
  that RID's package. The shim is a symlink to the native binary on Unix, and no .NET runtime is
  needed to run it.
- **Unlisted RIDs** install `any`, which needs the .NET 10 runtime, as today.
- **Updating and `dnx`.** `dotnet tool update` from a framework-dependent install to the hybrid
  package works, and `dnx` / `dotnet tool exec` pick the RID package the same way.
- **Missing RID package.** If the package for the machine's RID is absent at the pointer's version,
  install fails, so the push order in 4 matters.
- **SDK 8 and 9** cannot install the hybrid package ("Format version is higher than supported").
  They already cannot install today's `net10.0` package, so no user loses anything.

Native symbols (`.dbg` on Linux, `.pdb` on Windows, `.dSYM` on macOS) are zipped per RID as
`dtk-<version>-<rid>-symbols.zip` and attached to the GitHub Release.

A plain `dotnet pack` now builds only the pointer. CLAUDE.md documents the full sequence.

### 3. CI and tests

#### 3.1 Reusable workflow `.github/workflows/aot-package.yml`

A `workflow_call` workflow with inputs `rid`, `runner` and `version`. Steps:

1. Checkout; `actions/setup-dotnet` from `global.json`; install the native AOT prerequisites the
   runner lacks. The plan checks each image: Linux needs `clang` and `zlib1g-dev`; Windows needs the
   MSVC toolset, present on `windows-latest`; macOS needs Xcode command-line tools, present on
   `macos-latest`.
2. `dotnet build -c Release` of the solution: the parity tests need the JIT `dtk.dll`.
3. `dotnet pack -c Release -r <rid> -p:IncludeSymbols=false -p:Version=<version>` and the pointer
   pack, both into a local feed directory, with the RID pack's output captured to a log.
4. `pwsh eng/check-aot-warnings.ps1 <log>` (see 1.3).
5. `dotnet tool install --tool-path <tmp>/tools DotnetTokenKiller --version <version>` against a
   `nuget.config` whose only source is the local feed. The step asserts that the tool store
   contains `dotnettokenkiller.<rid>`, so a silent fallback to `any` fails the job.
6. The parity tests, with `DTK_AOT_BINARY` set to the installed `dtk`.
7. On linux-x64 and win-x64 only, the whole `DotnetTokenKiller.Cli.IntegrationTests` project, with
   `DTK_TEST_BINARY` set to the installed `dtk`.
8. Upload the RID `.nupkg` and the native-symbols zip as artifacts.

#### 3.2 `ci.yml`

The existing `build` job is unchanged. Two jobs are added:

- **`aot`:** a matrix calling `aot-package.yml` for linux-x64 on `ubuntu-latest`, linux-arm64 on
  `ubuntu-24.04-arm`, osx-arm64 on `macos-latest` and win-x64 on `windows-latest`, with
  `fail-fast: false`.
- **`fallback`** (ubuntu-latest):
  - packs `-r any -p:PublishAot=false`, plus a pointer packed with
    `-p:ToolPackageRuntimeIdentifiers=any` (a single value, so the command-line limitation does not
    apply)
  - installs it from a local feed and asserts the store contains `dotnettokenkiller.any`
  - runs the parity tests against the installed shim

#### 3.3 Test changes (`tests/DotnetTokenKiller.Cli.IntegrationTests`)

- **`IntegrationTestHelper`:** when `DTK_TEST_BINARY` is set and not blank, every dtk invocation
  starts that executable directly instead of `dotnet dtk.dll`. `StartDetached` follows the same
  rule. Nothing else about the spawned process changes (environment, isolation, timeouts).
- **`AotParityTests`:**
  - Every test is skipped unless `DTK_AOT_BINARY` is set, through a `Fact` attribute subclass that
    sets `Skip` when the variable is absent. Local `dotnet test` runs and the `build` job are
    unaffected.
  - Each case runs the same deterministic input through `dotnet dtk.dll` and through
    `DTK_AOT_BINARY`, each with its own hermetic `DTK_CONFIG_PATH`, `DTK_DB_PATH`, `DTK_TEE_DIR`,
    `HOME`/`USERPROFILE` and `XDG_CONFIG_HOME`.
  - It asserts equal exit codes; equal stdout and stderr after normalizing the hermetic root,
    timestamps, durations and tee-log file names; equal tracking rows, with token counts exact and
    elapsed time ignored; and byte-identical files written under the hermetic root.
  - Cases:
    - `--version` and `--help`
    - `pipe` over one fixture per filter (build, test, restore, clean, format, list package), plus
      `--vv --show-log`
    - `gain`, `gain --json`, `gain --coverage`, `gain --export csv`, `gain --days 7 --command build`
    - `log --list --all` and `log --all --lines 5`
    - `config show`, `config set` with a valid and an invalid value
    - `doctor`
    - `completion` for bash, zsh, fish and powershell
    - `integrate` for all eight providers into a project directory, a repeated `claude`, and
      `--global` for `claude` and `copilot-cli` with an rtk hook and an rtk TOML config present
    - passthrough and an unknown command
    - one wrapped command per filter against a fake `dotnet` that prints a fixture and exits with a
      set code
    - `reset --force --all`
  - **The fake `dotnet`.** On Linux and macOS it is a generated POSIX shell script on the child's
    `PATH`, like `cold-start`'s. `Process.Start` on Windows does not run a `.cmd` found on `PATH`
    as `dotnet`. The plan settles by probe whether a Windows substitute exists without new build
    artifacts; if none does, the wrapped cases are skipped on Windows. The win-x64 integration-suite
    run in 3.1 covers wrapped commands there against real `dotnet`.
  - Real builds stay out of parity tests: their output depends on restore and build state, as the
    probe showed.
- **`CommandSettingsAotGuardTests`:** the settings-type guard from 1.3. Always on.
- **`CommandRegistrationTests`:** the DI guard from 1.3. Always on.

### 4. Release workflow (`publish.yml`, `v*` tags)

The version comes from the tag and is passed as `-p:Version=<version>` to every build and pack. It
replaces the `sed` edit of `Directory.Build.props`, whose comment is updated to match.

Jobs:

1. **`test`** (ubuntu-latest): restore, build, test, as today.
2. **`pack-rid`:** the same four-RID matrix as CI, calling `aot-package.yml` with the tag version.
3. **`pack-any`** (ubuntu-latest):
   - the `any` pack (`.nupkg` + `.snupkg`) and the real pointer pack, which lists all five RIDs,
     uploaded as artifacts
   - before uploading, a separate check-only pointer packed with
     `-p:ToolPackageRuntimeIdentifiers=any` into another local feed is installed with the `any`
     package, as in the CI `fallback` job, and the parity tests run against it. The check-only
     pointer is never uploaded.
4. **`publish`** (ubuntu-latest, needs all three):
   - downloads every artifact and logs in with `NuGet/login` (trusted publishing, as today)
   - pushes to nuget.org in two passes, with `--skip-duplicate`: first the four RID packages and
     `DotnetTokenKiller.any` (its `.snupkg` alongside), **then** the pointer
   - pushes to GitHub Packages in the same two passes
   - creates the GitHub Release with all six `.nupkg` files, the `.snupkg` and the four native
     symbol zips, with generated release notes as today

Failure semantics:

- **A pack or check fails:** nothing is pushed.
- **A push fails part-way:** the pointer has not been pushed, so installs keep resolving the
  previous version, whose packages all exist. Re-running the job is safe because of
  `--skip-duplicate`.

### 5. Docs

- **CLAUDE.md:**
  - AOT `cold-start`, `--version` and tracking-off figures beside the JIT ones
  - the pack sequence, and that a plain `dotnet pack` builds only the pointer
  - the AOT publish command for local use
  - `DTK_TEST_BINARY` and `DTK_AOT_BINARY`, and how to run the parity tests locally against a
    linux-x64 publish
  - the contained Spectre exception and its guards, so nobody "fixes" the suppression or adds a
    dictionary option without extending the parity tests
- **`README.md`, `src/DotnetTokenKiller.Cli/README.md` (the package readme),
  `docfx/articles/getting-started.md`:** which platforms get a native binary that needs no .NET
  runtime, and that everything else runs on the .NET 10 runtime. The install command is unchanged.

## Release prerequisites

Not code, but required before the first hybrid release:

- **ID-prefix reservation.** Ask nuget.org to reserve the `DotnetTokenKiller` ID prefix. It is not
  reserved today (the package shows as not verified), so anyone could register
  `DotnetTokenKiller.linux-x64` first, blocking the push and serving their package to that RID.
- **Trusted publishing.** Confirm the policy lets the workflow create the five new package IDs.
- **Dry run.** Push a pre-release tag (for example `v0.8.0-rc.1`) to exercise the whole release
  workflow, then install it with `dotnet tool install -g DotnetTokenKiller --prerelease` on at
  least one AOT RID.

## Measurement protocol

Each step is a full `cold-start` run on the measuring machine, checked against its `Built:` line,
plus a one-off script run: 21 fresh processes each of `--version` and of tracking-off
`pipe build --exit-code 1` over `dotnet_build_errors.txt`, medians, hermetic state. Each scenario is
compared only with its own baseline.

1. **Baseline, before any change under `src/`:** `dotnet build src/DotnetTokenKiller.Cli -c Release`
   on `perf/native-aot` at the spec commit.
2. **Final JIT:** the same build after all code changes. This is what the `any` fallback runs.
3. **Final AOT:** the linux-x64 RID package packed and installed from a local feed. `cold-start`
   points at the installed binary, which on Linux is what the shim links to.

Report the tracking share under AOT (tracking on minus tracking off) as input to sub-project 4.
Also report whether the pipe and instant-child figures still leave enough dtk start-up to reconsider
starting setup from `Program.cs`. Report only; do not implement.

## Success criteria

- **Timings:**
  - The AOT pipe median and both wrapped-overhead medians are far below their JIT figures. The probe
    suggests roughly 270 → 65 ms for pipe, but the criterion is the relationship, not that estimate.
  - The final JIT figures are within 5 ms of the baseline on every scenario.
- **Correctness:**
  - `SavingsBaselineTests` pass with `savings-baseline.json` unchanged.
  - The parity tests pass on all four RIDs and in the fallback job, which includes identical token
    counts.
  - The integration suite passes against the installed AOT binary on linux-x64 and win-x64.
  - With tracking disabled, `LD_DEBUG=files` on the AOT binary shows no `e_sqlite3`.
- **Code:**
  - The normal JIT build is clean with `IsAotCompatible` on all three libraries.
  - The repository contains exactly one trim or AOT suppression (1.3).

## Testing

Test-first where the change is code:

- **`CommandSettingsAotGuardTests`:** passes on the current settings. A settings class with a
  `Dictionary<string, int>` property, a `[TypeConverter]` or an `int[]` fails it. Checked with
  temporary test-local types, not by changing real settings.
- **`CommandRegistrationTests`:** passes with the new registration list. Removing one command from
  it fails the test.
- **`RtkHookCoexistence`:** the existing tests pass unchanged with the source-generated context.
  They already cover a config without `dotnet`, with it, and invalid TOML.
- **Integrators:** the existing `IntegratorHelpers` and `CopilotCliIntegrator` tests pass unchanged,
  so the JSON output is unchanged.
- **`IntegrationTestHelper`:** with `DTK_TEST_BINARY` unset, behaviour is unchanged (the existing
  suite). A test in the parity class, running only when `DTK_AOT_BINARY` is set, proves the helper
  honours the variable by running `--version` through both paths.
- **Local verification:**
  - The layer test projects run per project, as usual.
  - The parity tests and the integration suite run locally against a linux-x64 publish on the
    development machine.
  - The build-spawning integration classes cannot run on the development machine; CI gates them,
    which the PR states.

## Out of scope

- Tokenizer spooling or approximate token counts (sub-project 4).
- Filter changes.
- Starting tracking setup from `Program.cs` (see Measurement protocol: report only).
- The SQLite statement, retention and WAL ideas rejected in the sub-project 2 spec.
- `InvariantGlobalization`: about 2 ms, and it changes culture-sensitive formatting.
- Replacing Spectre.Console.Cli.
- More RIDs (`osx-x64`, `win-arm64`, `linux-musl-x64`). Each is later one
  `ToolPackageRuntimeIdentifiers` entry and one matrix row, under the same native-runner rule.
- Statically linking `e_sqlite3` into the binary. It would make the tracking-off loader probe
  meaningless.
- **The rewrite hook's double-prefix bug** in `.claude/hooks/dotnet-to-dtk.py` and
  `HookScriptTemplates.cs`. It is unrelated, has been reported, and gets its own change.

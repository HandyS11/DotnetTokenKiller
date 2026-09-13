**Status:** Implemented — see [the plan](../plans/2026-09-13-native-aot.md). Amended while planning;
see "Amendments" at the end.

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
  two call sites at the Spectre.Console.Cli boundary and one assembly's publish warnings, and it is
  guarded by tests (see 1.3).
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
- **`TypeRegistrar.Register` keeps `services.AddSingleton(service, implementation)`.** Registering
  nothing there instead (generic registrations for dtk's commands, Spectre's activator for the rest)
  was probed and rejected. Spectre.Console.Cli's hidden built-in commands (`dtk cli version`,
  `dtk cli explain`, `dtk cli opencli`, and the `--help-dump-opencli` option) take Spectre-internal
  services in their constructors, and only this reflective call can register them. Without it they
  fail with "Could not resolve type" in the JIT build as well as the AOT build. With it, the rooted
  AOT build runs them correctly. The IL2067 it raises is part of the contained exception below.

#### 1.3 The contained exception

- **The two suppressions**, both in `DotnetTokenKiller.Cli.Infrastructure`, at the
  Spectre.Console.Cli boundary. They are the repository's only trim or AOT suppressions:
  - `new CommandApp(registrar)` moves into a single factory method
    `SpectreCommandApp.Create(ITypeRegistrar registrar)`, which carries
    `[UnconditionalSuppressMessage("AotAnalysis", "IL3050:…")]`.
  - `TypeRegistrar.Register` carries `[UnconditionalSuppressMessage("Trimming", "IL2067:…")]`.
  - Each justification names what makes it safe: the two rooted assemblies, the settings-type guard,
    the parity tests and the built-in command tests.
- **Settings-type guard.** A test reflects over every `CommandSettings` subclass in the `dtk`
  assembly. It fails if any option or argument property is not `bool`, `int`, `string` or
  `string[]`, or if a settings class or property carries `[TypeConverter]` or `[PairDeconstructor]`.
  Those are the Spectre paths proven under AOT; adding another means extending the parity tests
  first.
- **Built-in command tests.** An integration test runs `cli version`, `cli explain`, `cli opencli`
  and `--help-dump-opencli` and requires exit code 0 and their expected output. Parity tests cannot
  catch a break that hits both builds alike; this can. It runs against the AOT binary wherever the
  integration suite does (see 3.1).
- **Publish warnings.**
  - The AOT publish emits IL2104, IL3053 and IL3000, all from `Spectre.Console.Cli`. With
    `TreatWarningsAsErrors` they fail the publish.
  - The CLI csproj adds those three codes to `WarningsNotAsErrors` inside a target that runs
    `BeforeTargets="WriteIlcRspFileForCompilation"`. The ILCompiler passes `WarningsNotAsErrors` as
    `--nowarnaserr` while still passing `--warnaserror`, and the Roslyn compile has already finished
    by then. So only the native compile is affected, any other IL warning stays an error, and IL3000
    in dtk's own code stays an error in every build through the single-file analyzer. Probed: the
    RID pack succeeds with exactly the three warnings.
  - `AotWarningLogTests` in the integration test project holds a parser for MSBuild logs, with unit
    tests. A test gated on `DTK_AOT_PACK_LOG` reads the RID pack's log and fails unless its IL
    diagnostics are exactly those three codes, as warnings, each originating in
    `Spectre.Console.Cli`. It also fails if any of the three is missing, because then the native
    compile did not run. A C# test replaces the PowerShell script first planned: it runs on all four
    runners and on the development machine, which has no `pwsh`.

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

- **Symbols.** `IncludeSymbols=true` with `SymbolPackageFormat=snupkg`, which the csproj set before
  this work, makes the pointer and AOT RID packs fail with NU5017. The csproj now sets
  `IncludeSymbols` only when `RuntimeIdentifier` is `any`, so the `any` pack keeps its `.snupkg` and
  the other packs need no flag; the workflows still pass `-p:IncludeSymbols=false` explicitly.
- **What a RID pack carries by default.** Tool packing globs the whole publish directory, so a
  default RID package (56 MB unpacked) holds the stripped-symbols file (`.dbg`), every `.pdb`, the
  `.xml` documentation and `libe_sqlite3.a`. For AOT RID-specific builds only, the CLI csproj sets
  `CopyOutputSymbolsToPublishDirectory=false`. A target `AfterTargets="ComputeResolvedFilesToPublishList"`
  removes `.pdb`, `.xml` and `.a` items from `ResolvedFileToPublish`. Probed: the linux-x64 package
  then holds exactly `dtk`, `libe_sqlite3.so` and the tool settings file (9.5 MB), and the `any`
  package is unchanged (42.5 MB, against 40.6 MB for the published 0.7.2).
- **Installing.** `dotnet tool install -g DotnetTokenKiller` on a listed RID installs the pointer and
  that RID's package. The shim is a symlink to the native binary on Unix, and no .NET runtime is
  needed to run it.
- **Unlisted RIDs** install `any`, which needs the .NET 10 runtime, as today — except musl: the
  SDK's RID graph maps `linux-musl-x64`/`linux-musl-arm64` to `linux-x64`/`linux-arm64`, so Alpine
  machines would install the glibc AOT package, which cannot run there (open issue, see Amendments).
- **Updating and `dnx`.** `dotnet tool update` from a framework-dependent install to the hybrid
  package works, and `dnx` / `dotnet tool exec` pick the RID package the same way.
- **Missing RID package.** If the package for the machine's RID is absent at the pointer's version,
  install fails, so the push order in 4 matters.
- **SDK 8 and 9** cannot install the hybrid package ("Format version is higher than supported").
  They already cannot install today's `net10.0` package, so no user loses anything.

Native symbols (`.dbg` on Linux, `.pdb` on Windows, `.dSYM` on macOS) stay in
`src/DotnetTokenKiller.Cli/bin/Release/net10.0/<rid>/native/`. They are zipped per RID as
`dtk-<version>-<rid>-symbols.zip` and attached to the GitHub Release.

A plain `dotnet pack` now builds only the pointer. CLAUDE.md documents the full sequence.

### 3. CI and tests

#### 3.1 Reusable workflow `.github/workflows/aot-package.yml`

A `workflow_call` workflow with inputs `rid`, `runner` and `version`. Steps:

1. Checkout; `actions/setup-dotnet` from `global.json`; install the native AOT prerequisites the
   runner lacks. The plan checks each image: Linux needs `clang` and `zlib1g-dev`; Windows needs the
   MSVC toolset, present on `windows-latest`; macOS needs Xcode command-line tools, present on
   `macos-latest`.
2. `dotnet build -c Release -p:Version=<version>` of the solution: the parity tests need the JIT
   build. The version must match the packs', or `--version` differs between the two builds.
3. `dotnet pack -c Release -r <rid> -p:IncludeSymbols=false -p:Version=<version>` and the pointer
   pack, both into a local feed directory. The RID pack writes an MSBuild file log
   (`-flp:LogFile=…;Verbosity=minimal`).
4. `AotWarningLogTests` with `DTK_AOT_PACK_LOG` set to that log (see 1.3).
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
  - packs the real pointer (listing every RID) and uploads it as `package-pointer`, which the
    release's `publish` job pushes, so that privileged job never restores or builds

#### 3.3 Test changes (`tests/DotnetTokenKiller.Cli.IntegrationTests`)

- **`IntegrationTestHelper`:** when `DTK_TEST_BINARY` is set and not blank, every dtk invocation
  starts that executable directly instead of `dotnet dtk.dll`. `StartDetached` follows the same
  rule. Nothing else about the spawned process changes (environment, isolation, timeouts).
- **`AotParityTests`** (in `Aot/`):
  - Every test is skipped unless `DTK_AOT_BINARY` is set, through `Theory` attribute subclasses that
    set `Skip` when the variable is absent. Local `dotnet test` runs and the `build` job are
    unaffected. CI sets `DTK_AOT_REQUIRED=1`, which turns a missing `DTK_AOT_BINARY` (or, for the
    pack-log check, `DTK_AOT_PACK_LOG`) into a failure instead of a skip.
  - Each case runs the same deterministic input through the JIT build's `dtk` apphost and through
    `DTK_AOT_BINARY`, with hermetic `DTK_CONFIG_PATH`, `DTK_DB_PATH`, `DTK_TEE_DIR`,
    `HOME`/`USERPROFILE` and `XDG_CONFIG_HOME`.
  - **Why the apphost and not `dotnet dtk.dll`.** On Unix, `Process.Start` looks for a bare file
    name beside the running executable before searching `PATH`. Under the `dotnet` muxer, dtk would
    start the real SDK instead of the fake `dotnet` (probed). The apphost is also what the `any`
    package runs.
  - **Same path for both sides.** The two sides run one after the other in the same sandbox path;
    the first side's directory is moved aside before the second starts. Spectre tables wrap long
    paths across lines, which no text normalization can rejoin (probed with `log --list`).
  - It asserts equal exit codes; equal combined output after normalizing the sandbox root,
    timestamps, durations and tee-log file names; equal tracking rows, with token counts exact and
    id, timestamp and elapsed time ignored; and equal normalized files written under the sandbox.
    The SDK's own first-run state is ignored.
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
    - Spectre's built-ins: `cli version`, `cli explain`, `cli opencli`, `--help-dump-opencli`
    - passthrough and an unknown command
    - one wrapped command per filter against a fake `dotnet` that prints a fixture and exits with a
      set code
    - `reset --force --all`
  - **Unix-only cases.** The fake `dotnet` is a generated POSIX shell script on the child's `PATH`,
    like `cold-start`'s; `Process.Start` on Windows does not run a script found on `PATH` as
    `dotnet`. `integrate --global` needs a home directory the test can move, and on Windows
    `USERPROFILE` does not move `Environment.SpecialFolder.UserProfile`. Both cases are skipped on
    Windows. The win-x64 integration-suite run in 3.1 covers wrapped commands there against real
    `dotnet`.
  - Real builds stay out of parity tests: their output depends on restore and build state, as the
    probe showed.
  - **Probed:** 20 cases (21 once the final review added an O200kBase tokenizer case) pass against
    the installed linux-x64 AOT tool and against the installed
    `any` fallback. Against an unrooted AOT build, 18 of the 19 cases that existed then failed; the
    one that passed was passthrough, which never reaches Spectre.
- **`AotWarningLogTests`** (in `Aot/`): the log parser from 1.3 with its unit tests, plus the
  `DTK_AOT_PACK_LOG` gated check.
- **`CommandSettingsAotGuardTests`:** the settings-type guard from 1.3. Always on.
- **`SpectreBuiltInCommandTests`:** the built-in command tests from 1.3. Always on, through
  `IntegrationTestHelper`, so they also run against `DTK_TEST_BINARY`.

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
  - the contained Spectre exception and its guards, so nobody "fixes" the two suppressions or adds
    a dictionary option without extending the parity tests
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
2. **Final JIT:** the `any` fallback package packed at the final commit and installed from a local
   feed. A plain Release build is not equivalent: with `PublishAot=true` in the csproj, its
   runtimeconfig carries the AOT feature switches, while the `any` pack (`-p:PublishAot=false`)
   keeps the original three properties.
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
  - The repository contains exactly two trim or AOT suppressions, both at the Spectre.Console.Cli
    boundary (1.3).
  - `dtk cli version`, `cli explain`, `cli opencli` and `--help-dump-opencli` still work in both
    builds.

## Testing

Test-first where the change is code:

- **`CommandSettingsAotGuardTests`:** passes on the current settings. Test-local settings types
  with a `Dictionary<string, int>`, an `int[]`, an `int?`, a `[TypeConverter]` on a property or a
  class, or a `[PairDeconstructor]` each produce exactly one violation.
- **`SpectreBuiltInCommandTests`:** passes today and after every change, in both builds.
- **`AotWarningLogTests`:** the accepted warnings, repeated as MSBuild's summary repeats them, report
  nothing. A dtk warning, an accepted code from another assembly, an accepted code raised as an error,
  and a log with no native compile are each reported.
- **`RtkHookCoexistence`:** the existing tests pass unchanged with the source-generated context.
  They already cover a config without `dotnet`, with it, and invalid TOML.
- **Integrators:** the existing `IntegratorHelpers` and `CopilotCliIntegrator` tests pass unchanged,
  so the JSON output is unchanged.
- **`IntegrationTestHelper`:** the choice lives in a pure `DtkLauncher.Resolve(testBinary, dllPath)`
  with unit tests (unset, empty and blank give `dotnet` plus the DLL; a path, trimmed, gives the
  binary alone). With `DTK_TEST_BINARY` unset, behaviour is unchanged (the existing suite).
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
  `ToolPackageRuntimeIdentifiers` entry and one matrix row in both `ci.yml` and `publish.yml`, under
  the same native-runner rule; the release derives its push list from the pointer and fails if a
  listed package is missing.
- Statically linking `e_sqlite3` into the binary. It would make the tracking-off loader probe
  meaningless.
- **The rewrite hook's double-prefix bug** in `.claude/hooks/dotnet-to-dtk.py` and
  `HookScriptTemplates.cs`. It is unrelated to AOT. At the user's request it was fixed on this
  branch as its own commit, before the pull request was opened.

## Amendments (2026-09-13, while writing the plan)

The plan's author prototyped every code change in a throwaway copy and ran it end to end on
linux-x64: build, RID and pointer packs, install from a local feed, parity, warning-log and guard
tests, and the process-spawning integration classes against the installed AOT tool (50 AOT-related
tests passing). That settled the spec's open mechanics and changed three decisions, all recorded
above:

- **Two suppressions, not one.** A no-op `TypeRegistrar.Register` broke Spectre's hidden built-in
  commands in both builds. The user chose to keep the reflective registration under a second
  contained suppression (IL2067), dropped the explicit command list and its DI guard, and added
  `SpectreBuiltInCommandTests` plus a parity case.
- **The warning check is a C# test** (`AotWarningLogTests`, gated on `DTK_AOT_PACK_LOG`), not
  `eng/check-aot-warnings.ps1`.
- **Parity runs the JIT side through the apphost**, with both sides at the same sandbox path, and
  `integrate --global` joins the wrapped commands as a Unix-only case.

Settled mechanics: the `BeforeTargets="WriteIlcRspFileForCompilation"` target for the three accepted
warnings, `CopyOutputSymbolsToPublishDirectory=false` plus a `ResolvedFileToPublish` filter for RID
package contents, native symbols under `bin/…/<rid>/native/`, the tool store layout
`.store/dotnettokenkiller/<version>/dotnettokenkiller.<rid>/`, and the requirement that CI build the
solution with the same `-p:Version` as the packs.

Also observed: ILC reports that Spectre.Console.Cli's `OpenCliParser.Parse` "will always throw",
because Spectre.Console.Cli 0.55.0 references NJsonSchema without declaring the dependency
(spectreconsole/spectre.console.cli#84). The JIT build has the same gap. dtk never parses OpenCLI
documents; `cli opencli` generates one, and works in both builds.

- **Found during Task 10's execution (review).** A plain `bin/Release` build is not the shipped
  fallback: `PublishAot=true` in the csproj means its runtimeconfig now carries the AOT feature
  switches, the same ones the linux-x64 tool's ILC compile applies. The `any` pack
  (`-p:PublishAot=false`) keeps the original three properties instead. Task 10's final JIT figures
  and the Measurement protocol above were corrected to measure the installed `any` package rather
  than `bin/Release`.

### Open issues found by the final review

1. **musl machines resolve the glibc AOT package.** The SDK's RID graph maps `linux-musl-x64` and
   `linux-musl-arm64` to `linux-x64` and `linux-arm64`, so `dotnet tool install` on Alpine picks the
   glibc AOT package, which cannot run there, instead of `any`. Confirmed in SDK 10.0.400's
   `RuntimeIdentifierGraph.json` and by a Docker install test.
2. **The Linux AOT `dtk` built on `ubuntu-latest` needs GLIBC_2.34.** It does not start on the glibc
   2.27–2.33 distros .NET 10 supports (RHEL/Rocky/Alma 8, Ubuntu 20.04, Debian 11), where today's
   framework-dependent tool starts. A probe built it in
   `mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-amd64` with
   `-p:SysRoot=/crossrootfs/x64 -p:LinkerFlavor=lld`: `dtk` then needs GLIBC_2.16 and runs on Rocky 8,
   Ubuntu 20.04 and 18.04. musl RIDs build in `…-cross-amd64-musl` or
   `mcr.microsoft.com/dotnet/sdk:10.0-alpine` and pass on Alpine 3.17–3.21.
3. **Pre-existing, independent of AOT:** SQLitePCLRaw.lib.e_sqlite3 3.53.3's `libe_sqlite3.so` needs
   GLIBC_2.34 (ericsink/SQLitePCL.raw#674), so tracking already fails on those distros in the
   released framework-dependent tool. Statically linking `libe_sqlite3.a` fixed it in the probe.
4. **Status:** not fixed on this branch; no release tag until the user decides. Options: cross-sysroot
   and musl RID jobs; Linux through `any` for now; or accept and document.

Probe recipe, recorded here because the throwaway probe directory was deleted:

- **Build image.** `FROM mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-amd64`
  plus `COPY --from=mcr.microsoft.com/dotnet/sdk:10.0 /usr/share/dotnet /usr/share/dotnet` (the
  pattern in dotnet/runtime `src/coreclr/nativeaot/docs/containers.md`). The `-cross-arm64`,
  `-cross-amd64-musl` and `-cross-arm64-musl` tags work the same way. They carry an Ubuntu 18.04
  (glibc 2.27) or Alpine 3.17 (musl 1.2.3) sysroot at `/crossrootfs/<arch>`, are amd64-only, and are
  about 6.5 GB each.
- **Pack.** `dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false
  -p:SysRoot=/crossrootfs/x64 -p:LinkerFlavor=lld` (for musl: `-r linux-musl-x64`, same sysroot path in
  the musl image). The binary needs ICU on the target (`libicu`, `icu-libs`).
- **Static SQLite, for issue 3.** In the CLI csproj, for Linux RIDs: `<DirectPInvoke Include="e_sqlite3"/>`,
  `<NativeLibrary Include="$(NuGetPackageRoot)sqlitepclraw.lib.e_sqlite3/<version>/runtimes/$(RuntimeIdentifier)/native/libe_sqlite3.a"/>`,
  remove `libe_sqlite3.so` from `ResolvedFileToPublish`, and link a shim object built with
  `clang --sysroot=/crossrootfs/x64 -O2 -fPIC -c fcntl64.c`, because glibc 2.27 has no `fcntl64`:

  ```c
  /* glibc < 2.28 has no fcntl64; on 64-bit Linux it is the same call as fcntl. */
  #include <stdarg.h>
  extern int fcntl(int fd, int cmd, ...);
  int fcntl64(int fd, int cmd, ...)
  {
      va_list ap;
      va_start(ap, cmd);
      void *arg = va_arg(ap, void *);
      va_end(ap);
      return fcntl(fd, cmd, arg);
  }
  ```

  Statically linking SQLite also makes the sub-project 2 tracking-off loader probe
  (`LD_DEBUG=files`, no `e_sqlite3`) meaningless; a replacement check would be needed.
- **CI shape.** glibc x64: a job-level `container:` with the cross image. musl x64: a
  `container: mcr.microsoft.com/dotnet/sdk:10.0-alpine` job (JavaScript actions work in Alpine
  containers on x64 runners only; `apk add bash clang build-base zlib-dev icu-libs` first). arm64:
  cross-build on `ubuntu-latest`, then install and test on `ubuntu-24.04-arm` (musl arm64 via
  `docker run`). Add a guard that fails if `dtk` or any packaged `.so` needs a `GLIBC_` above 2.27.

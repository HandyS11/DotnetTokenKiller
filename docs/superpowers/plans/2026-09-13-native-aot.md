# Native AOT Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship dtk as Native AOT tool packages for linux-x64, linux-arm64, osx-arm64 and win-x64
with a framework-dependent `any` fallback, with every trim/AOT warning in dtk's own code fixed,
Spectre.Console.Cli contained behind two tested suppressions, and CI proving the installed AOT tool
behaves exactly like the JIT build on every PR.

**Architecture:** The three libraries become `IsAotCompatible`, which forces real fixes for
`JsonArray.Add<T>` and reflection-based Tomlyn. The CLI sets `PublishAot`, roots `dtk` and
`Spectre.Console.Cli`, and confines Spectre's reflection to `SpectreCommandApp.Create` and
`TypeRegistrar.Register`. `ToolPackageRuntimeIdentifiers` produces a pointer package, four AOT RID
packages and an `any` package. Integration tests gain a `DTK_TEST_BINARY` switch, a JIT-vs-AOT parity
suite gated on `DTK_AOT_BINARY`, and a pack-log warning check gated on `DTK_AOT_PACK_LOG`. Reusable
workflows pack, install and test each package in CI and in the release workflow, which pushes RID
packages before the pointer.

**Tech Stack:** net10.0, C# 14, SDK 10.0.4xx, ILCompiler 10.0.11, Spectre.Console.Cli 0.55.0,
Tomlyn 2.10.1 (source generator), Microsoft.Data.Sqlite 10.0.12, xunit 2.9.3, FluentAssertions 8,
GitHub Actions (checkout v7, setup-dotnet v6, upload-artifact v7, download-artifact v8,
softprops/action-gh-release v3, NuGet/login v1).

**Spec:** [docs/superpowers/specs/2026-09-13-native-aot-design.md](../specs/2026-09-13-native-aot-design.md)
(read its "Amendments" section: the plan's code was prototyped end to end before this plan was
written).

## Global Constraints

- **Token counts stay exact.** `SavingsBaselineTests` must pass and
  `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` must not change.
  Never run `update-baseline`.
- **No filter changes, no tokenizer changes, no SQLite changes, no `Program.cs` warm-up changes.**
- **Exactly two trim/AOT suppressions exist when this plan is done**, both `UnconditionalSuppressMessage`:
  IL3050 on `SpectreCommandApp.Create` and IL2067 on `TypeRegistrar.Register`. Every other trim/AOT
  warning is fixed. Never add `NoWarn`, `#pragma`, `.editorconfig` severity changes or another
  suppression attribute; the one MSBuild exception is the `AllowSpectreCliAotWarnings` target in
  Task 4, which keeps exactly IL2104, IL3053 and IL3000 as warnings for the native compile only.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all` (Roslynator, SonarAnalyzer,
  NetAnalyzers, VS Threading, xunit analyzers). Fix every analyzer error. Traps already hit while
  prototyping this plan's code: RCS1141/RCS1140 (missing `param`/`exception` doc tags, also in tests),
  CA1062 (null-check `Type` parameters of public test methods), CA1812 (private test-local classes
  never instantiated: make them `abstract`), CA1034 and CA1819 (do not make nested test types
  public), IDE0028/IDE0306 (use `[.. items]` for `TheoryData`).
- **The code blocks in this plan compiled and passed on linux-x64 in a prototype.** Copy them exactly.
  If an analyzer or `dotnet format` still asks for a change, make the smallest change that satisfies
  it and say so in your report.
- **Code style:** file-scoped namespaces; `var`; private fields `_camelCase`; async methods end in
  `Async`; `ConfigureAwait(false)` on awaits in `src/` (tests do not use it); XML doc comments on
  members in `src/`; one type per file in `src/`.
- **Formatting:** LF, no trailing whitespace, no BOM, 4-space indent for `.cs`, 2-space for XML, JSON
  and YAML. Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before every commit.
- **Use `dtk dotnet …`** for build, test and format. `dotnet pack`, `dotnet publish`, `dotnet run` and
  `dotnet tool …` are not rewritten and are used as-is.
- **The Bash hook rewrites `dotnet build|test|restore|clean|format|list package` anywhere in a
  command,** including quoted strings and heredocs. Create files (scripts, configs, workflow YAML)
  with the Write tool, never with `cat <<EOF` or `echo` in Bash. Write every commit message to a file
  with the Write tool and commit with `git commit -F <file>`.
- **Commit messages end with** a blank line and the `Co-Authored-By:` trailer your own session's
  instructions specify.
- **The build-spawning CLI integration tests cannot pass on this machine.** Run test projects
  individually: `tests/DotnetTokenKiller.Domain.Tests`, `tests/DotnetTokenKiller.Application.Tests`,
  `tests/DotnetTokenKiller.Infrastructure.Tests`, and for `tests/DotnetTokenKiller.Cli.IntegrationTests`
  only the classes each task names, with `--filter`. CI gates the rest. Never run the whole CLI
  integration project or the whole solution's tests locally.
- **Never push, open a PR, or change anything under `~/.dotnet/tools`.** Local packages use version
  `0.0.0-local` and install only with `--tool-path` under `artifacts/`.
- **`artifacts/` is git-ignored.** Measurement output, local feeds and installed tools live there.
  Never commit them.
- **Another session may be active.** If files change under you without your doing, stop and report.

## File Structure

| File | Task | Responsibility |
|---|---|---|
| `src/DotnetTokenKiller.{Domain,Application,Infrastructure}/*.csproj` (modify) | 2 | `IsAotCompatible` |
| `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs` (modify) | 2 | Trim-safe `JsonArray` construction |
| `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (modify) | 2 | Trim-safe `JsonArray` construction and `Add` |
| `src/DotnetTokenKiller.Application/Integration/RtkTomlContext.cs` (new) | 2 | Source-generated Tomlyn context for `TomlTable` |
| `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs` (modify) | 2 | Parse through `RtkTomlContext` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Infrastructure/CommandSettingsAotGuardTests.cs` (new) | 3 | Settings member types stay on AOT-proven Spectre paths |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/SpectreBuiltInCommandTests.cs` (new) | 3 | Spectre's hidden `cli` commands keep working |
| `src/DotnetTokenKiller.Cli/Infrastructure/SpectreCommandApp.cs` (new) | 4 | The IL3050 suppression |
| `src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs` (modify) | 4 | The IL2067 suppression |
| `src/DotnetTokenKiller.Cli/Program.cs` (modify) | 4 | Create the app through `SpectreCommandApp` |
| `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (modify) | 4, 5 | `PublishAot`, rooting, ILC warning target, tool RIDs, RID package contents |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLog.cs` (new) | 4 | Pack-log parser |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs` (new) | 4, 7 | Skip-unless-configured attributes |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLogTests.cs` (new) | 4 | Parser tests and the gated pack-log check |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncher.cs` (new) | 6 | JIT build or `DTK_TEST_BINARY` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncherTests.cs` (new) | 6 | Launcher tests |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs` (modify) | 6 | Start dtk through `DtkLauncher` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj` (modify) | 7 | Copy the Application fixtures to the output |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/{ParityCase,ParitySandbox,ParityRunner,ParityCases,AotParityTests}.cs` (new) | 7 | JIT-vs-AOT parity suite |
| `.github/workflows/aot-package.yml` (new) | 8 | Pack, install and test one AOT RID |
| `.github/workflows/fallback-package.yml` (new) | 8 | Pack, install and test the `any` package |
| `.github/workflows/ci.yml` (modify) | 8 | `aot` matrix and `fallback` jobs |
| `.github/workflows/publish.yml` (rewrite) | 9 | Release: test, pack all, push RIDs then pointer, release assets |
| `Directory.Build.props` (modify) | 9 | Version comment |
| `CLAUDE.md`, `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/articles/getting-started.md` (modify) | 10 | Figures, pack sequence, AOT testing, install notes |
| `docs/superpowers/specs/2026-09-13-native-aot-design.md` (modify) | 10 | Status line |

## Measurement Commands

Tasks 1 and 10 use these. Output goes under `artifacts/native-aot/`.

**Cold start** (replace `<binary>` and `<name>`):

```bash
mkdir -p artifacts/native-aot
set -o pipefail; dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start <binary> 2>&1 | tee artifacts/native-aot/<name>.txt
```

`cold-start` does not build the CLI; build it first. It takes about 3 minutes, so set the Bash
timeout to `600000`. Check the output's `Built:` line is the build you just made. The run must print
all three sections; an exception means a sample was rejected and the figures are unusable.

**`--version` and tracking off.** Create `artifacts/native-aot/startup-extra.py` with the Write tool
if it does not exist:

```python
#!/usr/bin/env python3
"""Times `dtk --version` and tracking-off `dtk pipe build` in a hermetic state directory.

Usage: python3 artifacts/native-aot/startup-extra.py <dtk binary>   (run from the repo root)
"""
import os
import pathlib
import statistics
import subprocess
import sys
import tempfile
import time

WARMUP = 5
MEASURED = 21

binary = sys.argv[1]
fixture = pathlib.Path("tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt").read_bytes()


def measure(label, args, env, stdin, expected_exit):
    samples = []
    for i in range(WARMUP + MEASURED):
        start = time.perf_counter()
        run = subprocess.run([binary, *args], input=stdin, env=env, capture_output=True)
        elapsed = (time.perf_counter() - start) * 1000
        if run.returncode != expected_exit or not run.stdout.strip():
            sys.exit(f"{label}: unexpected run: exit {run.returncode}\n{run.stdout.decode()}\n{run.stderr.decode()}")
        if i >= WARMUP:
            samples.append(elapsed)
    samples.sort()
    print(f"{label}: median {statistics.median(samples):.1f} ms, "
          f"min {samples[0]:.1f} ms, max {samples[-1]:.1f} ms ({MEASURED} runs)")


with tempfile.TemporaryDirectory(prefix="dtk-startup-extra-") as root:
    env = dict(os.environ,
               DTK_CONFIG_PATH=f"{root}/config.json",
               DTK_DB_PATH=f"{root}/tracking.db",
               DTK_TEE_DIR=f"{root}/logs")
    env.pop("NO_COLOR", None)

    measure("dtk --version", ["--version"], env, b"", 0)

    subprocess.run([binary, "config", "set", "tracking.enabled", "false"],
                   env=env, check=True, stdout=subprocess.DEVNULL)
    measure("tracking off, dtk pipe build", ["pipe", "build", "--exit-code", "1"], env, fixture, 1)

    if os.path.exists(f"{root}/tracking.db"):
        sys.exit("tracking.db was created, so tracking was not off; these are not tracking-off timings")
```

Run it (replace `<binary>` and `<name>`):

```bash
python3 artifacts/native-aot/startup-extra.py <binary> | tee artifacts/native-aot/<name>.txt
```

## Local Package Commands

Tasks 5, 7 and 10 use these to pack and install the linux-x64 tool exactly as CI does. Create
`artifacts/native-aot/feed.nuget.config` with the Write tool if it does not exist:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="/home/cloudcli/projects/DotnetTokenKiller/artifacts/native-aot/feed" />
  </packageSources>
</configuration>
```

Pack and install (about a minute):

```bash
rm -rf artifacts/native-aot/feed artifacts/native-aot/tools src/DotnetTokenKiller.Cli/bin/Release/net10.0/linux-x64/publish ~/.nuget/packages/dotnettokenkiller/0.0.0-local ~/.nuget/packages/dotnettokenkiller.linux-x64/0.0.0-local
mkdir -p artifacts/native-aot/feed
dtk dotnet build DotnetTokenKiller.slnx -c Release -p:Version=0.0.0-local
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/native-aot/feed "-flp:LogFile=artifacts/native-aot/pack-linux-x64.log;Verbosity=minimal"
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/native-aot/feed
dotnet tool install --tool-path artifacts/native-aot/tools --configfile artifacts/native-aot/feed.nuget.config DotnetTokenKiller --version 0.0.0-local
test -d artifacts/native-aot/tools/.store/dotnettokenkiller/0.0.0-local/dotnettokenkiller.linux-x64 && echo "installed the linux-x64 AOT package"
```

The build must use the same `-p:Version` as the packs, or `--version` differs between the JIT build
the parity tests compare against and the installed tool. Removing the `0.0.0-local` entries from the
NuGet cache matters: packages with the same ID and version are otherwise served from the cache, not
the feed. Removing the old `publish` directory matters too: tool packing globs it, so stale files
would be packed.

---

### Task 1: Baseline measurements

No code changes. These figures are what every later scenario is compared with.

**Files:** none (output under `artifacts/native-aot/`)

- [ ] **Step 1: Confirm the tree is the spec commit plus docs only**

Run: `git status --short && git log --oneline -3`
Expected: clean tree; the top commits are this plan and the spec. `git diff develop --stat -- src/`
prints nothing.

- [ ] **Step 2: Build the JIT CLI in Release**

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release`
Expected: build succeeds.

- [ ] **Step 3: Run the cold-start baseline**

Use the cold-start command with `<binary>` = `src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk` and
`<name>` = `baseline-jit-cold-start`. Check the `Built:` line.

- [ ] **Step 4: Run the `--version` and tracking-off baseline**

Create `startup-extra.py` as described, then run it with
`<binary>` = `src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk` and `<name>` = `baseline-jit-extra`.

- [ ] **Step 5: Report the medians**

Report, from the two files: pipe median; instant-child overhead median; 1000 ms-child overhead
median and wall-clock; `--version` median; tracking-off median. Also report `uptime`'s load average
at the start of Step 3. No commit.

---

### Task 2: Make the libraries AOT-compatible

`IsAotCompatible` turns on the trim, AOT and single-file analyzers in every normal build of the three
libraries. Adding it first makes the build fail at exactly the sites to fix, which is this task's
failing test.

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj`
- Modify: `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj`
- Modify: `src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj`
- Modify: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs:137-147`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs:297-304,386`
- Create: `src/DotnetTokenKiller.Application/Integration/RtkTomlContext.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs:86,144`
- Test: existing `tests/DotnetTokenKiller.Application.Tests/Integration/{CopilotCliIntegratorTests,IntegratorHelpersTests,RtkHookCoexistenceTests}.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed partial class RtkTomlContext : TomlSerializerContext` in
  `DotnetTokenKiller.Application.Integration`, with the generated `RtkTomlContext.Default`.

- [ ] **Step 1: Turn on the analyzers**

In each of the three csproj files, add `IsAotCompatible` as the first property of the first
`PropertyGroup`. For example, the Domain csproj becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Trim, AOT and single-file analyzers in every build: the CLI ships as Native AOT. -->
    <IsAotCompatible>true</IsAotCompatible>
    <RootNamespace>DotnetTokenKiller.Domain</RootNamespace>
  </PropertyGroup>
```

Do the same in the Application and Infrastructure csproj files, keeping their `RootNamespace`.

- [ ] **Step 2: Build and watch it fail at the five sites**

Run: `dtk dotnet build src/DotnetTokenKiller.Application -c Release`
Expected: FAIL with IL2026 and IL3050 at `CopilotCliIntegrator.cs(139,…)`,
`IntegratorHelpers.cs(299,…)`, `IntegratorHelpers.cs(386,…)`, `RtkHookCoexistence.cs(86,…)` and
`RtkHookCoexistence.cs(144,…)`, and nothing else.

- [ ] **Step 3: Fix `CopilotCliIntegrator.BuildHookJson`**

Replace:

```csharp
                ["preToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = $"python3 {HookScriptName}",
                        ["cwd"] = cwd,
                        ["timeoutSec"] = 10
                    }
                }
```

with:

```csharp
                ["preToolUse"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = $"python3 {HookScriptName}",
                        ["cwd"] = cwd,
                        ["timeoutSec"] = 10
                    })
```

- [ ] **Step 4: Fix `IntegratorHelpers`**

In `WriteHookAndSettingsAsync`, replace:

```csharp
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = spec.Command
                    }
                }
```

with:

```csharp
                ["hooks"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = spec.Command
                    })
```

In `MergeJsonSettingsAsync`, replace `hookArray.Add(hookEntry);` with:

```csharp
            // The JsonNode overload: Add<JsonObject> is neither trim- nor AOT-safe.
            hookArray.Add((JsonNode)hookEntry);
```

- [ ] **Step 5: Build and confirm only the Tomlyn sites remain**

Run: `dtk dotnet build src/DotnetTokenKiller.Application -c Release`
Expected: FAIL with IL2026 and IL3050 at `RtkHookCoexistence.cs(86,…)` and `(144,…)` only.

- [ ] **Step 6: Add the Tomlyn context**

Create `src/DotnetTokenKiller.Application/Integration/RtkTomlContext.cs`:

```csharp
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Source-generated Tomlyn metadata for reading rtk's config as an untyped <see cref="TomlTable"/>, so
/// parsing needs no reflection and stays safe under trimming and Native AOT.
/// </summary>
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class RtkTomlContext : TomlSerializerContext;
```

- [ ] **Step 7: Parse through the context**

In `RtkHookCoexistence.cs`, both call sites read:

```csharp
TomlSerializer.TryDeserialize<TomlTable>(text, out var model)
```

Replace each with:

```csharp
TomlSerializer.TryDeserialize(text, RtkTomlContext.Default, out TomlTable? model)
```

- [ ] **Step 8: Build the whole solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx -c Release`
Expected: PASS with no warnings or errors.

- [ ] **Step 9: Run the library tests**

Run each:
- `dtk dotnet test tests/DotnetTokenKiller.Application.Tests -c Release --no-build`
- `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests -c Release --no-build`
- `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests -c Release --no-build`

Expected: all PASS. `RtkHookCoexistenceTests`, `CopilotCliIntegratorTests` and
`IntegratorHelpersTests` pass unchanged, which shows the JSON written and the TOML parsing are
unchanged.

- [ ] **Step 10: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
perf: make the libraries AOT-compatible

IsAotCompatible on Domain, Application and Infrastructure runs the trim,
AOT and single-file analyzers in every build. Build hook JSON through the
JsonNode overloads instead of JsonArray.Add<JsonObject>, and read rtk's
TOML through a source-generated Tomlyn context.
```

---

### Task 3: Guard the Spectre behaviour AOT depends on

Two always-on tests that pass on today's code and fail if a later change takes Spectre.Console.Cli
down a path Native AOT has not been proven on, or breaks its built-in commands. Write them before
touching the CLI, so Task 4 is checked against them.

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Infrastructure/CommandSettingsAotGuardTests.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/SpectreBuiltInCommandTests.cs`

**Interfaces:**
- Consumes: `CliConfigurator` (internal, visible to the test project), `IntegrationTestHelper.RunDtkAsync(params string[])`.
- Produces: nothing other tasks call.

- [ ] **Step 1: Write the settings guard**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Infrastructure/CommandSettingsAotGuardTests.cs`:

```csharp
using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Spectre.Console.Cli binds settings by reflection, which Native AOT does not support in general. The
/// member types allowed here are the ones <c>AotParityTests</c> proves under AOT: plain generic
/// instantiations over reference types, <c>string[]</c>, and the built-in converters for
/// <see cref="bool"/>, <see cref="int"/> and <see cref="string"/>. A dictionary or pair option, a
/// value-type array, a nullable or a custom converter takes Spectre down code paths no parity test
/// covers; extend <c>AotParityTests</c> before extending this list.
/// </summary>
public sealed class CommandSettingsAotGuardTests
{
    private static readonly Type[] AllowedMemberTypes = [typeof(bool), typeof(int), typeof(string), typeof(string[])];

    private static readonly Type[] DtkSettingsTypes =
    [
        .. typeof(CliConfigurator).Assembly.GetTypes()
            .Where(type => typeof(CommandSettings).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal),
    ];

    public static TheoryData<Type> SettingsTypes => [.. DtkSettingsTypes];

    [Fact]
    public void SettingsTypes_FindsEverySettingsClass()
    {
        DtkSettingsTypes.Should().HaveCount(9, "dtk has nine settings classes, one abstract; update this when adding one");
    }

    [Theory]
    [MemberData(nameof(SettingsTypes))]
    public void Settings_UseOnlyAotProvenMembers(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        FindViolations(settingsType).Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(DictionaryOptionSettings))]
    [InlineData(typeof(ValueTypeArrayOptionSettings))]
    [InlineData(typeof(NullableOptionSettings))]
    [InlineData(typeof(ConverterOptionSettings))]
    [InlineData(typeof(PairDeconstructorOptionSettings))]
    [InlineData(typeof(ConverterClassSettings))]
    public void FindViolations_UnprovenMember_IsReported(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        FindViolations(settingsType).Should().ContainSingle();
    }

    private static List<string> FindViolations(Type settingsType)
    {
        var violations = new List<string>();
        if (settingsType.GetCustomAttribute<TypeConverterAttribute>() is not null)
        {
            violations.Add($"{settingsType.Name} has a [TypeConverter]");
        }

        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var property in settingsType.GetProperties(declared))
        {
            if (property.GetCustomAttribute<CommandOptionAttribute>() is null
                && property.GetCustomAttribute<CommandArgumentAttribute>() is null)
            {
                continue; // Spectre binds only options and arguments
            }

            if (!AllowedMemberTypes.Contains(property.PropertyType))
            {
                violations.Add($"{settingsType.Name}.{property.Name} is {property.PropertyType}");
            }
            else if (property.GetCustomAttribute<TypeConverterAttribute>() is not null)
            {
                violations.Add($"{settingsType.Name}.{property.Name} has a [TypeConverter]");
            }
            else if (property.GetCustomAttribute<PairDeconstructorAttribute>() is not null)
            {
                violations.Add($"{settingsType.Name}.{property.Name} has a [PairDeconstructor]");
            }
        }

        return violations;
    }

    private abstract class DictionaryOptionSettings : CommandSettings
    {
        [CommandOption("--values")]
        public Dictionary<string, int> Values { get; init; } = [];
    }

    private abstract class ValueTypeArrayOptionSettings : CommandSettings
    {
        [CommandOption("--numbers")]
        public int[] Numbers { get; init; } = [];
    }

    private abstract class NullableOptionSettings : CommandSettings
    {
        [CommandOption("--count")]
        public int? Count { get; init; }
    }

    private abstract class ConverterOptionSettings : CommandSettings
    {
        [CommandOption("--name")]
        [TypeConverter(typeof(StringConverter))]
        public string Name { get; init; } = string.Empty;
    }

    private abstract class PairDeconstructorOptionSettings : CommandSettings
    {
        [CommandOption("--pairs")]
        [PairDeconstructor(typeof(NoPairDeconstructor))]
        public string[] Pairs { get; init; } = [];
    }

    [TypeConverter(typeof(StringConverter))]
    private abstract class ConverterClassSettings : CommandSettings;

    private abstract class NoPairDeconstructor : PairDeconstructor<string, string>
    {
        protected override (string Key, string Value) Deconstruct(string? value) => throw new NotSupportedException();
    }
}
```

The test-local settings types are `abstract` because CA1812 rejects private classes that are never
instantiated, and CA1034 rejects nested public ones. `FindViolations` only reflects over them.

- [ ] **Step 2: Write the built-in command tests**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/SpectreBuiltInCommandTests.cs`:

```csharp
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Spectre.Console.Cli adds hidden built-in commands whose constructors take its internal services, so
/// they resolve only through the reflective <c>TypeRegistrar.Register</c>. Parity tests cannot catch a
/// break that hits the JIT and AOT builds alike; these can. With <c>DTK_TEST_BINARY</c> set they run
/// against that binary.
/// </summary>
public sealed class SpectreBuiltInCommandTests
{
    [Theory(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    [InlineData(new[] { "cli", "version" }, "Spectre.Console.Cli version")]
    [InlineData(new[] { "cli", "explain" }, "CLI Configuration")]
    [InlineData(new[] { "cli", "opencli" }, "\"opencli\"")]
    [InlineData(new[] { "--help-dump-opencli" }, "\"opencli\"")]
    public async Task BuiltInCommand_RunsAsync(string[] arguments, string expected)
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(arguments);

        exitCode.Should().Be(0, output);
        output.Should().Contain(expected).And.NotContain("Could not resolve type");
    }
}
```

- [ ] **Step 3: Build and run both classes**

Run: `dtk dotnet build DotnetTokenKiller.slnx -c Release`
Then: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~CommandSettingsAotGuardTests|FullyQualifiedName~SpectreBuiltInCommandTests"`
Expected: PASS, 20 tests (1 count, 9 settings types, 6 violation cases, 4 built-ins). The six
violation cases passing is what shows the guard can fail.

- [ ] **Step 4: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
test: guard the Spectre.Console.Cli paths Native AOT depends on

Settings options and arguments may only be bool, int, string or string[]
without converters or pair deconstructors, the paths the parity tests
prove under AOT. Spectre's hidden cli version, cli explain and cli opencli
commands must keep running in every build.
```

---

### Task 4: Publish the CLI as Native AOT

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLog.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLogTests.cs`
- Create: `src/DotnetTokenKiller.Cli/Infrastructure/SpectreCommandApp.cs`
- Modify: `src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs`
- Modify: `src/DotnetTokenKiller.Cli/Program.cs:53`
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`

**Interfaces:**
- Consumes: `IntegrationTestHelper.DefaultTimeoutMs`.
- Produces:
  - `internal static class AotWarningLog` with `internal static List<string> FindProblems(string log)`.
  - `internal static class AotParitySkip` with `internal const string AotBinaryVariable = "DTK_AOT_BINARY"`
    and `internal static string? Reason(bool unixOnly)`.
  - Attributes `AotParityTheoryAttribute`, `AotParityUnixTheoryAttribute`, and
    `AotPackLogFactAttribute` with `internal const string PackLogVariable = "DTK_AOT_PACK_LOG"`.
  - `internal static class SpectreCommandApp` with `internal static CommandApp Create(ITypeRegistrar registrar)`.
  - All test types above are in namespace `DotnetTokenKiller.Cli.IntegrationTests.Aot`.

- [ ] **Step 1: Write the attributes**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs`:

```csharp
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>Why a parity test is skipped in this environment, or <see langword="null"/> to run it.</summary>
internal static class AotParitySkip
{
    /// <summary>The environment variable naming the dtk binary compared against the JIT build.</summary>
    internal const string AotBinaryVariable = "DTK_AOT_BINARY";

    /// <summary>Returns the skip reason, or <see langword="null"/> when the test should run.</summary>
    /// <param name="unixOnly">Whether the test cannot run on Windows.</param>
    /// <returns>The reason to skip, or <see langword="null"/>.</returns>
    internal static string? Reason(bool unixOnly)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AotBinaryVariable)))
        {
            return $"Set {AotBinaryVariable} to a dtk binary to compare it with the JIT build.";
        }

        return unixOnly && OperatingSystem.IsWindows()
            ? "Unix only: needs a POSIX fake dotnet, or a home directory that USERPROFILE can move."
            : null;
    }
}

/// <summary>A theory that runs only when <c>DTK_AOT_BINARY</c> is set.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityTheoryAttribute : TheoryAttribute
{
    public AotParityTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: false);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>A theory that runs only when <c>DTK_AOT_BINARY</c> is set and the OS is not Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityUnixTheoryAttribute : TheoryAttribute
{
    public AotParityUnixTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: true);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>A fact that runs only when <c>DTK_AOT_PACK_LOG</c> names the log of a Native AOT pack.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotPackLogFactAttribute : FactAttribute
{
    /// <summary>The environment variable naming the pack log to check.</summary>
    internal const string PackLogVariable = "DTK_AOT_PACK_LOG";

    public AotPackLogFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PackLogVariable)))
        {
            Skip = $"Set {PackLogVariable} to the log of a 'dotnet pack -r <rid>' run to check its warnings.";
        }
    }
}
```

The two parity theory attributes are used in Task 7; defining them here keeps the skip logic in one
file.

- [ ] **Step 2: Write the warning-log tests**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLogTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

public sealed class AotWarningLogTests
{
    private const string SpectreTrim =
        "/root/.nuget/packages/spectre.console.cli/0.55.0/lib/net10.0/Spectre.Console.Cli.dll : warning IL2104: Assembly 'Spectre.Console.Cli' produced trim warnings. For more information see https://aka.ms/il2104 [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string SpectreAot =
        "/root/.nuget/packages/spectre.console.cli/0.55.0/lib/net10.0/Spectre.Console.Cli.dll : warning IL3053: Assembly 'Spectre.Console.Cli' produced AOT analysis warnings. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string SpectreSingleFile =
        "/_/src/Spectre.Console.Cli/Internal/Modelling/CommandModel.cs(50): warning IL3000: Spectre.Console.Cli.CommandModel.GetApplicationFile(): 'System.Reflection.Assembly.Location.get' always returns an empty string for assemblies embedded in a single-file app. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string Accepted = SpectreTrim + "\n" + SpectreAot + "\n" + SpectreSingleFile + "\n";

    [Fact]
    public void FindProblems_ExactlyTheAcceptedWarnings_ReportsNothing()
    {
        AotWarningLog.FindProblems("  Restored.\n" + Accepted + Accepted + "Build succeeded.\n").Should().BeEmpty(
            "MSBuild repeats warnings in its summary, and repeats are not new problems");
    }

    [Fact]
    public void FindProblems_WarningFromDtkCode_IsReported()
    {
        const string ours =
            "/repo/src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs(386): Trim analysis warning IL2026: DotnetTokenKiller.Application.Integration.IntegratorHelpers.MergeJsonSettingsAsync(): Using member 'System.Text.Json.Nodes.JsonArray.Add<JsonObject>(JsonObject)' [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

        AotWarningLog.FindProblems(Accepted + ours).Should().ContainSingle().Which.Should().Contain("IL2026");
    }

    [Fact]
    public void FindProblems_AcceptedCodeFromAnotherAssembly_IsReported()
    {
        const string other =
            "/root/.nuget/packages/tomlyn/2.10.1/lib/net10.0/Tomlyn.dll : warning IL2104: Assembly 'Tomlyn' produced trim warnings. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

        AotWarningLog.FindProblems(Accepted + other).Should().ContainSingle().Which.Should().Contain("Tomlyn");
    }

    [Fact]
    public void FindProblems_AcceptedCodeAsError_IsReported()
    {
        AotWarningLog.FindProblems(Accepted + SpectreTrim.Replace("warning IL2104", "error IL2104", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("error IL2104");
    }

    [Fact]
    public void FindProblems_NoNativeCompile_ReportsEveryMissingWarning()
    {
        AotWarningLog.FindProblems("  dtk -> /repo/bin/dtk.dll\nBuild succeeded.\n").Should().HaveCount(3);
    }

    [AotPackLogFact]
    public async Task PackLog_HasOnlyTheAcceptedSpectreWarningsAsync()
    {
        var log = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable(AotPackLogFactAttribute.PackLogVariable)!.Trim());

        AotWarningLog.FindProblems(log).Should().BeEmpty(
            "Spectre.Console.Cli's IL2104, IL3053 and IL3000 are the only accepted trim or AOT warnings");
    }
}
```

- [ ] **Step 3: Build and watch the tests fail to compile**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests -c Release`
Expected: FAIL with CS0103 (`AotWarningLog` does not exist).

- [ ] **Step 4: Write the parser**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotWarningLog.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Reads a Native AOT pack or publish log and reports every trim or AOT diagnostic outside the one
/// accepted exception: IL2104, IL3053 and IL3000, as warnings, from Spectre.Console.Cli.
/// </summary>
internal static partial class AotWarningLog
{
    private static readonly string[] AllowedCodes = ["IL2104", "IL3053", "IL3000"];

    /// <summary>Lists what is wrong with <paramref name="log"/>, or nothing when it holds exactly the accepted warnings.</summary>
    /// <param name="log">The whole text of an MSBuild log from <c>dotnet pack -r &lt;rid&gt;</c> or <c>dotnet publish</c>.</param>
    /// <returns>One line per unexpected diagnostic, and one per accepted code that never appeared.</returns>
    internal static List<string> FindProblems(string log)
    {
        var diagnostics = log.Split('\n')
            .Select(line => DiagnosticRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => new Diagnostic(
                match.Groups["origin"].Value.Trim(),
                match.Groups["level"].Value,
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim()))
            .Distinct()
            .ToList();

        var problems = diagnostics
            .Where(d => d.Level != "warning"
                        || !AllowedCodes.Contains(d.Code, StringComparer.Ordinal)
                        || !d.Origin.Contains("Spectre.Console.Cli", StringComparison.OrdinalIgnoreCase))
            .Select(d => $"unexpected {d.Level} {d.Code} from {d.Origin}: {d.Message}")
            .ToList();

        // A missing accepted warning means the native compile did not run, or Spectre.Console.Cli changed
        // and the exception has to be re-verified; either way the log proves nothing.
        problems.AddRange(AllowedCodes
            .Where(code => !diagnostics.Exists(d => d.Code == code))
            .Select(code => $"expected warning {code} from Spectre.Console.Cli, but the log has none"));

        return problems;
    }

    // "<origin>: warning IL2104: <message> [<project>]", where <origin> is a file, a file(line) or "ILC".
    [GeneratedRegex(@"^\s*(?<origin>.*?)\s*:\s*(?:Trim analysis |AOT analysis )?(?<level>warning|error) (?<code>IL\d{4})\s*:\s*(?<message>.*?)(?:\s*\[[^\]]*\])?\s*$")]
    private static partial Regex DiagnosticRegex();

    private sealed record Diagnostic(string Origin, string Level, string Code, string Message);
}
```

- [ ] **Step 5: Run the parser tests**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests -c Release`
Then: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~AotWarningLogTests"`
Expected: 5 PASS, 1 skipped (`PackLog_HasOnlyTheAcceptedSpectreWarningsAsync`, no `DTK_AOT_PACK_LOG`).

- [ ] **Step 6: Add the IL3050 factory**

Create `src/DotnetTokenKiller.Cli/Infrastructure/SpectreCommandApp.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Creates the Spectre.Console.Cli application, which Native AOT does not officially support.</summary>
internal static class SpectreCommandApp
{
    /// <summary>Creates a <see cref="CommandApp"/> that resolves commands through <paramref name="registrar"/>.</summary>
    /// <param name="registrar">The registrar bridging Spectre.Console.Cli to dependency injection.</param>
    /// <returns>The unconfigured application.</returns>
    [UnconditionalSuppressMessage(
        "AotAnalysis",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "Spectre.Console.Cli marks CommandApp as unsupported under Native AOT because it binds "
                        + "commands and settings by reflection. dtk roots both assemblies (TrimmerRootAssembly), "
                        + "limits settings to the member types CommandSettingsAotGuardTests allows, and "
                        + "AotParityTests runs every command family through the AOT binary on each release RID. "
                        + "One of the two trim or AOT suppressions at the Spectre.Console.Cli boundary; see the "
                        + "native AOT design spec.")]
    internal static CommandApp Create(ITypeRegistrar registrar) => new(registrar);
}
```

In `src/DotnetTokenKiller.Cli/Program.cs`, replace `var app = new CommandApp(registrar);` with:

```csharp
    var app = SpectreCommandApp.Create(registrar);
```

- [ ] **Step 7: Add the IL2067 suppression**

Replace the whole of `src/DotnetTokenKiller.Cli/Infrastructure/TypeRegistrar.cs` with:

```csharp
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Bridges Spectre.Console.Cli's type registration to Microsoft DI.</summary>
/// <param name="services">The service collection to register types into.</param>
internal sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    /// <inheritdoc/>
    public ITypeResolver Build()
    {
        return new TypeResolver(services.BuildServiceProvider());
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method",
        Justification = "Spectre.Console.Cli passes the command, settings and built-in command types it registers "
                        + "(including its hidden 'cli version', 'cli explain' and 'cli opencli' commands, whose "
                        + "constructors take its internal services), and ITypeRegistrar carries no trimming "
                        + "annotations. dtk and Spectre.Console.Cli are rooted (TrimmerRootAssembly), so every "
                        + "constructor survives; AotParityTests and SpectreBuiltInCommandTests run these types "
                        + "through the AOT binary. One of the two trim or AOT suppressions at the Spectre.Console.Cli "
                        + "boundary; see the native AOT design spec.")]
    public void Register(Type service, Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <inheritdoc/>
    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <inheritdoc/>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        services.AddSingleton(service, _ => factory());
    }
}
```

`Register` must keep registering. Making it a no-op was prototyped and broke `dtk cli version`,
`cli explain` and `cli opencli` in both builds; `SpectreBuiltInCommandTests` now catches that.

- [ ] **Step 8: Turn on Native AOT in the CLI csproj**

In `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`, directly after the `TieredPGO` element
and its comment, add:

```xml
    <!--
      Native AOT: installed tools on the RIDs in ToolPackageRuntimeIdentifiers run a native binary (see
      docs/superpowers/specs/2026-09-13-native-aot-design.md). PublishAot also runs the AOT, trim and
      single-file analyzers in every normal build. The `any` package is packed with
      -p:PublishAot=false, stays framework-dependent, and keeps TieredPGO above.
    -->
    <PublishAot>true</PublishAot>
```

Before the closing `</Project>`, add:

```xml
  <!--
    Spectre.Console.Cli binds commands and settings by reflection and does not support trimming or
    Native AOT. Rooting both assemblies keeps every constructor and property it reflects over. The
    exception is contained and tested: see SpectreCommandApp, TypeRegistrar.Register,
    CommandSettingsAotGuardTests, SpectreBuiltInCommandTests and AotParityTests.
  -->
  <ItemGroup>
    <TrimmerRootAssembly Include="dtk"/>
    <TrimmerRootAssembly Include="Spectre.Console.Cli"/>
  </ItemGroup>
  <!--
    The native compile reports three warnings from inside Spectre.Console.Cli (IL2104, IL3053 and IL3000),
    which TreatWarningsAsErrors would turn into errors. This target keeps them warnings for the
    ILCompiler only: it runs after the Roslyn compile, so IL3000 in dtk's own code is still an error, and
    any other IL warning still fails the publish. AotWarningLogTests checks a pack log holds exactly these
    three.
  -->
  <Target Name="AllowSpectreCliAotWarnings" BeforeTargets="WriteIlcRspFileForCompilation">
    <PropertyGroup>
      <WarningsNotAsErrors>$(WarningsNotAsErrors);IL2104;IL3053;IL3000</WarningsNotAsErrors>
    </PropertyGroup>
  </Target>
```

- [ ] **Step 9: Build the solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx -c Release`
Expected: PASS with no warnings or errors. `PublishAot` now runs the analyzers over the CLI, and the
two suppressions are the only reason `Program.cs` and `TypeRegistrar.cs` stay clean.

- [ ] **Step 10: Publish linux-x64 with a file log**

Run (timeout `600000`):

```bash
mkdir -p artifacts/native-aot
rm -rf artifacts/native-aot/publish-linux-x64
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/native-aot/publish-linux-x64 "-flp:LogFile=artifacts/native-aot/publish-linux-x64.log;Verbosity=minimal"
```

Expected: exit 0. `grep -c "warning IL" artifacts/native-aot/publish-linux-x64.log` prints `3`.

- [ ] **Step 11: Check the log with the gated test**

Run: `DTK_AOT_PACK_LOG=$PWD/artifacts/native-aot/publish-linux-x64.log dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~AotWarningLogTests"`
Expected: 6 PASS, none skipped.

- [ ] **Step 12: Smoke the published binary**

Run each:
- `artifacts/native-aot/publish-linux-x64/dtk --version`: prints the version, exit 0.
- `artifacts/native-aot/publish-linux-x64/dtk cli explain | head -3`: prints `CLI Configuration`.
- `DTK_CONFIG_PATH=$(mktemp -d)/c.json DTK_DB_PATH=$(mktemp -d)/t.db artifacts/native-aot/publish-linux-x64/dtk pipe build --exit-code 1 < tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt; echo "exit $?"`:
  prints the build filter summary and `exit 1`.

- [ ] **Step 13: Re-run the Task 3 guards**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~CommandSettingsAotGuardTests|FullyQualifiedName~SpectreBuiltInCommandTests|FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~SubcommandRegistrationTests"`
Expected: all PASS.

- [ ] **Step 14: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
perf: publish the CLI as Native AOT

PublishAot with dtk and Spectre.Console.Cli rooted. Spectre's reflection
is confined to two suppressions at its boundary, SpectreCommandApp.Create
(IL3050) and TypeRegistrar.Register (IL2067). The native compile keeps
Spectre's own IL2104, IL3053 and IL3000 as warnings, and
AotWarningLogTests fails a pack log that holds anything else.
```

---

### Task 5: Pack the RID-specific tool packages

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`

**Interfaces:**
- Consumes: Task 4's csproj.
- Produces: `dotnet pack -r <rid>` yields `DotnetTokenKiller.<rid>.<version>.nupkg` holding only the
  native `dtk` and `libe_sqlite3`; plain `dotnet pack` yields the pointer; `-r any -p:PublishAot=false`
  yields `DotnetTokenKiller.any.<version>.nupkg` and `.snupkg`.

- [ ] **Step 1: See the untrimmed package contents fail the expectation**

First set the RIDs, so a RID pack exists at all. In the csproj's first `PropertyGroup`, directly after
`<PublishAot>true</PublishAot>`, add:

```xml
    <!--
      One tool package per RID below plus the pointer package that lists them; `any` is the
      framework-dependent fallback for every other platform. Set here, not with -p:, because a
      semicolon list on the command line collapses into one RID. The pack sequence is in CLAUDE.md.
    -->
    <ToolPackageRuntimeIdentifiers>linux-x64;linux-arm64;osx-arm64;win-x64;any</ToolPackageRuntimeIdentifiers>
```

Run (timeout `600000`):

```bash
rm -rf artifacts/native-aot/feed-probe && mkdir -p artifacts/native-aot/feed-probe
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/native-aot/feed-probe
unzip -l artifacts/native-aot/feed-probe/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg | grep "tools/"
```

Expected: the listing includes `dtk.dbg`, `*.pdb`, `*.xml` and `libe_sqlite3.a` beside `dtk` and
`libe_sqlite3.so`. That is the failure to fix.

- [ ] **Step 2: Keep AOT RID packages to runtime files**

Before the `TrimmerRootAssembly` comment added in Task 4, add:

```xml
  <!--
    An AOT RID package carries the native dtk and libe_sqlite3 only. Tool packing globs the whole publish
    directory, so symbols (released as GitHub Release assets instead), managed .pdb and .xml files, and
    SQLitePCLRaw's static libe_sqlite3.a are kept out of it here. The `any` package is unaffected, and
    keeps its .pdb files for its .snupkg.
  -->
  <PropertyGroup Condition="'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' != '' and '$(RuntimeIdentifier)' != 'any'">
    <CopyOutputSymbolsToPublishDirectory>false</CopyOutputSymbolsToPublishDirectory>
  </PropertyGroup>
  <Target Name="KeepAotToolPackageRuntimeOnly" AfterTargets="ComputeResolvedFilesToPublishList"
          Condition="'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' != '' and '$(RuntimeIdentifier)' != 'any'">
    <ItemGroup>
      <ResolvedFileToPublish Remove="@(ResolvedFileToPublish)"
                             Condition="'%(ResolvedFileToPublish.Extension)' == '.pdb' or '%(ResolvedFileToPublish.Extension)' == '.xml' or '%(ResolvedFileToPublish.Extension)' == '.a'"/>
    </ItemGroup>
  </Target>
```

The `PropertyGroup` must come after the one that sets `PublishAot`.

- [ ] **Step 3: Pack again and check the contents**

Run (timeout `600000`):

```bash
rm -rf artifacts/native-aot/feed-probe src/DotnetTokenKiller.Cli/bin/Release/net10.0/linux-x64/publish && mkdir -p artifacts/native-aot/feed-probe
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/native-aot/feed-probe
unzip -l artifacts/native-aot/feed-probe/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg | grep "tools/"
ls src/DotnetTokenKiller.Cli/bin/Release/net10.0/linux-x64/native/
```

Expected: exactly `tools/any/linux-x64/DotnetToolSettings.xml`, `tools/any/linux-x64/dtk` and
`tools/any/linux-x64/libe_sqlite3.so`. The `native/` directory still holds `dtk` and `dtk.dbg`: the
symbols CI uploads.

- [ ] **Step 4: Check the fallback and pointer packs**

Run:

```bash
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version=0.0.0-local -o artifacts/native-aot/feed-probe
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/native-aot/feed-probe
ls artifacts/native-aot/feed-probe
unzip -p artifacts/native-aot/feed-probe/DotnetTokenKiller.0.0.0-local.nupkg tools/any/any/DotnetToolSettings.xml
unzip -l artifacts/native-aot/feed-probe/DotnetTokenKiller.any.0.0.0-local.nupkg | grep -c "tools/net10.0/any/dtk.dll"
```

Expected: four files, `DotnetTokenKiller.0.0.0-local.nupkg`, `DotnetTokenKiller.any.0.0.0-local.nupkg`,
`DotnetTokenKiller.any.0.0.0-local.snupkg` and `DotnetTokenKiller.linux-x64.0.0.0-local.nupkg`. The
settings XML lists five `RuntimeIdentifierPackage` entries (linux-x64, linux-arm64, osx-arm64,
win-x64, any). The last command prints `1`.

- [ ] **Step 5: Install the AOT tool from a local feed**

Create `artifacts/native-aot/feed.nuget.config` and run the pack-and-install block from "Local Package
Commands" (timeout `600000`).
Expected: ends with `installed the linux-x64 AOT package`.
`readlink artifacts/native-aot/tools/dtk` points into
`.store/dotnettokenkiller/0.0.0-local/dotnettokenkiller.linux-x64/`.

- [ ] **Step 6: Smoke the installed tool and the tracking-off loader probe**

Run:

```bash
BIN=artifacts/native-aot/tools/dtk
FIX=tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt
$BIN --version
H=$(mktemp -d)
DTK_CONFIG_PATH=$H/config.json DTK_DB_PATH=$H/tracking.db DTK_TEE_DIR=$H/logs $BIN config set tracking.enabled false > /dev/null
DTK_CONFIG_PATH=$H/config.json DTK_DB_PATH=$H/tracking.db DTK_TEE_DIR=$H/logs LD_DEBUG=files $BIN pipe build --exit-code 1 < $FIX 2>&1 | grep -c e_sqlite3
DTK_CONFIG_PATH=$H/config.json DTK_DB_PATH=$H/tracking.db DTK_TEE_DIR=$H/logs $BIN config set tracking.enabled true > /dev/null
DTK_CONFIG_PATH=$H/config.json DTK_DB_PATH=$H/tracking.db DTK_TEE_DIR=$H/logs LD_DEBUG=files $BIN pipe build --exit-code 1 < $FIX 2>&1 | grep -c e_sqlite3
```

Expected: `--version` prints `0.0.0-local`; the first count is `0` (tracking off loads no SQLite); the
second is greater than `0`.

- [ ] **Step 7: Run the gated warning check on the pack log**

Run: `DTK_AOT_PACK_LOG=$PWD/artifacts/native-aot/pack-linux-x64.log dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~AotWarningLogTests"`
Expected: 6 PASS.

- [ ] **Step 8: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore` (it does not touch csproj files, but keep
the habit). Commit message:

```
build: pack RID-specific Native AOT tool packages with an any fallback

ToolPackageRuntimeIdentifiers lists linux-x64, linux-arm64, osx-arm64,
win-x64 and any. AOT RID packages carry only the native dtk and
libe_sqlite3; native symbols stay under bin/<rid>/native for the release.
```

---

### Task 6: Let the integration tests run against any dtk binary

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncherTests.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncher.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class DtkLauncher` with `internal const string TestBinaryVariable = "DTK_TEST_BINARY"`
  and `internal static (string Executable, string[] PrefixArguments) Resolve(string? testBinary, string dllPath)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncherTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

public sealed class DtkLauncherTests
{
    private const string DllPath = "/build/dtk.dll";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoBinary_StartsDllThroughDotnet(string? testBinary)
    {
        var (executable, prefix) = DtkLauncher.Resolve(testBinary, DllPath);

        executable.Should().Be("dotnet");
        prefix.Should().Equal(DllPath);
    }

    [Fact]
    public void Resolve_Binary_StartsItDirectly()
    {
        var (executable, prefix) = DtkLauncher.Resolve("  /tools/dtk  ", DllPath);

        executable.Should().Be("/tools/dtk");
        prefix.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Watch it fail**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests -c Release`
Expected: FAIL with CS0103 (`DtkLauncher` does not exist).

- [ ] **Step 3: Write the launcher**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/DtkLauncher.cs`:

```csharp
namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>Decides how integration tests start dtk: the JIT build through <c>dotnet</c>, or a given binary.</summary>
internal static class DtkLauncher
{
    /// <summary>The environment variable naming a dtk executable to test instead of the JIT build.</summary>
    internal const string TestBinaryVariable = "DTK_TEST_BINARY";

    /// <summary>Resolves the executable to start and the arguments that precede dtk's own.</summary>
    /// <param name="testBinary">The value of <see cref="TestBinaryVariable"/>, or <see langword="null"/>.</param>
    /// <param name="dllPath">The JIT build's <c>dtk.dll</c>.</param>
    /// <returns><c>dotnet</c> and <paramref name="dllPath"/> when no binary is given; the binary alone otherwise.</returns>
    internal static (string Executable, string[] PrefixArguments) Resolve(string? testBinary, string dllPath) =>
        string.IsNullOrWhiteSpace(testBinary) ? ("dotnet", [dllPath]) : (testBinary.Trim(), []);
}
```

- [ ] **Step 4: Route every dtk invocation through it**

In `IntegrationTestHelper.cs`, directly after the `DllPath` field, add:

```csharp

    /// <summary>How every dtk invocation starts: the JIT build, or the binary named by
    /// <c>DTK_TEST_BINARY</c> (CI points it at the installed Native AOT tool).</summary>
    private static readonly (string Executable, string[] PrefixArguments) Launcher =
        DtkLauncher.Resolve(Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable), DllPath);
```

Replace each of the six occurrences of `RunProcessAsync("dotnet", [DllPath, .. args]` with
`RunProcessAsync(Launcher.Executable, [.. Launcher.PrefixArguments, .. args]` (in `RunDtkAsync`,
`RunDtkWithDbAsync`, `RunDtkWithStdinAsync`, both `RunDtkInDirAsync` overloads and
`RunDtkWithStdinInDirAsync`). Leave `RunDotnetAsync` alone: it runs the real SDK.

In `StartDetached`, replace:

```csharp
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args)
```

with:

```csharp
        var psi = new ProcessStartInfo(Launcher.Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in Launcher.PrefixArguments.Concat(args))
```

Then `grep -n "DllPath" tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs`
must show only the field and the `Launcher` initializer.

- [ ] **Step 5: Run the launcher tests and a sample of process-spawning classes under JIT**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests -c Release -p:Version=0.0.0-local`
Then: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DtkLauncherTests|FullyQualifiedName~SpectreBuiltInCommandTests|FullyQualifiedName~PipeIntegrationTests|FullyQualifiedName~TeeDurabilityTests|FullyQualifiedName~LogIntegrationTests"`
Expected: all PASS (21 tests when prototyped).

- [ ] **Step 6: Run the same classes against the installed AOT tool**

The tool from Task 5 must exist (`artifacts/native-aot/tools/dtk`); if it does not, run the "Local
Package Commands" block first.
Run: `DTK_TEST_BINARY=$PWD/artifacts/native-aot/tools/dtk dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DtkLauncherTests|FullyQualifiedName~SpectreBuiltInCommandTests|FullyQualifiedName~PipeIntegrationTests|FullyQualifiedName~TeeDurabilityTests|FullyQualifiedName~LogIntegrationTests"`
Expected: all PASS, faster than under JIT.

- [ ] **Step 7: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
test: let the CLI integration tests run against a given dtk binary

DTK_TEST_BINARY makes IntegrationTestHelper start that executable instead
of dotnet dtk.dll, so CI can run the existing suite against the installed
Native AOT tool.
```

---

### Task 7: Add the JIT-vs-AOT parity tests

**Files:**
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCase.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParitySandbox.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityTests.cs`

**Interfaces:**
- Consumes: `AotParitySkip.AotBinaryVariable`, `AotParityTheoryAttribute`, `AotParityUnixTheoryAttribute` (Task 4).
- Produces: the test class `AotParityTests` with theories `Portable_MatchesJitBuildAsync(string caseName)`
  and `UnixOnly_MatchesJitBuildAsync(string caseName)`; CI filters on
  `FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests`.

- [ ] **Step 1: Copy the fixtures to the test output**

In the test csproj, directly after the `xunit.runner.json` `Content` item, add:

```xml
    <None Include="../DotnetTokenKiller.Application.Tests/Fixtures/*.txt" LinkBase="Fixtures" CopyToOutputDirectory="PreserveNewest"/>
```

- [ ] **Step 2: Write the case records**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCase.cs`:

```csharp
namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>One dtk invocation inside a parity case.</summary>
/// <param name="Arguments">Arguments for dtk. <c>{project}</c> is replaced with the sandbox's project
/// directory.</param>
/// <param name="StdinFixture">A fixture file name piped to standard input, or <see langword="null"/>
/// for an empty, closed standard input.</param>
internal sealed record ParityStep(string[] Arguments, string? StdinFixture = null);

/// <summary>A sequence of dtk invocations sharing one sandbox, compared as a whole.</summary>
/// <param name="Steps">The invocations, run in order.</param>
/// <param name="Arrange">Prepares the sandbox before the first step, or <see langword="null"/>.</param>
internal sealed record ParityCase(IReadOnlyList<ParityStep> Steps, Action<ParitySandbox>? Arrange = null);
```

- [ ] **Step 3: Write the sandbox**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParitySandbox.cs`:

```csharp
using System.Runtime.Versioning;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// The private world one side of a parity case runs in: a home directory, a project directory and
/// dtk's config, database and tee logs, all under <see cref="Root"/>.
/// </summary>
internal sealed class ParitySandbox
{
    private const string FakeDotnetDirectoryName = "fake-dotnet";

    internal ParitySandbox(string root)
    {
        Root = root;
        Home = Path.Combine(root, "home");
        Project = Path.Combine(root, "proj");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Project);
    }

    internal string Root { get; }

    internal string Home { get; }

    internal string Project { get; }

    internal string DatabasePath => Path.Combine(Home, "tracking.db");

    /// <summary>The directory holding a fake <c>dotnet</c>, or <see langword="null"/> when none was made.</summary>
    internal string? FakeDotnetDirectory { get; private set; }

    /// <summary>Writes <paramref name="text"/> to <paramref name="relativePath"/> under <see cref="Root"/>.</summary>
    /// <param name="relativePath">A path relative to <see cref="Root"/>, with <c>/</c> separators.</param>
    /// <param name="text">The file's content.</param>
    internal void WriteFile(string relativePath, string text)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Writes a POSIX shell script named <c>dotnet</c> that prints the fixture mapped to its first
    /// argument and exits with the mapped code. dtk starts the bare name <c>dotnet</c> through
    /// <c>PATH</c>, so putting the script's directory first on the child's <c>PATH</c> replaces the SDK.
    /// </summary>
    /// <param name="subcommands">The fixture file and exit code for each first argument.</param>
    [UnsupportedOSPlatform("windows")]
    internal void CreateFakeDotnet(IReadOnlyDictionary<string, (string Fixture, int ExitCode)> subcommands)
    {
        var directory = Path.Combine(Root, FakeDotnetDirectoryName);
        Directory.CreateDirectory(directory);

        var script = new System.Text.StringBuilder("#!/bin/sh\ncase \"$1\" in\n");
        foreach (var (subcommand, (fixture, exitCode)) in subcommands)
        {
            File.Copy(ParityRunner.FixturePath(fixture), Path.Combine(directory, fixture));

            // $0 carries the script's absolute path because dtk execs it after resolving PATH.
            script.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"  {subcommand}) cat \"${{0%/*}}/{fixture}\"; exit {exitCode} ;;\n");
        }

        script.Append("  *) echo \"fake dotnet: unexpected arguments: $*\" >&2; exit 99 ;;\nesac\n");

        var scriptPath = Path.Combine(directory, "dotnet");
        File.WriteAllText(scriptPath, script.ToString());
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        FakeDotnetDirectory = directory;
    }

    /// <summary>Whether <paramref name="relativePath"/> is state the comparison ignores.</summary>
    /// <param name="relativePath">A path relative to <see cref="Root"/>, with <c>/</c> separators.</param>
    internal static bool IsIgnored(string relativePath)
    {
        string[] ignoredPrefixes =
        [
            "home/tracking.db", // compared row by row instead; the file itself holds timestamps
            "home/.dotnet/", "home/.nuget/", "home/.local/", "home/.templateengine/", // the SDK's own first-run state
            FakeDotnetDirectoryName + "/",
        ];
        return ignoredPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 4: Write the runner**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>What one side of a parity case produced, with everything run-specific normalized away.</summary>
/// <param name="Steps">Each step's exit code and combined output, in order.</param>
/// <param name="Files">Every file the steps left under the sandbox, by normalized relative path.</param>
/// <param name="TrackingRows">The tracking database's rows, in insertion order.</param>
internal sealed record ParityResult(
    IReadOnlyList<string> Steps,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> TrackingRows);

/// <summary>Runs a parity case through the JIT build and through the binary in <c>DTK_AOT_BINARY</c>.</summary>
internal static partial class ParityRunner
{
    /// <summary>
    /// The JIT build's apphost, not <c>dotnet dtk.dll</c>: on Unix, <c>Process.Start</c> looks for a bare
    /// file name beside the running executable before searching <c>PATH</c>, so under the <c>dotnet</c>
    /// muxer dtk would find the real SDK before a fake <c>dotnet</c>. The apphost is also what the
    /// framework-dependent tool package runs.
    /// </summary>
    private static readonly string JitAppHost =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "dtk.exe" : "dtk");

    internal static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

    /// <summary>Runs <paramref name="parityCase"/> once per build, each in a fresh sandbox.</summary>
    /// <param name="parityCase">The case to run.</param>
    internal static async Task<(ParityResult Jit, ParityResult Aot)> RunBothAsync(ParityCase parityCase)
    {
        var aotBinary = Environment.GetEnvironmentVariable(AotParitySkip.AotBinaryVariable)!.Trim();
        var caseRoot = Path.Combine(Path.GetTempPath(), $"dtk-parity-{Guid.NewGuid():N}");
        var sandboxRoot = Path.Combine(caseRoot, "sandbox");
        try
        {
            // Both sides run at the same path, one after the other, so paths dtk prints (including
            // those a table wraps across lines, which no normalization can rejoin) are identical.
            var jit = await RunAsync(JitAppHost, new ParitySandbox(sandboxRoot), parityCase);
            Directory.Move(sandboxRoot, Path.Combine(caseRoot, "jit"));
            var aot = await RunAsync(aotBinary, new ParitySandbox(sandboxRoot), parityCase);
            return (jit, aot);
        }
        finally
        {
            try
            {
                Directory.Delete(caseRoot, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing the comparison over.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    private static async Task<ParityResult> RunAsync(string executable, ParitySandbox sandbox, ParityCase parityCase)
    {
        parityCase.Arrange?.Invoke(sandbox);

        var steps = new List<string>();
        foreach (var step in parityCase.Steps)
        {
            var (output, exitCode) = await RunStepAsync(executable, sandbox, step);
            steps.Add($"[{string.Join(' ', step.Arguments)}] exit {exitCode}\n{Normalize(output, sandbox)}");
        }

        return new ParityResult(steps, ReadFiles(sandbox), await ReadTrackingRowsAsync(sandbox));
    }

    private static async Task<(string Output, int ExitCode)> RunStepAsync(
        string executable, ParitySandbox sandbox, ParityStep step)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = sandbox.Project,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in step.Arguments)
        {
            psi.ArgumentList.Add(argument.Replace("{project}", sandbox.Project, StringComparison.Ordinal));
        }

        psi.Environment["DTK_CONFIG_PATH"] = Path.Combine(sandbox.Home, "config.json");
        psi.Environment["DTK_DB_PATH"] = sandbox.DatabasePath;
        psi.Environment["DTK_TEE_DIR"] = Path.Combine(sandbox.Home, "logs");
        psi.Environment["HOME"] = sandbox.Home;
        psi.Environment["USERPROFILE"] = sandbox.Home;
        psi.Environment["XDG_CONFIG_HOME"] = Path.Combine(sandbox.Home, ".config");
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        if (sandbox.FakeDotnetDirectory is not null)
        {
            psi.Environment["PATH"] = sandbox.FakeDotnetDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");

        if (step.StdinFixture is not null)
        {
            await process.StandardInput.WriteAsync(await File.ReadAllTextAsync(FixturePath(step.StdinFixture)));
        }

        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        return (await stdout + await stderr, process.ExitCode);
    }

    private static Dictionary<string, string> ReadFiles(ParitySandbox sandbox)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(sandbox.Root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sandbox.Root, path).Replace('\\', '/');
            if (ParitySandbox.IsIgnored(relative))
            {
                continue;
            }

            files[Normalize(relative, sandbox)] = Normalize(File.ReadAllText(path), sandbox);
        }

        return files;
    }

    private static async Task<List<string>> ReadTrackingRowsAsync(ParitySandbox sandbox)
    {
        if (!File.Exists(sandbox.DatabasePath))
        {
            return [];
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sandbox.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        // Everything but id, timestamp and execution_time_ms, which differ between any two runs.
        command.CommandText = """
                              SELECT command, project_path, input_tokens, output_tokens, saved_tokens,
                                     savings_percentage, success, outcome, source
                              FROM commands ORDER BY id
                              """;

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fields = Enumerable.Range(0, reader.FieldCount)
                .Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
            rows.Add(Normalize(string.Join('|', fields), sandbox));
        }

        return rows;
    }

    /// <summary>Replaces the sandbox root, timestamps, durations and log-file stamps with fixed tokens.</summary>
    /// <param name="text">Output, a file's content or a relative path.</param>
    /// <param name="sandbox">The sandbox whose root to replace.</param>
    internal static string Normalize(string text, ParitySandbox sandbox)
    {
        var root = sandbox.Root;
        string[] roots = OperatingSystem.IsMacOS()
            ? ["/private" + root, root] // macOS temp paths can surface through the /private/var symlink
            : [root.Replace("\\", @"\\", StringComparison.Ordinal), root.Replace('\\', '/'), root];

        foreach (var form in roots)
        {
            text = text.Replace(form, "<ROOT>", StringComparison.Ordinal);
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = TimestampRegex().Replace(text, "<TIMESTAMP>");
        text = TimeSpanRegex().Replace(text, "<TIMESPAN>");
        text = DurationRegex().Replace(text, "<DURATION>");
        text = ExportElapsedRegex().Replace(text, "<ELAPSED>,");
        return LogStampRegex().Replace(text, "<LOG>_");
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}(\.\d+)?\b")]
    private static partial Regex TimeSpanRegex();

    [GeneratedRegex(@"\b\d+(\.\d+)?\s?(ms|s)\b")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"\b\d+\.\d{2},(?=[01],)")]
    private static partial Regex ExportElapsedRegex();

    [GeneratedRegex(@"\b\d{10,}_[0-9a-f]{32}_")]
    private static partial Regex LogStampRegex();
}
```

- [ ] **Step 5: Write the cases**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`:

```csharp
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Every command family, as deterministic inputs: fixtures on stdin, a fake <c>dotnet</c>, or state the
/// case writes first. Real builds stay out, because their output depends on restore and build state.
/// </summary>
internal static class ParityCases
{
    private const string RtkHookSettings =
        """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook claude"}]}]}}""";

    private static readonly Dictionary<string, ParityCase> Portable = new(StringComparer.Ordinal)
    {
        ["version"] = Steps(["--version"]),
        ["help"] = Steps(["--help"]),
        ["pipe-build"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["pipe", "build"], "dotnet_build_warnings.txt"),
            new ParityStep(["pipe", "build", "--vv", "--show-log"], "dotnet_build_success.txt"),
        ]),
        ["pipe-test"] = new ParityCase(
        [
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["pipe", "test"], "dotnet_test_all_pass.txt"),
        ]),
        ["pipe-restore"] = new ParityCase([new ParityStep(["pipe", "restore"], "dotnet_restore_raw.txt")]),
        ["pipe-clean"] = new ParityCase([new ParityStep(["pipe", "clean"], "dotnet_clean_raw.txt")]),
        ["pipe-format"] = new ParityCase(
            [new ParityStep(["pipe", "format", "--exit-code", "2"], "dotnet_format_violations_raw.txt")]),
        ["pipe-list-package"] = new ParityCase(
            [new ParityStep(["pipe", "list", "package"], "dotnet_list_package_outdated_raw.txt")]),
        ["gain"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["pipe", "restore"], "dotnet_restore_raw.txt"),
            new ParityStep(["gain"]),
            new ParityStep(["gain", "--json"]),
            new ParityStep(["gain", "--coverage"]),
            new ParityStep(["gain", "--export", "csv"]),
            new ParityStep(["gain", "--days", "7", "--command", "build"]),
        ]),
        ["log"] = new ParityCase(
        [
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["log", "--list", "--all"]),
            new ParityStep(["log", "--all", "--lines", "5"]),
        ]),
        ["config"] = Steps(
            ["config", "show"],
            ["config", "set", "display.emoji", "false"],
            ["config", "set", "tracking.enabled", "maybe"],
            ["config", "show"]),
        ["doctor"] = Steps(["doctor"]),
        ["completion"] = Steps(
            ["completion", "bash"], ["completion", "zsh"], ["completion", "fish"], ["completion", "powershell"]),
        ["integrate-project"] = new ParityCase(
        [
            .. new[] { "claude", "copilot", "copilot-cli", "gemini", "cursor", "windsurf", "aider", "jetbrains" }
                .Select(provider => new ParityStep(["integrate", provider, "--dir", "{project}"])),
            new ParityStep(["integrate", "claude", "--dir", "{project}"]),
        ], ArrangeRtk),
        ["spectre-built-ins"] = Steps(["cli", "version"], ["cli", "explain"], ["cli", "opencli"], ["--help-dump-opencli"]),
        ["passthrough"] = Steps(["dotnet", "--version"]),
        ["unknown-command"] = Steps(["frobnicate"]),
        ["reset"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["reset", "--force", "--all"]),
            new ParityStep(["gain"]),
        ]),
    };

    private static readonly Dictionary<string, ParityCase> UnixOnly = new(StringComparer.Ordinal)
    {
        // On Windows, USERPROFILE does not move Environment.SpecialFolder.UserProfile, so a global
        // install would write into the runner's real profile.
        ["integrate-global"] = new ParityCase(
        [
            new ParityStep(["integrate", "claude", "--global"]),
            new ParityStep(["integrate", "copilot-cli", "--global"]),
        ], ArrangeRtk),
        ["wrapped"] = new ParityCase(
        [
            new ParityStep(["dotnet", "build"]),
            new ParityStep(["dotnet", "test"]),
            new ParityStep(["dotnet", "restore"]),
            new ParityStep(["dotnet", "clean"]),
            new ParityStep(["dotnet", "format"]),
            new ParityStep(["dotnet", "list", "package"]),
        ], ArrangeFakeDotnet),
    };

    public static TheoryData<string> PortableNames => [.. Portable.Keys];

    public static TheoryData<string> UnixOnlyNames => [.. UnixOnly.Keys];

    internal static ParityCase Get(string name) => Portable.TryGetValue(name, out var parityCase) ? parityCase : UnixOnly[name];

    private static ParityCase Steps(params string[][] invocations) =>
        new([.. invocations.Select(arguments => new ParityStep(arguments))]);

    private static void ArrangeRtk(ParitySandbox sandbox)
    {
        sandbox.WriteFile("home/.config/rtk/config.toml", "[hooks]\nexclude_commands = [\"git\"]\n");
        sandbox.WriteFile("proj/.claude/settings.json", RtkHookSettings);
        sandbox.WriteFile("home/.claude/settings.json", RtkHookSettings);
    }

    private static void ArrangeFakeDotnet(ParitySandbox sandbox)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake dotnet is a POSIX shell script.");
        }

        sandbox.CreateFakeDotnet(new Dictionary<string, (string Fixture, int ExitCode)>(StringComparer.Ordinal)
        {
            ["build"] = ("dotnet_build_errors.txt", 1),
            ["test"] = ("dotnet_test_failures.txt", 1),
            ["restore"] = ("dotnet_restore_raw.txt", 0),
            ["clean"] = ("dotnet_clean_raw.txt", 0),
            ["format"] = ("dotnet_format_violations_raw.txt", 2),
            ["list"] = ("dotnet_list_package_outdated_raw.txt", 0),
        });
    }
}
```

- [ ] **Step 6: Write the test class**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityTests.cs`:

```csharp
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Runs every command family through the JIT build and through the binary named by
/// <c>DTK_AOT_BINARY</c>, and requires the same exit codes, output, written files and tracking rows.
/// Spectre.Console.Cli does not support Native AOT; this is what makes dtk's use of it tested rather
/// than hoped for. Skipped unless <c>DTK_AOT_BINARY</c> is set.
/// </summary>
public sealed class AotParityTests
{
    [AotParityTheory]
    [MemberData(nameof(ParityCases.PortableNames), MemberType = typeof(ParityCases))]
    public Task Portable_MatchesJitBuildAsync(string caseName) => AssertParityAsync(caseName);

    [AotParityUnixTheory]
    [MemberData(nameof(ParityCases.UnixOnlyNames), MemberType = typeof(ParityCases))]
    public Task UnixOnly_MatchesJitBuildAsync(string caseName) => AssertParityAsync(caseName);

    private static async Task AssertParityAsync(string caseName)
    {
        var (jit, aot) = await ParityRunner.RunBothAsync(ParityCases.Get(caseName));

        aot.Steps.Should().Equal(jit.Steps, "every step's exit code and output must match the JIT build");
        aot.Files.Should().BeEquivalentTo(jit.Files, "the same files, with the same content, must be written");
        aot.TrackingRows.Should().Equal(jit.TrackingRows, "token counts and outcomes must be recorded identically");
        jit.Steps.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 7: Build and confirm the suite skips without a binary**

Run: `dtk dotnet build DotnetTokenKiller.slnx -c Release -p:Version=0.0.0-local`
Then: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests"`
Expected: 20 skipped, 0 failed.

- [ ] **Step 8: Run parity against the installed AOT tool**

If `artifacts/native-aot/tools/dtk` does not exist, or anything under `src/` changed since it was
installed, re-run the "Local Package Commands" block first (it also rebuilds the tests with
`-p:Version=0.0.0-local`).
Run: `DTK_AOT_BINARY=$PWD/artifacts/native-aot/tools/dtk dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot"`
Expected: 20 parity cases PASS (18 portable, 2 Unix-only) and 5 warning-log tests PASS, 1 skipped.
The prototype took about 12 seconds.

- [ ] **Step 9: Show the suite can fail**

Run: `DTK_AOT_BINARY=/bin/true dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests"`
Expected: FAIL on every case (`/bin/true` prints nothing and writes nothing). This confirms the
comparison compares.

- [ ] **Step 10: Run parity against the `any` fallback**

Run:

```bash
rm -rf artifacts/native-aot/feed-any artifacts/native-aot/tools-any ~/.nuget/packages/dotnettokenkiller/0.0.0-local ~/.nuget/packages/dotnettokenkiller.any/0.0.0-local
mkdir -p artifacts/native-aot/feed-any
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version=0.0.0-local -o artifacts/native-aot/feed-any
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:ToolPackageRuntimeIdentifiers=any -p:Version=0.0.0-local -o artifacts/native-aot/feed-any
```

Create `artifacts/native-aot/feed-any.nuget.config` with the Write tool: the same content as
`feed.nuget.config` with the source path ending in `artifacts/native-aot/feed-any`. Then run:

```bash
dotnet tool install --tool-path artifacts/native-aot/tools-any --configfile artifacts/native-aot/feed-any.nuget.config DotnetTokenKiller --version 0.0.0-local
test -d artifacts/native-aot/tools-any/.store/dotnettokenkiller/0.0.0-local/dotnettokenkiller.any && echo "installed the any package"
```

Then: `DTK_AOT_BINARY=$PWD/artifacts/native-aot/tools-any/dtk dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests"`
Expected: `installed the any package`, then 20 PASS.

- [ ] **Step 11: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
test: compare the Native AOT tool with the JIT build across every command

AotParityTests runs pipe, gain, log, config, doctor, completion,
integrate, Spectre's built-ins, passthrough, wrapped commands and reset
through both builds and requires identical exit codes, output, written
files and tracking rows. Skipped unless DTK_AOT_BINARY is set.
```

---

### Task 8: Pack, install and test every package in CI

**Files:**
- Create: `.github/workflows/aot-package.yml`
- Create: `.github/workflows/fallback-package.yml`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the test filters and environment variables from Tasks 4, 6 and 7.
- Produces: reusable workflows `aot-package.yml` (inputs `rid`, `runner`, `version`,
  `run-integration-suite`; artifacts `package-<rid>` and `symbols-<rid>`) and `fallback-package.yml`
  (input `version`; artifact `package-any`), which Task 9 calls.

- [ ] **Step 1: Install actionlint**

Run: `python3 -m venv /tmp/alint-venv && /tmp/alint-venv/bin/pip install -q actionlint-py && /tmp/alint-venv/bin/actionlint --version`
Expected: prints a version (1.7.12 when this plan was written). Then
`/tmp/alint-venv/bin/actionlint .github/workflows/ci.yml` passes on the unchanged file.

- [ ] **Step 2: Write the AOT reusable workflow**

Create `.github/workflows/aot-package.yml` with the Write tool:

```yaml
name: AOT package

# Packs, installs and tests the Native AOT tool package for one runtime identifier. Called by CI on
# every push and pull request, and by Publish for a release, so a release is built and checked
# exactly the way every PR is.

on:
  workflow_call:
    inputs:
      rid:
        description: Runtime identifier to pack, e.g. linux-x64. Must be native to the runner's OS.
        required: true
        type: string
      runner:
        description: GitHub-hosted runner label that runs the RID's binary natively.
        required: true
        type: string
      version:
        description: Package version, passed to the build and to both packs.
        required: true
        type: string
      run-integration-suite:
        description: Also run the whole CLI integration suite against the installed binary.
        required: false
        type: boolean
        default: false

permissions:
  contents: read

jobs:
  package:
    name: ${{ inputs.rid }}
    runs-on: ${{ inputs.runner }}
    env:
      DOTNET_NOLOGO: '1'
      DOTNET_CLI_TELEMETRY_OPTOUT: '1'
      VERSION: ${{ inputs.version }}
      RID: ${{ inputs.rid }}
    defaults:
      run:
        shell: bash

    steps:
      - uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Install Native AOT prerequisites
        if: runner.os == 'Linux'
        run: sudo apt-get update && sudo apt-get install -y clang zlib1g-dev

      # The parity tests compare against this JIT build, so it must carry the packs' version or
      # `--version` differs between the two.
      - name: Build
        run: dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$VERSION"

      - name: Pack the AOT package
        run: >-
          dotnet pack src/DotnetTokenKiller.Cli -c Release -r "$RID"
          -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/feed
          "-flp:LogFile=artifacts/pack-$RID.log;Verbosity=minimal"

      - name: Pack the pointer package
        run: dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/feed

      - name: Install the tool from the local feed
        run: |
          feed="$(pwd)/artifacts/feed"
          cat > artifacts/feed.nuget.config <<EOF
          <?xml version="1.0" encoding="utf-8"?>
          <configuration>
            <packageSources>
              <clear />
              <add key="local" value="$feed" />
            </packageSources>
          </configuration>
          EOF
          tools="$RUNNER_TEMP/dtk-tools"
          dotnet tool install --tool-path "$tools" --configfile artifacts/feed.nuget.config DotnetTokenKiller --version "$VERSION"

          version_lower="$(echo "$VERSION" | tr '[:upper:]' '[:lower:]')"
          store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.$RID"
          if [ ! -d "$store" ]; then
            echo "::error::Expected $store: the install did not pick the $RID package."
            ls -R "$tools/.store"
            exit 1
          fi

          binary="$tools/dtk"
          if [ "$RUNNER_OS" = "Windows" ]; then binary="$binary.exe"; fi
          echo "DTK_INSTALLED=$binary" >> "$GITHUB_ENV"

      - name: Check the AOT warnings and parity
        run: >-
          DTK_AOT_PACK_LOG="$(pwd)/artifacts/pack-$RID.log" DTK_AOT_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build
          --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot" --verbosity normal

      - name: Run the integration suite against the installed binary
        if: inputs.run-integration-suite
        run: >-
          DTK_TEST_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --verbosity normal

      - name: Upload the package
        uses: actions/upload-artifact@v7
        with:
          name: package-${{ inputs.rid }}
          path: artifacts/feed/DotnetTokenKiller.${{ inputs.rid }}.*.nupkg
          if-no-files-found: error
          retention-days: 14

      - name: Upload the native symbols
        uses: actions/upload-artifact@v7
        with:
          name: symbols-${{ inputs.rid }}
          path: |
            src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.dbg
            src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.pdb
            src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.dSYM
          if-no-files-found: error
          retention-days: 14
```

Inputs reach shell steps only through `env` (`VERSION`, `RID`), never through `${{ }}` inside `run`,
which keeps the workflow free of expression injection.

- [ ] **Step 3: Write the fallback reusable workflow**

Create `.github/workflows/fallback-package.yml` with the Write tool:

```yaml
name: Fallback package

# Packs the framework-dependent `any` tool package, the one every runtime identifier without a
# Native AOT package installs, then installs and tests it. Called by CI and by Publish.

on:
  workflow_call:
    inputs:
      version:
        description: Package version, passed to the build and to both packs.
        required: true
        type: string

permissions:
  contents: read

jobs:
  package:
    name: any
    runs-on: ubuntu-latest
    env:
      DOTNET_NOLOGO: '1'
      DOTNET_CLI_TELEMETRY_OPTOUT: '1'
      VERSION: ${{ inputs.version }}
    defaults:
      run:
        shell: bash

    steps:
      - uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Build
        run: dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$VERSION"

      - name: Pack the fallback package
        run: dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version="$VERSION" -o artifacts/feed

      # The real pointer lists all five RIDs, so on this linux-x64 runner it would install the AOT
      # package. A check-only pointer that lists just `any` forces the fallback. It is never uploaded.
      - name: Pack a check-only pointer that lists only the fallback
        run: >-
          dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false
          -p:ToolPackageRuntimeIdentifiers=any -p:Version="$VERSION" -o artifacts/check-feed

      - name: Install the fallback from the local feed
        run: |
          cp artifacts/feed/DotnetTokenKiller.any.*.nupkg artifacts/check-feed/
          feed="$(pwd)/artifacts/check-feed"
          cat > artifacts/check-feed.nuget.config <<EOF
          <?xml version="1.0" encoding="utf-8"?>
          <configuration>
            <packageSources>
              <clear />
              <add key="local" value="$feed" />
            </packageSources>
          </configuration>
          EOF
          tools="$RUNNER_TEMP/dtk-tools"
          dotnet tool install --tool-path "$tools" --configfile artifacts/check-feed.nuget.config DotnetTokenKiller --version "$VERSION"

          version_lower="$(echo "$VERSION" | tr '[:upper:]' '[:lower:]')"
          store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.any"
          if [ ! -d "$store" ]; then
            echo "::error::Expected $store: the install did not pick the any package."
            ls -R "$tools/.store"
            exit 1
          fi

          echo "DTK_INSTALLED=$tools/dtk" >> "$GITHUB_ENV"

      - name: Check parity
        run: >-
          DTK_AOT_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build
          --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests" --verbosity normal

      - name: Upload the package
        uses: actions/upload-artifact@v7
        with:
          name: package-any
          path: |
            artifacts/feed/DotnetTokenKiller.any.*.nupkg
            artifacts/feed/DotnetTokenKiller.any.*.snupkg
          if-no-files-found: error
          retention-days: 14
```

- [ ] **Step 4: Call both from CI**

Append to the `jobs:` map of `.github/workflows/ci.yml`, after the `build` job (keep one blank line
between jobs):

```yaml

  aot:
    name: AOT (${{ matrix.rid }})
    strategy:
      fail-fast: false
      matrix:
        include:
          - rid: linux-x64
            runner: ubuntu-latest
            run-integration-suite: true
          - rid: linux-arm64
            runner: ubuntu-24.04-arm
            run-integration-suite: false
          - rid: osx-arm64
            runner: macos-latest
            run-integration-suite: false
          - rid: win-x64
            runner: windows-latest
            run-integration-suite: true
    uses: ./.github/workflows/aot-package.yml
    with:
      rid: ${{ matrix.rid }}
      runner: ${{ matrix.runner }}
      version: 0.0.0-ci
      run-integration-suite: ${{ matrix.run-integration-suite }}

  fallback:
    name: Fallback (any)
    uses: ./.github/workflows/fallback-package.yml
    with:
      version: 0.0.0-ci
```

- [ ] **Step 5: Lint**

Run: `/tmp/alint-venv/bin/actionlint .github/workflows/*.yml`
Expected: no output, exit 0.

- [ ] **Step 6: Rehearse the linux-x64 job locally**

The workflow cannot run before a push. Rehearse its commands on this machine, as the job runs them,
with version `0.0.0-ci`. Create `artifacts/native-aot/rehearse-aot.sh` with the Write tool:

```sh
#!/bin/sh
# Rehearses .github/workflows/aot-package.yml for linux-x64 on this machine.
set -eu
cd /home/cloudcli/projects/DotnetTokenKiller
VERSION=0.0.0-ci
RID=linux-x64
rm -rf artifacts/feed artifacts/pack-$RID.log artifacts/feed.nuget.config artifacts/native-aot/rehearse-tools \
  src/DotnetTokenKiller.Cli/bin/Release/net10.0/$RID/publish \
  "$HOME/.nuget/packages/dotnettokenkiller/$VERSION" "$HOME/.nuget/packages/dotnettokenkiller.$RID/$VERSION"
dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$VERSION" > artifacts/native-aot/rehearse-build.log
dotnet pack src/DotnetTokenKiller.Cli -c Release -r "$RID" -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/feed \
  "-flp:LogFile=artifacts/pack-$RID.log;Verbosity=minimal" > /dev/null
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/feed > /dev/null
feed="$(pwd)/artifacts/feed"
printf '<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key="local" value="%s" />\n  </packageSources>\n</configuration>\n' "$feed" > artifacts/feed.nuget.config
tools="$(pwd)/artifacts/native-aot/rehearse-tools"
dotnet tool install --tool-path "$tools" --configfile artifacts/feed.nuget.config DotnetTokenKiller --version "$VERSION"
test -d "$tools/.store/dotnettokenkiller/$VERSION/dotnettokenkiller.$RID"
DTK_AOT_PACK_LOG="$(pwd)/artifacts/pack-$RID.log" DTK_AOT_BINARY="$tools/dtk" \
  dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build \
  --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot"
ls src/DotnetTokenKiller.Cli/bin/Release/net10.0/$RID/native/dtk.dbg artifacts/feed/DotnetTokenKiller.$RID.*.nupkg
echo "rehearsal passed"
```

Run: `sh artifacts/native-aot/rehearse-aot.sh 2>&1 | tail -8` (timeout `600000`).
Expected: the test run reports 26 passed and 0 failed (20 parity, 6 warning-log), then the `ls` line,
then `rehearsal passed`. The script is not rewritten by the hook, because the hook only edits Bash
command text, not the files it runs. The integration-suite step (`run-integration-suite`) is not
rehearsed: it spawns real builds, which this machine cannot run (see Global Constraints). CI gates it.

- [ ] **Step 7: Commit**

Commit the three workflow files. Commit message:

```
ci: pack, install and test the Native AOT and fallback tool packages

A reusable aot-package workflow packs each RID on its native runner,
checks the pack log's warnings, installs the tool from a local feed and
runs the parity tests against it, plus the whole integration suite on
linux-x64 and win-x64. A fallback-package workflow does the same for the
framework-dependent any package.
```

---

### Task 9: Release every package, RID packages before the pointer

**Files:**
- Modify (rewrite): `.github/workflows/publish.yml`
- Modify: `Directory.Build.props:10-15`

**Interfaces:**
- Consumes: `aot-package.yml` and `fallback-package.yml` from Task 8, their artifacts `package-<rid>`,
  `symbols-<rid>` and `package-any`.
- Produces: nothing later tasks call.

- [ ] **Step 1: Rewrite the release workflow**

Replace the whole of `.github/workflows/publish.yml` with the Write tool:

```yaml
name: Publish

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: read

jobs:
  version:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.get_version.outputs.version }}
    steps:
      - name: Extract version from tag
        id: get_version
        env:
          TAG: ${{ github.ref_name }}
        run: |
          VERSION="${TAG#v}"
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "Version: $VERSION"

  test:
    runs-on: ubuntu-latest
    needs: version
    env:
      VERSION: ${{ needs.version.outputs.version }}
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release -p:Version="$VERSION"

      - name: Test
        run: dotnet test --no-build --configuration Release --verbosity normal

  pack-rid:
    needs: version
    strategy:
      fail-fast: false
      matrix:
        include:
          - rid: linux-x64
            runner: ubuntu-latest
            run-integration-suite: true
          - rid: linux-arm64
            runner: ubuntu-24.04-arm
            run-integration-suite: false
          - rid: osx-arm64
            runner: macos-latest
            run-integration-suite: false
          - rid: win-x64
            runner: windows-latest
            run-integration-suite: true
    uses: ./.github/workflows/aot-package.yml
    with:
      rid: ${{ matrix.rid }}
      runner: ${{ matrix.runner }}
      version: ${{ needs.version.outputs.version }}
      run-integration-suite: ${{ matrix.run-integration-suite }}

  pack-any:
    needs: version
    uses: ./.github/workflows/fallback-package.yml
    with:
      version: ${{ needs.version.outputs.version }}

  publish:
    runs-on: ubuntu-latest
    needs: [ version, test, pack-rid, pack-any ]
    permissions:
      id-token: write
      contents: write
      packages: write
    env:
      VERSION: ${{ needs.version.outputs.version }}
    steps:
      - uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Pack the pointer package
        run: dotnet pack src/DotnetTokenKiller.Cli --configuration Release -p:IncludeSymbols=false -p:Version="$VERSION" --output ./artifacts

      - name: Download the RID and fallback packages
        uses: actions/download-artifact@v8
        with:
          path: ./downloads

      - name: Gather packages and native symbols
        run: |
          set -euo pipefail
          for rid in linux-x64 linux-arm64 osx-arm64 win-x64 any; do
            cp "downloads/package-$rid/"* artifacts/
          done
          for rid in linux-x64 linux-arm64 osx-arm64 win-x64; do
            (cd "downloads/symbols-$rid" && zip -qr "$GITHUB_WORKSPACE/artifacts/dtk-$VERSION-$rid-symbols.zip" .)
          done
          ls -la artifacts

      - name: NuGet login (trusted publishing)
        uses: NuGet/login@v1
        id: login
        with:
          user: HandyS11

      # The pointer goes last. Until it is pushed, `dotnet tool install` keeps resolving the previous
      # version, whose RID packages all exist, so a push that fails part-way breaks no install.
      - name: Publish to NuGet.org
        env:
          NUGET_API_KEY: ${{ steps.login.outputs.NUGET_API_KEY }}
        run: |
          set -euo pipefail
          for rid in linux-x64 linux-arm64 osx-arm64 win-x64 any; do
            dotnet nuget push "artifacts/DotnetTokenKiller.$rid.$VERSION.nupkg" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
          done
          dotnet nuget push "artifacts/DotnetTokenKiller.$VERSION.nupkg" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate

      - name: Publish to GitHub Packages
        env:
          GITHUB_PACKAGES_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          OWNER: ${{ github.repository_owner }}
        run: |
          set -euo pipefail
          source="https://nuget.pkg.github.com/$OWNER/index.json"
          for rid in linux-x64 linux-arm64 osx-arm64 win-x64 any; do
            dotnet nuget push "artifacts/DotnetTokenKiller.$rid.$VERSION.nupkg" --source "$source" --api-key "$GITHUB_PACKAGES_TOKEN" --skip-duplicate
          done
          dotnet nuget push "artifacts/DotnetTokenKiller.$VERSION.nupkg" --source "$source" --api-key "$GITHUB_PACKAGES_TOKEN" --skip-duplicate

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v3
        with:
          files: |
            artifacts/*.nupkg
            artifacts/*.snupkg
            artifacts/*-symbols.zip
          generate_release_notes: true
          draft: false
          prerelease: false
```

Compared with today's workflow: the version reaches the build as `-p:Version` instead of a `sed` edit;
the `test` job keeps today's restore, build and full test; the tag comes in through `env`
(`TAG`), not `${{ }}` in the shell; permissions are elevated only on `publish`.

- [ ] **Step 2: Update the version comment**

In `Directory.Build.props`, replace:

```xml
    <!--
      Placeholder for local builds only. The Publish workflow (.github/workflows/publish.yml)
      overwrites this value from the pushed release tag (`VERSION=${GITHUB_REF_NAME#v}`) before
      packing, so the published package version always comes from the tag, not from this number.
    -->
```

with:

```xml
    <!--
      Placeholder for local builds only. The Publish workflow (.github/workflows/publish.yml) passes
      the pushed release tag's version (`v1.2.3` -> `-p:Version=1.2.3`) to every build and pack,
      and CI passes `0.0.0-ci`, so a published package's version always comes from the tag.
    -->
```

- [ ] **Step 3: Lint**

Run: `/tmp/alint-venv/bin/actionlint .github/workflows/*.yml` (reinstall with Task 8 Step 1 if
`/tmp/alint-venv` is gone).
Expected: no output, exit 0.

- [ ] **Step 4: Check the push loop's file names against real pack output**

The push loops assume `DotnetTokenKiller.<rid>.<version>.nupkg` and `DotnetTokenKiller.<version>.nupkg`.
Run: `ls artifacts/feed/` (left by Task 8's rehearsal).
Expected: `DotnetTokenKiller.0.0.0-ci.nupkg` and `DotnetTokenKiller.linux-x64.0.0.0-ci.nupkg`, matching
the loop's patterns with `VERSION=0.0.0-ci`.

- [ ] **Step 5: Build to confirm the props change**

Run: `dtk dotnet build DotnetTokenKiller.slnx -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

Commit message:

```
ci: release the RID-specific tool packages, RID packages before the pointer

Publish tests, packs the four AOT packages and the any fallback through
the same reusable workflows as CI, then pushes every RID package before
the pointer so a failed push never breaks an install, and attaches each
RID's native symbols to the GitHub Release. The version reaches builds as
-p:Version instead of a sed edit of Directory.Build.props.
```

---

### Task 10: Measure, document, and close the spec

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md:96-112`
- Modify: `src/DotnetTokenKiller.Cli/README.md:18-29`
- Modify: `docfx/articles/getting-started.md:12-30`
- Modify: `docs/superpowers/specs/2026-09-13-native-aot-design.md:1-2`

**Interfaces:**
- Consumes: everything above.
- Produces: the figures the final report and PR quote.

- [ ] **Step 1: Final JIT measurements**

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release`
Then the cold-start command with `<binary>` = `src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk`,
`<name>` = `final-jit-cold-start`, and `startup-extra.py` with the same binary and
`<name>` = `final-jit-extra`.

- [ ] **Step 2: Final AOT measurements**

Run the "Local Package Commands" block (fresh pack and install), then the cold-start command with
`<binary>` = `artifacts/native-aot/tools/dtk`, `<name>` = `final-aot-cold-start`, and
`startup-extra.py` with the same binary and `<name>` = `final-aot-extra`.
Expected: the cold-start header says the binary has no runtimeconfig.

Then repeat Task 5 Step 6's loader probe against `artifacts/native-aot/tools/dtk`.
Expected: `0` with tracking off and a positive count with tracking on.

- [ ] **Step 3: Compare**

Each scenario against its own baseline from Task 1:
- **Final JIT:** every median within 5 ms of its baseline. If one is not, re-run that measurement once
  at low load before reporting; if it still is not, report it as a regression with both figures.
- **Final AOT:** pipe and both wrapped overheads far below their JIT figures.
- **Tracking share under AOT:** pipe median minus tracking-off median.

- [ ] **Step 4: Update CLAUDE.md**

1. In the "Commands" code block, after the `tokenizer-load` lines, add:

```bash
# Publish the CLI as Native AOT for this machine (linux-x64 here; the native compile cannot cross OSes)
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot

# Pack the tool as CI does: the RID package (on its own OS), the pointer, and the framework-dependent fallback.
# A plain `dotnet pack` now builds only the pointer; IncludeSymbols=true breaks the pointer and RID packs.
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -o artifacts/feed

# Compare an AOT binary with the JIT build, and check a pack log's trim/AOT warnings (skipped unless set)
DTK_AOT_BINARY=/abs/path/to/dtk DTK_AOT_PACK_LOG=/abs/path/to/pack.log dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot"
```

2. After the "Benchmarks" section's `tokenizer-load` bullet, add a paragraph in the same style,
   filled with the figures from Steps 1-2 and today's date, for example:

```markdown
Native AOT, measured <date>, JIT (`bin/Release`) → AOT (linux-x64 tool package installed from a local
feed; the shim is a symlink to the binary): pipe <a> → <b> ms; wrapped overhead <c> → <d> ms (instant
child) and <e> → <f> ms (1000 ms child); `dtk --version` <g> → <h> ms; tracking off <i> → <j> ms. Under
AOT, tracking (tokenizer load, counting, SQLite) is <b − j> ms of the pipe figure.
```

3. Add a section after "Benchmarks":

```markdown
## Native AOT

The tool ships as RID-specific packages: native AOT for linux-x64, linux-arm64, osx-arm64 and win-x64,
and the framework-dependent `any` package everywhere else (`ToolPackageRuntimeIdentifiers` in the CLI
csproj). CI's `aot-package.yml` packs each RID on its own OS, installs the tool from a local feed and runs
`AotParityTests` against it; `publish.yml` pushes the RID packages before the pointer.

Spectre.Console.Cli does not support Native AOT. dtk keeps it under a contained exception
(docs/superpowers/specs/2026-09-13-native-aot-design.md): both `dtk` and `Spectre.Console.Cli` are
rooted, the only two trim/AOT suppressions are on `SpectreCommandApp.Create` (IL3050) and
`TypeRegistrar.Register` (IL2067), and the native compile's IL2104/IL3053/IL3000 from Spectre stay
warnings. Do not remove those suppressions by making `Register` a no-op: that breaks Spectre's
built-in `dtk cli …` commands (`SpectreBuiltInCommandTests`). Do not give a settings class a
dictionary, value-type array, nullable or converter option without first extending `AotParityTests`:
`CommandSettingsAotGuardTests` fails until you do.

`DTK_TEST_BINARY` runs the CLI integration suite against any dtk binary; `DTK_AOT_BINARY` enables the
parity tests; `DTK_AOT_PACK_LOG` checks a pack log's warnings. `IsAotCompatible` is on for the three
libraries, so a trim- or AOT-unsafe call fails the normal build.
```

- [ ] **Step 5: Update the install docs**

In `README.md`, directly after the install code block (the one containing
`dotnet tool install -g DotnetTokenKiller`), add:

```markdown
On Linux (x64 and arm64), macOS on Apple silicon and Windows x64, this installs a natively compiled
`dtk` that starts in milliseconds and needs no .NET runtime to run. Other platforms get the
framework-dependent build, which runs on the .NET 10 runtime.
```

Add the same paragraph in `src/DotnetTokenKiller.Cli/README.md` after its install code block, and in
`docfx/articles/getting-started.md` after the first `dotnet tool install -g DotnetTokenKiller` code
block.

- [ ] **Step 6: Close the spec's status line**

In `docs/superpowers/specs/2026-09-13-native-aot-design.md`, replace the first two lines (the
`**Status:**` paragraph) with:

```markdown
**Status:** Implemented — see [the plan](../plans/2026-09-13-native-aot.md). Amended while planning;
see "Amendments" at the end.
```

- [ ] **Step 7: Verify docs and the unaffected tests**

Run:
- `dtk dotnet build DotnetTokenKiller.slnx -c Release`
- `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~DocsBindingTests|FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~CommandSettingsAotGuardTests|FullyQualifiedName~DtkLauncherTests|FullyQualifiedName~AotWarningLogTests"`
- `dtk dotnet test tests/DotnetTokenKiller.Application.Tests -c Release --no-build` (it holds
  `SavingsBaselineTests`)
- `git diff develop --stat -- benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/`

Expected: all PASS; the last command prints nothing (baseline unchanged).

- [ ] **Step 8: Report the figures**

Report a table: scenario, baseline JIT, final JIT, final AOT (pipe; instant-child overhead;
1000 ms-child overhead and wall-clock; `--version`; tracking off), plus the tracking share under
AOT. Say whether the pipe and instant-child AOT figures leave enough dtk start-up (AOT tracking-off
figure) to reconsider starting setup from `Program.cs`: report only, do not implement.

- [ ] **Step 9: Format and commit**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`. Commit message:

```
docs: record the Native AOT figures and how to pack and test the AOT tool

CLAUDE.md gains the JIT and AOT cold-start figures, the pack sequence, the
parity and warning-check variables, and the Spectre.Console.Cli exception
nobody should "fix". The install docs say which platforms get a native
binary.
```

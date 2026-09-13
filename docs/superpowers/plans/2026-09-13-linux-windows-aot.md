# Linux Cross-Sysroot, musl and Windows-Fallback AOT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make dtk's Native AOT tool packages start on every Linux that .NET 10 supports (a glibc 2.27 floor,
plus new musl packages) with SQLite linked statically into the glibc binaries, move Windows to the
framework-dependent `any` package, and make CI prove all of it on every pull request.

**Architecture:** The four Linux RIDs pack in Microsoft's cross-build images through `docker run`, driven by
committed POSIX scripts in `eng/aot/` that run identically on a developer machine and in CI. The CLI csproj
links `libe_sqlite3.a` and a C `fcntl64` shim into the linux-x64 and linux-arm64 binaries. Each package is
installed and tested on a runner of its own architecture (inside the Alpine SDK image for musl). The glibc
packages also pass a `readelf` floor check and a Rocky Linux 8 smoke run. A gated macOS test replaces the
manual "tracking off loads no SQLite" check. The `any` package is tested on Windows from Git Bash, pwsh and cmd.

**Tech Stack:** net10.0, SDK 10.0.4xx, ILCompiler 10.0.12, SQLitePCLRaw.lib.e_sqlite3 3.53.3, xunit 2.9.3,
FluentAssertions 8, Docker, `mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-{amd64,arm64,amd64-musl,arm64-musl}`,
`mcr.microsoft.com/dotnet/sdk:10.0-alpine`, `rockylinux:8`, GitHub Actions (checkout v7, setup-dotnet v6,
upload-artifact v7, download-artifact v8).

**Spec:** [docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md](../specs/2026-09-13-linux-windows-aot-design.md).
Every code block, script and workflow in this plan was run in a throwaway clone before the plan was written:
all four Linux packs, the floor check (red and green), the Rocky 8 smoke (red and green), `test-package.sh`
natively for linux-x64 (37 of 37) and in Alpine for linux-musl-x64 (43 passed, 1 macOS-only skip), the
`ParityProcess` regression test (red with the old stream order, green with the new), the loader test's logic
(with `LD_DEBUG=files` against a dynamic build, then reverted to its macOS form), a local no-sysroot publish,
`dotnet format --verify-no-changes`, actionlint and shellcheck.

## Global Constraints

- **Token counts stay exact.** `SavingsBaselineTests` must pass and
  `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` must not change. Never run
  `update-baseline`.
- **No filter, tokenizer, tracking-schema or `src/` C# changes.** The only `src/` changes are the CLI csproj
  and the new `src/DotnetTokenKiller.Cli/Native/fcntl64.c`.
- **Exactly two trim/AOT suppressions exist**, as today: IL3050 on `SpectreCommandApp.Create` and IL2067 on
  `TypeRegistrar.Register`. Add none.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all`. Fix analyzer errors; never suppress.
  Test traps: RCS1141/RCS1140 (doc tags), CA1062, CA1812 (make test-local classes `abstract`), CA1034/CA1819
  (no public nested test types), IDE0028/IDE0306.
- **Copy the code blocks exactly.** If an analyzer or `dotnet format` still asks for a change, make the smallest
  change that satisfies it and say so in your report.
- **Formatting:** LF, no trailing whitespace, no BOM; 4-space indent for `.cs` and `.sh`, 2-space for XML, JSON
  and YAML. Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before every commit that touches `.cs`.
- **Scripts are POSIX `sh`** (`#!/bin/sh`, `set -eu`, no bash-isms), always invoked as `sh eng/aot/<name>.sh`,
  and committed executable (`git add --chmod=+x`). They must pass
  `/tmp/wprobe/venv/bin/shellcheck -s sh eng/aot/*.sh`. If that venv is missing, create it:
  `python3 -m venv /tmp/wprobe/venv && /tmp/wprobe/venv/bin/pip install actionlint-py shellcheck-py`.
- **Use `dtk dotnet …`** for build, test and format. `dotnet pack`, `dotnet publish`, `dotnet run`,
  `dotnet tool …` and `sh eng/aot/…` are not rewritten and are used as-is.
- **The Bash hook rewrites `dotnet build|test|restore|clean|format|list package` anywhere in a command,**
  including quoted strings and heredocs. Create and edit files (scripts, YAML, configs, docs) with the Write
  and Edit tools, never with `cat <<EOF`, `echo` or `sed -i` in Bash. Write every commit message to a file with
  the Write tool and commit with `git commit -F <file>`.
- **Commit messages end with** a blank line and the `Co-Authored-By:` trailer your own session's instructions
  specify.
- **The build-spawning CLI integration tests cannot pass on this machine.** Run
  `tests/DotnetTokenKiller.{Domain,Application,Infrastructure}.Tests` one project at a time, and in
  `tests/DotnetTokenKiller.Cli.IntegrationTests` only the classes a task names, with `--filter`, or through
  `eng/aot/test-package.sh` (which runs only the `Aot` namespace unless given `--integration-suite`; never
  pass that flag locally). CI gates the rest.
- **Docker:** these images are already pulled on this machine; do not delete them (the controller cleans up
  after the final review): the four `mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-*`
  images above, `mcr.microsoft.com/dotnet/sdk:10.0-alpine` and `rockylinux:8`. Every container that writes to
  the checkout runs as your user (the scripts pass `--user`); never leave root-owned files in the repository.
- **Run one pack at a time.** Packs for different RIDs share `src/DotnetTokenKiller.Cli/obj`.
- **Local packages use version `0.0.0-local`** and live under `artifacts/aot/` (git-ignored). Never commit
  anything under `artifacts/`. Never push, open a PR, or touch `~/.dotnet/tools`.
- **Another session may be active.** If files change under you without your doing, stop and report.

## File Structure

| File | Task | Responsibility |
|---|---|---|
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcess.cs` (new) | 1 | Run a child with piped streams without deadlocking |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcessTests.cs` (new) | 1 | Large-input regression test |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/UnixFactAttribute.cs` (new) | 1 | Fact skipped on Windows |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs` (modify) | 1 | `CreateStartInfo`, uses `ParityProcess` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs` (modify) | 2 | `AotParitySkip.MacOSReason`, `AotMacOSFactAttribute` |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParitySkipTests.cs` (modify) | 2 | `MacOSReason` unit tests |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs` (new) | 2 | Tracking off loads no SQLite (macOS) |
| `eng/aot/check-glibc-floor.sh` (new) | 3 | Fail on a GLIBC_ version above the floor |
| `eng/aot/pack-linux.sh` (new) | 3 | Pack a Linux RID in its cross-build image |
| `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (modify) | 3, 4, 5 | Static SQLite, shim target, RID list |
| `src/DotnetTokenKiller.Cli/Native/fcntl64.c` (new) | 3 | `fcntl64` for glibc < 2.28 |
| `eng/aot/smoke-old-glibc.sh` (new) | 4 | Start, filter and track on Rocky Linux 8 |
| `eng/aot/test-package.sh` (new) | 4 | Install a RID package and run the AOT tests |
| `.github/workflows/aot-package.yml` (rewrite) | 5 | `pack` and `test` jobs per RID |
| `.github/workflows/fallback-package.yml` (rewrite) | 5 | `any` on Linux and Windows, Windows shell smoke |
| `.github/workflows/ci.yml`, `.github/workflows/publish.yml` (modify) | 5 | Five-RID matrix |
| `CLAUDE.md`, `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/articles/getting-started.md` (modify) | 7 | Platforms, scripts, figures |
| `docs/superpowers/specs/2026-09-13-native-aot-design.md`, `docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md` (modify) | 7 | Pointer and status |

---

### Task 1: Drain child output before writing its input

The parity runner writes all of standard input before it starts reading standard output. A child that writes
while its input is still arriving (dtk does not today: `pipe` reads everything first) fills its output pipe and
blocks while the runner blocks writing. Extract the process handling, prove the deadlock with `cat`, fix the
order, and expose the hermetic start info that Task 2 reuses.

**Files:**

- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcess.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcessTests.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/UnixFactAttribute.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs` (usings; `RunStepAsync`)

**Interfaces:**

- Produces: `internal sealed record ProcessOutput(string Stdout, string Stderr, int ExitCode)`;
  `internal static Task<ProcessOutput> ParityProcess.RunAsync(ProcessStartInfo startInfo, string? stdin)`;
  `internal static ProcessStartInfo ParityRunner.CreateStartInfo(string executable, ParitySandbox sandbox, IEnumerable<string> arguments)`;
  `public sealed class UnixFactAttribute : FactAttribute` in namespace `DotnetTokenKiller.Cli.IntegrationTests.Helpers`.

- [ ] **Step 1: Create the helper with today's stream order**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcess.cs`. This first version keeps the
runner's current order (write input, then read output) so the next step's test can fail:

```csharp
using System.Diagnostics;
using System.Text;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>What a finished child process wrote and how it exited.</summary>
/// <param name="Stdout">Everything written to standard output.</param>
/// <param name="Stderr">Everything written to standard error.</param>
/// <param name="ExitCode">The exit code.</param>
internal sealed record ProcessOutput(string Stdout, string Stderr, int ExitCode);

/// <summary>Runs a child process to completion with piped standard streams.</summary>
internal static class ParityProcess
{
    /// <summary>
    /// Starts <paramref name="startInfo"/>, writes <paramref name="stdin"/> and closes standard input, and
    /// returns both output streams once the process exits. The output streams are drained before input is
    /// written: a child that writes while its input is still arriving would otherwise fill its output pipe
    /// (about 64 KB on Linux) and block, while this side blocks writing its input.
    /// </summary>
    /// <param name="startInfo">The process to start; its stream settings are overwritten.</param>
    /// <param name="stdin">Text for standard input, or <see langword="null"/> for an empty, closed input.</param>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    internal static async Task<ProcessOutput> RunAsync(ProcessStartInfo startInfo, string? stdin)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.StandardInputEncoding = Encoding.UTF8;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        return new ProcessOutput(await stdout, await stderr, process.ExitCode);
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/UnixFactAttribute.cs`:

```csharp
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>A fact that is skipped on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix only.";
        }
    }
}
```

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcessTests.cs`:

```csharp
using System.Diagnostics;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

public sealed class ParityProcessTests
{
    /// <summary>
    /// <c>cat</c> echoes its input as it reads it, so once its output pipe fills it stops reading. Writing all
    /// of standard input before draining standard output deadlocks well below this input's size.
    /// </summary>
    [UnixFact]
    public async Task RunAsync_ChildEchoesLargeInput_ReturnsItAsync()
    {
        var input = string.Concat(Enumerable.Repeat("0123456789abcdef0123456789abcdef0123456789abcdef012345678\n", 16_000));

        var output = await ParityProcess.RunAsync(new ProcessStartInfo("cat"), input)
            .WaitAsync(TimeSpan.FromSeconds(30));

        output.ExitCode.Should().Be(0);
        output.Stdout.Should().Be(input);
        output.Stderr.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests && dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~ParityProcessTests"`
Expected: FAIL after 30 s with `System.TimeoutException : The operation has timed out.` The blocked `cat`
exits when the test host does.

- [ ] **Step 4: Drain the output streams first**

In `ParityProcess.RunAsync`, move the two `ReadToEndAsync` lines above the input write, so the body after
`Process.Start` reads:

```csharp
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();

        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        return new ProcessOutput(await stdout, await stderr, process.ExitCode);
```

- [ ] **Step 5: Run it to verify it passes**

Run the Step 3 command again.
Expected: PASS in well under a second.

- [ ] **Step 6: Route the parity runner through the helper**

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs`, delete the line `using System.Text;`.
Replace the whole `RunStepAsync` method, from `private static async Task<(string Output, int ExitCode)> RunStepAsync(`
through its closing brace, with:

```csharp
    private static async Task<(string Output, int ExitCode)> RunStepAsync(
        string executable, ParitySandbox sandbox, ParityStep step)
    {
        var stdin = step.StdinFixture is null ? null : await File.ReadAllTextAsync(FixturePath(step.StdinFixture));
        var output = await ParityProcess.RunAsync(CreateStartInfo(executable, sandbox, step.Arguments), stdin);
        return (output.Stdout + output.Stderr, output.ExitCode);
    }

    /// <summary>
    /// A start for <paramref name="executable"/> confined to <paramref name="sandbox"/>: dtk's config, database
    /// and tee logs, the home directories and the fake <c>dotnet</c>, if any, all point into it.
    /// </summary>
    /// <param name="executable">The dtk binary to run.</param>
    /// <param name="sandbox">The sandbox to run in.</param>
    /// <param name="arguments">Arguments for dtk; <c>{project}</c> is replaced with the sandbox's project directory.</param>
    internal static ProcessStartInfo CreateStartInfo(string executable, ParitySandbox sandbox, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = sandbox.Project };

        foreach (var argument in arguments)
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

        return psi;
    }
```

The environment lines are the ones the old method already had, unchanged. Parity output stays exactly as
before: stdout followed by stderr.

- [ ] **Step 7: Build, format and run the affected classes**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests && dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes && dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~ParityProcessTests|FullyQualifiedName~AotParitySkipTests|FullyQualifiedName~AotWarningLogTests|FullyQualifiedName~AotParityTests"`
Expected: build clean; format reports nothing; `ParityProcessTests` and `AotParitySkipTests` pass; the
`AotParityTests` cases and the pack-log fact skip (no `DTK_AOT_BINARY` locally). Task 4 runs the parity cases
against real packages.

- [ ] **Step 8: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcess.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityProcessTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/UnixFactAttribute.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityRunner.cs
git commit -F <message file>
```

Message: `test: drain a parity child's output before writing its input`, with a body explaining the ~64 KB
pipe deadlock and that `cat` proves it.

---

### Task 2: Check on macOS that tracking off loads no SQLite

Sub-project 2's "tracking off loads no SQLite" was a manual `LD_DEBUG=files` check. Once the glibc binaries
link SQLite statically (Task 3), Linux cannot show a load at all, and musl has no loader trace. dyld prints
every image it loads when `DYLD_PRINT_LIBRARIES` is set, and the osx-arm64 AOT binary still loads
`libe_sqlite3.dylib` dynamically, so the check becomes a macOS test with a positive control. This machine
cannot run it (it skips here); the osx-arm64 CI job runs it with `DTK_AOT_REQUIRED=1`. Its logic was validated
in the prototype by swapping in `LD_DEBUG=files` on Linux: it passed against a dynamic build and its control
failed against the static one.

**Files:**

- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParitySkipTests.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs`

**Interfaces:**

- Consumes: `ParityProcess.RunAsync`, `ProcessOutput`, `ParityRunner.CreateStartInfo` (Task 1);
  `ParityRunner.FixturePath(string)`, `ParitySandbox`, `AotParitySkip.ReadRequired`,
  `AotParitySkip.AotBinaryVariable`, `AotParitySkip.RequiredVariable` (existing).
- Produces: `internal static string? AotParitySkip.MacOSReason(string? aotBinary, string? required, bool isMacOS)`;
  `public sealed class AotMacOSFactAttribute : FactAttribute`.

- [ ] **Step 1: Write the failing gating tests**

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParitySkipTests.cs`, add after the last test
(`Reason_UnixOnlyNotOnWindows_Runs`):

```csharp
    [Theory]
    [InlineData(Binary, null)]
    [InlineData(null, "1")]
    public void MacOSReason_NotMacOS_Skips(string? aotBinary, string? required)
    {
        AotParitySkip.MacOSReason(aotBinary, required, isMacOS: false).Should().Contain("macOS only",
            "Linux and Windows CI set DTK_AOT_REQUIRED=1 too, and must not run a dyld check");
    }

    [Fact]
    public void MacOSReason_MacOSWithBinary_Runs()
    {
        AotParitySkip.MacOSReason(Binary, required: null, isMacOS: true).Should().BeNull();
    }

    [Fact]
    public void MacOSReason_MacOSBinaryBlankAndNotRequired_Skips()
    {
        AotParitySkip.MacOSReason(aotBinary: null, required: null, isMacOS: true)
            .Should().Contain(AotParitySkip.AotBinaryVariable);
    }

    [Fact]
    public void MacOSReason_MacOSBinaryBlankAndRequired_Runs()
    {
        AotParitySkip.MacOSReason(aotBinary: null, required: "1", isMacOS: true).Should().BeNull(
            "a missing binary must fail the macOS job, not skip it, when DTK_AOT_REQUIRED is 1");
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests`
Expected: FAIL to compile with CS0117 (`'AotParitySkip' does not contain a definition for 'MacOSReason'`).

- [ ] **Step 3: Add the gating**

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs`, inside `AotParitySkip`, add
directly above the `/// <summary>Whether <c>DTK_AOT_REQUIRED</c> demands the AOT inputs.</summary>` comment:

```csharp
    /// <summary>Returns the skip reason for a macOS-only AOT test, or <see langword="null"/> when it should run.</summary>
    /// <param name="aotBinary">The value of <c>DTK_AOT_BINARY</c>.</param>
    /// <param name="required">The value of <c>DTK_AOT_REQUIRED</c>.</param>
    /// <param name="isMacOS">Whether the tests run on macOS.</param>
    /// <returns>The reason to skip, or <see langword="null"/>.</returns>
    internal static string? MacOSReason(string? aotBinary, string? required, bool isMacOS) =>
        isMacOS
            ? Reason(unixOnly: false, aotBinary, required, isWindows: false)
            : "macOS only: needs dyld's DYLD_PRINT_LIBRARIES and a dynamically loaded SQLite.";

```

In the same file, add directly above the doc comment of `AotPackLogFactAttribute`
(`/// A fact that runs only when <c>DTK_AOT_PACK_LOG</c> names the log …`, including its `/// <summary>` line):

```csharp
/// <summary>
/// A fact that runs only on macOS, and there only when <c>DTK_AOT_BINARY</c> is set or <c>DTK_AOT_REQUIRED</c>
/// is <c>1</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotMacOSFactAttribute : FactAttribute
{
    public AotMacOSFactAttribute()
    {
        Skip = AotParitySkip.MacOSReason(
            Environment.GetEnvironmentVariable(AotParitySkip.AotBinaryVariable),
            Environment.GetEnvironmentVariable(AotParitySkip.RequiredVariable),
            OperatingSystem.IsMacOS());
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

```

- [ ] **Step 4: Run the gating tests to verify they pass**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests && dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~AotParitySkipTests"`
Expected: PASS, including the five new cases.

- [ ] **Step 5: Add the loader test**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs`:

```csharp
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// With tracking off, dtk must not load SQLite: that start-up saving is sub-project 2's, and nothing else
/// notices if a change loses it. dyld reports every image it loads when <c>DYLD_PRINT_LIBRARIES</c> is set, so
/// this runs on macOS, where the AOT binary still loads <c>libe_sqlite3.dylib</c> dynamically; the glibc
/// binaries link SQLite statically and musl has no loader trace. Skipped unless <c>DTK_AOT_BINARY</c> is set;
/// with <c>DTK_AOT_REQUIRED=1</c> a missing binary fails instead.
/// </summary>
public sealed class SqliteLoaderTests
{
    private const string SqliteLibrary = "libe_sqlite3";

    [AotMacOSFact]
    public async Task PipeBuild_TrackingOff_DoesNotLoadSqliteAsync()
    {
        var binary = AotParitySkip.ReadRequired(AotParitySkip.AotBinaryVariable);
        var root = Path.Combine(Path.GetTempPath(), $"dtk-loader-{Guid.NewGuid():N}");
        try
        {
            var sandbox = new ParitySandbox(root);

            var trackingOn = await RunPipeBuildAsync(binary, sandbox);
            trackingOn.Stderr.Should().Contain(SqliteLibrary,
                "with tracking on dyld must report loading SQLite, or the tracking-off assertion below proves nothing");

            var disable = await ParityProcess.RunAsync(
                ParityRunner.CreateStartInfo(binary, sandbox, ["config", "set", "tracking.enabled", "false"]), stdin: null);
            disable.ExitCode.Should().Be(0, disable.Stdout + disable.Stderr);

            var trackingOff = await RunPipeBuildAsync(binary, sandbox);
            trackingOff.Stderr.Should().NotContain(SqliteLibrary, "tracking off must not load SQLite");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ProcessOutput> RunPipeBuildAsync(string binary, ParitySandbox sandbox)
    {
        var startInfo = ParityRunner.CreateStartInfo(binary, sandbox, ["pipe", "build", "--exit-code", "1"]);
        startInfo.Environment["DYLD_PRINT_LIBRARIES"] = "1";

        var output = await ParityProcess.RunAsync(
            startInfo, await File.ReadAllTextAsync(ParityRunner.FixturePath("dotnet_build_errors.txt")));

        output.ExitCode.Should().Be(1, output.Stdout + output.Stderr);
        output.Stdout.Should().StartWith("dotnet build: 3 errors");
        return output;
    }
}
```

The binary is started directly, not through `/bin/sh`: SIP strips `DYLD_*` variables when a protected binary
such as `/bin/sh` is involved.

- [ ] **Step 6: Build, format and confirm it skips here**

Run: `dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests && dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes && DTK_AOT_REQUIRED=1 dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~SqliteLoaderTests|FullyQualifiedName~AotParitySkipTests"`
Expected: build and format clean; `AotParitySkipTests` pass; `SqliteLoaderTests` is skipped with the
"macOS only" reason even though `DTK_AOT_REQUIRED=1` is set.

- [ ] **Step 7: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParityAttributes.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/AotParitySkipTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/SqliteLoaderTests.cs
git commit -F <message file>
```

Message: `test: check on macOS that tracking off loads no SQLite`.

---

### Task 3: Pack linux-x64 against glibc 2.27 with SQLite linked in

**Files:**

- Create: `eng/aot/check-glibc-floor.sh`
- Create: `eng/aot/pack-linux.sh`
- Create: `src/DotnetTokenKiller.Cli/Native/fcntl64.c`
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`

**Interfaces:**

- Produces: `sh eng/aot/check-glibc-floor.sh <nupkg> [max-version]` (exit 0 when every ELF file needs at most
  `GLIBC_<max>`, default 2.27; exit 1 otherwise or when the package has no ELF file);
  `sh eng/aot/pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments...]` for
  `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64` (writes `DotnetTokenKiller.<rid>.<version>.nupkg`
  into `<feed-dir>` and the MSBuild log to `<pack-log>`); MSBuild property `DtkLinkSqliteStatically`
  (`true` for AOT linux-x64/linux-arm64; `-p:DtkLinkSqliteStatically=false` turns it off).

- [ ] **Step 1: Write the floor check**

Create `eng/aot/check-glibc-floor.sh`:

```sh
#!/bin/sh
# Fails if any ELF file in a tool package needs a glibc symbol version above the floor. The glibc Native
# AOT packages must start wherever .NET 10 does: glibc 2.27 by default.
#
# Usage: eng/aot/check-glibc-floor.sh <nupkg> [max-version]
#
# Needs unzip, readelf (binutils) and GNU sort.
set -eu

usage="usage: eng/aot/check-glibc-floor.sh <nupkg> [max-version]"
nupkg=${1:?$usage}
max=${2:-2.27}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
unzip -q "$nupkg" -d "$work/package"
find "$work/package" -type f | sort > "$work/files"

elf_count=0
failed=0
while IFS= read -r file; do
    if [ "$(head -c 4 "$file" | od -An -tx1 | tr -d ' \n')" != "7f454c46" ]; then
        continue
    fi
    elf_count=$((elf_count + 1))
    name=${file#"$work/package/"}
    highest=$(readelf --version-info -W "$file" | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/^GLIBC_//' | sort -uV | tail -n 1)
    if [ -z "$highest" ]; then
        echo "$name: needs no versioned glibc symbol"
        continue
    fi
    if [ "$(printf '%s\n%s\n' "$highest" "$max" | sort -V | tail -n 1)" != "$max" ]; then
        echo "::error::$name needs GLIBC_$highest, above the GLIBC_$max floor"
        failed=1
    else
        echo "$name: highest glibc symbol version GLIBC_$highest (floor GLIBC_$max)"
    fi
done < "$work/files"

if [ "$elf_count" -eq 0 ]; then
    echo "::error::$nupkg contains no ELF file"
    exit 1
fi
exit "$failed"
```

- [ ] **Step 2: Show the check fails on today's package (red)**

Pack linux-x64 on the host, as CI does today:

```bash
rm -rf artifacts/aot && mkdir -p artifacts/aot
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/aot/host-feed
sh eng/aot/check-glibc-floor.sh artifacts/aot/host-feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg; echo "exit $?"
```

Expected: `::error::tools/any/linux-x64/dtk needs GLIBC_2.34, above the GLIBC_2.27 floor`, the same for
`tools/any/linux-x64/libe_sqlite3.so`, and `exit 1`.

- [ ] **Step 3: Write the cross-image pack script**

Create `eng/aot/pack-linux.sh`:

```sh
#!/bin/sh
# Packs dtk's Native AOT tool package for a Linux RID inside Microsoft's cross-build image for that RID,
# against the image's sysroot: glibc 2.27 (an Ubuntu 18.04 rootfs, the floor .NET itself supports) for
# linux-x64 and linux-arm64, musl 1.2.3 (an Alpine 3.17 rootfs) for the musl RIDs.
#
# Usage: eng/aot/pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments...]
#
# Needs Docker and the .NET SDK on the host. The images (about 7 GB each) carry no SDK: the host's SDK,
# the checkout, the NuGet cache, the feed and the log directory are mounted at their host paths, and the
# pack runs as the invoking user, so no root-owned files are left behind and git still reads the checkout
# (the informational version carries the commit SHA, which the parity tests compare).
set -eu

usage="usage: eng/aot/pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments...]"
rid=${1:?$usage}
version=${2:?$usage}
feed=${3:?$usage}
log=${4:?$usage}
shift 4

case "$rid" in
    linux-x64) tag=cross-amd64; arch=x64 ;;
    linux-arm64) tag=cross-arm64; arch=arm64 ;;
    linux-musl-x64) tag=cross-amd64-musl; arch=x64 ;;
    linux-musl-arm64) tag=cross-arm64-musl; arch=arm64 ;;
    *) echo "pack-linux.sh: unsupported RID '$rid'" >&2; exit 2 ;;
esac
image="mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-$tag"

repo=$(cd "$(dirname "$0")/../.." && pwd)
dotnet_root=${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
mkdir -p "$feed" "$nuget" "$(dirname "$log")"
feed=$(cd "$feed" && pwd)
log_dir=$(cd "$(dirname "$log")" && pwd)
log="$log_dir/$(basename "$log")"

echo "Packing $rid $version in $image"
docker run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$repo:$repo" -v "$dotnet_root:$dotnet_root:ro" -v "$nuget:$nuget" -v "$feed:$feed" -v "$log_dir:$log_dir" \
    -w "$repo" \
    -e HOME=/tmp -e DOTNET_ROOT="$dotnet_root" -e NUGET_PACKAGES="$nuget" \
    -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "$image" \
    "$dotnet_root/dotnet" pack src/DotnetTokenKiller.Cli -c Release -r "$rid" \
    -p:IncludeSymbols=false -p:Version="$version" -p:SysRoot="/crossrootfs/$arch" -p:LinkerFlavor=lld \
    -o "$feed" "-flp:LogFile=$log;Verbosity=minimal" "$@"
```

- [ ] **Step 4: Show the sysroot alone is not enough (still red)**

```bash
sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg; echo "exit $?"
find . -user root -not -path './.git/*' | head -3
```

Expected: the pack prints `Successfully created package` (about 30 s once restored; "An issue was
encountered verifying workloads" is harmless). The check prints
`tools/any/linux-x64/dtk: highest glibc symbol version GLIBC_2.16 (floor GLIBC_2.27)` but
`::error::tools/any/linux-x64/libe_sqlite3.so needs GLIBC_2.34`, and `exit 1`. `find` prints nothing.

- [ ] **Step 5: Add the fcntl64 shim**

Create `src/DotnetTokenKiller.Cli/Native/fcntl64.c`:

```c
/*
 * glibc < 2.28 has no fcntl64, which the statically linked SQLite calls; on 64-bit Linux it is the same call
 * as fcntl. Linked into the linux-x64 and linux-arm64 Native AOT binaries only (see DotnetTokenKiller.Cli.csproj).
 */
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

- [ ] **Step 6: Link SQLite statically for the glibc AOT RIDs**

In `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`:

(a) In the `ItemGroup` holding the Spectre package references, add after
`<PackageReference Include="Spectre.Console.Cli"/>`:

```xml
    <!--
      Already a transitive dependency through Microsoft.Data.Sqlite, and pinned in Directory.Packages.props.
      Referenced directly for $(PkgSQLitePCLRaw_lib_e_sqlite3), the static library's location below.
    -->
    <PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" GeneratePathProperty="true"/>
```

(b) Replace the comment above the `CopyOutputSymbolsToPublishDirectory` property group, which begins
`An AOT RID package carries the native dtk and libe_sqlite3 only.`, with:

```xml
  <!--
    An AOT RID package carries the native dtk and, where SQLite is not linked in, libe_sqlite3. Tool packing globs
    the whole publish directory, so symbols (released as GitHub Release assets instead), managed .pdb and .xml files,
    and SQLitePCLRaw's static libe_sqlite3.a are kept out of it here. The `any` package is unaffected, and keeps its
    .pdb files for its .snupkg.
  -->
```

(c) In the `KeepAotToolPackageRuntimeOnly` target, add a second `Remove` after the existing one, inside the
same `ItemGroup`:

```xml
      <ResolvedFileToPublish Remove="@(ResolvedFileToPublish)"
                             Condition="'$(DtkLinkSqliteStatically)' == 'true' and '%(ResolvedFileToPublish.Filename)%(ResolvedFileToPublish.Extension)' == 'libe_sqlite3.so'"/>
```

(d) Directly after that target's closing `</Target>`, add:

```xml
  <!--
    The glibc AOT binaries link SQLite statically. SQLitePCLRaw.lib.e_sqlite3 3.53.3's libe_sqlite3.so needs
    GLIBC_2.34 (ericsink/SQLitePCL.raw#674), while CI builds these RIDs against a glibc 2.27 sysroot, the floor
    .NET itself supports; downgrading the package would bring back GHSA-2m69-gcr7-jv3q. musl, macOS and the
    `any` package keep loading the shared library. See docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md.
  -->
  <PropertyGroup Condition="'$(PublishAot)' == 'true' and ('$(RuntimeIdentifier)' == 'linux-x64' or '$(RuntimeIdentifier)' == 'linux-arm64')">
    <DtkLinkSqliteStatically>true</DtkLinkSqliteStatically>
  </PropertyGroup>
  <ItemGroup Condition="'$(DtkLinkSqliteStatically)' == 'true'">
    <DirectPInvoke Include="e_sqlite3"/>
    <NativeLibrary Include="$(PkgSQLitePCLRaw_lib_e_sqlite3)/runtimes/$(RuntimeIdentifier)/native/libe_sqlite3.a"/>
  </ItemGroup>
  <!--
    The static SQLite calls fcntl64, which glibc only added in 2.28; Native/fcntl64.c supplies it. The object is
    compiled after SetupOSSpecificProps, which sets TargetTriple for cross builds, and passed as a LinkerArg:
    SetupOSSpecificProps has already copied NativeLibrary items into LinkerArg by then, so a NativeLibrary item
    added here is silently dropped and the link fails with "undefined symbol: fcntl64". -Wl,defsym cannot alias
    it either: lld needs fcntl defined, and it only exists in libc.so.
  -->
  <Target Name="CompileFcntl64Shim" AfterTargets="SetupOSSpecificProps" BeforeTargets="LinkNative"
          Condition="'$(DtkLinkSqliteStatically)' == 'true'">
    <PropertyGroup>
      <_DtkFcntl64Object>$(IntermediateOutputPath)native/fcntl64.o</_DtkFcntl64Object>
      <_DtkShimSysRootArg Condition="'$(SysRoot)' != ''">--sysroot=&quot;$(SysRoot)&quot;</_DtkShimSysRootArg>
      <_DtkShimTargetArg Condition="'$(TargetTriple)' != ''">--target=$(TargetTriple)</_DtkShimTargetArg>
    </PropertyGroup>
    <MakeDir Directories="$(IntermediateOutputPath)native"/>
    <Exec Command="&quot;$(CppCompilerAndLinker)&quot; $(_DtkShimSysRootArg) $(_DtkShimTargetArg) -O2 -fPIC -c &quot;$(MSBuildProjectDirectory)/Native/fcntl64.c&quot; -o &quot;$(_DtkFcntl64Object)&quot;"/>
    <ItemGroup>
      <LinkerArg Include="&quot;$(_DtkFcntl64Object)&quot;"/>
    </ItemGroup>
  </Target>
```

- [ ] **Step 7: Pack again and see the check pass (green)**

```bash
rm -rf artifacts/aot/feed
sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg; echo "exit $?"
unzip -l artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg | grep tools/
rm -rf artifacts/aot/extract && mkdir -p artifacts/aot/extract && unzip -q artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg -d artifacts/aot/extract
chmod +x artifacts/aot/extract/tools/any/linux-x64/dtk && artifacts/aot/extract/tools/any/linux-x64/dtk --version && git rev-parse HEAD
```

Expected: `tools/any/linux-x64/dtk: highest glibc symbol version GLIBC_2.16 (floor GLIBC_2.27)`, `exit 0`;
the package lists only `DotnetToolSettings.xml` and `dtk`; `--version` prints `0.0.0-local+<sha>` where `<sha>`
equals `git rev-parse HEAD` (git works inside the container).

- [ ] **Step 8: Confirm a local publish without a sysroot still works**

```bash
rm -rf artifacts/aot/publish
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot/publish
ls artifacts/aot/publish
state=$(mktemp -d)
DTK_CONFIG_PATH="$state/c.json" DTK_DB_PATH="$state/t.db" DTK_TEE_DIR="$state/logs" artifacts/aot/publish/dtk pipe build --exit-code 1 < tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt | head -1
DTK_CONFIG_PATH="$state/c.json" DTK_DB_PATH="$state/t.db" DTK_TEE_DIR="$state/logs" artifacts/aot/publish/dtk gain --json | grep -o '"TotalCommands":[0-9]*'
rm -rf "$state"
```

Expected: the publish directory holds only `dtk`; `dotnet build: 3 errors, 1 warning (1.89s)`;
`"TotalCommands":1` (statically linked SQLite works with the host's clang and linker).

- [ ] **Step 9: Build, lint and run the library tests**

```bash
dtk dotnet build DotnetTokenKiller.slnx
/tmp/wprobe/venv/bin/shellcheck -s sh eng/aot/*.sh
dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests
```

Expected: build clean (the new package reference changes nothing else); shellcheck silent; tests pass.

- [ ] **Step 10: Commit**

```bash
git add --chmod=+x eng/aot/check-glibc-floor.sh eng/aot/pack-linux.sh
git add src/DotnetTokenKiller.Cli/Native/fcntl64.c src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj
git commit -F <message file>
```

Message: `build: pack Linux AOT against a glibc 2.27 sysroot with SQLite linked in`, body naming the floor
check, #674 and the `fcntl64` shim.

---

### Task 4: Test every Linux package the way CI will, and add the musl RIDs

**Files:**

- Create: `eng/aot/smoke-old-glibc.sh`
- Create: `eng/aot/test-package.sh`
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (`ToolPackageRuntimeIdentifiers`)

**Interfaces:**

- Consumes: `pack-linux.sh`, `check-glibc-floor.sh` (Task 3); the `Aot` test namespace (Tasks 1 and 2).
- Produces: `sh eng/aot/smoke-old-glibc.sh <tools-dir>`;
  `sh eng/aot/test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]`
  (packs the pointer into `<feed-dir>`, installs into `<tools-dir>`, asserts `dotnettokenkiller.<rid>` in the store,
  runs the `Aot` namespace with `DTK_AOT_REQUIRED=1`).

- [ ] **Step 1: Write the Rocky Linux 8 smoke**

Create `eng/aot/smoke-old-glibc.sh`:

```sh
#!/bin/sh
# Runs an installed dtk tool on Rocky Linux 8 (glibc 2.28), the oldest glibc distribution .NET 10 supports:
# it must start, filter a build log and record it in the statically linked SQLite tracking database.
#
# Usage: eng/aot/smoke-old-glibc.sh <tools-dir>
#
# <tools-dir> is a `dotnet tool install --tool-path` directory. Needs Docker; runs the image for the host's
# architecture, with the checkout and the tools directory mounted read-only.
set -eu

if [ "${1:-}" = "--in-container" ]; then
    dnf install -y -q libicu > /dev/null
    ldd --version | head -n 1
    state=$(mktemp -d)
    export DTK_CONFIG_PATH="$state/config.json" DTK_DB_PATH="$state/tracking.db" DTK_TEE_DIR="$state/logs" HOME="$state"

    /tools/dtk --version

    status=0
    /tools/dtk pipe build --exit-code 1 < /repo/tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt > "$state/pipe.txt" || status=$?
    cat "$state/pipe.txt"
    if [ "$status" -ne 1 ] || ! grep -q '^dotnet build: 3 errors' "$state/pipe.txt"; then
        echo "::error::dtk pipe build exited $status without the build summary"
        exit 1
    fi

    /tools/dtk gain --json > "$state/gain.json"
    if ! grep -q '"TotalCommands":1,' "$state/gain.json"; then
        cat "$state/gain.json"
        echo "::error::the pipe run was not recorded in the tracking database"
        exit 1
    fi
    echo "dtk started, filtered and tracked on $(grep '^PRETTY_NAME=' /etc/os-release | cut -d= -f2- | tr -d '"')"
    exit 0
fi

tools=${1:?usage: eng/aot/smoke-old-glibc.sh <tools-dir>}
tools=$(cd "$tools" && pwd)
repo=$(cd "$(dirname "$0")/../.." && pwd)
docker run --rm -v "$repo:/repo:ro" -v "$tools:/tools:ro" rockylinux:8 sh /repo/eng/aot/smoke-old-glibc.sh --in-container
```

- [ ] **Step 2: Show the smoke fails on a binary built for the host's glibc (red)**

The Task 3 Step 8 publish directory holds a `dtk` that needs GLIBC_2.38:

```bash
sh eng/aot/smoke-old-glibc.sh artifacts/aot/publish; echo "exit $?"
```

Expected: `ldd (GNU libc) 2.28`, then one or more `/tools/dtk: /lib64/…: version 'GLIBC_2.xx' not found`
lines, and `exit 1`. RPM key-import lines on stderr are harmless.

- [ ] **Step 3: Write the package test script**

Create `eng/aot/test-package.sh`:

```sh
#!/bin/sh
# Installs a dtk Native AOT tool package from a local feed and tests it against the JIT build: the AOT
# warning check on its pack log, the JIT-vs-AOT parity tests and, optionally, the whole CLI integration suite.
#
# Usage: eng/aot/test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]
#
# <feed-dir> holds DotnetTokenKiller.<rid>.<version>.nupkg; the pointer package is packed into it here.
# <tools-dir> receives the installed tool. With --image, the script runs itself inside that image (the musl
# RIDs use mcr.microsoft.com/dotnet/sdk:10.0-alpine) as the invoking user, with the checkout and the NuGet
# cache mounted at their host paths; every path argument must then be inside the checkout.
set -eu

usage="usage: eng/aot/test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]"
repo=$(cd "$(dirname "$0")/../.." && pwd)
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}

image=
if [ "${1:-}" = "--image" ]; then
    image=${2:?$usage}
    shift 2
fi

rid=${1:?$usage}
version=${2:?$usage}
feed=${3:?$usage}
log=${4:?$usage}
tools=${5:?$usage}
integration_suite=${6:-}

mkdir -p "$tools" "$nuget"
feed=$(cd "$feed" && pwd)
log="$(cd "$(dirname "$log")" && pwd)/$(basename "$log")"
tools=$(cd "$tools" && pwd)

if [ -n "$image" ]; then
    for path in "$feed" "$log" "$tools"; do
        case "$path" in
            "$repo"/*) ;;
            *) echo "test-package.sh: with --image, $path must be inside $repo" >&2; exit 2 ;;
        esac
    done
    exec docker run --rm \
        --user "$(id -u):$(id -g)" \
        -v "$repo:$repo" -v "$nuget:$nuget" -w "$repo" \
        -e HOME=/tmp -e NUGET_PACKAGES="$nuget" -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        "$image" \
        sh "$repo/eng/aot/test-package.sh" "$rid" "$version" "$feed" "$log" "$tools" ${integration_suite:+"$integration_suite"}
fi

cd "$repo"
version_lower=$(echo "$version" | tr '[:upper:]' '[:lower:]')

# The parity tests compare against this JIT build, so it must carry the packs' version.
dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$version" -o "$feed"

# A package already in the NuGet cache at this ID and version is installed from the cache, not the feed.
rm -rf "$nuget/dotnettokenkiller/$version_lower" "$nuget/dotnettokenkiller.$rid/$version_lower"

config=$(mktemp)
trap 'rm -f "$config"' EXIT
cat > "$config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotnetTokenKiller*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
rm -rf "$tools"
dotnet tool install --tool-path "$tools" --configfile "$config" DotnetTokenKiller --version "$version"

store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.$rid"
if [ ! -d "$store" ]; then
    echo "::error::Expected $store: the install did not pick the $rid package."
    ls -R "$tools/.store"
    exit 1
fi

# DTK_AOT_REQUIRED=1 makes a missing binary or pack log fail these tests instead of skipping them.
DTK_AOT_REQUIRED=1 DTK_AOT_PACK_LOG="$log" DTK_AOT_BINARY="$tools/dtk" \
    dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build \
    --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot" --verbosity normal

if [ "$integration_suite" = "--integration-suite" ]; then
    DTK_TEST_BINARY="$tools/dtk" \
        dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --verbosity normal
fi
```

The `cat > "$config" <<EOF` inside this file is fine: the hook only rewrites commands you type into Bash, and
this heredoc contains no `dotnet` verb anyway. Create the file with the Write tool.

- [ ] **Step 4: Repack at the current commit, test the linux-x64 package natively, then smoke it on Rocky 8 (green)**

Task 3's package was packed before Task 3's commit. The informational version carries the commit SHA and
parity's `version` case compares it with the JIT build, so repack first:

```bash
rm -rf artifacts/aot/feed
sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log
sh eng/aot/test-package.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log artifacts/aot/tools/linux-x64 2>&1 | tee artifacts/aot/test-linux-x64.txt | grep -E "error|Tool '|Total tests|Passed:|Failed:|Skipped:"
sh eng/aot/smoke-old-glibc.sh artifacts/aot/tools/linux-x64; echo "exit $?"
```

Expected (several minutes): `Tool 'dotnettokenkiller' (version '0.0.0-local') was successfully installed.`;
`Total tests: 44`, `Passed: 43`, `Skipped: 1` (the macOS loader test); then the smoke prints the version with
the commit SHA, the build summary, `dtk started, filtered and tracked on Rocky Linux 8.x (Green Obsidian)` and
`exit 0`. Do not commit between this step and Step 6: every package tested in this task must be packed at the
same commit as the JIT build it is compared with.

- [ ] **Step 5: Add the musl RIDs**

In `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj`, change

```xml
    <ToolPackageRuntimeIdentifiers>linux-x64;linux-arm64;osx-arm64;win-x64;any</ToolPackageRuntimeIdentifiers>
```

to

```xml
    <ToolPackageRuntimeIdentifiers>linux-x64;linux-arm64;linux-musl-x64;linux-musl-arm64;osx-arm64;win-x64;any</ToolPackageRuntimeIdentifiers>
```

(win-x64 leaves in Task 5, together with its CI rows.) Also change the sentence in the comment above it that
reads `` `any` is the framework-dependent fallback for every other platform`` to
`` `any` is the framework-dependent fallback for every other platform. The musl RIDs must be listed: the SDK's RID graph maps them to linux-x64 and linux-arm64, so Alpine would otherwise install the glibc package``.

- [ ] **Step 6: Pack and test linux-musl-x64 in Alpine**

```bash
sh eng/aot/pack-linux.sh linux-musl-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-musl-x64.log
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-musl-x64.0.0.0-local.nupkg; echo "exit $?"
sh eng/aot/test-package.sh --image mcr.microsoft.com/dotnet/sdk:10.0-alpine linux-musl-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-musl-x64.log artifacts/aot/tools/linux-musl-x64 2>&1 | tee artifacts/aot/test-linux-musl-x64.txt | grep -E "error|Tool '|Total tests|Passed:|Failed:|Skipped:"
find . -user root -not -path './.git/*' | head -3
```

Expected: the check prints `needs no versioned glibc symbol` for `dtk` and `libe_sqlite3.so` and `exit 0`;
the Alpine run installs the tool (the store holds `dotnettokenkiller.linux-musl-x64`, or the script fails) and
reports `Total tests: 44`, `Passed: 43`, `Skipped: 1`; `find` prints nothing. This run rebuilt the Release
solution inside Alpine, so `bin/Release` now holds a musl apphost: before running `test-package.sh` natively
again (in this or a later task, for example after a review fix), run
`dtk dotnet build DotnetTokenKiller.slnx -c Release --no-incremental` first.

- [ ] **Step 7: Pack the arm64 RIDs (cross-compiled; not runnable here)**

```bash
sh eng/aot/pack-linux.sh linux-arm64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-arm64.log
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-arm64.0.0.0-local.nupkg; echo "exit $?"
unzip -l artifacts/aot/feed/DotnetTokenKiller.linux-arm64.0.0.0-local.nupkg | grep tools/
sh eng/aot/pack-linux.sh linux-musl-arm64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-musl-arm64.log
unzip -l artifacts/aot/feed/DotnetTokenKiller.linux-musl-arm64.0.0.0-local.nupkg | grep tools/
file src/DotnetTokenKiller.Cli/bin/Release/net10.0/linux-arm64/native/dtk
```

Expected: linux-arm64's `dtk` needs at most `GLIBC_2.17`, `exit 0`, and its package holds only
`DotnetToolSettings.xml` and `dtk` (the shim compiled with `--target=aarch64-linux-gnu`);
linux-musl-arm64's package holds `DotnetToolSettings.xml`, `dtk` and `libe_sqlite3.so`; `file` reports
`ARM aarch64`. CI tests both on `ubuntu-24.04-arm`.

- [ ] **Step 8: Lint and commit**

```bash
/tmp/wprobe/venv/bin/shellcheck -s sh eng/aot/*.sh
git add --chmod=+x eng/aot/smoke-old-glibc.sh eng/aot/test-package.sh
git add src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj
git commit -F <message file>
```

Message: `build: add musl AOT packages, and scripts that test each Linux package as CI will`.

Report the three test summaries (linux-x64, linux-musl-x64, and the smoke) in your task report.

---

### Task 5: Pack and test every package in CI; Windows gets the fallback

**Files:**

- Rewrite: `.github/workflows/aot-package.yml`
- Rewrite: `.github/workflows/fallback-package.yml`
- Modify: `.github/workflows/ci.yml` (the `aot` job)
- Modify: `.github/workflows/publish.yml` (the `pack-rid` job)
- Modify: `src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj` (drop `win-x64`)

**Interfaces:**

- Consumes: all four `eng/aot/*.sh` scripts (Tasks 3 and 4).
- Produces: artifacts `package-<rid>`, `packlog-<rid>`, `symbols-<rid>` per AOT RID, and `package-any`,
  `package-pointer` from the ubuntu fallback row (the names `publish.yml`'s gather step already expects).

- [ ] **Step 1: Drop win-x64**

In the CLI csproj, change `ToolPackageRuntimeIdentifiers` to:

```xml
    <ToolPackageRuntimeIdentifiers>linux-x64;linux-arm64;linux-musl-x64;linux-musl-arm64;osx-arm64;any</ToolPackageRuntimeIdentifiers>
```

and append this sentence to the comment above it (no `--` may appear inside an XML comment):

```text
Windows installs `any`: the SDK's shim for a native tool there is a dtk.cmd batch file, which Git Bash cannot run and which re-parses | & ^ % in arguments (see docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md).
```

- [ ] **Step 2: Rewrite `aot-package.yml`**

Replace the whole file with:

```yaml
name: AOT package

# Packs, installs and tests the Native AOT tool package for one runtime identifier. Called by CI on
# every push and pull request, and by Publish for a release, so a release is built and checked
# exactly the way every PR is. Linux RIDs pack in Microsoft's cross-build images against old
# sysroots (eng/aot/pack-linux.sh) and are tested on a runner of their own architecture.

on:
  workflow_call:
    inputs:
      rid:
        description: Runtime identifier to pack, e.g. linux-x64.
        required: true
        type: string
      version:
        description: Package version, passed to the builds and to every pack.
        required: true
        type: string
      pack-runner:
        description: Runner that packs. ubuntu-latest for Linux RIDs (cross-build images), macos-latest for osx-arm64.
        required: true
        type: string
      test-runner:
        description: Runner whose OS and architecture run the RID's binary natively.
        required: true
        type: string
      test-image:
        description: Container image the tests run in (the musl RIDs); empty to run them on the runner.
        required: false
        type: string
        default: ''
      glibc:
        description: Check the package's glibc floor and smoke-test the installed tool on Rocky Linux 8.
        required: false
        type: boolean
        default: false
      run-integration-suite:
        description: Also run the whole CLI integration suite against the installed binary.
        required: false
        type: boolean
        default: false

permissions:
  contents: read

jobs:
  pack:
    name: pack
    runs-on: ${{ inputs.pack-runner }}
    timeout-minutes: 45
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

      # Each cross-build image is about 7 GB; the runner's preinstalled Android, GHC and CodeQL
      # toolchains are not needed here.
      - name: Free disk space
        if: runner.os == 'Linux'
        run: |
          df -h /
          sudo rm -rf /usr/local/lib/android /opt/ghc /usr/local/.ghcup /opt/hostedtoolcache/CodeQL
          df -h /

      - name: Pack the AOT package in the cross-build image
        if: runner.os == 'Linux'
        run: sh eng/aot/pack-linux.sh "$RID" "$VERSION" artifacts/feed "artifacts/pack-$RID.log"

      - name: Pack the AOT package
        if: runner.os == 'macOS'
        run: >-
          dotnet pack src/DotnetTokenKiller.Cli -c Release -r "$RID"
          -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/feed
          "-flp:LogFile=artifacts/pack-$RID.log;Verbosity=minimal"

      - name: Check the glibc floor
        if: inputs.glibc
        run: sh eng/aot/check-glibc-floor.sh "artifacts/feed/DotnetTokenKiller.$RID.$VERSION.nupkg"

      - name: Upload the package
        uses: actions/upload-artifact@v7
        with:
          name: package-${{ inputs.rid }}
          path: artifacts/feed/DotnetTokenKiller.${{ inputs.rid }}.*.nupkg
          if-no-files-found: error
          retention-days: 14

      - name: Upload the pack log
        uses: actions/upload-artifact@v7
        with:
          name: packlog-${{ inputs.rid }}
          path: artifacts/pack-${{ inputs.rid }}.log
          if-no-files-found: error
          retention-days: 14

      - name: Upload the native symbols
        uses: actions/upload-artifact@v7
        with:
          name: symbols-${{ inputs.rid }}
          path: |
            src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.dbg
            src/DotnetTokenKiller.Cli/bin/Release/net10.0/${{ inputs.rid }}/native/dtk.dSYM
          if-no-files-found: error
          retention-days: 14

  test:
    name: test
    needs: pack
    runs-on: ${{ inputs.test-runner }}
    timeout-minutes: 60
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

      # With a test image, the SDK comes from the image instead.
      - name: Setup .NET
        if: inputs.test-image == ''
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Download the package
        uses: actions/download-artifact@v8
        with:
          name: package-${{ inputs.rid }}
          path: artifacts/feed

      - name: Download the pack log
        uses: actions/download-artifact@v8
        with:
          name: packlog-${{ inputs.rid }}
          path: artifacts

      - name: Install and test the package
        env:
          TEST_IMAGE: ${{ inputs.test-image }}
          RUN_INTEGRATION_SUITE: ${{ inputs.run-integration-suite }}
        run: |
          set --
          if [ -n "$TEST_IMAGE" ]; then
            set -- --image "$TEST_IMAGE"
          fi
          set -- "$@" "$RID" "$VERSION" artifacts/feed "artifacts/pack-$RID.log" artifacts/tools
          if [ "$RUN_INTEGRATION_SUITE" = "true" ]; then
            set -- "$@" --integration-suite
          fi
          sh eng/aot/test-package.sh "$@"

      - name: Smoke-test on Rocky Linux 8
        if: inputs.glibc
        run: sh eng/aot/smoke-old-glibc.sh artifacts/tools
```

- [ ] **Step 3: Rewrite `fallback-package.yml`**

Replace the whole file with:

```yaml
name: Fallback package

# Packs the framework-dependent `any` tool package, the one Windows and every runtime identifier without
# a Native AOT package install, then installs and tests it on Linux and Windows. Also packs the pointer
# package Publish pushes. Called by CI and by Publish.

on:
  workflow_call:
    inputs:
      version:
        description: Package version, passed to the build and to every pack.
        required: true
        type: string

permissions:
  contents: read

jobs:
  package:
    name: any (${{ matrix.os }})
    runs-on: ${{ matrix.os }}
    timeout-minutes: 60
    strategy:
      fail-fast: false
      matrix:
        os: [ ubuntu-latest, windows-latest ]
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

      # The real pointer, listing every RID, that Publish pushes. Packed here so the privileged publish job builds nothing.
      - name: Pack the pointer package
        if: runner.os == 'Linux'
        run: dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$VERSION" -o artifacts/pointer

      # The real pointer lists the AOT RIDs, so on a linux-x64 runner it would install the AOT package. A
      # check-only pointer that lists just `any` forces the fallback. It is never uploaded.
      - name: Pack a check-only pointer that lists only the fallback
        run: >-
          dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false
          -p:ToolPackageRuntimeIdentifiers=any -p:Version="$VERSION" -o artifacts/check-feed

      - name: Install the fallback from the local feed
        run: |
          cp artifacts/feed/DotnetTokenKiller.any.*.nupkg artifacts/check-feed/
          # A relative source resolves against the config file's directory. An absolute "$(pwd)" path
          # would be POSIX-style (/d/a/...) under Git Bash on Windows, which NuGet cannot read.
          # nuget.org serves everything except dtk's own packages.
          cat > artifacts/check-feed.nuget.config <<EOF
          <?xml version="1.0" encoding="utf-8"?>
          <configuration>
            <packageSources>
              <clear />
              <add key="local" value="check-feed" />
              <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            </packageSources>
            <packageSourceMapping>
              <packageSource key="local">
                <package pattern="DotnetTokenKiller*" />
              </packageSource>
              <packageSource key="nuget.org">
                <package pattern="*" />
              </packageSource>
            </packageSourceMapping>
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

          # On Windows the `any` package's shim is dtk.exe, an apphost; on Unix it is dtk.
          binary="$tools/dtk"
          if [ "$RUNNER_OS" = "Windows" ]; then
            binary="$tools/dtk.exe"
          fi
          echo "DTK_INSTALLED=$binary" >> "$GITHUB_ENV"

      # DTK_AOT_REQUIRED=1 makes a missing binary fail the parity tests instead of skipping them.
      - name: Check parity
        env:
          DTK_AOT_REQUIRED: '1'
        run: >-
          DTK_AOT_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build
          --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests" --verbosity normal

      # Windows installs this package, so the whole suite runs against it there.
      - name: Run the integration suite against the installed binary
        if: runner.os == 'Windows'
        run: >-
          DTK_TEST_BINARY="$DTK_INSTALLED"
          dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --verbosity normal

      # The shells a Windows user or agent runs dtk from. `dtk config set` echoes the value it received, so
      # a `|` that a shell or shim re-parsed shows up as a missing or mangled value.
      - name: Smoke-test the installed tool from Git Bash
        if: runner.os == 'Windows'
        env:
          DTK_TOOLS: ${{ runner.temp }}\dtk-tools
          DTK_CONFIG_PATH: ${{ runner.temp }}\smoke\config.json
          DTK_DB_PATH: ${{ runner.temp }}\smoke\tracking.db
          DTK_TEE_DIR: ${{ runner.temp }}\smoke\logs
        run: |
          tools_posix="$(cygpath -u "$DTK_TOOLS")"
          export PATH="$tools_posix:$PATH"
          command -v dtk
          dtk --version
          status=0
          output="$(dtk config set tracking.enabled 'Name~A|Name~B' 2>&1)" || status=$?
          echo "$output"
          if [ "$status" -ne 1 ]; then
            echo "::error::Git Bash: expected exit 1, got $status"
            exit 1
          fi
          case "$output" in
            *"'Name~A|Name~B'"*) ;;
            *) echo "::error::Git Bash: the argument did not reach dtk verbatim"; exit 1 ;;
          esac

      - name: Smoke-test the installed tool from pwsh
        if: runner.os == 'Windows'
        shell: pwsh
        env:
          DTK_TOOLS: ${{ runner.temp }}\dtk-tools
          DTK_CONFIG_PATH: ${{ runner.temp }}\smoke\config.json
          DTK_DB_PATH: ${{ runner.temp }}\smoke\tracking.db
          DTK_TEE_DIR: ${{ runner.temp }}\smoke\logs
        run: |
          $env:PATH = "$env:DTK_TOOLS;$env:PATH"
          (Get-Command dtk).Source
          dtk --version
          if ($LASTEXITCODE -ne 0) { throw "pwsh: dtk --version exited $LASTEXITCODE" }
          $output = dtk config set tracking.enabled 'Name~A|Name~B' 2>&1 | Out-String
          $status = $LASTEXITCODE
          $output
          if ($status -ne 1) { throw "pwsh: expected exit 1, got $status" }
          if (-not $output.Contains("'Name~A|Name~B'")) { throw 'pwsh: the argument did not reach dtk verbatim' }
          # The runner exits with the last native exit code, which is the expected 1.
          exit 0

      - name: Smoke-test the installed tool from cmd
        if: runner.os == 'Windows'
        shell: cmd
        env:
          DTK_TOOLS: ${{ runner.temp }}\dtk-tools
          DTK_CONFIG_PATH: ${{ runner.temp }}\smoke\config.json
          DTK_DB_PATH: ${{ runner.temp }}\smoke\tracking.db
          DTK_TEE_DIR: ${{ runner.temp }}\smoke\logs
        run: |
          set "PATH=%DTK_TOOLS%;%PATH%"
          where dtk || exit /b 1
          dtk --version || exit /b 1
          dtk config set tracking.enabled "Name~A|Name~B" > "%RUNNER_TEMP%\smoke-cmd.txt" 2>&1
          set status=%ERRORLEVEL%
          type "%RUNNER_TEMP%\smoke-cmd.txt"
          if not "%status%"=="1" (
            echo ::error::cmd: expected exit 1, got %status%
            exit /b 1
          )
          findstr /C:"'Name~A|Name~B'" "%RUNNER_TEMP%\smoke-cmd.txt" > nul || (
            echo ::error::cmd: the argument did not reach dtk verbatim
            exit /b 1
          )

      - name: Upload the package
        if: runner.os == 'Linux'
        uses: actions/upload-artifact@v7
        with:
          name: package-any
          path: |
            artifacts/feed/DotnetTokenKiller.any.*.nupkg
            artifacts/feed/DotnetTokenKiller.any.*.snupkg
          if-no-files-found: error
          retention-days: 14

      - name: Upload the pointer package
        if: runner.os == 'Linux'
        uses: actions/upload-artifact@v7
        with:
          name: package-pointer
          path: artifacts/pointer/DotnetTokenKiller.*.nupkg
          if-no-files-found: error
          retention-days: 14
```

- [ ] **Step 4: Replace the AOT matrix in `ci.yml`**

In `.github/workflows/ci.yml`, replace the whole `aot:` job (from its `aot:` line up to, not including, the
`fallback:` job line) with:

```yaml
  aot:
    name: AOT (${{ matrix.rid }})
    strategy:
      fail-fast: false
      matrix:
        include:
          - rid: linux-x64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-latest
            test-image: ''
            glibc: true
            run-integration-suite: true
          - rid: linux-arm64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-24.04-arm
            test-image: ''
            glibc: true
            run-integration-suite: false
          - rid: linux-musl-x64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-latest
            test-image: mcr.microsoft.com/dotnet/sdk:10.0-alpine
            glibc: false
            run-integration-suite: false
          - rid: linux-musl-arm64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-24.04-arm
            test-image: mcr.microsoft.com/dotnet/sdk:10.0-alpine
            glibc: false
            run-integration-suite: false
          - rid: osx-arm64
            pack-runner: macos-latest
            test-runner: macos-latest
            test-image: ''
            glibc: false
            run-integration-suite: false
    uses: ./.github/workflows/aot-package.yml
    with:
      rid: ${{ matrix.rid }}
      version: 0.0.0-ci
      pack-runner: ${{ matrix.pack-runner }}
      test-runner: ${{ matrix.test-runner }}
      test-image: ${{ matrix.test-image }}
      glibc: ${{ matrix.glibc }}
      run-integration-suite: ${{ matrix.run-integration-suite }}

```

- [ ] **Step 5: Replace the RID matrix in `publish.yml`**

In `.github/workflows/publish.yml`, replace the whole `pack-rid:` job (from its `pack-rid:` line up to, not
including, the `pack-any:` job line) with this block. It is Step 4's block with `pack-rid:` and
`needs: version` in place of the job name and `name:` lines, and the tag's version instead of `0.0.0-ci`:

```yaml
  pack-rid:
    needs: version
    strategy:
      fail-fast: false
      matrix:
        include:
          - rid: linux-x64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-latest
            test-image: ''
            glibc: true
            run-integration-suite: true
          - rid: linux-arm64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-24.04-arm
            test-image: ''
            glibc: true
            run-integration-suite: false
          - rid: linux-musl-x64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-latest
            test-image: mcr.microsoft.com/dotnet/sdk:10.0-alpine
            glibc: false
            run-integration-suite: false
          - rid: linux-musl-arm64
            pack-runner: ubuntu-latest
            test-runner: ubuntu-24.04-arm
            test-image: mcr.microsoft.com/dotnet/sdk:10.0-alpine
            glibc: false
            run-integration-suite: false
          - rid: osx-arm64
            pack-runner: macos-latest
            test-runner: macos-latest
            test-image: ''
            glibc: false
            run-integration-suite: false
    uses: ./.github/workflows/aot-package.yml
    with:
      rid: ${{ matrix.rid }}
      version: ${{ needs.version.outputs.version }}
      pack-runner: ${{ matrix.pack-runner }}
      test-runner: ${{ matrix.test-runner }}
      test-image: ${{ matrix.test-image }}
      glibc: ${{ matrix.glibc }}
      run-integration-suite: ${{ matrix.run-integration-suite }}

```

Nothing else in `publish.yml` changes: its gather step derives the RID list from the pointer package and
already fails if a listed package or its `symbols-<rid>` artifact is missing; the extra `packlog-<rid>`
artifacts it downloads are ignored.

- [ ] **Step 6: Lint the workflows**

```bash
/tmp/wprobe/venv/bin/actionlint -shellcheck /tmp/wprobe/venv/bin/shellcheck .github/workflows/*.yml && echo ok
grep -rn "win-x64" .github src --include=*.yml --include=*.csproj
```

Expected: `ok`; `grep` prints nothing (no workflow row, RID entry or symbols path names win-x64).

- [ ] **Step 7: Check the pointer lists exactly the new RIDs**

```bash
rm -rf artifacts/aot/pointer
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.0-local -o artifacts/aot/pointer
unzip -p artifacts/aot/pointer/DotnetTokenKiller.0.0.0-local.nupkg tools/any/any/DotnetToolSettings.xml | grep -o 'RuntimeIdentifier="[^"]*"'
```

Expected, in any order: `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-arm64`, `any`;
no `win-x64`.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/aot-package.yml .github/workflows/fallback-package.yml .github/workflows/ci.yml .github/workflows/publish.yml src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj
git commit -F <message file>
```

Message: `ci: pack Linux AOT in cross-build images, test musl in Alpine, and move Windows to the fallback`, with
a body summarizing the two Windows probe findings (Git Bash cannot run dtk.cmd; cmd re-parses arguments) and
the new job shape.

---

### Task 6: Measure the static link

No code changes. The spec's measurement protocol: three installed linux-x64 AOT tools, back to back in one
session. Output goes under `artifacts/aot-measure/`.

**Files:** none committed.

- [ ] **Step 1: Prepare the measurement helpers**

Confirm `artifacts/native-aot/startup-extra.py` exists (`ls artifacts/native-aot/startup-extra.py`). If it does
not, recreate it with the Write tool from the "Measurement Commands" section of
`docs/superpowers/plans/2026-09-13-native-aot.md`, verbatim.

Create `artifacts/aot-measure/feed.nuget.config` with the Write tool:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="/home/cloudcli/projects/DotnetTokenKiller/artifacts/aot-measure/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotnetTokenKiller*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- [ ] **Step 2: Pack the three variants**

Variant 1 is today's package: `develop`, packed on the host. Variant 2 is cross-built with the static link
turned off. Variant 3 is what ships. Each gets its own version so they share one feed:

```bash
rm -rf artifacts/aot-measure/feed artifacts/aot-measure/tools-* /tmp/dtk-measure-develop
mkdir -p artifacts/aot-measure/feed
git worktree add /tmp/dtk-measure-develop develop
(cd /tmp/dtk-measure-develop && dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -p:Version=0.0.1-m1 -o /home/cloudcli/projects/DotnetTokenKiller/artifacts/aot-measure/feed && dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.1-m1 -o /home/cloudcli/projects/DotnetTokenKiller/artifacts/aot-measure/feed)
sh eng/aot/pack-linux.sh linux-x64 0.0.1-m2 artifacts/aot-measure/feed artifacts/aot-measure/pack-m2.log -p:DtkLinkSqliteStatically=false
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.1-m2 -o artifacts/aot-measure/feed
sh eng/aot/pack-linux.sh linux-x64 0.0.1-m3 artifacts/aot-measure/feed artifacts/aot-measure/pack-m3.log
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version=0.0.1-m3 -o artifacts/aot-measure/feed
for m in m1 m2 m3; do
  rm -rf ~/.nuget/packages/dotnettokenkiller/0.0.1-$m ~/.nuget/packages/dotnettokenkiller.linux-x64/0.0.1-$m
  dotnet tool install --tool-path artifacts/aot-measure/tools-$m --configfile artifacts/aot-measure/feed.nuget.config DotnetTokenKiller --version 0.0.1-$m
  test -d artifacts/aot-measure/tools-$m/.store/dotnettokenkiller/0.0.1-$m/dotnettokenkiller.linux-x64 && echo "$m: linux-x64 installed"
done
unzip -l artifacts/aot-measure/feed/DotnetTokenKiller.linux-x64.0.0.1-m2.nupkg | grep libe_sqlite3
unzip -l artifacts/aot-measure/feed/DotnetTokenKiller.linux-x64.0.0.1-m3.nupkg | grep -c libe_sqlite3
```

Expected: three `linux-x64 installed` lines; m2's package contains `libe_sqlite3.so`; m3's count is `0`.

- [ ] **Step 3: Run the measurements back to back**

For each of `m1`, `m2`, `m3` in that order (set the Bash timeout to `600000` for each `cold-start`):

```bash
uptime
binary="$(readlink -f artifacts/aot-measure/tools-<m>/dtk)"
set -o pipefail; dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start "$binary" 2>&1 | tee artifacts/aot-measure/<m>-cold-start.txt
python3 artifacts/native-aot/startup-extra.py "$binary" | tee artifacts/aot-measure/<m>-extra.txt
```

Each `cold-start` must print all three sections and its `Built:` line must name that binary; an exception
means a sample was rejected and the figures are unusable (rerun that variant).

- [ ] **Step 4: Apply the stop rule and report**

Report, per variant: pipe median; instant-child overhead median; 1000 ms-child overhead median;
`dtk --version` median; tracking-off median; and the `uptime` load average. If m3's pipe median exceeds m1's
by more than 5 ms, stop and report BLOCKED with the figures: the user decides whether to continue.

- [ ] **Step 5: Clean up the worktree**

```bash
git worktree remove /tmp/dtk-measure-develop
```

No commit.

---

### Task 7: Document the platforms, the scripts and the figures

**Files:**

- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `src/DotnetTokenKiller.Cli/README.md`
- Modify: `docfx/articles/getting-started.md`
- Modify: `docs/superpowers/specs/2026-09-13-native-aot-design.md`
- Modify: `docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md`

**Interfaces:**

- Consumes: Task 6's reported figures.

- [ ] **Step 1: Update CLAUDE.md's commands**

In the Commands code block, replace these lines:

```bash
# Publish the CLI as Native AOT for this machine (linux-x64 here; the native compile cannot cross OSes)
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot

# Pack the tool as CI does: the RID package (on its own OS), the pointer, and the framework-dependent fallback.
# A plain `dotnet pack` now builds only the pointer; IncludeSymbols=true breaks the pointer and RID packs.
dotnet pack src/DotnetTokenKiller.Cli -c Release -r linux-x64 -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -o artifacts/feed
```

with:

```bash
# Publish the CLI as Native AOT for this machine (linux-x64 here; the native compile cannot cross OSes).
# Without a sysroot the binary needs this machine's glibc; the shipped packs use eng/aot/pack-linux.sh.
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot

# Pack the tool as CI does. Linux RIDs pack in Docker, in Microsoft's cross-build image for the RID (~7 GB
# each); osx-arm64 packs natively with `dotnet pack -r osx-arm64 -p:IncludeSymbols=false`. A plain
# `dotnet pack` builds only the pointer; IncludeSymbols=true breaks the pointer and RID packs.
sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -o artifacts/feed

# Check a glibc package's floor, install and test a packed RID package (musl: put
# `--image mcr.microsoft.com/dotnet/sdk:10.0-alpine` first), and smoke-test the installed tool on Rocky Linux 8
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg
sh eng/aot/test-package.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log artifacts/aot/tools/linux-x64
sh eng/aot/smoke-old-glibc.sh artifacts/aot/tools/linux-x64
```

- [ ] **Step 2: Record the figures in CLAUDE.md**

In the Benchmarks section, directly after the paragraph that begins `Native AOT, measured 2026-09-13,`, add a
paragraph with Task 6's medians (use the date of the measurement):

```markdown
Static SQLite, measured <date>, three linux-x64 AOT tools installed from a local feed: host-built with a
dynamic `libe_sqlite3.so` (the package before the cross-sysroot work) → cross-built against the glibc 2.27
sysroot, still dynamic (`-p:DtkLinkSqliteStatically=false`) → cross-built with SQLite linked in (what ships):
pipe <m1> → <m2> → <m3> ms; wrapped overhead <m1> → <m2> → <m3> ms (instant child) and <m1> → <m2> → <m3> ms
(1000 ms child); `dtk --version` <m1> → <m2> → <m3> ms; tracking off <m1> → <m2> → <m3> ms.
```

Replace every `<m1>`, `<m2>`, `<m3>` with the reported median to one decimal place. No placeholder may remain.

- [ ] **Step 3: Rewrite CLAUDE.md's "Native AOT" opening paragraph**

Replace the first paragraph of the `## Native AOT` section with these three paragraphs. That paragraph begins
"The tool ships as RID-specific packages: native AOT for linux-x64, linux-arm64, osx-arm64 and win-x64," and
ends "measure the installed `any` package for fallback figures.":

```markdown
The tool ships as RID-specific packages: native AOT for linux-x64, linux-arm64, linux-musl-x64,
linux-musl-arm64 and osx-arm64, and the framework-dependent `any` package everywhere else, Windows included
(`ToolPackageRuntimeIdentifiers` in the CLI csproj). Windows has no AOT package because the SDK's shim for a
native tool is a `dtk.cmd` batch file: Git Bash, Claude Code's shell on Windows, cannot run it, and cmd
re-parses `| & ^ %` in arguments from pwsh (docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md).
The musl RIDs must stay listed: the SDK's RID graph maps them to the glibc RIDs, so Alpine would otherwise
install a binary that cannot run there.

CI's `aot-package.yml` packs each Linux RID in Microsoft's cross-build image against its sysroot
(`eng/aot/pack-linux.sh`: glibc 2.27, the floor .NET supports, or musl 1.2.3) and osx-arm64 on macOS, then
installs and tests every package on a runner of its own architecture (`eng/aot/test-package.sh`, inside
`mcr.microsoft.com/dotnet/sdk:10.0-alpine` for musl). The glibc packages must also pass
`eng/aot/check-glibc-floor.sh` (no symbol above GLIBC_2.27) and `eng/aot/smoke-old-glibc.sh` (start, filter
and track on Rocky Linux 8). `fallback-package.yml` tests `any` on Linux and on Windows, where it also runs the
integration suite and a smoke test from Git Bash, pwsh and cmd. `publish.yml` pushes the RID packages before
the pointer. A local Release build of the CLI carries the AOT feature switches in its runtimeconfig
(`PublishAot=true` in the csproj), so `cold-start` on `bin/Release` measures an AOT-like JIT build, not the
shipped fallback; measure the installed `any` package for fallback figures.

linux-x64 and linux-arm64 link SQLite statically (`DtkLinkSqliteStatically` in the CLI csproj), because
SQLitePCLRaw's `libe_sqlite3.so` needs GLIBC_2.34 (ericsink/SQLitePCL.raw#674). glibc 2.27 has no `fcntl64`,
so the `CompileFcntl64Shim` target compiles `Native/fcntl64.c` and passes the object as a `LinkerArg`. Do not
turn it into a `NativeLibrary` item (it is added after the ILC targets copy those into the link, which then
fails with "undefined symbol: fcntl64") or a `-Wl,--defsym` alias (lld rejects it). `-p:DtkLinkSqliteStatically=false`
packs a dynamic build for comparisons. A local publish without `-p:SysRoot` compiles the shim with the host's
clang and needs the host's glibc; only `pack-linux.sh` packs carry the 2.27 floor. The `any` package still
cannot track on glibc < 2.34. With SQLite linked in, `LD_DEBUG=files` no longer shows whether tracking off
loads SQLite; `SqliteLoaderTests` checks that on macOS with `DYLD_PRINT_LIBRARIES`.
```

- [ ] **Step 4: Update the install paragraph in README.md, the package README and getting-started**

In each of `README.md`, `src/DotnetTokenKiller.Cli/README.md` and `docfx/articles/getting-started.md`, replace
the paragraph

```markdown
On Linux (x64 and arm64), macOS on Apple silicon and Windows x64, this installs a natively compiled
`dtk` that starts in milliseconds and needs no .NET runtime to run. Other platforms get the
framework-dependent build, which runs on the .NET 10 runtime.
```

with:

```markdown
On Linux (x64 and arm64, glibc 2.27 or later, or musl as on Alpine) and macOS on Apple silicon, this installs
a natively compiled `dtk` that starts in milliseconds and needs no .NET runtime to run; on Linux it needs ICU
(`libicu`, or `icu-libs` on Alpine), as .NET does. Windows and every other platform get the
framework-dependent build, which runs on the .NET 10 runtime: on Windows, the SDK's launcher for a native tool
cannot be started from Git Bash and re-parses `|`, `&`, `^` and `%` in arguments. The same command,
`dotnet tool update`, tool manifests and `dnx` all pick the right package for the machine.
```

In all three files, add after that paragraph:

```markdown
The framework-dependent build cannot record token savings on glibc older than 2.34, because its SQLite
library needs GLIBC_2.34 ([ericsink/SQLitePCL.raw#674](https://github.com/ericsink/SQLitePCL.raw/issues/674));
filtering still works. Only Linux architectures without a native package (32-bit ARM, for example) get that
build.
```

- [ ] **Step 5: Point the old spec at the new one, and mark the new spec implemented**

In `docs/superpowers/specs/2026-09-13-native-aot-design.md`, under `### Open issues found by the final review`,
change item 4 to:

```markdown
4. **Status:** addressed by [the Linux and Windows AOT spec](2026-09-13-linux-windows-aot-design.md):
   cross-sysroot and musl RID packages, SQLite linked into the glibc binaries, and Windows on the `any`
   package.
```

In `docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md`, change the first line to:

```markdown
**Status:** Implemented — see [the plan](../plans/2026-09-13-linux-windows-aot.md).
```

- [ ] **Step 6: Lint and verify the docs**

```bash
npx --yes markdownlint-cli2 CLAUDE.md README.md src/DotnetTokenKiller.Cli/README.md docfx/articles/getting-started.md docs/superpowers/specs/2026-09-13-native-aot-design.md docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md
grep -n "Windows x64\|<m1>\|<m2>\|<m3>\|<date>" CLAUDE.md README.md src/DotnetTokenKiller.Cli/README.md docfx/articles/getting-started.md
dtk dotnet build tests/DotnetTokenKiller.Cli.IntegrationTests && dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~DocsBindingTests"
```

Expected: markdownlint reports `0 issues` (fix any MD032 by adding blank lines around lists); `grep` prints
nothing; `DocsBindingTests` pass.

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md README.md src/DotnetTokenKiller.Cli/README.md docfx/articles/getting-started.md docs/superpowers/specs/2026-09-13-native-aot-design.md docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md
git commit -F <message file>
```

Message: `docs: document the Linux, musl and Windows packages, the eng/aot scripts and the static-link figures`.

**Status:** Implemented — see [the plan](../plans/2026-09-13-linux-windows-aot.md).

## Context

Sub-project 3 of the startup-speed effort ([spec](2026-09-13-native-aot-design.md), PR #147, merged
into `develop` as `ec991a4`) ships dtk as .NET 10 RID-specific tool packages: Native AOT for
linux-x64, linux-arm64, osx-arm64 and win-x64, a framework-dependent `any` fallback, and a pointer
package listing them. Its final review left four Linux issues open ("Open issues found by the final
review" in that spec), and the win-x64 package's shim was never exercised from a real shell. No
release has been tagged since; `develop` carries the problems described here.

`cold-start` medians on the measuring machine (Ryzen 5 3600, Linux, 2026-09-13), both builds
installed from a local feed:

| Scenario | JIT (`any` fallback) | Native AOT (linux-x64) |
|---|---:|---:|
| `pipe build` | 238.9 ms | 65.0 ms |
| wrapped overhead, instant child | 225.5 ms | 63.6 ms |
| wrapped overhead, 1000 ms child | 186.8 ms | 29.4 ms |
| `dtk --version` | 112.9 ms | 12.5 ms |
| tracking off | 207.2 ms | 14.4 ms |

### Probe results: the Windows shim (2026-09-13)

A throwaway branch (`probe/windows-shim`, runs 34770961298 and 34771338814, `windows-latest`, SDK
10.0.401, Git Bash 5.3.15, PowerShell 7.6.5 with `$PSNativeCommandArgumentPassing = Windows`)
installed the win-x64 AOT package and, as a control, the `any` package, each from a local feed.

For a tool with the `executable` runner, the SDK writes the Windows shim as a batch file
(`ShellShimRepository.cs` in dotnet/sdk). Installed, it reads:

```bat
@echo off
"%~dp0.store\dotnettokenkiller\<version>\dotnettokenkiller.win-x64\<version>\tools\any\win-x64\dtk.exe" %*
```

The SDK has no alternative: packaged shims exist only for the `dotnet` runner, which is what the `any`
package uses (its shim is `dtk.exe`, an apphost). The related Ctrl+C "Terminate batch job (Y/N)?"
prompt is dotnet/sdk#49662 (open, Backlog); the fix it proposes, an extra `.ps1` shim, would not help
Git Bash.

Round 1, resolution and basics:

| | AOT (`dtk.cmd`) | `any` (`dtk.exe`) |
|---|---|---|
| Git Bash: `command -v dtk`, `dtk --version`, `bash -c 'dtk --version'` | **command not found (127)** | works |
| Git Bash: `dtk.cmd --version` | works | not applicable |
| pwsh and cmd: `dtk --version`, `dtk pipe build` over a fixture | works | works |
| exit codes through the shim | preserved | preserved |

Round 2, arguments. `dtk config set tracking.enabled <value>` echoes the value it received:

| Argument | Shell | AOT (`dtk.cmd`, `%*` re-parsed by cmd) | `any` |
|---|---|---|---|
| `x\|y&z`, `Name~A\|Name~B` | pwsh; Git Bash via `dtk.cmd` | **cmd runs the text after `\|` as a command** (exit 255) | verbatim |
| `a^b` | pwsh; Git Bash via `dtk.cmd` | **`ab`** | verbatim |
| `%OS%` | pwsh; Git Bash via `dtk.cmd` | **`Windows_NT`** | verbatim |
| `a"b` | pwsh | **`ab`** | verbatim |
| `a b"c\|d` | pwsh; Git Bash via `dtk.cmd` | **breaks at `\|`** | verbatim |
| unquoted `a^^b` (the caller yields `a^b`) | cmd | **`ab`** | `a^b` |
| a `.bat` running `dtk --version` without `call` | cmd | **the rest of the batch file never runs**, exit 0 | continues |
| quoted `"x\|y&z"`, `!OS!`, `dtk dotnet --version` | all | same as `any` | same |

pwsh and Git Bash pass an argument without spaces unquoted, so cmd re-parses its `| & ^ %`
characters. An `&` in an argument therefore runs whatever follows it as a command.

### Probe results: Linux (2026-09-13)

Run with Docker on the measuring machine against a throwaway copy of `develop` at `ec991a4`. The build
image was `mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-amd64` with the
SDK copied in (7.15 GB; clang 20.1.8; glibc 2.27 sysroot at `/crossrootfs/x64`).

- **`-Wl,--defsym,fcntl64=fcntl` does not link.** lld reports "symbol not found: fcntl": `fcntl` lives
  in `libc.so`, and `--defsym` needs a defined symbol. The C shim from the earlier probe is required.
- **Ordering trap.** A target that runs after `SetupOSSpecificProps` (it needs `$(TargetTriple)`) and
  adds the shim object as a `NativeLibrary` item has no effect: `SetupOSSpecificProps` has already
  copied `@(NativeLibrary)` into `LinkerArg`, and the link fails with "undefined symbol: fcntl64".
  Adding the object as a `LinkerArg` works.
- **The static-SQLite linux-x64 package** holds only `dtk` and `DotnetToolSettings.xml` (9.4 MB).
  `readelf --version-info` lists GLIBC_2.2.5 through GLIBC_2.16, nothing higher. `NEEDED` is `libdl`,
  `librt`, `libm`, `libpthread`, `libc` and `ld-linux-x86-64`.
- **Old distros.** In `rockylinux:8` (glibc 2.28) and `ubuntu:18.04` (glibc 2.27), with only ICU
  installed, `dtk --version` and `pipe build` over `dotnet_build_errors.txt` work, and `gain --json`
  then reports one tracked command (710 → 138 tokens), so static SQLite works there.
- **Alpine.** In `mcr.microsoft.com/dotnet/sdk:10.0-alpine`, with `linux-musl-x64` added to
  `ToolPackageRuntimeIdentifiers`, a musl AOT pack installs as `dotnettokenkiller.linux-musl-x64`
  (with its musl `libe_sqlite3.so`), and the `…IntegrationTests.Aot` tests pass against it: 37 of 37,
  parity and pack-log check included, unmodified.
- **Other install paths.** Against a local feed on linux-x64, a tool-manifest install (`dotnet tool
  install DotnetTokenKiller`, run as `dotnet dtk`), `dotnet tool restore` from an empty package cache,
  and `dotnet dnx DotnetTokenKiller` all fetch `dotnettokenkiller.linux-x64` and run the native binary.

### nuget.org (2026-09-13)

`DotnetTokenKiller` is registered (owner HandyS11, 0.7.0–0.7.2) with `verified: false`: its ID prefix
is not reserved. `DotnetTokenKiller.linux-x64`, `.linux-arm64`, `.linux-musl-x64`, `.linux-musl-arm64`,
`.osx-arm64` and `.any` do not exist yet.

## Problem

- **musl.** The SDK's RID graph maps `linux-musl-x64` and `linux-musl-arm64` to `linux-x64` and
  `linux-arm64`, so Alpine installs the glibc AOT package, which cannot run there.
- **glibc floor.** The Linux AOT `dtk` built on `ubuntu-latest` needs GLIBC_2.34 and does not start on
  the glibc 2.27–2.33 distributions that the framework-dependent tool runs on (RHEL/Rocky/Alma 8
  among them).
- **SQLite.** SQLitePCLRaw.lib.e_sqlite3 3.53.3's glibc `libe_sqlite3.so` also needs GLIBC_2.34
  (ericsink/SQLitePCL.raw#674). Downgrading to 2.1.11 brings back GHSA-2m69-gcr7-jv3q.
- **Windows.** The win-x64 AOT package's `dtk.cmd` shim is unreachable from Git Bash, which is the
  shell of Claude Code's Bash tool on Windows, so the rewrite hook turns every `dotnet build` into
  "command not found". It also corrupts arguments from pwsh and Git Bash and silently truncates batch
  files.

## Decisions

1. **Linux: cross-sysroot builds and musl RIDs.** linux-x64 and linux-arm64 are built against the glibc
   2.27 sysroot in Microsoft's cross images, the same floor as .NET itself. linux-musl-x64 and
   linux-musl-arm64 AOT packages are added, built against the Alpine 3.17 sysroot (musl 1.2.3). All
   four Linux RIDs pack in cross images.
2. **SQLite: static in the glibc AOT binaries.** linux-x64 and linux-arm64 link SQLitePCLRaw's
   `libe_sqlite3.a` plus a C `fcntl64` shim. musl, macOS and the `any` package keep dynamic loading.
   The `any` package's silent tracking failure on glibc < 2.34 predates this work; it is documented,
   not fixed.
3. **Windows: no AOT package.** win-x64 leaves `ToolPackageRuntimeIdentifiers`, so every Windows
   machine installs `any`, whose `dtk.exe` passed every probe case in every shell. Windows keeps JIT
   start-up. Adding win-x64 back is one RID entry plus matrix rows, once the SDK can generate an
   executable shim.
4. **CI containers run through `docker run` and committed POSIX scripts** in `eng/aot/`, not job-level
   `container:`. A job container is pulled before any step can free runner disk, JavaScript actions
   fail in Alpine containers on arm64, and the scripts run locally exactly as in CI.
5. **glibc floor guard:** a `readelf` check on the packed files (fail above GLIBC_2.27), plus a smoke run
   in `rockylinux:8` on each glibc arch.
6. **Tracking-off loader check** moves to macOS as a gated C# test with a positive control. Static
   linking makes `LD_DEBUG=files` meaningless on glibc, and musl has no equivalent.
7. **Windows CI** runs the `any` package installed on `windows-latest`: parity, the whole integration
   suite, and a smoke test from Git Bash, pwsh and cmd.

## Solution

### 1. Platforms and packages

`ToolPackageRuntimeIdentifiers` becomes
`linux-x64;linux-arm64;linux-musl-x64;linux-musl-arm64;osx-arm64;any`. A release has seven packages:
the pointer, five AOT RID packages and `any`.

| Machine | Package | Contents | Needs at run time |
|---|---|---|---|
| glibc Linux x64 / arm64 | `DotnetTokenKiller.linux-x64` / `.linux-arm64` | `dtk`, SQLite linked in | glibc ≥ 2.27, ICU |
| Alpine x64 / arm64 | `DotnetTokenKiller.linux-musl-x64` / `.linux-musl-arm64` | `dtk`, `libe_sqlite3.so` | ICU (`icu-libs`) |
| macOS on Apple silicon | `DotnetTokenKiller.osx-arm64` (unchanged) | `dtk`, `libe_sqlite3.dylib` | nothing |
| Windows (any architecture), Intel macOS, every other RID | `DotnetTokenKiller.any` (unchanged) | framework-dependent `dtk.dll`, apphost | .NET 10 runtime |

Every machine installs with `dotnet tool install -g DotnetTokenKiller`, which needs the .NET 10 SDK
(dtk wraps `dotnet`, so its users have it). The SDK picks the package for its own RID and falls back
through the RID graph to `any`. Tool manifests, `dotnet tool restore` and `dnx` resolve the same way.

A local `dotnet publish -r linux-x64` without a sysroot still works: the shim is compiled with the
host's clang, and the binary needs the host's glibc. Only packs made in the cross images carry the
2.27 floor.

### 2. Build mechanics

**CLI csproj.**

- `ToolPackageRuntimeIdentifiers` as in 1.
- `<PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" GeneratePathProperty="true"/>`. The package
  is already a transitive dependency and its version stays pinned in `Directory.Packages.props`;
  `$(PkgSQLitePCLRaw_lib_e_sqlite3)` locates the static library without repeating the version.
- For AOT builds whose RID is `linux-x64` or `linux-arm64` only:
  - `<DirectPInvoke Include="e_sqlite3"/>` and
    `<NativeLibrary Include="$(PkgSQLitePCLRaw_lib_e_sqlite3)/runtimes/$(RuntimeIdentifier)/native/libe_sqlite3.a"/>`.
  - A `CompileFcntl64Shim` target, `AfterTargets="SetupOSSpecificProps"` and
    `BeforeTargets="LinkNative"`, compiles `src/DotnetTokenKiller.Cli/Native/fcntl64.c` into
    `$(IntermediateOutputPath)native/fcntl64.o` with `$(CppCompilerAndLinker)`, adding
    `--sysroot="$(SysRoot)"` and `--target=$(TargetTriple)` when those are set, and adds the object
    as a `LinkerArg` (not a `NativeLibrary`; see the ordering trap above).
  - `KeepAotToolPackageRuntimeOnly` also removes `libe_sqlite3.so` from `ResolvedFileToPublish`.
- Comments say why: glibc 2.27 has no `fcntl64` (it arrived in 2.28, and on 64-bit Linux it is the same
  call as `fcntl`); the 3.53.3 `.so` needs GLIBC_2.34 (#674); and the ordering trap.

`fcntl64.c`:

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

**Scripts in `eng/aot/`.** POSIX `sh` with `set -eu` and no bash-isms, runnable locally with the same
arguments as in CI:

- **`pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments]`** runs the RID's
  cross image through `docker run`, with the checkout, a NuGet package cache, the feed, the log directory
  and the host's SDK mounted at their host paths, as the invoking user
  (`--user $(id -u):$(id -g)`). In CI the host SDK comes from `actions/setup-dotnet`, pinned by
  `global.json`, so no image is built (verified while planning: the mounted SDK packs in all four
  images). The script maps the RID to its
  image (`-cross-amd64`, `-cross-arm64`, `-cross-amd64-musl`, `-cross-arm64-musl`) and sysroot
  (`/crossrootfs/x64` or `/crossrootfs/arm64`), and packs with `-p:SysRoot=… -p:LinkerFlavor=lld
  -p:IncludeSymbols=false -p:Version=<version>`, writing the MSBuild file log that `AotWarningLogTests`
  reads. Running as the invoking user keeps git working on the mounted checkout. If git failed, the
  AOT binary's informational version would lose its commit SHA, and parity's `--version` case would fail.
- **`check-glibc-floor.sh <nupkg> [max]`** (max defaults to 2.27) unzips the package and runs
  `readelf --version-info` on every ELF file in it. It prints the highest `GLIBC_` version found and
  exits non-zero if that is above the max. A package with no ELF file is an error.
- **`test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]`**,
  given a feed directory that holds the RID package, does the following:
  1. Builds the solution at that version.
  2. Packs the pointer into the feed and installs the tool with `--tool-path`, through a generated
     `nuget.config` that maps `DotnetTokenKiller*` to the feed and everything else to nuget.org.
  3. Asserts that the store holds `dotnettokenkiller.<rid>`.
  4. Runs the `DotnetTokenKiller.Cli.IntegrationTests.Aot` tests with `DTK_AOT_REQUIRED=1`,
     `DTK_AOT_BINARY` and `DTK_AOT_PACK_LOG` set.
  5. With the flag, runs the whole integration suite with `DTK_TEST_BINARY` set.

  It runs natively on glibc Linux and macOS. With `--image mcr.microsoft.com/dotnet/sdk:10.0-alpine`
  (the musl RIDs) it re-runs itself in that image as the invoking user, with the checkout and NuGet cache
  mounted at their host paths. That image is multi-arch, so musl arm64 runs natively on an arm64 runner.
  It deletes the package's ID and version from the NuGet cache before installing, since a cached package
  would be installed instead of the feed's.
- **`smoke-old-glibc.sh <tools-dir>`** runs itself inside `docker run rockylinux:8`. It installs `libicu`, then
  runs the installed `dtk` with hermetic state: `--version`, then `pipe build --exit-code 1` over
  `dotnet_build_errors.txt` with tracking on. `gain --json` must then report `TotalCommands` of 1.

### 3. CI workflows

**`aot-package.yml` (reusable)** takes `rid`, `version`, `pack-runner`, `test-runner`, `test-image`
(empty means native), `glibc` and `run-integration-suite`, and has two jobs:

1. **`pack`** on `pack-runner`:
   - Linux: frees runner disk (the preinstalled Android, GHC and CodeQL toolchains), then runs
     `eng/aot/pack-linux.sh`.
   - macOS: runs today's `dotnet pack -r osx-arm64`.
   - glibc RIDs: runs `check-glibc-floor.sh` on the packed RID package.
   - Uploads `package-<rid>`, `packlog-<rid>` and `symbols-<rid>`.
2. **`test`** on `test-runner`, needing `pack`: downloads the package and pack log, then runs
   `eng/aot/test-package.sh`.
   - glibc Linux and macOS: runs it natively after `actions/setup-dotnet`.
   - musl: runs it inside `test-image`.
   - glibc RIDs: then runs `smoke-old-glibc.sh` on the installed tool.

Both jobs set `timeout-minutes`.

**Matrix rows**, identical in `ci.yml` (`aot`) and `publish.yml` (`pack-rid`):

| rid | pack-runner | test-runner | test-image | glibc | run-integration-suite |
|---|---|---|---|---|---|
| linux-x64 | ubuntu-latest | ubuntu-latest | | true | true |
| linux-arm64 | ubuntu-latest | ubuntu-24.04-arm | | true | false |
| linux-musl-x64 | ubuntu-latest | ubuntu-latest | mcr.microsoft.com/dotnet/sdk:10.0-alpine | false | false |
| linux-musl-arm64 | ubuntu-latest | ubuntu-24.04-arm | mcr.microsoft.com/dotnet/sdk:10.0-alpine | false | false |
| osx-arm64 | macos-latest | macos-latest | | false | false |

**`fallback-package.yml`** gains an OS matrix with `ubuntu-latest` and `windows-latest`.

Both rows:

- pack `any` and the check-only pointer (`-p:ToolPackageRuntimeIdentifiers=any`)
- install from the local feed and assert `dotnettokenkiller.any`
- run `AotParityTests` against the installed shim

The Windows row also:

- runs the whole integration suite with `DTK_TEST_BINARY` set to the installed `dtk.exe`, which takes
  over the coverage of the removed win-x64 AOT row
- runs a shell smoke in Git Bash, pwsh and cmd. Each shell runs `dtk --version` and
  `dtk config set tracking.enabled "Name~A|Name~B"`, and must exit 1 with the value echoed verbatim.
  Git Bash also checks that `command -v dtk` finds the tool.

Only the ubuntu row packs the real pointer and uploads `package-any` and `package-pointer`, so artifact
names stay unique.

**`publish.yml`.**

- The gather and push steps are unchanged: they derive the RID list from the pointer and fail if a
  listed package or its symbols are missing.
- `publish` still needs `test`, `pack-rid` and `pack-any`.
- The GitHub Release carries seven `.nupkg` files, the `.snupkg` and five native-symbol zips.

A CI run grows from 5 package jobs to 12 (5 pack, 5 test, 2 fallback), including four pulls of a
cross image of about 7 GB.

### 4. Tests

- **`SqliteLoaderTests`** (in `Aot/`) replaces sub-project 2's manual `LD_DEBUG=files` check.
  - **Gating.** Its attribute skips it on every OS but macOS. On macOS it follows the parity gating:
    it runs when `DTK_AOT_BINARY` is set, and fails under `DTK_AOT_REQUIRED=1` if the binary is
    missing.
  - **The two runs.** It runs the installed binary directly (not through `/bin/sh`, whose SIP
    protection would strip `DYLD_*`) in a `ParitySandbox`, with `DYLD_PRINT_LIBRARIES=1`, as
    `pipe build --exit-code 1` over `dotnet_build_errors.txt`:
    1. With tracking on, stderr must mention `libe_sqlite3`. This is the positive control.
    2. After `config set tracking.enabled false`, stderr must not mention `libe_sqlite3`.
- **`ParityRunner`:**
  - The hermetic start info moves into `ParityRunner.CreateStartInfo`, and the process handling into
    `ParityProcess.RunAsync`, which returns stdout and stderr separately. The loader test uses both,
    adding its own environment variable.
  - The runner starts draining stdout and stderr before writing stdin. Today it writes all of stdin
    first, a latent deadlock above about 64 KB for a child that writes while reading. dtk is not such a
    child (`pipe` reads all input first), so the regression test runs `cat` over about 900 KB of input
    through `ParityProcess`: it times out before the fix and passes after.
  - Parity output is unchanged.
- **Unchanged:** `AotParityTests`, `AotWarningLogTests`, `CommandSettingsAotGuardTests` and
  `SpectreBuiltInCommandTests`.
- **Scripts, red then green**, in the plan's tasks:
  - `check-glibc-floor.sh` fails on a linux-x64 package packed from `develop` on the host (GLIBC_2.34)
    and passes on the cross-built one.
  - `smoke-old-glibc.sh` fails on a `dtk` published without a sysroot, which needs the host's glibc and
    does not start on Rocky 8, and passes on the installed static-SQLite package.
  - `test-package.sh` passes locally for linux-x64 natively and for linux-musl-x64 in the Alpine image.
- **Local verification:**
  - The library test projects run one at a time.
  - The CLI classes that spawn no builds run with `--filter`, and the `Aot` namespace runs against the
    locally built linux-x64 and linux-musl-x64 packages.
  - CI gates the loader test (macOS), arm64, the Windows row and the build-spawning integration
    classes; the pull request says so.

### 5. Docs

- **CLAUDE.md, "Native AOT":**
  - the RID list, and one paragraph on why Windows gets `any`, pointing here
  - the 2.27 floor, static SQLite and the shim, with a warning not to move the shim object into
    `NativeLibrary`
  - `eng/aot/*.sh`, and running them locally (Docker, about 7 GB per cross image)
  - a local publish without a sysroot needs the host's glibc
- **CLAUDE.md, Commands:** the Linux pack lines use `pack-linux.sh`.
- **CLAUDE.md, measurements:** the three measurements in "Measurement protocol", beside the existing
  figures.
- **`README.md`, `src/DotnetTokenKiller.Cli/README.md` (the package readme),
  `docfx/articles/getting-started.md`:**
  - the same install command everywhere, and what each machine gets (the table in 1)
  - the .NET 10 SDK to install, ICU on Linux
  - the `any` package's tracking failure on glibc < 2.34, linking ericsink/SQLitePCL.raw#674
  - Any docs-binding test over that text changes with it.
- **The native AOT spec:** its "Open issues found by the final review" gets a line pointing here.

## Release prerequisites

Owner actions, not code:

- **Reserve the `DotnetTokenKiller` ID prefix** on nuget.org. The six RID package IDs are unclaimed
  today; until the prefix is reserved anyone can register one first, which blocks the release push and
  serves their package to that RID.
- **Trusted publishing:** confirm the policy lets the workflow create those six IDs.
- **Dry run:** push a pre-release tag such as `v0.8.0-rc.1` (`publish.yml` marks versions containing `-`
  as pre-releases), then install it with `--prerelease` on at least one glibc Linux, one Alpine and one
  Windows machine.

## Measurement protocol

On the measuring machine, `cold-start` runs against three installed linux-x64 AOT tools, one after
another in the same session:

1. **Today:** packed from `develop` on the host (dynamic SQLite, host toolchain).
2. **Cross-built, dynamic SQLite:** packed in the cross image without the static-link items. This
   isolates the toolchain and sysroot.
3. **Cross-built, static SQLite:** what ships.

Each run reports pipe, both wrapped overheads, `dtk --version` and tracking off. Step 3 against step 2
is the static link's own effect. Timings stay ungated. If pipe regresses by more than 5 ms from step 1
to step 3, work stops and the user decides.

## Success criteria

- **CI:**
  - All 12 package jobs pass on the pull request.
  - The floor guard reports at most GLIBC_2.27 for linux-x64 and linux-arm64.
  - The Rocky 8 smoke passes on both architectures.
  - musl parity passes on both architectures.
  - The macOS loader test passes, control included.
  - The Windows shell smoke passes in all three shells.
- **Unchanged:** `savings-baseline.json`, and the repository still has exactly two trim or AOT
  suppressions.
- **Docs:** CLAUDE.md records the three measurements.

## Out of scope

- Items from the native AOT final review that this work does not touch:
  - the `cold-start` header's "no runtimeconfig.json beside the binary" for the `any` apphost
  - scoping `PublishAot=true` to AOT packs (needs its own probe: the pointer pack relies on it)
- win-x64 AOT, until the SDK generates an executable shim for `executable` tools. Filing that upstream
  is the owner's call.
- Fixing the `any` package's SQLite on glibc < 2.34.
- Tokenizer spooling (sub-project 4), filter changes, and replacing Spectre.Console.Cli.
- The `dtk integrate claude` settings-merge bug, fixed on its own branch.

## Amendments (2026-09-13, while writing the plan)

The plan's author prototyped every change in a throwaway clone before writing
[the plan](../plans/2026-09-13-linux-windows-aot.md), and the sections above were corrected to match:

- **Script signatures.** `pack-linux.sh` and `test-package.sh` take explicit feed, log and tools paths,
  and `test-package.sh` takes `--image` to run itself in the Alpine SDK image. `pack-linux.sh` passes
  extra MSBuild arguments through, which the measurement protocol uses for `-p:DtkLinkSqliteStatically=false`.
- **The deadlock regression test uses `cat`**, because dtk's `pipe` reads all input before writing and
  so cannot reproduce it.
- **The red smoke run uses a no-sysroot publish**, which needs the host's glibc (GLIBC_2.38 here).
- **Verified in the prototype:**
  - all four Linux packs through the mounted host SDK, as the invoking user, with the commit SHA intact;
    linux-arm64 needs at most GLIBC_2.17
  - the floor check red on today's host-built package and on a cross-built dynamic one (its
    `libe_sqlite3.so`), green on the static one
  - the Rocky 8 smoke red and green
  - `test-package.sh` for linux-x64 natively and for linux-musl-x64 in Alpine (43 passed, the macOS loader
    test skipped), with no root-owned files
  - the loader test's logic, with `LD_DEBUG=files` swapped in on Linux: it passed against a dynamic build,
    and its control failed against the static one
  - a local no-sysroot publish tracking with SQLite linked in
  - actionlint and shellcheck on every workflow and script

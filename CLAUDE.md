# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

DotnetTokenKiller is a .NET CLI proxy that reduces LLM token usage through dotnet commands.

## Commands

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack to reduce
token usage. The repository registers no hook of its own; `dtk init claude --global` (dtk 0.8.0 or later) installs
one that rewrites these commands.

```bash
# Build
dtk dotnet build DotnetTokenKiller.slnx

# Run tests
dtk dotnet test DotnetTokenKiller.slnx

# Run a single test
dtk dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Format code
dtk dotnet format DotnetTokenKiller.slnx --no-restore

# Verify formatting without making changes
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes

# Inspect package references with filtered output
dtk dotnet list package --outdated

# Run the benchmark suite (Release only; the full run takes tens of minutes)
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*FilterBenchmarks*'

# Measure the end-to-end cold-start cost of the Release build (piped, and wrapping a fake dotnet, the
# fourth scenario printing a 1024 KB generated log; ~4 min, Linux/macOS). It carries the AOT feature
# switches (PublishAot=true in the csproj), so it is neither the shipped `any` fallback nor the AOT
# binary; for those, pass an installed tool's binary path to `cold-start`. `--state-dir <dir>` puts the
# hermetic state (and its tracking database) on a real disk instead of the temp root's default tmpfs.
dotnet build src/DotnetTokenKiller.Cli -c Release
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start [dtk] [--state-dir <dir>]

# Measure the one-time tiktoken vocabulary load (one fresh process per sample, ~10s)
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- tokenizer-load

# Publish the CLI as Native AOT for this machine (linux-x64 here; the native compile cannot cross OSes).
# Without a sysroot the binary needs this machine's glibc; the shipped packs use eng/aot/pack-linux.sh.
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot

# Pack the tool as CI does. Linux RIDs pack in Docker, in Microsoft's cross-build image for the RID (~7 GB
# each); osx-arm64 packs natively with `dotnet pack -r osx-arm64 -p:IncludeSymbols=false`. A plain
# `dotnet pack` builds only the pointer; IncludeSymbols=true breaks the pointer and RID packs.
sh eng/aot/pack-linux.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -o artifacts/feed
dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -o artifacts/feed

# Pack and test the Windows x64 package (Git Bash on Windows; needs the MSVC toolset for the AOT compile).
sh eng/aot/pack-windows.sh 0.0.0-local artifacts/aot/feed artifacts/aot/win-x64 artifacts/aot/pack-win-x64.log
sh eng/aot/test-windows.sh 0.0.0-local artifacts/aot/feed artifacts/aot/tools/win-x64 --compare-any

# Check a glibc package's floor, install and test a packed RID package (musl: put
# `--image mcr.microsoft.com/dotnet/sdk:10.0-alpine` first), and smoke-test the installed tool on Rocky Linux 8
sh eng/aot/check-glibc-floor.sh artifacts/aot/feed/DotnetTokenKiller.linux-x64.0.0.0-local.nupkg
sh eng/aot/test-package.sh linux-x64 0.0.0-local artifacts/aot/feed artifacts/aot/pack-linux-x64.log artifacts/aot/tools/linux-x64
sh eng/aot/smoke-old-glibc.sh artifacts/aot/tools/linux-x64

# Compare an AOT binary with the JIT build, and check a pack log's trim/AOT warnings (skipped unless set)
DTK_AOT_BINARY=/abs/path/to/dtk DTK_AOT_PACK_LOG=/abs/path/to/pack.log dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot"

# Regenerate the savings baseline after intentionally changing a filter
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline

# Inspect code quality with ReSharper CLT (jb is a local dotnet tool)
jb inspectcode DotnetTokenKiller.slnx --output=artifacts/inspectcode.xml --format=Xml

# Apply ReSharper cleanup (reformat + syntax style) — run after build
jb cleanupcode DotnetTokenKiller.slnx --profile="Built-in: Reformat & Apply Syntax Style"
```

`dotnet publish` and `dotnet pack` share `MsBuildDiagnosticReport` — the same error/warning summary `dotnet build`
uses — via `DotnetPublishFilter`/`DotnetPackFilter`, which each add their own success line (`project -> publish dir`,
`Successfully created package '…'`). Either run with `--interactive` (used to prompt for private-feed credentials) is
passed through unfiltered with the terminal attached, never captured, via `PassthroughSubcommands.IsInteractiveFilteredRun`.

`dtk init copilot-cli` (alias `dtk integrate`) installs a GitHub Copilot CLI `preToolUse` hook (`.github/hooks/`)
that runs `dtk hook copilot-cli`, rewriting `dotnet …` to `dtk dotnet …`. Supports `--global` (`~/.copilot/hooks/`).
Distinct from `dtk init copilot` (instruction-only, Copilot IDE). Every hook is `dtk hook <provider>`. Its reply's
`permissionDecision: "allow"` is gated by `DotnetCommandRewriter.IsSimpleCommand`, which must stay fail-closed: any
command substitution (`$(`, backticks), heredoc, or unquoted chaining character makes it return `false` (`"ask"`)
rather than guess a command is safe.

`dtk init codex` writes a shared `AGENTS.md` section, the `.agents/skills/dotnet-token-killer` skill and a
`PreToolUse` hook in `.codex/hooks.json` (`--global`: `$CODEX_HOME` or `~/.codex`, and `~/.agents/skills`). Codex runs
the hook only after the user approves it under `/hooks`, keyed by a hash of the definition, so `dtk hook codex` and its
`timeout` must never change; `dtk doctor` warns until `config.toml` records an approval.

`dtk init opencode` writes the same `AGENTS.md` section and skill plus a generated, stamped `.opencode/plugins/dtk.js`
(`--global`: `$XDG_CONFIG_HOME/opencode` or `~/.config/opencode`). OpenCode has no hook commands: the plugin's
`tool.execute.before` spawns `dtk hook opencode` (no shell) for `bash` commands containing `dotnet` and mutates
`output.args.command` in place. It spawns the absolute path it finds on `PATH`, never a bare `dtk`: on Windows that
would try the project directory first, before OpenCode's permission check. `OpenCodePluginTests` run it under Node;
CI sets `DTK_NODE_REQUIRED=1`.

`dtk init antigravity` writes the shared `AGENTS.md` section and skill plus a `"dtk"` hook group in `.agents/hooks.json`
(`--global`: `~/.gemini/config/hooks.json`, with the section in `~/.gemini/GEMINI.md`). `dtk hook antigravity` replies
`{"decision":"ask","overwrite":{"CommandLine":…}}` — never `allow`, which auto-approves. Hooks run through
`sh -c`/`cmd /c` and a failing hook blocks the command, hence `|| exit 0`. Gate G results are in the PR that added it.

`dtk init <provider> --uninstall` (respects `--dir`/`--global`) removes what that install writes, through
`UninstallHelpers` (every integrator implements `IUninstallIntegrator`): dtk's hook entries (the install's own match),
marked sections, and generated files only when their stamp or exact content proves them dtk's; edited files are kept.
A shared `AGENTS.md`/`GEMINI.md`/copilot instructions/skill stays while another provider's dtk hook is registered in
the same scope. It never edits rtk's config or Codex's `config.toml`. `UninstallIntegrationTests` round-trips every
provider in both scopes against the whole temp tree.

Ctrl+C (SIGINT) and, on Unix only, SIGTERM cancel the wrapped dotnet command (`RunCancellation`, in
`src/DotnetTokenKiller.Cli/Infrastructure/`): SIGTERM cancels at once; Ctrl+C cancels only after a 5 s grace period,
because the terminal usually delivers it to the child too, which then exits on its own; a second signal of either
kind takes the default action and kills dtk immediately. A captured run's cancellation kill-and-reap is bounded by
its own 5 s grace (`ProcessCommandRunner.ReapGracePeriod`). An attached passthrough (`dotnet run`, `dotnet watch` —
anything `PassthroughRunUseCase.KeepsStdioAttached`) leaves the first Ctrl+C to the child instead of starting the
grace period (`RunCancellation.LeaveInterruptToChild`), since the child owns the terminal and may legitimately take
longer to shut down, or never exit.

`DotnetTestFilterTests`' `dotnet_test_mtp_*` fixtures are real `dotnet test` output, not hand-written: a .NET 10 SDK
run of an MSTest 4.0.2 project under Microsoft.Testing.Platform (`"test": {"runner": "Microsoft.Testing.Platform"}`
in `global.json`), with the machine path replaced by `/test/project/root`.

## Git Hooks

The pre-commit hook auto-formats staged `.cs` files and validates `.csproj`/`.props` files. Install it once with:

```bash
git config core.hooksPath .githooks
```

## Benchmarks

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus` holds the fixture corpus, a seeded log generator
and the savings engine; `benchmarks/DotnetTokenKiller.Benchmarks` holds the BenchmarkDotNet suite.

Performance here has two dimensions, gated differently:

- **Token savings** is deterministic and hard-gated. `SavingsBaselineTests` compares every scenario
  against `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` and runs
  as part of `dotnet test`. **Changing a filter's output changes its savings and fails this test.**
  That is intended: regenerate with `update-baseline` and let the diff show how the numbers moved.
- **Timings are never gated.** Shared CI runners vary too much for a threshold to mean anything, so
  the suite runs on demand via the `Benchmarks` workflow and uploads its results as artifacts. See
  [Performance](docs/articles/performance.md) for every dated figure referenced in this section.

Two costs cannot be measured in process and have their own verbs instead of BenchmarkDotNet jobs:

- `cold-start` times the built `dtk` binary end to end in four scenarios, 55 dtk spawns each (the
  wrapped scenarios also spawn the fake child alone 55 times): `dtk pipe build` with a fixture on
  stdin, and `dtk dotnet build` wrapping a generated shell-script `dotnet` on the child's `PATH`
  that either exits at once, sleeps 1000 ms first, or sleeps 1000 ms first and prints a 1024 KB
  generated build log. The wrapped scenarios pair a run of the fake child alone with a run of dtk
  around it on every iteration, alternating which goes first, and report the paired difference as
  dtk's overhead. The instant child is the worst case (background setup can overlap only dtk's own
  work); the sleeping child is the best case (an idle CPU). For output this fixture's size (2.6 KB),
  a real build's cost lies between them; dtk's per-line tee write and flush still run in the pump
  and grow with output size, while token counting now overlaps the child except for the last chunk
  (under 64 K chars) and stderr, so that bracket says nothing about a much larger build log's tee
  cost — the fourth scenario keeps the same idle-CPU sleeping child but swaps in the 1024 KB log,
  isolating the tee-and-final-chunk cost at that size from process-spawn overhead. Every sample must print the build filter's
  summary line, because the real SDK found on `PATH` by mistake also exits 1. The header prints any
  `DOTNET_*`/`COMPlus_*` variables, the binary's `runtimeconfig.json` properties, and a
  `State: <root> (<filesystem>)` line, since all three move the figures: state defaults to the temp
  root (tmpfs on the measuring machine) or, with `--state-dir <dir>`, a caller-chosen directory on a
  real disk. Needs a POSIX shell. See [Performance § Cold start](docs/articles/performance.md#cold-start)
  for measured figures.
- `tokenizer-load` times the one-time tiktoken vocabulary load, **one fresh process per sample**.
  `Microsoft.ML.Tokenizers` caches the parsed vocabulary in internal static state, so an
  in-process benchmark measures a cache hit — microseconds for something that costs about 113 ms.
  Do not "simplify" this back into a `[Benchmark]`; there is no in-process form of it that is not
  a lie. See [Performance § Tokenizer load](docs/articles/performance.md#tokenizer-load) for measured
  figures.

See [Performance](docs/articles/performance.md) for the Native AOT, Static SQLite, Windows x64 packaged shim,
`dtk hook`, and OpenCode plugin cold-start figures.

Both fail loudly — non-zero exit, the child's own output — rather than reporting a fast number they
did not measure. A BenchmarkDotNet run that matches no benchmark also exits non-zero, so a typo in
the workflow's `filter` input cannot go green with an empty artifact.

## Native AOT

The tool ships as RID-specific packages: native AOT for linux-x64, linux-arm64, linux-musl-x64,
linux-musl-arm64 and osx-arm64; win-x64 as a framework-dependent package (`Runner="dotnet"`) whose
packaged shim is that same Native AOT `dtk.exe`; and the framework-dependent `any` package everywhere
else, Windows arm64 included (`ToolPackageRuntimeIdentifiers` in the CLI csproj). win-x64's packaged shim
(`DtkPackagedShim`, target `UseNativeBinaryAsPackagedShim`) is copied by the SDK, by file name and unchecked,
to `~/.dotnet/tools/dtk.exe`, so every shell runs the native binary while `dotnet tool run`, tool manifests
and `dnx` run `dtk.dll` on the runtime; that exe uses `SQLitePCLRaw.bundle_winsqlite3` (`DtkUseWinSqlite3`)
because nothing ships beside it. `eng/aot/pack-windows.sh` verifies the packed shim byte for byte and writes
native symbols to `artifacts/aot/win-x64-symbols/dtk.pdb`; `eng/aot/test-windows.sh` checks the install. A
packaged shim is needed at all because the SDK writes a `dtk.cmd` batch file for a plain native tool: Git Bash,
Claude Code's shell on Windows, cannot run it, and cmd re-parses `| & ^ %` in arguments from pwsh and Git Bash
(docs/superpowers/specs/2026-09-13-linux-windows-aot-design.md). The musl RIDs must stay listed: the SDK's RID
graph maps them to the glibc RIDs, so Alpine would otherwise install a binary that cannot run there.

CI's `aot-package.yml` packs each Linux RID in Microsoft's cross-build image against its sysroot
(`eng/aot/pack-linux.sh`: glibc 2.27, the floor .NET supports, or musl 1.2.3) and osx-arm64 on macOS, then
installs and tests every package on a runner of its own architecture (`eng/aot/test-package.sh`, inside
`mcr.microsoft.com/dotnet/sdk:10.0-alpine` for musl). The glibc packages must also pass
`eng/aot/check-glibc-floor.sh` (no symbol above GLIBC_2.27) and `eng/aot/smoke-old-glibc.sh` (start, filter
and track on Rocky Linux 8). `fallback-package.yml` tests `any` on Linux and on Windows, where it also runs the
integration suite and a smoke test from Git Bash, pwsh and cmd. `publish.yml` pushes the RID packages before
the pointer. The RID list and its runners live only in `aot-rids.yml`, which `ci.yml` and `publish.yml` both
call; add or change a RID there. The Windows shell smoke test is the local action
`.github/actions/smoke-windows-shells`, shared by both package workflows. A local Release build of the CLI
carries the AOT feature switches in its runtimeconfig (`PublishAot=true` in the csproj), so `cold-start` on
`bin/Release` measures an AOT-like JIT build, not the shipped fallback; measure the installed `any` package for
fallback figures.

linux-x64 and linux-arm64 link SQLite statically (`DtkLinkSqliteStatically` in the CLI csproj), because
SQLitePCLRaw's `libe_sqlite3.so` needs GLIBC_2.34 (ericsink/SQLitePCL.raw#674). glibc 2.27 has no `fcntl64`,
so the `CompileFcntl64Shim` target compiles `Native/fcntl64.c` and passes the object as a `LinkerArg`. Do not
turn it into a `NativeLibrary` item (it is added after the ILC targets copy those into the link, which then
fails with "undefined symbol: fcntl64") or a `-Wl,--defsym` alias (lld rejects it). `LinkNative` does not list
the shim object as an input, so after editing only `Native/fcntl64.c` delete `obj/` before publishing again.
`-p:DtkLinkSqliteStatically=false` packs a dynamic build for comparisons. A local publish without `-p:SysRoot`
compiles the shim with the host's C compiler (clang, or gcc through the ILC fallback) and needs the host's
glibc; only `pack-linux.sh` packs carry the 2.27 floor. The `any` package
records runs on glibc < 2.34 but cannot read them there (`gain`, `reset`, retention). With SQLite linked in,
`LD_DEBUG=files` no longer shows whether a tracked run or tracking off loads SQLite; `SqliteLoaderTests`
checks that on macOS with `DYLD_PRINT_LIBRARIES`.

Spectre.Console.Cli does not support Native AOT. dtk keeps it under a contained exception
(docs/superpowers/specs/2026-09-13-native-aot-design.md): both `dtk` and `Spectre.Console.Cli` are
rooted, the only two trim/AOT suppressions are on `SpectreCommandApp.Create` (IL3050) and
`TypeRegistrar.Register` (IL2067), and the native compile's IL2104/IL3053/IL3000 from Spectre stay
warnings. Do not remove those suppressions by making `Register` a no-op: that breaks Spectre's
built-in `dtk cli …` commands (`SpectreBuiltInCommandTests`). Do not give a settings class a
dictionary, value-type array, nullable or converter option without first extending `AotParityTests`:
`CommandSettingsAotGuardTests` fails until you do.

`DTK_TEST_BINARY` runs the CLI integration suite against any dtk binary; `DTK_AOT_BINARY` enables the
parity tests; `DTK_AOT_PACK_LOG` checks a pack log's warnings. CI also sets `DTK_AOT_REQUIRED=1`, which
makes those tests fail instead of skip when either variable is missing. `IsAotCompatible` is on for the three
libraries, so a trim- or AOT-unsafe call fails the normal build.

## Tracking

A tracked run writes one JSON file to `<database file>.pending/` beside the tracking database (by default
`tracking.db.pending/`) and opens no SQLite connection. Readers (`gain`, with `--coverage` and `--export`;
`reset`; retention) fold the journal first under `<database file>.pending/.lock`, claiming files into
`folding-<id>/` and recording the id in the `folds` table, so a fold interrupted at any point is neither lost
nor duplicated. A warm-up folds in the background when 64 or more files wait. `SqliteLoaderTests` uses `gain`
as its positive control for that reason. `:memory:` data sources insert directly.

## Tee Logging

A truncated tee log's sole signal is its body's last line, `[dtk: output truncated at <N> bytes]`
(`TeeTruncationMarker.IsMarkerLine`), matched by exact shape regardless of the cap value. There is no header field
for it: `TeeLogHeader.TryReadFields` rejects any key it does not recognise, so adding one would make a truncated log
unreadable by an older dtk sharing the same tee directory, while a body line has no such compatibility hazard — an
older dtk simply displays it like any other line. Rendering (`TeeTruncationMarker.Render`) and detection are shared
between the writer (`FileTeeSession`) and reader (`TeeLogRenderer`) so the two can never disagree about whether a
log was truncated.

## Architecture & Stack

- **Target framework**: net10.0
- **CLI framework**: Spectre.Console + Spectre.Console.Cli
- **Testing**: xunit + FluentAssertions + Spectre.Console.Testing
- **Package management**: Central via `Directory.Packages.props` — all version numbers go there, `.csproj` files omit
  versions. The one exception is `samples/SampleApp.BadPackage`, whose deliberately nonexistent
  `DotnetTokenKiller.DoesNotExist` reference (it triggers NU1101 for the restore tests) carries a `VersionOverride`
  so that no fake pin sits in `Directory.Packages.props`

## Code Style

Enforced via `.editorconfig` and build-time analyzers (Roslynator, SonarAnalyzer, Microsoft.CodeAnalysis.NetAnalyzers):

- `TreatWarningsAsErrors` is enabled — all analyzer warnings must be resolved
- File-scoped namespaces (`namespace Foo;`)
- `var` preferred throughout
- Private fields: `_camelCase`; async methods must end in `Async`
- Interfaces: `IPascalCase`; type parameters: `TPascalCase`
- Line endings: LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`, 2-space for XML/JSON/YAML

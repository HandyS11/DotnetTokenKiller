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

`dtk init copilot-cli` (alias `dtk integrate`) installs a GitHub Copilot CLI `preToolUse` hook (`.github/hooks/`)
that runs `dtk hook copilot-cli`, rewriting `dotnet …` to `dtk dotnet …`. Supports `--global` (`~/.copilot/hooks/`).
Distinct from `dtk init copilot` (instruction-only, Copilot IDE). Every hook is `dtk hook <provider>`.

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
- **Timings** are never gated. Shared CI runners vary too much for a threshold to mean anything, so
  the suite runs on demand via the `Benchmarks` workflow and uploads its results as artifacts.

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
  real disk. Needs a POSIX shell. Measured 2026-09-13, before → after starting tracking setup in the
  background and turning `TieredPGO` off: pipe 295.9 → 230.2 ms; wrapped overhead 286.6 → 228.2 ms
  (instant child) and 288.4 → 185.5 ms (1000 ms child, wall-clock 1188.0 ms). Measured 2026-09-14,
  tmpfs → ext4, both against the same local AOT publish (host toolchain, SQLite linked in): pipe
  63.9 → 75.5 ms; wrapped overhead 61.9 → 74.9 ms (instant child), 28.8 → 179.1 ms (1000 ms child),
  and 185.7 → 307.1 ms (1000 ms child, 1 MB log). The medians show every scenario costing more on
  disk than on tmpfs, with the 1000 ms-child scenario rising far more (150.3 ms) than pipe or the
  instant child (11.6 ms and 13.0 ms) — more than the ext4 fsync cost alone accounts for — and the
  disk run's variance was much wider throughout (e.g. 1000 ms-child p95 571.4 ms, max 981.8 ms,
  against tmpfs's 25.6–31.8 ms full range). Journal, measured 2026-09-14, baseline → a tracked run
  writing a journal file instead of SQLite, both local AOT publishes: on tmpfs, pipe 63.9 → 65.6 ms;
  wrapped overhead 61.9 → 63.3 ms (instant child), 28.8 → 26.5 ms (1000 ms child), and
  185.7 → 186.9 ms (1000 ms child, 1 MB log); on ext4, pipe 75.5 → 63.4 ms; wrapped overhead
  74.9 → 63.7 ms (instant child), 179.1 → 26.5 ms (1000 ms child), and 307.1 → 188.6 ms (1000 ms
  child, 1 MB log). With no SQLite write left in the run, the ext4 medians sit within 2 ms of tmpfs
  and the ext4 spread closed (1000 ms-child p95 30.8 ms, max 31.8 ms). Streaming count, measured
  2026-09-14, journal → counting stdout in chunks while the child runs, both local AOT publishes: on
  tmpfs, pipe 65.6 → 63.9 ms; wrapped overhead 63.3 → 62.5 ms (instant child), 26.5 → 25.5 ms (1000 ms
  child), and 186.9 → 158.9 ms (1000 ms child, 1 MB log); on ext4, pipe 63.4 → 64.9 ms; wrapped
  overhead 63.7 → 62.7 ms (instant child), 26.5 → 25.8 ms (1000 ms child), and 188.6 → 160.6 ms
  (1000 ms child, 1 MB log). The three unchanged scenarios moved by at most 1.7 ms either way, well
  under the 3 ms ceiling; the 1 MB scenario's overhead dropped 28.0 ms on both filesystems, 2.0 ms
  short of the 30 ms target the spec set for it.
- `tokenizer-load` times the one-time tiktoken vocabulary load, **one fresh process per sample**.
  `Microsoft.ML.Tokenizers` caches the parsed vocabulary in internal static state, so an
  in-process benchmark measures a cache hit — microseconds for something that costs about 113 ms.
  Do not "simplify" this back into a `[Benchmark]`; there is no in-process form of it that is not
  a lie. Measured 2026-09-12: `cl100k_base` median 112.7 ms, `o200k_base` median 173.6 ms, against
  a 287.9 ms pipe cold-start median (before the tracking-path changes) on the same machine.

Native AOT, measured 2026-09-13, JIT (the `any` fallback package installed from a local feed) → AOT
(linux-x64 tool package installed from a local feed; the shim is a symlink to the binary): pipe
238.9 → 65.0 ms; wrapped overhead 225.5 → 63.6 ms (instant child) and 186.8 → 29.4 ms (1000 ms child);
`dtk --version` 112.9 → 12.5 ms; tracking off 207.2 → 14.4 ms. Under AOT, tracking (tokenizer load,
counting, SQLite) is 50.6 ms of the pipe figure.

Static SQLite, measured 2026-09-13, three linux-x64 AOT tools installed from a local feed: host-built with a
dynamic `libe_sqlite3.so` (the package before the cross-sysroot work) → cross-built against the glibc 2.27
sysroot, still dynamic (`-p:DtkLinkSqliteStatically=false`) → cross-built with SQLite linked in (what ships):
pipe 65.0 → 65.7 → 65.2 ms; wrapped overhead 62.7 → 62.8 → 63.9 ms (instant child) and 29.3 → 29.5 → 29.2 ms
(1000 ms child); `dtk --version` 12.5 → 12.7 → 12.5 ms; tracking off 14.3 → 14.6 → 14.6 ms.

Windows x64 packaged shim, measured 2026-09-14, windows-latest, indicative (a shared CI runner): the win-x64 package
is 12,609,768 bytes (12.6 MB) and its native `dtk.exe` shim 16,414,720 bytes; parity tests (43) and the whole
CLI integration suite (392) pass against the installed `dtk.exe`; tracking reaches Windows' own `winsqlite3.dll`
(Windows 10 1903 or later). 21 runs each in Git Bash, medians minus a ~34 ms Git Bash process-start baseline
measured the same way (raw medians in parentheses): `dtk --version` 5.0 ms for the native shim vs 131.0 ms for
`any` (40 vs 166 ms raw); `dtk pipe build` 57.0 ms vs 277.0 ms (90 vs 310 ms raw).

`dtk hook`, measured 2026-09-14, 55 runs each, local AOT publish (linux-x64) against the Python hook it replaced, medians
including a 1.0 ms `/bin/true` fork-and-exec baseline: `dtk hook claude` 9.5 ms (no rewrite) and 9.8 ms
(rewrite); `python3 .claude/hooks/dotnet-to-dtk.py` (this repository's former hook, since deleted) 15.9 ms and
15.9 ms; `dtk --version` 12.9 ms. A harness runs the
hook on every shell tool call, so this is a per-call cost; on the `any` fallback it is the JIT start-up instead.
The hook runs 2.9–3.4 ms *faster* than `--version`, because it returns before the service container and
Spectre are built, which `--version` still constructs — meeting the spec's 3 ms ceiling on hook overhead, a
gap a repeat run confirmed as stable (9.5/10.0 ms hook vs 12.9 ms `--version`).

OpenCode plugin, measured 2026-09-15, OpenCode 1.18.31 (`opencode-ai` from npm) running `opencode run` against a
local fake OpenAI-compatible model that requests one `bash` call, local AOT publish (linux-x64) of dtk first on
`PATH`, 21 runs per configuration in interleaved rounds, plugin absent → installed. `--print-logs` has no per-tool
timing, so the figure is the median interval from the fake model receiving the tool-offering request to OpenCode
logging the `bash` permission check, which runs after the plugin's hook: `echo hi` 165 → 165 ms and
`dotnet --version` 167 → 181 ms. The whole run's wall clock (about 2.1 s) moved 2086.9 → 2093.4 ms and
2167.4 → 2180.3 ms, but its per-round installed-minus-absent differences spread from −66 to +81 ms, so it resolves
neither figure; only these in-run figures were taken inside OpenCode. The OpenCode CLI runs plugins under the Bun
it embeds (a probe plugin's `tool.execute.before` saw `Bun.version` 1.3.14), while the figures that isolate the
hook come from Node 26, outside OpenCode: the plugin's `tool.execute.before` imported and timed by a driver script,
55 samples, `dotnet --version` 10.7 ms (one `dtk hook opencode` start, no rewrite) and `dotnet build` 11.0 ms
(rewritten to `dtk dotnet build`), against 1.2 ms for spawning `/bin/true` the same way. The plugin starts no
process for a command without `dotnet`: inside OpenCode, a logging `dtk` wrapper on `PATH` saw no start for
`echo hi` and one for `dotnet --version`; under the Node driver, the hook returned in under 0.01 ms for `echo hi`.

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
the pointer. A local Release build of the CLI carries the AOT feature switches in its runtimeconfig
(`PublishAot=true` in the csproj), so `cold-start` on `bin/Release` measures an AOT-like JIT build, not the
shipped fallback; measure the installed `any` package for fallback figures.

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

## Architecture & Stack

- **Target framework**: net10.0
- **CLI framework**: Spectre.Console + Spectre.Console.Cli
- **Testing**: xunit + FluentAssertions + Spectre.Console.Testing
- **Package management**: Central via `Directory.Packages.props` — all version numbers go there, `.csproj` files omit
  versions

## Code Style

Enforced via `.editorconfig` and build-time analyzers (Roslynator, SonarAnalyzer, Microsoft.CodeAnalysis.NetAnalyzers):

- `TreatWarningsAsErrors` is enabled — all analyzer warnings must be resolved
- File-scoped namespaces (`namespace Foo;`)
- `var` preferred throughout
- Private fields: `_camelCase`; async methods must end in `Async`
- Interfaces: `IPascalCase`; type parameters: `TPascalCase`
- Line endings: LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`, 2-space for XML/JSON/YAML

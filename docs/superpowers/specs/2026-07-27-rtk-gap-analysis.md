# DTK vs RTK — Gap Analysis

**Date:** 2026-07-27
**Status:** Analysis (no implementation decision yet)
**Versions compared:** dtk 0.6.0 · rtk 0.43.0

Scope of the review: DTK's full source tree (`src/`, 102 `.cs` files across Domain / Application /
Infrastructure / Cli), both binaries exercised locally, and the two command surfaces compared.

> Measurement caveat: the PreToolUse hook rewrites `dotnet …` → `dtk dotnet …`, so every "raw"
> baseline below was captured through the absolute SDK path (`/usr/share/dotnet/dotnet`) to bypass
> it. Numbers taken without that bypass are dtk-filtered on both sides and meaningless.

## Where DTK is already ahead of rtk

- More `dotnet` depth: rtk covers build/test/restore/format; DTK adds `clean`.
- Real tokenizer counting (`Microsoft.ML.Tokenizers`, cl100k/o200k) vs rtk's estimates.
- 8-provider integration, shell completion, `doctor`, tee logs.
- Substantially stronger test + mutation-testing setup.
- Filter quality is good: `samples/SampleApp.Warnings` build measured 19,406 B → 4,009 B (**79%**).

## 1. Coverage is the biggest gap — and it is invisible

DTK filters 5 subcommands. Everything else falls through the passthrough branch at
`src/DotnetTokenKiller.Cli/Program.cs:24-29`, which is **unfiltered *and* untracked** — so DTK
cannot measure what it is missing.

Unhandled high-noise `dotnet` surface:

| Priority | Command | Why it matters |
|---|---|---|
| High | `list package --outdated` / `--vulnerable` / `--deprecated` | One table per project × TFM; noise grows linearly with solution size |
| High | `ef migrations` / `ef database update` | Build output plus full SQL dumps |
| Medium | `publish`, `pack` | Per-project asset lines on solutions |
| Medium | `tool list/restore`, `workload list`, `sln list`, `msbuild` | Fixed-noise, cheap filters |
| Later | `run`, `watch` | Blocked on streaming (see §4) |

> **Resolved 2026-07-27.** `list package` is now filtered (plain, `--outdated`, `--deprecated`, and
> `--vulnerable`). The remaining rows in this table are no longer guesses either — coverage
> tracking (below) measures raw tokens at stake per command, so the next choice is data-driven.
> See [the design](2026-07-27-list-package-filter-design.md).

Adjacent tooling **this repo itself uses** and would benefit from: `jb inspectcode` XML → compact
findings, Stryker mutation output, coverage summaries.

> **Resolved 2026-07-27.** Passthrough runs are now tracked. Allowlisted batch subcommands are
> streamed through and measured; interactive ones are counted but not captured. `dtk gain
> --coverage` ranks every command by unfiltered tokens at stake.
> See [the design](2026-07-27-coverage-tracking-design.md).

## 2. No pipe mode

rtk has `rtk pipe --filter <name>`. DTK can only filter what it launches itself, so CI logs,
`dotnet build | dtk pipe build`, and any invocation the hook missed cannot be reduced. The filters
are already pure `Apply(string rawOutput, int exitCode)`, so the change is small and the payoff is
broad.

## 3. Filters are neither extensible nor observable

- rtk supports project-local TOML filters with `trust` / `untrust` / `verify` (including inline
  tests). DTK's filters are compiled C# — a user with custom analyzer or MSBuild output needs a PR.
- **No fallback visibility.** `FilteredRunUseCase.ApplyFilterSafelyAsync` swallows filter
  exceptions, and the raw-tail fallback path is not recorded either. rtk exposes exactly this via
  `gain --failures`. Adding a `fallback_reason` column plus `dtk gain --failures` is what would tell
  us which filter to fix next.

  > **Resolved 2026-07-27.** Filter exceptions and raw-tail fallbacks are recorded as
  > `FilterFaulted` and `RawTailFallback` respectively, and surface in `dtk gain --coverage`.
  > Project-local extensible filters (the first bullet) remain open.

## 4. Output is fully buffered

`ProcessCommandRunner.RunCapturedAsync` uses `ReadToEnd`, and the tee file is written only after the
process exits. Two consequences:

1. No progress signal on multi-minute builds.
2. If the agent's tool call times out or is cancelled, **both the output and the tee log are lost** —
   precisely the case where the log is most wanted.

Streaming the tee incrementally fixes both and is the prerequisite for supporting `run` / `watch`.

## 5. No way to retrieve the previous run's detail

There is no `dtk log` / `dtk last`. Tee files exist, but the path surfaces only with `--show-log`,
and only when output exceeds 500 chars (`FileTeeService`). So an agent needing full detail re-runs a
two-minute build. Neither tool has this — cheap differentiator.

## 6. Analytics thinner than rtk

DTK `gain` has `--days` / `--project` / `--json` / `--export` / `--command`. rtk adds `--history`,
`--graph`, `--daily` / `--weekly` / `--monthly`, and `--quota --tier` (savings against subscription
tier). Missing entirely: rtk's `discover` (mines Claude Code history for missed opportunities),
`session` (adoption rate), and `cc-economics`.

A `dtk discover` scanning `~/.claude/projects/*.jsonl` for raw `dotnet` invocations would directly
quantify whether the hook is actually working.

## 7. The subcommand list exists in four places

| Location | Layer | Order | Bound to canonical? |
|---|---|---|---|
| `CliConfigurator` / `ArgumentPreprocessor` (Cli) | Cli | display | canonical (`DotnetSubcommands.Ordered`, via Spectre registration test) |
| `FilterKeys` | Domain | — | canonical (aliases `DotnetSubcommands` consts) |
| `_DTK_SUBCOMMANDS` in `HookScriptTemplates.cs` | Application | alphabetical | canonical (generated from `DotnetSubcommands.Sorted`, bound by test) |
| `.claude/hooks/dotnet-to-dtk.py` | repo | alphabetical | canonical (locked byte-identical to the generated template by test) |

The canonical list lives in `Cli` while the hook template that needs it lives in `Application` —
that layering is what forced the copy.

**Failure mode:** add a `publish` filter, register the command, ship — and forget the tuple in
`HookScriptTemplates.cs`. The hook never rewrites `dotnet publish`, the agent keeps running it
raw, and the new filter sees 0% adoption with no error anywhere. Nothing in the test suite caught
this at the time of the analysis, and it worsened with every subcommand added.

> **Resolved 2026-07-27.** The canonical list now lives in `DotnetTokenKiller.Domain.DotnetSubcommands`.
> `FilterKeys` aliases its constants, the Cli copy is gone, the hook's tuple and docstrings are
> generated from it, and binding tests cover the DI keys, the Spectre registrations, the generated
> hook, and this repo's committed `.claude/hooks/dotnet-to-dtk.py`.
> See [the design](2026-07-27-subcommand-single-source-of-truth-design.md).

## 8. `doctor` does not check the most fragile thing

It checks SDK, config, DB, and tee — but not whether the hook is installed, registered in
`settings.json`, or whether `python3` is on PATH (all three hooks are Python). rtk has `verify` for
hook integrity. The strongest version feeds a sample payload through the *installed* hook and
asserts the rewrite comes back.

## 9. Smaller items

- **Negative-savings accounting.** When `dotnet format` prints nothing, raw = 0 tokens while DTK's
  synthesized `✓` line costs ~10. The README's own example shows `format (ok) 103 runs … -1.0K,
  0.0%`. Synthesized-confirmation overhead should be reported separately, not as negative savings.
- **No density knob.** rtk has `--ultra-compact`. `DotnetBuildFilter` caps message length
  (`MessageMaxLen = 120`) but not the *number* of warning groups, and keeps full
  `learn.microsoft.com` URLs — the warnings sample still emits 54 lines. Capping groups and dropping
  the URLs would cut the worst case materially. (`DotnetListPackageFilter` does not share this gap —
  it caps groups at 30 with an explicit truncation line — so this complaint now applies to
  `DotnetBuildFilter` only.)
- **Startup:** 117 ms (dtk) vs 2 ms (rtk). Real but minor. NativeAOT / ReadyToRun binaries on GitHub
  Releases alongside the NuGet tool would also serve machines without the SDK (rtk ships a
  curl-installable binary).

## Suggested sequencing

1. §7 — collapse the subcommand list to one source of truth, with a test binding canonical list ↔
   Spectre registrations ↔ DI filter keys ↔ the generated hook. Pure refactor; makes every later
   subcommand safe to add.
2. ~~§1/§3 — track passthrough runs so the choice of the next filter is data-driven rather than a
   guess.~~ **Done 2026-07-27** — see the resolution notes above.
3. ~~First new filter, now to be chosen from what `dtk gain --coverage` reports rather than
   guessed~~ **Done 2026-07-27** — `list package` is filtered (all four variants; see
   [the design](2026-07-27-list-package-filter-design.md)). Next: §2 pipe mode and §5 `dtk log`.

§7 is recommended first not because it is the most valuable, but because it is the prerequisite for
the work that is, and skipping it fails silently.

**Status:** Implemented — see [the plan](../plans/2026-07-27-list-package-filter.md)
**Implements:** item 3 of the sequencing in [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

## Problem

The gap analysis nominated `dotnet list package` as the leading uncovered command, but flagged that
choice as a guess. Coverage tracking now makes it measurable. Bootstrapping a throwaway database by
running every allowlisted passthrough subcommand against this repository gives:

| Command | Runs | Raw tokens |
|---|---|---|
| `list package` | 5 | 5.3K |
| `publish` | 1 | 691 |
| `pack` | 1 | 351 |
| `sln` | 1 | 164 |
| `tool list` | 1 | 154 |
| `workload list` | 1 | 48 |
| `msbuild` | 1 | 35 |

`list package` is an order of magnitude above the next candidate, at ~1.1K tokens per run. The guess
was right, and it is now backed by measurement. Per-variant raw output on this repository:

| Variant | Bytes | Lines |
|---|---|---|
| plain | 7892 | 107 |
| `--outdated` | 2231 | 44 |
| `--deprecated` | 1785 | 30 |
| `--vulnerable` | 1000 | 13 |

The redundancy is structural rather than incidental. Under central package management the same five
analyzer packages repeat across all eight projects, `Requested` equals `Resolved` in nearly every
row, and the audit variants emit one `has no deprecated packages` line per project before any real
finding. On `--vulnerable` and `--deprecated`, a completely clean result still costs most of a
kilobyte to say nothing.

`list package` is also the first subcommand dtk filters that is **two tokens**. That assumption is
load-bearing in four places, and `dotnet list reference` must not be captured by mistake.

## Solution

Two independent pieces: generalize subcommand routing to token sequences, then add the filter.

### Multi-token subcommands

A subcommand becomes an ordered token sequence matched longest-first. Its canonical name stays a
space-joined string (`"list package"`), so DI keys, help text, and hook generation continue to
operate on strings and need no shape change.

`DotnetSubcommands` gains a `ListPackage` const and a `TryMatch(args, out match)` returning the
canonical name and its **token count**. The three `ArgumentPreprocessor` methods currently test
`DotnetSubcommands.All.Contains(args[1])` and assume the subcommand ends at index 1; they become
`TryMatch`-based, and `InsertSeparator` places `--` after `match.TokenCount` tokens rather than
always at index 2.

Longest-match-first is specified now, while only one multi-token entry exists, so that adding a
future bare `list` filter cannot silently shadow `list package`.

`dotnet list reference` stays safe because of dispatch *order*, not a special case: `IsPassthrough`
runs before Spectre, `list reference` matches no subcommand, so it forwards to real `dotnet`
untouched and never reaches the `dotnet list` branch. Bare `dotnet list` behaves the same way and
lets dotnet print its own error. This ordering is the mechanism, and a test pins it.

This generalization is not speculative: `ef migrations`, `ef database update`, `tool list`,
`sln list`, and `workload list` are all multi-token and all on the passthrough allowlist, so the
alternative is paying this cost again for each.

### The filter

`Apply(string rawOutput, int exitCode)` receives **no arguments**, so the variant must be detected
from the output itself. Each has a distinct marker phrase:

| Variant | Marker |
|---|---|
| plain | `has the following package references` |
| `--outdated` | `has the following updates to its packages` |
| `--deprecated` | `has the following deprecated packages` / `has no deprecated packages` |
| `--vulnerable` | `has the following vulnerable packages` / `has no vulnerable packages` |

Rows are parsed by splitting on runs of two or more spaces, with the **preceding header row**
deciding what the columns mean. Header-driven interpretation is required rather than convenient: it
is what handles `Reason(s)` values containing spaces (`Critical Bugs`), `Alternative` values like
`Verify.XunitV3 >= 0.0.0`, and `--include-transitive`, whose `Transitive Package` sub-table has no
`Requested` column at all.

Target output, against this repository's real data:

```
✓ dotnet list package (8 projects, 21 packages)
  all projects: Microsoft.CodeAnalysis.NetAnalyzers 10.0.302, Microsoft.VisualStudio.Threading.Analyzers 18.7.23,
                Roslynator.Analyzers 4.15.0, Roslynator.Formatting.Analyzers 4.15.0, SonarAnalyzer.CSharp 10.29.0.143774
  DotnetTokenKiller.Cli: Spectre.Console 0.57.2, Spectre.Console.Cli 0.55.0
```

```
dotnet list package --outdated: 1 package with updates (all 8 projects)
  SonarAnalyzer.CSharp 10.29.0.143774 → 10.30.0.144632 (8 projects)
```

```
dotnet list package --deprecated: 2 deprecated packages (4 of 8 projects)
  Verify.Xunit 31.12.5 — Legacy → Verify.XunitV3 >= 0.0.0 (2 projects)
  xunit 2.9.3 — Legacy → xunit.v3 >= 0.0.0 (4 projects)
```

These are this repository's actual figures as of 2026-07-27, so they double as the first fixtures.
Note the two shapes in the header: `(all 8 projects)` when every project is affected, `(4 of 8
projects)` otherwise — the `N of M` form is what makes a partial result legible, and collapsing both
into one phrasing loses that.

Rules:

- **Shared set plus per-project delta** for the plain variant: one `all projects:` line, then only
  each project's additions. The project→package mapping stays recoverable, which a flat deduplicated
  list would discard. With no shared packages it degrades to a plain per-project listing — never
  worse than raw.

  "Shared" means **present in every project at the same version** — a strict all-or-nothing test, not
  a majority. A package in 7 of 8 projects is therefore repeated on those 7 per-project lines. This
  is chosen deliberately over a "most projects, except X" form: the strict rule keeps the output
  unambiguous to read, and the exception form is only worth its complexity if measurement later shows
  near-universal packages are common. On this repository the strict rule already captures the five
  analyzer packages that cause the bulk of the repetition.
- **The restore preamble is dropped** in every variant: `Determining projects to restore...`,
  `All projects are up-to-date for restore.`, `The following sources were used:` and its URLs.
- **`Requested ≠ Resolved` renders as `Pkg 1.0.0→1.2.3`**, never collapsed to a single version. A
  floating version resolving unexpectedly is precisely what someone runs this command to find, so
  compression must not hide it.
- **All three audit variants group by package, not by project**, and count affected projects: by
  `(package, resolved, latest)` for `--outdated`, `(package, resolved, reason, alternative)` for
  `--deprecated`, `(package, resolved, severity, advisory)` for `--vulnerable`. A package whose
  details differ between projects gets one line per distinct combination, so grouping never merges
  rows that say different things. Project *names* are dropped in favour of a count once more than one
  project is affected; a single affected project is named, since the count alone would be useless.
- **A clean audit run collapses to one line**, e.g.
  `✓ dotnet list package --vulnerable (no vulnerable packages, 8 projects)`. The per-project
  `has no ...` lines vanish entirely. This is the largest single win on `--deprecated` and
  `--vulnerable`.
- **`✓` appears only when `exitCode == 0` *and* there are no findings.** The exit code remains the
  sole source of the success verdict, per the `IOutputFilter` contract; findings are reported
  regardless of exit code.
- **Density cap at 30 package groups**, then `… and N more packages (use --show-log for full output)`.
  Truncation is always stated, never silent, and the tee log retains the full text. This addresses
  the "no density knob" item in §9 of the gap analysis for this filter.

### Parsing substrate: console output, not `--format json`

SDK 10.0.302 supports `dotnet list package --format json` with `--output-version` to pin the schema,
which would be a materially more robust parse than four console table shapes. It is deliberately not
used.

Injecting `--format json` would make dtk run a *different command* than the user typed. That breaks
three things: the tee log would hold JSON rather than the output the user would have seen; the pure
`Apply(rawOutput, exitCode)` contract that §2 pipe mode depends on would no longer hold, since piped
input arrives in console format; and `dtk dotnet list package` would stop being byte-honest about
being the same command.

The fragility this trades for has to be made observable rather than silent. Coverage tracking records
`FilterFaulted` and `RawTailFallback`, but both of those only reach the user on a *failed* run, and
this command always exits 0 (see the correction under "Known risk" below). So the filter's own
unrecognized-output guard — not coverage tracking — is what keeps a table shape that defeats the
parser from becoming a confidently wrong summary. With that guard in place, console parsing is an
acceptable risk here where it previously would not have been.

**The user may pass `--format json` themselves.** dtk not injecting the flag says nothing about the
user not typing it: `--format <console|json>` is documented and first-class, and `dtk dotnet list
package --format json` forwards it unchanged. That output has no `has the following` lines and no
`> ` rows, so no variant is detected — which is an explicitly handled degradation, not an
unconsidered one. The guard passes it through verbatim behind a `⚠` marker rather than swallowing it.
The original analysis of this section considered only dtk injecting the flag, never the user
supplying it.

## Components

| Layer | Change |
|---|---|
| Domain | `DotnetSubcommands`: `ListPackage` const, `Ordered` entry, `TryMatch`; `FilterKeys.ListPackage` |
| Application | **new** `Filters/DotnetListPackageFilter.cs`; `AddKeyedTransient` registration in `AddApplication`; `HookScriptTemplates` multi-token regex |
| Cli | **new** `Commands/DotnetListPackageCommand.cs`; `CliConfigurator` nested `dotnet list` → `package` branch; `ArgumentPreprocessor` all three methods |
| Repo | regenerate `.claude/hooks/dotnet-to-dtk.py` |
| Prose | `Integration/IntegrationInstructions.cs`, `Integration/CopilotCliIntegrator.cs` |

The order above is the six-step checklist in `DotnetSubcommands`' own remarks. Step 6 of that
checklist notes the integration prose hardcodes the subcommand list and that "nothing guards them."

`PassthroughSubcommands` needs no change. `list package` will no longer reach the passthrough
branch, while `list reference` still will, and `Measurable` containing `list` remains correct for it.

## Data flow

The hook rewrites `dotnet list package --outdated` to `dtk dotnet list package --outdated`.
`ArgumentPreprocessor.Normalize` canonicalizes token casing; `InsertSeparator` inserts `--` after
two tokens so Spectre forwards the rest as `Remaining.Raw`. Spectre routes to
`DotnetListPackageCommand`. `FilteredRunUseCase` resolves the keyed `IOutputFilter` registered under
`"list package"`, runs the child process, applies the filter, and records the outcome.

## Error handling

**Failure with nothing parsed returns empty**, matching `DotnetRestoreFilter`, so
`FilteredRunUseCase`'s raw-tail fallback surfaces the real error and the run records as
`RawTailFallback`. This holds only because the exit code is non-zero — a restore failure, say.

**Success with nothing parsed returns the raw output behind a `⚠` marker.** Returning empty would
show the user nothing, since the raw-tail fallback is gated on a non-zero exit code and this command
always exits 0. See the correction under "Known risk" below for the full reasoning.

NuGet error parsing is deliberately **not** implemented. Failures of this command are predominantly
restore errors, for which the raw tail is already faithful and complete. Duplicating
`DotnetRestoreFilter`'s error patterns would add code whose value is speculative, and if the
assumption is wrong it shows up as a `RawTailFallback` count in `dtk gain --coverage` rather than
silently. That is the cheaper way to find out.

Empty input returns empty, as every existing filter does.

## Testing

Filter unit tests, one per variant, plus these cases:

- `--include-transitive` tables (no `Requested` column)
- `Requested ≠ Resolved` preserved as an arrow
- the 30-group density cap, asserting the trailing count line
- empty output
- failed run with nothing parsed → empty
- a single-project solution
- **no packages shared across projects** → degrades to a per-project listing, still smaller than raw

Fixtures use real captured output for plain, `--outdated`, and `--deprecated`.

Binding and routing tests:

- `SubcommandBindingTests`' two pinned literals, which bind DI keys, Spectre registrations, the
  generated hook, and the committed `.claude/hooks/dotnet-to-dtk.py`
- `ArgumentPreprocessor`: `list package` matches; `list reference` is passthrough; bare `list` is
  passthrough; `DOTNET LIST PACKAGE` normalizes; `--` lands after two tokens
- **new** binding test over the integration prose in `IntegrationInstructions.cs` and
  `CopilotCliIntegrator.cs`, closing the unguarded gap that step 6 of the `DotnetSubcommands`
  checklist warns about. This change is the first to exercise that gap.

### Known risk: `--vulnerable` has no real sample

This repository has no vulnerable packages, so the `--vulnerable` fixture must be hand-written from
the documented column shape (`Severity`, `Advisory URL`). It is the one variant whose parser ships
unverified against real SDK output. The fixture should be replaced with a real capture at the first
opportunity.

**Correction (post-implementation).** An earlier version of this section claimed the risk "fails
safe — an unparsed table yields empty, which triggers the raw-tail fallback and records
`RawTailFallback`". That was **wrong**, and it was the load-bearing claim under this whole section.
`dotnet list package` **always exits 0** — verified against SDK 10.0.302, including runs that report
deprecated and vulnerable packages. `FilteredRunUseCase` only invokes the raw-tail fallback when
`ExitCode != 0`, so for this command **the fallback can never fire**. An unparsed table was not
failing safe: the project header lines still parsed, so the variant was still detected, `Entries` was
empty, and the zero-findings path emitted an affirmative
`✓ dotnet list package --vulnerable (no vulnerable packages, 8 projects)` for a repository that has
vulnerable packages.

The filter therefore carries **its own guard**, and does not borrow the raw-tail fallback:

- `ParseState.DroppedRows` counts `> ` rows that were seen but could not be mapped onto a recognized
  header row. That is what distinguishes "understood, nothing to report" from "did not understand
  this" — a distinction the original design had no way to express.
- `Apply` returns `DotnetListPackageFilter.Unrecognized` when `DroppedRows > 0` **or** the variant is
  `Unknown` and the input was not empty/whitespace. On exit 0 that yields the raw output behind a
  one-line `⚠ dotnet list package: unrecognized output, passed through unfiltered` marker, preserving
  the "never worse than raw" guarantee and mirroring `ApplyFilterSafelyAsync`'s behaviour for a filter
  that throws. On a non-zero exit it still yields empty, because there the raw-tail fallback *does*
  fire and adds an explicit failure verdict plus the `RawTailFallback` coverage signal.

The general lesson, for the `publish` / `pack` / `tool list` filters queued behind this one: **a
filter for a command that always exits 0 cannot rely on the exit-code-gated raw-tail fallback and
needs its own guard.**

### Success criterion

Measured, not asserted: re-run the coverage bootstrap into a throwaway database and compare
`dtk gain --coverage` before and after. The baseline to beat is plain 7892 B / 107 lines,
`--outdated` 2231 B, `--deprecated` 1785 B, `--vulnerable` 1000 B.

## Out of scope

- **Any other filter.** `publish`, `pack`, `tool list`, `workload list`, and `msbuild` are all
  measured now and can be chosen from data later; this design covers `list package` only.
- **§2 pipe mode and §5 `dtk log`.** Next in the sequencing, unaffected by this work beyond the
  console-parsing decision that keeps pipe mode possible.
- **Project-name shortening.** Stripping a common dot-delimited prefix from project names would save
  real bytes on solutions with long names, but adds ambiguity for no measured need. Revisit if
  coverage data on a large solution shows it matters.
- **`PassthroughSubcommands` granularity bug.** The bootstrap surfaced that
  `dotnet sln MySln.slnx list` records as `sln`, not `sln list`, because `QualifyingVerbs` inspects
  only `args[1]`. Real and pre-existing; tracked separately.

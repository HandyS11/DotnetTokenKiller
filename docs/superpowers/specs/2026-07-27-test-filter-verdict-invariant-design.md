# Filter verdict invariant — no false "nothing happened" — Design

**Date:** 2026-07-27
**Status:** Approved (brainstorming)
**Branch:** feature work off `develop`
**Origin:** [DTK vs RTK gap analysis](./2026-07-27-rtk-gap-analysis.md) §3 — "filter produced junk / fell back"

## Problem

`DotnetTestFilter` treats a **per-assembly** "no tests" signal as a **global** verdict.

`state.ZeroTestsFound` is a sticky boolean set by any single `No test matches the given testcase
filter` line (`DotnetTestFilter.cs:60-65`), and it is then OR'd *ahead* of the accumulated totals
(`DotnetTestFilter.cs:267-271`):

```csharp
var zeroTestsSignal = state.ZeroTestsFound || state is { ProjectCount: > 0, TotalPassed: 0 };
if (exitCode == 0 && zeroTestsSignal)
{
    return "✓ dotnet test: 0 tests found\n";
}
```

In a multi-project solution, one assembly matching nothing therefore discards every other
assembly's real results.

**Reproduced on 2026-07-27** (dtk 0.6.0):

```sh
dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliHook_ReusesSharedRewriteCore"
# → ✓ dotnet test: 0 tests found      (exit 0)
```

Ground truth from the same run via the absolute SDK path (bypassing the rewrite hook): **1 test
passed** in `DotnetTokenKiller.Application.Tests`; the other four test projects each printed
`No test matches the given testcase filter`.

Severity is high for two reasons: `test` is the most-invoked filter, and the failure is presented as
a **success** (`✓`, exit 0). It silently masks real results and can fake a passing TDD RED/GREEN
cycle.

This is not a new observation — it has been worked around rather than fixed. It is recorded as
project knowledge in the `dtk-test-filter-quirk` memory, and
`docs/superpowers/plans/2026-07-16-gain-dashboard.md:20` already instructs implementers to target the
test `.csproj` instead of the `.slnx` because of it. This design root-causes it.

The `exitCode == 0` guard does mean genuine *failures* are never masked. The blast radius is exactly
"tests passed, dtk says none ran".

## The invariant

> A filter may report a "nothing happened" verdict only when it accumulated **no positive
> evidence**.

`DotnetRestoreFilter` already obeys this — its `AllUpToDate` fallback is gated on
`totalProjects == 0` (`DotnetRestoreFilter.cs:155`), so it fires only in the absence of counted
work. It becomes the reference implementation for the other filters.

## Scope & Decisions

| Decision | Choice |
|---|---|
| Scope | Fix the test filter **and** enforce the invariant across all five filters (option B) |
| Genuine zero-match output | Warn instead of `✓`; **exit code untouched** |
| Parenthetical wording | Conditional — see below |
| Fallback observability (§3) | Out of scope; follow-up work |

**Why the parenthetical is conditional.** `NoTestsPattern` also matches `No test is available`,
which fires when an assembly contains no tests at all — no `--filter` involved. Wording that names
the filter would be wrong in that case, so the detail appears only when the explicit no-match
pattern fired.

**Why exit code stays untouched.** dtk must remain faithful to `dotnet`'s own verdict.
`FilteredRunUseCase` already returns `result.ExitCode` verbatim; nothing changes there. Only the
human/agent-facing glyph and message change.

## Components

### 1. `DotnetTestFilter.FormatOutput` (fix)

Replace the guard at `:267-271` with:

```csharp
// A "nothing ran" verdict requires that no assembly produced evidence of a test. ZeroTestsFound is
// per-assembly: in a multi-project run, one assembly matching nothing must never override another's
// real results.
var noTestEvidence = state is { TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0 }
                     && state.Failures.Count == 0;
if (exitCode == 0 && noTestEvidence && (state.ZeroTestsFound || state.ProjectCount > 0))
{
    return state.ZeroTestsFound
        ? "⚠ dotnet test: 0 tests found (no assembly matched)\n"
        : "⚠ dotnet test: 0 tests found\n";
}
```

The skipped-only branch (`:261-264`) returns before this, and failures are handled earlier by
`FormatFailures`, so parts of `noTestEvidence` are redundant against today's control flow. It is
stated explicitly anyway so the guard is self-contained, survives future reordering, and exposes
each clause to mutation testing individually.

Post-fix output for the reproduction: `✓ dotnet test: 1 passed (1 project, 52ms)`.

Note on wording: `ProjectCount` counts assemblies that *printed a summary*, so "1 project" means one
project reported results. Listing the non-matching assemblies was considered and rejected (YAGNI —
it costs tokens on every filtered run).

### 2. `FilteredRunUseCase.NormalizeGlyphs` (extend)

`⚠` is new to the codebase; the method currently maps only `✓` → `ok:` and `✗` → `FAIL:`
(`FilteredRunUseCase.cs:145-146`). Add `⚠` → `WARN:`, otherwise the `NO_COLOR` /
`display.emoji = false` path leaks a raw glyph.

### 3. Invariant audit — remaining four filters

One test each, asserting that a "nothing happened" marker in the input cannot suppress real parsed
evidence. "Mixed input" means something different per filter, so each test is specified concretely
rather than by analogy to the `test` case:

| Filter | Expected status | Reasoning | Invariant test input |
|---|---|---|---|
| `DotnetRestoreFilter` | compliant | `AllUpToDate` gated on `totalProjects == 0` | `All projects are up-to-date for restore` **plus** a restored/up-to-date project line → counts reported, not the bare "all up-to-date" verdict |
| `DotnetFormatFilter` | compliant | Empty-input synthesis gated on empty **and** `exitCode == 0`; violations return earlier | A real violation line **plus** a `Format complete in …ms` line → violation reported, not "nothing to format" |
| `DotnetBuildFilter` | compliant | Verdict derived from parsed diagnostic counts | A real diagnostic **plus** a `0 Error(s)` / `0 Warning(s)` MSBuild summary line → diagnostic reported, not a clean verdict |
| `DotnetCleanFilter` | compliant | Purely evidence-driven; emits nothing when no errors found | A real error line **plus** the `\d+ Error(s)` noise summary it filters → error survives |

If the audit contradicts an expectation, that filter gets the same treatment as `test`. If it
confirms them, the tests are the regression net that keeps it true.

## Error Handling

No new failure modes. `FilteredRunUseCase.ApplyFilterSafelyAsync` still catches filter exceptions
and degrades to raw output, and the raw-tail fallback for non-zero exits is untouched. The change is
confined to which of several already-reachable strings a successful parse returns.

## Testing

TDD, RED before GREEN.

**RED evidence must not come from `dtk dotnet test <slnx> --filter`** — that is the very invocation
under repair, so it cannot be trusted to prove RED. Target the test project directly:

```sh
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj \
  --filter "FullyQualifiedName~<NewTest>"
```

A welcome side effect of this fix: that caveat disappears afterwards, and the single-test invocation
documented in `CLAUDE.md` starts working as written.

**New fixture.** `Fixtures/dotnet_test_multiproject_partial_match.txt` — the real 26-line captured
output from the reproduction, not hand-written, matching the existing `Fixtures/dotnet_test_*.txt`
convention.

**New tests.**

- `Apply_MultiProjectPartialMatch_ReportsPassedCount` — the regression test for this bug.
- `Apply_MultiProjectPartialMatch_MatchesSnapshot` — `Verify` snapshot, per existing convention.
- One clause-killing test per component of the new guard (`TotalPassed`, `TotalFailed`,
  `TotalSkipped`, `Failures.Count`, and the `ZeroTestsFound || ProjectCount > 0` disjunction).
  Stryker runs in CI and the existing tests carry explicit mutant-kill annotations.
- One invariant test per remaining filter (§Components 3).
- `NormalizeGlyphs` coverage for `⚠` → `WARN:` under `display.emoji = false` and under `NO_COLOR`.

**Intentionally updated assertions.** These pin the current string and change *deliberately* as part
of the behavior change — they must be updated, not weakened:

- `DotnetTestFilterTests.cs:72` — `Apply_ZeroTestsFixture_ReturnsZeroTestsMessage`
- `DotnetTestFilterTests.cs:330` — `Apply_ZeroTestsFromAllSummariesZero_ReturnsZeroTestsMessage`
- `DotnetTestFilterTests.cs:596` — `Apply_NoTestsPattern_SetsZeroTestsFlag`
- `Snapshots/DotnetTestFilterTests.Apply_ZeroTestsFixture_MatchesSnapshot.verified.txt`

## Files Touched

| File | Change |
|---|---|
| `src/…/Filters/DotnetTestFilter.cs` | Fix the verdict guard |
| `src/…/UseCases/FilteredRunUseCase.cs` | Map `⚠` → `WARN:` |
| `tests/…/Fixtures/dotnet_test_multiproject_partial_match.txt` | New fixture (real captured output) |
| `tests/…/Filters/DotnetTestFilterTests.cs` | New tests; update 3 pinned assertions |
| `tests/…/Filters/Dotnet{Build,Clean,Format,Restore}FilterTests.cs` | One invariant test each |
| `tests/…/Snapshots/…Apply_ZeroTestsFixture_MatchesSnapshot.verified.txt` | Accept new text |
| `tests/…/Snapshots/…Apply_MultiProjectPartialMatch_MatchesSnapshot.verified.txt` | New snapshot |

No documentation changes are required: `README.md` and `docfx/` do not quote the `0 tests found`
string (verified 2026-07-27). The two dated plan docs under `docs/superpowers/plans/` that mention it
are historical records and stay as written.

## Completion Criteria

1. The reproduction command reports `1 passed`, not `0 tests found`.
2. A genuine zero-match run warns (`⚠` / `WARN:`) and still passes `dotnet`'s exit code through.
3. All five filters carry an invariant test.
4. Build clean under `TreatWarningsAsErrors`; `dotnet format --verify-no-changes` clean; mutation
   score not regressed.
5. The `dtk-test-filter-quirk` memory is deleted — it documents a workaround this fix removes.

## Out of Scope

§3 fallback observability (`fallback_reason` + `dtk gain --failures`) — a genuinely useful follow-up,
but it records only failures a filter *knows* about, and this bug was a confident wrong answer down
the happy path. It would not have caught it.

Also out: streaming/incremental tee (§4), `doctor` hook verification (§8), output density and CA help
URLs (§9), pipe mode (§2), new `dotnet` subcommands (§1), `DotnetCleanFilter`'s silence-on-success
question, and the README's negative `format` savings row.

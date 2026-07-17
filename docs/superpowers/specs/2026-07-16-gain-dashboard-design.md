# `dtk gain` Dashboard Display — Design

**Date**: 2026-07-16
**Status**: Approved

## Goal

Replace the plain `dtk gain` table with an rtk-style dashboard: a title/scope header, a
totals recap block with an efficiency meter, and a colored per-command table with
human-readable K/M token units and an impact bar column. Machine-readable outputs
(`--json`, `--export csv`) keep raw numbers.

## Target output

```
DTK Token Savings (Global Scope)
════════════════════════════════════════════════════════════

Total commands:    982
Without tool:      995.4K
Used by tool:      88.4K
Tokens saved:      907.0K (91.1%)
Total exec time:   38m12s (avg 2.3s)
Efficiency meter: ██████████████████████░░ 91.1%

By Command
──────────────────────────────────────────────────────────────────────
 Command         Runs  Without Tool  Used by Tool    Saved   Avg%  Impact
 build (ok)       151        104.5K          2.3K   102.2K  96.7%  ██░░░░░░░░
 build (fail)      37         47.7K          6.9K    40.9K  83.4%  █░░░░░░░░░
 clean (ok)         3        241.9K            18   241.9K 100.0%  ██████░░░░
 format (ok)      103             0          1.0K    -1.0K   0.0%  ░░░░░░░░░░
 ...
 test (ok)        442        420.6K          7.8K   412.8K  95.8%  ██████████
 test (fail)      234        177.1K         68.4K   108.7K  48.9%  ██░░░░░░░░
```

## Decisions

1. **Header & scope** — bold cyan title `DTK Token Savings (<scope>)` over a grey `═` rule.
   Scope text composes from the active filters: `Global Scope` by default, `Project Scope`
   with `--project`, appending `, last N days` when `--days` differs from the default 30,
   and `, command: <name>` when `--command` is set.
2. **Recap block** — replaces the in-table `TOTAL` row (removed; no duplication). Lines:
   total commands, without tool, used by tool, tokens saved (green, with overall %),
   total exec time (with per-run average), and a 24-character efficiency meter
   (`█` filled / `░` empty, filled portion green) followed by the percentage.
3. **K/M units** — values `< 1000` render as-is; `≥ 1000` as `X.YK`; `≥ 1_000_000` as
   `X.YM` (one decimal, invariant culture). Negative values keep the sign (`-1.0K`).
   Durations render as `Xms` / `X.Ys` / `XmYs` / `XhYm` picking the two most significant
   units.
4. **Table** — split ok/fail rows are kept. Command cell stays green `(ok)` / red `(fail)`.
   `Saved` cell is green when positive, red when negative, grey when zero. `Avg%` colored
   by scale: ≥ 80 green, 40–79 yellow, < 40 red. New right-most `Impact` column: a
   10-character `█`/`░` bar proportional to the row's saved tokens relative to the largest
   row (rows with non-positive savings render an empty bar).
5. **Exec time aggregation** — `SqliteTracker.GetSummaryAsync` SQL adds
   `SUM(execution_time_ms)`; `GainSummary` and `CommandGainDetail` gain a
   `TotalExecutionTime` (`TimeSpan`) component. The `--json` output gains the field
   additively (serialized via the existing source-generated context). CSV export is
   unchanged (already per-record).

## Components

| Unit | Location | Responsibility |
|---|---|---|
| `TokenFormat` (static) | `DotnetTokenKiller.Cli/Formatting/` | `Tokens(long)` → K/M string; `Duration(TimeSpan)` → compact duration string |
| `GainDashboardRenderer` | `DotnetTokenKiller.Cli/Formatting/` | Takes `GainSummary` + scope description, writes header/recap/meter/table to `IAnsiConsole` |
| `GainCommand` | existing | Builds scope text from settings, delegates human rendering to the renderer; JSON/CSV paths untouched apart from the new summary field |
| `GainSummary` / `CommandGainDetail` | Domain | Add `TotalExecutionTime` component |
| `SqliteTracker` | Infrastructure | Aggregate `SUM(execution_time_ms)` in summary query (overall and per command/success group) |

## Data flow

`GainCommand` → `GainReportUseCase.GetSummaryAsync` → `SqliteTracker` (SQL aggregation,
now including exec time) → `GainSummary` → `GainDashboardRenderer` → `IAnsiConsole`.

## Error handling

- Empty summary (`TotalCommands == 0`): keep the existing grey "No data yet" message,
  renderer not invoked.
- Zero max savings across rows (all zero/negative): impact bars all empty; no division by
  zero.
- Overall percentage outside 0–100 is clamped for the meter width only (displayed number
  stays exact).

## Testing

- `TokenFormat` unit tests: boundaries (999/1000, 999_950, 1M), negatives, durations.
- `GainDashboardRenderer` tests with `Spectre.Console.Testing.TestConsole`: header scope
  variants, recap values, meter width, impact bar proportions, color-scale buckets.
- Update existing `GainCommand` display tests to the new layout; JSON test asserts the new
  exec-time field; CSV tests unchanged.
- `SqliteTracker` tests: summary exec-time sums per group and overall.

# Token Analytics

DTK tracks token counts for every run, comparing the raw `dotnet` output against the filtered result. This lets you measure exactly how much context you're saving.

## Viewing Analytics

```sh
dtk gain                       # last 30 days (default)
dtk gain --days 7              # last 7 days
dtk gain --project             # current project only
dtk gain --command build       # filter to a specific command (build, test, restore, clean)
dtk gain --json                # machine-readable JSON output
dtk gain --export csv          # export raw records as CSV
```

### Example Output

```sh
DTK Token Savings (Global Scope, last 7 days)
════════════════════════════════════════════════════════════

Total commands:    129
Without tool:      60.1K
Used by tool:      8.7K
Tokens saved:      51.4K (85.5%)
Total exec time:   4m57s (avg 2.3s)
Efficiency meter: █████████████████████░░░ 85.5%

By Command
──────────────────────────────────────────────────────────────────────
 Command        Runs  Without Tool  Used by Tool     Saved    Avg%  Impact
 build (ok)       39         28.4K          4.6K     23.8K   83.8%  ██████████
 build (fail)      5          2.3K            578      1.7K   74.9%  █░░░░░░░░░

 clean (ok)       18          8.8K            108      8.7K   98.8%  ████░░░░░░

 restore (ok)     26          2.7K          1.0K      1.7K   63.0%  █░░░░░░░░░

 test (ok)        35         15.9K          2.0K     13.9K   87.4%  ██████░░░░
 test (fail)       6          2.0K            434      1.6K   78.3%  █░░░░░░░░░
```

### Columns

The header line shows the title and active scope (`Global Scope` or `Project Scope`,
with `, last N days` and `, command: <name>` suffixes when the corresponding filters are set).

The recap block above the table replaces the old in-table `TOTAL` row:

| Line | Description |
|------|-------------|
| **Total commands** | Number of tracked runs across all commands |
| **Without tool** | Total tokens in the raw `dotnet` output |
| **Used by tool** | Total tokens in the filtered DTK output |
| **Tokens saved** | Total tokens saved, with the overall percentage in parentheses |
| **Total exec time** | Summed wall-clock time, with the per-run average in parentheses |
| **Efficiency meter** | A 24-character `█`/`░` bar proportional to the overall savings percentage |

The `By Command` table splits each command into `(ok)` / `(fail)` rows, with a blank
spacer row between commands so each group reads as one block:

| Column | Description |
|--------|-------------|
| **Command** | The `dotnet` subcommand (build, test, restore, clean, format), suffixed `(ok)` or `(fail)` |
| **Runs** | Number of times the command was executed |
| **Without Tool** | Total tokens in the raw `dotnet` output |
| **Used by Tool** | Total tokens in the filtered DTK output |
| **Saved** | Tokens saved (Without Tool − Used by Tool); colored green when positive, red when negative, grey when zero |
| **Avg%** | Average percentage reduction across the row's runs; colored green ≥ 80%, yellow 40–79%, red < 40% |
| **Impact** | A 10-character `█`/`░` bar proportional to the row's saved tokens relative to the largest row (rows with non-positive savings render an empty bar) |

## Filtering by Command

Use `--command` to see savings for a single `dotnet` subcommand:

```sh
dtk gain --command build        # build runs only
dtk gain --command test --days 7
dtk gain --command restore --project
dtk gain --command build --export csv > build-savings.csv
```

The filter is exact-match on the command slug recorded at run time (`build`, `test`, `restore`, `clean`).

## JSON Output

Use `--json` for machine-readable output, useful for CI pipelines or dashboards:

```sh
dtk gain --json
```

## CSV Export

Use `--export csv` to export the raw per-run tracking records. This outputs a CSV to stdout that can be piped to a file or a spreadsheet tool:

```sh
dtk gain --export csv > savings.csv
dtk gain --export csv --days 7
dtk gain --export csv --project
```

### CSV Columns

| Column | Description |
|--------|-------------|
| `timestamp` | ISO 8601 timestamp of the run |
| `command` | The `dotnet` subcommand (build, test, restore, clean) |
| `project_path` | Working directory when the command ran |
| `input_tokens` | Estimated tokens in the raw output |
| `output_tokens` | Estimated tokens in the filtered output |
| `saved_tokens` | Tokens saved by filtering |
| `savings_pct` | Percentage of tokens saved |
| `execution_time_ms` | Wall-clock time for the command in milliseconds |

Fields containing commas, quotes, or newlines are RFC 4180-quoted.

## Resetting Data

To clear tracking data:

```sh
dtk reset           # prompts for confirmation
dtk reset --force   # skips confirmation
dtk reset --all     # also removes tee logs and config file
dtk reset --all --force  # full cleanup without confirmation
```

`--all` removes all dtk state: the tracking database records, all tee log files, and the configuration file. This is useful for a full uninstall or to start fresh.

## How Token Counting Works

DTK uses the [Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers) library to count tokens. The tokenizer model is configurable — see [Configuration](configuration.md#tokenizer-models) for available models.

By default, DTK uses `cl100k_base` (the encoding used by GPT-4 and GPT-3.5-turbo). Both the raw command output and the filtered result are tokenized, and the difference is recorded per run.

## Storage

Tracking data is stored in a SQLite database at `%LOCALAPPDATA%/dtk/tracking.db` (configurable via `tracking.dbPath` in [config](configuration.md)). Old records are automatically purged based on the `retentionDays` setting (default: 90 days).

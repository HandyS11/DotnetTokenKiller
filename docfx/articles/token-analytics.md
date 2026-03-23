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
┌─────────┬──────┬──────────────┬──────────────┬───────┬─────────────┐
│ Command │ Runs │ Without Tool │ Used by Tool │ Saved │ Avg Savings │
├─────────┼──────┼──────────────┼──────────────┼───────┼─────────────┤
│ build   │   44 │        30720 │         5178 │ 25542 │       77.6% │
│ clean   │   18 │         8752 │          108 │  8644 │       97.9% │
│ restore │   26 │         2651 │         1022 │  1629 │       47.0% │
│ test    │   41 │        17939 │         2434 │ 15505 │       84.1% │
│         │      │              │              │       │             │
│ TOTAL   │  129 │        60062 │         8742 │ 51320 │       85.4% │
└─────────┴──────┴──────────────┴──────────────┴───────┴─────────────┘
```

### Columns

| Column | Description |
|--------|-------------|
| **Command** | The `dotnet` subcommand (build, test, restore, clean) |
| **Runs** | Number of times the command was executed |
| **Without Tool** | Total tokens in the raw `dotnet` output |
| **Used by Tool** | Total tokens in the filtered DTK output |
| **Saved** | Tokens saved (Without Tool − Used by Tool) |
| **Avg Savings** | Average percentage reduction across all runs |

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

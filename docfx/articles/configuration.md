# Configuration

dtk reads its settings from a JSON file that controls how aggressively it filters, where it writes logs, and how it counts tokens. You can edit that file directly or go through `dtk config`, which validates what you write.

## CLI (recommended)

```sh
dtk config show                          # display all current values
dtk config set <key> <value>             # update a value and save
```

See [Usage Guide — dtk config](usage.md#dtk-config) for examples and valid values per key.

## JSON file

DTK uses an optional JSON configuration file at:

```sh
# Default locations:
# - Windows: %APPDATA%/dtk/config.json
# - macOS:  ~/Library/Application\ Support/dtk/config.json
# - Linux:  ~/.config/dtk/config.json
```

> [!TIP]
> **Zero-config by default** — DTK works out of the box without any configuration file. All settings have sensible defaults. You only need to create or edit the config file if you want to customize behavior.

A minimal config that only disables emoji would be:

```json
{
  "display": {
    "emoji": false
  }
}
```

Any omitted keys use their defaults.

## Full Configuration File

```json
{
  "tracking": {
    "enabled": true,
    "retentionDays": 90,
    "dbPath": null,
    "tokenizer": "Cl100kBase"
  },
  "display": {
    "emoji": true
  },
  "tee": {
    "mode": "failures",
    "directory": null,
    "maxFiles": 20,
    "maxFileSizeBytes": 1048576
  }
}
```

## Tracking

Controls token usage tracking and persistence.

| Key | Default | Description |
|-----|---------|-------------|
| `enabled` | `true` | Enable or disable token tracking |
| `retentionDays` | `90` | How many days of history to keep |
| `dbPath` | `null` | Custom SQLite path (defaults to `%LOCALAPPDATA%/dtk/tracking.db` on Windows, `~/.local/share/dtk/tracking.db` on Linux/macOS) |
| `tokenizer` | `"Cl100kBase"` | Tokenizer model used for token counting (see [Tokenizer Models](#tokenizer-models)) |

## Display

Controls terminal output formatting.

| Key | Default | Description |
|-----|---------|-------------|
| `emoji` | `true` | Enable emoji characters (✓, etc.) |

## Tee Logs

Controls raw output logging to disk.

| Key | Default | Description |
|-----|---------|-------------|
| `mode` | `"failures"` | `"failures"` saves only failed runs; `"always"` saves all runs; `"never"` disables tee logging |
| `directory` | `null` | Log directory (defaults to `%LOCALAPPDATA%/dtk/tee`) |
| `maxFiles` | `20` | Maximum log files to keep; oldest are deleted first |
| `maxFileSizeBytes` | `1048576` | Maximum size per log file (1 MB) |

## Tokenizer Models

The `tokenizer` field accepts one of the following values:

| Value | Encoding | Typical Models |
|-------|----------|----------------|
| `Cl100kBase` | `cl100k_base` | GPT-4, GPT-3.5-turbo |
| `O200kBase` | `o200k_base` | GPT-4o, o1, o3 |

Token counting uses the [Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers) library, which provides accurate BPE tokenization matching the selected model's encoding.

> [!NOTE]
> **Which tokenizer should I pick?** Claude, Gemini, and other non-OpenAI models use their own tokenizers, but no public BPE files are available for them. The `Cl100kBase` default provides a close-enough token estimate for any model and is the recommended choice unless you specifically need `O200kBase` for GPT-4o/o1/o3 accuracy.

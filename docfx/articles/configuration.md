# Configuration

DTK uses an optional JSON configuration file at:

```sh
~/.config/dtk/config.json
```

All settings have sensible defaults — no configuration is required to get started.

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
    "colors": true,
    "emoji": true,
    "width": 120
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
| `colors` | `true` | Enable colored terminal output |
| `emoji` | `true` | Enable emoji characters (✓, etc.) |
| `width` | `120` | Display width in characters |

## Tee Logs

Controls raw output logging to disk.

| Key | Default | Description |
|-----|---------|-------------|
| `mode` | `"failures"` | `"failures"` saves only failed runs; `"always"` saves all runs |
| `directory` | `null` | Log directory (defaults to `%LOCALAPPDATA%/dtk/tee`) |
| `maxFiles` | `20` | Maximum log files to keep; oldest are deleted first |
| `maxFileSizeBytes` | `1048576` | Maximum size per log file (1 MB) |

## Tokenizer Models

The `tokenizer` field accepts one of the following values:

| Value | Encoding | Typical Models |
|-------|----------|----------------|
| `Cl100kBase` | `cl100k_base` | GPT-4, GPT-3.5-turbo |
| `O200kBase` | `o200k_base` | GPT-4o |
| `P50kBase` | `p50k_base` | Codex, text-davinci |
| `P50kEdit` | `p50k_edit` | text-davinci-edit, code-davinci-edit |
| `R50kBase` | `r50k_base` | GPT-3 |

Token counting uses the [Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers) library, which provides accurate BPE tokenization matching the selected model's encoding.

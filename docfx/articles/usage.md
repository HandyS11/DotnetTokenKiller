# Usage Guide

## General Syntax

```sh
dtk [OPTIONS] <COMMAND>
```

### Global Options

| Option | Description |
|--------|-------------|
| `-h`, `--help` | Print help information |
| `-v`, `--version` | Print version information |

## Commands

### `dtk dotnet build`

Run `dotnet build` with filtered output. All positional arguments and flags are forwarded:

```sh
dtk dotnet build
dtk dotnet build --configuration Release
dtk dotnet build src/MyProject/MyProject.csproj
dtk dotnet build --no-restore
```

On success, output is reduced to a single summary line. On failure, errors are grouped by file with workspace-relative paths and top error codes.

### `dtk dotnet test`

Run `dotnet test` with filtered output:

```sh
dtk dotnet test
dtk dotnet test --filter "Category=Unit"
dtk dotnet test --configuration Release
dtk dotnet test src/MyProject.Tests/MyProject.Tests.csproj
```

Passing tests are summarized with counts. Failing tests show the test name, duration, error message, and a clean stack trace with relative paths. xUnit/NUnit/MSTest adapter banners, license warnings, and framework internals are stripped.

### `dtk dotnet restore`

Run `dotnet restore` with filtered output:

```sh
dtk dotnet restore
dtk dotnet restore src/MyProject/MyProject.csproj
```

Restore errors include the error code, message, and workspace-relative project path.

### `dtk dotnet clean`

Run `dotnet clean` with filtered output:

```sh
dtk dotnet clean
dtk dotnet clean --configuration Release
```

### `dtk integrate`

Install dtk integration artifacts for an AI assistant provider:

```sh
dtk integrate claude      # Claude Code skill + PreToolUse hook
dtk integrate copilot     # GitHub Copilot instructions section
dtk integrate gemini      # Gemini CLI hook + settings merge
dtk integrate cursor      # Cursor rules file
dtk integrate windsurf    # Windsurf rules file
dtk integrate aider       # Aider instructions + .aider.conf.yml section
dtk integrate jetbrains   # JetBrains AI guidelines section
```

All commands accept `--force` to overwrite existing files and `--dir <path>` to target a specific directory.

See [AI Agent Setup](ai-agent-setup.md) for details on what each provider installs.

### `dtk gain`

Display token savings analytics. See [Token Analytics](token-analytics.md) for details.

```sh
dtk gain               # last 30 days
dtk gain --days 7      # last 7 days
dtk gain --project     # current project only
dtk gain --json        # machine-readable JSON output
dtk gain --export csv  # export raw records as CSV
```

### `dtk reset`

Clear tracking data (and optionally all dtk state):

```sh
dtk reset              # prompts for confirmation
dtk reset --force      # skips confirmation
dtk reset --all        # also removes tee logs and config file
dtk reset --all --force  # full cleanup without confirmation
```

### `dtk config`

View or modify configuration without editing the JSON file directly:

```sh
dtk config show                          # display all keys and current values
dtk config set <key> <value>             # update a single value and save
```

Examples:

```sh
dtk config set tracking.enabled false
dtk config set tracking.retentionDays 30
dtk config set display.width 100
dtk config set tee.mode Always
dtk config set tracking.tokenizer O200kBase
```

Returns exit code `1` with an error message if the key is unknown or the value is invalid. See [Configuration](configuration.md) for the full list of supported keys and valid values.

### `dtk doctor`

Run self-diagnostic checks:

```sh
dtk doctor
```

Reports pass/fail for four checks: dotnet SDK availability, config file load, tracking database path access, and tee directory writability. Exits `0` if all pass, `1` if any fail.

### `dtk completion`

Print a shell completion script:

```sh
dtk completion bash
dtk completion zsh
dtk completion fish
dtk completion powershell   # also accepts: pwsh
```

Pipe the output into your shell profile to enable tab completion for all `dtk` subcommands.

## Passthrough Behavior

Any `dotnet` subcommand not in the supported list (build, test, restore, clean) is passed through to `dotnet` unchanged:

```sh
dtk dotnet publish    # runs: dotnet publish
dtk dotnet pack       # runs: dotnet pack
dtk dotnet run        # runs: dotnet run
```

## Log Files

DTK can save the raw, unfiltered command output to disk. This is controlled by the `tee.mode` configuration setting (see [Configuration](configuration.md)). By default, only failed runs are saved.

To print the log path after a command:

```sh
dtk dotnet test --show-log
```

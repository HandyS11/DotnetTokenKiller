# Usage Guide

Every command dtk supports, the flags it accepts, and what each one changes about the output. If you only need the short version: prefix any supported `dotnet` command with `dtk` and everything else stays the same.

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

The `dotnet` word is optional for the filtered subcommands: `dtk test --filter "Category=Unit"` runs `dtk dotnet test --filter "Category=Unit"`, and `dtk build`, `dtk restore`, `dtk clean`, `dtk format`, `dtk list package`, `dtk publish` and `dtk pack` work the same way. They are not listed in `dtk --help` or in shell completion.

### `dtk dotnet build`

Run `dotnet build` with filtered output. All positional arguments and flags are forwarded:

```sh
dtk dotnet build
dtk dotnet build --configuration Release
dtk dotnet build src/MyProject/MyProject.csproj
dtk dotnet build --no-restore
dtk dotnet build -q                  # quiet: filtered content only, no DTK meta-output
```

On success, output is reduced to a single summary line. On failure, errors are grouped by file with workspace-relative paths, each as `(line,col) CODE: message`; a build that only warns lists its warnings the same way. A file's diagnostics that share a code and a message read once, with each position, in line order, and a count (`(96,59) (99,59) (111,59) CS1503 ×3: message`; a line lists at most 20 positions and then counts the rest, `… +N more`, with the count and the file's heading keeping the full numbers), and a file that comes to a single line is printed on it, led by its name (`src/Foo.cs(8,10) CS1002: ; expected`), with no heading. Messages are printed whole, up to 400 characters for an error and 160 for a warning; links are removed with the "see …" that introduced them, and the `System.` and `Microsoft.` namespaces inside a quoted type name are dropped (`'System.Collections.Generic.IEnumerable<int>'` reads `'IEnumerable<int>'`; a quoted package, assembly, path or file name is left as written). A `Top codes:` line, ranking the codes among the diagnostics listed, follows only when the build reported more diagnostics than are listed (a project built for several target frameworks reports each diagnostic once per framework, and dtk shows it once) and more than one code is listed. The summary counts the projects involved, those that failed as well as those that built, by project file (a solution is not a project), and a multi-targeted project counts once.
The summary is always the last line (`✓ dotnet build …`, or `✗ dotnet build failed (exit 1): 3 errors, 1 warning …`
when the exit code is non-zero), so `| tail -1` carries the verdict.

### `dtk dotnet test`

Run `dotnet test` with filtered output:

```sh
dtk dotnet test
dtk dotnet test --filter "Category=Unit"
dtk dotnet test --configuration Release
dtk dotnet test src/MyProject.Tests/MyProject.Tests.csproj
dtk dotnet test -q                   # quiet mode
```

Passing tests are summarized with counts. When the run compiled with warnings, the status line ends with their count and up to three top codes (`· 3 build warnings (CS0219×2, CS8602)`); a build that is already up to date prints none. Failing tests show the test name, duration, error message, and a clean stack trace with relative paths. xUnit/NUnit/MSTest adapter banners, license warnings, and framework internals are stripped.

### `dtk dotnet restore`

Run `dotnet restore` with filtered output:

```sh
dtk dotnet restore
dtk dotnet restore src/MyProject/MyProject.csproj
dtk dotnet restore -q
```

Restore errors include the error code, message, and workspace-relative project path, one line each, followed by the
status line `✗ dotnet restore: 2 errors`.

### `dtk dotnet clean`

Run `dotnet clean` with filtered output:

```sh
dtk dotnet clean
dtk dotnet clean --configuration Release
dtk dotnet clean -q
```

A successful clean prints `✓ dotnet clean`. A failed one lists its first five error lines, then `... and N more errors`
when there are more, and ends with the status line `✗ dotnet clean: 7 errors`, which counts every error.

### `dtk dotnet format`

Run `dotnet format` with filtered output:

```sh
dtk dotnet format
dtk dotnet format MyApp.slnx
dtk dotnet format --verify-no-changes
dtk dotnet format -q
```

When nothing needs formatting, the raw command produces no output at all. dtk synthesises a `✓ dotnet format (nothing to format)` confirmation so AI agents receive an explicit positive signal. When `--verify-no-changes` finds violations, they are grouped by file and code, one line each, with workspace-relative paths, and the last line is the verdict:

```text
Fmt/F.cs: WHITESPACE ×10 (5:28 insert 1 space, 7:17 insert 1 space, 7:18 insert 1 space, 11:9 delete 2 chars, 11:28 insert 1 space, 11:29 insert 1 space, 11:31 insert 1 space, 11:33 insert 1 space, 11:34 insert 1 space, 12:18 delete 1 char)
Fmt/A.cs: WHITESPACE ×5 (2:1 insert newline, 2:15 newline, 3:21 newline + 4 spaces, 9:7 insert 2 spaces, 11:1 4 spaces)
Fmt/B.cs: WHITESPACE ×2 (8:25 newline + 8 spaces, 10:40 newline + 12 spaces)
Fmt/B.cs: IDE0005 (1:1): Using directive is unnecessary.
Fmt/B.cs: IDE0161 (3:11): Convert to file-scoped namespace
Fmt/B.cs: IMPORTS (1:1): Fix imports ordering.
Fmt/NoFinal.cs: FINALNEWLINE (6:2 insert newline)
✗ dotnet format: 21 violations in 4 files
```

Each position is `line:col`, in line and column order, and `×n` appears only for a group of more than one. A whitespace violation says what to change there: the text the tool puts in (`newline + 4 spaces`, `4 spaces`), or `insert …` and `delete n chars`. A violation with no change to name (`IMPORTS`, `CHARSET`, an analyzer's diagnostic) has its message once, after the positions; the same code with different messages in one file keeps a line for each message. The `[project.csproj]` suffix is dropped, and a diagnostic the tool reports twice counts once. The files with the most violations come first, ties in name order. A line lists at most 20 positions, then ` … +N more` (`×n` keeps the full count), and the report at most 40 lines, then one line such as `… 6 more groups in 2 files (dtk log)`; the verdict counts everything either way. It reads `⚠` instead of `✗` when the exit code is zero.

### `dtk dotnet list package`

Run `dotnet list package` with filtered output, covering all four of its variants:

```sh
dtk dotnet list package
dtk dotnet list package --outdated
dtk dotnet list package --deprecated
dtk dotnet list package --vulnerable
```

The plain variant collapses packages shared by every project into one `all projects:` line, then
lists only each project's remaining additions. The `--outdated`, `--deprecated`, and `--vulnerable`
variants group findings by package across projects instead of repeating them per project, and a
clean audit run (no findings) collapses to a single `✓` line. Output is capped at 30 package groups,
with an explicit truncation line if there are more. The summary is always the last line:
`✓ dotnet list package (8 projects, 21 packages)` for the plain variant and
`⚠ dotnet list package --outdated: 1 package with updates (all 8 projects)` when an audit variant finds something
(`✗` instead on the rare non-zero exit).

If the output arrives in a shape dtk does not recognise — a localised SDK or a future column layout — dtk
prints it unfiltered, followed by the
`⚠ dotnet list package: unrecognized output, passed through unfiltered` line, rather than guessing.
`dotnet list package --format json` is machine-readable, so dtk passes it through untouched and without that line.
`dotnet list package` always exits 0, even when it reports vulnerable packages, so this is the only
signal available: a clean `✓` from dtk always means dtk read the tables, never that it failed to.

#### Which spellings are filtered

Only the two-tokens-adjacent form is filtered. These are:

```sh
dtk dotnet list package                 # filtered
dtk dotnet list package --outdated      # filtered
dtk dotnet list package MyApp.sln       # filtered (target after the two tokens)
```

These pass through to `dotnet` **unfiltered**, with the raw output shown as-is:

```sh
dtk dotnet list MyApp.sln package       # NOT filtered — target between the two tokens
dtk dotnet package list                 # NOT filtered — noun-first spelling
```

`dotnet list <PROJECT|SOLUTION|FILE> package` is the SDK's primary documented synopsis, and
`dotnet package list` exists in .NET 10, so both are legitimate spellings — dtk simply does not
match them yet, because its subcommand matcher requires the tokens to be adjacent at the front of
the argument list. Passthrough is safe (the output is correct, just unfiltered) and these runs are
recorded under `list` and `package` respectively in `dtk gain --coverage`. The generated agent hooks
do not rewrite them either.

### `dtk dotnet publish`

Run `dotnet publish` with filtered output:

```sh
dtk dotnet publish
dtk dotnet publish src/MyApp/MyApp.csproj -c Release
dtk dotnet publish -r linux-x64 --self-contained
```

Diagnostics are summarised exactly as for `dtk dotnet build`. On success each project's publish directory, with a
workspace-relative path, sits directly above the summary line, which stays last:

```text
  MyApp -> src/MyApp/bin/Release/net10.0/publish/
✓ dotnet publish (2 projects)
```

On failure the output is the build's error layout ending in the `✗` summary line, and no publish directory is listed.

### `dtk dotnet pack`

Run `dotnet pack` with filtered output:

```sh
dtk dotnet pack
dtk dotnet pack -o artifacts
dtk dotnet pack -c Release -p:Version=1.2.3
```

Diagnostics, including NuGet's `NU5xxx` pack warnings, are summarised exactly as for `dtk dotnet build`. On
success every package created (`.nupkg` and `.snupkg`), with a workspace-relative path, sits directly above the
summary line, which stays last:

```text
  artifacts/MyLib.1.2.3.nupkg
  artifacts/MyLib.1.2.3.snupkg
✓ dotnet pack (1 project)
```

On failure the output is the build's error layout ending in the `✗` summary line, and no package is listed.

`dtk dotnet publish --interactive` and `dtk dotnet pack --interactive` are not filtered: they run with the
terminal attached, as they did before dtk filtered these commands, so a credential provider can prompt for a
private feed. They are recorded as unmeasured passthrough runs.

Output that another program reads is not filtered either: `dtk dotnet list package --format json`, and `-getProperty`,
`-getItem` or `-getTargetResult` (also spelled `--getProperty` or `/getProperty`) on `build`, `test`, `restore`,
`clean`, `publish` and `pack`, print exactly what `dotnet` prints, so `| jq` and command substitution keep working.
dtk's own options (`-q`, `-v` without a level, `--show-log`) are dropped from these runs, which are recorded like the
`--interactive` ones.

### `dtk pipe`

Filter output on stdin from a command dtk did not run — CI logs, or any invocation the hook
missed:

```sh
# Accurate verdict: the producing command's exit code is passed explicitly.
dotnet build > build.log 2>&1; dtk pipe build --exit-code $? < build.log

# Convenient form. Bash cannot give a pipeline's right-hand side its predecessor's
# status, so without --exit-code the verdict is assumed to be success.
dotnet build 2>&1 | dtk pipe build

dotnet list package --outdated 2>&1 | dtk pipe list package
```

`dtk pipe` exits with whatever `--exit-code` it was given, so a CI step wrapping a failed build
still fails. Piped runs are tracked separately from runs dtk executed itself under a `Source`
column in `dtk gain --coverage`.

### `dtk init`

Install dtk integration artifacts for an AI assistant provider:

```sh
dtk init claude      # Claude Code skill + PreToolUse hook
dtk init copilot     # GitHub Copilot instructions section
dtk init copilot-cli # GitHub Copilot CLI hook + instructions section
dtk init gemini      # Gemini CLI hook + settings merge
dtk init codex       # Codex CLI hook + AGENTS.md section + skill
dtk init opencode    # OpenCode plugin + AGENTS.md section + skill
dtk init antigravity # Antigravity CLI hook + AGENTS.md section + skill
dtk init pi          # pi extension + AGENTS.md section + skill
dtk init oh-my-pi    # oh-my-pi extension + AGENTS.md section + skill
dtk init cursor      # Cursor rule + rewrite hook (--global: hook only)
dtk init devin       # Devin (formerly Windsurf) rule + rewrite hook; 'windsurf' is an alias
dtk init droid       # Factory Droid rewrite hook + AGENTS.md section + skill
dtk init crush       # Crush rewrite hook (crushrc section) + AGENTS.md section + skill
dtk init kilo        # Kilo Code rewrite plugin + AGENTS.md section + skill
dtk init amp         # Amp rewrite plugin + AGENTS.md section + skill
dtk init aider       # Aider instructions + .aider.conf.yml section
dtk init jetbrains   # JetBrains AI guidelines section
```

All commands accept `--force`/`-f` to overwrite existing files and `--dir <path>`/`-d` to target a specific directory. `--global`/`-g` installs into your home config instead of the project — supported for **claude**, **gemini**, **codex**, **opencode**, **antigravity**, **pi**, **oh-my-pi**, **aider**, **copilot-cli**, **cursor**, **devin**, **droid**, **crush**, **kilo**, and **amp** (the providers with a home config) — and cannot be combined with `--dir`:

```sh
dtk init claude --global      # ~/.claude, applies to every project
```

`dtk integrate` is an alias of `dtk init`.

See [AI Agent Setup](ai-agent-setup.md) for details on what each provider installs.

### `dtk gain`

Display token savings analytics. See [Token Analytics](token-analytics.md) for details.

```sh
dtk gain                       # last 30 days
dtk gain --days 7              # last 7 days
dtk gain --project             # current project only
dtk gain --command build       # filter to a specific command
dtk gain --json                # machine-readable JSON output
dtk gain --export csv          # export raw records as CSV
```

### `dtk log`

Retrieve a previous run's full output instead of re-running it:

```sh
dtk log                  # newest log for this project: last 100 lines
dtk log build            # newest build log for this project
dtk log --list           # what is available
dtk log --index 3        # the 3rd newest
dtk log --lines 300      # a wider window
dtk log --full           # everything
dtk log --all            # include other projects
```

Logs are written by the tee feature, which defaults to `tee.mode = Failures`: the latest run of each kind (build, test, …) in each project is always saved, so `dtk log` never shows an older run as the latest, and older runs are kept only when they failed with at least 500 bytes of output. Use `dtk config set tee.mode Always` to keep older passing runs too. Logs written by dtk 0.6.0 or earlier carry no project metadata and appear only under `--all`.

A run that dtk did not finish — because dtk itself was killed outright, or you pressed Ctrl-C twice — still leaves a log. `dtk log` shows it with `incomplete` in place of an exit code and a note saying the output ends where dtk was killed. A single Ctrl-C (or SIGTERM on Linux and macOS) lets dtk finish the log; see [Interrupting a run](#interrupting-a-run).

A log that hit `tee.maxFileSizeBytes` stops growing but stays readable: the body ends with a `[dtk: output truncated at <N> bytes]` marker, and `dtk log` shows a note that the output was truncated.

Passthrough subcommands dtk measures but does not filter — `msbuild`, `ef migrations`, and similar — are tee'd like any other run. Interactive passthrough (`run`, `watch`) stays attached to the terminal and is not tee'd, so `dtk log` will never find it.

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
dtk config show                          # display all keys and current values (plain `dtk config` does the same)
dtk config set <key> <value>             # update a single value and save
```

Examples:

```sh
dtk config set tracking.enabled false
dtk config set tracking.retentionDays 30
dtk config set display.emoji false
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

Print a shell completion script for the given shell:

```sh
dtk completion bash
dtk completion zsh
dtk completion fish
dtk completion powershell   # also accepts: pwsh
```

**bash** — write to a dedicated file (preferred) or to the system-wide completions directory:

```sh
dtk completion bash > ~/.bash_completion
# or system-wide:
dtk completion bash > /etc/bash_completion.d/dtk
```

**zsh** — the script must be autoloaded from `$fpath`; do not source it inline in `~/.zshrc`:

```sh
mkdir -p ~/.zfunc && dtk completion zsh > ~/.zfunc/_dtk
```

Then add these two lines to `~/.zshrc` before any existing `compinit` call:

```sh
fpath=(~/.zfunc $fpath)
autoload -Uz compinit && compinit
```

**fish** — write to the completions directory:

```sh
dtk completion fish > ~/.config/fish/completions/dtk.fish
```

**PowerShell** — append to your profile:

```pwsh
dtk completion powershell >> $PROFILE
```

## Verbosity

Add `-v` / `--verbose` to any `dtk dotnet` (or `dtk pipe`) command to echo the resolved command line before running it. Add `--vv` instead to also dump the raw, unfiltered `dotnet` output and the elapsed time — useful when the filtered result looks wrong and you need to see what dtk actually saw:

```sh
dtk dotnet build --verbose
dtk dotnet test --vv
```

`--vv` implies everything `-v`/`--verbose` shows, plus the raw dump; the two are not combined.

## Quiet Mode

Add `-q` / `--quiet` to any `dtk dotnet` command to suppress DTK meta-output and forward only the filtered content. This is useful when piping output into other tools:

```sh
dtk dotnet build -q | grep "error"
dtk dotnet test -q > test-results.txt
```

In quiet mode, verbosity flags (`-v`/`--verbose`, `--vv`) and `--show-log` are ignored — only the filtered command output is written.

## Passthrough Behavior

Any `dotnet` subcommand not in the supported list (build, test, restore, clean, format, list package, publish, pack) is passed through to `dotnet` unchanged:

```sh
dtk dotnet run        # runs: dotnet run
dtk dotnet watch      # runs: dotnet watch
dtk dotnet msbuild    # runs: dotnet msbuild
```

## Interrupting a run

Ctrl-C (SIGINT) and, on Linux and macOS, SIGTERM stop a `dtk dotnet …` run without leaving `dotnet` running behind dtk:

- **Ctrl-C** at a terminal reaches `dotnet` as well as dtk. dtk gives `dotnet` up to 5 seconds to stop on its own; when it does, dtk shows the filtered output, saves the log and records the run as usual, and exits with `dotnet`'s own exit code. If `dotnet` is still running after 5 seconds, or the signal reached dtk alone, dtk stops it and every process it started, and exits with code `130`.
- **Ctrl-C with `dotnet run`, `dotnet watch`** and other interactive commands, which stay attached to the terminal (as does every passthrough command when tracking is off and `tee.mode` is `Never`): the command handles Ctrl-C itself and may take as long as it needs to shut down, so dtk waits for it without a time limit and exits with its exit code.
- **SIGTERM** (Linux and macOS only) stops `dotnet` and every process it started at once; dtk exits with code `130`. On Windows, a console close, logoff or shutdown ends dtk the default way.
- A **second** Ctrl-C or SIGTERM ends dtk immediately.

When dtk stops `dotnet` itself, it prints no filtered output and does not record the run in `dtk gain`, but it finishes the log, which shows exit code `130` and is kept like any failed run's. Other dtk commands (`gain`, `log`, `config`, …) keep the default behavior: Ctrl-C ends them at once.

## Log Files

DTK can save the raw, unfiltered command output to disk. This is controlled by the `tee.mode` configuration setting (see [Configuration](configuration.md)). By default, failed runs and the latest run of each kind are saved.

Each log file also records the full command line and working directory that produced it, alongside the output. Log files are kept owner-only (mode `0600`) for that reason.

To print the log path after a command:

```sh
dtk dotnet test --show-log
```

Or retrieve a previous run's log without re-running anything: see [`dtk log`](#dtk-log) above.

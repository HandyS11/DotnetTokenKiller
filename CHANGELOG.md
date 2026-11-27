# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Under Claude Code with rtk's hook registered beside dtk's, a command both would rewrite (`git status && dotnet build`
  even with rtk's `dotnet` exclusion) ran with only one rewrite, because Claude Code runs the hooks in parallel and applies
  one `updatedInput`. `dtk hook claude` now refuses such a command with the merged command in the reason, and the agent
  re-sends it, one extra turn. It fails open when rtk is missing, slow (2 s) or answers unexpectedly, and is sent only
  when the merged command is stable under both rewrites. In default permission mode the re-sent command needs allow
  rules for the prefixed forms (`Bash(rtk git status:*)`, `Bash(dtk dotnet:*)`; never `Bash(rtk:*)`, which allows
  `rtk run <anything>`); auto and bypass modes are unaffected. Writing `rtk …` and `dtk dotnet …` yourself avoids the
  turn. See [Running beside rtk](docs/articles/ai-agent-setup.md#running-beside-rtk).

### Added

- `dtk doctor`'s rtk check probes the installed hook with `git status && dotnet build` and warns when the command does
  not come back merged.

### Changed

- The agent hooks turn `dotnet test | tail -20` and `| head -5` (also `-n N`, `--lines N`) into
  `dtk dotnet test --max-lines 20`, dropping the pipe. The command now exits with dotnet's code, so
  `dotnet build | tail -5 && git add .` stops on a failure, which it did not before (`tail`'s exit code hid it). `tail`
  gave the last N lines; `--max-lines` keeps the first N plus the status line, where the verdict is.

## [0.13.0] - 2026-11-22

### Added

- Every Claude Code agent in a .NET project now starts knowing dtk's rules — run it plainly, without `| tail` or
  `| grep`; use `dtk log build`/`dtk log test` instead of running a build again; `--names`, `DTK_RAW=1` — subagents
  included, which never loaded the skill. `dtk init claude` registers `dtk hook claude` for `SessionStart`
  (`startup|clear|compact`) and `SubagentStart` beside `PreToolUse`, and the hook adds those four lines to the agent's
  context only when its working directory is in a .NET project (a solution or project file, `global.json` or
  `Directory.Build.props` in it or a parent, or a solution or project file at most two levels below; or a project whose
  own `.claude/settings.json` registers the hook), in 10–19 ms per start with the native binary. Re-run `dtk init claude` (or `--global`) to add the two hooks to an existing install; `dtk doctor` warns until
  then and checks that the dtk on `PATH` answers them. When Cursor runs these Claude Code hooks, it gets a reply in its
  own schema.

### Changed

- The dtk section of `AGENTS.md` and `GEMINI.md`, the Copilot instructions, JetBrains AI's `.junie/guidelines.md` and
  the Cursor, Devin and Aider rule files lead with the same three rules (`dtk log --list` is now in the skill only).
  They change only when `dtk init <provider>` runs again. `dtk init <provider> --uninstall` still recognises the
  unedited rule files 0.11.0 and 0.12.0 wrote.
- The `dotnet-token-killer` skill is now the reference for what the rules leave out, and its description triggers on
  those options (`--tee`, `--show-output`, `--names`, `--fail-on-empty`, `--max-lines`, `dtk log`, `dtk pipe`, every
  warning of a build, `dtk gain`) instead of on every `dotnet` command, which loaded the whole skill at the start of most
  sessions only to repeat the rules. It explains the changed-files warning lines and drops the installation section.

### Fixed

- A build, test, publish or pack run in a git work tree hid warnings the work in progress caused, counting them among
  the `warnings in unchanged files not shown`:
  - a project-level warning such as NuGet's `App.csproj : warning NU1903` (a vulnerable package), which carries no
    position, was in no file at all. It is now filed under its project file, and an error of that kind reads
    `src/App.csproj NU1101: …` instead of `(no file) NU1101: …`; the same warning raised by two projects is listed
    for each. A warning that names no file (`CSC : warning CS8032`) is listed, since nothing says the change did not
    cause it;
  - a change to the build's configuration (a project file, a `.props` or `.targets` file, `.editorconfig`, a
    `.globalconfig` or `.ruleset`, `global.json`, `nuget.config`, `Directory.Build.rsp`) can raise a warning in any
    file (`<Nullable>enable</Nullable>`, an analyzer's severity, a package); every warning is now listed then;
  - without an `origin/HEAD`, which a remote added with `git remote add` lacks, only the files not committed yet
    counted as changed, so a warning in a file the branch had committed was hidden. dtk now lists every warning, as it
    does outside a work tree; `git remote set-head origin --auto` gives the repository its `origin/HEAD`.
- A status line that lists only the warnings of changed files no longer says `5 warnings (3 shown)` when it shows none
  of them: it reads `(3 parsed)` when some of what MSBuild counted matched no pattern. A warning NuGet reports twice
  word for word (at restore, then when the build reads the assets file), which MSBuild counts twice, reads
  `4 warnings (3 distinct)` rather than `(3 shown)`.
- The agent hooks rewrote a `dotnet` command whatever word stood before it, so `$D dotnet build` with `D` set to a dtk
  binary became `$D dtk dotnet build` and failed with `Unknown command 'dtk'`; `echo dotnet build`,
  `ssh host dotnet build` and `sudo dotnet build` were rewritten too. A hook now rewrites past assignments, shell
  keywords (`if`, `then`, `!`, `do`, …) and the launchers that run the command here with the same `PATH` (`env`,
  `time`, `timeout`, `nice`, `nohup`, `exec`), and leaves the command as it is behind any other word.
- The agent hooks rewrote `dotnet build 2>&1 | grep -E "error|warn"`, whose filter, written for MSBuild's lines, found
  nothing in dtk's (`(12,5) CS0029: …`) and left only the status line; an agent that met it stopped trusting dtk. A
  `dotnet` whose output goes to `grep`, `egrep`, `fgrep`, `rg`, `ag`, `ack`, `awk`, `sed` or `cut`, or to a file
  (`> build.log`, `>>`, `1>`, `&>`), is now left as it is. A pipe into `tail`, `head` or `tee`, and `2>&1` or
  `2> err.log` alone, are still rewritten.

## [0.12.0] - 2026-11-16

### Added

- In a git work tree with changes, a build, test, publish or pack run without errors lists only the warnings of the
  files the work in progress changed (committed since the branch left `origin/HEAD`, or not committed yet, or
  untracked and not ignored), and counts the others on one line with their top codes and the command that lists them
  all (`dtk log build --full | dtk pipe build`). The status line says how many are in changed files
  (`✓ dotnet build: 0 errors, 336 warnings (165 distinct), 1 in changed files`, or `none in changed files`).
  `dotnet test` lists them above its status line, which ends `· 2 build warnings (CS0219×2), 1 in changed files`;
  like its warning count, they come only from the projects that run compiled, so a run that compiled nothing reports
  none. A clean checkout, a run outside a git work tree, a run that failed with errors and `dtk pipe` list every
  warning as before. dtk runs the `git` found on `PATH` to find the changes, only when a report has warnings to list;
  if git does not answer within 10 seconds, every warning is listed.

### Changed

- The messages of a test run's failures share 4,500 characters instead of 300 each: a single failure keeps up to
  3,000, so an aggregated FluentAssertions scope or a container's validation errors show whole.
- A warning or error count that MSBuild inflated by repeating a diagnostic per target framework reads
  `336 warnings (172 distinct)` instead of `(172 shown)`, which looked like a list cut short.

### Fixed

- `dtk dotnet list <PROJECT|SOLUTION> package`, dotnet's own spelling, is filtered, and the agent hooks rewrite it;
  `dtk dotnet list package <PROJECT|SOLUTION>`, which dotnet rejects, now runs it.
- `dotnet jb cleanupcode` in a large repository no longer says `changed files not computed (tree too large)`: it
  compares only the file types code cleanup rewrites, and only files git does not ignore. At `--verbosity=ERROR`, which
  prints no file list, it no longer reports `0 processed, 0 skipped`.
- MSBuild's `MSB1001: Unknown switch.` names the switch (`Switch: --no-incremental`), which MSBuild prints after
  echoing the command line and the switches a `Directory.Build.rsp` added.
- When the user's data folder did not exist yet (`~/.local/share` in a fresh container or under a sandboxed `HOME`),
  dtk wrote its tracking database and tee logs to a `dtk/` folder in the working directory, which a git work tree then
  counted as changes; `dtk config set` did the same with `~/.config`. dtk now creates those folders and keeps its
  files there. A `dtk/` folder an earlier version left in a project can be deleted.

## [0.11.0] - 2026-11-05

### Added

- Five options, documented in the usage guide. `--tee <path>` keeps the run's raw output in a file you choose, as
  evidence, next to the normal filtered output (fails before `dotnet` starts when the file cannot be created).
  `--max-lines N` caps the filtered body and ends with `… N more lines (dtk log)`; the status line is never cut.
  On `dotnet test` and `dtk pipe test`, `--names` lists the tests that passed grouped by class, `--show-output` shows
  each failing test's console output, and `--fail-on-empty` makes a run in which no test matched exit 8
  (`✗ dotnet test: no test matched filter … — --fail-on-empty`). A run that `dotnet` prints unfiltered all the same
  (`DTK_RAW=1`, `--interactive`, `--format json`, `-getProperty`) refuses `--tee` and `--fail-on-empty` before it
  starts, and ignores the other three with a warning.
- `dotnet tool restore` reports `✓ dotnet tool restore: 2 tools (dotnet-ef, jb)`, and on failure each distinct error
  once.
- `dotnet ef migrations …` and `dotnet ef database …` drop the build and application log noise and keep the result; a
  failed build says to run `dtk dotnet build` for the errors, which ef never prints.
- `dotnet stryker` reports the mutation score and the counts, and, when Stryker's json reporter is on, up to 20
  surviving mutants read from its report.
- `dotnet jb cleanupcode` lists the files it changed, found by hashing the tree before and after, instead of every
  file it processed.
- `dtk pipe test` recognises test output in another language and says so, with the remedy (`run it through dtk dotnet
  test, or set DOTNET_CLI_UI_LANGUAGE=en`), instead of the generic "not recognized" line.

### Changed

- `dotnet stryker` and `dotnet jb cleanupcode` are no longer attached passthrough: they run filtered and print once
  they end. `dotnet tool restore` and `dotnet ef migrations`/`database` are no longer unfiltered, except where the
  output is a document or the tool waits for an answer: `ef … --json`, `ef migrations script` without `-o`,
  `ef database drop` without `--force` and `tool restore --interactive` still pass through. The agent hooks now
  rewrite all four to `dtk dotnet …`, but never auto-approve them, so Cursor's hook, which rewrites only what it can
  auto-approve, leaves them as written.
- On `ef`, `stryker` and `jb cleanupcode`, now filtered, every short option and `--verbose`, `--quiet` and `--help`
  still belong to the tool, so `-v` and `-q` there reach the tool instead of meaning dtk's verbose and quiet as on
  `build` or `test`. dtk reads only its long options `--vv`, `--show-log`, `--tee` and `--max-lines` there, and
  `--help` passes through unfiltered.
- A command containing `--tee`, one the shell would expand (a `$` outside single quotes, an unquoted `*`, `?` or `[`,
  any `{`), or one that spans more than one line (a `\` line continuation included) is no longer auto-approved by
  the Copilot CLI hook (`ask`) and is left as written by Cursor's.
- The generated skill and instruction text lists the new subcommands and options, and no longer says `-q` works on any
  command. Re-run `dtk init <provider>` to refresh the skill, and add `--force` to refresh a section in an existing
  `AGENTS.md`.

### Fixed

- A failing test with no stack trace no longer folds the test's own console output into its failure message.
- A Microsoft.Testing.Platform run in which every test was skipped (exit 8) reports `⚠ dotnet test: 0 passed,
  1 skipped (1 project, 0.30s) · no test ran (exit 8)` instead of the last 40 raw lines and `✗ dotnet test failed
  (exit 8)`.

## [0.10.0] - 2026-11-04

### Added

- `dtk test`, `dtk build`, `dtk restore`, `dtk clean`, `dtk format`, `dtk publish`, `dtk pack` and `dtk list package`
  work without the `dotnet` word and mean `dtk dotnet …`. `dtk test` used to answer "Unknown command 'test'. Did you
  mean 'reset'?", which points at the command that deletes your tracking data.
- A way to get `dotnet`'s own output when the filtered one is not enough. Set `DTK_RAW=1` (any non-empty value except
  `0` and `false`, in the environment or as a prefix) and `dtk dotnet …` runs `dotnet` unfiltered, recorded like any
  other passthrough, without dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`,
  `--show-log`). The rewrite hook of every supported agent now also leaves alone a `dotnet` that the command asks for
  raw: `rtk proxy dotnet …`, `command dotnet …` and `DTK_RAW=1 dotnet …`, next to the `\dotnet` and absolute-path forms
  that already worked. What a hook auto-approves does not change.
- `dotnet test --list-tests` prints the listed test names, one per line, with the build noise removed, and ends with
  `✓ dotnet test --list-tests: 4 tests (1 project)`. It printed nothing before.
- A test run that compiled with warnings reports them at the end of its status line, naming at most three codes, most
  frequent first: `✓ dotnet test: 1 passed (1 project, 0.01s) · 3 build warnings (CS0219×2, CS8602)`. Only projects
  compiled in that run report any: an up-to-date build recompiles nothing, so it has none to report.
- `dtk doctor` checks that rtk and dtk do not both rewrite `dotnet`. When an rtk hook is registered but
  `rtk rewrite "dotnet build"` still rewrites, it warns and names the rtk config file to fix. It never edits that
  file.

### Changed

- **The summary line is now the last line of every filtered run.** A script that reads the first line of dtk's output
  must read the last one. `dotnet build`, `publish` and `pack` no longer open with a `dotnet build: 3 errors, 1
  warning` header and a `---` separator; the verdict closes the report instead, and on a failure it carries the exit
  code (`✗ dotnet build failed (exit 1): 3 errors, 1 warning (1 project, 1.89s)`; a clean build is still
  `✓ dotnet build (1 project, 1.19s)`). `dotnet restore` errors, `dotnet format` and every `dotnet list package`
  report close with their summary instead of opening with it, a failed `dotnet clean` now ends with one
  (`✗ dotnet clean: 1 error`), and the `--show-log` hint comes before the status line. A run whose filter has nothing
  to show prints the last 40 raw lines and then a status line: `⚠ <command>: output not recognized, raw tail shown
  above`, `✗ <command> failed (exit N)`, or `✓ <command> (no output)` when there was nothing at all, where `<command>`
  is the `dotnet` command line as dtk ran it (`dotnet test --no-build --filter X`; `dtk pipe` names only `dotnet test`).
  On a zero exit such a run used to print nothing, and on a failure it put the status line above the raw tail. A run
  that exits 0 but whose output counted errors ends with `⚠` rather than `✓`; the exit code stays the verdict.
- A failing `dotnet test` run's status line now opens with `✗` like every other failure line, where 0.9.0 printed it
  bare: `✗ dotnet test: 1 failed, 3 passed (1 project, 0.07s)`. A script that matches the start of that line must allow
  for the glyph.
- `dtk log` now keeps the newest run of each kind (build, test, format, …) in each project, passing or failing and
  whatever its size, so `dtk log` and `dtk log build` always show the latest run. In the default `tee.mode` of
  `Failures` a passing run's log used to be deleted as it finished, so after a passing run `dtk log` showed an older
  failed run as if it were the latest. The cost is disk: the tee directory now holds a log for each kind and project
  even when everything passes, each at most `tee.maxFileSizeBytes` (1 MB by default) and counted toward `tee.maxFiles`
  (20). The next run of that kind in that project replaces the previous passing log, and any older log under 500
  bytes; failing logs of 500 bytes or more stay until `tee.maxFiles` rotates them out. A passing run's raw output,
  test stdout included, is therefore now kept on disk by default (readable by its owner only on Linux and macOS; on
  Windows a log takes the permissions of its folder, by default `%LOCALAPPDATA%/dtk/tee`); set `tee.mode` to
  `Never` (`dtk config set tee.mode Never`) to turn the logs off.
- `-v <level>` now goes to `dotnet`: `dtk dotnet build -v q` used to take `-v` for dtk's `--verbose` and pass `q` on as
  a project path (MSB1009). Only a `-v` followed by `q`, `quiet`, `m`, `minimal`, `n`, `normal`, `d`, `detailed`,
  `diag` or `diagnostic` (any case) is left to `dotnet`; a bare `-v` is still dtk's verbose flag.
- `dotnet publish --interactive` and `dotnet pack --interactive` still pass through unfiltered with the terminal
  attached, but no longer hand dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`,
  `--show-log`) to `dotnet`, which rejects them.
- Errors and warnings from `dotnet build`, `publish`, `pack` and `dotnet test` share one layout, grouped by file as
  `(line,col) CODE: message`, where errors were grouped by file and warnings by code. A file with a single line of
  diagnostics prints it on that line with no heading, and a diagnostic repeated in a file prints once with all its
  positions (`(96,59) (99,59) (111,59) CS1503 ×3: …`, at most 20, then `… +N more`). Messages are cut at 400
  characters for an error and 160 for a warning instead of 120, so CS1503 keeps its "to 'X'" and CS0121 its second
  overload, and help links and `System.` / `Microsoft.` prefixes on quoted type names are dropped. `Top codes` appears
  only when it adds something, and the project count is shown on a failed build too, counting a multi-targeted project
  once.
- Warnings hidden behind errors read `6 warnings not shown (dtk log)` instead of `6 warnings (3 shown) suppressed (use
  --vv to see)`: the failed run is already saved for `dtk log`, which costs far less than `--vv`'s raw dump.
- `dotnet format` problems are grouped by file and code, one line each, where each violation had a line of its own: the
  `[project.csproj]` suffix is gone, a whitespace change reads in words (`WHITESPACE ×7 (1:8 newline, 2:4 insert 1
  space, …)`), and the verdict is `✗ dotnet format: 18 violations in 2 files`. A line lists at most 20 positions, then
  `… +N more`. The report lists at most 40 group lines, then one `… 6 more groups in 2 files (dtk log)` line and the
  status line; the counts stay complete. The old cap of ten violations is gone.
- A `dotnet test` filter that matches nothing now says which one: `⚠ dotnet test: no test matched filter
  FullyQualifiedName~X (Probe.dll)`, where it said `0 tests found (no assembly matched)`. The exit code is still
  dotnet's own.
- `dtk gain` no longer records a loss: saved tokens are clamped at zero per run, so a filter that writes its own line
  for an empty input (`dotnet format` with nothing to do) shows `0` rather than `-20`. Runs already recorded are not
  rewritten.
- The instructions `dtk init <provider>` generates (the skill, the `AGENTS.md`, `GEMINI.md`, Copilot and JetBrains
  sections, and the Cursor, Devin and Aider rules) teach agents what this release added: `dtk test` without the
  `dotnet` word, `dtk log`, the raw-output forms, and not to pipe dtk's output into `tail` or `grep`, since the last
  line is the status. They no longer advise running `dtk dotnet clean` first for a full warning report, and they point
  to `dtk log` rather than `--vv` for a run's raw output. Re-run `dtk init <provider>` to refresh the skill, and add
  `--force` to refresh a section in an existing `AGENTS.md`; files written by earlier releases are still recognised by
  an upgrade and by `--uninstall`.

### Fixed

- A failed `dotnet clean` prints its error lines as MSBuild wrote them, with only the project root shortened. dtk used
  to treat each whole line as a path, so on Windows a line lost its final `.`.
- An MSBuild command-line error keeps the argument it rejected. After `MSB1009: Project file does not exist.` MSBuild
  names that argument on a line of its own (`Switch: nothere.csproj`), which `dotnet build`, `publish` and `pack` used
  to drop. It now ends the error's message: `(no file) MSB1009: Project file does not exist. Switch: nothere.csproj`.
- A `dotnet test` run whose test project does not compile (the red step of TDD) shows its compile errors grouped like
  `dotnet build` does, ending with `✗ dotnet test: 1 project did not build (2 errors)`, instead of the last 40 raw
  MSBuild lines, which lost the first errors and the total. When other projects ran, their failures follow the errors
  and one line names both counts: `✗ dotnet test: 1 failed, 2 passed · 1 project did not build (2 errors) (1 project,
  0.02s)`, where the trailing `(1 project, …)` counts the projects that ran tests. They were dropped silently before.
- `dotnet test` runs that printed nothing now print a result: a project whose tests are all skipped (VSTest's
  `Skipped!`), alone or next to others, a project that finished in under a millisecond (`Duration: < 1 ms`), and a run
  with the normal or detailed console logger (`Test Run Successful.` / `Test Run Failed.`), which also reported
  `1 failed, 0 passed` for a failing run that had passes.
- A failed test keeps its cause. A message may run to 300 characters (it was 200), and when it nests exceptions the
  innermost `---- Type : message` is kept (up to 200 characters) and the outer ones are shortened first. xUnit's
  assertion header (`Collections differ`, `Strings differ`, `Values differ`) and mismatch position survive
  (`Assert.Equal() Failure: Collections differ at pos 0 — Expected: [1, 2, 3], Actual: [9, 2, 3]`), and a pending
  Reqnroll step shows the step to implement (`No matching step definition: When the numbers are frobnicated`) instead of
  the generated binding snippet. xUnit v3 failures under Microsoft.Testing.Platform, a timeout included, keep their
  message instead of a blank line.
- `dtk dotnet list package --format json` and the MSBuild queries `-getProperty:`, `-getItem:` and `-getTargetResult:`
  (on `build`, `test`, `restore`, `clean`, `publish` and `pack`) pass through untouched, with the terminal attached
  and dtk's own output options (`-q`, `--quiet`, `-v` without a level, `--verbose`, `--vv`, `--show-log`) dropped. A
  filter used to append a line to the JSON document or replace the printed value with a status line, which broke
  `| jq .` and `-getProperty:Version`.
- Numbers and durations no longer follow the system culture: under a French locale a summary read `0,34s`. Durations
  also changed shape in every filter that prints one (build, publish, pack, test, restore and format). A minute or more
  now reads `1m03s`, and an hour `1h02m03s`, where 0.9.0 printed `63.21s` and `3723.40s`; a script that parses seconds
  out of a status line must read the new form. Under a minute, a duration the tool gives with fractions is unchanged
  (`12.50s`, `0.78s`). A source that only gives whole units no longer claims decimals: VSTest's `Duration: 12 s` prints
  `12s` rather than `12.00s`, `Duration: 1 m 40 s` prints `1m40s` rather than `100.00s`, and restore's `(in 2 sec)`
  prints `2s`.
- `dtk init` writes rtk's `[hooks] exclude_commands = ["dotnet"]` where rtk reads it: the path `rtk config` prints, or
  else the platform default (`$XDG_CONFIG_HOME` or `~/.config` on Linux, `~/Library/Application Support/rtk/config.toml`
  on macOS, `%APPDATA%\rtk\config.toml` on Windows, which is inferred and not verified). This applies to every
  provider that detects an rtk hook: Claude Code, Codex, Cursor, OpenCode, pi and oh-my-pi, Antigravity and Factory
  Droid. It wrote `$XDG_CONFIG_HOME` or `~/.config` on every system, so on macOS both hooks still rewrote
  `dotnet build`. Re-run `dtk init <provider>` to write the right file; an exclusion in the old place is left as it was.
  `dtk doctor`'s rtk check looks only at Claude Code's settings.
- `dtk config` with no subcommand shows the configuration instead of printing help and exiting 1.
- `dtk doctor` no longer lists a hook's checks twice when the working directory is your home directory.
- `dtk doctor`, `dtk gain` and dtk's other messages no longer wrap at 80 columns in the middle of a path when their
  output is piped or redirected.

## [0.9.0] - 2026-09-28

### Security

- Hardened the shell-aware `dotnet` command rewriter against command substitution, heredocs, and quoting edge
  cases that could let the Copilot CLI hook treat a disguised arbitrary shell command as "simple" and auto-approve
  it.
- The Copilot CLI hook auto-approves (`allow`) fewer commands. It still replies `allow` for a single, plain
  `dotnet build`, `test`, `restore`, `clean`, `format` or `list package` invocation, but now replies `ask` for a
  command with an environment-variable prefix (`FOO=1 dotnet build`), a `!` outside single quotes, a redirection, or
  a command substitution (even inside double quotes), and for every `dotnet publish` and `dotnet pack` it rewrites.

### Added

- Rewrite hooks for nine more coding agents, each installed by `dtk init <provider>` and probed by `dtk doctor`.
  In a project, each writes the shared `AGENTS.md` section and `.agents/skills/dotnet-token-killer` skill plus the
  file listed below; `--global` writes their equivalents under your home directory instead:
  - Codex CLI (`dtk init codex`): a `PreToolUse` hook in `.codex/hooks.json`. Codex runs it only once you approve
    it under `/hooks`.
  - OpenCode (`dtk init opencode`) and Kilo Code (`dtk init kilo`): a generated plugin, `.opencode/plugins/dtk.js`
    or `.kilo/plugin/dtk.js`.
  - Antigravity CLI (`dtk init antigravity`): a hook in `.agents/hooks.json` that replies `ask`, never `allow`.
  - pi and oh-my-pi (`dtk init pi`, `dtk init oh-my-pi`): a generated extension in `.pi/extensions/` or
    `.omp/extensions/`.
  - Factory Droid (`dtk init droid`): a `PreToolUse` hook for the `Execute` tool, in whichever of `hooks.json` and
    `settings.json` Droid reads its hooks from.
  - Crush 0.88.0 or later (`dtk init crush`): a marked section in `.crushrc`.
  - Amp (`dtk init amp`): a generated plugin in `.amp/plugins/dtk.js`.
- `dtk init cursor` also installs a rewrite hook in `.cursor/hooks.json`, and `dtk init cursor --global` installs
  that hook alone in `~/.cursor/hooks.json`. It auto-approves only the commands the Copilot CLI hook does.
- `dtk init devin` installs a rule and a rewrite hook for Devin Local and Devin CLI: `.devin/rules/dtk.md` and
  `.devin/hooks.v1.json` in a project. With `--global` it writes the hook under the `hooks` key of
  `~/.config/devin/config.json` (`%APPDATA%\devin\config.json` on Windows) and a section in
  `~/.codeium/windsurf/memories/global_rules.md`.
- `dtk init <provider> --uninstall` removes a previously installed provider integration and reports each file as
  removed, unchanged, or kept. It also recognizes, and removes, the unedited Cursor, Windsurf and Aider files an
  earlier dtk release wrote; a kept file's note says how to remove it anyway.
- Filters for `dotnet publish` and `dotnet pack`, summarizing diagnostics and output locations like `dotnet build`.
- Truncated tee logs are now marked with a `[dtk: output truncated at <N> bytes]` line, and `dtk log` warns when it
  prints a truncated log.
- A captured `dotnet` run (its stdio piped through dtk's filters) now stops cleanly on Ctrl+C. The child usually
  exits on its own within a 5-second grace period; dtk then shows the filtered output, saves the log, records the
  run, and exits with the child's own exit code. Only if the child is still running after 5 seconds (or the signal
  reached dtk alone) does dtk kill the process tree and exit `130`. An attached run (`dotnet run`, `dotnet watch`,
  or any passthrough run whose stdio stays attached to the terminal) is left to handle Ctrl+C itself; a second
  Ctrl+C ends dtk either way. SIGTERM (Linux/macOS only) stops the tree at once.
- The `dtk hook` rewrite now also covers `dotnet publish` and `dotnet pack`. A `dotnet publish --interactive` or
  `dotnet pack --interactive` run passes through unfiltered with the terminal attached, so a credential provider's
  prompt stays visible.

### Changed

- `dtk init windsurf` is now an alias of `dtk init devin`, since Windsurf is now Devin Desktop. It removes the
  `.windsurf/rules/dtk.md` an older dtk wrote, unless it was edited.
- `dtk doctor` can now report a warning, which leaves its exit code unchanged: a Codex hook not yet approved, a
  Codex project not yet trusted, or a Factory Droid hook in a file Droid does not read.
- Re-running `dtk init` on an instructions file whose dtk section is already current reports it as `unchanged`
  instead of suggesting `--force`.
- Local/dev builds now report version `0.0.0-dev` instead of a stale released version number.

### Fixed

- A settings file with comments or trailing commas now gets an error that names the cause, instead of "must
  contain a valid JSON object".
- `dotnet test` output is now parsed correctly when a project uses the Microsoft.Testing.Platform test runner.
- Build diagnostic deduplication no longer merges identical-looking diagnostics that come from different projects.
- `dotnet ef database drop`'s interactive confirmation prompt is passed through instead of being captured (and
  starved) by the passthrough filter.
- dtk no longer crashes at startup on Windows when it runs without a console (for example as a harness's child
  process or a service): it now keeps the default output encoding when it cannot switch to UTF-8.

## [0.8.0] - 2026-09-14

### Changed

- Stabilized the `0.8.0-beta.1` Native AOT release; no further user-facing changes.

## [0.8.0-beta.1] - 2026-09-14

### Added

- Native AOT packages for linux-x64, linux-arm64, linux-musl-x64, linux-musl-arm64, and osx-arm64, plus a Native AOT
  `dtk.exe` shim on win-x64, cutting cold-start time substantially.
- A native `dtk hook` replaces the Python-based hooks; `dtk integrate` is renamed to `dtk init`.

### Changed

- Tracking setup now starts in the background while the wrapped command runs, and output is counted as it streams,
  reducing per-command overhead.

### Fixed

- Hook registration merging now recognizes equivalent hook commands and keeps an already-identical registration
  instead of duplicating it.

## [0.7.2] - 2026-09-12

### Fixed

- Build diagnostics were silently under-reported in some cases; the reported count now matches the actual output.

## [0.7.1] - 2026-08-08

### Fixed

- `dtk doctor` now names the correct scope in its suggested hook remedies.

## [0.7.0] - 2026-08-08

### Added

- `dtk log` to retrieve a previous run's raw output.
- `dtk pipe` to filter output that dtk did not itself produce.
- `dotnet list package` output filtering.
- `dtk gain --coverage` to show tracking coverage, and a new rtk-style dashboard for `dtk gain`.
- Tee logs now stream as the command runs, so a killed run keeps whatever output was written before it was killed.
- Integration artifacts (hooks, skills) dtk previously generated now refresh themselves automatically when a
  newer dtk adds something they're missing, and `dtk doctor` now checks that an installed hook is registered,
  current, and actually fires.

### Fixed

- A per-assembly test no-match result no longer masks real test results from other assemblies.

## [0.6.0] - 2026-07-13

### Added

- A GitHub Copilot CLI integration provider (`dtk integrate copilot-cli`).
- `dtk integrate --global` to install AI agent integrations once for every project.
- dtk now takes over `dotnet` commands automatically when `rtk` is also installed, instead of conflicting with it.

### Fixed

- Filters now receive the wrapped command's exit code, so a failed run can no longer print a success summary.
- `dotnet build`/`test`/`restore` filters handle more real-world diagnostic formats correctly.
- Tee log hardening and deterministic child-process-tree termination when a run is interrupted.
- The Claude Code hook installer now writes a valid `updatedInput` schema and no longer duplicates hooks.
- Various CLI correctness fixes: `dtk gain` output, `--` argument forwarding, and verbosity handling.

## [0.5.0] - 2026-03-30

### Changed

- `dtk gain` now shows whether each tracked run succeeded.

## [0.4.0] - 2026-03-26

### Added

- `dotnet format` output filtering.

## [0.3.1] - 2026-03-25

### Fixed

- A missing command exclusion and other small correctness issues.

## [0.3.0] - 2026-03-24

### Changed

- Improved provider integration setup commands and their argument handling.

## [0.2.0] - 2026-03-22

### Added

- AI agent provider integration (`dtk integrate`) to set up hooks and skills for coding agents.
- A `dotnet-token-killer` Claude Code skill with usage documentation.

## [0.1.0] - 2026-03-19

### Added

- Initial release of `dtk`, a .NET CLI proxy that filters `dotnet build`, `test`, `restore`, `publish`, and `pack`
  output to reduce token usage.
- Token savings analytics (`dtk gain`).
- JSON configuration support.
- Packaging as a .NET tool (`dotnet tool install DotnetTokenKiller`).

[Unreleased]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.13.0...HEAD
[0.13.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.8.0-beta.1...v0.8.0
[0.8.0-beta.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.2...v0.8.0-beta.1
[0.7.2]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/HandyS11/DotnetTokenKiller/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/HandyS11/DotnetTokenKiller/releases/tag/v0.1.0

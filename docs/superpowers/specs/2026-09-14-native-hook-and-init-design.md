# Native `dtk hook` and `dtk init` — design

Date: 2026-09-14. Status: awaiting review.

## Goal

Replace the generated Python hook scripts with a native `dtk hook <provider>` subcommand, and rename
`dtk integrate` to `dtk init` (rtk's verb) while `integrate` keeps working.

The three hook-capable integrations — Claude Code, Gemini CLI and GitHub Copilot CLI — each install a
Python script generated from `HookScriptTemplates` and register it as `python3 …/dotnet-to-dtk.py`.
That needs a Python on `PATH` under the name `python3` (Windows users are told to hand-edit it to
`python`), and the Gemini registration cannot work on Windows at all: Gemini CLI runs hook commands
through PowerShell there, where `"$GEMINI_PROJECT_DIR"` is not an environment variable reference.

A POSIX sh script was considered and rejected (see *Research*): sh cannot parse JSON without `jq`, Git
for Windows does not ship `jq`, and most harnesses run hooks through PowerShell or `cmd` on Windows.
`dtk` itself is the one program guaranteed to be present wherever the hook fires, because the rewritten
command runs it. rtk reached the same conclusion: it dropped its jq-based `rtk-rewrite.sh` for
`rtk hook claude|gemini|copilot|…`.

## Scope

In scope:

1. `dtk hook <claude|gemini|copilot-cli>`: reads the harness payload on stdin, prints the rewrite.
2. `dtk init` replaces `dtk integrate`; `integrate` stays as an alias.
3. `init` registers `dtk hook <provider>` instead of writing a script, and migrates existing Python
   installs on re-run.
4. `doctor` checks the new registrations and recognizes legacy Python ones.
5. Docs, completion scripts and every user-visible `dtk integrate` string move to `dtk init`.

Out of scope (sub-project 2, its own spec): new hook-capable harnesses — Cursor `preToolUse`, Codex CLI,
Factory Droid, Crush, VS Code Copilot agent hooks, Junie CLI — and plugin-based ones (OpenCode, Kilo
Code, Amp). Also out of scope: Claude Code's `PowerShell` tool matcher, and any change to *which*
commands are rewritten (the rewrite semantics are ported, not redesigned).

## Research summary (2026-09-14)

How each current harness executes a hook command:

| Harness | Linux/macOS | Windows | Rewrite contract |
|---|---|---|---|
| Claude Code | `sh -c` | Git Bash (PowerShell fallback) | `hookSpecificOutput.updatedInput` |
| Gemini CLI | `bash -c` | PowerShell (`-NoProfile -Command`) | `hookSpecificOutput.tool_input` |
| Copilot CLI | `bash` field | `powershell` field | `permissionDecision` + `modifiedArgs`; non-zero exit **denies** |

Sources: [Claude Code hooks](https://code.claude.com/docs/en/hooks),
[Gemini hookRunner](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/hooks/hookRunner.ts),
[Copilot hooks configuration](https://docs.github.com/en/copilot/reference/hooks-configuration),
[rtk hook_cmd.rs](https://github.com/rtk-ai/rtk/blob/develop/src/hooks/hook_cmd.rs),
[Git Bash lacks jq — anthropics/claude-code#14817](https://github.com/anthropics/claude-code/issues/14817).
rtk's Windows failures to avoid: an unquoted absolute `C:\…\rtk.exe` path in a hook command loses its
backslashes under Git Bash ([rtk#3632](https://github.com/rtk-ai/rtk/issues/3632)); a `.sh` wrapper
opens through the WSL `bash.exe` association ([rtk#2323](https://github.com/rtk-ai/rtk/issues/2323)).
dtk registers the bare name `dtk`, never a path, so neither applies.

## 1. `dtk hook <provider>`

### Entry point

`Program.cs` checks `args is ["hook", ..]` first, before argument normalization, DI and Spectre, and
hands off to a `HookEntryPoint` in the CLI project, the same way `PassthroughEntryPoint` short-circuits
passthrough. The hook fires on every shell tool call, so it must not pay for the service container,
the Spectre command tree, tracking, SQLite or the tokenizer. It is therefore absent from `dtk --help`,
which is intended: only harnesses call it.

`HookEntryPoint` does I/O only:

- Reads stdin as bytes (`Console.OpenStandardInput`) and parses it as UTF-8 JSON, and writes stdout as
  UTF-8 bytes. It never goes through `Console.In`, whose Windows encoding is the OEM code page and would
  corrupt a command containing non-ASCII text when the rewrite round-trips it.
- When stdin is not redirected (a person typed `dtk hook claude`), prints a one-line usage to stderr and
  exits 0 without waiting.
- Unknown or missing provider: one line to stderr, exit 0.
- **Always exits 0**, and any exception means "no change": nothing on stdout, except that Gemini
  prints `{"decision":"allow"}` on every path where stdin parsed as JSON. A hook that fails must never
  block or deny the tool call.

### Rewrite core

Application layer, `Integration/Hooks/`, pure and unit-tested without processes:

- `DotnetCommandRewriter.Rewrite(string command) → string` — a faithful port of the Python `rewrite()`:
  - Matches `dotnet` + whitespace + a subcommand from `DotnetSubcommands`, multi-token subcommands
    (`list package`) matching any whitespace run between tokens, longest subcommand first, with a word
    boundary after the subcommand. The match is replaced by `dtk dotnet <subcommand as written>`.
  - Skips a match whose preceding character is not one of ` \t;&|({\`\n` (a path such as
    `/usr/lib64/dotnet/dotnet build` or `./dotnet build`).
  - Skips a match inside a single- or double-quoted region, honoring backslash escapes outside single
    quotes.
  - Skips a match already run by dtk: the word before `dotnet`, split at those boundary characters and
    stripped of any directory (`/` or `\`), is `dtk` or `dtk.exe`.
  - Existing behavior is preserved exactly, including the word boundary letting `dotnet build-server`
    match `build`; `dtk dotnet build-server` passes through to dotnet unchanged, so this is harmless.
  - No `Regex` construction per call on the hot path: a hand-written scanner, or a source-generated
    regex if the subcommand alternation can be made a constant with a test pinning it to
    `DotnetSubcommands`.
- `DotnetCommandRewriter.IsSimpleCommand(string command) → bool` — port of Copilot's
  `_is_simple_command`: no unquoted `;&|\`\n()`, skipping escaped characters.
- One payload handler per provider, bytes in → bytes out, using `JsonNode` (AOT-safe, as today):

| Provider | Reads | Prints on rewrite | Prints otherwise |
|---|---|---|---|
| `claude` | `tool_input.command` | `{"hookSpecificOutput":{"hookEventName":"PreToolUse","updatedInput":<tool_input with command replaced>}}` | nothing |
| `gemini` | `tool_input.command` | `{"decision":"allow","hookSpecificOutput":{"tool_input":{"command":<rewritten>}}}` | `{"decision":"allow"}`, nothing if stdin is not valid JSON |
| `copilot-cli` | `toolName == "bash"`; `toolArgs` as object or JSON string, `.command` | `{"permissionDecision":"allow" or "ask","modifiedArgs":<toolArgs with command replaced>}` — `ask` unless `IsSimpleCommand` | nothing |

These are the Python hooks' current outputs; other `tool_input`/`toolArgs` fields round-trip untouched.

### Performance

Measured once on this machine and recorded in `CLAUDE.md` under *Benchmarks*: median of 55 runs of
the local AOT publish's `dtk hook claude` fed a non-dotnet payload and a rewriting payload, against
`dtk --version` and against `python3 .claude/hooks/dotnet-to-dtk.py` fed the same payloads. Target:
the AOT hook's median within 3 ms of `dtk --version`. No new benchmark verb.

## 2. `dtk init`, with `integrate` as an alias

- `CliConfigurator` registers the command as `init` with `.WithAlias("integrate")`. Same positional
  `<provider>` and `--dir`, `--force`, `--global`; `dtk integrate …` behaves identically, exit codes
  included, and prints no deprecation warning. `dtk --help` lists `init`; whether Spectre also shows the
  alias there is left to Spectre. Examples switch to `init`.
- CLI-layer names follow the verb: `InitCommand`, `InitCommandSettings`, `CliConfigurator.InitCommand`.
  Application-layer names (`IntegrateUseCase`, `*Integrator`, `IntegrationResult`) keep the
  "integration" vocabulary, which still describes what they do, to avoid a rename-only diff.
- Every remedy and note string (`HookHealthChecker`, `CopilotCliIntegrator`, `ClaudeCodeIntegrator`,
  `IntegrateUseCase`, `DotnetSubcommands`) says `dtk init`.
- Completion scripts (bash, zsh, fish, powershell) list `init` as the top-level command, complete
  providers after both `init` and `integrate`, and add the missing `copilot-cli` provider.

## 3. What `init` installs

No hook script is written for any provider. Registrations are identical in both scopes, because nothing
is path-rooted any more:

| Provider | Registration file (project / `--global`) | Entry |
|---|---|---|
| `claude` | `.claude/settings.json` / `~/.claude/settings.json` | `PreToolUse`, matcher `Bash`, command `dtk hook claude` |
| `gemini` | `.gemini/settings.json` / `~/.gemini/settings.json` | `BeforeTool`, matcher `run_shell_command`, command `dtk hook gemini` |
| `copilot-cli` | `.github/hooks/dtk-dotnet.json` / `~/.copilot/hooks/dtk-dotnet.json` | see below |

Claude and Gemini get the bare command so it parses identically under sh, bash, Git Bash and
PowerShell. If `dtk` is missing from `PATH`, Claude Code documents a non-zero exit other than 2 as a
non-blocking hook error and runs the command unrewritten; Gemini CLI is expected to do the same, and
that is confirmed during implementation (open question 3). If it blocks instead, Gemini gets the same
guards as Copilot.

Copilot CLI denies the tool call on a non-zero exit, so a missing `dtk` would block every bash call.
Its registration guards both shells and drops the now-meaningless `cwd`:

```json
{
  "version": 1,
  "hooks": {
    "preToolUse": [
      {
        "type": "command",
        "matcher": "bash",
        "bash": "dtk hook copilot-cli || true",
        "powershell": "dtk hook copilot-cli; exit 0",
        "timeoutSec": 10
      }
    ]
  }
}
```

Instruction artifacts change only in text: the "shells out to `python3`" notes leave `SKILL.md`, the
`GEMINI.md` section and the Copilot instructions section. `SKILL.md` refreshes through its existing
provenance stamp; the two sections refresh as they do today.

## 4. Migrating an existing install

Re-running `dtk init <provider>` (or `dtk integrate <provider>`) on a Python install:

1. **Claude/Gemini registration.** `MergeJsonSettingsAsync` treats any inner hook whose command contains
   `dotnet-to-dtk.py` as dtk's own legacy registration — including one hand-edited from `python3` to
   `python` or to a quoted interpreter path. It is upgraded in place to `dtk hook <provider>` (keeping
   extra properties such as `timeout`) and extra equivalents are dropped, exactly as the current
   quote-insensitive dedupe does. `DeriveLegacyCommand`'s `$…_PROJECT_DIR` form is subsumed by this
   rule and removed.
2. **Copilot registration.** `dtk-dotnet.json` is dtk-owned. It is rewritten without `--force` when it
   parses as JSON and every `hooks.preToolUse` entry's `bash`/`powershell` value references
   `dotnet-to-dtk.py` or `dtk hook copilot-cli`; byte-identical is reported `unchanged`; anything else
   is `skipped` unless `--force`, as today.
3. **Old script.** `hooks/dotnet-to-dtk.py` beside the registration is deleted when dtk can prove it
   wrote it — its provenance stamp verifies, or it is unstamped and carries the legacy signature
   `_DTK_SUBCOMMANDS` — and reported on a new `removed` line. Its `hooks/` directory is deleted too if
   that leaves it empty. A script dtk cannot prove it wrote is left in place with a note that it is no
   longer registered; `--force` deletes it. The script is only considered when the registration step
   succeeded, so a malformed settings file never costs the user their hook.
4. **Reporting.** `IntegrationResult` gains `RemovedFiles`; `InitCommand` prints them as
   `removed  <path>`, and a removal counts as a change in the summary ("Done.", not "Already
   integrated.").

A project-scope removal shows up in the user's `git status`, which is the intended, reviewable outcome.

## 5. `doctor`

`HookInstallation` drops its `Script` artifact and carries the provider's hook command and the legacy
script path instead. Per installation and scope:

- **Presence.** An installation is reported when its registration names `dtk hook <provider>` or
  `dotnet-to-dtk.py`, or the legacy script exists.
- **Status.**
  - Registration names `dotnet-to-dtk.py`: fail — "legacy Python hook — run `dtk init <provider>
    [--global]` to migrate".
  - Registration names `dtk hook <provider>`: pass — "registered".
  - Neither: fail — "not registered — run `dtk init <provider> [--global]`".
  - The freshness ("stale", "modified locally") check goes away with the script.
- **Probe** (only when registered with `dtk hook`): runs `dtk` resolved from `PATH` — as the harness
  will, not the running binary's path — with `hook <provider>` and the existing all-subcommands
  payload, and asserts every subcommand is rewritten. Failures name the cause: `dtk` not found on
  `PATH`, a `dtk` too old to know `hook` (remedy: update the tool), or missing rewrites.
- `RemedyCommand` renders `dtk init …`.

## 6. Documentation

- `README.md`, `src/DotnetTokenKiller.Cli/README.md` (the packaged readme), `docfx/index.md`,
  `docfx/articles/getting-started.md`, `usage.md` and `ai-agent-setup.md` use `dtk init`, drop the
  Python requirement, and the manual-install sections become a settings snippet with `dtk hook …`
  instead of `curl` + `python3`. `ai-agent-setup.md` notes that `dtk integrate` still works.
- `CLAUDE.md`'s `dtk integrate copilot-cli` paragraph becomes `dtk init copilot-cli`.
- Generated API docs under `docfx/api` are regenerated, not hand-edited.

## 7. This repository's own hook

`.claude/settings.json` here runs the committed `.claude/hooks/dotnet-to-dtk.py`. Switching it to
`dtk hook claude` now would give every contributor whose installed dtk predates this change a hook error
on every Bash call. So in this change the committed script and registration stay as they are, frozen;
`SubcommandBindingTests` stops pinning the script to a generator that no longer exists; and a follow-up
after the first release containing `dtk hook` runs `dtk init claude` here, which migrates it through the
path in section 4. `.github/copilot-instructions.md` is regenerated in this change (its pinned section
text changes).

## 8. Testing

- **Rewriter unit tests** carry over every case in `HookScriptExecutionTests` and
  `HookScriptTemplatesTests` (boundaries, quotes, escapes, path-qualified `dtk`, `dtk.exe`, multi-token
  whitespace, every canonical subcommand, simple-vs-compound), plus non-ASCII commands round-tripping
  byte-exactly.
- **Differential check, once, before the Python templates are deleted:** a corpus of generated and
  hand-picked commands run through both the Python `rewrite()` and the C# port, with identical output
  required. Recorded in the PR; the Python code is not kept as a fixture.
- **Payload handler tests** per provider: rewrite, no rewrite, missing/empty command, invalid JSON,
  unexpected shapes (top-level array, `toolArgs` string vs object, non-`bash` tool), extra fields
  preserved.
- **CLI integration tests** run `dtk hook <provider>` as a process with piped payloads (no build
  spawned, so they run locally and against `DTK_TEST_BINARY`/`DTK_AOT_BINARY`): exit 0 on every path,
  stdout exact, no stdout for a person at a terminal, unknown provider.
- **`init` tests:** `dtk init X` and `dtk integrate X` produce identical files and output; migration
  from each shipped Python generation (stamped, unstamped legacy, hand-edited `python`, Copilot's old
  `dtk-dotnet.json`), including the modified-script, `--force` and malformed-settings cases.
- **`doctor` tests** for each status above and each probe failure.
- **Windows:** `eng/aot/test-windows.sh` and the `fallback-package.yml` Windows job pipe a payload
  through the installed tool as each harness would: `dtk hook claude` from Git Bash,
  `pwsh -NoProfile -Command "dtk hook gemini"` and `powershell -NoProfile -Command "dtk hook gemini"`
  with stdin redirected, and both Copilot guard commands, including a `PATH` without `dtk` exiting 0.
  This settles the open question below.
- `SavingsBaselineTests` is unaffected (no filter changes).

## Open questions resolved during implementation

1. **Does a native command launched by `powershell -Command "dtk …"` receive the redirected stdin?**
   Gemini on Windows and Copilot's `powershell` field depend on it. Verified by the Windows tests above;
   if it does not hold, the PowerShell forms pipe `$input` explicitly (`$input | dtk hook gemini`), and
   the spec is amended.
2. **Claude Code and Gemini with the same hook in project and global settings.** Both invocations now
   run the same command and produce the same output, so the result is correct either way; whether the
   harness deduplicates it only affects latency. Checked against the harness docs, not tested.
3. **Does Gemini CLI run the tool call when its hook command exits non-zero (other than 2)?** Checked
   against `hookRunner.ts`; see section 3 for the fallback.

## Acceptance

- No integration writes or requires Python; `HookScriptTemplates` is deleted.
- `dtk hook claude|gemini|copilot-cli` matches the Python hooks' rewrite on the differential corpus,
  exits 0 on every path, and meets the performance target.
- `dtk init` is the documented verb; `dtk integrate` works identically.
- Re-running `init` on each shipped Python install leaves exactly one `dtk hook` registration and no
  dtk-written Python script; `doctor` is green afterwards and flags a legacy install before.
- The build, the full test suite and `dtk dotnet format --verify-no-changes` pass; the Windows jobs pass.

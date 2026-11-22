# dtk's rules for every Claude Code agent — design

Date: 2026-11-22. Status: draft, awaiting the user's review. Branch: `feat/claude-agent-context`.

Source: item 3.1 of the 2026-11-22 dtk usage analysis (Claude Code transcripts of November, the tracking database).
Decided by the user (2026-11-22): deliver the rules through hooks (approach B below), not a CLAUDE.md section; keep
the skill, as the on-demand reference for what the rules leave out (*The skill* below).

Calls made by the author rather than the user (check these). Two set the scope: injection only in a .NET project
(*Detecting a .NET project*; it matters mostly for a `--global` install, which fires in every folder), and the shared
AGENTS.md / rule-file text leading with the same lines (it changes what every other harness's instruction file
renders). The rest: the rule text and its four lines; `SessionStart` on `startup`, `clear` and `compact` but not
`resume`, which the gate settles; every subagent type except `web-fetch` and `statusline-setup`, so a custom agent
without a shell still gets the rules; no context hook for the other harnesses in this change; the gate runs first, as
a go/no-go.

## Why

An agent that pipes dtk into `tail` cuts off the very diagnostics dtk kept, and an agent that cannot see them re-runs
the build instead of asking dtk for the output it saved. The analysis found `dtk … | tail -N` was the dominant pattern
(about a third of all dtk calls, most of them from subagents); of the failing runs that went through `tail`, roughly
four in ten cut off the failure details, half were followed by a re-run of the same command, and almost none used
`dtk log`.

The rules that prevent this are written down already, in the skill and in the dtk section other harnesses read. A
Claude Code agent sees neither:

- `dtk init claude` writes the skill and the `PreToolUse` hook, nothing in CLAUDE.md. A skill's body is loaded only
  when the agent invokes it, which a main session rarely does and a subagent almost never does.
- Subagents load CLAUDE.md, except the built-in Explore and Plan agents, which skip it (code.claude.com/docs,
  sub-agents § What loads at startup). Claude Code reads AGENTS.md only when no CLAUDE.md exists.

Claude Code 2.1.330 (installed here) offers two hook events that inject text into an agent's context:
`SessionStart` (the main session, "before the first prompt") and `SubagentStart` (each subagent, "before its first
prompt", for every agent type including Explore and Plan; Claude Code drops a duplicate when the hook reruns for the
same subagent). Both take `hookSpecificOutput.additionalContext`.

**What success looks like:** in a .NET project, every Claude Code agent — main session, general-purpose, Explore,
Plan, custom — starts knowing to run dtk plainly and to use `dtk log` instead of re-running, without the user editing
an instruction file, and with the text always matching the dtk installed.

## Goals

- `dtk init claude` (project and `--global`) registers `SessionStart` and `SubagentStart` beside `PreToolUse`, all
  running the same `dtk hook claude`; `--uninstall` removes all three; re-running `dtk init claude` adds the two new
  ones to an existing install.
- `dtk hook claude` answers both events with the rules as `additionalContext` when the payload's `cwd` is in a .NET
  project, and with nothing otherwise.
- `dtk doctor` reports a Claude Code install missing either new registration, with `dtk init claude` (or
  `--global`) as the remedy, and probes the context reply.
- The rules are written once (`IntegrationInstructions`) and also lead the dtk section of AGENTS.md, GEMINI.md, the
  Copilot instructions and the Cursor, Devin and Aider rule files, so every harness reads the same words.

## Non-goals

- No CLAUDE.md or AGENTS.md section for Claude Code (approach A, rejected).
- No context hook for the other harnesses yet (Codex `SessionStart`, Copilot CLI `sessionStart`, Droid, Cursor): they
  read their instruction file in every session already; whether their subagents do is a separate research item.
- No change to the `PreToolUse` rewrite or to any filter's output. Making the failure status line itself point at
  `dtk log` is a separate idea, not part of this change.

## Anti-goals

- Delaying or blocking a session or a subagent: the hook keeps its existing guarantee (always exit 0, print nothing on
  any failure), and the project check is bounded.
- Text in an agent that cannot use it: none outside .NET projects, none in agents without a shell tool.
- Growing the rules: they are paid at every session start and every subagent spawn, so they stay at four lines.

## Approaches

- **A. A `<!-- dtk -->` section in CLAUDE.md** (project `./CLAUDE.md`, global `~/.claude/CLAUDE.md`), as AGENTS.md
  gets today; the rtk-style variant writes `DTK.md` and adds one `@DTK.md` import. Visible and editable, but it misses
  Explore and Plan, writes into a file teams commit, goes stale after a dtk upgrade until `dtk init` runs again, and
  the global copy costs tokens in every project, .NET or not.
- **B. Hook-injected context (chosen).** Reaches every agent type, edits no instruction file, always matches the
  installed dtk, and costs nothing outside .NET projects. It costs one `dtk hook` start per session and per subagent
  (a few milliseconds for the native binary), and the text is not in a file the user can read; the docs and
  `dtk doctor` say what it injects.
- **A + B.** Both channels would inject the same rules twice in most agents. Rejected.

## Design

### The rules

One constant, `IntegrationInstructions.AgentRules`, four lines (wording the builder may polish, keeping the content
and the length):

```text
dtk (DotnetTokenKiller) filters this project's dotnet commands; a hook runs `dotnet build|test|…` through it.
- Run them plainly: dtk's output is already short and its last line is the verdict. Don't pipe it into `tail` or `grep`; `--max-lines N` caps it.
- To see more of a run, or what a cap cut, use `dtk log build` / `dtk log test` (`--full` for all) instead of running it again.
- `dtk dotnet test --names` lists the tests that passed; `DTK_RAW=1` gives dotnet's own output.
```

The first line names `dotnet build|test|…` literally rather than every subcommand in `DotnetSubcommands` (thirteen
names, paid at every start).

### Hook dispatch

`dtk hook claude` reads `hook_event_name` from the payload:

- `SessionStart` / `SubagentStart`: when the payload's `cwd` is in a .NET project, print
  `{"hookSpecificOutput":{"hookEventName":"<event>","additionalContext":"<rules>"}}`; otherwise print nothing.
  `SubagentStart` with `agent_type` `web-fetch` or `statusline-setup` (built-ins without a shell tool) prints nothing.
  The payload carries no tool list, so a custom or plugin agent without a shell still gets the rules; the docs say so.
- Anything else — `PreToolUse`, no event name, or another harness's own event name — stays on today's path,
  unchanged: the rewrite of any payload with a `tool_input.command`. Cursor's import of Claude Code's hook relies on
  that.

This stays on `HookEntryPoint`'s fast path (no DI, config, tracking or tokenizer) and inside its catch-all.

### Detecting a .NET project

A `cwd` is in a .NET project when it or one of its ancestors holds a `*.sln`, `*.slnx`, `*.slnf`, `*.csproj`,
`*.fsproj`, `*.vbproj`, `global.json` or `Directory.Build.props`; or, since a repository may keep its .NET code in a
subfolder (`backend/`), when a directory at most two levels below `cwd` holds a solution or project file. The walk
below skips hidden directories, `bin`, `obj`, `node_modules` and `artifacts`, stops at the first match, and gives up
(answering "not .NET") after a fixed budget of directory entries, so a huge non-.NET tree costs a bounded time. A
`cwd` that is missing, relative or unreadable is "not .NET".

### Registration

`ClaudeCodeIntegrator` writes three `HookRegistrationSpec`s into the same `settings.json` (`.claude/settings.json` or
`~/.claude/settings.json`), all with the command `dtk hook claude`:

| Event | Matcher |
|---|---|
| `PreToolUse` | `Bash` (unchanged) |
| `SessionStart` | `startup\|clear\|compact` |
| `SubagentStart` | `*` (every agent type; dtk filters) |

`resume` is left out on the assumption that a resumed transcript keeps the context it was given; the gate checks it
and adds `resume` if not. Uninstall removes the three. The legacy-script migration and rtk reconciliation are
unchanged.

### Doctor

`DescribeHooks` (or a Claude-specific check beside it) covers the two new registrations: a Claude Code install whose
`settings.json` registers `PreToolUse` but not `SessionStart`/`SubagentStart` gets a warning, `dtk init claude` (or
`--global`) as the remedy. The probe also feeds `dtk hook claude` a `SubagentStart` payload whose `cwd` is a temporary
directory holding a `.csproj`, and expects the rules back. Today's check finds the command anywhere in the file, so it
cannot tell the events apart; the builder chooses how to extend it.

### Harnesses that run Claude Code's hooks

Cursor and Devin also run the hooks registered in Claude Code's settings files (`ImportedClaudeHook`). If either maps
Claude's `SessionStart`/`SubagentStart` to an event of its own, it runs `dtk hook claude` with its own payload and
reads a Claude-shaped reply. The builder checks both harnesses' docs: dtk must stay harmless there (no blocked
session, no error). Only a payload named `SessionStart` or `SubagentStart` gets the context reply; any other event name
keeps today's behaviour. What is found goes in `docs/articles/harness-verification.md`.

### The shared text

`UsageBody`'s two bullets on `tail`/`grep` and `dtk log` are replaced by `AgentRules`' three bullets, moved to the top
of the list, so the dtk section of AGENTS.md and GEMINI.md, the Copilot CLI and IDE instructions, the skill and the
Cursor, Devin/Windsurf and Aider rule files all lead with the same words. That changes the bodies those files render,
so, as `IntegrationInstructions.ReleasedCursorRuleHashes` and `ReleasedMarkdownRuleHashes` require, the hash of the body
released since 0.10.0 is appended to each list before the body changes, and `ReleasedOwnedFileHashesTests` re-pinned.
Per the tags, the body changed in 0.11.0 and is identical in 0.12.0, so each list gains one entry (`0.11.0–0.12.0`),
taken from the files those packages write.

### The skill

Today the skill is how a main agent learns the rules, and only where nothing else mentions dtk: in the November
transcripts, main sessions in projects whose CLAUDE.md does not mention dtk invoked it in every session that used dtk
(16 of 16), the others almost never (1 of 21), and subagents never (0 of 159). Each invocation loads about 750
tokens, and its description sits in the skill listing of every session. Once the rules are injected, that eager
invocation repeats what the agent already has.

So the skill (`SharedInstructionArtifacts.SkillMarkdown`, which Claude Code and the `.agents/skills` harnesses share)
becomes the reference for what the rules leave out:

- **Description** (the trigger): no longer "instead of raw `dotnet` … when running build, test, …", but dtk's options
  beyond plain use — keeping a run's raw output (`--tee`), a failing test's console output (`--show-output`), the
  tests that passed (`--names`), failing a run that matched no test (`--fail-on-empty`), capping output
  (`--max-lines`), a run's saved raw output (`dtk log`), filtering saved output (`dtk pipe`), every warning of a build
  rather than those of changed files, and token savings (`dtk gain`).
- **Body**: drops what the rules say (and the installation section: an agent reading it has dtk), keeps the options,
  `dtk log`/`dtk pipe`, passthrough and raw output, the key behaviours and `dtk gain`, and gains one line on the
  changed-files narrowing (`N warnings in unchanged files not shown`, how to list them all, when every warning is
  listed).

The AGENTS.md section keeps the rules and the usage notes it has, so a harness that reads it loses nothing.

### Docs

`docs/articles/ai-agent-setup.md` (what `dtk init claude` now registers and the exact text it injects, and that
re-running `dtk init claude` adds it to an existing install), `docs/articles/usage.md` if it lists what init writes,
CHANGELOG, and CLAUDE.md's Claude Code paragraph.

## Verification

- Unit tests for the dispatch (each event, .NET and not, the skipped agent types, malformed payloads), the project
  detection (ancestors, two levels down, skipped folders, the budget), registration, uninstall and doctor.
- `UninstallIntegrationTests` keeps round-tripping Claude Code in both scopes with the three registrations.
- Timing: `dtk hook claude` answering `SessionStart` in this repository and in a large non-.NET tree, against the
  existing `dtk hook` figures in docs/articles/performance.md.
- **Gate (real Claude Code, run in the foreground), first, as a go/no-go:** before doctor, docs and the shared text,
  the dispatch and registration alone are tried against the real Claude Code. A scratch .NET project with a
  project-scope `dtk init claude` and the branch's dtk first on `PATH`; `claude -p` asked to report what its context
  says about dtk, then to spawn a general-purpose and an Explore subagent that do the same. Pass: all three quote the
  rules. Then `claude -p` in a scratch non-.NET folder: none does. Then `--resume` the first session and ask again,
  which settles the `resume` matcher. If `SubagentStart`'s context does not reach the subagents, the work stops there
  and goes back to the user, since subagents are the reason for it. Results go in the PR.

## Left to the builder

The rule wording within its four lines; how doctor tells the three registrations apart; the entry budget of the .NET
walk; test layout; the exact JSON shape Claude Code accepts if the docs and the gate disagree.

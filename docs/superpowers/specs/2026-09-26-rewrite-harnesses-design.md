# Rewrite-capable harnesses (Cursor, Devin, Factory Droid, Crush, Kilo Code, Amp) — design

Date: 2026-09-26. Status: approved; PR 1 implemented.

## Goal

Extend `dtk init` and `dtk hook` to the widely used coding harnesses whose pre-tool mechanism can **rewrite** the
agent's shell command, so `dotnet build …` becomes `dtk dotnet build …` without the agent's cooperation. Two of
them (Cursor, Windsurf) are already listed by `dtk init` but only get a rule file today; they are upgraded first.

Guiding decision: enforced rewrites first. Harnesses whose hooks can only block, or that have no pre-tool
mechanism at all, are out of scope (see *Out of scope*).

## Scope

In scope, delivered as three independent PRs off `develop`, merged in order (the repository squash-merges, so
they are not stacked):

| PR | Providers | Mechanism |
|----|-----------|-----------|
| 1 | `cursor` (extended), `devin` (new; `windsurf` becomes its alias) | JSON command hooks |
| 2 | `droid`, `crush` | Claude-style command hook; `crushrc` script section |
| 3 | `kilo`, `amp` | Generated JS plugins over `PluginRuntime` |

Each provider gets project and `--global` installs, `--uninstall`, `doctor` support, docs and completion.
PR 1 also lands the shared reply helper and the provider alias table.

Out of scope:

- Block-only harnesses: Kiro (exit 2 + stderr), Auggie (deny only; script-file hooks), Qwen Code (documents
  `updatedInput` but its scheduler ignores it at `f3bcadb`), Cline's VS Code extension, Goose (its denial text
  says "Do not retry"), OpenHands, Kimi Code, Windsurf's legacy Cascade agent. A "deny and guide" reply kind is a
  separate future design.
- Instruction-only harnesses: Zed, Warp. Dead projects: Roo Code (shut down 2026-05-15), Continue (end of life).
- Rewrite-capable but deferred: Trae, Qoder CLI, Hermes Agent, OpenClaw, Mistral Vibe, Cline CLI (`overrideInput`
  is undocumented).
- Any change to *which* commands are rewritten. `DotnetCommandRewriter` is reused unchanged.

## Research summary (2026-09-26)

From each harness's official docs, changelogs and, where open, source. "Unverified" marks what no primary source
or live run confirmed; those points go on the manual checklist (see *Verification*).

### Cursor (IDE and `cursor-agent`)

- Hooks since Cursor 1.7 (2025-09-29); no longer labelled beta. `cursor-agent` shares them
  (cursor.com/docs/hooks.md, cursor.com/docs/cli/changelog.md).
- Files: project `.cursor/hooks.json` (trusted workspaces only), user `~/.cursor/hooks.json`, enterprise paths.
  All levels run; `deny` beats `ask` beats `allow`. Schema `{"version":1,"hooks":{"<event>":[{"command",
  "matcher","timeout","failClosed"}]}}`, timeout in seconds.
- `beforeShellExecution` can only allow/deny/ask. `preToolUse` can rewrite: stdin
  `{"tool_name":"Shell","tool_input":{"command","working_directory"},…}`, reply
  `{"permission":"allow"|"deny","user_message","agent_message","updated_input":{…}}`. `ask` is accepted but not
  enforced for `preToolUse`. The matcher is tested against the tool name.
- Exit 0 uses the reply; exit 2 denies; other failures fail open. But for permission hooks, "invalid JSON or a
  response that doesn't match the hook's schema blocks the action" — the hook must always print valid JSON.
- Cursor also imports Claude Code hooks from `~/.claude/settings.json` and `.claude/settings*.json` (on by
  default; `Bash` maps to `Shell`, `updatedInput` to `updated_input`).
- rtk ships a `Shell` `preToolUse` rewrite hook in `~/.cursor/hooks.json` returning `permission: "allow"`.
- Known rewrite gaps: empty stdin on remote Linux workspaces (forum 164951); rewrites possibly dropped for
  subagent `Shell` calls (rtk#2786). Windows: the hook command is composed as PowerShell but run through bash
  when Git Bash markers are present (forum 168129); a bare `dtk hook cursor` parses the same under both.
- Rules: `.cursor/rules/*.mdc`, `AGENTS.md`. User rules live in the settings UI, not a file.
- **Unverified:** whether `permission: "allow"` also skips Cursor's own approval prompt; whether `{}` is accepted
  as a neutral reply (rtk relies on it).

### Devin Desktop (formerly Windsurf) and Devin CLI

- Windsurf was renamed Devin Desktop on 2026-06-02; docs moved to docs.devin.ai. The default agent is now Devin
  Local, which shares the Devin CLI's hooks. The legacy Cascade agent's `pre_run_command` can only block.
- Devin Local / CLI hooks use Claude Code's format (docs.devin.ai/cli/extensibility/hooks/overview):
  project `.devin/hooks.v1.json` (the whole file is the hooks object), `.devin/config.json`,
  `.devin/config.local.json`; user `~/.config/devin/config.json` (`%APPDATA%\devin\config.json` on Windows),
  under a `hooks` key. It also imports `.claude/settings*.json` and `~/.claude/settings*.json` by default.
- `PreToolUse` stdin `{"hook_event_name","tool_name":"exec","tool_input":{"command","shell_id"},…}`; reply
  `hookSpecificOutput.updatedInput`, "merged into the tool call before it runs". Exit 2 blocks; other exits are
  logged, not blocking.
- Rules: `.devin/rules/*.md` (`.windsurf/rules/*.md` and `.windsurfrules` are fallbacks), root `AGENTS.md`,
  global `~/.codeium/windsurf/memories/global_rules.md` (always on, 6,000 characters).
- **Unverified:** whether matcher `exec` is required (or `Bash` aliases to it); the shell used on Windows;
  whether Restricted Mode disables project hooks (the Desktop changelog of 2026-07-29 suggests so).

### Factory Droid

- Hooks mature (hook fixes from v0.152.0; current v0.228.0). docs.factory.ai/reference/hooks-reference.
- Files: project `.factory/hooks.json`, user `~/.factory/hooks.json` (event names are the top-level keys).
  **If `hooks.json` is absent, Droid reads the `hooks` key of the matching `settings.json` instead.**
- `PreToolUse`, shell tool `Execute`, stdin `tool_input.command`; reply is Claude's `hookSpecificOutput`
  (`permissionDecision`, `updatedInput`, `additionalContext`). rtk returns the full `tool_input` with only
  `command` replaced and no decision, "verified on Droid v0.140–0.164".
- Default timeout 60 s; exit 2 blocks; other failures continue (fail open). No trust step; hooks are snapshotted
  at startup.
- Instructions: `AGENTS.md` (also under `.factory/`, `.agents/`), personal `~/.factory/AGENTS.md`; skills in
  `.factory/skills`, `.agents/skills` and their home equivalents.
- **Unverified:** the shell on Windows; timeout and invalid-JSON behaviour.

### Crush

- `PreToolUse` hooks since v0.63.0 (2026-04-27), still called "preliminary"; current v0.96.1. Config moved to
  `crushrc`, a Bash script (`crush.json` is deprecated, "won't be receiving new features"). `hook add`/`hook
  remove --name` since v0.88.0.
- Files: project `./.crushrc` or `./crushrc`; global `$CRUSH_GLOBAL_CONFIG`, else `$XDG_CONFIG_HOME/crush/`,
  else `~/.config/crush/`. All are merged; project wins.
- Stdin `{"event":"PreToolUse","session_id","cwd","tool_name":"bash","tool_input":{"command"}}` (no
  `hook_event_name`). Native reply `{"version":1,"decision","halt","reason","context","updated_input"}`;
  `updated_input` is a shallow-merge patch; `"allow"` "bypasses the permission prompt entirely", and the docs say
  to omit the decision when only rewriting. Claude's `hookSpecificOutput` is also parsed.
- Hooks run in Crush's embedded POSIX shell (`mvdan.cc/sh`), identically on Windows (v0.67.0+). Timeout 30 s;
  anything but exit 0/2/49 is "no opinion" (fail open). Hooks fire only for top-level agent tool calls.
- Instructions: `AGENTS.md`, `CRUSH.md`, …; global `~/.config/crush/CRUSH.md`, `~/.config/AGENTS.md`. Skills:
  `.agents/skills`, `~/.agents/skills`, and others.
- **Unverified:** the Windows global path (README says `%USERPROFILE%\.config\crush`, config doc says
  `%XDG_CONFIG_HOME%\crush`); the first version that reads `crushrc` (to be pinned by gate C).

### Kilo Code (v7, VS Code extension and `kilo` CLI)

- An OpenCode fork: JS/TS plugins, "behavior is identical to OpenCode" (kilo.ai/docs/automate/extending/plugins).
  Directories `~/.config/kilo/plugin/`, `.kilo/plugin/` (legacy `.kilocode/plugin/`); a plugin must
  `export default { id, server }`.
- `tool.execute.before(input:{tool,…}, output:{args})`; shell tool `bash`, `args.command`; mutating `output.args`
  rewrites; a throw blocks the tool. The bash permission prompt runs inside `execute`, after the hook.
- Instructions: `AGENTS.md` (walking up), global `~/.config/kilo/AGENTS.md` or `$KILO_CONFIG_DIR/AGENTS.md`.
  `KILO_PURE=1` disables external plugins.
- **Unverified:** the Windows global path; whether Kilo reads `.agents/skills` (gate K decides).

### Amp

- Plugin API released with the "Neo" CLI on 2026-05-06; permissions are now a plugin and Amp no longer prompts
  before tools (ampcode.com/news/neo, ampcode.com/docs/markdown/customize/plugins).
- Directories `.amp/plugins/`, `$XDG_CONFIG_HOME/amp/plugins/` or `~/.config/amp/plugins/`
  (`%USERPROFILE%\.config\amp\plugins\` on Windows). Plugins run under Bun.
- `amp.on('tool.call', (event, ctx) => ToolCallResult)`; results `allow`, `reject-and-continue`,
  `modify {input}`, `synthesize`, `error`. `amp.helpers.shellCommandFromToolCall(event)` returns `{command, dir?}`
  for `Bash` or `shell_command` calls. Handler order between plugins is undefined.
- Instructions: `AGENTS.md` walking up to `$HOME`, global `~/.config/amp/AGENTS.md`; skills include
  `.agents/skills` and `~/.agents/skills`.
- **Unverified:** the input field name (`cmd` on `Bash` in 2025 docs); what a handler returning nothing or
  throwing does; project plugins apparently load without a prompt.

## Design

### Shared: `HookPayloads` and the reply rules

- New `HookPayloadKind` values: `Cursor`, `Devin`, `FactoryDroid`, `Crush`, `Kilo`, `Amp`. `TryGetKind` maps
  `cursor`, `devin`, `droid`, `crush`, `kilo`, `amp`.
- `ReplyToClaude`'s body becomes `ReplyWithUpdatedInput(JsonNode? root, string? expectedTool)`: when
  `expectedTool` is set and the payload names another tool (`NamesAnotherTool` on `tool_name`), no rewrite.
  Claude passes `null`, keeping its reply byte for byte. Devin passes `exec`, Droid `Execute`. The reply carries
  no decision, so the harness's own approval applies to the rewritten command.
- `ReplyToCursor`: rewrite only when `TryRewrite` succeeds **and** `DotnetCommandRewriter.IsAutoApprovable`
  holds (the Copilot CLI rule), replying `{"permission":"allow","updated_input":<tool_input clone with command
  replaced>}`. Every other outcome — another tool, nothing to rewrite, a complex command, malformed input, a
  duplicate key — replies `{}`, never nothing, because Cursor blocks on output that doesn't match its schema.
  `Reply`'s exception path returns `{}` for Cursor as it returns the allow reply for Gemini.
- `ReplyToCrush`: `{"version":1,"updated_input":{"command":<rewritten>}}` when `tool_name` is `bash` (or absent,
  for doctor's probe) and a rewrite applies; otherwise nothing. Never a `decision`.
- `Kilo` and `Amp` reuse `ReplyToOpenCode`: dtk owns both ends of that `{command}` contract.

### PR 1a: Cursor

| Scope | Artifacts |
|-------|-----------|
| Project | `.cursor/rules/dtk.mdc` (unchanged) and `.cursor/hooks.json` |
| Global (new) | `~/.cursor/hooks.json`; no rule file (user rules are UI-only — the install says so) |

The hook entry is `{"command":"dtk hook cursor","matcher":"Shell","timeout":10}` under `hooks.preToolUse`,
merged, never overwritten; `version: 1` is added when absent and other hooks are kept. dtk finds its own entry by
exact command, as for Codex. Uninstall removes that entry and deletes the file when only
`{"version":1,"hooks":{}}` (or an empty `preToolUse`) remains.

`CursorIntegrator` gains `IGlobalIntegrator` and `IHookIntegrator`. `RtkHookCoexistence` additionally scans
`~/.cursor/hooks.json` (and the project's) for an rtk hook and applies the existing reconciliation: exclude
`dotnet` in rtk's `config.toml`. The install prints a note when dtk's Claude hook is also registered in a Claude
settings file Cursor imports (`ImportedClaudeHook`); a double rewrite is harmless because the rewriter skips
commands `dtk` already runs. `doctor` probes Cursor with one simple command per auto-approvable subcommand.

Init output notes: project hooks run only in trusted workspaces; `cursor-agent` needs `--trust` headless;
rewrites do not happen on remote Linux workspaces or, possibly, in subagents.

### PR 1b: Devin, and the `windsurf` alias

| Scope | Artifacts |
|-------|-----------|
| Project | `.devin/rules/dtk.md` (the Windsurf rule text) and `.devin/hooks.v1.json` |
| Global | `hooks` key of `~/.config/devin/config.json` (`%APPDATA%\devin\config.json` on Windows) and a `<!-- dtk -->` section in `~/.codeium/windsurf/memories/global_rules.md` |

Hook entry: `{"PreToolUse":[{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin",
"timeout":10}]}]}`, merged. `WindsurfIntegrator` is renamed `DevinIntegrator` (provider `devin`).

Alias: `IntegrateUseCase` gets an alias table (`windsurf` → `devin`) consulted by `RunAsync`, `RunGlobalAsync`
and `UninstallAsync`; the CLI prints "Windsurf is now Devin Desktop; installed the `devin` integration."
`AvailableProviders` lists canonical names only; the unknown-provider error mentions the alias.

Migration: install and uninstall both remove `.windsurf/rules/dtk.md` when its hash is in
`ReleasedMarkdownRuleHashes` (Devin would otherwise load it as a fallback too); an edited copy is kept with a
note. The Cascade agent gets no hook.

### PR 2a: Factory Droid

| Scope | Artifacts |
|-------|-----------|
| Project | `AGENTS.md` section, `.agents/skills/dotnet-token-killer/SKILL.md`, hook registration |
| Global | `~/.factory/AGENTS.md` section, `~/.agents/skills/…`, hook registration |

Registration file: `.factory/hooks.json` (or `~/.factory/hooks.json`), **unless** it is absent and the matching
`settings.json` already has a `hooks` key — then dtk merges into that key, because creating `hooks.json` would
silently disable the user's settings hooks. `DescribeHooks`, doctor and uninstall resolve the file the same way.
Entry: `{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"dtk hook droid",
"timeout":10}]}]}`.

### PR 2b: Crush

| Scope | Artifacts |
|-------|-----------|
| Project | `AGENTS.md` section, `.agents/skills/…`, marked section in `./.crushrc` |
| Global | marked section in `$CRUSH_GLOBAL_CONFIG`, else `$XDG_CONFIG_HOME/crush/crushrc`, else `~/.config/crush/crushrc` (Windows: `%USERPROFILE%\.config\crush\crushrc`, unverified); `~/.config/crush/CRUSH.md` section; `~/.agents/skills/…` |

```sh
# >>> dtk (DotnetTokenKiller) >>>
hook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'
# <<< dtk <<<
```

dtk edits only between its markers. A missing end marker or a duplicated section makes install and uninstall
refuse, naming the file and what to fix, rather than guess. A new file is created; an existing file keeps its
mode and line endings. If the project uses `./crushrc` (no dot) and no `./.crushrc`, dtk writes there instead.
The minimum Crush version is the first that loads `crushrc` with `hook add`, pinned by gate C and stated in the
docs.

### PR 3a: Kilo Code

| Scope | Artifacts |
|-------|-----------|
| Project | `.kilo/plugin/dtk.js`, `AGENTS.md` section, `.agents/skills/…` (dropped if gate K shows Kilo ignores it) |
| Global | `$KILO_CONFIG_DIR` or `~/.config/kilo/`: `plugin/dtk.js` and `AGENTS.md` section; `~/.agents/skills/…` |

`KiloPlugin.Body` is OpenCode's handler (`tool.execute.before`, `tool === "bash"`, `includes("dotnet")`
pre-filter, in-place `output.args.command`, every path resolving to no rewrite) wrapped as
`export default { id: "dtk", server: async () => ({ … }) }`, over `PluginRuntime.Source("kilo", "Kilo Code")`.
Stamped with `StampStyle.SlashComment`; never written to `.kilocode/`.

### PR 3b: Amp

| Scope | Artifacts |
|-------|-----------|
| Project | `.amp/plugins/dtk.js`, `AGENTS.md` section, `.agents/skills/…` |
| Global | `$XDG_CONFIG_HOME/amp/` or `~/.config/amp/`: `plugins/dtk.js` and `AGENTS.md` section; `~/.agents/skills/…` |

`AmpPlugin.Body` registers `amp.on('tool.call', …)`: it gets the command from
`amp.helpers.shellCommandFromToolCall(event)`, skips anything without `dotnet`, spawns `dtk hook amp`, and on a
rewrite returns `{action: "modify", input: {...event.input, [field]: rewritten}}`, where `field` is whichever of
`cmd` or `command` holds the original string (neither → no rewrite). Otherwise it returns `undefined`, so dtk
never overrides another plugin's decision; if Amp requires a result, the fallback is `{action: "allow"}`. The
handler never throws. Init output notes that Amp loads project plugins without asking.

### doctor, uninstall and shared files

Every new integrator implements `IHookIntegrator` and `IUninstallIntegrator`, so doctor's hook probe,
`SharedArtifactsInUse` (an `AGENTS.md`/skill stays while another provider's dtk hook uses it) and
`UninstallIntegrationTests` cover them without special cases. `CliConfigurator` gets an example per provider.

## Verification

### Automated tests (every PR)

- `HookPayloadsTests`: per-kind fixtures from the documented payloads, each citing its source and date — rewrite,
  other tool, nothing to rewrite, complex command (Cursor → `{}`), BOM, duplicate key, malformed JSON.
- Integrator tests per provider: empty tree, merge into existing config, `--force`, global paths from
  environment variables (`XDG_CONFIG_HOME`, `CRUSH_GLOBAL_CONFIG`, `KILO_CONFIG_DIR`, `APPDATA`); Droid's
  `settings.json` versus `hooks.json` choice; Crush's missing/duplicated markers and `crushrc` fallback; Devin's
  Windsurf rule migration (released and edited).
- `UninstallIntegrationTests` round-trips all six providers in both scopes; released-hash pins for the Devin rule
  and both plugins.
- `KiloPluginTests` and `AmpPluginTests` run the generated JS under Node (Amp against a fake `amp` object), gated by
  `DTK_NODE_REQUIRED` in CI like `OpenCodePluginTests`.

### Live gates (on demand, results recorded in each PR)

No harness account or model key is available on the development machine, so live gates use a mock
OpenAI-compatible server (`eng/gates/mock-openai`) that answers the first request with one scripted `bash` tool
call, `dotnet build`, and the next with a final message. A fake `dotnet` and `dtk` on `PATH` record what ran.

- **Gate C (Crush, PR 2):** the released `crush` binary in Docker, configured with a custom provider at the mock.
  Passes when the recorded command is `dtk dotnet build`, with dtk's `.crushrc` section as the only hook. Repeated
  on older releases to pin the minimum version.
- **Gate K (Kilo, PR 3):** the `kilo` CLI in Docker with the mock provider. Passes when the export shape loads, the
  recorded command is `dtk dotnet build`, and records whether `.agents/skills` is read.

### Manual checklist (docs page, for anyone with an account)

Cursor: `{}` accepted as neutral; `permission: "allow"` bypasses approval or not (if not, loosen the Q1 rule in a
follow-up); rewrite lands in `cursor-agent` and the IDE; interaction with an imported Claude hook. Devin: matcher
`exec`; Windows shell; Restricted Mode. Droid: rewrite on the current release; Windows shell. Amp: `cmd` field;
`undefined` return; spawn under Bun. Until checked, the docs label these four providers "not verified against a
live run".

## Documentation

Each PR updates the README provider table, the integration docs page, CLAUDE.md's integration paragraphs and the
completion scripts for its providers.

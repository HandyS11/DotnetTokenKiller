# Codex CLI, OpenCode and Antigravity CLI integrations — design

Date: 2026-09-15. Status: awaiting review.

## Goal

Add three harnesses to `dtk init` and `dtk hook`: OpenAI's Codex CLI, OpenCode, and Google's
Antigravity CLI (`agy`). Each gets the same treatment the three current hook providers have — a
rewrite hook that turns `dotnet build …` into `dtk dotnet build …` before the command runs, plus
instructions the agent reads — so a user of any of them gets dtk's savings without retyping commands.

This is sub-project 2 of the work the native-hook spec deferred
(`2026-09-14-native-hook-and-init-design.md`, *Scope*), which named Codex CLI and OpenCode. Antigravity
CLI did not exist when that spec was written; it has since replaced Gemini CLI for consumer users.

## Scope

In scope:

1. Three providers — `codex`, `antigravity`, `opencode` — with project and `--global` installs.
2. `dtk hook codex|antigravity|opencode`: three payload handlers over the existing rewriter.
3. A generated OpenCode plugin, the first artifact dtk writes that is executable JavaScript.
4. A shared `AGENTS.md` section and a shared `.agents/skills/` skill, both read by all three.
5. `doctor` support for all three, including Codex's approval state.
6. rtk coexistence extended to the three new harnesses' config files.
7. Docs, completion scripts, and the version floors each harness needs.

Out of scope:

- **OpenCode v2** (beta; a different plugin API with no tool hook in its public context yet). A
  follow-up once v2 is stable.
- Any change to *which* commands are rewritten. `DotnetCommandRewriter` is reused unchanged.
- The other harnesses the native-hook spec listed (Cursor `preToolUse`, Factory Droid, Crush, VS Code
  Copilot agent hooks, Junie CLI, Kilo Code, Amp).
- Retiring the `gemini` provider. Gemini CLI still serves enterprise licences.

## Research summary (2026-09-15)

Checked against Codex `rust-v0.154.0` and `main` at `18d7ace`; OpenCode v1.18.31 (dev `e03db9b`),
including an end-to-end run against a local fake model; Antigravity CLI `agy` 1.2.2, whose embedded
documentation was read out of the release binary; and rtk v0.49.0 with `develop` at `5e0f92c`.

| Harness | Extension point | Reads | Rewrite contract | Hook fails |
|---|---|---|---|---|
| Codex CLI | `PreToolUse` command hook, matcher `Bash` | `tool_input.command` (string) | `hookSpecificOutput.updatedInput.command`, which requires `permissionDecision: "allow"` | ignored; the original command runs |
| Antigravity CLI | `PreToolUse` command hook, matcher `run_command` | `toolCall.args.CommandLine` | `overwrite.CommandLine`, a shallow merge into the tool arguments | **blocks the tool call** |
| OpenCode | a JS/TS plugin's `tool.execute.before` | `output.args.command` when `input.tool === "bash"` | mutate `output.args.command` in place | a throw blocks that tool call |

Sources: [Codex hooks](https://learn.chatgpt.com/docs/hooks) and `codex-rs/hooks/src/`
(`engine/discovery.rs`, `engine/output_parser.rs`, `events/pre_tool_use.rs`, `schema.rs`);
[OpenCode plugins](https://opencode.ai/docs/plugins/) and `packages/opencode/src/session/tools.ts`,
`packages/plugin/src/index.ts`, `packages/opencode/src/config/plugin.ts`;
[Antigravity hooks](https://antigravity.google/docs/hooks/) plus the `agy` binary's embedded hook
reference, which documents `overwrite` and the `decision` values the website omits;
[Antigravity CLI install](https://antigravity.google/docs/cli/install/);
[Gemini CLI transition](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/).

Three findings shape the design:

- **Codex requires the user to approve a hook before it runs.** Approval is a `trusted_hash` in
  `config.toml`, keyed by `<hooks.json path>:pre_tool_use:<group>:<handler>` and computed over a
  normalized form of the hook (event, matcher, command, timeout). The interactive UI prompts at
  startup and offers `/hooks`; `codex exec` silently skips unapproved hooks. Project `.codex/`
  configuration is read only in projects the user has trusted. dtk does not write that hash: the
  hashing is internal and undocumented, and self-approving would sidestep a review the user is meant
  to perform.
- **Antigravity's `decision` field is required and grants permission.** Per the binary's own
  reference: `"allow"` — "Automatically allow the tool execution"; `"ask"` — "Prompt the user for
  permission (respects 'Always Allow' cache)"; `"force_ask"`, `"deny"`, `"deny_unless_prior_grant"`.
  rtk's open Antigravity PR replies `{"decision":"allow"}` on every call, which auto-approves every
  shell command the agent runs. dtk never replies `allow`: `dotnet build` and `dotnet test` execute
  arbitrary MSBuild targets, so auto-approving them is auto-approving code execution.
- **OpenCode has no command hooks.** The `experimental.hook` entries of v1.0.0 (`file_edited`,
  `session_completed`) are gone from the current config schema and could not rewrite a tool call
  anyway. A plugin is the only way in. `session/tools.ts` passes the same `args` object to the hook
  and then to the tool, so mutating `output.args.command` in place propagates — replacing
  `output.args` wholesale does not.

Known harness bugs recorded here because they affect what users will see, not because dtk can fix
them: Antigravity discards an `overwrite` when a command needs review
([#575](https://github.com/google-antigravity/antigravity-cli/issues/575)), and hooks have been
reported not firing at all on 1.2.1/1.2.2 on macOS
([#1008](https://github.com/google-antigravity/antigravity-cli/issues/1008)); OpenCode checks
permission rules against the *rewritten* command
([#35882](https://github.com/anomalyco/opencode/issues/35882)), as it does for rtk today.

## 1. What `dtk init` writes

Three new providers, each implementing `IProviderIntegrator`, `IGlobalIntegrator` and
`IHookIntegrator`, registered in `DependencyInjection` beside the existing eight.

| Provider | Project scope | `--global` scope |
|---|---|---|
| `codex` | `AGENTS.md` section; `.agents/skills/dotnet-token-killer/SKILL.md`; `.codex/hooks.json` | `$CODEX_HOME/AGENTS.md` (default `~/.codex`); `~/.agents/skills/dotnet-token-killer/SKILL.md`; `$CODEX_HOME/hooks.json` |
| `antigravity` | `AGENTS.md` section; `.agents/skills/dotnet-token-killer/SKILL.md`; `.agents/hooks.json` | `~/.gemini/GEMINI.md` section; the global skill directory gate G5 settles; `~/.gemini/config/hooks.json` |
| `opencode` | `AGENTS.md` section; `.agents/skills/dotnet-token-killer/SKILL.md`; `.opencode/plugins/dtk.js` | `$XDG_CONFIG_HOME/opencode/AGENTS.md` (default `~/.config/opencode`); `~/.agents/skills/dotnet-token-killer/SKILL.md`; `$XDG_CONFIG_HOME/opencode/plugins/dtk.js` |

`HomePaths` gains `CodexDir` (honoring `$CODEX_HOME`), `OpenCodeConfigDir` (honoring
`$XDG_CONFIG_HOME`), `AgentsSkillsDir` (`~/.agents/skills`) and `AntigravityHooksPath`
(`~/.gemini/config/hooks.json`). `IntegrateUseCase` and `InitCommand` are unchanged.

### Shared instruction artifacts

All three harnesses read `AGENTS.md` and `.agents/skills/`, so both artifacts are shared rather than
per-provider:

- **`AGENTS.md` section.** Section-merged between `<!-- dtk -->` and `<!-- /dtk -->` by the existing
  `WriteSectionBasedFileAsync`. Its body is the *same constant* the Gemini integrator writes into
  `GEMINI.md` today — `GeminiCliIntegrator.GeminiSection` is renamed and moved to
  `IntegrationInstructions` as `SharedSection`, with both integrators referring to it. Two
  consequences that tests pin: running `dtk init codex` and then `dtk init opencode` in one repository
  leaves exactly one section, reported `unchanged` the second time; and `dtk init antigravity --global`
  after `dtk init gemini --global` leaves `~/.gemini/GEMINI.md` byte-identical.
- **The skill.** `ClaudeCodeIntegrator.SkillMarkdown`, likewise moved to a shared home, written to
  `.agents/skills/dotnet-token-killer/SKILL.md` through `WriteGeneratedFileAsync` with the existing
  stamp and legacy signature. The name `dotnet-token-killer` satisfies OpenCode's
  `^[a-z0-9]+(-[a-z0-9]+)*$` rule and Codex's 64-character limit, and its frontmatter carries the
  `description` all three require.
- Neither artifact mentions any harness's hook, because all three share them.
- `WriteSectionBasedFileAsync` today reports an existing file `skipped` (or `updated` under `--force`) even when its
  dtk section is already current, which would advise a pointless `--force` on every shared `AGENTS.md`. It now reports
  such a file `unchanged` and writes nothing, force or not. This also changes the second-run report of the existing
  `gemini`, `copilot-cli`, `jetbrains` and possibly `aider` providers, whose tests are updated to match. An existing
  `AGENTS.md` without the dtk section still gets it only with `--force`, as `GEMINI.md` does today.
- OpenCode also reads `.claude/skills/`, so a repository holding both Claude's and the shared skill
  makes OpenCode log `duplicate skill name` and keep one of two byte-identical files
  (`packages/opencode/src/skill/index.ts`). dtk does not special-case it.

### Registrations

**Codex** (`.codex/hooks.json`, or `$CODEX_HOME/hooks.json`) merges through the existing
`MergeJsonSettingsAsync` with no change, because Codex's schema is Claude's:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [{ "type": "command", "command": "dtk hook codex", "timeout": 10 }]
      }
    ]
  }
}
```

`HookRegistrationSpec` gains an optional timeout, which `MergeJsonSettingsAsync` writes into the
handler object; the existing three providers pass none and their registrations stay byte-identical.

`Bash` is the tool name hooks see, whatever shell tool the model actually called
(`codex-rs/core/src/tools/hook_names.rs`). The command is unguarded: Codex ignores a failed hook and
runs the original command, so a missing `dtk` surfaces as a visible error rather than silence — the
same reasoning as Claude Code's registration. **The command string must never change once shipped**:
it is part of the approval hash, so changing it silently revokes every user's approval.

`init codex` prints two notes:

- "Codex runs this hook only after you approve it: open Codex and review it under `/hooks`. `codex
  exec` skips unapproved hooks silently. Requires Codex 0.131 or later."
- For a project install: "Codex reads `.codex/` only in projects you have trusted."

**Antigravity** (`.agents/hooks.json`, or `~/.gemini/config/hooks.json`) uses the same merge helper
against a different parent key: dtk owns the top-level group `"dtk"`, and `MergeJsonSettingsAsync`
gains a container-key parameter (today hardcoded to `hooks`) so the group's `PreToolUse` array merges
the same way, leaving any other group in the file untouched.

```json
{
  "dtk": {
    "PreToolUse": [
      {
        "matcher": "run_command",
        "hooks": [{ "type": "command", "command": "dtk hook antigravity || exit 0", "timeout": 10 }]
      }
    ]
  }
}
```

The guard is section 4's subject. `init antigravity` notes that a project `.agents/hooks.json` loads
only in workspaces trusted in Antigravity.

**OpenCode** gets a generated, stamped plugin file (section 3) rather than a registration, written
through `WriteGeneratedFileAsync`: refreshed when dtk wrote the installed copy, left alone with a note
when the user edited it, replaced under `--force`.

## 2. `dtk hook codex|antigravity|opencode`

Handled by the existing `HookEntryPoint`: no service container, no Spectre, no tracking or tokenizer;
stdin read as raw bytes with a leading BOM tolerated; stdout written as UTF-8 bytes; **always exit 0**;
any exception means "no rewrite". Each provider adds a `HookPayloadKind`, an arm in
`HookPayloads.TryGetKind`, a reply handler, and a probe payload in `HookHealthChecker.BuildPayload`.
The usage line in `HookEntryPoint` lists the six providers.

| Provider | Reads | Prints on rewrite | Prints otherwise |
|---|---|---|---|
| `codex` | `tool_input.command` | `{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow","updatedInput":<tool_input with command replaced>}}` | nothing |
| `antigravity` | `toolCall.name == "run_command"`, `toolCall.args.CommandLine` | `{"decision":"ask","overwrite":{"CommandLine":<rewritten>}}` | gate G1's neutral reply |
| `opencode` | `command` | `{"command":<rewritten>}` | nothing |

- **Codex.** `permissionDecision: "allow"` is mandatory: Codex rejects `updatedInput` without it, and
  rejects `"allow"` without `updatedInput` (`engine/output_parser.rs`). It is not an escalation as
  long as Codex applies its approval policy and sandbox to the rewritten command, which gate C1
  verifies. Only `updatedInput.command` is read; the rest of `tool_input` round-trips for symmetry
  with the Claude handler.
- **Antigravity.** `overwrite` is a shallow merge, so only `CommandLine` is sent. No `reason` is sent:
  `agy` already tells the agent that a hook rewrote the arguments. `decision` is `"ask"` — never
  `"allow"` — so the user's own permission rules and "Always Allow" choices continue to decide, and
  dtk grants nothing the user had not already granted.
- **OpenCode.** The payload is dtk's own contract, because dtk generates the plugin on the other end.
  Only the command crosses the boundary, and both sides change together.
- Every unexpected shape — a different tool name, a missing or empty command, invalid JSON, a
  duplicate JSON key — yields the provider's "otherwise" reply, never an exception and never a
  non-zero exit.

## 3. The OpenCode plugin

`.opencode/plugins/dtk.js`, or `$XDG_CONFIG_HOME/opencode/plugins/dtk.js`. **JavaScript, not
TypeScript**: a `.ts` plugin needs a type import from `@opencode-ai/plugin`, and the Desktop app runs
plugins on Node rather than Bun. Plain JS importing only `node:child_process` loads under both.

```js
import { spawn } from "node:child_process";

const TIMEOUT_MS = 5000;

function rewrite(command) {
  return new Promise((resolve) => {
    let child;
    try {
      child = spawn("dtk", ["hook", "opencode"], {
        stdio: ["pipe", "pipe", "ignore"],
        windowsHide: true,
      });
    } catch {
      resolve(null);
      return;
    }

    const chunks = [];
    const timer = setTimeout(() => {
      child.kill();
      resolve(null);
    }, TIMEOUT_MS);

    child.on("error", () => {
      clearTimeout(timer);
      resolve(null);
    });
    child.stdout.on("data", (chunk) => chunks.push(chunk));
    child.on("close", () => {
      clearTimeout(timer);
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")).command ?? null);
      } catch {
        resolve(null);
      }
    });
    child.stdin.on("error", () => {});
    child.stdin.end(JSON.stringify({ command }));
  });
}

export const DtkPlugin = async () => ({
  "tool.execute.before": async (input, output) => {
    try {
      if (input?.tool !== "bash") return;
      const command = output?.args?.command;
      if (typeof command !== "string" || !command.includes("dotnet")) return;
      const rewritten = await rewrite(command);
      if (typeof rewritten === "string" && rewritten !== "") output.args.command = rewritten;
    } catch {
      // A failed rewrite must never block the tool call.
    }
  },
});
```

Why each part is the way it is:

- **`includes("dotnet")` before spawning.** A superset of everything the rewriter can rewrite, by
  construction, so it needs no test keeping it in step with `DotnetSubcommands`. Non-matching calls
  pay a substring search; only commands mentioning `dotnet` pay a process start. A false positive
  (`ls ~/dotnet-notes`) costs one `dtk` run that replies with nothing.
- **Never throws, never blocks.** Every failure path resolves to `null` and leaves the command alone:
  `dtk` missing from `PATH` (`error`), a wedged child (the 5 s timeout kills it), unparsable output.
  A throw here would fail the tool call with `BLOCKED_BY_PLUGIN`.
- **Mutation in place.** `output.args.command = …` propagates; assigning a new `output.args` does not
  (`session/tools.ts:104-111`).
- **No Bun `$`, no `which`.** `$` is undefined when OpenCode runs on Node (Desktop), and `which` does
  not exist on Windows — both are live bugs in rtk's OpenCode plugin. `spawn` without a shell also
  avoids quoting differences; on Windows libuv's PATH search finds `dtk.exe`, which both the win-x64
  and `any` packages install.
- **One function export**, as OpenCode v1's loader requires: a single non-function export makes the
  whole plugin fail silently.
- **Stamped.** A new `StampStyle.SlashComment` writes the provenance line as `// dtk-generated
  sha256:…`. The legacy signature is `DtkPlugin`, which every generation carries.

Cost, measured once and recorded in `CLAUDE.md` under *Benchmarks*: the added time per shell tool call
with and without `dotnet` in the command, in a real `opencode run` against a fake model. The research
run measured ~0.02 ms for a pure-JS hook and 15–17 ms for a `dtk` process start, so the expected shape
is "microseconds on ordinary commands, one process start on dotnet ones".

## 4. Antigravity's guard, and the verification gates

### The guard

Antigravity blocks the tool call when a hook fails, and runs hook commands through `sh -c` on Unix and
`cmd /c` on Windows. Gemini's `; exit 0` is not valid under `cmd`. **`dtk hook antigravity || exit 0`**
is valid in both: `sh` exits 127 and `cmd` exits 9009 when `dtk` is missing, and `exit 0` then returns
success in either shell. No single command string can print valid JSON under both shells, because they
disagree about quoting — which is why the fallback below changes the registration rather than the
guard.

### Gate G — Antigravity, needs a signed-in `agy`

The `antigravity` PR does not merge until these are recorded in it, with the `agy` version and OS.

| # | Check | Pass | Fail |
|---|---|---|---|
| G1 | Empty stdout with exit 0 leaves the call to the user's normal permissions | neutral reply is nothing; registration keeps `\|\| exit 0` | neutral reply is `{"decision":"ask"}`; registration drops the guard, and `init` notes that a missing `dtk` blocks shell commands |
| G2 | `decision:"ask"` with `overwrite` does not prompt for a command already allowed by `permissions.allow` or "Always Allow", and any prompt shows the rewritten command | ship as designed | **stop; return to the user** — the choice becomes extra prompts versus escalation |
| G3 | The rewrite still applies after the user approves a prompt ([#575](https://github.com/google-antigravity/antigravity-cli/issues/575)) | — | ship; document as a known issue (the command runs unrewritten, costing only the savings) |
| G4 | Hooks fire on the tested version ([#1008](https://github.com/google-antigravity/antigravity-cli/issues/1008)) | — | record the working versions in the docs |
| G5 | Which global skill directory the CLI reads (`~/.gemini/config/skills/`, `~/.gemini/antigravity-cli/skills/` and `~/.gemini/antigravity/skills/` are all documented somewhere) | use it for `--global` | — |
| G6 | Whether a hook group needs `"enabled": true` (rtk's open PR writes it; the embedded reference does not list it) | omit it | write it into the `"dtk"` group |

### Gate C — Codex, local, no account

Codex is installed from npm and pointed at a local fake Responses endpoint, the way the OpenCode
research ran OpenCode against a fake model.

| # | Check | Fail |
|---|---|---|
| C1 | Under `approval_policy = "untrusted"`, a rewritten `dotnet build` still prompts for approval | **stop; return to the user** — `permissionDecision: "allow"` would be an escalation, and Codex offers no other way to rewrite |
| C2 | The hook receives `tool_name: "Bash"` and a string `tool_input.command`, and the rewrite reaches the executed command | fix the handler |
| C3 | After approving in the TUI, `config.toml` holds the `hooks.state` key `doctor` looks for | adjust doctor's check |

## 5. `doctor`

- **Registration formats.** `HookInstallation` gains an optional `PluginArtifact` (the generated file that is the
  registration; `null` for JSON), and `LegacyScriptPath` becomes optional (none of the new providers ever had a
  Python hook).
  - *JSON* (Codex, Antigravity): the existing depth-first search for a string containing
    `dtk hook <provider>` works unchanged, across both schemas.
  - *Plugin* (OpenCode): **registered** when the stamp verifies and the body matches the current
    template; **fail — "stale — run `dtk init opencode`"** when it verifies against an older template;
    **registered, "modified locally"** when the stamp does not verify but the body still runs
    `dtk hook opencode`; **absent** otherwise.
- **Probe.** Unchanged in shape: feed the provider's payload to the `dtk` resolved from `PATH` and
  require `dtk dotnet <sub>` for every subcommand in the reply.
- **Codex approval.** `DiagnosticCheck` gains a third state, a **warning** rendered in yellow that
  does not affect doctor's exit code. Using Tomlyn, already a dependency, doctor reads
  `$CODEX_HOME/config.toml`:
  - no `hooks.state` key starting with `<hooks.json path>:pre_tool_use:` → **warn** "not yet approved
    — review it under `/hooks` in Codex";
  - such a key present → **pass** "approval recorded (dtk cannot tell whether it matches the current
    definition)", because the hash is internal;
  - project install whose directory is not `trusted` under `[projects]` → **warn**.
- **rtk coexistence.** `RtkHookCoexistence.IsRtkHookPresent` also scans `.codex/hooks.json` and
  `$CODEX_HOME/hooks.json`; `.agents/hooks.json`, `~/.gemini/config/hooks.json` and the Antigravity
  plugin directories; and both OpenCode `plugins/` directories — looking for `rtk hook` or
  `rtk rewrite`. All three new integrators call `ReconcileAsync`, as Claude's does. rtk routes every
  hook entry point through one `decide` that honors `exclude_commands` (`src/hooks/decision.rs`), so
  the existing `exclude_commands = ["dotnet"]` reconciliation covers the new harnesses with no new
  rtk-side knowledge.

## 6. Testing

- **Payload handlers**, per provider: rewrite, no rewrite, wrong tool name, missing/empty command,
  invalid JSON, duplicate keys, unexpected shapes, other fields preserved, non-ASCII round-tripping
  byte-exactly.
- **Integrators**: first install, unchanged re-run, merge beside a foreign hook in the same file,
  Antigravity's `"dtk"` group merging without touching other groups, `--force`, and a settings file
  that cannot be parsed. Two cross-provider cases: `init codex` then `init opencode` leaves exactly one
  `AGENTS.md` section (the second reported `unchanged`); `init gemini --global` then
  `init antigravity --global` leaves `~/.gemini/GEMINI.md` byte-identical.
- **Plugin artifact**: stamp verification, refresh of a dtk-written copy, a user-edited copy left with
  a note, `--force`.
- **Plugin execution**: the generated `dtk.js` run under `node` with a fake `dtk` on `PATH` — rewrite
  applied, `dtk` missing, `dtk` hanging (timeout), non-bash tool, malformed reply, and `output.args`
  mutated in place. Skipped when `node` is absent; CI sets the flag that turns the skip into a failure.
- **`HomePaths`**: `$CODEX_HOME` and `$XDG_CONFIG_HOME` honored, with defaults when unset.
- **`doctor`**: every registration state, the plugin states, the Codex approval warning, the untrusted
  project warning, and each probe failure.
- **CLI integration**: `dtk hook codex|antigravity|opencode` as a real process with piped payloads —
  exit 0 on every path, exact stdout, nothing on stdout for a person at a terminal. Runs against
  `DTK_TEST_BINARY` and `DTK_AOT_BINARY` too.
- **Windows** (`eng/aot/test-windows.sh`, `fallback-package.yml`): `cmd /c "dtk hook antigravity || exit 0"`
  fed a payload, asserting the rewrite on stdout, and again with `dtk` removed from `PATH`, asserting
  exit 0; the Codex and OpenCode payloads through Git Bash; and the plugin execution test under Node
  starting `dtk.exe`.
- `SavingsBaselineTests` is unaffected: no filter changes.

## 7. Documentation

- Completion scripts (bash, zsh, fish, PowerShell) list the three new providers.
- `README.md` and `src/DotnetTokenKiller.Cli/README.md` list the supported harnesses.
- `docs/index.md` and `docs/articles/ai-agent-setup.md` gain a section per harness covering what is
  installed, the version floor (Codex 0.131 or later for `updatedInput`; OpenCode 1.x, not the v2
  beta; the `agy` version gate G ran on), Codex's approval step, and Antigravity's workspace trust.
- `CLAUDE.md`'s provider paragraph names the new providers and the files they write.
- Generated examples guarded by `ExamplesBindingTests` are regenerated from real runs, never
  hand-edited.

## 8. Delivery

Three pull requests, merged in order, each branched from `develop` after the previous one merges —
not stacked, because this repository squash-merges only.

1. **`codex`**, carrying the shared work: the `AGENTS.md` section constant, the `.agents` skill, the
   `unchanged` report for current sections, the optional `LegacyScriptPath`, registration timeouts,
   `DiagnosticCheck`'s warning state, file-based rtk reconciliation, and `HomePaths` additions. Gate C runs
   locally and is recorded in the PR.
2. **`opencode`**: the generated plugin, `HookInstallation.PluginArtifact`, `StampStyle.SlashComment`, and the
   plugin execution test.
3. **`antigravity`**: the container-key parameter on `MergeJsonSettingsAsync`, the group merge, and
   the guard. Gate G runs against the user's signed-in `agy` and is recorded in the PR.

## Acceptance

- `dtk init codex|antigravity|opencode`, with and without `--global`, writes exactly the files in
  section 1; a re-run reports everything `unchanged`; combinations with the existing `claude` and
  `gemini` providers leave one shared section and one shared skill.
- `dtk hook codex|antigravity|opencode` rewrites every canonical subcommand, exits 0 on every path,
  and prints nothing for a person at a terminal.
- The OpenCode plugin rewrites a `dotnet` command in a real `opencode run`, and leaves the command
  untouched — without failing the tool call — when `dtk` is missing.
- dtk never grants a permission the user had not granted: Antigravity replies `ask`, never `allow`,
  and gate C1 confirms Codex still applies its approval policy to the rewritten command.
- `doctor` reports all three providers, warns about an unapproved Codex hook, and goes green once the
  hook is approved.
- Gates C and G are recorded in their PRs.
- The build, the full test suite, `dtk dotnet format --verify-no-changes` and the Windows jobs pass.

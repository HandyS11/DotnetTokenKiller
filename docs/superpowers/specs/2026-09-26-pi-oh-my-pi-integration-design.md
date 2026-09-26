# pi and oh-my-pi integrations — design

Date: 2026-09-26. Status: awaiting review.

## Goal

Add two harnesses to `dtk init` and `dtk hook`: the pi coding agent (`pi`) and its fork oh-my-pi (`omp`).
Each gets what the current providers have — a rewrite hook that turns `dotnet build …` into
`dtk dotnet build …` before the agent's bash tool runs it, plus instructions the agent reads — so a user of
either gets dtk's savings without retyping commands.

## Scope

In scope:

1. Two providers — `pi` and `oh-my-pi` — with project and `--global` installs, and `--uninstall`.
2. `dtk hook pi|oh-my-pi`: two payload kinds over the existing rewriter and OpenCode's reply shape.
3. One generated extension, `dtk.js`, written to each harness's extensions directory.
4. The shared `AGENTS.md` section and `.agents/skills/` skill, which both harnesses already read.
5. `doctor` support for both, including a note on pi's project trust.
6. rtk coexistence: rtk ships a pi extension (`rtk init --agent pi`).
7. Docs and completion scripts.

Out of scope:

- `user_bash` (the user's own `!`/`!!` commands). Only the agent's `bash` tool calls are rewritten, as for
  every other provider. On oh-my-pi a `user_bash` handler cannot wrap the command, only replace the whole run.
- oh-my-pi's shell output minimizer (`shellMinimizer.*`). dtk does not edit oh-my-pi's `config.yml`
  (see *oh-my-pi's minimizer*).
- oh-my-pi's `hooks/pre/<tool>.ts` layer. Extensions are the documented, stable route.
- Any change to *which* commands are rewritten. `DotnetCommandRewriter` is reused unchanged.

## Research summary (2026-09-26)

Checked in the published npm sources: `@earendil-works/pi-coding-agent` 0.87.1 (pi moved there from the
deprecated `@mariozechner/pi-coding-agent`, last 0.73.1; binary and directories unchanged) and
`@oh-my-pi/pi-coding-agent` 18.3.2 with `@oh-my-pi/pi-natives-linux-x64` 18.3.2.

### pi

- **Extensions** are JS/TS modules with `export default function (pi: ExtensionAPI)`, loaded through jiti
  from `.pi/extensions/` (project) and `~/.pi/agent/extensions/` (global; the agent directory moves with
  `PI_CODING_AGENT_DIR`). `*.ts`, `*.js`, and `*/index.{ts,js}` are discovered.
- **`tool_call`** passes `{ type, toolName, toolCallId, input }`; for `bash`, `input` is
  `{ command, timeout? }`. `agent-session.js` passes the tool's own `args` object as `input`, and the type's
  doc comment says "To modify arguments, mutate `event.input` in place". `ExtensionRunner.emitToolCall`
  reads only `result.block` from a handler's return value; any other field is ignored.
- **A throw blocks the tool** ("Extension failed, blocking execution").
- **Project trust (0.73+)**: `.pi/extensions`, `.pi/skills`, `.pi/settings.json` and project
  `.agents/skills` load only in a trusted project — `--approve`, `/trust`, `~/.pi/agent/trust.json`, or
  `defaultProjectTrust`. The default is `"ask"`, and print, JSON and RPC modes skip project resources when
  asked. Global extensions need no trust.
- **Context and skills**: `AGENTS.md`/`CLAUDE.md` from `~/.pi/agent/`, every parent directory, and cwd;
  skills from `~/.pi/agent/skills/`, `~/.agents/skills/`, `.pi/skills/`, and `.agents/skills/`.
- **Shell**: `/bin/bash -c <command>` (bash on `PATH`, then `sh`; Git Bash on Windows).

### oh-my-pi

- **Directories**: `.omp/` (project) and `~/.omp/agent/` (global). It does not read `.pi/`. Ambient
  extension discovery uses the native provider only, so it will not load `.opencode/plugins/dtk.js`.
- **Extensions**: same `ExtensionFactory` default export, from `.omp/extensions/` and
  `~/.omp/agent/extensions/`, `*.ts`/`*.js`/`index.{ts,js}` (`extensions/loader.ts`).
- **`tool_call`** passes a *normalized copy* of the input (`wrapper.ts`: `normalizeToolEventInput(…)`), so an
  in-place mutation does not reach the tool. Since 18.2.1 a handler returns
  `{ input: {...} }` and the tool executes with that input (`effectiveParams = callResult.input`); last
  handler wins. The bash input has more fields (`cwd`, `env`, `pty`, …), so the returned input must spread
  the original.
- **A throw or a handler exceeding `extensionHandlers.toolCallTimeoutMs` (default 30 s) blocks the tool.**
- **No project trust gate** found (`isProjectTrusted: () => true` in the runner).
- **Context and skills**: `AGENTS.md`/`CLAUDE.md` walking up from cwd, `.omp/AGENTS.md`,
  `~/.omp/agent/AGENTS.md`; skills from `.omp/skills`, `~/.omp/agent/skills`, `.agents/skills` (walking up
  and in home), `.claude/skills` and others.
- **Shell**: brush-core, a bash-compatible shell in Rust, with a persistent session.
- **Output minimizer**, on by default, dispatches on the program name. Its native filter list includes
  `dotnet` (`crates/pi-shell/src/minimizer/filters/dotnet.rs`: build, test, restore, format) but not `dtk`.

### Consequences

- One extension body serves both harnesses: it mutates `event.input.command` (what pi runs) *and* returns
  `{ input: { ...event.input, command } }` (what oh-my-pi runs). Each harness ignores the other's
  convention, as verified above.
- The extension must never throw and must finish well under 30 s; the existing 5 s spawn timeout does.
- The shared `AGENTS.md` section and `.agents/skills` skill need no harness-specific variant.

## Design

### Providers and paths

| | `pi` | `oh-my-pi` |
|---|---|---|
| Project extension | `.pi/extensions/dtk.js` | `.omp/extensions/dtk.js` |
| Global extension | `<pi agent dir>/extensions/dtk.js` | `~/.omp/agent/extensions/dtk.js` |
| Project instructions | `AGENTS.md` | `AGENTS.md` |
| Global instructions | `<pi agent dir>/AGENTS.md` | `~/.omp/agent/AGENTS.md` |
| Project skill | `.agents/skills/dotnet-token-killer/` | same |
| Global skill | `~/.agents/skills/dotnet-token-killer/` | same |

`<pi agent dir>` is `$PI_CODING_AGENT_DIR` when set, else `~/.pi/agent`. oh-my-pi's profiles
(`~/.omp/profiles/<name>/agent`) are not targeted; the default profile is `~/.omp/agent`. Both go in
`HomePaths` (`PiAgentDir`, `OhMyPiAgentDir`).

The file is `.js`, not `.ts`: both harnesses load it, it needs no type imports (so it depends on neither
package name), and the Node tests run it without a TypeScript step.

Two `IProviderIntegrator`s, `PiIntegrator` and `OhMyPiIntegrator`, implement `IGlobalIntegrator`,
`IHookIntegrator` and `IUninstallIntegrator`, following `OpenCodeIntegrator`. Their shared logic (write the
agents files, write the extension, reconcile rtk, uninstall) lives in an abstract `PiFamilyIntegrator` base
parameterized by provider name and paths, so the two sealed classes hold only their paths and the trust note.

### The extension

`PiExtension` (beside `OpenCodePlugin`) generates the body for a provider name. It is stamped with
`StampStyle.SlashComment` like the OpenCode plugin, and has no legacy variant.

```js
// dtk (DotnetTokenKiller) rewrites the dotnet commands dtk supports to `dtk dotnet ...` before <harness> runs them.
// Generated by `dtk init <provider>`; run it again to refresh this file.
import { spawn } from "node:child_process";
// … findDtk() and rewrite(command) shared with the OpenCode plugin, spawning ["hook", "<provider>"] …

export default function (pi) {
  pi.on("tool_call", async (event) => {
    try {
      if (event?.toolName !== "bash") return undefined;
      const command = event.input?.command;
      if (typeof command !== "string" || !command.includes("dotnet")) return undefined;
      const rewritten = await rewrite(command);
      if (typeof rewritten !== "string" || rewritten === "") return undefined;
      event.input.command = rewritten;                          // pi executes the mutated args
      return { input: { ...event.input, command: rewritten } }; // oh-my-pi executes the returned input
    } catch {
      return undefined; // a throw would block the tool call
    }
  });
}
```

`findDtk()` and `rewrite()` move out of `OpenCodePlugin.Body` into a shared snippet with the hook's provider
name as a parameter, so all three generated files resolve dtk the same way: absolute `PATH` entries only,
never a bare name (Windows would try the project directory first, before the harness's permission check),
executable off Windows, no shell, 5 s timeout, and every failure meaning "no rewrite". The OpenCode plugin's
output bytes stay identical, which `OpenCodeIntegratorTests` and its stamp tests pin.

The generated file carries each provider's invocation signature (`["hook", "pi"]`,
`["hook", "oh-my-pi"]`) so a locally edited file is recognized as dtk's and left alone, as for OpenCode.

### The hook verbs

`dtk hook pi` and `dtk hook oh-my-pi` take OpenCode's payload: `{"command":"…"}` on stdin; the reply is
`{"command":"dtk dotnet …"}`, or empty stdout when the command is kept. `HookPayloadKind` gains `Pi` and
`OhMyPi`; `HookPayloads.Reply` routes both to the OpenCode reply. As with OpenCode, the harness still asks
the user to approve what runs, so there is no `allow` decision and no `IsSimpleCommand` gate. `HookEntryPoint`'s
usage string, `HookCommands`, completion scripts and `InitCommandSettings` list both.

### Generalizing the OpenCode-only checks

`UninstallHelpers.IsRegistered` and `HookHealthChecker.ClassifyPlugin` compare a plugin file against
`OpenCodePlugin.InvocationSignature`. The signature moves onto the plugin's `HookInstallation` (or its
`GeneratedArtifact`), and both checks read it from there. No behaviour changes for OpenCode.

### pi's project trust

A project-scope `dtk init pi` adds a note to its result:

> pi loads `.pi/extensions` only in a trusted project: approve it when pi asks, or run `/trust`. Print,
> JSON and RPC modes (`pi -p`) skip it until then; `dtk init pi --global` avoids the prompt.

`dtk doctor` repeats the note beside a current project-scope pi install. dtk does not read or write
`trust.json`: trusting a project is the user's decision. oh-my-pi gets no note.

### rtk coexistence

rtk's `rtk init --agent pi` writes `.pi/extensions/rtk.ts`, which calls `rtk rewrite` and mutates
`event.input.command`. Unreconciled, it and dtk's extension would both claim `dotnet` commands, in load order.
Both integrators pass every file in both scopes' extensions directories to
`RtkHookCoexistence.ReconcileFilesAsync`, as OpenCode does with its plugin folders; the existing
`rtk rewrite` detection matches rtk's pi extension, and reconciling adds `dotnet` to rtk's
`exclude_commands`. oh-my-pi's directories are scanned too, since a user may copy rtk's extension there.

### oh-my-pi's minimizer

Not configured by dtk. Once the command is `dtk dotnet …`, the minimizer's program-name dispatch no
longer selects its `dotnet` filter, so the output is filtered once, by dtk. The docs say so, and point to
`shellMinimizer.except` for a user who wants oh-my-pi to leave other commands alone too. Editing oh-my-pi's
`config.yml` would be a user-config write with nothing to fix.

### Version floors

pi 0.73.1 or later (in-place mutation; 0.87.1 verified). oh-my-pi 18.2.1 or later (returned `input`); on
an older oh-my-pi the extension loads and the command runs unchanged. The docs state both; dtk does not
detect harness versions.

### Uninstall

`--uninstall` removes the stamped `dtk.js` (kept if edited), and the shared `AGENTS.md` section and skill
unless another provider's dtk hook is registered in the same scope — the existing
`IntegrateUseCase.SharedArtifactsInUse` rule, which the two integrators join by implementing
`IUninstallIntegrator` and `IHookIntegrator`.

## Testing

- **Application tests**: `PiIntegratorTests` and `OhMyPiIntegratorTests` (both scopes, `PI_CODING_AGENT_DIR`,
  `--force`, stamp refresh, local edits kept, the trust note only for project-scope pi, rtk reconcile);
  `HookPayloadsTests` for both kinds; `HomePathsTests`; `HookHealthCheckerTests` for classification and
  probes of both; `ArtifactStampingTests` for the new bodies.
- **OpenCode unchanged**: a test pins `OpenCodePlugin.Body` against its current bytes before the shared
  snippet is extracted.
- **Node tests** (`PiExtensionTests`, `[NodeFact]`/`[NodeUnixFact]`, `DTK_NODE_REQUIRED` in CI): import the
  generated `dtk.js`, call its default export with a fake `pi` that records the `tool_call` handler, and
  drive it against the real dtk and fake shell-script dtks:
  - a dotnet command is rewritten both in place and in the returned `{input}`, which keeps the other fields;
  - non-bash tools, non-dotnet commands and non-string commands pass through (`undefined`, input untouched);
  - a missing dtk, a non-zero exit, a timeout, and malformed JSON resolve to no rewrite, without throwing.
- **CLI integration tests**: `dtk init pi|oh-my-pi` in both scopes, `UninstallIntegrationTests`
  round-trips, `HookEntryPointTests`, help and completion snapshots, AOT `ParityCases`.
- **End-to-end (manual, recorded in the PR)**: install pi 0.87.1 and oh-my-pi 18.3.2, point each at a fake
  or real model, and confirm that a `dotnet build` bash tool call runs as `dtk dotnet build` in both scopes,
  and that pi's project scope loads only after trust.

## Docs

README (provider count, `--global` list, provider table), `docs/index.md`, `docs/articles/ai-agent-setup.md`
(a pi section and an oh-my-pi section: paths, trust, minimizer, version floors), `usage.md`,
`getting-started.md`, `CLAUDE.md` (a paragraph like OpenCode's), and `src/DotnetTokenKiller.Cli/README.md`.

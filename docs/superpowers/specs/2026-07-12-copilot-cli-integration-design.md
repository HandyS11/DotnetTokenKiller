# Design: GitHub Copilot CLI integration provider (`copilot-cli`)

**Date:** 2026-07-12
**Status:** Approved (pending spec review)

## Goal

Add a new dtk integration provider, `copilot-cli`, that gives **GitHub Copilot CLI** a
strong, hook-based integration: transparently rewriting `dotnet build|clean|format|restore|test`
shell commands to `dtk dotnet …` so Copilot CLI gets the same automatic token savings that the
`claude` and `gemini` providers already deliver — plus a shared instructions doc (the user chose
the "hook + doc" tier).

This is distinct from the existing `copilot` provider, which is instruction-only (it targets the
GitHub Copilot **IDE** experience via `.github/copilot-instructions.md`). `copilot-cli` is a new,
separately-named provider; the existing `copilot` provider is unchanged.

## Background: how GitHub Copilot CLI hooks work

Confirmed from GitHub docs (July 2026):

- Copilot CLI discovers hooks as individual `*.json` files:
  - **Personal / global:** `~/.copilot/hooks/*.json` (honoring `COPILOT_HOME` → `$COPILOT_HOME/hooks/`).
  - **Repository:** `.github/hooks/*.json`.
- A hook file has the shape:
  ```json
  {
    "version": 1,
    "hooks": {
      "preToolUse": [
        {
          "type": "command",
          "matcher": "bash",
          "bash": "python3 dotnet-to-dtk.py",
          "cwd": ".github/hooks",
          "timeoutSec": 10
        }
      ]
    }
  }
  ```
  The `matcher` is a regex anchored as `^(?:…)$` against `toolName`.
- **`preToolUse` input** (stdin JSON): `{ sessionId, timestamp, cwd, toolName, toolArgs }`.
  For the file-based CLI hook, `toolArgs` arrives as a **JSON string** (double-encoded), e.g.
  `"toolArgs": "{\"command\":\"dotnet build\"}"`. The script must parse it, then read `.command`.
  (The SDK passes `toolArgs` as an object; the script handles both defensively.)
- **`preToolUse` output** to rewrite (stdout JSON):
  ```json
  { "permissionDecision": "allow",
    "modifiedArgs": { "command": "dtk dotnet build", "timeout": 30000 } }
  ```
  `modifiedArgs` is an **object** (not a string) that fully replaces `toolArgs`; other fields
  (e.g. `timeout`) must be preserved by spreading the original args.
- **Fail-closed:** a crash or non-zero exit **denies** the tool call. The script must therefore
  always exit 0 with valid output, and emit nothing (allow, no change) on the no-rewrite path.

Sources:
- <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks>
- <https://docs.github.com/en/copilot/reference/hooks-configuration>
- <https://docs.github.com/en/copilot/tutorials/copilot-cli-hooks>

## Existing architecture (what we plug into)

- `IProviderIntegrator` (Domain) — `ProviderName` + `IntegrateAsync(directory, force, ct)`.
- `IGlobalIntegrator` (Domain) — optional `IntegrateGlobalAsync(force, ct)` for `--global`.
- `IntegrateUseCase` (Application) — receives all `IProviderIntegrator`s via DI, indexes by name,
  dispatches. `--global` requires the provider to implement `IGlobalIntegrator`.
- `IntegratorHelpers` — `WriteFileAsync` (skip-if-exists / overwrite-on-force),
  `WriteSectionBasedFileAsync` (marker-delimited doc merge), and the JSON hook-merge helpers.
- `HookScriptTemplates` — `SharedCore` Python (quote-aware `dotnet <sub>` → `dtk dotnet <sub>`
  rewriter) + per-host header/main. Claude and Gemini reuse `SharedCore`; they differ only in the
  I/O shape of the emitted JSON.
- `HomePaths` — resolves `~/.claude`, `~/.gemini`, etc. for global installs.
- `IntegrationInstructions.Markdown` — the shared human-readable "prefer dtk" doc text.
- DI registration in `DependencyInjection.cs`; command wiring in `Program.cs` +
  `IntegrateCommandSettings`.

**Key finding:** the shared `WriteHookAndSettingsAsync`/`MergeJsonSettingsAsync` helpers emit the
Claude/Gemini `settings.json` shape (`hooks[Event] = [{matcher, hooks:[{type,command}]}]`), which
is **not** Copilot CLI's hook-file shape. Copilot CLI therefore needs its own tiny JSON emitter.
Because dtk owns a **dedicated** hook file (`dtk-dotnet.json`) that contains only dtk's hook, we do
**not** need the merge machinery — we use `WriteFileAsync`, which already implements the correct
skip/overwrite/`--force` semantics. Lower risk, no third-party JSON to merge.

## Design

### New provider: `CopilotCliIntegrator`

`internal sealed class CopilotCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator`
in `src/DotnetTokenKiller.Application/Integration/`. `ProviderName => "copilot-cli"`.

Artifacts installed (both repo and global variants):

1. **Hook script** — `dotnet-to-dtk.py`, written via `WriteFileAsync`.
   - repo: `.github/hooks/dotnet-to-dtk.py`
   - global: `~/.copilot/hooks/dotnet-to-dtk.py`
2. **Hook registration JSON** — `dtk-dotnet.json`, written via `WriteFileAsync` (dtk-owned,
   dedicated; no merge).
   - repo: `.github/hooks/dtk-dotnet.json`
   - global: `~/.copilot/hooks/dtk-dotnet.json`
3. **Instructions doc** (repo variant only) — section-merged into `.github/copilot-instructions.md`
   via `WriteSectionBasedFileAsync`, reusing the same `<!-- dtk -->` markers and
   `IntegrationInstructions.Markdown` as the existing `copilot` provider. Running both `integrate
   copilot` and `integrate copilot-cli` is benign but **not** idempotent: `copilot-cli`'s section
   additionally includes a preToolUse-hook paragraph that the IDE `copilot` section lacks, so the
   two providers write different content into the same `<!-- dtk -->` block — last writer wins, with
   no crash and no user-content loss, but switching providers may need `--force` to refresh the
   section.
   The **global** variant installs the hook only and emits an advisory note that the instructions
   doc is repository-scoped.

### New hook script template: `HookScriptTemplates.CopilotCliHook`

Add `CopilotCliHeader` + `CopilotCliMain`, reusing the existing `SharedCore` verbatim. The `main()`:

```python
def main() -> None:
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return  # allow, no change

    if payload.get("toolName") != "bash":
        return

    tool_args = payload.get("toolArgs", {})
    if isinstance(tool_args, str):          # CLI file hooks: toolArgs is a JSON string
        try:
            tool_args = json.loads(tool_args)
        except (json.JSONDecodeError, TypeError):
            return
    if not isinstance(tool_args, dict):
        return

    command = tool_args.get("command", "")
    if not command:
        return

    rewritten = rewrite(command)
    if rewritten != command:
        modified = dict(tool_args)          # preserve timeout et al.
        modified["command"] = rewritten
        # Auto-approve only a simple single invocation; a compound command (e.g.
        # `dotnet build && rm -rf x`) is still rewritten but returns "ask" so Copilot
        # prompts rather than silently approving its non-dotnet parts.
        decision = "allow" if _is_simple_command(command) else "ask"
        print(json.dumps({
            "permissionDecision": decision,
            "modifiedArgs": modified,
        }))
    # No output on the no-change path: Copilot CLI proceeds normally.
```

Exposed as `internal static string CopilotCliHook { get; } = CopilotCliHeader + SharedCore + CopilotCliMain;`.

### Hook JSON content

Built in `CopilotCliIntegrator` (small enough to inline; it is not the Claude/Gemini shape):

```json
{
  "version": 1,
  "hooks": {
    "preToolUse": [
      {
        "type": "command",
        "matcher": "bash",
        "bash": "python3 dotnet-to-dtk.py",
        "cwd": "<hooks dir>",
        "timeoutSec": 10
      }
    ]
  }
}
```

- `bash` invokes the sibling script by name; `cwd` points at the hooks directory so the relative
  script path resolves regardless of Copilot's process cwd.
- **Open implementation detail to verify with a manual Copilot CLI run:** whether `cwd` is
  interpreted relative to the repo root (repo hooks) and how it should be expressed for the global
  `~/.copilot/hooks` location (relative default vs. `$HOME`-rooted vs. resolved absolute path). Default
  plan: repo → `cwd: ".github/hooks"`; global → the resolved absolute `~/.copilot/hooks` path. If a
  manual run shows the script is found without `cwd`, drop the field. Windows note (mirroring
  Gemini's doc caveat): the launcher may be `python` rather than `python3`; the instructions section
  will mention editing the hook if it doesn't fire.

### `HomePaths` addition

Add `internal string CopilotHooksDir => Path.Combine(CopilotHome, "hooks");` where `CopilotHome`
honors the `COPILOT_HOME` env var, falling back to `Path.Combine(Home, ".copilot")`. Keep the
`COPILOT_HOME` resolution inside `HomePaths` so the test seam (injected home) still isolates it —
tests set `COPILOT_HOME`-independent behavior by relying on the `.copilot` fallback under the
injected home. (If `COPILOT_HOME` reading complicates the test seam, resolve it in the integrator
instead; decide during implementation, preferring the fallback-under-injected-home path for tests.)

### Wiring

- `DependencyInjection.cs`: `services.AddTransient<IProviderIntegrator, CopilotCliIntegrator>();`
- `IntegrateCommandSettings`: add `copilot-cli` to the `<provider>` description; update the
  `--global` description to include `copilot-cli`.
- `Program.cs`: add `.WithExample(["integrate", "copilot-cli"])` (and a `--global` example).
- `IntegrateCommand` prints notes/summary through the existing generic path — no change needed
  beyond the provider being resolvable.

## Testing

New `tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs`, mirroring
`GeminiCliIntegratorTests`/`ClaudeCodeIntegratorTests`:

- **Repo install** creates `.github/hooks/dotnet-to-dtk.py`, `.github/hooks/dtk-dotnet.json`, and
  the `.github/copilot-instructions.md` dtk section.
- **Hook JSON** parses and has `version: 1`, `hooks.preToolUse[0].matcher == "bash"`, `type ==
  "command"`, and a `bash` field referencing the script.
- **Global install** targets `~/.copilot/hooks/` (via injected `HomePaths`), installs the hook +
  script, does **not** write a repo instructions doc, and returns a note.
- **`--global` is supported**: `IntegrateUseCase.RunGlobalAsync("copilot-cli", …)` does not throw
  (i.e. the provider implements `IGlobalIntegrator`).
- **Idempotency**: second run without `--force` skips existing files; with `--force` overwrites.
- **Rewrite behavior** (exercise the generated Python via a subprocess, as existing hook tests do
  if they do — otherwise assert on the emitted script text / a focused Python invocation):
  - `toolName == "bash"`, `toolArgs` as a **JSON string** `{"command":"dotnet build"}` →
    output `permissionDecision: allow` + `modifiedArgs.command == "dtk dotnet build"`.
  - `timeout` in `toolArgs` is preserved in `modifiedArgs`.
  - Non-matching command (e.g. `git status`) or `toolName != "bash"` → **no stdout**, exit 0.
  - Already-`dtk`-prefixed / quoted / path-embedded `dotnet` left untouched (covered by
    `SharedCore`; a light assertion suffices since the core is shared and already tested).

## Docs

- Update `CLAUDE.md` (and README if it lists providers) to include `copilot-cli` in the provider
  list and note it is the hook-based Copilot **CLI** integration (vs. the instruction-only IDE
  `copilot`).

## Out of scope (YAGNI)

- Changing or deprecating the existing `copilot` (IDE) provider.
- Global/personal Copilot CLI instructions file (unclear whether Copilot CLI reads a home-level
  instructions doc; global install ships the hook only). Revisit if a personal-instructions path is
  confirmed.
- Enterprise-managed plugins / MCP-based distribution.

## Build sequence

1. `HomePaths.CopilotHooksDir` (+ `COPILOT_HOME` handling).
2. `HookScriptTemplates.CopilotCliHook` (header + main; reuse `SharedCore`).
3. `CopilotCliIntegrator` (repo + global).
4. DI registration + CLI settings/examples.
5. Tests.
6. Manual verification against a real Copilot CLI session to confirm `cwd`/script resolution and
   the rewrite firing end-to-end; fold any `cwd` correction back into the integrator.
7. Docs (`CLAUDE.md` / README).

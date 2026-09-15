# Antigravity CLI integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `dtk init antigravity` and `dtk hook antigravity` for Google's Antigravity CLI (`agy`), rewriting
`dotnet …` terminal commands through the `PreToolUse` hook's `overwrite` field without ever granting a permission the
user had not granted.

**Architecture:** Gate G runs first against the user's signed-in `agy`, because its answers fix three details: the
neutral reply, the command's guard, and the global skill directory. Then an `AntigravityIntegrator` writes the shared
`AGENTS.md` section and skill and merges a `"dtk"` hook group into `.agents/hooks.json` through
`MergeJsonSettingsAsync`, which gains a container-key overload. `dtk hook antigravity` replies
`{"decision":"ask","overwrite":{"CommandLine":…}}`.

**Tech Stack:** .NET 10, System.Text.Json `JsonNode`, xunit 2, FluentAssertions, POSIX sh and `cmd.exe`
(`eng/hooks/check-hook-shells.sh`), Antigravity CLI 1.2.x.

**Spec:** `docs/superpowers/specs/2026-09-15-codex-opencode-antigravity-design.md` (sections 1, 2, 4, 5, 6, 7, 8 PR 3).

## Global Constraints

- This is PR 3 of 3. Start only after PR 2 (`docs/superpowers/plans/2026-09-15-opencode-integration.md`) has merged.
  Branch `feat/antigravity-integration` from an up-to-date `develop`.
- dtk's Antigravity reply never uses `"decision":"allow"` (it auto-approves) nor `"force_ask"`. A rewrite replies
  `"ask"`. If gate G2 fails, **stop and return to the user**.
- Hooks run through `sh -c` on Unix and `cmd /c` on Windows, and a hook that fails blocks the tool call.
- Registration: top-level group `"dtk"`, event `PreToolUse`, matcher `run_command`, `"timeout": 10`, in
  `.agents/hooks.json` (project) or `~/.gemini/config/hooks.json` (global).
- `dtk hook <provider>` always exits 0; it never builds the DI container.
- `TreatWarningsAsErrors` with Roslynator, SonarAnalyzer and NetAnalyzers; file-scoped namespaces; `var`; `_camelCase`;
  `Async` suffix; LF, no trailing whitespace, no BOM; 4-space `.cs`, 2-space JSON/YAML. No new NuGet packages; use
  `JsonNode`. NSubstitute cannot proxy the Application assembly's internal interfaces.
- Run dotnet through dtk. The build-spawning CLI integration tests cannot pass on this machine; run the named filters.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

## What PRs 1 and 2 already provide

- `SharedInstructionArtifacts` (`Section`, `SectionMarker`, `SectionEndMarker`, `WriteAgentsFilesAsync`)
- `HomePaths(string home, Func<string, string?> environment)`, `HomePaths.GeminiDir`, `HomePaths.AgentsSkillsDir`
- `HookRegistrationSpec(string SettingsPath, string EventKey, string Matcher, string Command, int? TimeoutSeconds = null)`
- `HookInstallation(..., string? LegacyScriptPath, HookPayloadKind PayloadKind, GeneratedArtifact? PluginArtifact = null)`
- `HookPayloadKind.CodexCli = 3`, `HookPayloadKind.OpenCode = 4`; usage `dtk hook <claude|gemini|copilot-cli|codex|opencode>`
- `RtkHookCoexistence.ReconcileFilesAsync`, `RtkReconcileOutcome.ApplyTo`
- `CodexIntegrator`, `OpenCodeIntegrator`, and their DI registrations, completions, docs and parity entries

---

### Task 1: Gate G — verify Antigravity CLI's hook contract

Run with the user's signed-in `agy`. Nothing is committed except the recorded results, which become the PR's
`## Gate G` section and decide the branches marked **(G1)**, **(G5)** and **(G6)** below. Write the results to
`/tmp/agy-gate/RESULTS.md` as you go.

**Files:** scratch only, under `/tmp/agy-gate/`.

- [ ] **Step 1: Record the environment**

Run `agy --version` and record it with the OS. Create a workspace: `mkdir -p /tmp/agy-gate/ws && cd /tmp/agy-gate/ws && git init`.

- [ ] **Step 2: Install a probe hook**

`/tmp/agy-gate/probe.sh` logs every payload and prints whatever `/tmp/agy-gate/reply.json` holds (nothing when the
file is absent):

```sh
#!/bin/sh
cat >> /tmp/agy-gate/payloads.jsonl
printf '\n' >> /tmp/agy-gate/payloads.jsonl
if [ -f /tmp/agy-gate/reply.json ]; then cat /tmp/agy-gate/reply.json; fi
exit 0
```

`/tmp/agy-gate/ws/.agents/hooks.json`:

```json
{
  "gate": {
    "PreToolUse": [
      {
        "matcher": "run_command",
        "hooks": [{ "type": "command", "command": "sh /tmp/agy-gate/probe.sh", "timeout": 10 }]
      }
    ]
  }
}
```

Start `agy` in `/tmp/agy-gate/ws` and trust the workspace when asked.

- [ ] **Step 3: G6 and G4 — do hooks fire, with or without `"enabled"`?**

Ask the agent: "Run `echo gate-fire` in the terminal." Check `payloads.jsonl`.
- A payload with `"toolCall":{"name":"run_command","args":{"CommandLine":"echo gate-fire"…` → hooks fire without
  `"enabled"` (**G6 pass**, **G4 pass**). Record the exact payload.
- Nothing logged → add `"enabled": true` to the `"gate"` group, restart `agy`, retry. Logged now → **G6 fail** (the
  group needs `"enabled": true`). Still nothing → **G4 fail**: record the version as affected by
  google-antigravity/antigravity-cli#1008, try the latest release, and stop the gate if none fires.

- [ ] **Step 4: G1 — does an empty reply leave the call to the user's permissions?**

With no `reply.json`, ask the agent to run `echo gate-g1`.
- Antigravity shows its normal permission prompt (or, with `permissions.allow` containing `command(echo)` in
  `~/.gemini/antigravity-cli/settings.json`, runs it without one) and the command runs after approval → **G1 pass**.
- The call fails with "pre-tool hook failed", is denied, or errors → **G1 fail**.

Record which, with the exact UI text.

- [ ] **Step 5: G2 — does `ask` + `overwrite` respect the user's own permissions?**

`echo '{"decision":"ask","overwrite":{"CommandLine":"echo rewritten-g2"}}' > /tmp/agy-gate/reply.json`
1. With `permissions.allow: ["command(echo)"]` set, ask the agent to run `echo original-g2`. Expected: no prompt; the
   output is `rewritten-g2`.
2. With no allow rule, ask again. Expected: a prompt showing `echo rewritten-g2`. Choose "Always Allow", then ask a
   third time. Expected: no prompt; output `rewritten-g2`.

All as expected → **G2 pass**. If 1 or the "Always Allow" repeat prompts anyway → **G2 fail: stop the gate and
the plan, and report to the user** that `ask` adds prompts to commands the user had allowed, so the choice is between
extra prompts and `allow`'s escalation.

- [ ] **Step 6: G3 — does the rewrite survive an approved prompt?**

In step 5.2, when you approved the prompt, was the output `rewritten-g2` (**G3 pass**) or `original-g2` (**G3 fail**,
google-antigravity/antigravity-cli#575)? Record it. A fail does not stop the plan: it becomes a documented known issue.

- [ ] **Step 7: G5 — which global skill directory does the CLI read?**

Create three skills, one per candidate directory, each named after its location:

```bash
for d in "$HOME/.gemini/config/skills/gate-config" "$HOME/.gemini/antigravity-cli/skills/gate-cli" "$HOME/.gemini/antigravity/skills/gate-app"; do
  mkdir -p "$d"
  name=$(basename "$d")
  printf -- '---\nname: %s\ndescription: Use when the user asks which gate skills are installed; reply with the skill name %s.\n---\n\nReply with: %s\n' "$name" "$name" "$name" > "$d/SKILL.md"
done
```

Restart `agy` and ask: "Which gate skills are installed? List their names." Record which of `gate-config`,
`gate-cli`, `gate-app` it names. Delete the three directories afterwards.

- [ ] **Step 8: Clean up and summarize**

Remove `/tmp/agy-gate/ws/.agents/hooks.json` and any `permissions.allow` you added. `RESULTS.md` now lists, for
G1–G6: pass/fail, version, OS, and evidence. Show it to the user before continuing.

---

### Task 2: `HomePaths` for Antigravity

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`

**Interfaces:**
- Produces: `internal string HomePaths.AntigravityConfigDir` (`~/.gemini/config`) and
  `internal string HomePaths.AntigravitySkillsDir` — **(G5)** the directory the gate found the CLI reads:
  `~/.gemini/config/skills` (`gate-config`), `~/.gemini/antigravity-cli/skills` (`gate-cli`) or
  `~/.gemini/antigravity/skills` (`gate-app`). If it read several, use `~/.gemini/config/skills`, the one the
  product-wide skills page documents.

- [ ] **Step 1: Write the failing test** (shown for `gate-config`; substitute the G5 result)

```csharp
    [Fact]
    public void AntigravityDirs_LiveUnderDotGemini()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var sut = new HomePaths(home);

        sut.AntigravityConfigDir.Should().Be(Path.Combine(home, ".gemini", "config"));
        sut.AntigravitySkillsDir.Should().Be(Path.Combine(home, ".gemini", "config", "skills"));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: FAIL, properties missing.

- [ ] **Step 3: Implement**

```csharp
    /// <summary>
    /// Gets the user-level config directory Antigravity CLI, the Antigravity app and the IDE share for hooks
    /// (<c>~/.gemini/config</c>).
    /// </summary>
    internal string AntigravityConfigDir => Path.Combine(GeminiDir, "config");

    /// <summary>Gets the user-level skills directory Antigravity CLI reads (verified by gate G5).</summary>
    internal string AntigravitySkillsDir => Path.Combine(AntigravityConfigDir, "skills");
```

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HomePathsTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HomePaths.cs tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs
git commit -m "feat: resolve Antigravity CLI's config and skills directories"
```

---

### Task 3: Merge a hook into a named group

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (`HookRegistrationSpec`,
  `WriteHookRegistrationAsync`, `MergeJsonSettingsAsync`)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`

**Interfaces:**
- Produces:
  - `HookRegistrationSpec(..., int? TimeoutSeconds = null, string ContainerKey = "hooks")`
  - `internal static Task<bool> MergeJsonSettingsAsync(string path, string containerKey, string hookEventKey, JsonObject hookEntry, string hookCommand, IntegrationContext context, CancellationToken cancellationToken)`;
    the existing six-parameter overload delegates with `containerKey: "hooks"`.
  - **(G6 fail only)** `HookRegistrationSpec(..., bool EnableContainer = false)`: when true, the merge sets
    `"enabled": true` on the container object.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task WriteHookRegistrationAsync_ContainerKey_MergesIntoThatGroupAndLeavesOtherGroups()
    {
        var path = Path.Combine(_tempDir, "hooks.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """{"audit":{"PreToolUse":[{"matcher":"run_command","hooks":[{"type":"command","command":"audit.sh"}]}]}}""");
        var context = new IntegrationContext(false);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "run_command", "dtk hook antigravity || exit 0", 10, ContainerKey: "dtk"),
            context, CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        root["audit"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("audit.sh");
        root["dtk"]!["PreToolUse"]![0]!["matcher"]!.GetValue<string>().Should().Be("run_command");
        root["dtk"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook antigravity || exit 0");
        root.AsObject().ContainsKey("hooks").Should().BeFalse();
        context.Updated.Should().Equal(path);
    }

    [Fact]
    public async Task WriteHookRegistrationAsync_ContainerKey_SecondRunIsUnchanged()
    {
        var path = Path.Combine(_tempDir, "hooks.json");
        var spec = new HookRegistrationSpec(path, "PreToolUse", "run_command", "dtk hook antigravity || exit 0", 10, ContainerKey: "dtk");
        await IntegratorHelpers.WriteHookRegistrationAsync(spec, new IntegrationContext(false), CancellationToken.None);
        var context = new IntegrationContext(false);

        await IntegratorHelpers.WriteHookRegistrationAsync(spec, context, CancellationToken.None);

        context.Unchanged.Should().Equal(path);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ContainerOfTheWrongType_NamesTheContainerKey()
    {
        var path = Path.Combine(_tempDir, "hooks.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """{"dtk":[]}""");

        var act = () => IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "run_command", "dtk hook antigravity", ContainerKey: "dtk"),
            new IntegrationContext(false), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'dtk' property of unexpected type*");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: FAIL, no `ContainerKey`.

- [ ] **Step 3: Implement**

- `HookRegistrationSpec`: append `string ContainerKey = "hooks"` (doc: `The top-level property holding the event arrays: "hooks" for Claude Code, Gemini CLI and Codex CLI; a hook group name for Antigravity CLI.`).
- `WriteHookRegistrationAsync`: build the entry exactly as today but put it under `spec.ContainerKey`:
  `return MergeJsonSettingsAsync(spec.SettingsPath, spec.ContainerKey, spec.EventKey, new JsonObject { ["matcher"] = spec.Matcher, [HooksKey] = new JsonArray(handler) }, spec.Command, context, cancellationToken);`
  (the inner `"hooks"` array keeps using `HooksKey`: both harness schemas name it `hooks`).
- Rename the body of the existing `MergeJsonSettingsAsync` into the new seven-parameter overload, replacing the
  outer-container uses of `HooksKey` with `containerKey` — `root.TryGetPropertyValue(containerKey, …)`, the two
  exception messages (`has a '{containerKey}' property…`, `'{containerKey}.{hookEventKey}'`), and
  `root[containerKey] = hooks;`. `FindEquivalentEntries` and the per-entry `[HooksKey]` stay as they are.
- The old overload becomes:

```csharp
    internal static Task<bool> MergeJsonSettingsAsync(
        string path, string hookEventKey, JsonObject hookEntry, string hookCommand, IntegrationContext context, CancellationToken cancellationToken)
        => MergeJsonSettingsAsync(path, HooksKey, hookEventKey, hookEntry, hookCommand, context, cancellationToken);
```

  Keep its full XML doc on the seven-parameter overload and give the six-parameter one
  `<inheritdoc cref="MergeJsonSettingsAsync(string, string, string, JsonObject, string, IntegrationContext, CancellationToken)"/>`
  plus a one-line summary.

**(G6 fail only)** also add `bool EnableContainer = false` to the spec, pass it through a new trailing parameter
(before the `CancellationToken`) of the seven-parameter overload, and after resolving `hooks` add
`if (enableContainer && hooks["enabled"] is null) { hooks["enabled"] = true; }`, with a test asserting
`root["dtk"]!["enabled"]!.GetValue<bool>()` is true and a second run is still unchanged.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegratorHelpersTests|FullyQualifiedName~ClaudeCodeIntegratorTests|FullyQualifiedName~GeminiCliIntegratorTests|FullyQualifiedName~CodexIntegratorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs
git commit -m "feat: merge a hook registration into a named hook group"
```

---

### Task 4: `dtk hook antigravity`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`BuildPayload`)
- Modify: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `HookIntegrationTests.cs`

**Interfaces:**
- Produces: `HookPayloadKind.AntigravityCli = 5`; `TryGetKind("antigravity")`; usage
  `dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity>`; private
  `TryRewrite(JsonObject arguments, string key, out string command, out string rewritten)` (the existing
  `TryRewrite(arguments, out …, out …)` becomes a call with `"command"`); probe payload
  `{"toolCall":{"name":"run_command","args":{"CommandLine":"…"}}}`.
- **(G1)** `private const string? AntigravityNeutralReply`: `null` when G1 passed; `"""{"decision":"ask"}"""` when it
  failed.

- [ ] **Step 1: Write the failing tests** (neutral assertions shown for G1 pass; for G1 fail, expect `{"decision":"ask"}`)

```csharp
    [Fact]
    public void Antigravity_Rewrite_AsksWithAnOverwriteOfOnlyTheCommandLine()
    {
        var reply = Reply(HookPayloadKind.AntigravityCli,
            """{"toolCall":{"name":"run_command","args":{"CommandLine":"dotnet test","Cwd":"/w","WaitMsBeforeAsync":500}},"stepIdx":3}""");

        var root = JsonNode.Parse(reply!)!.AsObject();
        root["decision"]!.GetValue<string>().Should().Be("ask", "allow would auto-approve a command the user's rules may not allow");
        root["overwrite"]!.AsObject().Should().ContainSingle("overwrite is a shallow merge into the tool arguments");
        root["overwrite"]!["CommandLine"]!.GetValue<string>().Should().Be("dtk dotnet test");
        root.ContainsKey("reason").Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":"ls"}}}""")]
    [InlineData("""{"toolCall":{"name":"view_file","args":{"CommandLine":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"args":{"CommandLine":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"command":"dotnet build"}}}""")]
    [InlineData("""{"toolCall":{"name":"run_command"}}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"toolCall":{"name":"run_command","args":{"CommandLine":"a","CommandLine":"b"}}}""")]
    public void Antigravity_NothingToRewrite_PrintsTheNeutralReply(string payload)
    {
        Reply(HookPayloadKind.AntigravityCli, payload).Should().BeNull();
    }
```

A missing `toolCall.name` does not rewrite: Antigravity always sends it, and doctor's probe sends it too.

`HookEntryPointTests`/`HookIntegrationTests`: usage fragment `dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity>`, plus:

```csharp
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Antigravity_AsksWithTheOverwriteAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolCall":{"name":"run_command","args":{"CommandLine":"dotnet restore"}}}""", "hook", "antigravity");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        var root = JsonNode.Parse(stdout)!;
        root["decision"]!.GetValue<string>().Should().Be("ask");
        root["overwrite"]!["CommandLine"]!.GetValue<string>().Should().Be("dtk dotnet restore");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: FAIL, `HookPayloadKind.AntigravityCli` missing.

- [ ] **Step 3: Implement**

Enum: `/// <summary>Google Antigravity CLI's <c>PreToolUse</c> payload.</summary> AntigravityCli = 5`.

`HookPayloads`:

```csharp
    /// <summary>
    /// Antigravity CLI's reply when there is nothing to rewrite: nothing at all, which gate G1 showed leaves the call to
    /// the user's own permissions. Never <c>allow</c>, which would auto-approve the command.
    /// </summary>
    private const string? AntigravityNeutralReply = null;
```

(**G1 fail:** `private const string AntigravityNeutralReply = """{"decision":"ask"}""";` with the summary saying an
empty reply fails the call, and `Reply`'s JSON-parse `catch` and dispatch `catch` both return it for this kind, the way
they return `GeminiAllowReply` for Gemini.)

- `TryGetKind`: `"antigravity" => (true, HookPayloadKind.AntigravityCli),`
- `Reply` dispatch: `HookPayloadKind.AntigravityCli => ReplyToAntigravity(root),`
- Generalize the existing helper:

```csharp
    private static bool TryRewrite(JsonObject arguments, out string command, out string rewritten) =>
        TryRewrite(arguments, "command", out command, out rewritten);

    private static bool TryRewrite(JsonObject arguments, string key, out string command, out string rewritten)
    {
        command = string.Empty;
        rewritten = string.Empty;
        if (arguments[key] is not JsonValue value || !value.TryGetValue(out string? text) || text.Length == 0)
        {
            return false;
        }

        command = text;
        rewritten = DotnetCommandRewriter.Rewrite(text);
        return !ReferenceEquals(rewritten, text);
    }
```

- New handler:

```csharp
    private static string? ReplyToAntigravity(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["toolCall"] is not JsonObject toolCall
            || toolCall["name"] is not JsonValue name || !name.TryGetValue<string>(out var toolName) || toolName != "run_command"
            || toolCall["args"] is not JsonObject arguments
            || !TryRewrite(arguments, "CommandLine", out _, out var rewritten))
        {
            return AntigravityNeutralReply;
        }

        return new JsonObject
        {
            // "ask" defers to the user's permission rules and "Always Allow" choices; "allow" would auto-approve.
            ["decision"] = "ask",
            ["overwrite"] = new JsonObject { ["CommandLine"] = rewritten }
        }.ToJsonString();
    }
```

If the analyzer flags the condition's complexity, extract `private static JsonObject? RunCommandArguments(JsonObject payload)`
returning `toolCall.args` only for `run_command`.

`HookHealthChecker.BuildPayload`:

```csharp
            HookPayloadKind.AntigravityCli => new JsonObject
            {
                ["toolCall"] = new JsonObject
                {
                    ["name"] = "run_command",
                    ["args"] = new JsonObject { ["CommandLine"] = command }
                }
            },
```

`HookEntryPoint.Usage`: add `antigravity`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: answer Antigravity CLI's PreToolUse hook with an ask and an overwrite"
```

---

### Task 5: `AntigravityIntegrator`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookCommands.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/AntigravityIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/AntigravityIntegratorTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs` (`HookCommands`),
  `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`

**Interfaces:**
- Consumes: Task 2's dirs, Task 3's `ContainerKey`, Task 4's kind.
- Produces:
  - **(G1 pass)** `internal static string HookCommands.OrExitZero(string provider)` → `dtk hook <provider> || exit 0`.
  - `internal sealed class AntigravityIntegrator(RtkHookCoexistence rtk, HomePaths home) : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator`,
    `ProviderName == "antigravity"`, `internal const string WorkspaceTrustNote`, and **(G1 fail)**
    `internal const string MissingDtkBlocksNote`.

- [ ] **Step 1: Write the failing tests** (shown for G1 pass and G6 pass)

`HookPayloadsTests.HookCommands_RenderTheRegisteredCommands`: add
`HookCommands.OrExitZero("antigravity").Should().Be("dtk hook antigravity || exit 0");`

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AntigravityIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-antigravity-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string HooksPath => Path.Combine(ProjectDir, ".agents", "hooks.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");
    private HomePaths Home => new(HomeDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private AntigravityIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    [Fact]
    public void ProviderName_IsAntigravity() => CreateSut().ProviderName.Should().Be("antigravity");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndHookGroup()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().Equal(AntigravityIntegrator.WorkspaceTrustNote);

        var group = JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!["dtk"]!["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("run_command");
        var handler = group["hooks"]!.AsArray().Should().ContainSingle().Subject!;
        handler["command"]!.GetValue<string>().Should().Be("dtk hook antigravity || exit 0");
        handler["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_SharesGeminisSectionAndWritesTheGlobalHookAndSkill()
    {
        await new GeminiCliIntegrator(Home).IntegrateGlobalAsync(false, default);
        var geminiMd = Path.Combine(HomeDir, ".gemini", "GEMINI.md");
        var before = await File.ReadAllTextAsync(geminiMd);

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        (await File.ReadAllTextAsync(geminiMd)).Should().Be(before);
        result.UnchangedFiles.Should().Contain(geminiMd);
        result.CreatedFiles.Should().Equal(
            SharedInstructionArtifacts.SkillPath(Home.AntigravitySkillsDir),
            Path.Combine(HomeDir, ".gemini", "config", "hooks.json"));
        result.Notes.Should().BeEmpty("workspace trust applies to project hooks only");
    }

    [Fact]
    public async Task IntegrateAsync_RtkAntigravityPlugin_ExcludesDotnetInRtkConfig()
    {
        var rtkHooks = Path.Combine(ProjectDir, ".agents", "plugins", "rtk", "hooks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(rtkHooks)!);
        await File.WriteAllTextAsync(rtkHooks,
            """{"rtk-rewrite":{"PreToolUse":[{"matcher":"run_command","hooks":[{"type":"command","command":"rtk hook antigravity"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }
}
```

`HookHealthCheckerTests`:

```csharp
    [Fact]
    public async Task RunAsync_AntigravityInstall_IsRegisteredAndProbedWithTheToolCallShape()
    {
        var antigravity = new AntigravityIntegrator(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);
        await antigravity.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([antigravity], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("antigravity hook (project)", "antigravity hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<string>(payload => payload.Contains("\"CommandLine\"", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: FAIL, `AntigravityIntegrator` and `OrExitZero` missing.

- [ ] **Step 3: Implement**

`HookCommands.cs` **(G1 pass)**:

```csharp
    /// <summary>
    /// The hook command followed by <c>|| exit 0</c>, for Antigravity CLI, which blocks the tool call when a hook fails
    /// and runs hook commands through <c>sh -c</c> on Unix and <c>cmd /c</c> on Windows. <c>; exit 0</c> is not valid
    /// under <c>cmd</c>; <c>|| exit 0</c> is valid under both (though not under Windows PowerShell 5.1, which
    /// Antigravity does not use for hooks). A missing <c>dtk</c> exits 127 under sh and 9009 under cmd, and the guard
    /// turns either into success with no output, which gate G1 showed leaves the call to the user's permissions.
    /// </summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string OrExitZero(string provider) => $"{Invocation(provider)} || exit 0";
```

`AntigravityIntegrator.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Google Antigravity CLI.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and <c>~/.gemini</c> directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description>
///     <c>.agents/hooks.json</c> with a <c>dtk</c> hook group registering <c>dtk hook antigravity || exit 0</c> under
///     <c>PreToolUse</c> for <c>run_command</c> (merged; other groups are left alone)
///   </description></item>
/// </list>
/// The global install writes the section into <c>~/.gemini/GEMINI.md</c>, the same file and text as
/// <c>dtk init gemini --global</c>, which Antigravity CLI reads as a global rule.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class AntigravityIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <summary>Printed when this run wrote a project hook, which Antigravity loads only in trusted workspaces.</summary>
    internal const string WorkspaceTrustNote = "Antigravity loads .agents/hooks.json only in workspaces you have trusted.";

    /// <summary>The hook group dtk owns inside a shared <c>hooks.json</c>.</summary>
    private const string GroupName = "dtk";

    private const int HookTimeoutSeconds = 10;

    /// <inheritdoc/>
    public string ProviderName => "antigravity";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var configDir = scope == HookScope.Global ? home.AntigravityConfigDir : Path.Combine(directory, ".agents");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(configDir, "hooks.json"),
                HookCommands.OrExitZero(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.AntigravityCli)
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), directory, HookScope.Project,
            force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.GeminiDir, "GEMINI.md"), home.AntigravitySkillsDir, home.Home, HookScope.Global,
            force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string instructionsPath,
        string skillsDirectory,
        string hookDirectory,
        HookScope scope,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(instructionsPath, skillsDirectory, context, cancellationToken)
            .ConfigureAwait(false);

        var hook = DescribeHooks(hookDirectory, scope)[0];
        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(
                hook.RegistrationPath, "PreToolUse", "run_command", hook.Command, HookTimeoutSeconds, ContainerKey: GroupName),
            context, cancellationToken).ConfigureAwait(false);

        if (scope == HookScope.Project
            && (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath)))
        {
            context.Notes.Add(WorkspaceTrustNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(hookDirectory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>Where rtk registers itself for Antigravity: a hooks file, or a plugin's hooks file, in either scope.</summary>
    /// <param name="hookDirectory">The project root.</param>
    private List<string> RtkCandidates(string hookDirectory)
    {
        var configDirectories = new[] { Path.Combine(hookDirectory, ".agents"), home.AntigravityConfigDir };
        var plugins = configDirectories
            .Select(directory => Path.Combine(directory, "plugins"))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateDirectories(directory))
            .Select(plugin => Path.Combine(plugin, "hooks.json"));

        return [.. configDirectories.Select(directory => Path.Combine(directory, "hooks.json")), .. plugins];
    }
}
```

**(G1 fail):** `DescribeHooks` registers `HookCommands.Invocation(ProviderName)` instead, `OrExitZero` is not added,
and the project/global note block adds
`internal const string MissingDtkBlocksNote = "Antigravity blocks a terminal command when its hook cannot run: keep dtk on PATH, or remove the dtk group from hooks.json.";`
whenever the hook file was created or updated, in both scopes. Adjust the tests' expected command and notes.
**(G6 fail):** pass `EnableContainer: true` in the `HookRegistrationSpec`.

Register in `DependencyInjection.cs` after `OpenCodeIntegrator`:
`services.AddTransient<IProviderIntegrator, AntigravityIntegrator>();`

`HookDescriptionTests`: add the integrator to both arrays and `["antigravity"] = "dtk hook antigravity || exit 0"`.
`InitCommandTests` routing theory: `[InlineData("antigravity")]`. `ParityCases.cs`: `"antigravity"` after `"opencode"`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application tests
git commit -m "feat: dtk init antigravity installs the shared instructions and a dtk hook group"
```

---

### Task 6: CLI surface and the sh/cmd hook check

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs`, `Commands/Settings/InitCommandSettings.cs`,
  `CliConfigurator.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`
- Modify: `eng/hooks/check-hook-shells.sh`

- [ ] **Step 1: Extend the completion test and watch it fail**

In `ExecuteAsync_EveryShell_CompletesTheNewHarnessProviders` add `.And.Contain("antigravity")`.
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests"` — Expected: FAIL.

- [ ] **Step 2: Implement the strings**

- bash `init_providers`: `claude copilot copilot-cli gemini codex opencode antigravity cursor windsurf aider jetbrains`
- zsh: `'antigravity:Install dtk hook and instructions for Antigravity CLI'` after opencode
- fish: `complete -c dtk -f -n '__fish_seen_subcommand_from init integrate' -a antigravity -d 'Install dtk hook and instructions for Antigravity CLI'`
- PowerShell `$providers`: `'antigravity'` after `'opencode'`
- `InitCommandSettings`: `antigravity` after `opencode` in both descriptions
- `CliConfigurator`: `.WithExample(InitCommand, "antigravity")` and `.WithExample(InitCommand, "antigravity", "--global")`

Run completion and `CliConfiguratorTests`; accept the InitHelp `.received.txt` after checking its diff. Expected: PASS.

- [ ] **Step 3: The per-shell check**

In `eng/hooks/check-hook-shells.sh`, after the other payload files:

```sh
printf '%s' '{"toolCall":{"name":"run_command","args":{"CommandLine":"dotnet build"}},"stepIdx":1}' > "$work/antigravity.json"
```

after the `powershell_cmd=` line:

```sh
cmd_cmd=$(command -v cmd.exe || true)
```

and before the summary **(G1 pass)**:

```sh
# Antigravity CLI: sh -c on Unix and cmd /c on Windows; it blocks the tool call when a hook fails, hence || exit 0.
check "antigravity, sh" "$work/antigravity.json" rewrite "$with_dtk" sh -c 'dtk hook antigravity || exit 0'
check "antigravity, sh, dtk missing" "$work/antigravity.json" no-rewrite "$without_dtk" sh -c 'dtk hook antigravity || exit 0'
if [ -n "$cmd_cmd" ]; then
    check "antigravity, cmd" "$work/antigravity.json" rewrite "$with_dtk" "$cmd_cmd" /c 'dtk hook antigravity || exit 0'
    check "antigravity, cmd, dtk missing" "$work/antigravity.json" no-rewrite "$without_dtk" "$cmd_cmd" /c 'dtk hook antigravity || exit 0'
fi
```

**(G1 fail):** register and check the bare `dtk hook antigravity` in both shells, with no "dtk missing" lines.

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release` then `sh eng/hooks/check-hook-shells.sh src/DotnetTokenKiller.Cli/bin/Release/net10.0`
Expected: every line `ok` (the `cmd` lines run on Windows CI only).

- [ ] **Step 4: Commit**

```bash
git add src/DotnetTokenKiller.Cli eng/hooks/check-hook-shells.sh tests/DotnetTokenKiller.Cli.IntegrationTests
git commit -m "feat: complete the antigravity provider and check its hook under sh and cmd"
```

---

### Task 7: Documentation

**Files:**
- Modify: `docs/articles/ai-agent-setup.md`, `docs/articles/usage.md`, `docs/index.md`, `README.md`,
  `src/DotnetTokenKiller.Cli/README.md`, `CLAUDE.md`

- [ ] **Step 1: `docs/articles/ai-agent-setup.md`**

*Installing globally*: add `dtk init antigravity --global   # ~/.gemini/config, ~/.gemini/GEMINI.md` and name
**antigravity**. Insert after the OpenCode section (text shown for G1 pass and G3 pass; see below for the other
branches). Replace `<G version>` and `<G OS>` with the `agy` version and OS recorded in `/tmp/agy-gate/RESULTS.md`, and
`<G5 directory>` with the `~/…` form of `HomePaths.AntigravitySkillsDir` from Task 2:

````markdown
## Antigravity CLI

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk` in Google's
Antigravity CLI (`agy`). Checked against Antigravity CLI <G version> on <G OS>.

### Installation

From your project root, run:

```sh
dtk init antigravity
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- `.agents/hooks.json` — a `dtk` hook group running `dtk hook antigravity || exit 0` for `run_command` (other hook groups in the file are left alone)

Antigravity loads a workspace's `.agents/hooks.json` only once you trust the workspace.

`dtk init antigravity --global` writes the hook to `~/.gemini/config/hooks.json`, the skill to
`<G5 directory>/dotnet-token-killer/SKILL.md`, and the instructions section to `~/.gemini/GEMINI.md` — the same
section `dtk init gemini --global` writes, so the two never conflict.

### How It Works

Before Antigravity runs a terminal command, it sends it to `dtk hook antigravity`, which replaces a matching
`dotnet …` command with `dtk dotnet …`. dtk replies `ask`, never `allow`: your permission rules and "Always Allow"
choices still decide whether the command runs. The `|| exit 0` guard keeps a missing `dtk` from blocking terminal
commands, which Antigravity does when a hook fails.

### Manual Installation

Add a `dtk` group to `.agents/hooks.json` (or `~/.gemini/config/hooks.json`):

```json
{
  "dtk": {
    "PreToolUse": [
      {
        "matcher": "run_command",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook antigravity || exit 0",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

````

**(G1 fail):** the bullet and JSON show `dtk hook antigravity`, and *How It Works* ends instead with "Antigravity blocks
a terminal command when its hook cannot run, so keep `dtk` on `PATH` while the hook is installed."
**(G3 fail):** add `### Known issue`: "When a command needs your approval, Antigravity CLI <version> runs the original
command instead of the rewritten one (google-antigravity/antigravity-cli#575). The command still runs; it is just not
filtered."
**(G6 fail):** the JSON group carries `"enabled": true`.

- [ ] **Step 2: The other docs**

- `docs/articles/usage.md`: `dtk init antigravity # Antigravity CLI hook + AGENTS.md section + skill`; name **antigravity** in `--global`.
- `docs/index.md`: `<span class="dtk-agent">Antigravity CLI</span>` after OpenCode.
- `README.md` and packaged README: `11 AI agent integrations — …, Codex CLI, OpenCode, Antigravity CLI, Cursor, …`;
  `README.md` global block `dtk init antigravity --global   # ~/.gemini/config`, supported list, and table row
  `| **Antigravity CLI**    | `dtk init antigravity` | PreToolUse hook group in .agents/hooks.json running dtk hook antigravity, AGENTS.md section, skill |`;
  packaged README names `antigravity` in the `--global` list.
- `CLAUDE.md`, after the opencode paragraph:

```markdown
`dtk init antigravity` writes the shared `AGENTS.md` section and skill plus a `"dtk"` hook group in `.agents/hooks.json`
(`--global`: `~/.gemini/config/hooks.json`, with the section in `~/.gemini/GEMINI.md`). `dtk hook antigravity` replies
`{"decision":"ask","overwrite":{"CommandLine":…}}` — never `allow`, which auto-approves. Hooks run through `sh -c`/`cmd /c`
and a failing hook blocks the command, hence `|| exit 0`. Gate G results are in the PR that added it.
```

- [ ] **Step 3: Run the docs binding tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests"` — Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add docs/articles docs/index.md README.md src/DotnetTokenKiller.Cli/README.md CLAUDE.md
git commit -m "docs: document dtk init antigravity"
```

---

### Task 8: End-to-end check in `agy`, verification, and pull request

- [ ] **Step 1: End to end in Antigravity CLI**

Build this branch (`dtk dotnet build src/DotnetTokenKiller.Cli -c Release`), put its `bin/Release/net10.0` first on
`PATH`, and in `/tmp/agy-gate/ws` run `dotnet <repo>/src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk.dll init antigravity`.
Start `agy`, ask it to run `dotnet build --help`, and approve if prompted. Expected: the command shown and run is
`dtk dotnet build --help` (or, on a G3-fail version, the documented known issue). Then restart `agy` with `dtk` not on
`PATH` and ask again. Expected **(G1 pass)**: the command runs unrewritten, not blocked. Record both in the PR draft.
Run `dtk doctor` in the workspace: `antigravity hook (project)` and its probe pass.

- [ ] **Step 2: Format, build, test**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore` then `--verify-no-changes`
Run: `dtk dotnet build DotnetTokenKiller.slnx`
Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Hook|FullyQualifiedName~InitCommand|FullyQualifiedName~Doctor|FullyQualifiedName~Completion|FullyQualifiedName~CliConfigurator|FullyQualifiedName~DocsBinding|FullyQualifiedName~OpenCodePlugin"`
Expected: no format changes; 0 warnings; all PASS.

- [ ] **Step 3: Push and open the PR — confirm with the user first**

```bash
git push -u origin feat/antigravity-integration
gh pr create --base develop --title "feat: dtk init antigravity and dtk hook antigravity" --body-file <draft>
```

The body links the spec and this plan, carries `/tmp/agy-gate/RESULTS.md` as `## Gate G` plus Step 1's end-to-end
results, and ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Expected: CI green,
including the `cmd` lines of `check-hook-shells.sh` on Windows.

# Kilo Code and Amp rewrite plugins (PR 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `dtk init kilo` (Kilo Code, an OpenCode fork: generated JS plugin) and `dtk init amp` (Amp: generated
JS plugin on its `tool.call` event), each with project and `--global` scope, `--uninstall`, doctor support and docs,
plus a live gate proving the Kilo rewrite with the real `kilo` CLI.

**Architecture:** Both plugins are generated, stamped JS files over the existing `PluginRuntime` (PATH lookup, spawn
of `dtk hook <provider>`, `{command}` reply). Kilo loads OpenCode's plugin format unchanged, so `OpenCodePlugin`'s
body is parameterized by provider (its own bytes pinned and unchanged) and reused for Kilo. Amp gets its own body
around `amp.on("tool.call", …)`. `dtk hook kilo|amp` reuse `ReplyToOpenCode`.

**Tech Stack:** .NET 10, C#, xunit + FluentAssertions; Node for the plugin tests; the `@kilocode/cli` npm package and
`eng/gates/mock-openai.mjs` for the gate.

**Spec:** `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md` (sections *Research summary: Kilo Code, Amp*,
*Shared*, *PR 3a*, *PR 3b*, *Verification*). See *Deviations from the spec*.

## Deviations from the spec (rulings, recorded here and amended in the spec by Task 9)

1. **Kilo plugin shape.** The spec says Kilo needs `export default { id, server }`. Kilo's loader
   (Kilo-Org/kilocode `packages/opencode/src/plugin/index.ts`, commit 7d977bc, 2026-09-26) accepts that *or* OpenCode's
   named function exports (`getLegacyPlugins`), and discovers `{plugin,plugins}/*.{ts,js}` in `.kilo/`, `.kilocode/`,
   the global config dir and `$KILO_CONFIG_DIR`. The shell tool id is `bash` (`tool/shell/id.ts`), and
   `tool.execute.before` fires before the tool's own permission check (`session/tools.ts`). So Kilo reuses OpenCode's
   plugin body byte for byte except the provider name and harness label: `.kilo/plugin/dtk.js`.
2. **Kilo paths, verified in source:** global config dir `$XDG_CONFIG_HOME/kilo` or `~/.config/kilo` (`xdg-basedir`,
   `packages/core/src/global.ts`); `$KILO_CONFIG_DIR` is an extra config dir and wins for global `AGENTS.md`
   (`session/instruction.ts`); Kilo scans `.agents/skills` (`skill/index.ts`), so the skill is kept.
3. **Amp's neutral result.** Amp's Plugin API reference (ampcode.com/docs/markdown/plugin-api, 2026-09-27): "For
   request events (e.g., tool.call), the handler must return a result." So the plugin returns `{ action: "allow" }`
   whenever it does not rewrite — never `undefined`.
4. **No rtk coexistence** for Kilo or Amp: rtk ships no plugin for either (its Amp integration was never merged).

## Global Constraints

- Branch: `feat/kilo-amp-plugins` (off `develop` at 7536777). One PR against `develop`; the repo squash-merges.
- `TreatWarningsAsErrors`: XML doc comments on every member; file-scoped namespaces, `var`, `_camelCase` fields, LF,
  no BOM. Analyzer gotchas: inline array arguments trip CA1861 (use static readonly fields), array literals passed
  to FluentAssertions trip S3878 (use params), private helpers need full `<param>` docs (RCS1141).
- Build: `dtk dotnet build DotnetTokenKiller.slnx`. Tests: `dtk dotnet test tests/<Project> --filter "FullyQualifiedName~<Class>"`.
  The CLI integration project is slow: run only named classes. Node-based tests use `[NodeFact]`/`[NodeUnixFact]`.
- `OpenCodePlugin.Body` must stay byte for byte (`OpenCodeIntegratorTests.PluginBody_IsByteForByteTheReleasedPlugin`
  pins its SHA-256 `7b0cbc214a049aff85dd0de73c4a1296f9723080d9f2ce1768e4d91dfd8cf521`).
- Kilo plugin: `.kilo/plugin/dtk.js` (project), `<kilo config dir>/plugin/dtk.js` (global), spawning `dtk hook kilo`.
- Amp plugin: `.amp/plugins/dtk.js` (project), `$XDG_CONFIG_HOME/amp/plugins/dtk.js` or `~/.config/amp/plugins/dtk.js`
  (global, Windows too), spawning `dtk hook amp`; it rewrites by returning `{ action: "modify", input: {...} }` and
  otherwise always returns `{ action: "allow" }`; it never throws.
- `dtk hook kilo` and `dtk hook amp` reply exactly like `dtk hook opencode`: `{"command":"<rewritten>"}` or nothing.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Existing OpenCode installs must not go stale: `OpenCodePlugin.Body` bytes unchanged after the refactor. Task 3.
2. An Amp plugin that cannot find dtk, gets a malformed reply, times out, or sees a non-shell tool returns
   `{ action: "allow" }` and never throws (a throw or an `error` result would stop Amp's thread). Task 4.
3. The Amp plugin writes the rewritten command back into whichever input field held it (`cmd` or `command`), never a
   new field. Task 4.
4. Uninstalling Kilo or Amp keeps the shared `AGENTS.md`/skill while another provider's dtk hook still uses them. Tasks 5-6.
5. Doctor probes both new hooks and reports them registered. Tasks 1, 5, 6.

---

### Task 1: `dtk hook kilo` and `dtk hook amp`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum), `Hooks/HookPayloads.cs`,
  `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`BuildPayload`), `src/DotnetTokenKiller.Cli/HookEntryPoint.cs` (`Usage`)
- Test: `HookPayloadsTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `HookIntegrationTests.cs`

**Interfaces:** Produces `HookPayloadKind.Kilo` (= 12), `HookPayloadKind.Amp` (= 13); `TryGetKind("kilo"|"amp")`.

- [ ] **Step 1: Failing tests.** In `HookPayloadsTests`: add `[InlineData("kilo", HookPayloadKind.Kilo)]` and
  `[InlineData("amp", HookPayloadKind.Amp)]` to `TryGetKind_KnownProvider_Resolves`, and add both kinds to the existing
  `PiFamily_UsesTheOpenCodeContract` theory (`[InlineData(HookPayloadKind.Kilo)]`, `[InlineData(HookPayloadKind.Amp)]`)
  — rename it `PluginHarnesses_UseTheOpenCodeContract`. In the two CLI test files, the usage text becomes
  `…|cursor|devin|droid|crush|kilo|amp>`.
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"` → build error.
- [ ] **Step 3: Implement.**
  - Enum, after `Crush = 11`:

    ```csharp
        /// <summary>dtk's own Kilo Code plugin payload, the same as <see cref="OpenCode"/>'s.</summary>
        Kilo = 12,

        /// <summary>dtk's own Amp plugin payload, the same as <see cref="OpenCode"/>'s.</summary>
        Amp = 13
    ```
  - `TryGetKind`: `"kilo" => (true, HookPayloadKind.Kilo),`, `"amp" => (true, HookPayloadKind.Amp),` (and the summary list).
  - Dispatch: `HookPayloadKind.OpenCode or HookPayloadKind.Pi or HookPayloadKind.OhMyPi or HookPayloadKind.Kilo or HookPayloadKind.Amp => ReplyToOpenCode(root),`
    and update `ReplyToOpenCode`'s summary to name all five plugin harnesses.
  - `HookHealthChecker.BuildPayload`: the same five kinds build `{ "command": … }`.
  - `HookEntryPoint.Usage`: `…|droid|crush|kilo|amp>`.
- [ ] **Step 4:** Run the Application filter above plus
  `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`, then build.
- [ ] **Step 5: Commit** — `feat: dtk hook kilo and dtk hook amp`.

---

### Task 2: Home paths for Kilo Code and Amp

**Files:** Modify `HomePaths.cs`; test `HomePathsTests.cs`.
**Interfaces:** Produces `HomePaths.KiloConfigDir`, `HomePaths.AmpConfigDir` (`string`).

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public void KiloConfigDir_PrefersKiloConfigDir_ThenXdgConfigHome_ThenDotConfig()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var profile = Path.Combine(Path.GetTempPath(), "kilo-profile");
        var xdg = Path.Combine(Path.GetTempPath(), "xdg");

        new HomePaths(home).KiloConfigDir.Should().Be(Path.Combine(home, ".config", "kilo"));
        new HomePaths(home, name => name == "XDG_CONFIG_HOME" ? xdg : null).KiloConfigDir.Should().Be(Path.Combine(xdg, "kilo"));
        new HomePaths(home, name => name switch { "KILO_CONFIG_DIR" => profile, "XDG_CONFIG_HOME" => xdg, _ => null })
            .KiloConfigDir.Should().Be(profile, "KILO_CONFIG_DIR is the directory itself");
    }

    [Fact]
    public void AmpConfigDir_UsesXdgConfigHome_ElseDotConfig()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var xdg = Path.Combine(Path.GetTempPath(), "xdg");

        new HomePaths(home).AmpConfigDir.Should().Be(Path.Combine(home, ".config", "amp"));
        new HomePaths(home, name => name == "XDG_CONFIG_HOME" ? xdg : null).AmpConfigDir.Should().Be(Path.Combine(xdg, "amp"));
    }
```

- [ ] **Step 2:** `--filter "FullyQualifiedName~HomePathsTests"` → build error.
- [ ] **Step 3: Implement** (after `CrushConfigDir`):

```csharp
    /// <summary>
    /// Gets Kilo Code's global config directory: <c>$KILO_CONFIG_DIR</c> when it is an absolute path (Kilo reads it as an
    /// extra config directory and prefers it for the global <c>AGENTS.md</c>), else <c>$XDG_CONFIG_HOME/kilo</c> when that
    /// is absolute, else <c>~/.config/kilo</c> — on Windows too.
    /// </summary>
    internal string KiloConfigDir => RootedOrDefault(
        "KILO_CONFIG_DIR", Path.Combine(RootedOrDefault("XDG_CONFIG_HOME", Path.Combine(Home, ".config")), "kilo"));

    /// <summary>
    /// Gets Amp's user config directory: <c>$XDG_CONFIG_HOME/amp</c> when that variable is an absolute path, else
    /// <c>~/.config/amp</c> — on Windows too (<c>%USERPROFILE%\.config\amp</c>).
    /// </summary>
    internal string AmpConfigDir =>
        Path.Combine(RootedOrDefault("XDG_CONFIG_HOME", Path.Combine(Home, ".config")), "amp");
```

- [ ] **Step 4:** Run the tests. **Step 5: Commit** — `feat: home paths for Kilo Code and Amp`.

---

### Task 3: `KiloPlugin` — OpenCode's plugin body for Kilo

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/OpenCodePlugin.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/KiloPlugin.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/KiloPluginTests.cs` (C# content tests)
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/KiloPluginRuntimeTests.cs` (runs it under Node)

**Interfaces:** Produces `OpenCodePlugin.BodyFor(string provider, string harness) : string`, `KiloPlugin.Body`,
`KiloPlugin.Artifact(string path) : GeneratedArtifact`, `KiloPlugin.InvocationSignature`.

- [ ] **Step 1: Failing tests.** `KiloPluginTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class KiloPluginTests
{
    [Fact]
    public void Body_IsOpenCodesBodyWithKilosNames()
    {
        KiloPlugin.Body.Should().Be(OpenCodePlugin.Body
            .Replace("`dtk init opencode`", "`dtk init kilo`", StringComparison.Ordinal)
            .Replace("[\"hook\", \"opencode\"]", "[\"hook\", \"kilo\"]", StringComparison.Ordinal)
            .Replace("OpenCode", "Kilo Code", StringComparison.Ordinal));
    }

    [Fact]
    public void Body_SpawnsDtkHookKiloAndExportsANamedPluginFunction()
    {
        KiloPlugin.Body.Should().Contain("[\"hook\", \"kilo\"]").And.Contain("export const DtkPlugin = async");
        KiloPlugin.InvocationSignature.Should().Be("[\"hook\", \"kilo\"]");
    }

    [Fact]
    public void OpenCodeBody_IsBodyForOpenCode()
    {
        OpenCodePlugin.BodyFor("opencode", "OpenCode").Should().Be(OpenCodePlugin.Body);
    }
}
```

(Read `OpenCodePlugin.Body` first: if the harness name appears in a form the three `Replace` calls miss, adjust the
expected value so the test states exactly which strings differ — no more.)

`KiloPluginRuntimeTests.cs`: copy `OpenCodePluginTests` (same `NodeDriver` usage and the same six tests), changing
only: the plugin is written to `.kilo/plugin/dtk.js` from `KiloPlugin.Body`; no `package.json` is written; the
driver imports `./.kilo/plugin/dtk.js`; the class summary names Kilo Code.

- [ ] **Step 2:** `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~KiloPluginTests|FullyQualifiedName~OpenCodeIntegratorTests"` → build error.
- [ ] **Step 3: Implement.** In `OpenCodePlugin.cs`, turn the body into
  `internal static string BodyFor(string provider, string harness)` (the same raw string, with `opencode`/`OpenCode`
  replaced by `{{provider}}`/`{{harness}}` where they name the provider or harness, including the
  `PluginRuntime.Source(provider, harness)` call), and keep
  `internal static readonly string Body = BodyFor("opencode", "OpenCode");`. Update the remarks to say Kilo Code reuses
  it. `KiloPlugin.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>The plugin <c>dtk init kilo</c> generates.</summary>
/// <remarks>
/// Kilo Code is an OpenCode fork whose loader still accepts OpenCode's named function exports and fires
/// <c>tool.execute.before</c> for its <c>bash</c> tool before the tool's own permission check, so the plugin is
/// <see cref="OpenCodePlugin"/>'s body with Kilo's provider name; see there for why each line is there.
/// </remarks>
internal static class KiloPlugin
{
    /// <summary>The call a plugin still runs dtk through, even after local edits.</summary>
    internal static readonly string InvocationSignature = PluginRuntime.InvocationSignature("kilo");

    /// <summary>The plugin source, before stamping.</summary>
    internal static readonly string Body = OpenCodePlugin.BodyFor("kilo", "Kilo Code");

    /// <summary>The plugin as a generated artifact at <paramref name="path"/>.</summary>
    /// <param name="path">Where the plugin is written.</param>
    internal static GeneratedArtifact Artifact(string path) =>
        // No dtk ever wrote an unstamped copy; the stamp prefix as legacy signature keeps the legacy branch
        // unreachable, exactly as for OpenCodePlugin.Artifact.
        new(path, Body, StampStyle.SlashComment, ArtifactStamping.StampPrefix);
}
```

- [ ] **Step 4:** Run the Application filter (the OpenCode pin test must still pass), then
  `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~KiloPluginRuntimeTests|FullyQualifiedName~OpenCodePluginTests"`, then build.
- [ ] **Step 5: Commit** — `feat: the Kilo Code plugin reuses OpenCode's plugin body`.

---

### Task 4: `AmpPlugin`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/AmpPlugin.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/AmpPluginTests.cs`

**Interfaces:** Produces `AmpPlugin.Body`, `AmpPlugin.Artifact(string path)`, `AmpPlugin.InvocationSignature`.

- [ ] **Step 1: Failing tests** (`AmpPluginTests.cs`, Node-driven like `OpenCodePluginTests`). Write the stamped body to
  `.amp/plugins/dtk.js` and a `driver.mjs` that fakes Amp's API as documented (the `helpers.shellCommandFromToolCall`
  stub returns `{ command }` for tools `Bash`/`shell_command` from `input.cmd ?? input.command`, else `null`):

```js
import plugin from "./.amp/plugins/dtk.js";
const [tool, field, command] = process.argv.slice(2);
let handler;
const amp = {
  on(event, fn) { if (event === "tool.call") handler = fn; return { dispose() {} }; },
  helpers: {
    shellCommandFromToolCall(call) {
      if (call.tool !== "Bash" && call.tool !== "shell_command") return null;
      const value = call.input.cmd ?? call.input.command;
      return typeof value === "string" ? { command: value } : null;
    },
  },
};
await plugin(amp);
const input = { [field]: command, other: "o" };
const result = await handler({ toolUseID: "t", tool, input, thread: { id: "T" } }, {});
process.stdout.write(JSON.stringify({ result, input }));
```

  Tests (each asserts the JSON the driver prints; `NodeDriver.RunAsync` takes the argv and the PATH to use — adapt the
  call to its actual signature):
  1. `[NodeFact] Bash_DotnetCommandInCmd_IsModifiedByTheRealDtk` — `Bash cmd "dotnet build"` with the real dtk →
     `result` is `{"action":"modify","input":{"cmd":"dtk dotnet build","other":"o"}}`, and the original `input`
     object was not mutated.
  2. `[NodeFact] ShellCommandInCommandField_IsModifiedInThatField` — `shell_command command "dotnet test"` → modify with
     `input.command == "dtk dotnet test"` and no `cmd` key.
  3. `[NodeFact] DtkMissingFromPath_Allows` — empty PATH dir → `{"action":"allow"}`.
  4. `[NodeUnixFact] OtherToolOrNoDotnet_AllowsWithoutStartingDtk` — `Read cmd "dotnet build"` and `Bash cmd "ls"` →
     allow, and a fake dtk that touches a marker was never started.
  5. `[NodeUnixFact] MalformedReply_Allows` — fake dtk printing `not json` → allow.
  6. `[NodeUnixFact] HangingDtk_TimesOutAndAllows` — fake dtk `exec sleep 60` → allow within 30 s.
  7. `[NodeFact] DtkOnlyInTheWorkingDirectory_IsNeverRun` — as the OpenCode test of the same name → allow.
  8. `[NodeUnixFact] UnchangedReply_Allows` — fake dtk echoing `{"command":"dotnet build"}` → allow (no modify for an identical command).

- [ ] **Step 2:** `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~AmpPluginTests"` → build error.
- [ ] **Step 3: Implement** `AmpPlugin.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>The plugin <c>dtk init amp</c> generates.</summary>
/// <remarks>
/// <para>
/// Amp loads <c>export default function (amp)</c> plugins from <c>.amp/plugins/</c> and the user's config directory and
/// fires <c>tool.call</c> before a tool runs. For a shell call (Amp's own <c>shellCommandFromToolCall</c> helper decides
/// which calls those are) whose command contains <c>dotnet</c>, the plugin asks <c>dtk hook amp</c> through
/// <see cref="PluginRuntime"/> and returns <c>{ action: "modify" }</c> with the rewritten command in whichever input
/// field held it (<c>cmd</c> or <c>command</c>), so the tool's other arguments are untouched.
/// </para>
/// <para>
/// Amp requires every <c>tool.call</c> handler to return a result, and a throw or an <c>error</c> result stops the
/// thread, so every other path — another tool, no dotnet, no dtk, a slow or malformed reply — returns
/// <c>{ action: "allow" }</c>.
/// </para>
/// </remarks>
internal static class AmpPlugin
{
    /// <summary>The call a plugin still runs dtk through, even after local edits.</summary>
    internal static readonly string InvocationSignature = PluginRuntime.InvocationSignature("amp");

    /// <summary>The plugin source, before stamping.</summary>
    internal static readonly string Body =
        $$"""
        // dtk (DotnetTokenKiller) rewrites the dotnet commands dtk supports to `dtk dotnet ...` before Amp runs them.
        // Generated by `dtk init amp`; run it again to refresh this file.
        {{PluginRuntime.Source("amp", "Amp")}}
        const ALLOW = { action: "allow" };

        export default function (amp) {
          amp.on("tool.call", async (event) => {
            try {
              const input = event?.input;
              const command = amp?.helpers?.shellCommandFromToolCall?.(event)?.command;
              if (typeof command !== "string" || !command.includes("dotnet") || input === null || typeof input !== "object") return ALLOW;
              const field = ["cmd", "command"].find((key) => input[key] === command);
              if (field === undefined) return ALLOW;
              const rewritten = await rewrite(command);
              if (typeof rewritten !== "string" || rewritten === "" || rewritten === command) return ALLOW;
              return { action: "modify", input: { ...input, [field]: rewritten } };
            } catch {
              // A throw would stop Amp's thread.
              return ALLOW;
            }
          });
        }

        """;

    /// <summary>The plugin as a generated artifact at <paramref name="path"/>.</summary>
    /// <param name="path">Where the plugin is written.</param>
    internal static GeneratedArtifact Artifact(string path) =>
        new(path, Body, StampStyle.SlashComment, ArtifactStamping.StampPrefix);
}
```

- [ ] **Step 4:** Run the filter above, then build.
- [ ] **Step 5: Commit** — `feat: the Amp plugin rewrites through tool.call`.

---

### Task 5: `dtk init kilo`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/KiloIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` (after `CrushIntegrator`)
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/KiloIntegratorTests.cs`
- Modify: `UninstallIntegrationTests.cs` (`new KiloIntegrator(home)`; `"kilo"` in `AllProviders`, `GlobalProviders`, `hookProviders`)

**Interfaces:** Consumes `KiloPlugin` (Task 3), `HomePaths.KiloConfigDir` (Task 2), `HookPayloadKind.Kilo` (Task 1).
Produces `internal sealed class KiloIntegrator(HomePaths home)` (`ProviderName == "kilo"`), note `KiloIntegrator.ReloadNote`.

Mirror `OpenCodeIntegrator` without the rtk parts:
- Project: `AGENTS.md` + `.agents/skills` (`SharedInstructionArtifacts.WriteAgentsFilesAsync`), and
  `KiloPlugin.Artifact(<dir>/.kilo/plugin/dtk.js)` via `IntegratorHelpers.WriteGeneratedFileAsync`.
- Global: `<KiloConfigDir>/AGENTS.md`, `~/.agents/skills` (`home.AgentsSkillsDir`), `<KiloConfigDir>/plugin/dtk.js`.
- `DescribeHooks` → one `HookInstallation(ProviderName, scope, pluginPath, HookCommands.Invocation("kilo"), null, HookPayloadKind.Kilo, KiloPlugin.Artifact(pluginPath))`.
- `SharedArtifactPaths` → `[instructions, SkillPath(skills)]`; uninstall → `RemoveAgentsFilesAsync` + `RemoveGeneratedFileAsync(artifact)`.
- `ReloadNote` (added when the plugin file is created or updated): `"Kilo Code loads plugins when it starts: restart Kilo (or the VS Code extension) for the rewrite to take effect."`

Tests (`KiloIntegratorTests`, modelled on `OpenCodeIntegratorTests`): fresh project creates `AGENTS.md`, skill and
`.kilo/plugin/dtk.js` (stamped body, `ArtifactStamping.IsAuthentic`), note equals `ReloadNote`; second run unchanged
and no note; global honours `KILO_CONFIG_DIR` and `XDG_CONFIG_HOME`; `DescribeHooks` path/command/kind/artifact in
both scopes; uninstall removes the plugin and prunes `.kilo`; an edited plugin is kept on uninstall with a note.

- [ ] Steps: failing tests → build error → implement → `--filter "FullyQualifiedName~KiloIntegratorTests|FullyQualifiedName~UninstallIntegrationTests|FullyQualifiedName~HookHealthCheckerTests"` → build →
  commit `feat: dtk init kilo installs a Kilo Code rewrite plugin`.

---

### Task 6: `dtk init amp`

Same shape as Task 5.

**Files:** Create `AmpIntegrator.cs`, `AmpIntegratorTests.cs`; modify `DependencyInjection.cs` (after `KiloIntegrator`),
`UninstallIntegrationTests.cs` (`new AmpIntegrator(home)`; `"amp"` in the three lists).

- Project: `AGENTS.md` + `.agents/skills`, and `AmpPlugin.Artifact(<dir>/.amp/plugins/dtk.js)`.
- Global: `<AmpConfigDir>/AGENTS.md`, `~/.agents/skills`, `<AmpConfigDir>/plugins/dtk.js`.
- `DescribeHooks` with `HookPayloadKind.Amp` and `AmpPlugin.Artifact(path)`.
- Notes when the plugin is created or updated: `ReloadNote` = `"Amp loads plugins when it starts: run 'plugins: reload' from Amp's command palette, or restart Amp."`;
  project scope also `ProjectPluginNote` = `"Amp runs project plugins without asking, so everyone who opens this project with Amp runs dtk's plugin (it only rewrites dotnet commands)."`

Tests mirror Task 5's (paths, notes, `XDG_CONFIG_HOME`, uninstall, edited plugin kept).

- [ ] Steps as in Task 5; commit `feat: dtk init amp installs an Amp rewrite plugin`.

---

### Task 7: CLI surface for `kilo` and `amp`

Exactly as PR 2's Task 8 did for `droid`/`crush`: provider description and `--global` description in
`InitCommandSettings.cs`, the four completion shells in `CompletionCommand.cs` (descriptions
`'kilo:Install dtk instructions and rewrite plugin for Kilo Code'`, `'amp:Install dtk instructions and rewrite plugin for Amp'`),
`CliConfigurator` examples (`kilo`, `kilo --global`, `amp`, `amp --global`), `InitCommandTests` routing theory,
`ParityCases.InitProviders` — each right after `crush`. Regenerate the init-help Verify snapshot from `.received.txt`,
check the diff is only these additions, strip any BOM (`head -c 3 <file> | od -An -tx1` must not print `ef bb bf`),
delete the `.received.txt`. Run `--filter "FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~DocsBindingTests"`, build,
commit `feat: kilo and amp in the init CLI surface`.

---

### Task 8: Gate K — the real `kilo` CLI rewrites through `dtk hook kilo`

**Files:** Create `eng/gates/kilo-gate.sh`; modify `eng/gates/README.md` (add a Kilo section and results table).

Reuse `eng/gates/mock-openai.mjs` unchanged if it fits (it picks the tool named `bash`; Kilo's shell tool is `bash`).
`kilo-gate.sh <kilo-version> [dtk-binary]`, in the style of `crush-gate.sh` (`set -eu`, scratch `HOME`/`XDG_*`,
`trap` cleanup, `timeout` on the run, fake `dotnet` and `dtk` wrapper logging to `$GATE_LOG`, real dtk for `dtk hook …`):
1. `npm install --prefix <scratch> @kilocode/cli@<version>`; find the `kilo` bin.
2. Scratch project (a git repo), `dtk init kilo --dir <project>`, then a project `kilo.json` configuring a custom
   OpenAI-compatible provider at the mock (read the installed package's docs/schema, or
   `packages/opencode/src/config/*.ts` in /tmp/kilo-src, for the exact keys — OpenCode's are
   `{"provider":{"mock":{"npm":"@ai-sdk/openai-compatible","options":{"baseURL":…,"apiKey":"x"},"models":{"mock":{}}}},"model":"mock/mock","permission":{"bash":"allow"}}`),
   with bash auto-approved in the scratch project only. Disable telemetry/auto-update/network extras via the env
   variables Kilo documents, if any.
3. Run `kilo run "build the project"` (non-interactive) with a 180 s timeout.
4. **Pass** when `$GATE_LOG` has the line `dtk dotnet build` and no bare `dotnet build`; **control run**: after
   `dtk init kilo --dir <project> --uninstall`, the log must show a bare `dotnet build`. Exit non-zero unless both hold.

Run it on `@kilocode/cli` latest (7.8.1 on 2026-09-27) and on the first 7.x release (plugins shipped with v7; find
it with `npm view @kilocode/cli versions`). Record each run (version, date, PASS/FAIL, control) in the README; if the
first 7.x fails for a reason unrelated to dtk, fix the harness; if it fails because plugins didn't load, find the
first passing release and state it as the minimum in the README (the docs task uses it). If `@ai-sdk/openai-compatible`
must be fetched at run time, allow it (network is available) and say so. If no version passes, change no product code,
record the output verbatim and report BLOCKED. Commit `test: gate K runs the real kilo CLI through dtk hook kilo`.

---

### Task 9: Docs, verification checklist and spec amendment

**Files:** `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docs/articles/ai-agent-setup.md`, `docs/articles/usage.md`,
`docs/articles/harness-verification.md`, `CLAUDE.md`, `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md`.

- README and CLI README: feature line gains `Kilo Code, Amp`; the integration count becomes 17 (count the
  `AddTransient<IProviderIntegrator, …>` registrations to confirm); provider table rows
  `| **Kilo Code** | `dtk init kilo` | `AGENTS.md`, `.agents/skills/…`, `.kilo/plugin/dtk.js` (`--global`: `~/.config/kilo/`) |` and
  `| **Amp** | `dtk init amp` | `AGENTS.md`, `.agents/skills/…`, `.amp/plugins/dtk.js` (`--global`: `~/.config/amp/`) |`
  (match alignment); every `--global` provider list and example block gains both.
- `usage.md`: the same list/example additions.
- `ai-agent-setup.md`: `## Kilo Code` and `## Amp` sections modelled on `## OpenCode`: files per scope, the plugin's
  behaviour (Kilo: OpenCode's plugin, rewrite before Kilo's permission check; Amp: `tool.call` → `modify` or `allow`,
  never throws, field-preserving), env vars (`KILO_CONFIG_DIR`, `XDG_CONFIG_HOME`), the reload/restart notes, Amp's
  project plugins running without a prompt. Kilo: "Verified against the real `kilo` CLI (gate K, `eng/gates/README.md`)"
  with the minimum version gate K found. Amp: "Not verified against a live run — see
  [Harness verification](harness-verification.md)". Add both to the "Installing globally" block.
- `harness-verification.md`: `## Amp` — (1) `dotnet build` becomes `dtk dotnet build` in `amp`; (2) the shell tool's
  input field (`cmd` or `command`); (3) a `{ action: "allow" }` from dtk's plugin does not override another plugin's
  `reject-and-continue` for the same call; (4) the plugin spawns dtk under Amp's Bun runtime on Windows.
- CLAUDE.md, after the Droid/Crush paragraph:

```markdown
`dtk init kilo` writes the shared `AGENTS.md` section and skill plus `.kilo/plugin/dtk.js` (`--global`:
`$KILO_CONFIG_DIR`, `$XDG_CONFIG_HOME/kilo` or `~/.config/kilo`). Kilo Code is an OpenCode fork whose loader accepts
OpenCode's plugin format, so `KiloPlugin.Body` is `OpenCodePlugin.BodyFor("kilo", "Kilo Code")`; `OpenCodePlugin.Body`
itself stays byte for byte (a SHA-256 pin). `dtk init amp` writes the section and skill plus `.amp/plugins/dtk.js`
(`--global`: `$XDG_CONFIG_HOME/amp` or `~/.config/amp`): an `amp.on("tool.call")` handler that returns
`{ action: "modify" }` with the rewritten command in the field that held it, and otherwise `{ action: "allow" }` —
Amp requires a result, and a throw stops its thread. `dtk hook kilo|amp` reply like `dtk hook opencode`.
`eng/gates/kilo-gate.sh` runs the real `kilo` CLI against the mock model.
```

- Spec: *Research summary: Kilo Code* — replace the export-shape and Windows-path claims with the verified facts
  (deviations 1-2); *Amp* — the handler must return a result (deviation 3); *PR 3a/3b* — the reused OpenCode body,
  `{ action: "allow" }` instead of `undefined`, no rtk (deviation 4); *Live gates* — gate K's result; `Status:` line
  `approved; PR 1 merged (#168), PR 2 merged (#169), PR 3 implemented`.
- Verify: build; `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`;
  `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests|FullyQualifiedName~ExamplesBindingTests|FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~SavingsBaselineTests"`;
  format verify. Commit `docs: Kilo Code and Amp rewrite plugins`.

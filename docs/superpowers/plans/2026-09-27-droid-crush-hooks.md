# Factory Droid and Crush rewrite hooks (PR 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `dtk init droid` (Factory Droid `PreToolUse` hook, Claude format) and `dtk init crush` (Crush `PreToolUse`
hook registered in a marked `crushrc` section), each with project and `--global` scope, `--uninstall`, doctor support
and docs, plus a live gate proving the Crush rewrite with the real `crush` binary.

**Architecture:** Two new `HookPayloadKind`s answered by `dtk hook droid|crush`: Droid reuses PR 1's
`ReplyWithUpdatedInput` with tool `Execute`; Crush gets its own envelope (`{"version":1,"updated_input":{…}}`, never a
decision). Droid's hook file is chosen by `FactoryDroidHooks.ResolveTarget` (rtk's verified four-step rule) and written
with the existing JSON registration helpers. Crush's `crushrc` is a Bash script, so `CrushrcFile` edits only a marked
section, and doctor learns to read a non-JSON registration by text.

**Tech Stack:** .NET 10, C#, System.Text.Json `JsonNode`, xunit + FluentAssertions + NSubstitute; Node (mock model
server) and the released `crush` binary for the gate.

**Spec:** `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md` (sections *Research summary: Factory Droid,
Crush*, *Shared*, *PR 2a*, *PR 2b*, *Verification*). This plan refines PR 2a's file rule and gate C's setup; see
*Deviations from the spec*.

## Deviations from the spec (rulings, recorded here and amended in the spec by Task 10)

1. **Droid's hook file.** The spec says: write `hooks.json` unless it is absent and `settings.json` has a `hooks` key.
   Droid actually merges `hooks.json` over `settings.json`'s `hooks` **per event key**, with a legacy
   `hooks/hooks.json` read only when the root file is absent (rtk `src/hooks/init/droid.rs`, verified on Droid
   v0.164.0). dtk uses the same four-step rule: (1) the live `hooks.json` when it already defines a non-empty
   `PreToolUse`; (2) else `settings.json` when its `hooks.PreToolUse` is non-empty; (3) else the live `hooks.json`
   when one exists; (4) else create `hooks.json`. Uninstall removes dtk's entry from all three files.
2. **Droid home.** `$FACTORY_HOME_OVERRIDE` replaces the home directory and `.factory` is still appended (same rtk
   source).
3. **Crush global path, verified in source** (charmbracelet/crush `internal/config/load.go`, `internal/home`, commit
   68d768c, 2026-09-26): the global `crushrc` and `CRUSH.md` live in `$CRUSH_GLOBAL_CONFIG` when set, else
   `$XDG_CONFIG_HOME/crush`, else `~/.config/crush` — on Windows too (`%USERPROFILE%\.config\crush`).
4. **Gate C** runs the released `crush` Linux binary directly under a scratch `HOME`, not in Docker: it is a single
   static binary, and a scratch `HOME` isolates it as well.
5. **Doctor** reads the Crush registration as text (it is a script, not JSON): `HookInstallation.IsScriptRegistration`.

## Global Constraints

- Branch: `feat/droid-crush-hooks` (off `develop` at 964dc6a). One PR against `develop`; the repo squash-merges.
- `TreatWarningsAsErrors` is on: every new member needs an XML doc comment (`///`), matching surrounding code.
  File-scoped namespaces, `var`, `_camelCase` private fields, `Async` suffix, LF endings, no BOM, 4-space indent.
- Build: `dtk dotnet build DotnetTokenKiller.slnx`. Tests: `dtk dotnet test tests/<Project> --filter "FullyQualifiedName~<Class>"`.
  The CLI integration project is slow: run only named classes.
- Hook timeout registered for both harnesses: `10` seconds.
- Droid hook entry, exactly: `{"matcher":"Execute","hooks":[{"type":"command","command":"dtk hook droid","timeout":10}]}`
  under `PreToolUse` — at the root of `hooks.json`, or under `hooks` in `settings.json`.
- Droid reply: Claude's `hookSpecificOutput.updatedInput` (whole tool input, command replaced), no decision; nothing
  printed when there is nothing to rewrite.
- Crush section, exactly (LF, or CRLF when the existing file uses CRLF):

  ```sh
  # >>> dtk (DotnetTokenKiller) >>>
  hook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'
  # <<< dtk <<<
  ```
- Crush reply: `{"version":1,"updated_input":{"command":"<rewritten>"}}`; never a `decision` (Crush's `allow`
  bypasses its permission prompt); nothing printed when there is nothing to rewrite.
- A damaged Crush section (a missing, extra or out-of-order marker) makes install and uninstall throw
  `InvalidOperationException` naming the file, and leaves it untouched.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A user's `~/.factory/settings.json` that already runs their own `PreToolUse` hooks keeps working: dtk merges into
   `settings.json`, never creates a `hooks.json` that would shadow it. Task 3/4.
2. A user's hand-written `crushrc` (providers, other hooks, CRLF endings) survives install → uninstall byte for byte.
   Task 5.
3. A damaged dtk section in `crushrc` is never guessed at: install and uninstall both refuse with the file named. Task 5.
4. `dtk doctor` reports a healthy Crush install as registered and probes it, instead of "could not be read as JSON". Task 6.
5. Uninstalling Droid removes dtk's entry wherever an older dtk or the user moved it (`hooks.json`, legacy
   `hooks/hooks.json`, `settings.json`), and leaves every other hook alone. Task 4.

---

### Task 1: `dtk hook droid` and `dtk hook crush` replies

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum `HookPayloadKind`)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Modify: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs` (`Usage`)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs`

**Interfaces:**
- Produces: `HookPayloadKind.FactoryDroid` (= 10), `HookPayloadKind.Crush` (= 11); `TryGetKind("droid"|"crush")`.

- [ ] **Step 1: Write the failing tests** — add to `TryGetKind_KnownProvider_Resolves`:

```csharp
    [InlineData("droid", HookPayloadKind.FactoryDroid)]
    [InlineData("crush", HookPayloadKind.Crush)]
```

and append:

```csharp
    // Payload and reply shapes: docs.factory.ai/reference/hooks-reference (PreToolUse), checked 2026-09-27.

    [Fact]
    public void Droid_ExecuteTool_ReturnsClaudesReplyWithoutADecision()
    {
        var reply = Reply(HookPayloadKind.FactoryDroid,
            """{"hook_event_name":"PreToolUse","tool_name":"Execute","tool_input":{"command":"dotnet build","timeout":60},"permission_mode":"auto-low"}""");

        var output = JsonNode.Parse(reply!)!["hookSpecificOutput"]!.AsObject();
        output.Count.Should().Be(2, "no permissionDecision: Droid's own approval applies to the rewritten command");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        output["updatedInput"]!["timeout"]!.GetValue<int>().Should().Be(60);
    }

    [Theory]
    [InlineData("""{"tool_name":"Read","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"Execute","tool_input":{"command":"ls"}}""")]
    [InlineData("not json")]
    public void Droid_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.FactoryDroid, payload).Should().BeNull();
    }

    // Payload and reply shapes: github.com/charmbracelet/crush docs/hooks/README.md and internal/hooks/input.go, checked 2026-09-27.

    [Fact]
    public void Crush_BashTool_ReturnsOnlyTheCommandPatchAndNoDecision()
    {
        var reply = Reply(HookPayloadKind.Crush,
            """{"event":"PreToolUse","session_id":"s","cwd":"/p","tool_name":"bash","tool_input":{"command":"dotnet test && dotnet build","description":"x"}}""");

        reply.Should().Be("""{"version":1,"updated_input":{"command":"dtk dotnet test && dtk dotnet build"}}""");
    }

    [Fact]
    public void Crush_ProbeShapedPayloadWithoutToolName_IsRewritten()
    {
        Reply(HookPayloadKind.Crush, """{"tool_input":{"command":"dotnet build"}}""")
            .Should().Be("""{"version":1,"updated_input":{"command":"dtk dotnet build"}}""");
    }

    [Theory]
    [InlineData("""{"tool_name":"edit","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"bash","tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_name":"bash","tool_input":{"command":"dotnet build","command":"x"}}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Crush_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.Crush, payload).Should().BeNull();
    }
```

In `HookEntryPointTests` and `HookIntegrationTests`, every assertion on the usage text changes from
`…|pi|oh-my-pi|cursor|devin>` to `…|pi|oh-my-pi|cursor|devin|droid|crush>`.

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: build error — no `FactoryDroid`/`Crush` kinds.

- [ ] **Step 3: Implement**

`IHookIntegrator.cs`, after `Devin = 9`:

```csharp
    /// <summary>Devin Local's and Devin CLI's <c>PreToolUse</c> payload: Claude Code's shape, for the <c>exec</c> tool.</summary>
    Devin = 9,

    /// <summary>Factory Droid's <c>PreToolUse</c> payload: Claude Code's shape, for the <c>Execute</c> tool.</summary>
    FactoryDroid = 10,

    /// <summary>Crush's <c>PreToolUse</c> payload, for its <c>bash</c> tool.</summary>
    Crush = 11
```

`HookPayloads.cs`:
- Constants beside `DevinShellTool`:

```csharp
    /// <summary>Factory Droid's shell tool.</summary>
    private const string DroidShellTool = "Execute";

    /// <summary>Crush's shell tool.</summary>
    private const string CrushShellTool = "bash";
```

- `TryGetKind`: `"droid" => (true, HookPayloadKind.FactoryDroid),` and `"crush" => (true, HookPayloadKind.Crush),`;
  add both names to its `<summary>` list.
- Dispatch: `HookPayloadKind.FactoryDroid => ReplyWithUpdatedInput(root, DroidShellTool),` and
  `HookPayloadKind.Crush => ReplyToCrush(root),`.
- New method after `ReplyToCursor`:

```csharp
    /// <summary>
    /// Replies to Crush's <c>PreToolUse</c> in Crush's own envelope. <c>updated_input</c> is a shallow-merge patch, so
    /// only the command is sent; there is never a <c>decision</c>, because Crush's <c>allow</c> bypasses its permission
    /// prompt entirely and dtk leaves that decision to the user.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string? ReplyToCrush(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, ToolNameProperty, CrushShellTool)
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        return new JsonObject
        {
            ["version"] = 1,
            ["updated_input"] = new JsonObject { [CommandProperty] = rewritten }
        }.ToJsonString();
    }
```

`HookEntryPoint.cs` `Usage`: `"usage: dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity|pi|oh-my-pi|cursor|devin|droid|crush> "`.

- [ ] **Step 4: Run the tests to verify they pass** — the command above, then
  `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`,
  then `dtk dotnet build DotnetTokenKiller.slnx`.

- [ ] **Step 5: Commit** — `feat: dtk hook droid and dtk hook crush replies`.

---

### Task 2: Home paths for Factory Droid and Crush

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`

**Interfaces:**
- Produces: `HomePaths.FactoryDir : string`, `HomePaths.CrushConfigDir : string`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void FactoryDir_IsDotFactoryUnderHome_OrUnderAnAbsoluteOverride()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var over = Path.Combine(Path.GetTempPath(), "factory-home");

        new HomePaths(home).FactoryDir.Should().Be(Path.Combine(home, ".factory"));
        new HomePaths(home, name => name == "FACTORY_HOME_OVERRIDE" ? over : null).FactoryDir
            .Should().Be(Path.Combine(over, ".factory"), "Droid replaces the home directory and still appends .factory");
        new HomePaths(home, name => name == "FACTORY_HOME_OVERRIDE" ? "relative" : null).FactoryDir
            .Should().Be(Path.Combine(home, ".factory"));
    }

    [Fact]
    public void CrushConfigDir_PrefersCrushGlobalConfig_ThenXdgConfigHome_ThenDotConfig()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var global = Path.Combine(Path.GetTempPath(), "crush-global");
        var xdg = Path.Combine(Path.GetTempPath(), "xdg");

        new HomePaths(home).CrushConfigDir.Should().Be(Path.Combine(home, ".config", "crush"));
        new HomePaths(home, name => name == "XDG_CONFIG_HOME" ? xdg : null).CrushConfigDir
            .Should().Be(Path.Combine(xdg, "crush"));
        new HomePaths(home, name => name switch { "CRUSH_GLOBAL_CONFIG" => global, "XDG_CONFIG_HOME" => xdg, _ => null })
            .CrushConfigDir.Should().Be(global, "CRUSH_GLOBAL_CONFIG is the directory itself");
    }
```

- [ ] **Step 2: Run to verify failure** — `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HomePathsTests"` → build error.

- [ ] **Step 3: Implement** (after `WindsurfGlobalRulesPath`):

```csharp
    /// <summary>
    /// Gets Factory Droid's user directory: <c>$FACTORY_HOME_OVERRIDE/.factory</c> when that variable is an absolute
    /// path (Droid replaces the home directory with it and still appends <c>.factory</c>), else <c>~/.factory</c>.
    /// </summary>
    internal string FactoryDir => Path.Combine(RootedOrDefault("FACTORY_HOME_OVERRIDE", Home), ".factory");

    /// <summary>
    /// Gets Crush's global config directory, which holds its global <c>crushrc</c> and <c>CRUSH.md</c>:
    /// <c>$CRUSH_GLOBAL_CONFIG</c> when it is an absolute path, else <c>$XDG_CONFIG_HOME/crush</c> when that is, else
    /// <c>~/.config/crush</c> — on Windows too.
    /// </summary>
    internal string CrushConfigDir => RootedOrDefault(
        "CRUSH_GLOBAL_CONFIG", Path.Combine(RootedOrDefault("XDG_CONFIG_HOME", Path.Combine(Home, ".config")), "crush"));
```

- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit** — `feat: home paths for Factory Droid and Crush`.

---

### Task 3: `FactoryDroidHooks` — which file Droid reads `PreToolUse` from

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/FactoryDroidHooks.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/FactoryDroidHooksTests.cs`

**Interfaces:**
- Produces: `FactoryDroidHooks.Candidates(string factoryDir) : IReadOnlyList<(string Path, string? ContainerKey)>`,
  `FactoryDroidHooks.ResolveTarget(string factoryDir) : (string Path, string? ContainerKey)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class FactoryDroidHooksTests : IDisposable
{
    private const string LivePreToolUse =
        """{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-droid-hooks-{Guid.NewGuid()}", ".factory");

    private string HooksJson => Path.Combine(_dir, "hooks.json");
    private string LegacyHooksJson => Path.Combine(_dir, "hooks", "hooks.json");
    private string SettingsJson => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void Candidates_ListRootLegacyAndSettings_InDroidsOrder()
    {
        FactoryDroidHooks.Candidates(_dir).Should().Equal(
            (HooksJson, (string?)null), (LegacyHooksJson, (string?)null), (SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_NothingThere_CreatesRootHooksJson()
    {
        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_SettingsRunsPreToolUseAndNoHooksJson_UsesSettings()
    {
        Write(SettingsJson, $$"""{"model":"x","hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_HooksJsonDefinesPreToolUse_WinsOverSettings()
    {
        Write(HooksJson, LivePreToolUse);
        Write(SettingsJson, $$"""{"hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_HooksJsonWithoutPreToolUse_AndSettingsWithIt_UsesSettings()
    {
        Write(HooksJson, """{"PostToolUse":[]}""");
        Write(SettingsJson, $$"""{"hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_HooksJsonWithoutPreToolUse_AndNoSettingsHooks_UsesHooksJson()
    {
        Write(HooksJson, """{"PostToolUse":[]}""");
        Write(SettingsJson, """{"hooks":{"PreToolUse":[]}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_OnlyLegacyHooksJson_UsesLegacy()
    {
        Write(LegacyHooksJson, LivePreToolUse);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((LegacyHooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_RootHooksJsonPresent_IgnoresLegacy()
    {
        Write(HooksJson, "{}");
        Write(LegacyHooksJson, LivePreToolUse);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null), "Droid reads the legacy file only when the root one is absent");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"hooks":[]}""")]
    public void ResolveTarget_UnreadableSettings_FallsBackToHooksJson(string settings)
    {
        Write(SettingsJson, settings);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~FactoryDroidHooksTests"` → build error.

- [ ] **Step 3: Implement** `FactoryDroidHooks.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Finds the file whose <c>PreToolUse</c> hooks Factory Droid runs. Droid reads the root <c>hooks.json</c> (the legacy
/// <c>hooks/hooks.json</c> only when the root file is absent) and merges it over the <c>hooks</c> key of
/// <c>settings.json</c> per event key (rtk <c>src/hooks/init/droid.rs</c>, verified on Droid v0.164.0). A hook written
/// to a shadowed file never runs, and a <c>PreToolUse</c> added to <c>hooks.json</c> would shadow the user's own
/// <c>settings.json</c> hooks, so dtk writes where Droid already reads <c>PreToolUse</c> from.
/// </summary>
internal static class FactoryDroidHooks
{
    /// <summary>The hook event dtk registers.</summary>
    internal const string EventKey = "PreToolUse";

    /// <summary>The <c>settings.json</c> property holding its hook events.</summary>
    private const string SettingsContainerKey = "hooks";

    /// <summary>
    /// Every file Droid may read <c>PreToolUse</c> from, in Droid's order, each with the property holding its events
    /// (<see langword="null"/> when they sit at the root).
    /// </summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static IReadOnlyList<(string Path, string? ContainerKey)> Candidates(string factoryDir) =>
    [
        (Path.Combine(factoryDir, "hooks.json"), null),
        (Path.Combine(factoryDir, "hooks", "hooks.json"), null),
        (Path.Combine(factoryDir, "settings.json"), SettingsContainerKey)
    ];

    /// <summary>
    /// The file dtk registers its hook in: the live <c>hooks.json</c> when it already defines <c>PreToolUse</c>; else
    /// <c>settings.json</c> when its <c>hooks.PreToolUse</c> is non-empty; else the live <c>hooks.json</c> when one
    /// exists; else a new root <c>hooks.json</c>, where Droid's own <c>/hooks</c> UI writes.
    /// </summary>
    /// <param name="factoryDir">A <c>.factory</c> directory: the project's or the user's.</param>
    internal static (string Path, string? ContainerKey) ResolveTarget(string factoryDir)
    {
        var candidates = Candidates(factoryDir);
        var root = candidates[0].Path;
        var legacy = candidates[1].Path;
        var settings = candidates[2].Path;

        var liveHooksJson = File.Exists(root) ? root : File.Exists(legacy) ? legacy : null;

        if (liveHooksJson is not null && DefinesPreToolUse(liveHooksJson, containerKey: null))
        {
            return (liveHooksJson, null);
        }

        if (DefinesPreToolUse(settings, SettingsContainerKey))
        {
            return (settings, SettingsContainerKey);
        }

        return (liveHooksJson ?? root, null);
    }

    /// <summary>Whether <paramref name="path"/> holds a non-empty <c>PreToolUse</c> array; unreadable files hold none.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="containerKey">The property holding the events, or <see langword="null"/> for the root.</param>
    private static bool DefinesPreToolUse(string path, string? containerKey)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: IntegratorHelpers.LenientJson);
            var events = containerKey is null ? node as JsonObject : (node as JsonObject)?[containerKey] as JsonObject;
            return events?[EventKey] is JsonArray { Count: > 0 };
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit** — `feat: resolve the file Factory Droid reads PreToolUse hooks from`.

---

### Task 4: `dtk init droid`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/FactoryDroidIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` (register it after `DevinIntegrator`)
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/FactoryDroidIntegratorTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallIntegrationTests.cs` (add
  `new FactoryDroidIntegrator(Rtk(), home)` to `Integrators()`, `"droid"` to `AllProviders`, `GlobalProviders` and
  the doctor test's `hookProviders`)

**Interfaces:**
- Consumes: `HookPayloadKind.FactoryDroid` (Task 1), `HomePaths.FactoryDir` (Task 2), `FactoryDroidHooks` (Task 3),
  `HookRegistrationSpec(…, ContainerKey: string?)`, `SharedInstructionArtifacts.WriteAgentsFilesAsync/RemoveAgentsFilesAsync/SkillPath`,
  `RtkHookCoexistence.ReconcileFilesAsync/IsRtkRewriteReferencedIn/NoteRemainingExclusion`.
- Produces: `internal sealed class FactoryDroidIntegrator(RtkHookCoexistence rtk, HomePaths home)` with
  `ProviderName == "droid"`, note `FactoryDroidIntegrator.SessionNote`.

- [ ] **Step 1: Write the failing tests** (`FactoryDroidIntegratorTests.cs`):

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class FactoryDroidIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-droid-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string ProjectHooksJson => Path.Combine(ProjectDir, ".factory", "hooks.json");
    private string ProjectSettingsJson => Path.Combine(ProjectDir, ".factory", "settings.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private HomePaths Home => new(HomeDir, name => _environment.GetValueOrDefault(name));

    private FactoryDroidIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static JsonObject DtkHandler(string path, string? containerKey)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var events = containerKey is null ? root : root[containerKey]!.AsObject();
        return events["PreToolUse"]!.AsArray()
            .Select(group => group!["hooks"]!.AsArray().Single()!.AsObject())
            .Single(handler => handler["command"]!.GetValue<string>() == "dtk hook droid");
    }

    [Fact]
    public void ProviderName_IsDroid() => CreateSut().ProviderName.Should().Be("droid");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesInstructionsSkillAndRootHooksJson()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, ProjectHooksJson);
        result.Notes.Should().Equal(FactoryDroidIntegrator.SessionNote);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksJson))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PreToolUse");
        var group = root["PreToolUse"]![0]!;
        group["matcher"]!.GetValue<string>().Should().Be("Execute");
        DtkHandler(ProjectHooksJson, null)["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SettingsAlreadyRunsPreToolUse_MergesThereAndCreatesNoHooksJson()
    {
        Write(ProjectSettingsJson,
            """{"model":"x","hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(ProjectSettingsJson);
        File.Exists(ProjectHooksJson).Should().BeFalse("a hooks.json PreToolUse would shadow the user's settings hooks");
        DtkHandler(ProjectSettingsJson, "hooks").Should().NotBeNull();
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("lint").And.Contain("\"model\"");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Contain(ProjectHooksJson);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsFactoryHomeOverride()
    {
        var over = Path.Combine(_tempDir, "factory-home");
        _environment["FACTORY_HOME_OVERRIDE"] = over;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        var hooks = Path.Combine(over, ".factory", "hooks.json");
        result.CreatedFiles.Should().Contain([Path.Combine(over, ".factory", "AGENTS.md"), hooks]);
        result.CreatedFiles.Should().Contain(Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"));
        DtkHandler(hooks, null).Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_PointsAtTheResolvedFile(bool settingsRunsHooks)
    {
        if (settingsRunsHooks)
        {
            Write(ProjectSettingsJson, """{"hooks":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"x"}]}]}}""");
        }

        var hook = CreateSut().DescribeHooks(ProjectDir, HookScope.Project).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(settingsRunsHooks ? ProjectSettingsJson : ProjectHooksJson);
        hook.Command.Should().Be("dtk hook droid");
        hook.PayloadKind.Should().Be(HookPayloadKind.FactoryDroid);
    }

    [Fact]
    public async Task IntegrateAsync_RtkDroidHook_ExcludesDotnetInRtksConfig()
    {
        Write(Path.Combine(HomeDir, ".factory", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"rtk hook droid"}]}]}}""");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task UninstallAsync_RemovesDtkFromEveryCandidateAndKeepsOtherHooks()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"dtk hook droid","timeout":10}]},{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}}""");

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain([ProjectHooksJson, AgentsPath, SkillPath]);
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("lint").And.NotContain("dtk hook droid");
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~FactoryDroidIntegratorTests"` → build error.

- [ ] **Step 3: Implement** `FactoryDroidIntegrator.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Factory Droid.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and <c>.factory</c> directories for global integration.</param>
/// <remarks>
/// Creates the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill (<c>~/.factory/AGENTS.md</c> and
/// <c>~/.agents/skills</c> globally), and registers <c>dtk hook droid</c> under <c>PreToolUse</c> for the
/// <c>Execute</c> tool in the file <see cref="FactoryDroidHooks.ResolveTarget"/> picks (merged, never overwritten).
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class FactoryDroidIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook: Droid snapshots hooks when a session starts.</summary>
    internal const string SessionNote =
        "Droid reads hooks when a session starts: restart any running droid session for the hook to take effect.";

    /// <summary>Seconds Droid waits for the hook.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <summary>Droid's shell tool, which the hook's matcher selects.</summary>
    private const string ShellTool = "Execute";

    /// <inheritdoc/>
    public string ProviderName => "droid";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            FactoryDroidHooks.ResolveTarget(FactoryDir(directory, scope)).Path,
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.FactoryDroid)
    ];

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken) =>
        IntegrateCoreAsync(directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken) =>
        IntegrateCoreAsync(home.Home, HookScope.Global, force, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        [InstructionsPath(directory, scope), SharedInstructionArtifacts.SkillPath(SkillsDirectory(directory, scope))];

    /// <inheritdoc/>
    /// <remarks>Removes dtk's entry from every file Droid reads hooks from, wherever an earlier run or the user put it.</remarks>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
            InstructionsPath(root, scope), SkillsDirectory(root, scope), context, cancellationToken).ConfigureAwait(false);

        foreach (var (path, containerKey) in FactoryDroidHooks.Candidates(FactoryDir(root, scope)))
        {
            await UninstallHelpers.RemoveHookRegistrationAsync(Registration(path, containerKey), context, cancellationToken)
                .ConfigureAwait(false);
        }

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(root, scope)));

        return context.ToResult();
    }

    private static HookRegistrationSpec Registration(string path, string? containerKey) =>
        new(path, FactoryDroidHooks.EventKey, ShellTool, HookCommands.Invocation("droid"), HookTimeoutSeconds,
            ContainerKey: containerKey);

    private string FactoryDir(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.FactoryDir : Path.Combine(directory, ".factory");

    private string InstructionsPath(string directory, HookScope scope) =>
        Path.Combine(scope == HookScope.Global ? home.FactoryDir : directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>Where rtk registers itself for Droid: every candidate file in this scope and in the user's home.</summary>
    private List<string> RtkCandidates(string directory, HookScope scope) =>
        [.. FactoryDroidHooks.Candidates(FactoryDir(directory, scope))
            .Concat(FactoryDroidHooks.Candidates(home.FactoryDir))
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.Ordinal)];

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string directory, HookScope scope, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            InstructionsPath(directory, scope), SkillsDirectory(directory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        var (path, containerKey) = FactoryDroidHooks.ResolveTarget(FactoryDir(directory, scope));
        await IntegratorHelpers.WriteHookRegistrationAsync(Registration(path, containerKey), context, cancellationToken)
            .ConfigureAwait(false);

        if (context.Created.Contains(path) || context.Updated.Contains(path))
        {
            context.Notes.Add(SessionNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory, scope), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }
}
```

`DependencyInjection.cs`: `services.AddTransient<IProviderIntegrator, FactoryDroidIntegrator>();` after the Devin line.
`UninstallIntegrationTests`: the edits listed under **Files**.

- [ ] **Step 4: Run the tests to verify they pass** —
  `--filter "FullyQualifiedName~FactoryDroidIntegratorTests|FullyQualifiedName~UninstallIntegrationTests"`, then the solution build.

- [ ] **Step 5: Commit** — `feat: dtk init droid installs a Factory Droid PreToolUse rewrite hook`.

---

### Task 5: `CrushrcFile` — dtk's marked section in a `crushrc`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/CrushrcFile.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CrushrcFileTests.cs`

**Interfaces:**
- Produces: `CrushrcFile.BeginMarker`, `CrushrcFile.EndMarker`, `CrushrcFile.Section(string command) : string` (LF),
  `CrushrcFile.WriteAsync(string path, string command, IntegrationContext, CancellationToken) : Task`,
  `CrushrcFile.RemoveAsync(string path, IntegrationContext, CancellationToken) : Task`,
  `CrushrcFile.HasSection(string path) : bool` (true when the file holds a well-formed section; false when absent,
  unreadable or damaged).

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CrushrcFileTests : IDisposable
{
    private const string Command = "dtk hook crush";
    private const string UserConfig = "provider add deepseek --type openai-compat\nhook add PreToolUse --name fmt --command 'gofmt -l .'\n";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-crushrc-{Guid.NewGuid()}");

    private string RcPath => Path.Combine(_tempDir, ".crushrc");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void Seed(string content)
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(RcPath, content);
    }

    private static IntegrationContext Install() => new(force: false);

    private IntegrationContext Uninstall() => IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

    [Fact]
    public void Section_IsExactlyTheThreeLines()
    {
        CrushrcFile.Section(Command).Should().Be(
            "# >>> dtk (DotnetTokenKiller) >>>\n"
            + "hook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'\n"
            + "# <<< dtk <<<\n");
    }

    [Fact]
    public async Task WriteAsync_NoFile_CreatesItWithTheSection()
    {
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(CrushrcFile.Section(Command));
        context.Created.Should().Equal(RcPath);
    }

    [Fact]
    public async Task WriteAsync_UserConfig_AppendsAfterABlankLineAndKeepsEverything()
    {
        Seed(UserConfig);
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(UserConfig + "\n" + CrushrcFile.Section(Command));
        context.Updated.Should().Equal(RcPath);
    }

    [Fact]
    public async Task WriteAsync_NoTrailingNewline_StillSeparatesTheSection()
    {
        Seed("option debug true");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be("option debug true\n\n" + CrushrcFile.Section(Command));
    }

    [Fact]
    public async Task WriteAsync_SecondRun_ReportsUnchanged()
    {
        Seed(UserConfig);
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var before = await File.ReadAllTextAsync(RcPath);
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        context.Unchanged.Should().Equal(RcPath);
        (await File.ReadAllTextAsync(RcPath)).Should().Be(before);
    }

    [Fact]
    public async Task WriteAsync_StaleSection_IsReplacedInPlace()
    {
        Seed("a\n# >>> dtk (DotnetTokenKiller) >>>\nhook add PreToolUse --command 'old'\n# <<< dtk <<<\nb\n");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be("a\n" + CrushrcFile.Section(Command) + "b\n");
    }

    [Fact]
    public async Task WriteAsync_CrlfFile_WritesTheSectionWithCrlf()
    {
        Seed("option debug true\r\n");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(
            "option debug true\r\n\r\n" + CrushrcFile.Section(Command).ReplaceLineEndings("\r\n"));
    }

    [Theory]
    [InlineData("# >>> dtk (DotnetTokenKiller) >>>\nhook add PreToolUse --command x\n")]
    [InlineData("hook add PreToolUse --command x\n# <<< dtk <<<\n")]
    [InlineData("# <<< dtk <<<\n# >>> dtk (DotnetTokenKiller) >>>\n")]
    [InlineData("# >>> dtk (DotnetTokenKiller) >>>\n# <<< dtk <<<\n# >>> dtk (DotnetTokenKiller) >>>\n# <<< dtk <<<\n")]
    public async Task WriteAndRemove_DamagedSection_ThrowAndLeaveTheFileAlone(string content)
    {
        Seed(content);

        var write = () => CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var remove = () => CrushrcFile.RemoveAsync(RcPath, Uninstall(), default);

        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{RcPath}*");
        await remove.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{RcPath}*");
        (await File.ReadAllTextAsync(RcPath)).Should().Be(content);
        CrushrcFile.HasSection(RcPath).Should().BeFalse();
    }

    [Theory]
    [InlineData(UserConfig)]
    [InlineData("option debug true")]
    [InlineData("option debug true\r\n")]
    public async Task InstallThenRemove_RestoresTheUserFileByteForByte(string original)
    {
        Seed(original);
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        CrushrcFile.HasSection(RcPath).Should().BeTrue();
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(original.EndsWith('\n') ? original : original + "\n");
        context.Updated.Should().Equal(RcPath);
    }

    [Fact]
    public async Task RemoveAsync_OnlyTheSection_DeletesTheFile()
    {
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        File.Exists(RcPath).Should().BeFalse();
        context.Removed.Should().Equal(RcPath);
    }

    [Fact]
    public async Task RemoveAsync_NoSection_ReportsUnchanged()
    {
        Seed(UserConfig);
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        context.Unchanged.Should().Equal(RcPath);
        (await File.ReadAllTextAsync(RcPath)).Should().Be(UserConfig);
    }

    [Fact]
    public async Task RemoveAsync_NoFile_DoesNothing()
    {
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        context.Unchanged.Should().BeEmpty();
        context.Removed.Should().BeEmpty();
    }
}
```

The round-trip test documents one normalization: a file without a trailing newline comes back with one.

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~CrushrcFileTests"` → build error.

- [ ] **Step 3: Implement** `CrushrcFile.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Reads and writes dtk's marked section in a Crush <c>crushrc</c>, a Bash script Crush runs to build its config.
/// </summary>
/// <remarks>
/// dtk edits only the lines between its markers and never guesses: a file with a missing, repeated or out-of-order
/// marker makes install and uninstall throw, naming the file, and leaves it untouched. The section takes the file's own
/// line endings; the rest of the file is written back as read, so its mode and every other line survive.
/// </remarks>
internal static class CrushrcFile
{
    /// <summary>The line opening dtk's section.</summary>
    internal const string BeginMarker = "# >>> dtk (DotnetTokenKiller) >>>";

    /// <summary>The line closing dtk's section.</summary>
    internal const string EndMarker = "# <<< dtk <<<";

    /// <summary>dtk's section, with LF endings, registering <paramref name="command"/> for Crush's <c>bash</c> tool.</summary>
    /// <param name="command">The hook command, <c>dtk hook crush</c>.</param>
    internal static string Section(string command) =>
        $"{BeginMarker}\nhook add PreToolUse --name dtk --matcher '^bash$' --command '{command}'\n{EndMarker}\n";

    /// <summary>Whether <paramref name="path"/> holds a well-formed dtk section; absent, unreadable or damaged files do not.</summary>
    /// <param name="path">The <c>crushrc</c> to read.</param>
    internal static bool HasSection(string path)
    {
        try
        {
            return File.Exists(path) && FindSection(File.ReadAllText(path), path) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Creates the file with the section, appends the section, or replaces a stale one.</summary>
    /// <param name="path">The <c>crushrc</c> to write.</param>
    /// <param name="command">The hook command.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file holds a damaged dtk section.</exception>
    internal static async Task WriteAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Section(command), cancellationToken).ConfigureAwait(false);
            context.Created.Add(path);
            return;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var newline = NewlineOf(content);
        var section = Section(command).ReplaceLineEndings(newline);
        string updated;

        if (FindSection(content, path) is { } span)
        {
            var (start, end) = span;
            if (string.Equals(content[start..end], section, StringComparison.Ordinal))
            {
                context.Unchanged.Add(path);
                return;
            }

            updated = content[..start] + section + content[end..];
        }
        else
        {
            var separator = content.Length == 0 ? string.Empty : content.EndsWith('\n') ? newline : newline + newline;
            updated = content + separator + section;
        }

        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>
    /// Removes the section and the blank line dtk put before it; deletes the file when nothing else is left.
    /// </summary>
    /// <param name="path">The <c>crushrc</c> to edit.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file holds a damaged dtk section.</exception>
    internal static async Task RemoveAsync(string path, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (FindSection(content, path) is not { } span)
        {
            context.Unchanged.Add(path);
            return;
        }

        var (start, end) = span;

        var remaining = content[..start] + content[end..];
        if (remaining.Trim().Length == 0)
        {
            UninstallHelpers.DeleteFile(path, context);
            return;
        }

        if (end == content.Length)
        {
            remaining = remaining.TrimEnd('\r', '\n') + NewlineOf(content);
        }

        await File.WriteAllTextAsync(path, remaining, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>CRLF when the file already uses it, else LF.</summary>
    private static string NewlineOf(string content) => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>
    /// The span from the begin marker's line to the end of the end marker's line (its newline included), or
    /// <see langword="null"/> when neither marker is present.
    /// </summary>
    /// <exception cref="InvalidOperationException">A marker is missing, repeated or out of order.</exception>
    private static (int Start, int End)? FindSection(string content, string path)
    {
        var begins = new List<int>();
        var ends = new List<int>();
        var offset = 0;

        while (offset < content.Length)
        {
            var newline = content.IndexOf('\n', offset);
            var lineEnd = newline < 0 ? content.Length : newline + 1;
            var line = content[offset..lineEnd].TrimEnd('\r', '\n');

            if (line == BeginMarker)
            {
                begins.Add(offset);
            }
            else if (line == EndMarker)
            {
                ends.Add(lineEnd);
            }

            offset = lineEnd;
        }

        if (begins.Count == 0 && ends.Count == 0)
        {
            return null;
        }

        if (begins.Count == 1 && ends.Count == 1 && begins[0] < ends[0])
        {
            return (begins[0], ends[0]);
        }

        throw new InvalidOperationException(
            $"The Crush config '{path}' has a damaged dtk section ({begins.Count} '{BeginMarker}' and {ends.Count} "
            + $"'{EndMarker}' lines). Delete dtk's lines and both markers by hand, then run the command again.");
    }
}
```

`RemoveAsync`'s trailing-blank handling: when the section ended the file, the blank separator line dtk inserted is
trimmed with it. A section in the middle of a file (user content after it) is removed exactly.

- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit** — `feat: read and write dtk's section in a Crush crushrc`.

---

### Task 6: doctor reads a script registration as text

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (`HookInstallation`)
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`ReadRegistrationAsync`)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Produces: `HookInstallation.IsScriptRegistration` (`bool`, init-only property, default `false`).

- [ ] **Step 1: Write the failing tests** (in `HookHealthCheckerTests`; a local stub avoids depending on Task 7):

```csharp
    private sealed class ScriptHookIntegrator(string path) : IHookIntegrator
    {
        public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
            scope == HookScope.Project
                ? [new HookInstallation("crush", scope, path, "dtk hook crush", null, HookPayloadKind.Crush) { IsScriptRegistration = true }]
                : [];
    }

    [Fact]
    public async Task RunAsync_ScriptRegistrationRunningTheHook_IsRegisteredAndProbed()
    {
        var path = Path.Combine(_tempDir, ".crushrc");
        await File.WriteAllTextAsync(path, "option debug true\nhook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'\n");

        var checks = await _sut.RunAsync([new ScriptHookIntegrator(path)], _tempDir, default);

        checks.Should().Contain(c => c.Name == "crush hook (project)" && c.Passed && c.Message == "registered");
        checks.Should().Contain(c => c.Name == "crush hook probe (project)");
    }

    [Fact]
    public async Task RunAsync_ScriptRegistrationWithoutTheHook_IsNotRegistered()
    {
        var path = Path.Combine(_tempDir, ".crushrc");
        await File.WriteAllTextAsync(path, "option debug true\n");

        var checks = await _sut.RunAsync([new ScriptHookIntegrator(path)], _tempDir, default);

        checks.Should().Contain(c => c.Name == "crush hook (project)" && !c.Passed && c.Message.Contains("not registered"));
    }
```

If `RunAsync` treats a registration whose file exists but holds no hook as "nothing installed" (the informational
single check), adapt the second test to assert that outcome instead — read `RunAsync` lines 44-86 first and assert
whatever it does for an `Absent` JSON registration, so script and JSON registrations behave alike.

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~HookHealthCheckerTests"` → build error
  (no `IsScriptRegistration`).

- [ ] **Step 3: Implement**

`HookInstallation`: keep the positional parameters; add to the record body

```csharp
{
    /// <summary>
    /// Gets a value indicating whether <see cref="RegistrationPath"/> is a script (Crush's <c>crushrc</c>) rather than
    /// JSON, so diagnostics search its text for the hook command instead of parsing it.
    /// </summary>
    internal bool IsScriptRegistration { get; init; }
}
```

and document it in the record's summary list of registration files. In `HookHealthChecker.ReadRegistrationAsync`,
right after the `PluginArtifact` branch:

```csharp
        if (installation.IsScriptRegistration)
        {
            return content.Contains(HookCommands.Invocation(installation.ProviderName), StringComparison.Ordinal)
                ? new Registration(RegistrationKind.Current, string.Empty)
                : new Registration(RegistrationKind.Absent, $"not registered — {path} does not run '{installation.Command}'");
        }
```

and mention script registrations in that method's `<summary>`.

- [ ] **Step 4: Run the tests to verify they pass.**
- [ ] **Step 5: Commit** — `feat: doctor reads a script hook registration as text`.

---

### Task 7: `dtk init crush`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/CrushIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` (after `FactoryDroidIntegrator`)
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/CrushIntegratorTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallIntegrationTests.cs` (add
  `new CrushIntegrator(home)`, and `"crush"` to `AllProviders`, `GlobalProviders`, `hookProviders`)

**Interfaces:**
- Consumes: `CrushrcFile` (Task 5), `HookInstallation.IsScriptRegistration` (Task 6), `HomePaths.CrushConfigDir`
  (Task 2), `HookPayloadKind.Crush` (Task 1).
- Produces: `internal sealed class CrushIntegrator(HomePaths home)` with `ProviderName == "crush"`, notes
  `CrushIntegrator.VersionNote`, `CrushIntegrator.SubagentNote`, constant `CrushIntegrator.MinimumCrushVersion`
  (`"0.88.0"`, provisional until Task 9 pins it).

- [ ] **Step 1: Write the failing tests** (`CrushIntegratorTests.cs`):

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CrushIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-crush-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private HomePaths Home => new(HomeDir, name => _environment.GetValueOrDefault(name));
    private string DotRc => Path.Combine(ProjectDir, ".crushrc");
    private string PlainRc => Path.Combine(ProjectDir, "crushrc");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CrushIntegrator CreateSut() => new(Home);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void ProviderName_IsCrush() => CreateSut().ProviderName.Should().Be("crush");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesInstructionsSkillAndDotCrushrc()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, DotRc);
        result.Notes.Should().Equal(CrushIntegrator.VersionNote, CrushIntegrator.SubagentNote);
        (await File.ReadAllTextAsync(DotRc)).Should().Be(CrushrcFile.Section("dtk hook crush"));
    }

    [Fact]
    public async Task IntegrateAsync_ProjectUsesPlainCrushrc_WritesThere()
    {
        Write(PlainRc, "option debug true\n");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        File.Exists(DotRc).Should().BeFalse();
        (await File.ReadAllTextAsync(PlainRc)).Should().Contain("--command 'dtk hook crush'");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Contain(DotRc);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsCrushGlobalConfig()
    {
        var global = Path.Combine(_tempDir, "crush-global");
        _environment["CRUSH_GLOBAL_CONFIG"] = global;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Contain(
        [
            Path.Combine(global, "CRUSH.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(global, "crushrc")
        ]);
        (await File.ReadAllTextAsync(Path.Combine(global, "CRUSH.md"))).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_IsAScriptRegistrationAtTheScopesCrushrc(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(global ? Path.Combine(HomeDir, ".config", "crush", "crushrc") : DotRc);
        hook.IsScriptRegistration.Should().BeTrue();
        hook.Command.Should().Be("dtk hook crush");
        hook.PayloadKind.Should().Be(HookPayloadKind.Crush);
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesEverythingAndKeepsUserConfig()
    {
        Write(PlainRc, "option debug true\n");
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain([AgentsPath, SkillPath]);
        (await File.ReadAllTextAsync(PlainRc)).Should().Be("option debug true\n");
    }

    [Fact]
    public async Task IntegrateAsync_DamagedSection_ThrowsNamingTheFile()
    {
        Write(DotRc, "# >>> dtk (DotnetTokenKiller) >>>\n");

        var act = () => CreateSut().IntegrateAsync(ProjectDir, false, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{DotRc}*");
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~CrushIntegratorTests"` → build error.

- [ ] **Step 3: Implement** `CrushIntegrator.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Crush.</summary>
/// <param name="home">Resolves the user's home and Crush config directories for global integration.</param>
/// <remarks>
/// Creates, in a project: the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill, and dtk's section in
/// <c>.crushrc</c> (or in an existing <c>crushrc</c> when the project has no <c>.crushrc</c>) registering
/// <c>dtk hook crush</c> for the <c>bash</c> tool. Globally: dtk's section in <c>crushrc</c> and in <c>CRUSH.md</c>
/// under <see cref="HomePaths.CrushConfigDir"/>, and the skill in <c>~/.agents/skills</c>.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CrushIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>The first Crush release that reads <c>crushrc</c> with <c>hook add</c>; pinned by gate C.</summary>
    internal const string MinimumCrushVersion = "0.88.0";

    /// <summary>Printed when this run wrote the hook: older Crush releases do not read <c>crushrc</c>.</summary>
    internal const string VersionNote =
        "The hook is registered in crushrc, which Crush reads from version " + MinimumCrushVersion + " on; update Crush if it is older.";

    /// <summary>Printed when this run wrote the hook.</summary>
    internal const string SubagentNote =
        "Crush runs hooks for the main agent's tool calls only, so a sub-agent's dotnet commands run as written.";

    /// <inheritdoc/>
    public string ProviderName => "crush";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            RcPath(directory, scope),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Crush) { IsScriptRegistration = true }
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
            .ConfigureAwait(false);
        await WriteHookAsync(RcPath(directory, HookScope.Project), context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
            SharedInstructionArtifacts.Section, context, cancellationToken).ConfigureAwait(false);
        await IntegratorHelpers.WriteGeneratedFileAsync(
            SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(RcPath(home.Home, HookScope.Global), context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        scope == HookScope.Global
            ? [SharedInstructionArtifacts.SkillPath(home.AgentsSkillsDir)]
            : [Path.Combine(directory, "AGENTS.md"), SharedInstructionArtifacts.SkillPath(Path.Combine(directory, ".agents", "skills"))];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        if (scope == HookScope.Global)
        {
            await UninstallHelpers.RemoveSectionAsync(
                GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
                context, cancellationToken).ConfigureAwait(false);
            await UninstallHelpers.RemoveGeneratedFileAsync(
                SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(RcPath(root, scope), context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
                Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
                .ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(Path.Combine(directory, ".crushrc"), context, cancellationToken).ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(Path.Combine(directory, "crushrc"), context, cancellationToken).ConfigureAwait(false);
        }

        return context.ToResult();
    }

    private string GlobalInstructionsPath => Path.Combine(home.CrushConfigDir, "CRUSH.md");

    /// <summary>
    /// The <c>crushrc</c> dtk registers in: globally <c>crushrc</c> in Crush's config directory; in a project,
    /// <c>.crushrc</c>, unless the project already has a <c>crushrc</c> and no <c>.crushrc</c>.
    /// </summary>
    private string RcPath(string directory, HookScope scope)
    {
        if (scope == HookScope.Global)
        {
            return Path.Combine(home.CrushConfigDir, "crushrc");
        }

        var dotRc = Path.Combine(directory, ".crushrc");
        var plainRc = Path.Combine(directory, "crushrc");
        return !File.Exists(dotRc) && File.Exists(plainRc) ? plainRc : dotRc;
    }

    private static async Task WriteHookAsync(string rcPath, IntegrationContext context, CancellationToken cancellationToken)
    {
        await CrushrcFile.WriteAsync(rcPath, HookCommands.Invocation("crush"), context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(rcPath) || context.Updated.Contains(rcPath))
        {
            context.Notes.Add(VersionNote);
            context.Notes.Add(SubagentNote);
        }
    }
}
```

Adjust member order if an analyzer (e.g. SA/S member ordering) requires it. `DependencyInjection.cs`:
`services.AddTransient<IProviderIntegrator, CrushIntegrator>();`. `UninstallIntegrationTests`: the edits listed.

- [ ] **Step 4: Run the tests to verify they pass** —
  `--filter "FullyQualifiedName~CrushIntegratorTests|FullyQualifiedName~UninstallIntegrationTests|FullyQualifiedName~HookHealthCheckerTests"`,
  then the solution build.

- [ ] **Step 5: Commit** — `feat: dtk init crush registers a PreToolUse hook in crushrc`.

---

### Task 8: CLI surface for `droid` and `crush`

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/InitCommandSettings.cs` (provider description: add `droid`, `crush`)
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs` (bash list, zsh and fish descriptions, PowerShell list)
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs` (examples `droid`, `droid --global`, `crush`, `crush --global`)
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs` (`[InlineData("droid")]`, `[InlineData("crush")]`)
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs` (`InitProviders` gains `"droid"`, `"crush"`)

- [ ] **Step 1:** Add the two `InlineData` rows and the two `ParityCases` names (after `"devin"`, `"windsurf"`).
- [ ] **Step 2:** Run `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"` — the
  new rows pass already (they use stubs); this step confirms the wiring compiles.
- [ ] **Step 3:** Edit the settings description (`…, cursor, devin (alias: windsurf), droid, crush, aider, jetbrains`),
  the completions (zsh/fish descriptions: `'droid:Install dtk instructions and rewrite hook for Factory Droid'`,
  `'crush:Install dtk instructions and rewrite hook for Crush'`), and the four `CliConfigurator` examples (use the
  existing `GlobalOption` constant).
- [ ] **Step 4:** Run `--filter "FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~DocsBindingTests"`.
  The init-help snapshot test fails with a `.received.txt`: check the diff is exactly the new examples and description,
  copy it over the `.verified.txt`, and **strip any UTF-8 BOM** (`head -c 3 <file> | od -An -tx1` must not print
  `ef bb bf`). Re-run until green, then build the solution.
- [ ] **Step 5: Commit** — `feat: droid and crush in the init CLI surface`.

---

### Task 9: Gate C — the real `crush` binary rewrites through `dtk hook crush`

**Files:**
- Create: `eng/gates/mock-openai.mjs` (Node, no dependencies)
- Create: `eng/gates/crush-gate.sh`
- Create: `eng/gates/README.md`

A live gate, run on demand (network needed to download Crush), not in CI. It proves, on the released binary, that a
`dtk init crush` project makes Crush run `dtk dotnet build` when the model asks for `dotnet build`, and pins the first
Crush release that does.

- [ ] **Step 1: The mock model.** `mock-openai.mjs` serves an OpenAI-compatible `POST /v1/chat/completions` (and
  `GET /v1/models` listing one model, `mock`) on a port given as `argv[2]`, printing each request body to a log file
  given as `argv[3]`. Its answer depends only on the conversation: when the request's messages contain no `tool`-role
  message, it answers with one tool call to the tool named `bash` whose arguments are `{"command":"dotnet build","description":"build"}`
  (take the exact tool name and its parameter names from the request's `tools` array: pick the tool whose name is
  `bash`, and fail loudly if there is none); once a `tool` message is present, it answers with a plain assistant
  message `done`. Support both `stream: false` and `stream: true` (SSE chunks in OpenAI's delta format, ending with
  `data: [DONE]`), because Crush may stream.
- [ ] **Step 2: The gate script.** `crush-gate.sh <crush-version> [dtk-binary]`:
  1. Downloads `crush_<version>_Linux_x86_64.tar.gz` from `https://github.com/charmbracelet/crush/releases/download/v<version>/`
     (check the asset name on the release page; adapt if it differs) into a scratch dir.
  2. Creates a scratch `HOME` and project dir; puts on `PATH` a fake `dotnet` that appends `dotnet $*` to
     `$GATE_LOG` and exits 0, and a `dtk` wrapper that runs the real dtk (default: the branch's Debug build,
     `src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk`) for `dtk hook …` and otherwise appends `dtk $*` to
     `$GATE_LOG` and exits 0.
  3. Runs `dtk init crush` in the project (through the real dtk), then appends to the project's `.crushrc` a
     `provider add mock --type openai-compat --base-url http://127.0.0.1:<port>/v1 --api-key x` line and whatever
     `model`/`option` lines that Crush version needs to select `mock` non-interactively (read that release's
     `docs/config/README.md`); starts the mock; runs `crush run` (non-interactive, with the flag that auto-approves
     tool calls, e.g. `--yolo`, only for this scratch run) with the prompt `build the project`, with a 120 s timeout.
  4. **Pass** when `$GATE_LOG` contains the line `dtk dotnet build` and no bare `dotnet build`. Print the log, the
     crush exit code and PASS/FAIL; exit non-zero on FAIL. **Control run:** the same without dtk's section (remove it
     with `dtk init crush --uninstall`) must log a bare `dotnet build` — proving the gate can fail.
- [ ] **Step 3: Run it.** Build the solution, then run the gate on the latest Crush release (at least v0.96.1). Then
  run it on v0.88.0 and on the last release before it (v0.87.x). The minimum version is the oldest release that
  passes. If v0.88.0 fails, bisect upward through the releases between it and the latest until one passes.
- [ ] **Step 4: Record.** In `eng/gates/README.md`: what the gate does, how to run it, and a results table (Crush
  version, date, PASS/FAIL, and the control run). If the pinned minimum differs from `0.88.0`, update
  `CrushIntegrator.MinimumCrushVersion` (and any test asserting `VersionNote` text still passes because tests compare
  the constant). If no release passes, do not change product code: record the failure output verbatim in the README
  and report BLOCKED with it.
- [ ] **Step 5: Commit** — `test: gate C runs the real crush binary through dtk hook crush`.

---

### Task 10: Docs, verification checklist and spec amendment

**Files:**
- Modify: `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docs/articles/ai-agent-setup.md`, `docs/articles/usage.md`,
  `docs/articles/harness-verification.md`, `CLAUDE.md`, `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md`

- [ ] **Step 1: README.md** — feature line: `… Cursor, Devin (formerly Windsurf), Factory Droid, Crush, Aider, JetBrains AI`
  and the integration count +2 (read the current number first). Provider table rows (match column alignment):

```markdown
| **Factory Droid**      | `dtk init droid`       | `AGENTS.md`, `.agents/skills/…`, `.factory/hooks.json` (or `settings.json`) (`--global`: `~/.factory/`) |
| **Crush**              | `dtk init crush`       | `AGENTS.md`, `.agents/skills/…`, `.crushrc` section (`--global`: `~/.config/crush/crushrc`, `CRUSH.md`) |
```

  Add `droid` and `crush` to every `--global` provider list (prose and the `--global` example block:
  `dtk init droid --global   # ~/.factory, ~/.agents/skills` and `dtk init crush --global   # ~/.config/crush, ~/.agents/skills`).
- [ ] **Step 2: `src/DotnetTokenKiller.Cli/README.md` and `docs/articles/usage.md`** — the same feature line and
  `--global` lists; usage gains `dtk init droid` and `dtk init crush` lines beside the others.
- [ ] **Step 3: `docs/articles/ai-agent-setup.md`** — new `## Factory Droid` and `## Crush` sections modelled on the
  Codex section: what is written in each scope; the reply (Droid: Claude's `updatedInput`, no decision; Crush: its own
  `updated_input` patch, no decision, so Crush still asks for permission); Droid's file rule (the four steps, and that
  uninstall cleans all three files); `FACTORY_HOME_OVERRIDE`; the session-restart note; Crush's `crushrc` section
  (show it), `.crushrc` vs `crushrc`, `CRUSH_GLOBAL_CONFIG`/`XDG_CONFIG_HOME`, the minimum Crush version, the damaged
  section refusal, sub-agents; rtk coexistence for Droid (`rtk hook droid`). Crush: "Verified against the real Crush
  binary (gate C, see `eng/gates/README.md`)". Droid: "Not verified against a live run — see
  [Harness verification](harness-verification.md)".
- [ ] **Step 4: `docs/articles/harness-verification.md`** — a `## Factory Droid` list: (1) `dotnet build` becomes
  `dtk dotnet build` in `droid` on the current release; (2) the shell running the hook on Windows; (3) with the user's
  own `settings.json` `PreToolUse` hooks, both theirs and dtk's run.
- [ ] **Step 5: CLAUDE.md** — after the Cursor/Devin paragraph:

```markdown
`dtk init droid` writes the shared `AGENTS.md` section and skill plus a `PreToolUse` entry for the `Execute` tool,
in the file `FactoryDroidHooks.ResolveTarget` picks (Droid merges `hooks.json` over `settings.json`'s `hooks` per
event key, so dtk writes where `PreToolUse` already lives; uninstall cleans every candidate). `--global` uses
`$FACTORY_HOME_OVERRIDE/.factory` or `~/.factory`. `dtk init crush` writes the section and skill plus a marked section
in `.crushrc` (or an existing `crushrc`), a Bash script Crush runs, so `CrushrcFile` edits only between its markers
and refuses a damaged section; `--global` uses `$CRUSH_GLOBAL_CONFIG`, `$XDG_CONFIG_HOME/crush` or `~/.config/crush`
(`crushrc`, `CRUSH.md`). `dtk hook crush` replies in Crush's own envelope and never with a `decision` (Crush's `allow`
skips its permission prompt). Doctor reads the `crushrc` registration as text (`HookInstallation.IsScriptRegistration`).
`eng/gates/crush-gate.sh` runs the real `crush` against a mock model to prove the rewrite.
```

- [ ] **Step 6: Spec** — in *PR 2a* replace the file-rule paragraph with the four-step rule and add
  `FACTORY_HOME_OVERRIDE`; in *Research summary: Crush* replace the Windows **Unverified** item with the verified path
  and the pinned minimum version; in *Live gates* note that gate C runs the release binary under a scratch `HOME`;
  set `Status:` to `approved; PR 1 merged (#168), PR 2 implemented`.
- [ ] **Step 7: Verify** — `dtk dotnet build DotnetTokenKiller.slnx`; `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`;
  `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests|FullyQualifiedName~ExamplesBindingTests|FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests|FullyQualifiedName~SavingsBaselineTests"`;
  `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`.
- [ ] **Step 8: Commit** — `docs: Factory Droid and Crush rewrite hooks`.

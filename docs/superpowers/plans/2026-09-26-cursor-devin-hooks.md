# Cursor and Devin rewrite hooks (PR 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `dtk init cursor` a `preToolUse` rewrite hook and a `--global` mode, and replace `dtk init windsurf` with a
`devin` provider (Devin Local / Devin CLI rewrite hook, `.devin/rules`), keeping `windsurf` as an alias.

**Architecture:** Two new `HookPayloadKind`s answered by `dtk hook cursor|devin` in `HookPayloads` (Devin shares a
tool-checked form of Claude's reply; Cursor has its own always-JSON reply gated by `IsAutoApprovable`). Cursor's flat
`hooks.json` gets a small dedicated reader/writer (`CursorHooksFile`); Devin reuses the matcher-group registration
helpers, extended to files whose event arrays sit at the root. `IntegrateUseCase` gains a provider alias table.

**Tech Stack:** .NET 10, C#, System.Text.Json `JsonNode`, xunit + FluentAssertions + NSubstitute, Spectre.Console.

**Spec:** `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md` (sections *Shared*, *PR 1a*, *PR 1b*,
*Verification*). PR 2 (Droid, Crush) and PR 3 (Kilo, Amp) get their own plans after this one merges.

## Global Constraints

- Branch: `feat/rewrite-harnesses` (already holds the spec). One PR against `develop`; the repo squash-merges.
- `TreatWarningsAsErrors` is on: every new member needs an XML doc comment (`///`), matching the surrounding code.
- File-scoped namespaces, `var`, `_camelCase` private fields, `Async` suffix, LF endings, 4-space indent.
- Build: `dtk dotnet build DotnetTokenKiller.slnx`. Tests: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~<Class>"`.
  If `dtk` reports "0 tests found" with `--filter` on the solution, run the filter against the test project instead
  (`tests/DotnetTokenKiller.Application.Tests`).
- Hook timeout registered for both harnesses: `10` seconds.
- Cursor hook entry, exactly: `{"command":"dtk hook cursor","matcher":"Shell","timeout":10}` under `hooks.preToolUse`, with `"version": 1`.
- Devin hook entry, exactly: `{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin","timeout":10}]}`
  under `PreToolUse` (at the root of `.devin/hooks.v1.json`; under `hooks` in `~/.config/devin/config.json`).
- Cursor reply rule (spec Q1): rewrite only when `DotnetCommandRewriter.IsAutoApprovable(original)` holds; the reply
  is `{"permission":"allow","updated_input":{…}}`; every other outcome prints exactly `{}`.
- Devin reply: Claude's `hookSpecificOutput.updatedInput` with no decision; nothing printed when there is nothing to rewrite.
- `windsurf` is an alias of `devin`; `AvailableProviders` lists canonical names only.
- The pre-commit hook formats staged `.cs` files; enable once with `git config core.hooksPath .githooks`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Cursor must never receive empty or non-JSON output from `dtk hook cursor` — empty stdin, `not json`, a BOM, a
   duplicate key, another tool, a chained command, `dotnet publish` all print `{}` (Cursor blocks the tool otherwise). Task 1.
2. A user's existing `~/.cursor/hooks.json` holding rtk's entry and no `version` keeps rtk's entry and gains
   `version: 1`; a `hooks.json` whose `hooks` is not an object fails with a clear message and is left untouched. Task 3.
3. `dtk doctor` on a healthy Cursor install passes: its probe must send single simple commands, because a chained probe
   is (correctly) not rewritten for Cursor. Task 5.
4. Upgrading a project with an edited `.windsurf/rules/dtk.md` keeps that file with a note — never reported as
   "skipped (use --force)", and never deleted. Task 6.
5. `dtk init Windsurf --uninstall` (any casing, with the alias) removes the Devin install and prints the alias note. Task 7.

---

### Task 1: `dtk hook cursor` and `dtk hook devin` replies

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum `HookPayloadKind`)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`

**Interfaces:**
- Produces: `HookPayloadKind.Cursor` (= 8), `HookPayloadKind.Devin` (= 9); `HookPayloads.TryGetKind("cursor"|"devin")`;
  `HookPayloads.CursorNeutralReply` (`internal const string`, value `{}`).

- [ ] **Step 1: Write the failing tests** — append to `HookPayloadsTests` (and add the two `InlineData` rows to
  `TryGetKind_KnownProvider_Resolves`):

```csharp
    [InlineData("cursor", HookPayloadKind.Cursor)]
    [InlineData("devin", HookPayloadKind.Devin)]
```

```csharp
    [Fact]
    public void Cursor_SimpleCommand_AllowsTheWholeToolInputWithTheCommandReplaced()
    {
        var reply = Reply(HookPayloadKind.Cursor,
            """{"tool_name":"Shell","tool_input":{"command":"dotnet build","working_directory":"/p"},"cwd":"/p"}""");

        var root = JsonNode.Parse(reply!)!.AsObject();
        root.Count.Should().Be(2);
        root["permission"]!.GetValue<string>().Should().Be("allow");
        root["updated_input"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        root["updated_input"]!["working_directory"]!.GetValue<string>().Should().Be("/p");
    }

    [Fact]
    public void Cursor_ProbeShapedPayloadWithoutToolName_IsRewritten()
    {
        var reply = Reply(HookPayloadKind.Cursor, """{"tool_input":{"command":"dotnet test"}}""");

        JsonNode.Parse(reply!)!["updated_input"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
    }

    [Theory]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dotnet build && dotnet test"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dotnet build | tee log"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dotnet publish"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dotnet pack"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"ls -la"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dtk dotnet build"}}""")]
    [InlineData("""{"tool_name":"Read","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":42,"tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"Shell","tool_input":{"command":"dotnet build","command":"x"}}""")]
    [InlineData("""{"tool_name":"Shell"}""")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("")]
    public void Cursor_AnythingElse_PrintsTheNeutralJsonReply(string payload)
    {
        Reply(HookPayloadKind.Cursor, payload).Should().Be("{}");
    }

    [Fact]
    public void Cursor_ByteOrderMark_IsIgnored()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("""{"tool_name":"Shell","tool_input":{"command":"dotnet build"}}"""))
            .ToArray();

        JsonNode.Parse(HookPayloads.Reply(HookPayloadKind.Cursor, bytes)!)!["permission"]!.GetValue<string>()
            .Should().Be("allow");
    }

    [Fact]
    public void Devin_ExecTool_ReturnsClaudesReplyWithoutADecision()
    {
        var reply = Reply(HookPayloadKind.Devin,
            """{"hook_event_name":"PreToolUse","tool_name":"exec","tool_input":{"command":"dotnet build","shell_id":"main"}}""");

        var output = JsonNode.Parse(reply!)!["hookSpecificOutput"]!.AsObject();
        output.Count.Should().Be(2, "no permissionDecision: Devin's own approval applies to the rewritten command");
        output["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        output["updatedInput"]!["shell_id"]!.GetValue<string>().Should().Be("main");
    }

    [Fact]
    public void Devin_ChainedCommand_IsRewrittenBecauseDevinStillAsks()
    {
        var reply = Reply(HookPayloadKind.Devin, """{"tool_name":"exec","tool_input":{"command":"dotnet build && dotnet publish"}}""");

        JsonNode.Parse(reply!)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build && dtk dotnet publish");
    }

    [Theory]
    [InlineData("""{"tool_name":"read","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"exec","tool_input":{"command":"ls"}}""")]
    [InlineData("not json")]
    public void Devin_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.Devin, payload).Should().BeNull();
    }

    [Fact]
    public void Devin_ProbeShapedPayloadWithoutToolName_IsRewritten()
    {
        Reply(HookPayloadKind.Devin, """{"tool_input":{"command":"dotnet build"}}""").Should().Contain("dtk dotnet build");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: build error — `HookPayloadKind` has no `Cursor`/`Devin`.

- [ ] **Step 3: Implement**

In `IHookIntegrator.cs`, after `OhMyPi = 7`:

```csharp
    /// <summary>dtk's own oh-my-pi extension payload, the same as <see cref="OpenCode"/>'s.</summary>
    OhMyPi = 7,

    /// <summary>Cursor's <c>preToolUse</c> payload, for its <c>Shell</c> tool.</summary>
    Cursor = 8,

    /// <summary>Devin Local's and Devin CLI's <c>PreToolUse</c> payload: Claude Code's shape, for the <c>exec</c> tool.</summary>
    Devin = 9
```

In `HookPayloads.cs`:

1. Add constants after `AntigravityNeutralReply`:

```csharp
    /// <summary>
    /// Cursor's reply when there is nothing to rewrite. Never empty: Cursor blocks the tool call when a permission
    /// hook's output does not match its schema (cursor.com/docs/hooks.md), and <c>{}</c> is what rtk's Cursor hook
    /// prints for the same case.
    /// </summary>
    internal const string CursorNeutralReply = "{}";

    /// <summary>Cursor's shell tool.</summary>
    private const string CursorShellTool = "Shell";

    /// <summary>Devin's shell tool.</summary>
    private const string DevinShellTool = "exec";

    /// <summary>The payload property naming the tool in Claude-shaped payloads (Claude Code, Codex CLI, Cursor, Devin).</summary>
    private const string ToolNameProperty = "tool_name";
```

2. `TryGetKind`: add `"cursor" => (true, HookPayloadKind.Cursor),` and `"devin" => (true, HookPayloadKind.Devin),`,
   and extend its `<summary>` provider list with `cursor`, `devin`.

3. In `Reply`, the parse catch becomes:

```csharp
        catch (JsonException)
        {
            return kind == HookPayloadKind.Cursor ? CursorNeutralReply : null;
        }
```

   the dispatch becomes:

```csharp
            return kind switch
            {
                HookPayloadKind.ClaudeCode => ReplyWithUpdatedInput(root, expectedTool: null),
                HookPayloadKind.Devin => ReplyWithUpdatedInput(root, DevinShellTool),
                HookPayloadKind.Cursor => ReplyToCursor(root),
                HookPayloadKind.GeminiCli => ReplyToGemini(root),
                HookPayloadKind.CopilotCli => ReplyToCopilot(root),
                HookPayloadKind.CodexCli => ReplyToCodex(root),
                HookPayloadKind.OpenCode or HookPayloadKind.Pi or HookPayloadKind.OhMyPi => ReplyToOpenCode(root),
                HookPayloadKind.AntigravityCli => ReplyToAntigravity(root),
                _ => null
            };
```

   and the exception-path return becomes:

```csharp
            return kind switch
            {
                HookPayloadKind.GeminiCli => GeminiAllowReply,
                HookPayloadKind.Cursor => CursorNeutralReply,
                _ => null
            };
```

4. Replace `ReplyToClaude` with:

```csharp
    /// <summary>
    /// Claude Code's reply, which Devin Local and Devin CLI share: the whole tool input with the command replaced, and
    /// no permission decision, so the harness still applies its own approval to the rewritten command.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    /// <param name="expectedTool">
    /// The harness's shell tool, or <see langword="null"/> to skip the check (Claude Code's matcher already selects
    /// <c>Bash</c>). A payload without <c>tool_name</c> — doctor's probe — always passes.
    /// </param>
    private static string? ReplyWithUpdatedInput(JsonNode? root, string? expectedTool)
    {
        if (root is not JsonObject payload
            || (expectedTool is not null && NamesAnotherTool(payload, ToolNameProperty, expectedTool))
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput[CommandProperty] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    /// <summary>
    /// Replies to Cursor's <c>preToolUse</c>. Cursor enforces only <c>allow</c> and <c>deny</c> there, and a rewrite
    /// must carry <c>allow</c>, which may also skip Cursor's own approval; so, like Copilot CLI's reply, dtk rewrites
    /// only commands <see cref="DotnetCommandRewriter.IsAutoApprovable"/> accepts and leaves every other command to
    /// the agent, answering <see cref="CursorNeutralReply"/>.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string ReplyToCursor(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, ToolNameProperty, CursorShellTool)
            || payload[ToolInputProperty] is not JsonObject toolInput
            || !TryRewrite(toolInput, out var command, out var rewritten)
            || !DotnetCommandRewriter.IsAutoApprovable(command))
        {
            return CursorNeutralReply;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput[CommandProperty] = rewritten;
        return new JsonObject { ["permission"] = "allow", ["updated_input"] = updatedInput }.ToJsonString();
    }
```

5. In `ReplyToCodex`, replace the literal `"tool_name"` with `ToolNameProperty`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: PASS, including every pre-existing Claude test (its reply is unchanged byte for byte).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs
git commit -m "feat: dtk hook cursor and dtk hook devin replies

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Hook registrations whose event arrays sit at the file root

Devin's project file `.devin/hooks.v1.json` *is* the hooks object (`{"PreToolUse":[…]}`), with no `hooks` container.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (`HookRegistrationSpec`, `MergeJsonSettingsAsync`)
- Modify: `src/DotnetTokenKiller.Application/Integration/UninstallHelpers.cs` (`RemoveHookRegistrationAsync`)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallHelpersTests.cs`

**Interfaces:**
- Produces: `HookRegistrationSpec.ContainerKey` becomes `string?` (default still `"hooks"`); `null` means "event
  arrays at the root". `MergeJsonSettingsAsync(string path, string? containerKey, …)` likewise.

- [ ] **Step 1: Write the failing tests**

In `IntegratorHelpersTests` (the class already has a temp-dir fixture; use its path helper — shown here as `_tempDir`):

```csharp
    [Fact]
    public async Task WriteHookRegistrationAsync_NullContainer_WritesTheEventArrayAtTheRoot()
    {
        var path = Path.Combine(_tempDir, ".devin", "hooks.v1.json");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null), context, default);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PreToolUse");
        root["PreToolUse"]![0]!["matcher"]!.GetValue<string>().Should().Be("exec");
        root["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook devin");
        context.Created.Should().Equal(path);
    }

    [Fact]
    public async Task WriteHookRegistrationAsync_NullContainer_KeepsOtherRootEvents()
    {
        var path = Path.Combine(_tempDir, "hooks.v1.json");
        await File.WriteAllTextAsync(path,
            """{"PostToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"lint"}]}]}""");

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null),
            new IntegrationContext(force: false), default);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PostToolUse", "PreToolUse");
    }
```

In `UninstallHelpersTests` (same fixture convention):

```csharp
    [Fact]
    public async Task RemoveHookRegistrationAsync_NullContainer_RemovesDtkAndKeepsOtherRootEvents()
    {
        var path = Path.Combine(_tempDir, "hooks.v1.json");
        await File.WriteAllTextAsync(path, """
            {"PostToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"lint"}]}],
             "PreToolUse":[{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin","timeout":10}]}]}
            """);
        var context = IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

        await UninstallHelpers.RemoveHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null), context, default);

        JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject().Select(pair => pair.Key).Should().Equal("PostToolUse");
        context.Updated.Should().Equal(path);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_NullContainer_DeletesTheFileItEmpties()
    {
        var path = Path.Combine(_tempDir, ".devin", "hooks.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            """{"PreToolUse":[{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin","timeout":10}]}]}""");
        var context = IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

        await UninstallHelpers.RemoveHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null), context, default);

        File.Exists(path).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(path)).Should().BeFalse("the uninstall prunes the directory it empties");
    }
```

If either test class names its temp directory differently, use that name; do not add a second fixture.

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegratorHelpersTests|FullyQualifiedName~UninstallHelpersTests"`
Expected: build error — `ContainerKey` cannot be `null`.

- [ ] **Step 3: Implement**

`IntegratorHelpers.cs`:

```csharp
/// <summary>A hook registration merged into a settings file.</summary>
/// <param name="ContainerKey">
/// The top-level property holding the event arrays (<c>"hooks"</c> for most harnesses), or <see langword="null"/>
/// when the event arrays sit at the file's root, as in Devin's <c>.devin/hooks.v1.json</c>.
/// </param>
internal sealed record HookRegistrationSpec(
    string SettingsPath, string EventKey, string Matcher, string Command, int? TimeoutSeconds = null, string? ContainerKey = "hooks");
```

(Keep any existing doc comment on the record and add the `ContainerKey` param line to it.)

In `MergeJsonSettingsAsync(string path, string? containerKey, …)` — change the parameter type, document
`null` in its `<param name="containerKey">`, and replace the container lookup and the final assignments:

```csharp
        JsonObject hooks;
        if (containerKey is null)
        {
            hooks = root;
        }
        else
        {
            root.TryGetPropertyValue(containerKey, out var hooksNode);
            hooks = hooksNode switch
            {
                null => [],
                JsonObject hooksObj => hooksObj,
                _ => throw new InvalidOperationException(
                    $"The settings file '{path}' has a '{containerKey}' property of unexpected type '{hooksNode.GetType().Name}'; expected a JSON object.")
            };
        }

        var eventPath = containerKey is null ? hookEventKey : $"{containerKey}.{hookEventKey}";
        hooks.TryGetPropertyValue(hookEventKey, out var eventNode);
        var hookArray = eventNode switch
        {
            null => [],
            JsonArray arr => arr,
            _ => throw new InvalidOperationException(
                $"The settings file '{path}' has a '{eventPath}' property of unexpected type '{eventNode.GetType().Name}'; expected a JSON array.")
        };
```

and at the end:

```csharp
        hooks[hookEventKey] = hookArray;
        if (containerKey is not null)
        {
            root[containerKey] = hooks;
        }
```

`UninstallHelpers.RemoveHookRegistrationAsync`:

```csharp
        var container = spec.ContainerKey is null ? root : root[spec.ContainerKey] as JsonObject;
        if (container is null
            || container[spec.EventKey] is not JsonArray hookArray
            || IntegratorHelpers.FindEquivalentEntries(hookArray, spec.Command) is not { Count: > 0 } matches)
        {
            context.Unchanged.Add(path);
            return;
        }

        IntegratorHelpers.RemoveEntries(hookArray, matches);

        if (hookArray.Count == 0)
        {
            container.Remove(spec.EventKey);
        }

        if (spec.ContainerKey is not null && container.Count == 0)
        {
            root.Remove(spec.ContainerKey);
        }
```

(the rest — delete when `root.Count == 0`, else write — is unchanged).

- [ ] **Step 4: Run the tests to verify they pass** (same command; every existing helper test must still pass).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs src/DotnetTokenKiller.Application/Integration/UninstallHelpers.cs tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs tests/DotnetTokenKiller.Application.Tests/Integration/UninstallHelpersTests.cs
git commit -m "feat: hook registrations with root-level event arrays

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `CursorHooksFile` — dtk's entry in Cursor's flat `hooks.json`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/CursorHooksFile.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CursorHooksFileTests.cs`

**Interfaces:**
- Produces:
  - `CursorHooksFile.WriteAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken) : Task`
  - `CursorHooksFile.RemoveAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken) : Task`
  - `CursorHooksFile.TimeoutSeconds` (`internal const int` = 10), `CursorHooksFile.ShellMatcher` (`"Shell"`)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CursorHooksFileTests : IDisposable
{
    private const string Command = "dtk hook cursor";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-cursor-hooks-{Guid.NewGuid()}");

    private string HooksPath => Path.Combine(_tempDir, ".cursor", "hooks.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private async Task<JsonObject> ReadAsync() => JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!.AsObject();

    private async Task SeedAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath, json);
    }

    private static IntegrationContext Uninstall(string boundary) =>
        IntegrationContext.ForUninstall(boundary, new Dictionary<string, string>());

    [Fact]
    public async Task WriteAsync_NoFile_CreatesVersionOneWithDtksShellEntry()
    {
        var context = new IntegrationContext(force: false);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        var root = await ReadAsync();
        root["version"]!.GetValue<int>().Should().Be(1);
        var entry = root["hooks"]!["preToolUse"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        entry["command"]!.GetValue<string>().Should().Be(Command);
        entry["matcher"]!.GetValue<string>().Should().Be("Shell");
        entry["timeout"]!.GetValue<int>().Should().Be(10);
        context.Created.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task WriteAsync_RtkEntryWithoutVersion_KeepsRtkAndAddsVersion()
    {
        await SeedAsync("""{"hooks":{"preToolUse":[{"command":"rtk hook cursor"}],"stop":[{"command":"notify"}]}}""");
        var context = new IntegrationContext(force: false);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        var root = await ReadAsync();
        root["version"]!.GetValue<int>().Should().Be(1);
        root["hooks"]!["preToolUse"]!.AsArray().Select(e => e!["command"]!.GetValue<string>())
            .Should().Equal("rtk hook cursor", Command);
        root["hooks"]!["stop"]!.AsArray().Should().ContainSingle();
        context.Updated.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task WriteAsync_SecondRun_ReportsUnchanged()
    {
        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);
        var before = await File.ReadAllTextAsync(HooksPath);
        var context = new IntegrationContext(force: true);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().Equal(HooksPath);
        (await File.ReadAllTextAsync(HooksPath)).Should().Be(before);
    }

    [Fact]
    public async Task WriteAsync_DuplicatedDtkEntries_KeepsTheFirst()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"dtk hook cursor","timeout":5},{"command":"dtk hook cursor"}]}}""");

        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);

        var entry = (await ReadAsync())["hooks"]!["preToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        entry["timeout"]!.GetValue<int>().Should().Be(5, "the surviving entry keeps the user's own settings");
    }

    [Theory]
    [InlineData("""{"hooks":[]}""")]
    [InlineData("""{"hooks":{"preToolUse":{}}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task WriteAsync_UnexpectedShape_ThrowsAndLeavesTheFileAlone(string json)
    {
        await SeedAsync(json);

        var act = () => CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{HooksPath}*");
        (await File.ReadAllTextAsync(HooksPath)).Should().Be(json);
    }

    [Fact]
    public async Task RemoveAsync_KeepsOtherEntries()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"},{"command":"dtk hook cursor","matcher":"Shell","timeout":10}]}}""");
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        (await ReadAsync())["hooks"]!["preToolUse"]!.AsArray().Select(e => e!["command"]!.GetValue<string>())
            .Should().Equal("rtk hook cursor");
        context.Updated.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_LastEntry_DeletesTheFileAndPrunesTheDirectory()
    {
        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        File.Exists(HooksPath).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(HooksPath)).Should().BeFalse();
        context.Removed.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_NoDtkEntry_ReportsUnchanged()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"}]}}""");
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_NoFile_DoesNothing()
    {
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().BeEmpty();
        context.Removed.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CursorHooksFileTests"`
Expected: build error — `CursorHooksFile` does not exist.

- [ ] **Step 3: Implement** `CursorHooksFile.cs`:

```csharp
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Reads and writes dtk's entry in Cursor's <c>hooks.json</c>:
/// <c>{"version":1,"hooks":{"preToolUse":[{"command":"dtk hook cursor","matcher":"Shell","timeout":10}]}}</c>.
/// </summary>
/// <remarks>
/// Cursor's entries are flat — one object per hook carrying its own <c>command</c> — unlike the matcher groups of
/// Claude Code's settings, so <see cref="IntegratorHelpers.WriteHookRegistrationAsync"/> does not apply. dtk's entry
/// is any entry whose command is exactly dtk's; every other entry, rtk's included, and every other event is kept.
/// </remarks>
internal static class CursorHooksFile
{
    /// <summary>Seconds Cursor waits for the hook.</summary>
    internal const int TimeoutSeconds = 10;

    /// <summary>The matcher selecting Cursor's shell tool.</summary>
    internal const string ShellMatcher = "Shell";

    private const string EventKey = "preToolUse";
    private const string HooksKey = "hooks";
    private const string VersionKey = "version";
    private const string CommandKey = "command";

    /// <summary>Adds dtk's entry, keeping everything else; reports the file unchanged when it is already there.</summary>
    /// <param name="path">The <c>hooks.json</c> to merge into.</param>
    /// <param name="command">The command dtk registers.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file is not JSON, or <c>hooks</c>/<c>hooks.preToolUse</c> has an unexpected type.</exception>
    internal static async Task WriteAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);
        var root = await IntegratorHelpers.ReadRootObjectAsync(path, exists, cancellationToken).ConfigureAwait(false);
        var hookArray = EventArray(root, path, create: true)!;
        var matches = Matches(hookArray, command);

        if (matches.Count == 1 && root[VersionKey] is not null)
        {
            context.Unchanged.Add(path);
            return;
        }

        if (matches.Count == 0)
        {
            // The JsonNode overload: Add<JsonObject> is neither trim- nor AOT-safe.
            hookArray.Add((JsonNode)new JsonObject
            {
                [CommandKey] = command,
                ["matcher"] = ShellMatcher,
                ["timeout"] = TimeoutSeconds
            });
        }

        foreach (var duplicate in matches.Skip(1))
        {
            hookArray.Remove(duplicate);
        }

        root[VersionKey] ??= 1;

        await IntegratorHelpers.WriteSettingsJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
        (exists ? context.Updated : context.Created).Add(path);
    }

    /// <summary>
    /// Removes dtk's entry; drops the event and <c>hooks</c> when that empties them, and deletes the file when nothing
    /// but <c>version</c> is left.
    /// </summary>
    /// <param name="path">The <c>hooks.json</c> to remove dtk's entry from.</param>
    /// <param name="command">The command dtk registers.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file is not JSON, or has an unexpected shape.</exception>
    internal static async Task RemoveAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var root = await IntegratorHelpers.ReadRootObjectAsync(path, exists: true, cancellationToken).ConfigureAwait(false);
        if (EventArray(root, path, create: false) is not { } hookArray || Matches(hookArray, command) is not { Count: > 0 } matches)
        {
            context.Unchanged.Add(path);
            return;
        }

        foreach (var match in matches)
        {
            hookArray.Remove(match);
        }

        var hooks = (JsonObject)root[HooksKey]!;
        if (hookArray.Count == 0)
        {
            hooks.Remove(EventKey);
        }

        if (hooks.Count == 0)
        {
            root.Remove(HooksKey);
        }

        if (root.Count == 0 || (root.Count == 1 && root.ContainsKey(VersionKey)))
        {
            UninstallHelpers.DeleteFile(path, context);
            return;
        }

        await IntegratorHelpers.WriteSettingsJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>The <c>hooks.preToolUse</c> array, created when <paramref name="create"/> is set, else <see langword="null"/> when absent.</summary>
    private static JsonArray? EventArray(JsonObject root, string path, bool create)
    {
        if (root[HooksKey] is null)
        {
            if (!create)
            {
                return null;
            }

            root[HooksKey] = new JsonObject();
        }

        if (root[HooksKey] is not JsonObject hooks)
        {
            throw new InvalidOperationException($"The Cursor hooks file '{path}' has a 'hooks' property that is not a JSON object.");
        }

        if (hooks[EventKey] is null)
        {
            if (!create)
            {
                return null;
            }

            hooks[EventKey] = new JsonArray();
        }

        return hooks[EventKey] as JsonArray
            ?? throw new InvalidOperationException($"The Cursor hooks file '{path}' has a 'hooks.{EventKey}' property that is not a JSON array.");
    }

    /// <summary>The entries whose command is exactly <paramref name="command"/>, in file order.</summary>
    private static List<JsonObject> Matches(JsonArray hookArray, string command) =>
        [.. hookArray.OfType<JsonObject>().Where(entry =>
            entry[CommandKey] is JsonValue value && value.TryGetValue<string>(out var text) && text == command)];
}
```

`ReadRootObjectAsync` already throws `InvalidOperationException` naming the path for non-JSON and non-object roots.

- [ ] **Step 4: Run the tests to verify they pass** (same command).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/CursorHooksFile.cs tests/DotnetTokenKiller.Application.Tests/Integration/CursorHooksFileTests.cs
git commit -m "feat: read and write dtk's entry in Cursor's hooks.json

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `dtk init cursor` installs the hook, with `--global`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs` (add `CursorDir`)
- Create: `src/DotnetTokenKiller.Application/Integration/ImportedClaudeHook.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/CursorIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` (no line change needed; constructor injection resolves — verify it builds)
- Modify tests: `tests/DotnetTokenKiller.Application.Tests/Integration/CursorIntegratorTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/ReleasedOwnedFileHashesTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallIntegrationTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs`

**Interfaces:**
- Consumes: `CursorHooksFile` (Task 3), `HookPayloadKind.Cursor` (Task 1).
- Produces: `internal sealed class CursorIntegrator(RtkHookCoexistence rtk, HomePaths home)` implementing
  `IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator`; notes `CursorIntegrator.AutoApprovalNote`,
  `TrustNote`, `KnownGapsNote`, `GlobalRuleNote`; `HomePaths.CursorDir`;
  `ImportedClaudeHook.SettingsFiles(HomePaths home, string? projectDirectory) : IReadOnlyList<string>`,
  `ImportedClaudeHook.IsRegisteredIn(IEnumerable<string> files) : bool`, `ImportedClaudeHook.Note(string harness) : string`.

- [ ] **Step 1: Write the failing tests**

`HomePathsTests` — add:

```csharp
    [Fact]
    public void CursorDir_IsDotCursorUnderHome()
    {
        new HomePaths("/home/u").CursorDir.Should().Be(Path.Combine("/home/u", ".cursor"));
    }
```

Replace `CursorIntegratorTests.cs` entirely:

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CursorIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-cursor-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string RulePath => Path.Combine(ProjectDir, ".cursor", "rules", "dtk.mdc");
    private string ProjectHooksPath => Path.Combine(ProjectDir, ".cursor", "hooks.json");
    private string GlobalHooksPath => Path.Combine(HomeDir, ".cursor", "hooks.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CursorIntegrator CreateSut()
    {
        var home = new HomePaths(HomeDir);
        return new CursorIntegrator(new RtkHookCoexistence(home.ClaudeDir, RtkConfigPath), home);
    }

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public void ProviderName_IsCursor() => CreateSut().ProviderName.Should().Be("cursor");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesRuleAndHookWithNotes()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().Equal(CursorIntegrator.AutoApprovalNote, CursorIntegrator.TrustNote, CursorIntegrator.KnownGapsNote);
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksPath))!["hooks"]!["preToolUse"]![0]!;
        entry["command"]!.GetValue<string>().Should().Be("dtk hook cursor");
        (await File.ReadAllTextAsync(RulePath)).Should().Contain("alwaysApply: false");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesOnlyTheHomeHookAndTheRuleNote()
    {
        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(GlobalHooksPath);
        result.Notes.Should().Equal(CursorIntegrator.AutoApprovalNote, CursorIntegrator.KnownGapsNote, CursorIntegrator.GlobalRuleNote);
        Directory.Exists(ProjectDir).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_PointsAtTheScopesHooksJson(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(global ? GlobalHooksPath : ProjectHooksPath);
        hook.Command.Should().Be("dtk hook cursor");
        hook.PayloadKind.Should().Be(HookPayloadKind.Cursor);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RtkCursorHook_ExcludesDotnetInRtksConfig()
    {
        await WriteAsync(GlobalHooksPath, """{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"}]}}""");

        await CreateSut().IntegrateGlobalAsync(false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateAsync_LegacyRtkCursorScript_ExcludesDotnetInRtksConfig()
    {
        await WriteAsync(Path.Combine(HomeDir, ".cursor", "hooks", "rtk-rewrite.sh"),
            "#!/usr/bin/env bash\n# all rewrite logic lives in `rtk rewrite`\n");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateAsync_DtkClaudeHookInUserSettings_AddsTheImportNote()
    {
        await WriteAsync(Path.Combine(HomeDir, ".claude", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook claude"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(ImportedClaudeHook.Note("Cursor"));
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesRuleAndHookAndPrunes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().BeEquivalentTo([RulePath, ProjectHooksPath]);
        Directory.Exists(Path.Combine(ProjectDir, ".cursor")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_Global_KeepsOtherHooks()
    {
        await WriteAsync(GlobalHooksPath, """{"version":1,"hooks":{"preToolUse":[{"command":"audit"}]}}""");
        await CreateSut().IntegrateGlobalAsync(false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Global, new Dictionary<string, string>(), default);

        result.UpdatedFiles.Should().Equal(GlobalHooksPath);
        (await File.ReadAllTextAsync(GlobalHooksPath)).Should().Contain("audit").And.NotContain("dtk hook cursor");
    }
}
```

`ReleasedOwnedFileHashesTests`: replace every `new CursorIntegrator()` with
`new CursorIntegrator(new RtkHookCoexistence(Path.Combine(ProjectDir, "no-claude"), Path.Combine(ProjectDir, "no-rtk.toml")), new HomePaths(Path.Combine(ProjectDir, "no-home")))`
— extract it once as a private `static CursorIntegrator Cursor(string root)` helper in that class. In
`CurrentCursorRule_MatchesThePinnedHash` the install now also writes `.cursor/hooks.json`; the test hashes only
`dtk.mdc`, so it stays valid.

`UninstallIntegrationTests`:
- In `Integrators()`, `new CursorIntegrator()` → `new CursorIntegrator(Rtk(), home)`.
- Add `"cursor"` to `GlobalProviders`.
- `Uninstall_RepositoryScopedProviderWithGlobal_Throws`: `"cursor"` → `"jetbrains"`.
- In `Doctor_AfterUninstallingEveryHook_ReportsNoHookInstalled`, add `"cursor"` to `hookProviders`.

`InitCommandTests.RunAsync_UninstallGlobalForARepositoryScopedProvider_FailsWithTheReason`: `new CursorIntegrator()`
→ `new JetBrainsAiIntegrator()` and `Provider = "cursor"` → `Provider = "jetbrains"`.

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CursorIntegratorTests|FullyQualifiedName~HomePathsTests"`
Expected: build errors — no `CursorDir`, no two-argument `CursorIntegrator` constructor, no `ImportedClaudeHook`.

- [ ] **Step 3: Implement**

`HomePaths.cs`, after `ClaudeDir`:

```csharp
    /// <summary>Gets Cursor's user directory (<c>~/.cursor</c>), which holds its user-level <c>hooks.json</c>.</summary>
    internal string CursorDir => Path.Combine(Home, ".cursor");
```

`ImportedClaudeHook.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Cursor and Devin also run the hooks registered in Claude Code's settings files, so dtk's Claude Code hook may fire
/// there beside the harness's own. Both rewrite the same command at most once between them, because
/// <see cref="DotnetCommandRewriter"/> never rewrites a command <c>dtk</c> already runs; the install says so.
/// </summary>
internal static class ImportedClaudeHook
{
    /// <summary>The Claude Code settings files a harness importing them reads.</summary>
    /// <param name="home">Resolves the user's Claude directory.</param>
    /// <param name="projectDirectory">The project root, or <see langword="null"/> for a global install.</param>
    internal static IReadOnlyList<string> SettingsFiles(HomePaths home, string? projectDirectory)
    {
        List<string> files =
            [Path.Combine(home.ClaudeDir, "settings.json"), Path.Combine(home.ClaudeDir, "settings.local.json")];
        if (projectDirectory is not null)
        {
            files.Add(Path.Combine(projectDirectory, ".claude", "settings.json"));
            files.Add(Path.Combine(projectDirectory, ".claude", "settings.local.json"));
        }

        return files;
    }

    /// <summary>Whether any of <paramref name="files"/> registers dtk's Claude Code hook; unreadable files count as no.</summary>
    /// <param name="files">The settings files to search.</param>
    internal static bool IsRegisteredIn(IEnumerable<string> files) => files.Any(Mentions);

    /// <summary>The note printed when <paramref name="harness"/> will also run dtk's Claude Code hook.</summary>
    /// <param name="harness">The harness's display name.</param>
    internal static string Note(string harness) =>
        $"{harness} also runs Claude Code's hooks, and dtk's Claude Code hook is registered: both may see the same "
        + "command, which is harmless — dtk never rewrites a command dtk already runs.";

    private static bool Mentions(string path)
    {
        try
        {
            return File.Exists(path)
                && File.ReadAllText(path).Contains(HookCommands.Invocation("claude"), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

`CursorIntegrator.cs` — replace the class (keep `CursorRule` byte for byte):

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Cursor (the IDE and <c>cursor-agent</c>).</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.cursor/rules/dtk.mdc</c> (Cursor project rule; project scope only)</description></item>
///   <item><description>
///     <c>.cursor/hooks.json</c> or <c>~/.cursor/hooks.json</c> registering <c>dtk hook cursor</c> under
///     <c>preToolUse</c> for the <c>Shell</c> tool (merged, never overwritten)
///   </description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CursorIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook: Cursor's rewrite carries an approval, so dtk narrows what it rewrites.</summary>
    internal const string AutoApprovalNote =
        "Cursor approves a command its hook rewrites, so dtk rewrites only simple `dotnet build`, `test`, `restore`, "
        + "`clean`, `format` and `list package` commands; chained or piped commands, `publish` and `pack` run as written.";

    /// <summary>Printed when this run wrote a project hook.</summary>
    internal const string TrustNote =
        "Cursor runs project hooks only in trusted workspaces, and cursor-agent needs --trust when it runs headless.";

    /// <summary>Printed when this run wrote the hook: the rewrites Cursor is known to drop.</summary>
    internal const string KnownGapsNote =
        "Cursor sends hooks no payload in remote Linux workspaces and may drop rewrites for subagents' shell calls; "
        + "those commands run as written, guided only by the rule.";

    /// <summary>Printed by a global install, which has no rule file to write.</summary>
    internal const string GlobalRuleNote =
        "Cursor keeps user rules in its settings, not in a file, so the global install adds only the hook. "
        + "Run 'dtk init cursor' in a project to add the rule there.";

    private static readonly string CursorRule =
        $"""
        ---
        description: Use dtk instead of dotnet for {IntegrationInstructions.SubcommandProse} commands
        globs:
          - "**/*.cs"
          - "**/*.csproj"
          - "**/*.slnx"
          - "**/*.sln"
        alwaysApply: false
        ---

        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <inheritdoc/>
    public string ProviderName => "cursor";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            Path.Combine(scope == HookScope.Global ? home.CursorDir : Path.Combine(directory, ".cursor"), "hooks.json"),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Cursor)
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), CursorRule, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(directory, HookScope.Project, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await WriteHookAsync(home.Home, HookScope.Global, context, cancellationToken).ConfigureAwait(false);
        context.Notes.Add(GlobalRuleNote);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        if (scope == HookScope.Project)
        {
            await UninstallHelpers.RemoveOwnedFileAsync(
                RulePath(directory), CursorRule, IntegrationInstructions.ReleasedCursorRuleHashes, "dtk init cursor", context,
                cancellationToken).ConfigureAwait(false);
        }

        var hook = DescribeHooks(root, scope)[0];
        await CursorHooksFile.RemoveAsync(hook.RegistrationPath, hook.Command, context, cancellationToken).ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(root)));

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".cursor", "rules", "dtk.mdc");

    private async Task WriteHookAsync(string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        await CursorHooksFile.WriteAsync(hook.RegistrationPath, hook.Command, context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(AutoApprovalNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(TrustNote);
            }

            context.Notes.Add(KnownGapsNote);
        }

        if (ImportedClaudeHook.IsRegisteredIn(ImportedClaudeHook.SettingsFiles(home, scope == HookScope.Project ? directory : null)))
        {
            context.Notes.Add(ImportedClaudeHook.Note("Cursor"));
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);
    }

    /// <summary>
    /// Where rtk registers itself for Cursor: <c>rtk hook cursor</c> in either <c>hooks.json</c>, or (rtk before its
    /// native hook) the <c>rtk-rewrite.sh</c> script, which runs <c>rtk rewrite</c>.
    /// </summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    private List<string> RtkCandidates(string directory) =>
        [.. new[]
        {
            Path.Combine(directory, ".cursor", "hooks.json"),
            Path.Combine(home.CursorDir, "hooks.json"),
            Path.Combine(home.CursorDir, "hooks", "rtk-rewrite.sh")
        }.Distinct(StringComparer.Ordinal)];
}
```

(`DependencyInjection.cs` keeps `services.AddTransient<IProviderIntegrator, CursorIntegrator>();` — the container
supplies `RtkHookCoexistence` and `HomePaths`, as for `CodexIntegrator`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CursorIntegratorTests|FullyQualifiedName~HomePathsTests|FullyQualifiedName~ReleasedOwnedFileHashesTests|FullyQualifiedName~UninstallIntegrationTests"`
then `dtk dotnet build DotnetTokenKiller.slnx`.
Expected: PASS; build clean (warnings are errors).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs
git commit -m "feat: dtk init cursor installs a preToolUse rewrite hook, with --global

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `dtk doctor` probes Cursor with single simple commands

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`ProbeAsync`, `BuildPayload`)
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Consumes: `CursorIntegrator` (Task 4), `DotnetCommandRewriter.AutoApprovableSubcommands`.

- [ ] **Step 1: Write the failing test** (add to `HookHealthCheckerTests`):

```csharp
    private CursorIntegrator Cursor => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);

    [Fact]
    public async Task RunAsync_CursorHook_ProbesEachAutoApprovableSubcommandAlone()
    {
        await Cursor.IntegrateAsync(_tempDir, force: false, default);
        _runner.RunCapturedWithInputAsync(null!, null!, null!).ReturnsForAnyArgs(call =>
        {
            var command = JsonNode.Parse(call.ArgAt<string>(2))!["tool_input"]!["command"]!.GetValue<string>();
            return new CommandResult($$"""{"permission":"allow","updated_input":{"command":"dtk {{command}}"}}""", string.Empty, 0);
        });

        var checks = await _sut.RunAsync([Cursor], _tempDir, default);

        checks.Should().Contain(c => c.Name == "cursor hook probe (project)" && c.Passed);
        await _runner.Received(DotnetCommandRewriter.AutoApprovableSubcommands.Count).RunCapturedWithInputAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "hook", "cursor" })),
            Arg.Is<string>(payload => !payload.Contains(';', StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
```

Add `using System.Text.Json.Nodes;` and `using DotnetTokenKiller.Application.Integration.Hooks;` if absent.

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthCheckerTests"`
Expected: FAIL — one chained probe run instead of six, and `publish`/`pack` reported missing.

- [ ] **Step 3: Implement** in `HookHealthChecker.cs`:

Add:

```csharp
    /// <summary>
    /// The subcommands a probe expects rewritten: Cursor's hook rewrites only what
    /// <see cref="DotnetCommandRewriter.IsAutoApprovable"/> accepts, every other hook all of them.
    /// </summary>
    /// <param name="kind">The hook's payload shape.</param>
    private static IReadOnlyList<string> ProbedSubcommands(HookPayloadKind kind) =>
        kind == HookPayloadKind.Cursor ? DotnetCommandRewriter.AutoApprovableSubcommands : DotnetSubcommands.Ordered;

    /// <summary>
    /// The commands one probe sends, one hook run each: a single chained command normally, but one simple command per
    /// subcommand for Cursor, whose hook leaves chained commands alone.
    /// </summary>
    /// <param name="kind">The hook's payload shape.</param>
    private static IReadOnlyList<string> ProbeCommands(HookPayloadKind kind) =>
        kind == HookPayloadKind.Cursor
            ? [.. ProbedSubcommands(kind).Select(sub => $"dotnet {sub}")]
            : [string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"))];
```

In `ProbeAsync`, replace the single run with a loop (keep the timeout/exception handling around it unchanged):

```csharp
        var expected = ProbedSubcommands(installation.PayloadKind);
        var output = new StringBuilder();

        try
        {
            foreach (var command in ProbeCommands(installation.PayloadKind))
            {
                var result = await runner
                    .RunCapturedWithInputAsync(
                        dtk,
                        [HookCommands.Verb, installation.ProviderName],
                        BuildPayload(installation.PayloadKind, command),
                        timeout.Token)
                    .ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    return new DiagnosticCheck(
                        name,
                        false,
                        $"'{hook}' exited with code {result.ExitCode}: {FirstErrorLine(result)} "
                        + $"A dtk older than 'dtk hook' cannot answer it; run '{UpdateCommand}'.");
                }

                output.Append(result.StdOut);
            }

            var missing = expected
                .Where(sub => !output.ToString().Contains($"dtk dotnet {sub}", StringComparison.Ordinal))
                .ToList();

            return missing.Count == 0
                ? new DiagnosticCheck(name, true, $"{dtk} rewrites all {expected.Count} subcommands")
                : new DiagnosticCheck(
                    name,
                    false,
                    $"{dtk} does not rewrite: {string.Join(", ", missing)}. It is older than this dtk; run '{UpdateCommand}'.");
        }
```

Delete the old `var command = string.Join(...)` line. Add `using System.Text;` and
`using DotnetTokenKiller.Application.Integration.Hooks;` if absent. `BuildPayload`'s default branch
(`{"tool_input":{"command":…}}`) already fits Cursor and Devin; add both kinds to its `<summary>` only if it
enumerates kinds.

- [ ] **Step 4: Run the tests to verify they pass** (same command; every existing doctor test must still pass — the
  other kinds still make exactly one run).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs
git commit -m "feat: doctor probes Cursor's hook one simple command at a time

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `dtk init devin` (replaces the Windsurf integrator)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs` (add `DevinConfigDir`, `WindsurfGlobalRulesPath`)
- Delete: `src/DotnetTokenKiller.Application/Integration/WindsurfIntegrator.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/DevinIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Delete: `tests/DotnetTokenKiller.Application.Tests/Integration/WindsurfIntegratorTests.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/DevinIntegratorTests.cs`
- Modify tests: `HomePathsTests.cs`, `ReleasedOwnedFileHashesTests.cs`, `UninstallIntegrationTests.cs`

**Interfaces:**
- Consumes: `HookRegistrationSpec(..., ContainerKey: null)` (Task 2), `HookPayloadKind.Devin` (Task 1), `ImportedClaudeHook` (Task 4).
- Produces: `internal sealed class DevinIntegrator(HomePaths home)` with `ProviderName == "devin"`;
  notes `DevinIntegrator.CascadeNote`, `DevinIntegrator.RestrictedModeNote`, `DevinIntegrator.LegacyRuleKeptNote(string path)`;
  `HomePaths.DevinConfigDir`, `HomePaths.WindsurfGlobalRulesPath`.

- [ ] **Step 1: Write the failing tests**

`HomePathsTests`:

```csharp
    [Fact]
    public void DevinConfigDir_UsesAppDataOnWindowsAndDotConfigElsewhere()
    {
        var appData = Path.Combine(Path.GetTempPath(), "appdata");
        var paths = new HomePaths("/home/u", name => name == "APPDATA" ? appData : null);

        paths.DevinConfigDir.Should().Be(OperatingSystem.IsWindows()
            ? Path.Combine(appData, "devin")
            : Path.Combine("/home/u", ".config", "devin"));
    }

    [Fact]
    public void WindsurfGlobalRulesPath_IsUnderCodeium()
    {
        new HomePaths("/home/u").WindsurfGlobalRulesPath
            .Should().Be(Path.Combine("/home/u", ".codeium", "windsurf", "memories", "global_rules.md"));
    }
```

`DevinIntegratorTests.cs`:

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class DevinIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-devin-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private HomePaths Home => new(HomeDir);
    private string RulePath => Path.Combine(ProjectDir, ".devin", "rules", "dtk.md");
    private string ProjectHooksPath => Path.Combine(ProjectDir, ".devin", "hooks.v1.json");
    private string LegacyRulePath => Path.Combine(ProjectDir, ".windsurf", "rules", "dtk.md");
    private string GlobalConfigPath => Path.Combine(Home.DevinConfigDir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private DevinIntegrator CreateSut() => new(Home);

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public void ProviderName_IsDevin() => CreateSut().ProviderName.Should().Be("devin");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesRuleAndRootLevelHook()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().Equal(DevinIntegrator.CascadeNote, DevinIntegrator.RestrictedModeNote);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksPath))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PreToolUse");
        var group = root["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("exec");
        group["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook devin");
        group["hooks"]![0]!["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesConfigHooksAndGlobalRulesSection()
    {
        await WriteAsync(GlobalConfigPath, """{"model":"swe-1"}""");

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(Home.WindsurfGlobalRulesPath);
        result.UpdatedFiles.Should().Equal(GlobalConfigPath);
        var config = JsonNode.Parse(await File.ReadAllTextAsync(GlobalConfigPath))!;
        config["model"]!.GetValue<string>().Should().Be("swe-1");
        config["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook devin");
        (await File.ReadAllTextAsync(Home.WindsurfGlobalRulesPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public async Task IntegrateAsync_ReleasedWindsurfRule_IsRemovedAndItsDirectoriesPruned()
    {
        await new DevinIntegrator(Home).IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, await File.ReadAllTextAsync(RulePath));
        File.Delete(RulePath);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.RemovedFiles.Should().Equal(LegacyRulePath);
        Directory.Exists(Path.Combine(ProjectDir, ".windsurf")).Should().BeFalse();
    }

    [Fact]
    public async Task IntegrateAsync_EditedWindsurfRule_IsKeptWithANoteNotSkipped()
    {
        await WriteAsync(LegacyRulePath, "# my own rules\n");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        File.Exists(LegacyRulePath).Should().BeTrue();
        result.SkippedFiles.Should().NotContain(LegacyRulePath);
        result.Notes.Should().Contain(DevinIntegrator.LegacyRuleKeptNote(LegacyRulePath));
    }

    [Fact]
    public async Task IntegrateAsync_DtkClaudeHookInProjectSettings_AddsTheImportNote()
    {
        await WriteAsync(Path.Combine(ProjectDir, ".claude", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook claude"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(ImportedClaudeHook.Note("Devin"));
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesRuleHookAndReleasedWindsurfRule()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, await File.ReadAllTextAsync(RulePath));

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().BeEquivalentTo([RulePath, ProjectHooksPath, LegacyRulePath]);
        Directory.Exists(Path.Combine(ProjectDir, ".devin")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_Global_RemovesSectionAndHookButKeepsOtherConfig()
    {
        await WriteAsync(GlobalConfigPath, """{"model":"swe-1"}""");
        await CreateSut().IntegrateGlobalAsync(false, default);

        await CreateSut().UninstallAsync(ProjectDir, HookScope.Global, new Dictionary<string, string>(), default);

        (await File.ReadAllTextAsync(GlobalConfigPath)).Should().NotContain("hooks").And.Contain("swe-1");
        File.Exists(Home.WindsurfGlobalRulesPath).Should().BeFalse();
    }
}
```

`ReleasedOwnedFileHashesTests`: rename `CurrentWindsurfRule…` → `CurrentDevinRule_MatchesThePinnedHash`, install with
`new DevinIntegrator(new HomePaths(Path.Combine(ProjectDir, "no-home")))`, and hash `.devin/rules/dtk.md` (the body is
unchanged, so the pinned hash and the `ReleasedMarkdownRuleHashes` assertion stay the same). Rename
`Uninstall_WindsurfRuleWrittenByV080_IsRemoved` → `Uninstall_WindsurfRuleWrittenByV080_IsRemovedByDevin`: seed
`.windsurf/rules/dtk.md` with `V080MarkdownRule`, uninstall with `DevinIntegrator`, assert the file is gone.

`UninstallIntegrationTests`: `new WindsurfIntegrator()` → `new DevinIntegrator(home)`; in `AllProviders` replace
`"windsurf"` with `"devin"`; add `"devin"` to `GlobalProviders` and to `hookProviders` in the doctor test.

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DevinIntegratorTests|FullyQualifiedName~HomePathsTests"`
Expected: build errors — no `DevinIntegrator`, `DevinConfigDir`, `WindsurfGlobalRulesPath`.

- [ ] **Step 3: Implement**

`HomePaths.cs`, after `CursorDir`:

```csharp
    /// <summary>
    /// Gets Devin CLI's and Devin Local's user config directory: <c>%APPDATA%\devin</c> on Windows (when
    /// <c>APPDATA</c> is an absolute path, else <c>~\AppData\Roaming\devin</c>), <c>~/.config/devin</c> elsewhere.
    /// </summary>
    internal string DevinConfigDir => OperatingSystem.IsWindows()
        ? Path.Combine(RootedOrDefault("APPDATA", Path.Combine(Home, "AppData", "Roaming")), "devin")
        : Path.Combine(Home, ".config", "devin");

    /// <summary>
    /// Gets the global rules file Devin Desktop (formerly Windsurf) always loads:
    /// <c>~/.codeium/windsurf/memories/global_rules.md</c>.
    /// </summary>
    internal string WindsurfGlobalRulesPath => Path.Combine(Home, ".codeium", "windsurf", "memories", "global_rules.md");
```

`DevinIntegrator.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Devin Desktop (formerly Windsurf) and Devin CLI.</summary>
/// <param name="home">Resolves the user's home and Devin config directories for global integration.</param>
/// <remarks>
/// Creates, in a project: <c>.devin/rules/dtk.md</c> and <c>.devin/hooks.v1.json</c>, whose root is the hooks object
/// registering <c>dtk hook devin</c> under <c>PreToolUse</c> for the <c>exec</c> tool. Globally: that registration
/// under <c>hooks</c> in Devin's <c>config.json</c>, and a dtk section in Devin Desktop's always-on
/// <c>global_rules.md</c>. Devin Local and Devin CLI run the hook; the legacy Cascade agent reads only the rule.
/// A project install and uninstall also retire the <c>.windsurf/rules/dtk.md</c> an older dtk wrote, which Devin still
/// reads as a fallback, when its content proves dtk wrote it.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class DevinIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook.</summary>
    internal const string CascadeNote =
        "Devin Local and Devin CLI run the rewrite hook; the legacy Cascade agent cannot rewrite commands and follows "
        + "only the rule.";

    /// <summary>Printed when this run wrote a project hook.</summary>
    internal const string RestrictedModeNote =
        "Devin Desktop runs no hooks while a workspace is in Restricted Mode: trust this workspace for the hook to run.";

    /// <summary>Seconds Devin waits for the hook.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <summary>Devin's shell tool, which the hook's matcher selects.</summary>
    private const string ShellTool = "exec";

    /// <summary>The rule, byte for byte the one <c>dtk init windsurf</c> wrote, so released hashes still recognize it.</summary>
    private static readonly string DevinRule =
        $"""
        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <summary>Printed when an edited <c>.windsurf/rules/dtk.md</c> is left in place.</summary>
    /// <param name="path">The legacy rule's path.</param>
    internal static string LegacyRuleKeptNote(string path) =>
        $"{path} was left in place: it differs from every rule dtk wrote, so it was edited. Devin still reads "
        + ".windsurf/rules, so delete it once .devin/rules/dtk.md covers it.";

    /// <inheritdoc/>
    public string ProviderName => "devin";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            scope == HookScope.Global
                ? Path.Combine(home.DevinConfigDir, "config.json")
                : Path.Combine(directory, ".devin", "hooks.v1.json"),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Devin)
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), DevinRule, context, cancellationToken).ConfigureAwait(false);
        await RetireWindsurfRuleAsync(directory, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(directory, HookScope.Project, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            home.WindsurfGlobalRulesPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
            SharedInstructionArtifacts.Section, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(home.Home, HookScope.Global, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        if (scope == HookScope.Project)
        {
            await UninstallHelpers.RemoveOwnedFileAsync(
                RulePath(directory), DevinRule, IntegrationInstructions.ReleasedMarkdownRuleHashes, "dtk init devin", context,
                cancellationToken).ConfigureAwait(false);
            await RetireWindsurfRuleAsync(directory, context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UninstallHelpers.RemoveSectionAsync(
                home.WindsurfGlobalRulesPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
                context, cancellationToken).ConfigureAwait(false);
        }

        await UninstallHelpers.RemoveHookRegistrationAsync(Registration(DescribeHooks(root, scope)[0]), context, cancellationToken)
            .ConfigureAwait(false);

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".devin", "rules", "dtk.md");

    private static string LegacyRulePath(string directory) => Path.Combine(directory, ".windsurf", "rules", "dtk.md");

    /// <summary>The project file's root is the hooks object; the global <c>config.json</c> holds it under <c>hooks</c>.</summary>
    private static HookRegistrationSpec Registration(HookInstallation hook) =>
        new(hook.RegistrationPath, "PreToolUse", ShellTool, hook.Command, HookTimeoutSeconds,
            ContainerKey: hook.Scope == HookScope.Global ? "hooks" : null);

    /// <summary>
    /// Deletes the <c>.windsurf/rules/dtk.md</c> an older dtk wrote when its content proves it (the current body or a
    /// released hash), pruning the directories that leaves empty; an edited copy is kept with a note. An install has no
    /// prune boundary of its own, so it deletes through a throwaway uninstall context rooted at the project.
    /// </summary>
    private static async Task RetireWindsurfRuleAsync(string directory, IntegrationContext context, CancellationToken cancellationToken)
    {
        var path = LegacyRulePath(directory);
        if (!File.Exists(path))
        {
            return;
        }

        var existing = await IntegratorHelpers.TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
        var ownsIt = existing is not null
            && (string.Equals(existing, DevinRule.ReplaceLineEndings("\n"), StringComparison.Ordinal)
                || IntegrationInstructions.ReleasedMarkdownRuleHashes.Contains(UninstallHelpers.HashOwnedFile(existing), StringComparer.Ordinal));

        if (!ownsIt)
        {
            context.Notes.Add(LegacyRuleKeptNote(path));
            return;
        }

        var deleter = context.PruneBoundary is null
            ? IntegrationContext.ForUninstall(directory, new Dictionary<string, string>())
            : context;
        UninstallHelpers.DeleteFile(path, deleter);

        if (!ReferenceEquals(deleter, context))
        {
            context.Removed.AddRange(deleter.Removed);
            context.Notes.AddRange(deleter.Notes);
        }
    }

    private async Task WriteHookAsync(string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        await IntegratorHelpers.WriteHookRegistrationAsync(Registration(hook), context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(CascadeNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(RestrictedModeNote);
            }
        }

        if (ImportedClaudeHook.IsRegisteredIn(ImportedClaudeHook.SettingsFiles(home, scope == HookScope.Project ? directory : null)))
        {
            context.Notes.Add(ImportedClaudeHook.Note("Devin"));
        }
    }
}
```

`TryReadExistingAsync` returns the content with `\n` endings (it is what `RemoveOwnedFileAsync` compares); if it
does not normalize, call `.ReplaceLineEndings("\n")` on `existing` before comparing and hashing.

`DependencyInjection.cs`: replace `services.AddTransient<IProviderIntegrator, WindsurfIntegrator>();` with
`services.AddTransient<IProviderIntegrator, DevinIntegrator>();`. Delete `WindsurfIntegrator.cs` and
`WindsurfIntegratorTests.cs`. In `IntegrationInstructions.cs`, update any doc comment naming `WindsurfIntegrator`
to `DevinIntegrator`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DevinIntegratorTests|FullyQualifiedName~HomePathsTests|FullyQualifiedName~ReleasedOwnedFileHashesTests|FullyQualifiedName~UninstallIntegrationTests"`
Expected: PASS. (`UninstallIntegrationTests` still fails for `windsurf` in any CLI-level list until Task 7 — none of
its theories use `windsurf` after this task's edits.)

- [ ] **Step 5: Commit**

```bash
git add -A src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: dtk init devin replaces the Windsurf rule with a Devin rewrite hook

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: `windsurf` as an alias of `devin`, and the CLI surface

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrateUseCase.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/InitCommand.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/InitCommandSettings.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`

**Interfaces:**
- Produces: `IntegrateUseCase.TryResolveProvider(string name, [NotNullWhen(true)] out string? canonical, out string? aliasNote) : bool`;
  `IntegrateUseCase.Aliases : IReadOnlyDictionary<string, string>` (alias → canonical).

- [ ] **Step 1: Write the failing tests**

`IntegrateUseCaseTests`:

```csharp
    [Theory]
    [InlineData("windsurf")]
    [InlineData("Windsurf")]
    public void TryResolveProvider_Alias_ResolvesToDevinWithANote(string name)
    {
        var useCase = new IntegrateUseCase([new StubIntegrator("devin")]);

        useCase.TryResolveProvider(name, out var canonical, out var note).Should().BeTrue();

        canonical.Should().Be("devin");
        note.Should().Contain("Devin");
    }

    [Fact]
    public void TryResolveProvider_RegisteredName_WinsOverAnAlias()
    {
        var useCase = new IntegrateUseCase([new StubIntegrator("windsurf"), new StubIntegrator("devin")]);

        useCase.TryResolveProvider("windsurf", out var canonical, out var note).Should().BeTrue();

        canonical.Should().Be("windsurf");
        note.Should().BeNull();
    }

    [Fact]
    public void TryResolveProvider_Unknown_Fails()
    {
        new IntegrateUseCase([new StubIntegrator("devin")]).TryResolveProvider("zed", out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_Alias_RunsTheCanonicalIntegrator()
    {
        var devin = new StubIntegrator("devin");

        await new IntegrateUseCase([devin]).RunAsync("windsurf", "/tmp/x", false, default);

        devin.LastDirectory.Should().Be("/tmp/x");
    }
```

(`StubIntegrator` already exists at the bottom of both `IntegrateUseCaseTests` and `InitCommandTests`; it records
`LastDirectory` and `LastForce`.)

`InitCommandTests`: add `[InlineData("devin")]` to `ExecuteAsync_EveryProviderName_RoutesToMatchingIntegrator`, and:

```csharp
    [Fact]
    public async Task RunAsync_WindsurfAlias_RunsDevinAndPrintsTheAliasNote()
    {
        var console = new TestConsole();
        var devin = new StubIntegrator("devin");
        var command = new InitCommand(new IntegrateUseCase([devin]), console);

        var exitCode = await command.RunAsync(new InitCommandSettings { Provider = "WINDSURF" }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("Windsurf is now Devin Desktop");
        console.Output.Should().Contain("devin");
    }

    [Fact]
    public async Task RunAsync_UnknownProvider_ListsTheAlias()
    {
        var console = new TestConsole();
        var command = new InitCommand(new IntegrateUseCase([new StubIntegrator("devin")]), console);

        var exitCode = await command.RunAsync(new InitCommandSettings { Provider = "zed" }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("windsurf → devin");
    }
```

`ParityCases.InitProviders`: add `"devin"` after `"cursor"` (keep `"windsurf"`: the alias must behave the same in the
AOT binary).

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegrateUseCaseTests"`
Expected: build error — no `TryResolveProvider`.

- [ ] **Step 3: Implement**

`IntegrateUseCase.cs`:

```csharp
    /// <summary>Provider names kept for compatibility, mapped to the provider that replaced them.</summary>
    private static readonly Dictionary<string, (string Canonical, string Note)> AliasTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["windsurf"] = ("devin", "Windsurf is now Devin Desktop: 'dtk init windsurf' runs 'dtk init devin'.")
        };

    /// <summary>Gets the provider aliases, each mapped to its canonical provider name.</summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        AliasTable.ToDictionary(pair => pair.Key, pair => pair.Value.Canonical, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a provider name as typed — any casing, or an alias — to a registered provider's name. A registered
    /// name wins over an alias spelled the same.
    /// </summary>
    /// <param name="name">The name the user typed.</param>
    /// <param name="canonical">The registered provider's name, when resolved.</param>
    /// <param name="aliasNote">The note to print when <paramref name="name"/> was an alias; otherwise <see langword="null"/>.</param>
    /// <returns>Whether <paramref name="name"/> names a registered provider.</returns>
    public bool TryResolveProvider(string name, [NotNullWhen(true)] out string? canonical, out string? aliasNote)
    {
        aliasNote = null;
        if (_integrators.TryGetValue(name, out var integrator))
        {
            canonical = integrator.ProviderName;
            return true;
        }

        if (AliasTable.TryGetValue(name, out var alias) && _integrators.TryGetValue(alias.Canonical, out integrator))
        {
            canonical = integrator.ProviderName;
            aliasNote = alias.Note;
            return true;
        }

        canonical = null;
        return false;
    }
```

`ResolveOrThrow` resolves aliases too:

```csharp
        if (!TryResolveProvider(providerName, out var canonical, out _))
        {
            throw new InvalidOperationException(
                $"Unknown provider '{providerName}'. Available: {string.Join(", ", _integrators.Keys)}");
        }

        return _integrators[canonical];
```

Add `using System.Diagnostics.CodeAnalysis;`.

`InitCommand.RunAsync` — replace the `availableProviders`/`canonicalProvider` block:

```csharp
        if (!integrateUseCase.TryResolveProvider(settings.Provider, out var canonicalProvider, out var aliasNote))
        {
            var aliases = string.Join(", ", IntegrateUseCase.Aliases.Select(pair => $"{pair.Key} → {pair.Value}"));
            console.MarkupLine(
                $"[red]Error:[/] Unknown provider '{Markup.Escape(settings.Provider)}'. " +
                $"Available: {Markup.Escape(string.Join(", ", integrateUseCase.AvailableProviders))} " +
                $"(alias: {Markup.Escape(aliases)})");
            return 1;
        }

        if (aliasNote is not null)
        {
            console.MarkupLine($"[cyan]note[/]     {Markup.Escape(aliasNote)}");
        }
```

`InitCommandSettings` description list: `…, cursor, devin (alias: windsurf), aider, jetbrains`.

`CompletionCommand`:
- bash `init_providers`: `… cursor devin windsurf aider jetbrains`.
- zsh: `'cursor:Install dtk rules and rewrite hook for Cursor'`, `'devin:Install dtk rules and rewrite hook for Devin (Windsurf)'`, `'windsurf:Alias of devin'`.
- fish: the same three descriptions (`-a cursor`, `-a devin`, `-a windsurf`).
- PowerShell `$providers`: `… 'cursor', 'devin', 'windsurf', 'aider', 'jetbrains'`.

`CliConfigurator`: after `.WithExample(InitCommand, "cursor")` add `.WithExample(InitCommand, "cursor", GlobalOption)`;
replace `.WithExample(InitCommand, "windsurf")` with `.WithExample(InitCommand, "devin")` and
`.WithExample(InitCommand, "devin", GlobalOption)`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegrateUseCaseTests"`
and `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests|FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~DocsBindingTests"`
Expected: PASS. (The CLI integration suite is slow; run only these classes.)

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: windsurf is an alias of devin

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Docs, manual checklist, and spec amendment

**Files:**
- Modify: `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docs/articles/ai-agent-setup.md`, `docs/articles/usage.md`, `CLAUDE.md`
- Create: `docs/articles/harness-verification.md`; Modify: `docs/articles/toc.yml`
- Modify: `docs/superpowers/specs/2026-09-26-rewrite-harnesses-design.md`

- [ ] **Step 1: README.md**
  - Feature line: `… oh-my-pi, Cursor, Devin (formerly Windsurf), Aider, JetBrains AI` (still 13 integrations).
  - Provider table rows:

```markdown
| **Cursor**             | `dtk init cursor`      | `.cursor/rules/dtk.mdc`, `.cursor/hooks.json` (`--global`: `~/.cursor/hooks.json`) |
| **Devin** (Windsurf)   | `dtk init devin`       | `.devin/rules/dtk.md`, `.devin/hooks.v1.json` (`--global`: `~/.config/devin/config.json`, `global_rules.md`) |
```

  - The repository-scoped sentence becomes: "`copilot` and `jetbrains` are repository-scoped and have no global mode.
    `windsurf` is an alias of `devin`."

- [ ] **Step 2: `src/DotnetTokenKiller.Cli/README.md`** — the same feature-line and repository-scoped edits.

- [ ] **Step 3: `docs/articles/ai-agent-setup.md`** — rewrite the `## Cursor` section: what the hook does (rewrite of
  simple `build/test/restore/clean/format/list package` commands, `permission: "allow"`, why chained/publish/pack are
  left to the rule), both scopes, the trust / `--trust` note, the known gaps (remote Linux workspaces, subagents),
  rtk coexistence, the imported-Claude-hook note, and "Not verified against a live run — see
  [Harness verification](harness-verification.md)". Replace `## Windsurf` with `## Devin (formerly Windsurf)`:
  Devin Local / Devin CLI hook, both scopes and files, the `windsurf` alias and the `.windsurf/rules` migration,
  Cascade gets the rule only, Restricted Mode, the same "not verified" line. Update the uninstall paragraph near
  line 48 ("the Cursor and Devin rules").

- [ ] **Step 4: `docs/articles/usage.md`** — replace the two lines with:

```text
dtk init cursor      # Cursor rule + rewrite hook (--global: hook only)
dtk init devin       # Devin (formerly Windsurf) rule + rewrite hook; 'windsurf' is an alias
```

- [ ] **Step 5: Create `docs/articles/harness-verification.md`** and add it to `toc.yml` after "AI Agent Setup":

```markdown
# Harness verification

Some integrations could not be run against the real harness when they were written, because the harness needs an
account. Their hooks follow the harness's documentation and are covered by payload tests, but the points below are
unconfirmed. If you can run one, please report the result in an issue.

## Cursor

1. `{}` is accepted as a neutral reply: with the hook installed, ask the agent to run `ls`; it must run normally.
2. A rewrite lands: ask the agent to run `dotnet build`; the command shown must be `dtk dotnet build`, in the IDE and in `cursor-agent -p`.
3. Whether `permission: "allow"` skips Cursor's approval prompt for the rewritten command (if it does not, dtk could
   also rewrite chained commands and `publish`/`pack`).
4. With dtk's Claude Code hook also in `~/.claude/settings.json`: the command is rewritten once, not `dtk dtk`.

## Devin (Devin Local and Devin CLI)

1. The matcher `exec` selects the shell tool: `dotnet build` becomes `dtk dotnet build` in `devin` and in Devin Desktop.
2. Which shell runs the hook command on Windows.
3. Whether a workspace in Restricted Mode runs the project hook.
```

- [ ] **Step 6: CLAUDE.md** — after the `dtk init antigravity` paragraph add:

```markdown
`dtk init cursor` writes `.cursor/rules/dtk.mdc` and a `preToolUse` entry in `.cursor/hooks.json` (`--global`:
`~/.cursor/hooks.json`, hook only). `dtk hook cursor` must always print JSON — Cursor blocks the tool on any output
that does not match its schema — so every non-rewrite is `{}`; a rewrite carries `permission: "allow"`, so it is
gated by `IsAutoApprovable` like Copilot CLI's. doctor probes it one simple command per subcommand. `dtk init devin`
(alias `windsurf`) writes `.devin/rules/dtk.md` and `.devin/hooks.v1.json`, whose root is the hooks object
(`HookRegistrationSpec.ContainerKey: null`); globally `~/.config/devin/config.json` and a section in
`~/.codeium/windsurf/memories/global_rules.md`. Both harnesses also import Claude Code's hooks; a double rewrite is
harmless. Unverified points are listed in docs/articles/harness-verification.md.
```

- [ ] **Step 7: Spec amendment** — in `2026-09-26-rewrite-harnesses-design.md`, *PR 1a*: replace "`doctor` reports, as
  information, when dtk's Claude hook is also registered …" with "The install prints a note when dtk's Claude hook is
  also registered in a Claude settings file Cursor imports (`ImportedClaudeHook`)", and add "doctor probes Cursor with
  one simple command per auto-approvable subcommand". Set `Status:` to `approved; PR 1 implemented`.

- [ ] **Step 8: Verify everything**

Run: `dtk dotnet build DotnetTokenKiller.slnx`, then `dtk dotnet test DotnetTokenKiller.slnx`, then
`dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`.
Expected: build clean; all tests pass (the savings baseline is untouched — no filter changed); no format changes.

- [ ] **Step 9: Commit**

```bash
git add README.md src/DotnetTokenKiller.Cli/README.md docs CLAUDE.md
git commit -m "docs: Cursor and Devin rewrite hooks, harness verification checklist

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

# pi and oh-my-pi Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `dtk init pi` / `dtk init oh-my-pi` install a generated `dtk.js` extension that rewrites the agent's
`dotnet …` bash tool calls to `dtk dotnet …`, plus the shared `AGENTS.md` section and skill; `dtk hook pi|oh-my-pi`
answer it.

**Architecture:** A shared JS runtime snippet (`PluginRuntime`: PATH lookup + spawn of `dtk hook <provider>`) is
extracted from `OpenCodePlugin` without changing OpenCode's bytes. `PiExtension` wraps it in a `tool_call` handler
that both mutates `event.input.command` (pi) and returns `{ input }` (oh-my-pi). An abstract `PiFamilyIntegrator`
holds the install/uninstall logic; `PiIntegrator` and `OhMyPiIntegrator` supply paths. Both hook verbs reuse the
OpenCode payload (`{"command":…}`).

**Tech Stack:** .NET 10, C# raw interpolated strings, xunit + FluentAssertions + NSubstitute, Verify snapshots, Node
(for the generated-extension tests).

**Spec:** `docs/superpowers/specs/2026-09-26-pi-oh-my-pi-integration-design.md`

## Global Constraints

- Provider names are exactly `pi` and `oh-my-pi`; hook verbs `dtk hook pi` and `dtk hook oh-my-pi`.
- Extension file name is `dtk.js` in `.pi/extensions/`, `<pi agent dir>/extensions/`, `.omp/extensions/`, `~/.omp/agent/extensions/`.
- `<pi agent dir>` = `$PI_CODING_AGENT_DIR` when set and absolute, else `~/.pi/agent`. oh-my-pi global = `~/.omp/agent`.
- The generated extension must never throw and never spawn a bare `dtk` or use a shell; spawn timeout stays 5000 ms.
- `OpenCodePlugin.Body` must stay byte-for-byte identical (pinned in Task 1).
- `DotnetCommandRewriter` is not modified.
- dtk never reads or writes pi's `trust.json` or oh-my-pi's `config.yml`.
- `TreatWarningsAsErrors` is on: every analyzer warning must be fixed. File-scoped namespaces, `var`, LF endings, `_camelCase` fields, async methods end in `Async`.
- Use `dtk dotnet build|test|format` instead of raw `dotnet` for those verbs.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- A `tool_call` event whose `input` is missing or whose `command` is not a string must pass through without throwing — Task 7 test `MissingOrNonStringCommand_PassesThroughAsync`.
- The returned `{ input }` must keep oh-my-pi's other bash fields (`cwd`, `env`, `timeout`) — Task 7 test `Bash_DotnetCommand_IsRewrittenBothWaysByTheRealDtkAsync` asserts `timeout` and `cwd` survive.
- `PI_CODING_AGENT_DIR` set to an empty or relative value must fall back to `~/.pi/agent` — Task 3 test.
- An rtk extension copied into `.omp/extensions` must still trigger the rtk `dotnet` exclusion — Task 5 test `IntegrateAsync_RtkExtensionInEitherHarness_ExcludesDotnetInRtkConfig`.
- Uninstalling pi while opencode (sharing `.agents/skills`) is still installed must keep the skill — Task 5 test `UninstallAsync_SkillStillUsedByOpenCode_IsKept`.

---

### Task 1: Extract the shared plugin runtime from the OpenCode plugin

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/PluginRuntime.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/OpenCodePlugin.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/OpenCodeIntegratorTests.cs`

**Interfaces:**
- Produces: `internal static class PluginRuntime` with `internal static string InvocationSignature(string provider)` (returns `["hook", "<provider>"]`) and `internal static string Source(string provider, string harness)` (the JS from the first `import` through the end of `rewrite()` plus one trailing `\n`). `OpenCodePlugin.Body` and `OpenCodePlugin.InvocationSignature` become `internal static readonly string` (no longer `const`).

- [ ] **Step 1: Pin the current plugin bytes**

Add to `OpenCodeIntegratorTests`:

```csharp
    [Fact]
    public void PluginBody_IsByteForByteTheReleasedPlugin()
    {
        // Pins the plugin every OpenCode user has installed: a byte change would mark every install stale.
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(OpenCodePlugin.Body)));

        hash.Should().Be("0000000000000000000000000000000000000000000000000000000000000000");
    }
```

- [ ] **Step 2: Capture the real hash**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~OpenCodeIntegratorTests.PluginBody_IsByteForByteTheReleasedPlugin"`
Expected: FAIL, message `Expected hash to be "000…" … but "<64 hex chars>"`. Replace the zeros with the actual hash
from the message, re-run, expect PASS. Commit:

```bash
git add tests/DotnetTokenKiller.Application.Tests/Integration/OpenCodeIntegratorTests.cs
git commit -m "test: pin the OpenCode plugin body before extracting its runtime"
```

- [ ] **Step 3: Create `PluginRuntime`**

`src/DotnetTokenKiller.Application/Integration/PluginRuntime.cs` — move the JS from `OpenCodePlugin.Body` verbatim
(the three `import` lines through the closing `}` of `rewrite`), replacing only `OpenCode` in the `findDtk` comment
with `{{harness}}` and `["hook", "opencode"]` with `{{InvocationSignature(provider)}}`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>The JavaScript every generated harness plugin shares: find <c>dtk</c> on <c>PATH</c> and ask it to rewrite.</summary>
/// <remarks>See <see cref="OpenCodePlugin"/> for why each line is there; the pi-family extension reuses it unchanged.</remarks>
internal static class PluginRuntime
{
    /// <summary>The call a generated plugin still runs dtk through, even after local edits.</summary>
    /// <param name="provider">The <c>dtk hook</c> argument.</param>
    internal static string InvocationSignature(string provider) => $"[\"hook\", \"{provider}\"]";

    /// <summary>The imports, <c>findDtk()</c> and <c>rewrite(command)</c>, ending with a blank line.</summary>
    /// <param name="provider">The <c>dtk hook</c> argument the plugin spawns.</param>
    /// <param name="harness">The harness's display name, for comments.</param>
    internal static string Source(string provider, string harness) =>
        $$"""
        import { spawn } from "node:child_process";
        import { accessSync, constants, statSync } from "node:fs";
        import { delimiter, isAbsolute, join } from "node:path";

        const TIMEOUT_MS = 5000;

        // Resolves dtk from PATH alone. Given a bare name, spawn on Windows tries the current directory (the project)
        // first, so a dtk.exe planted in a repository would run before {{harness}} asks for permission.
        function findDtk() {
          const isWindows = process.platform === "win32";
          const name = isWindows ? "dtk.exe" : "dtk";
          for (const entry of (process.env.PATH ?? "").split(delimiter)) {
            const directory = entry.length >= 2 && entry.startsWith('"') && entry.endsWith('"') ? entry.slice(1, -1) : entry;
            if (directory === "" || !isAbsolute(directory)) continue;
            const candidate = join(directory, name);
            try {
              if (!statSync(candidate).isFile()) continue;
              if (!isWindows) accessSync(candidate, constants.X_OK);
              return candidate;
            } catch {
              // Missing or not executable: try the next entry.
            }
          }
          return null;
        }

        function rewrite(command) {
          return new Promise((resolve) => {
            let child;
            try {
              const dtk = findDtk();
              if (dtk === null) {
                resolve(null);
                return;
              }
              child = spawn(dtk, {{InvocationSignature(provider)}}, { stdio: ["pipe", "pipe", "ignore"], windowsHide: true });
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

        """;
}
```

Copy the JS lines from the current `OpenCodePlugin.cs`, not from this plan, if they differ in any character.

- [ ] **Step 4: Rebuild `OpenCodePlugin.Body` from it**

In `OpenCodePlugin.cs`, keep the XML docs and `Artifact`, and replace the two constants:

```csharp
    /// <summary>The call a plugin still runs dtk through, even after local edits.</summary>
    internal static readonly string InvocationSignature = PluginRuntime.InvocationSignature("opencode");

    /// <summary>The plugin source, before stamping.</summary>
    internal static readonly string Body =
        $$"""
        // dtk (DotnetTokenKiller) rewrites the dotnet commands dtk supports to `dtk dotnet ...` before OpenCode runs them.
        // Generated by `dtk init opencode`; run it again to refresh this file.
        {{PluginRuntime.Source("opencode", "OpenCode")}}
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

        """;
```

In `OpenCodeIntegratorTests.PluginBody_KeepsTheContractOpenCodeAndWindowsNeed`, change `const string body = OpenCodePlugin.Body;`
to `var body = OpenCodePlugin.Body;`. Fix any other `const` use the build reports.

- [ ] **Step 5: Verify the bytes did not move**

Run: `dtk dotnet build DotnetTokenKiller.slnx` then `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~OpenCode"`
Expected: build clean; all pass, including `PluginBody_IsByteForByteTheReleasedPlugin`. If the pin fails, diff
whitespace between `Source` and the old body (usually the blank line before `export`) until it passes — never
update the pinned hash.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/PluginRuntime.cs src/DotnetTokenKiller.Application/Integration/OpenCodePlugin.cs tests/DotnetTokenKiller.Application.Tests/Integration/OpenCodeIntegratorTests.cs
git commit -m "refactor: extract the generated plugins' shared JavaScript runtime"
```

### Task 2: Derive the plugin invocation signature from the provider

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/UninstallHelpers.cs:313`
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs:257`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs:44`
- Modify: `docs/superpowers/specs/2026-09-26-pi-oh-my-pi-integration-design.md` (*Generalizing the OpenCode-only checks*)

**Interfaces:**
- Consumes: `PluginRuntime.InvocationSignature(string provider)` (Task 1).
- Produces: plugin registration checks keyed by `installation.ProviderName`; no new `HookInstallation` field.

- [ ] **Step 1: Replace the three OpenCode-specific references**

`UninstallHelpers.IsRegistered`:
```csharp
                : content.Contains(PluginRuntime.InvocationSignature(installation.ProviderName), StringComparison.Ordinal);
```
`HookHealthChecker.ClassifyPlugin`:
```csharp
        return normalized.Contains(PluginRuntime.InvocationSignature(installation.ProviderName), StringComparison.Ordinal)
```
`HookDescriptionTests`:
```csharp
                registration.Should().Contain(installation.PluginArtifact is null
                    ? installation.Command
                    : PluginRuntime.InvocationSignature(installation.ProviderName));
```

- [ ] **Step 2: Update the spec to match**

Replace the paragraph under *Generalizing the OpenCode-only checks* with:

```markdown
`UninstallHelpers.IsRegistered` and `HookHealthChecker.ClassifyPlugin` compare a plugin file against
`OpenCodePlugin.InvocationSignature`. Every generated plugin spawns `["hook", "<provider>"]`, so both checks derive
the signature from the installation's provider name through `PluginRuntime.InvocationSignature`; `HookInstallation`
gains no field. No behaviour changes for OpenCode.
```

- [ ] **Step 3: Run the affected tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthChecker|FullyQualifiedName~Uninstall|FullyQualifiedName~HookDescription"`
Expected: PASS (pure refactor).

- [ ] **Step 4: Commit**

```bash
git add -A src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests docs/superpowers/specs
git commit -m "refactor: derive a generated plugin's invocation signature from its provider"
```

### Task 3: Home paths and hook payload kinds

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs` (`TryGetKind`, `Reply`, doc comment)
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`BuildPayload`)
- Modify: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs:20` (usage)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs`

**Interfaces:**
- Produces: `HomePaths.PiAgentDir` (string), `HomePaths.OhMyPiAgentDir` (string); `HookPayloadKind.Pi = 6`, `HookPayloadKind.OhMyPi = 7`; `HookPayloads.TryGetKind("pi"|"oh-my-pi")`.

- [ ] **Step 1: Write failing tests**

`HomePathsTests`:
```csharp
    [Fact]
    public void PiAgentDir_DefaultsToDotPiAgent()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        new HomePaths(home).PiAgentDir.Should().Be(Path.Combine(home, ".pi", "agent"));
    }

    [Theory]
    [InlineData("rooted")]
    [InlineData("relative")]
    [InlineData("empty")]
    public void PiAgentDir_HonorsOnlyAnAbsolutePiCodingAgentDir(string kind)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        var custom = Path.Combine(Path.GetTempPath(), "pi-agent");
        var value = kind switch { "rooted" => custom, "relative" => "pi-agent", _ => string.Empty };

        var expected = kind == "rooted" ? custom : Path.Combine(home, ".pi", "agent");
        new HomePaths(home, name => name == "PI_CODING_AGENT_DIR" ? value : null).PiAgentDir.Should().Be(expected);
    }

    [Fact]
    public void OhMyPiAgentDir_IsDotOmpAgent()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        new HomePaths(home).OhMyPiAgentDir.Should().Be(Path.Combine(home, ".omp", "agent"));
    }
```

`HookPayloadsTests`: add `[InlineData("pi", HookPayloadKind.Pi)]` and `[InlineData("oh-my-pi", HookPayloadKind.OhMyPi)]`
to the `TryGetKind` theory (next to line 16's `opencode` row), and:
```csharp
    [Theory]
    [InlineData(HookPayloadKind.Pi)]
    [InlineData(HookPayloadKind.OhMyPi)]
    public void PiFamily_UsesTheOpenCodeContract(HookPayloadKind kind)
    {
        var reply = JsonNode.Parse(Reply(kind, """{"command":"dotnet build"}""")!)!;

        reply.ToJsonString().Should().Be("""{"command":"dtk dotnet build"}""");
        Reply(kind, """{"command":"ls -la"}""").Should().BeNull();
    }
```
(Use the same `Reply` helper the existing `OpenCode_*` tests use.)

`HookEntryPointTests` (lines 39, 54) and `HookIntegrationTests` (line 56): change the expected usage to
`dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity|pi|oh-my-pi>`. Add to `HookIntegrationTests`, next to the
OpenCode test at line 88, the same test for `"hook", "pi"` and `"hook", "oh-my-pi"` (copy that test's body, changing
only the provider argument and name).

- [ ] **Step 2: Run to see them fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: compile errors — `PiAgentDir`, `HookPayloadKind.Pi` do not exist.

- [ ] **Step 3: Implement**

`HomePaths.cs`, after `OpenCodeConfigDir`:
```csharp
    /// <summary>Gets pi's agent directory: <c>$PI_CODING_AGENT_DIR</c> when it is an absolute path, else <c>~/.pi/agent</c>.</summary>
    internal string PiAgentDir => RootedOrDefault("PI_CODING_AGENT_DIR", Path.Combine(Home, ".pi", "agent"));

    /// <summary>Gets oh-my-pi's default-profile agent directory (<c>~/.omp/agent</c>).</summary>
    internal string OhMyPiAgentDir => Path.Combine(Home, ".omp", "agent");
```
Also update the `AgentsSkillsDir` summary to "Codex CLI, OpenCode, pi and oh-my-pi all read".

`IHookIntegrator.cs` enum:
```csharp
    /// <summary>Google Antigravity CLI's <c>PreToolUse</c> payload.</summary>
    AntigravityCli = 5,

    /// <summary>dtk's own pi extension payload, the same as <see cref="OpenCode"/>'s.</summary>
    Pi = 6,

    /// <summary>dtk's own oh-my-pi extension payload, the same as <see cref="OpenCode"/>'s.</summary>
    OhMyPi = 7
```

`HookPayloads.TryGetKind`: add `"pi" => (true, HookPayloadKind.Pi),` and `"oh-my-pi" => (true, HookPayloadKind.OhMyPi),`
after `antigravity`, and add `<c>pi</c>`, `<c>oh-my-pi</c>` to its summary. `Reply`'s switch:
```csharp
                HookPayloadKind.OpenCode or HookPayloadKind.Pi or HookPayloadKind.OhMyPi => ReplyToOpenCode(root),
```
and extend `ReplyToOpenCode`'s summary: "…generated OpenCode plugin and pi-family extension…".

`HookHealthChecker.BuildPayload`:
```csharp
            HookPayloadKind.OpenCode or HookPayloadKind.Pi or HookPayloadKind.OhMyPi => new JsonObject { ["command"] = command },
```

`HookEntryPoint.cs` usage: `"usage: dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity|pi|oh-my-pi> "`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HomePaths|FullyQualifiedName~HookPayloads"` and
`dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPoint|FullyQualifiedName~HookIntegrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat: dtk hook pi and dtk hook oh-my-pi"
```

### Task 4: The pi-family extension generator

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/PiExtension.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/PiExtensionTests.cs`

**Interfaces:**
- Consumes: `PluginRuntime.Source`, `PluginRuntime.InvocationSignature` (Task 1).
- Produces: `internal static class PiExtension` with `internal static string Body(string provider, string harness)` and `internal static GeneratedArtifact Artifact(string path, string provider, string harness)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class PiExtensionTests
{
    [Theory]
    [InlineData("pi", "pi")]
    [InlineData("oh-my-pi", "oh-my-pi")]
    public void Body_KeepsTheContractBothHarnessesNeed(string provider, string harness)
    {
        var body = PiExtension.Body(provider, harness);

        body.Should().StartWith($"// dtk (DotnetTokenKiller) rewrites the dotnet commands dtk supports to `dtk dotnet ...` before {harness} runs them.\n");
        body.Should().Contain($"Generated by `dtk init {provider}`");
        body.Should().Contain("spawn(dtk, " + PluginRuntime.InvocationSignature(provider))
            .And.NotContain("spawn(\"dtk\"").And.NotContain("shell: true");
        body.Should().Contain("pi.on(\"tool_call\"").And.Contain("event?.toolName !== \"bash\"").And.Contain("command.includes(\"dotnet\")");
        body.Should().Contain("input.command = rewritten;", "pi executes the args object it passed in");
        body.Should().Contain("return { input: { ...input, command: rewritten } };", "oh-my-pi executes the returned input");
        body.Split("export ").Should().HaveCount(2).And.Subject.Last().Should().StartWith("default function (pi)");
        body.Should().NotContain("@earendil-works").And.NotContain("@mariozechner").And.NotContain("@oh-my-pi");
    }

    [Fact]
    public void Artifact_IsStampedWithSlashComments()
    {
        var artifact = PiExtension.Artifact("/x/dtk.js", "pi", "pi");

        artifact.Style.Should().Be(StampStyle.SlashComment);
        artifact.Body.Should().Be(PiExtension.Body("pi", "pi"));
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: `PiExtension` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>The extension <c>dtk init pi</c> and <c>dtk init oh-my-pi</c> generate.</summary>
/// <remarks>
/// <para>
/// Both harnesses load <c>export default function (pi)</c> modules from their extensions directories and fire
/// <c>tool_call</c> before a tool runs. For a <c>bash</c> call whose command contains <c>dotnet</c>, the extension runs
/// <c>dtk hook &lt;provider&gt;</c> through <see cref="PluginRuntime"/> and applies the reply both ways, because the
/// two harnesses disagree on how a handler changes the input: pi passes the tool's own args object and ignores every
/// returned field but <c>block</c>, so the command is mutated in place; oh-my-pi passes a normalized copy and executes
/// the returned <c>input</c> (18.2.1 and later), so the full input is returned with the new command. Each harness
/// ignores the other's convention.
/// </para>
/// <para>
/// In both, a throwing handler blocks the tool call, so every path resolves to "no rewrite". The file imports neither
/// harness package, so it loads under either package name and under plain Node in tests.
/// </para>
/// </remarks>
internal static class PiExtension
{
    /// <summary>The extension source, before stamping.</summary>
    /// <param name="provider">The <c>dtk init</c>/<c>dtk hook</c> provider name.</param>
    /// <param name="harness">The harness's display name.</param>
    internal static string Body(string provider, string harness) =>
        $$"""
        // dtk (DotnetTokenKiller) rewrites the dotnet commands dtk supports to `dtk dotnet ...` before {{harness}} runs them.
        // Generated by `dtk init {{provider}}`; run it again to refresh this file.
        {{PluginRuntime.Source(provider, harness)}}
        export default function (pi) {
          pi.on("tool_call", async (event) => {
            try {
              if (event?.toolName !== "bash") return undefined;
              const input = event.input;
              const command = input?.command;
              if (typeof command !== "string" || !command.includes("dotnet")) return undefined;
              const rewritten = await rewrite(command);
              if (typeof rewritten !== "string" || rewritten === "") return undefined;
              // pi runs the args object it passed in; oh-my-pi passes a copy and runs the input a handler returns.
              input.command = rewritten;
              return { input: { ...input, command: rewritten } };
            } catch {
              // A throw would block the tool call.
              return undefined;
            }
          });
        }

        """;

    /// <summary>The extension as a generated artifact at <paramref name="path"/>.</summary>
    /// <param name="path">Where the extension is written.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="harness">The harness's display name.</param>
    internal static GeneratedArtifact Artifact(string path, string provider, string harness) =>
        // No dtk ever wrote an unstamped copy; the stamp prefix as legacy signature keeps the legacy branch unreachable,
        // exactly as for OpenCodePlugin.Artifact.
        new(path, Body(provider, harness), StampStyle.SlashComment, ArtifactStamping.StampPrefix);
}
```

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PiExtensionTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/PiExtension.cs tests/DotnetTokenKiller.Application.Tests/Integration/PiExtensionTests.cs
git commit -m "feat: generate the pi-family dtk extension"
```

### Task 5: `PiIntegrator` and `OhMyPiIntegrator`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/PiFamilyIntegrator.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/PiIntegrator.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/OhMyPiIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` (after `OpenCodeIntegrator`)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/PiIntegratorTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/OhMyPiIntegratorTests.cs`

**Interfaces:**
- Consumes: `PiExtension.Artifact` (Task 4), `HomePaths.PiAgentDir`/`OhMyPiAgentDir`, `HookPayloadKind.Pi`/`OhMyPi` (Task 3), `SharedInstructionArtifacts`, `IntegratorHelpers.WriteGeneratedFileAsync`, `UninstallHelpers.RemoveGeneratedFileAsync`, `RtkHookCoexistence.ReconcileFilesAsync`/`NoteRemainingExclusion`/`IsRtkRewriteReferencedIn`, `IntegratorHelpers.EnumerateSafely`.
- Produces: `PiIntegrator(RtkHookCoexistence rtk, HomePaths home)` (`ProviderName` `"pi"`, also `IHookApprovalInspector`), `OhMyPiIntegrator(RtkHookCoexistence rtk, HomePaths home)` (`"oh-my-pi"`); `PiIntegrator.TrustNote` (`internal const string`).

- [ ] **Step 1: Write the failing tests**

`PiIntegratorTests.cs` (fixture copied from `OpenCodeIntegratorTests`):

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class PiIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-pi-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string ExtensionPath => Path.Combine(ProjectDir, ".pi", "extensions", "dtk.js");
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

    private RtkHookCoexistence Rtk() => new(Home.ClaudeDir, RtkConfigPath);

    private PiIntegrator CreateSut() => new(Rtk(), Home);

    [Fact]
    public void ProviderName_IsPi() => CreateSut().ProviderName.Should().Be("pi");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedExtension()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, ExtensionPath);
        var extension = await File.ReadAllTextAsync(ExtensionPath);
        extension.Should().StartWith(PiExtension.Body("pi", "pi"));
        ArtifactStamping.IsAuthentic(extension).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_ProjectScope_NotesPiProjectTrust()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(PiIntegrator.TrustNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderPiAgentDirWithoutTrustNote()
    {
        var agentDir = Path.Combine(_tempDir, "custom-pi");
        _environment["PI_CODING_AGENT_DIR"] = agentDir;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(agentDir, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(agentDir, "extensions", "dtk.js"));
        result.Notes.Should().NotContain(PiIntegrator.TrustNote);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, ExtensionPath);
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_EditedExtension_IsLeftAloneWithoutForceAndReplacedWithIt()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        var edited = (await File.ReadAllTextAsync(ExtensionPath)).Replace("5000", "9000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(ExtensionPath, edited);

        var kept = await CreateSut().IntegrateAsync(ProjectDir, false, default);
        (await File.ReadAllTextAsync(ExtensionPath)).Should().Be(edited);
        kept.SkippedFiles.Should().Equal(ExtensionPath);

        var forced = await CreateSut().IntegrateAsync(ProjectDir, true, default);
        forced.UpdatedFiles.Should().Contain(ExtensionPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExtensionFromAnOlderDtk_IsRefreshedWithoutForce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExtensionPath)!);
        var older = PiExtension.Body("pi", "pi").Replace("5000", "4000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(ExtensionPath, ArtifactStamping.Apply(older, StampStyle.SlashComment));

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(ExtensionPath);
    }

    [Theory]
    [InlineData(".pi")]
    [InlineData(".omp")]
    public async Task IntegrateAsync_RtkExtensionInEitherHarness_ExcludesDotnetInRtkConfig(string folder)
    {
        var rtkExtension = Path.Combine(ProjectDir, folder, "extensions", "rtk.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(rtkExtension)!);
        await File.WriteAllTextAsync(rtkExtension, "// all rewrite logic lives in `rtk rewrite`\nawait pi.exec(\"rtk\", [\"rewrite\", cmd])");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }

    [Fact]
    public async Task UninstallAsync_SkillStillUsedByOpenCode_IsKept()
    {
        var useCase = new IntegrateUseCase([CreateSut(), new OpenCodeIntegrator(Rtk(), Home)]);
        await useCase.RunAsync("opencode", ProjectDir, false, default);
        await useCase.RunAsync("pi", ProjectDir, false, default);

        await useCase.UninstallAsync("pi", ProjectDir, false, default);

        File.Exists(ExtensionPath).Should().BeFalse();
        File.Exists(SkillPath).Should().BeTrue("OpenCode's hook is still registered in the project");
    }

    [Fact]
    public void InspectApproval_ProjectScope_ReportsTrustAsANote()
    {
        var sut = CreateSut();
        var project = sut.DescribeHooks(ProjectDir, HookScope.Project)[0];
        var global = sut.DescribeHooks(ProjectDir, HookScope.Global)[0];

        sut.InspectApproval(project, ProjectDir).Should().ContainSingle()
            .Which.Should().Be(new HookApprovalFinding("project trust", true, PiIntegrator.TrustNote));
        sut.InspectApproval(global, ProjectDir).Should().BeEmpty();
    }
}
```

`OhMyPiIntegratorTests.cs`: same fixture with `OhMyPiIntegrator`, `ExtensionPath` under `.omp/extensions`, and these
tests: `ProviderName_IsOhMyPi` (`"oh-my-pi"`); `IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedExtension`
(body `PiExtension.Body("oh-my-pi", "oh-my-pi")`); `IntegrateAsync_ProjectScope_HasNoTrustNote`
(`result.Notes.Should().NotContain(PiIntegrator.TrustNote)`); and
```csharp
    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderDotOmpAgent()
    {
        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(HomeDir, ".omp", "agent", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(HomeDir, ".omp", "agent", "extensions", "dtk.js"));
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx` — Expected: `PiIntegrator`/`OhMyPiIntegrator` do not exist.

- [ ] **Step 3: Implement the base**

`PiFamilyIntegrator.cs`:

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Installs dtk for a harness of the pi family: the shared <c>AGENTS.md</c> section and skill, and a generated
/// <c>extensions/dtk.js</c> (see <see cref="PiExtension"/>).
/// </summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and the harnesses' agent directories.</param>
internal abstract class PiFamilyIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <inheritdoc/>
    public abstract string ProviderName { get; }

    /// <summary>Gets the harness's display name, for the generated file's comments.</summary>
    protected abstract string HarnessName { get; }

    /// <summary>Gets the harness's project config folder, e.g. <c>.pi</c>.</summary>
    protected abstract string ProjectFolder { get; }

    /// <summary>Gets the payload kind the probe sends.</summary>
    protected abstract HookPayloadKind PayloadKind { get; }

    /// <summary>Gets the harness's global agent directory.</summary>
    /// <param name="paths">The home paths to resolve against.</param>
    protected abstract string GlobalAgentDirectory(HomePaths paths);

    /// <summary>Adds scope-specific notes after a successful install.</summary>
    /// <param name="scope">The scope installed.</param>
    /// <param name="context">The integration context.</param>
    protected virtual void AddNotes(HookScope scope, IntegrationContext context)
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = Path.Combine(ConfigDirectory(directory, scope), "extensions", "dtk.js");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                PayloadKind,
                PiExtension.Artifact(path, ProviderName, HarnessName))
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.Home, HookScope.Global, force, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        [InstructionsPath(directory, scope), SharedInstructionArtifacts.SkillPath(SkillsDirectory(directory, scope))];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var hookDirectory = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(hookDirectory, sharedInUse);

        await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
            InstructionsPath(hookDirectory, scope), SkillsDirectory(hookDirectory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await UninstallHelpers.RemoveGeneratedFileAsync(DescribeHooks(hookDirectory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(hookDirectory)));

        return context.ToResult();
    }

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string directory, HookScope scope, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            InstructionsPath(directory, scope), SkillsDirectory(directory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        AddNotes(scope, context);

        return context.ToResult();
    }

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? GlobalAgentDirectory(home) : Path.Combine(directory, ProjectFolder);

    private string InstructionsPath(string directory, HookScope scope) =>
        scope == HookScope.Global ? Path.Combine(GlobalAgentDirectory(home), "AGENTS.md") : Path.Combine(directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>
    /// Every file either pi-family harness would load as an extension, in both scopes: rtk installs its pi extension
    /// in pi's folders, and a user may copy it into oh-my-pi's.
    /// </summary>
    /// <param name="directory">The project root.</param>
    private List<string> RtkCandidates(string directory) =>
    [
        .. new[]
            {
                Path.Combine(directory, ".pi", "extensions"),
                Path.Combine(home.PiAgentDir, "extensions"),
                Path.Combine(directory, ".omp", "extensions"),
                Path.Combine(home.OhMyPiAgentDir, "extensions")
            }
            .Where(Directory.Exists)
            .SelectMany(folder => IntegratorHelpers.EnumerateSafely(folder, Directory.EnumerateFiles))
    ];
}
```

If `IntegratorHelpers.EnumerateSafely`'s signature differs from its use in `OpenCodeIntegrator.RtkCandidates`, copy
that call's form exactly.

- [ ] **Step 4: Implement the two providers**

`PiIntegrator.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for the pi coding agent.</summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and pi's agent directory.</param>
/// <remarks>
/// pi 0.73 and later load <c>.pi/extensions</c> only in a trusted project, and print, JSON and RPC modes skip it until
/// then. dtk never reads or writes pi's <c>trust.json</c>, so a project install and doctor only say so.
/// </remarks>
internal sealed class PiIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : PiFamilyIntegrator(rtk, home), IHookApprovalInspector
{
    /// <summary>What a project-scope install and doctor tell the user about pi's project trust.</summary>
    internal const string TrustNote =
        "pi loads .pi/extensions only in a trusted project: approve it when pi asks, or run /trust. "
        + "Print, JSON and RPC modes (pi -p) skip it until then; 'dtk init pi --global' avoids the prompt.";

    /// <inheritdoc/>
    public override string ProviderName => "pi";

    /// <inheritdoc/>
    protected override string HarnessName => "pi";

    /// <inheritdoc/>
    protected override string ProjectFolder => ".pi";

    /// <inheritdoc/>
    protected override HookPayloadKind PayloadKind => HookPayloadKind.Pi;

    /// <inheritdoc/>
    protected override string GlobalAgentDirectory(HomePaths paths) => paths.PiAgentDir;

    /// <inheritdoc/>
    protected override void AddNotes(HookScope scope, IntegrationContext context)
    {
        if (scope == HookScope.Project)
        {
            context.Notes.Add(TrustNote);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory) =>
        installation.Scope == HookScope.Project ? [new HookApprovalFinding("project trust", true, TrustNote)] : [];
}
```

`OhMyPiIntegrator.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for oh-my-pi (<c>omp</c>), a fork of pi with its own directories.</summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and oh-my-pi's agent directory.</param>
/// <remarks>
/// oh-my-pi's shell minimizer has its own dotnet filter, selected by program name; once the command is
/// <c>dtk dotnet …</c> it no longer applies, so dtk leaves oh-my-pi's <c>config.yml</c> alone.
/// </remarks>
internal sealed class OhMyPiIntegrator(RtkHookCoexistence rtk, HomePaths home) : PiFamilyIntegrator(rtk, home)
{
    /// <inheritdoc/>
    public override string ProviderName => "oh-my-pi";

    /// <inheritdoc/>
    protected override string HarnessName => "oh-my-pi";

    /// <inheritdoc/>
    protected override string ProjectFolder => ".omp";

    /// <inheritdoc/>
    protected override HookPayloadKind PayloadKind => HookPayloadKind.OhMyPi;

    /// <inheritdoc/>
    protected override string GlobalAgentDirectory(HomePaths paths) => paths.OhMyPiAgentDir;
}
```

`DependencyInjection.cs`, after the `OpenCodeIntegrator` line:
```csharp
        services.AddTransient<IProviderIntegrator, PiIntegrator>();
        services.AddTransient<IProviderIntegrator, OhMyPiIntegrator>();
```

If `IntegrationContext.Notes` is not a mutable list, use the same API `RtkReconcileOutcome.ApplyTo` uses to add a note.

- [ ] **Step 5: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PiIntegratorTests|FullyQualifiedName~OhMyPiIntegratorTests"`
Expected: PASS. If `IntegrateGlobalAsync` reports the files in a different order, fix the order in the test to match
`OpenCodeIntegratorTests.IntegrateGlobalAsync_*` (instructions, skill, plugin), not the implementation.

- [ ] **Step 6: Commit**

```bash
git add -A src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: dtk init pi and dtk init oh-my-pi"
```

### Task 6: Doctor, uninstall round-trips and CLI wiring

**Files:**
- Modify: `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallIntegrationTests.cs` (lines 30-33, 40-56, 301)
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs` (integrator list)
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/InitCommandSettings.cs:12,27`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs:110-111`
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs:37,109,171,202`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs:16`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs:366`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs:150`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`

**Interfaces:**
- Consumes: `PiIntegrator`, `OhMyPiIntegrator`, `PiIntegrator.TrustNote` (Task 5); `HookPayloadKind.Pi` (Task 3).

- [ ] **Step 1: Doctor tests**

In `HookHealthCheckerTests`, next to `OpenCode` (line 56), add
`private PiIntegrator Pi => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);`
and, modelled on `RunAsync_CurrentOpenCodePlugin_PassesAndProbesWithTheOpenCodePayload`:

```csharp
    private static readonly string[] PiHookArguments = ["hook", "pi"];

    [Fact]
    public async Task RunAsync_CurrentPiExtension_ProbesWithTheCommandPayloadAndNotesTrust()
    {
        await Pi.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([Pi], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("pi hook (project)", "pi hook probe (project)", "pi project trust (project)");
        checks.Should().OnlyContain(c => c.Passed && !c.IsWarning);
        checks[2].Message.Should().Be(PiIntegrator.TrustNote);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(PiHookArguments)),
            Arg.Is<string>(payload => payload.StartsWith("{\"command\":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StalePiExtension_WarnsWithTheRemedy()
    {
        var path = Pi.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var older = PiExtension.Body("pi", "pi").Replace("5000", "4000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(older, StampStyle.SlashComment));

        var checks = await _sut.RunAsync([Pi], _tempDir, default);

        checks[0].Message.Should().Contain("stale").And.Contain("dtk init pi");
    }
```

- [ ] **Step 2: Add both providers to the cross-provider tests**

- `UninstallIntegrationTests`: add `"pi", "oh-my-pi"` to `AllProviders` and `GlobalProviders`; add
  `new PiIntegrator(Rtk(), home), new OhMyPiIntegrator(Rtk(), home),` after `OpenCodeIntegrator` in `Integrators()`;
  add `"pi", "oh-my-pi"` to `hookProviders` in `Doctor_AfterUninstallingEveryHook_ReportsNoHookInstalled`.
- `HookDescriptionTests`: add `new PiIntegrator(...)` and `new OhMyPiIntegrator(...)` with the same arguments as `OpenCodeIntegrator`.
- `InitCommandTests`: add `[InlineData("pi")]` and `[InlineData("oh-my-pi")]` after `opencode`.
- `ParityCases.cs:16`: add `"pi", "oh-my-pi"` after `"antigravity"`.
- `CompletionCommandTests.cs:150`: append `.And.Contain("oh-my-pi")`.

- [ ] **Step 3: Run to see failures**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests` — Expected: PASS except nothing CLI-related; then
`dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Completion|FullyQualifiedName~InitCommand|FullyQualifiedName~CliConfigurator"`
Expected: completion test FAILS (no `oh-my-pi` yet).

- [ ] **Step 4: Wire the CLI**

- `InitCommandSettings.cs:12`: `"AI assistant provider to set up (claude, copilot, copilot-cli, gemini, codex, opencode, antigravity, pi, oh-my-pi, cursor, windsurf, aider, jetbrains)"`
- `InitCommandSettings.cs:27`: `"Install into the user's home config (claude, copilot-cli, gemini, codex, opencode, antigravity, pi, oh-my-pi, aider) instead of a project"`
- `CliConfigurator.cs`, after line 111:
  ```csharp
              .WithExample(InitCommand, "pi", GlobalOption)
  ```
- `CompletionCommand.cs`:
  - bash (37): `local init_providers="claude copilot copilot-cli gemini codex opencode antigravity pi oh-my-pi cursor windsurf aider jetbrains"`
  - zsh, after line 109: `'pi:Install dtk extension and instructions for pi'` and `'oh-my-pi:Install dtk extension and instructions for oh-my-pi'`
  - fish, after line 171, two lines in the same form: `-a pi -d 'Install dtk extension and instructions for pi'` and `-a oh-my-pi -d 'Install dtk extension and instructions for oh-my-pi'`
  - pwsh (202): add `'pi', 'oh-my-pi'` after `'antigravity'`.

- [ ] **Step 5: Update the help snapshot**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CliConfiguratorTests"`
Expected: `Configure_InitHelp_MatchesSnapshot` FAILS and writes a `.received.txt` beside the `.verified.txt`. Read the
diff: it must show only the new example line and the two provider lists. Then
`mv tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.received.txt tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`
and re-run: PASS.

- [ ] **Step 6: Run the suites**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests` and
`dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Completion|FullyQualifiedName~InitCommand|FullyQualifiedName~CliConfigurator|FullyQualifiedName~Hook"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -m "feat: wire pi and oh-my-pi into doctor, uninstall, help and completion"
```

### Task 7: Run the generated extension under Node

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/PiExtensionTests.cs`

**Interfaces:**
- Consumes: `PiExtension.Body` (Task 4); `NodeFact`/`NodeUnixFact`, `ParityProcess`, `DtkLauncher.TestBinaryVariable`, `ExecutableSearch` (existing helpers used by `OpenCodePluginTests`).

- [ ] **Step 1: Write the tests**

```csharp
using System.Diagnostics;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Aot;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the extension <c>dtk init pi</c> generates under Node, as pi and oh-my-pi call it: the default export with an
/// API that records the <c>tool_call</c> handler, then that handler with a bash event, against a real or fake <c>dtk</c>.
/// </summary>
public sealed class PiExtensionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-piext-{Guid.NewGuid()}");

    public PiExtensionTests()
    {
        // pi's layout: the extension under .pi/extensions/ with no package.json, so Node detects ESM by syntax.
        var extensions = Directory.CreateDirectory(Path.Combine(_dir, ".pi", "extensions")).FullName;
        File.WriteAllText(Path.Combine(extensions, "dtk.js"), ArtifactStamping.Apply(PiExtension.Body("pi", "pi"), StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import factory from "./.pi/extensions/dtk.js";
            const [toolName, command] = process.argv.slice(2);
            let handler;
            factory({ on: (name, fn) => { if (name === "tool_call") handler = fn; } });
            const input = command === "<none>" ? { timeout: 7, cwd: "w" } : { command, timeout: 7, cwd: "w" };
            const result = await handler({ type: "tool_call", toolName, toolCallId: "c", input });
            process.stdout.write(JSON.stringify({ command: input.command ?? null, result: result ?? null }));
            """);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static string RealDtkDirectory
    {
        get
        {
            var binary = Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable);
            return string.IsNullOrWhiteSpace(binary) ? AppContext.BaseDirectory : Path.GetDirectoryName(binary.Trim())!;
        }
    }

    [NodeFact]
    public async Task Bash_DotnetCommand_IsRewrittenBothWaysByTheRealDtkAsync()
    {
        var result = await RunAsync("bash", "dotnet build # répertoire", RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire", "pi runs the mutated args");
        var input = result["result"]!["input"]!;
        input["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire", "oh-my-pi runs the returned input");
        input["timeout"]!.GetValue<int>().Should().Be(7);
        input["cwd"]!.GetValue<string>().Should().Be("w");
    }

    [NodeFact]
    public async Task DtkMissingFromPath_LeavesTheCommandAndReturnsNothingAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await RunAsync("bash", "dotnet build", empty);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
        result["result"].Should().BeNull();
    }

    [NodeFact]
    public async Task MissingOrNonStringCommand_PassesThroughAsync()
    {
        var result = await RunAsync("bash", "<none>", RealDtkDirectory);

        result["command"].Should().BeNull();
        result["result"].Should().BeNull();
    }

    [NodeUnixFact]
    public async Task OtherToolOrNoDotnet_NeverStartsDtkAsync()
    {
        var bin = FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await RunAsync("read", "dotnet build", bin))["result"].Should().BeNull();
        (await RunAsync("bash", "ls -la", bin))["result"].Should().BeNull();
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeUnixFact]
    public async Task FailingOrMalformedDtk_LeavesTheCommandAsync()
    {
        foreach (var script in new[] { "cat >/dev/null; echo 'not json'", "cat >/dev/null; exit 3" })
        {
            var result = await RunAsync("bash", "dotnet test", FakeDtk(script));

            result["command"]!.GetValue<string>().Should().Be("dotnet test");
            result["result"].Should().BeNull();
        }
    }

    [NodeUnixFact]
    public async Task HangingDtk_TimesOutAndLeavesTheCommandAsync()
    {
        var bin = FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await RunAsync("bash", "dotnet test", bin);

        result["command"]!.GetValue<string>().Should().Be("dotnet test");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the extension gives dtk 5 seconds, under oh-my-pi's 30 s handler limit");
    }
}
```

Then copy `FakeDtk` and both `RunAsync` overloads verbatim from `OpenCodePluginTests` (lines ~112-154) into this class,
changing only the `node` arguments to `driver.mjs`, `toolName`, `command`. Keep the `PATH` restriction comment.

- [ ] **Step 2: Run them**

Run: `dtk dotnet build DotnetTokenKiller.slnx` then `DTK_NODE_REQUIRED=1 dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --no-build --filter "FullyQualifiedName~PiExtensionTests"`
Expected: 6 PASS (Node must be on PATH; `node --version` first).

- [ ] **Step 3: Prove the tests catch a broken extension**

Temporarily change `return { input: { ...input, command: rewritten } };` in `PiExtension.cs` to
`return { input: { command: rewritten } };`, rebuild, re-run: `Bash_DotnetCommand_IsRewrittenBothWaysByTheRealDtkAsync`
must FAIL on `timeout`. Revert, rebuild, re-run: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests/PiExtensionTests.cs
git commit -m "test: run the generated pi-family extension under Node"
```

### Task 8: Documentation

**Files:**
- Modify: `README.md` (~91 integration count, ~222-229 `--global` list, ~243 provider table)
- Modify: `docs/index.md` (~95 `dtk-agent` spans)
- Modify: `docs/articles/ai-agent-setup.md` (`--global` list, uninstall notes, new `## pi` and `## oh-my-pi` sections after `## OpenCode`)
- Modify: `docs/articles/usage.md` (~206), `docs/articles/getting-started.md` (~150)
- Modify: `CLAUDE.md` (a paragraph after the `dtk init opencode` one)
- Modify: `src/DotnetTokenKiller.Cli/README.md`

- [ ] **Step 1: Update every provider list**

In each file above, add pi and oh-my-pi wherever opencode appears in a provider list, in the same order as
`InitCommandSettings` (after antigravity). Bump the README's integration count by two (11 → 13) and add two table
rows copied from the OpenCode row's shape: `pi` — "`.pi/extensions/dtk.js` + `AGENTS.md` + skill", `--global` yes;
`oh-my-pi` — "`.omp/extensions/dtk.js` + `AGENTS.md` + skill", `--global` yes.

- [ ] **Step 2: Write the two setup sections**

In `docs/articles/ai-agent-setup.md`, after the OpenCode section, add (matching the OpenCode section's heading
level and code-block style):

```markdown
## pi

`dtk init pi` writes a generated extension, `.pi/extensions/dtk.js`, plus the shared `AGENTS.md` section and the
`.agents/skills/dotnet-token-killer` skill. `--global` writes `~/.pi/agent/extensions/dtk.js` (or under
`$PI_CODING_AGENT_DIR`) and `~/.pi/agent/AGENTS.md`, and the skill to `~/.agents/skills`.

The extension handles pi's `tool_call` event: for a `bash` call whose command contains `dotnet`, it asks
`dtk hook pi` for the rewrite and changes the command before pi runs it. It finds `dtk` on `PATH` itself and never
blocks a tool call: if dtk is missing, slow or fails, the command runs unchanged. Commands you type with `!` are not
rewritten.

pi 0.73 and later load `.pi/extensions` only in a trusted project: approve it when pi asks, or run `/trust`. Print,
JSON and RPC modes (`pi -p`) skip project extensions until then, so use `--global` for those. Requires pi 0.73.1 or
later.

If rtk's pi extension (`rtk init --agent pi`) is installed, `dtk init pi` adds `dotnet` to rtk's `exclude_commands`
so dtk owns dotnet commands.

## oh-my-pi

`dtk init oh-my-pi` writes the same extension to `.omp/extensions/dtk.js` (`--global`: `~/.omp/agent/extensions/dtk.js`
and `~/.omp/agent/AGENTS.md`), plus the shared `AGENTS.md` section and skill. oh-my-pi does not read pi's `.pi`
folder, so install each harness you use. Requires oh-my-pi 18.2.1 or later; older versions load the extension but
run commands unchanged.

oh-my-pi's shell minimizer (`shellMinimizer.enabled`, on by default) has its own `dotnet` filter, chosen by program
name. Once a command runs as `dtk dotnet …` that filter no longer applies, so the output is filtered once, by dtk.
dtk does not change oh-my-pi's `config.yml`.
```

- [ ] **Step 3: CLAUDE.md paragraph**

After the `dtk init opencode` paragraph in `CLAUDE.md`, add:

```markdown
`dtk init pi` and `dtk init oh-my-pi` write the shared `AGENTS.md` section and skill plus a generated, stamped
`extensions/dtk.js` (`.pi/`, `.omp/`; `--global`: `$PI_CODING_AGENT_DIR` or `~/.pi/agent`, and `~/.omp/agent`). One
body (`PiExtension`) serves both: its `tool_call` handler mutates `event.input.command` (pi runs the args it passed)
*and* returns `{ input }` (oh-my-pi runs a returned input, 18.2.1+), and never throws, since a throw blocks the tool
in both. It shares `PluginRuntime` (PATH lookup, spawn) with the OpenCode plugin, whose bytes a test pins. pi's
project scope needs project trust; init and doctor only say so. `PiExtensionTests` run it under Node.
```

- [ ] **Step 4: Verify docs build inputs and commit**

Run: `rtk proxy grep -rn "antigravity" README.md docs/index.md docs/articles/usage.md docs/articles/getting-started.md src/DotnetTokenKiller.Cli/README.md`
and check every hit that lists providers now also lists pi and oh-my-pi.

```bash
git add -A README.md docs CLAUDE.md src/DotnetTokenKiller.Cli/README.md
git commit -m "docs: document the pi and oh-my-pi integrations"
```

### Task 9: Full verification and end-to-end check

**Files:**
- Create: `artifacts/e2e-pi.md` (not committed; its content goes in the PR description)

- [ ] **Step 1: Format, build, full test run**

Run, in order:
```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test tests/DotnetTokenKiller.Application.Tests
DTK_NODE_REQUIRED=1 dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests
```
Expected: no format changes left, 0 warnings, all tests pass. (The CLI integration suite is slow; it may be run
in the background, split by class with `--no-build`, if it exceeds the tool timeout.)

- [ ] **Step 2: End-to-end on pi 0.87.1**

```bash
cd /tmp && rm -rf pi-e2e && mkdir pi-e2e && cd pi-e2e
npm i @earendil-works/pi-coding-agent@0.87.1 @oh-my-pi/pi-coding-agent@18.3.2
dotnet new console -o app >/dev/null
```
Install the branch's dtk on `PATH` (`dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -o /tmp/pi-e2e/feed` then
`dotnet tool install --tool-path /tmp/pi-e2e/tool DotnetTokenKiller --add-source /tmp/pi-e2e/feed --version <packed version>`, and prepend
`/tmp/pi-e2e/tool` to `PATH`). In `app/`: `dtk init pi`, then load the extension against a scripted tool call. pi
ships an RPC mode; the cheapest deterministic route is its SDK: write `e2e-pi.mjs` that creates a session with
`createAgentSession` (from `@earendil-works/pi-coding-agent`), with `--approve`-equivalent trust, a fake model or
none, and calls the session's bash tool with `dotnet build` through the extension runner — or, if the SDK cannot run
a tool without a model, run `pi --approve -p "run: dotnet build"` with a real model key if one is available.
Record which route was used. Pass condition: the bash tool's executed command is `dtk dotnet build` (visible in the
tool result's filtered output or pi's session log). Repeat with `dtk init pi --global` in a scratch `HOME`.

- [ ] **Step 3: End-to-end on oh-my-pi 18.3.2**

Same as Step 2 with `dtk init oh-my-pi` and `omp` (`@oh-my-pi/pi-coding-agent`). Pass condition: the executed command
is `dtk dotnet build`, and oh-my-pi's minimizer label (if logged) is not `dotnet`.

- [ ] **Step 4: Write up**

Record in `artifacts/e2e-pi.md`: harness versions, route used, commands, observed executed command, and anything
that did not behave as the spec predicts. If either harness does not rewrite, stop and report — do not ship.

- [ ] **Step 5: Final commit if formatting changed anything**

```bash
git status --short
git add -A src tests && git commit -m "style: format" || true
```

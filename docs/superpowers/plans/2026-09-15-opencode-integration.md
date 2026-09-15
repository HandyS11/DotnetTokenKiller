# OpenCode integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `dtk init opencode` and `dtk hook opencode`: a generated JavaScript plugin that asks `dtk` to rewrite
`dotnet …` shell commands, plus the shared `AGENTS.md` section and skill.

**Architecture:** OpenCode has no command hooks, only in-process plugins. dtk generates a stamped `dtk.js` whose
`tool.execute.before` hook, for `bash` commands containing `dotnet`, spawns `dtk hook opencode` without a shell and
writes the reply back into `output.args.command` in place. The rewrite logic stays in C#. `doctor` classifies the
plugin by its provenance stamp instead of searching JSON.

**Tech Stack:** .NET 10, System.Text.Json `JsonNode`, xunit 2, FluentAssertions, Node.js (plugin execution tests),
OpenCode 1.18.x (cost measurement only).

**Spec:** `docs/superpowers/specs/2026-09-15-codex-opencode-antigravity-design.md` (sections 1, 2, 3, 5, 6, 7, 8 PR 2).

## Global Constraints

- This is PR 2 of 3. Start only after PR 1 (`docs/superpowers/plans/2026-09-15-codex-integration.md`) has merged.
  Branch `feat/opencode-integration` from an up-to-date `develop`.
- Plugin file names: `.opencode/plugins/dtk.js` (project) and `$XDG_CONFIG_HOME/opencode/plugins/dtk.js`, default
  `~/.config/opencode/plugins/dtk.js` (global). JavaScript, not TypeScript; its only import is `node:child_process`.
- The plugin never throws, never replaces `output.args`, never uses Bun `$` or `which`, and exports exactly one
  function. It spawns `dtk` only when `input.tool === "bash"` and the command contains `dotnet`.
- Target OpenCode 1.x; the v2 beta is out of scope.
- `dtk hook <provider>` always exits 0 and prints nothing on any failure; it never builds the DI container.
- `TreatWarningsAsErrors` with Roslynator, SonarAnalyzer and NetAnalyzers; file-scoped namespaces; `var`; `_camelCase`
  private fields; `Async` suffix; LF, no trailing whitespace, no BOM; 4-space `.cs`, 2-space JSON/YAML.
- No new NuGet packages; the libraries are `IsAotCompatible` (use `JsonNode`, no reflection).
- NSubstitute cannot proxy the Application assembly's internal interfaces; use hand-written fakes for them.
- Run dotnet through dtk (`dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test <project> --filter …`). The
  build-spawning CLI integration tests cannot pass on this machine; run the filters each task names.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

## What PR 1 already provides

- `SharedInstructionArtifacts.WriteAgentsFilesAsync(string instructionsPath, string skillsDirectory, IntegrationContext, CancellationToken)`
- `HomePaths(string home, Func<string, string?> environment)`, `HomePaths.AgentsSkillsDir`, private `RootedOrDefault(string variable, string fallback)`
- `HookInstallation(string ProviderName, HookScope Scope, string RegistrationPath, string Command, string? LegacyScriptPath, HookPayloadKind PayloadKind)`
- `HookPayloadKind.CodexCli = 3`; `HookPayloads.NamesAnotherTool`; usage string `dtk hook <claude|gemini|copilot-cli|codex>`
- `RtkHookCoexistence.ReconcileFilesAsync(IReadOnlyList<string>, CancellationToken)`, `RtkReconcileOutcome.ApplyTo(IntegrationContext)`
- `DiagnosticCheck.Warning`, `IHookApprovalInspector`, and `HookHealthChecker.CheckAsync(IHookIntegrator, HookInstallation, Registration, string projectDirectory, CancellationToken)`
- `WriteSectionBasedFileAsync` reports a file whose section is already current as unchanged.

---

### Task 1: `HomePaths.OpenCodeConfigDir` and `StampStyle.SlashComment`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs:6-14,61-73`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/ArtifactStampingTests.cs`

**Interfaces:**
- Produces: `internal string HomePaths.OpenCodeConfigDir` (`$XDG_CONFIG_HOME/opencode` when rooted, else
  `~/.config/opencode`); `StampStyle.SlashComment = 2`, rendered `// dtk-generated sha256:<hash>`.

- [ ] **Step 1: Write the failing tests**

`HomePathsTests`:

```csharp
    [Fact]
    public void OpenCodeConfigDir_DefaultsToDotConfigOpencode()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home).OpenCodeConfigDir.Should().Be(Path.Combine(home, ".config", "opencode"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenCodeConfigDir_HonorsOnlyAnAbsoluteXdgConfigHome(bool rooted)
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var xdg = rooted ? Path.Combine(Path.GetTempPath(), "xdg") : "relative/xdg";

        var expected = rooted ? Path.Combine(xdg, "opencode") : Path.Combine(home, ".config", "opencode");
        new HomePaths(home, name => name == "XDG_CONFIG_HOME" ? xdg : null).OpenCodeConfigDir.Should().Be(expected);
    }
```

`ArtifactStampingTests`:

```csharp
    [Fact]
    public void Apply_SlashComment_EndsWithAJavaScriptLineCommentThatVerifies()
    {
        var content = ArtifactStamping.Apply("export const DtkPlugin = async () => ({});\n", StampStyle.SlashComment);

        content.Should().MatchRegex(@"\n// dtk-generated sha256:[0-9a-f]{64}\n$");
        ArtifactStamping.IsAuthentic(content).Should().BeTrue();
        ArtifactStamping.IsAuthentic(content.Replace("async", "sync", StringComparison.Ordinal)).Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `OpenCodeConfigDir` and `StampStyle.SlashComment` do not exist.

- [ ] **Step 3: Implement**

`HomePaths.cs`:

```csharp
    /// <summary>
    /// Gets OpenCode's user config directory: <c>$XDG_CONFIG_HOME/opencode</c> when that variable is an absolute path,
    /// else <c>~/.config/opencode</c> — on Windows too, where OpenCode also uses <c>~/.config</c>.
    /// </summary>
    internal string OpenCodeConfigDir =>
        Path.Combine(RootedOrDefault("XDG_CONFIG_HOME", Path.Combine(Home, ".config")), "opencode");
```

`ArtifactStamping.cs` — enum:

```csharp
    /// <summary>A <c>//</c> line comment, for JavaScript artifacts such as the OpenCode plugin.</summary>
    SlashComment = 2
```

and in `Apply`:

```csharp
        var line = style switch
        {
            StampStyle.HtmlComment => $"<!-- {stamp} -->",
            StampStyle.SlashComment => $"// {stamp}",
            _ => $"# {stamp}"
        };
```

`TryParse` needs no change: a `//` stamp, like `#`, has only the newline after the hash. Update the
`LineTerminators` doc to say so.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HomePathsTests|FullyQualifiedName~ArtifactStampingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration tests/DotnetTokenKiller.Application.Tests/Integration
git commit -m "feat: resolve OpenCode's config directory and stamp JavaScript artifacts"
```

---

### Task 2: `dtk hook opencode`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (enum)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (`BuildPayload`)
- Modify: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs` (usage)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `HookIntegrationTests.cs`

**Interfaces:**
- Produces: `HookPayloadKind.OpenCode = 4`; `TryGetKind("opencode")`; stdin `{"command":"…"}` → stdout
  `{"command":"<rewritten>"}` or nothing; usage `dtk hook <claude|gemini|copilot-cli|codex|opencode>`; probe payload
  `{"command":"<all subcommands>"}`.

- [ ] **Step 1: Write the failing tests**

`HookPayloadsTests` — add `[InlineData("opencode", HookPayloadKind.OpenCode)]` to `TryGetKind_KnownProvider_Resolves`, then:

```csharp
    [Fact]
    public void OpenCode_Rewrite_PrintsOnlyTheRewrittenCommand()
    {
        var reply = JsonNode.Parse(Reply(HookPayloadKind.OpenCode, """{"command":"dotnet build # répertoire","extra":1}""")!)!.AsObject();

        reply.Should().ContainSingle("only the command crosses the boundary");
        reply["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire");
    }

    [Theory]
    [InlineData("""{"command":"ls ~/dotnet-notes"}""")]
    [InlineData("""{"command":""}""")]
    [InlineData("""{"command":7}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"command":"a","command":"b"}""")]
    public void OpenCode_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.OpenCode, payload).Should().BeNull();
    }
```

`HookEntryPointTests`/`HookIntegrationTests`: replace the usage fragment with
`dtk hook <claude|gemini|copilot-cli|codex|opencode>`, and add:

```csharp
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_OpenCode_RewritesAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"command":"dotnet format --verify-no-changes"}""", "hook", "opencode");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["command"]!.GetValue<string>().Should().Be("dtk dotnet format --verify-no-changes");
    }
```

`HookHealthCheckerTests` gains a probe-shape test in Task 4 (it needs the integrator).

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `HookPayloadKind.OpenCode` does not exist.

- [ ] **Step 3: Implement**

Enum: `/// <summary>dtk's own OpenCode plugin payload: <c>{"command": …}</c>.</summary> OpenCode = 4`.

`HookPayloads`: `"opencode" => (true, HookPayloadKind.OpenCode),` in `TryGetKind`;
`HookPayloadKind.OpenCode => ReplyToOpenCode(root),` in `Reply`; and:

```csharp
    /// <summary>
    /// Replies to dtk's generated OpenCode plugin. The contract is dtk's own, because dtk writes both ends: only the
    /// command crosses the process boundary.
    /// </summary>
    /// <param name="root">The parsed payload.</param>
    private static string? ReplyToOpenCode(JsonNode? root)
    {
        if (root is not JsonObject payload || !TryRewrite(payload, out _, out var rewritten))
        {
            return null;
        }

        return new JsonObject { ["command"] = rewritten }.ToJsonString();
    }
```

`HookHealthChecker.BuildPayload`:

```csharp
            HookPayloadKind.OpenCode => new JsonObject { ["command"] = command },
```

`HookEntryPoint.Usage`: list `opencode`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: answer dtk's OpenCode plugin with dtk hook opencode"
```

---

### Task 3: The plugin template and `OpenCodeIntegrator`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/OpenCodePlugin.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/OpenCodeIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` (`HookInstallation`)
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/OpenCodeIntegratorTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs`

**Interfaces:**
- Consumes: `StampStyle.SlashComment`, `HomePaths.OpenCodeConfigDir` (Task 1), `HookPayloadKind.OpenCode` (Task 2).
- Produces:
  - `internal static class OpenCodePlugin` with `internal static readonly string Body`,
    `internal const string LegacySignature = "export const DtkPlugin"`,
    `internal const string InvocationSignature = "spawn(\"dtk\", [\"hook\", \"opencode\"]"`,
    `internal static GeneratedArtifact Artifact(string path)`.
  - `HookInstallation` gains a last positional parameter `GeneratedArtifact? PluginArtifact = null`; non-null means the
    registration is that generated file rather than JSON.
  - `internal sealed class OpenCodeIntegrator(RtkHookCoexistence rtk, HomePaths home) : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator`,
    `ProviderName == "opencode"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class OpenCodeIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-opencode-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string PluginPath => Path.Combine(ProjectDir, ".opencode", "plugins", "dtk.js");
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

    private OpenCodeIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    [Fact]
    public void ProviderName_IsOpencode() => CreateSut().ProviderName.Should().Be("opencode");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedPlugin()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        var plugin = await File.ReadAllTextAsync(PluginPath);
        plugin.Should().StartWith(OpenCodePlugin.Body);
        ArtifactStamping.IsAuthentic(plugin).Should().BeTrue();
    }

    [Fact]
    public void PluginBody_KeepsTheContractOpenCodeAndWindowsNeed()
    {
        var body = OpenCodePlugin.Body;

        body.Should().Contain("import { spawn } from \"node:child_process\";");
        body.Should().Contain(OpenCodePlugin.InvocationSignature);
        body.Should().Contain("input?.tool !== \"bash\"").And.Contain("command.includes(\"dotnet\")");
        body.Should().Contain("output.args.command = rewritten").And.NotContain("output.args =");
        body.Should().NotContain("$`").And.NotContain("which").And.NotContain("shell: true");
        body.Split("export ").Should().HaveCount(2, "OpenCode v1 fails a plugin silently when any export is not a function");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_EditedPlugin_IsLeftAloneWithoutForceAndReplacedWithIt()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        var edited = (await File.ReadAllTextAsync(PluginPath)).Replace("5000", "9000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(PluginPath, edited);

        var kept = await CreateSut().IntegrateAsync(ProjectDir, false, default);
        (await File.ReadAllTextAsync(PluginPath)).Should().Be(edited);
        kept.SkippedFiles.Should().Equal(PluginPath);

        var forced = await CreateSut().IntegrateAsync(ProjectDir, true, default);
        forced.UpdatedFiles.Should().Contain(PluginPath);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(PluginPath)).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderXdgConfigHomeAndTheSharedSkillsDirectory()
    {
        var xdg = Path.Combine(_tempDir, "xdg");
        _environment["XDG_CONFIG_HOME"] = xdg;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(xdg, "opencode", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(xdg, "opencode", "plugins", "dtk.js"));
    }

    [Fact]
    public async Task IntegrateAsync_AfterCodex_SharesOneSectionAndOneSkill()
    {
        await new CodexIntegrator(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home).IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath);
        result.CreatedFiles.Should().Equal(PluginPath);
        (await File.ReadAllTextAsync(AgentsPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Theory]
    [InlineData("plugins", "rtk.ts")]
    [InlineData("plugin", "rtk.ts")]
    public async Task IntegrateAsync_RtkOpenCodePlugin_ExcludesDotnetInRtkConfig(string folder, string file)
    {
        var rtkPlugin = Path.Combine(ProjectDir, ".opencode", folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(rtkPlugin)!);
        await File.WriteAllTextAsync(rtkPlugin, "const result = await $`rtk rewrite ${command}`.quiet().nothrow()");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `OpenCodePlugin` and `OpenCodeIntegrator` do not exist.

- [ ] **Step 3: Implement `OpenCodePlugin`**

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>The OpenCode plugin <c>dtk init opencode</c> generates.</summary>
/// <remarks>
/// <para>
/// OpenCode runs no hook commands, only in-process plugins, so this plugin is the hook: for a <c>bash</c> tool call
/// whose command contains <c>dotnet</c>, it runs <c>dtk hook opencode</c> and writes the reply into
/// <c>output.args.command</c>. The rewrite itself stays in <c>DotnetCommandRewriter</c>.
/// </para>
/// <para>
/// Every line answers a known failure: <c>includes("dotnet")</c> is a superset of what the rewriter matches, so only
/// dotnet commands pay a process start; a throw would fail the tool call, so every path resolves to "no rewrite";
/// OpenCode passes the same <c>args</c> object on to the tool, so the command is changed in place and never replaced;
/// Bun's <c>$</c> is undefined when OpenCode runs on Node (Desktop) and <c>which</c> does not exist on Windows, so the
/// plugin uses <c>spawn</c> without a shell, which on Windows finds <c>dtk.exe</c>; and OpenCode v1 silently drops a
/// plugin with any non-function export.
/// </para>
/// </remarks>
internal static class OpenCodePlugin
{
    /// <summary>Present in every generation of the plugin; recognizes an unstamped copy.</summary>
    internal const string LegacySignature = "export const DtkPlugin";

    /// <summary>The call a plugin still runs dtk through, even after local edits.</summary>
    internal const string InvocationSignature = "spawn(\"dtk\", [\"hook\", \"opencode\"]";

    /// <summary>The plugin source, before stamping.</summary>
    internal static readonly string Body =
        """
        // dtk (DotnetTokenKiller) rewrites `dotnet build|test|restore|clean|format|list package` to `dtk dotnet ...`
        // before OpenCode runs a shell command. Generated by `dtk init opencode`; run it again to refresh this file.
        import { spawn } from "node:child_process";

        const TIMEOUT_MS = 5000;

        function rewrite(command) {
          return new Promise((resolve) => {
            let child;
            try {
              child = spawn("dtk", ["hook", "opencode"], { stdio: ["pipe", "pipe", "ignore"], windowsHide: true });
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

        """;

    /// <summary>The plugin as a generated artifact at <paramref name="path"/>.</summary>
    /// <param name="path">Where the plugin is written.</param>
    internal static GeneratedArtifact Artifact(string path) =>
        new(path, Body, StampStyle.SlashComment, LegacySignature);
}
```

(The comment line says `dtk dotnet ...` with three dots so the body stays ASCII.)

- [ ] **Step 4: Implement `HookInstallation.PluginArtifact` and `OpenCodeIntegrator`**

`IHookIntegrator.cs`: append `GeneratedArtifact? PluginArtifact = null` to `HookInstallation`, documented
`The generated plugin file that is this registration, for harnesses that load plugins instead of running hook commands; null for a JSON registration.`

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for OpenCode.</summary>
/// <param name="rtk">Detects and reconciles an rtk plugin so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and OpenCode config directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.opencode/plugins/dtk.js</c>, the generated plugin (see <see cref="OpenCodePlugin"/>)</description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class OpenCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <inheritdoc/>
    public string ProviderName => "opencode";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = Path.Combine(ConfigDirectory(directory, scope), "plugins", "dtk.js");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.OpenCode,
                OpenCodePlugin.Artifact(path))
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
            Path.Combine(home.OpenCodeConfigDir, "AGENTS.md"), home.AgentsSkillsDir, home.Home, HookScope.Global,
            force, cancellationToken);

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.OpenCodeConfigDir : Path.Combine(directory, ".opencode");

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

        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(hookDirectory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(hookDirectory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>Every plugin file OpenCode would load from either scope, where rtk installs its own plugin.</summary>
    /// <param name="hookDirectory">The project root.</param>
    private List<string> RtkCandidates(string hookDirectory) =>
    [
        .. new[] { HookScope.Project, HookScope.Global }
            .SelectMany(scope => new[] { "plugin", "plugins" }.Select(folder => Path.Combine(ConfigDirectory(hookDirectory, scope), folder)))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder))
    ];
}
```

Register after `CodexIntegrator` in `DependencyInjection.cs`:
`services.AddTransient<IProviderIntegrator, OpenCodeIntegrator>();`

`HookDescriptionTests`: add `new OpenCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home)` to
both arrays and `["opencode"] = "dtk hook opencode"`. In `DescribeHooks_Project_NamesTheRegistrationInitWrote`, a
plugin registration holds the invocation signature, not the command string:

```csharp
                var registration = await File.ReadAllTextAsync(installation.RegistrationPath);
                registration.Should().Contain(installation.PluginArtifact is null ? installation.Command : OpenCodePlugin.InvocationSignature);
```

`InitCommandTests` routing theory: add `[InlineData("opencode")]`. `ParityCases.cs`: add `"opencode"` after `"codex"`.

- [ ] **Step 5: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application tests
git commit -m "feat: dtk init opencode generates a plugin that runs dtk hook opencode"
```

---

### Task 4: doctor classifies the plugin

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Consumes: `HookInstallation.PluginArtifact`, `OpenCodePlugin.InvocationSignature` (Task 3).
- Produces: two new `RegistrationKind` values, `Stale = 4` (a stamped plugin from an older dtk: fail with
  `stale — <path> was written by an older dtk. Run 'dtk init opencode[ --global]'.`) and `Modified = 5` (unstamped or
  edited, but still running `dtk hook opencode`: pass `registered (modified locally)`, then probe).

- [ ] **Step 1: Write the failing tests**

```csharp
    /// <summary>The arguments the probe must pass to <c>dtk</c> for the OpenCode hook.</summary>
    private static readonly string[] OpenCodeHookArguments = ["hook", "opencode"];

    private OpenCodeIntegrator OpenCode => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);

    [Fact]
    public async Task RunAsync_CurrentOpenCodePlugin_PassesAndProbesWithTheOpenCodePayload()
    {
        await OpenCode.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("opencode hook (project)", "opencode hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed && !c.IsWarning);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(OpenCodeHookArguments)),
            Arg.Is<string>(payload => payload.StartsWith("{\"command\":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PluginFromAnOlderDtk_FailsStaleWithTheInitRemedy()
    {
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var older = OpenCodePlugin.Body.Replace("5000", "4000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(older, StampStyle.SlashComment));

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("stale").And.Contain("dtk init opencode");
        await _runner.DidNotReceiveWithAnyArgs().RunCapturedWithInputAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RunAsync_LocallyEditedPluginStillRunningDtk_PassesAsModifiedAndIsProbed()
    {
        await OpenCode.IntegrateAsync(_tempDir, force: false, default);
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("5000", "9000", StringComparison.Ordinal));

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("opencode hook (project)", "opencode hook probe (project)");
        checks[0].Message.Should().Be("registered (modified locally)");
    }

    [Fact]
    public async Task RunAsync_ForeignFileAtThePluginPath_IsNotReported()
    {
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "export const Other = async () => ({});");

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Should().ContainSingle().Which.Name.Should().Be("hook integration");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthCheckerTests"`
Expected: the four new tests FAIL (the plugin is read as JSON and reported unreadable).

- [ ] **Step 3: Implement**

In `HookHealthChecker`:
- Add to `RegistrationKind`:

```csharp
        /// <summary>A generated plugin dtk wrote, from an older template.</summary>
        Stale = 4,

        /// <summary>A generated plugin edited since dtk wrote it, which still runs <c>dtk hook &lt;provider&gt;</c>.</summary>
        Modified = 5
```

- At the top of `ReadRegistrationAsync`, after the `File.Exists` check and the guarded read, branch before the JSON
  parse:

```csharp
        if (installation.PluginArtifact is { } artifact)
        {
            return ClassifyPlugin(artifact, content, path, installation);
        }
```

- Add:

```csharp
    /// <summary>Classifies a generated plugin by its provenance stamp; it is JavaScript, so there is no JSON to search.</summary>
    /// <param name="artifact">The plugin as the running dtk would generate it.</param>
    /// <param name="content">The file on disk.</param>
    /// <param name="path">Its path, for messages.</param>
    /// <param name="installation">The installation, for the remedy command.</param>
    private static Registration ClassifyPlugin(GeneratedArtifact artifact, string content, string path, HookInstallation installation)
    {
        var normalized = content.ReplaceLineEndings("\n");

        if (normalized == ArtifactStamping.Apply(artifact.Body, artifact.Style))
        {
            return new Registration(RegistrationKind.Current, string.Empty);
        }

        if (ArtifactStamping.IsAuthentic(normalized))
        {
            return new Registration(RegistrationKind.Stale,
                $"stale — {path} was written by an older dtk. Run '{RemedyCommand(installation)}'");
        }

        return normalized.Contains(OpenCodePlugin.InvocationSignature, StringComparison.Ordinal)
            ? new Registration(RegistrationKind.Modified, string.Empty)
            : new Registration(RegistrationKind.Absent, $"not registered — {path} does not run '{installation.Command}'");
    }
```

- In `CheckAsync`, add arms before the `_` arm:

```csharp
            RegistrationKind.Stale => [new DiagnosticCheck(name, false, registration.Problem + ".")],
            RegistrationKind.Modified =>
            [
                new DiagnosticCheck(name, true, "registered (modified locally)"),
                await ProbeAsync(installation, cancellationToken).ConfigureAwait(false),
                .. ApprovalChecks(integrator, installation, projectDirectory)
            ],
```

- The class summary gains: "A generated plugin registration is classified by its provenance stamp instead."

`OpenCodePlugin.InvocationSignature` names the one provider that has a plugin today; when a second plugin provider
arrives, move the signature onto `HookInstallation`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthCheckerTests|FullyQualifiedName~HookDescriptionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs
git commit -m "feat: doctor checks the OpenCode plugin by its provenance stamp"
```

---

### Task 5: Run the generated plugin under Node

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/NodeFactAttribute.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/OpenCodePluginTests.cs`
- Modify: `.github/workflows/ci.yml` (Test step env), `.github/workflows/fallback-package.yml` (integration suite step env)

**Interfaces:**
- Consumes: `OpenCodePlugin.Artifact` (Task 3), `ExecutableSearch.FindOnProcessPath` (existing, internal),
  `DtkLauncher.TestBinaryVariable` (existing).
- Produces: `NodeFactAttribute(bool unixOnly = false)` — skips when `node` is not on `PATH` unless
  `DTK_NODE_REQUIRED=1`, and on Windows when `unixOnly`.

- [ ] **Step 1: Write the attribute**

```csharp
using DotnetTokenKiller.Application.Helpers;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>A fact that needs Node.js on <c>PATH</c>; CI sets <c>DTK_NODE_REQUIRED=1</c> so it fails instead of skipping.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NodeFactAttribute : FactAttribute
{
    /// <summary>The variable that turns a missing Node.js into a failure.</summary>
    internal const string RequiredVariable = "DTK_NODE_REQUIRED";

    /// <summary>Creates the attribute.</summary>
    /// <param name="unixOnly">Whether the test also needs a POSIX shell script on <c>PATH</c>.</param>
    public NodeFactAttribute(bool unixOnly = false)
    {
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;

        if (unixOnly && OperatingSystem.IsWindows())
        {
            Skip = "Unix only: uses a shell-script dtk.";
        }
        else if (ExecutableSearch.FindOnProcessPath("node") is null && Environment.GetEnvironmentVariable(RequiredVariable) != "1")
        {
            Skip = $"Node.js is not on PATH. Set {RequiredVariable}=1 to fail instead.";
        }
    }
}
```

- [ ] **Step 2: Write the tests**

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the plugin <c>dtk init opencode</c> generates under Node, as OpenCode's plugin host would call it: the exported
/// factory, then <c>tool.execute.before</c> with an args object, against a real or fake <c>dtk</c> on <c>PATH</c>.
/// </summary>
public sealed class OpenCodePluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-ocplugin-{Guid.NewGuid()}");

    public OpenCodePluginTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "dtk.js"), ArtifactStamping.Apply(OpenCodePlugin.Body, StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, "package.json"), """{"type":"module"}""");
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import { DtkPlugin } from "./dtk.js";
            const [tool, command] = process.argv.slice(2);
            const hooks = await DtkPlugin({});
            const output = { args: { command, workdir: "w" } };
            const args = output.args;
            await hooks["tool.execute.before"]({ tool, sessionID: "s", callID: "c" }, output);
            process.stdout.write(JSON.stringify({ command: output.args.command, sameObject: output.args === args, workdir: output.args.workdir }));
            """);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>The directory holding the dtk under test: the installed binary's, or the JIT build's apphost beside the tests.</summary>
    private static string RealDtkDirectory
    {
        get
        {
            var binary = Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable);
            return string.IsNullOrWhiteSpace(binary) ? AppContext.BaseDirectory : Path.GetDirectoryName(binary.Trim())!;
        }
    }

    [NodeFact]
    public async Task Bash_DotnetCommand_IsRewrittenInPlaceByTheRealDtkAsync()
    {
        var result = await RunAsync("bash", "dotnet build # répertoire", RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire");
        result["sameObject"]!.GetValue<bool>().Should().BeTrue("OpenCode passes the same args object on to the tool");
        result["workdir"]!.GetValue<string>().Should().Be("w");
    }

    [NodeFact]
    public async Task DtkMissingFromPath_LeavesTheCommandAndDoesNotThrowAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await RunAsync("bash", "dotnet build", empty);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
    }

    [NodeFact(unixOnly: true)]
    public async Task OtherToolOrNoDotnet_NeverStartsDtkAsync()
    {
        var bin = FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await RunAsync("read", "dotnet build", bin))["command"]!.GetValue<string>().Should().Be("dotnet build");
        (await RunAsync("bash", "ls -la", bin))["command"]!.GetValue<string>().Should().Be("ls -la");
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeFact(unixOnly: true)]
    public async Task MalformedReply_LeavesTheCommandAsync()
    {
        var bin = FakeDtk("cat >/dev/null; echo 'not json'");

        (await RunAsync("bash", "dotnet test", bin))["command"]!.GetValue<string>().Should().Be("dotnet test");
    }

    [NodeFact(unixOnly: true)]
    public async Task HangingDtk_TimesOutAndLeavesTheCommandAsync()
    {
        var bin = FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await RunAsync("bash", "dotnet test", bin);

        result["command"]!.GetValue<string>().Should().Be("dotnet test");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the plugin gives dtk 5 seconds");
    }

    private string FakeDtk(string script)
    {
        var bin = Directory.CreateDirectory(Path.Combine(_dir, $"bin-{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(bin, "dtk");
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return bin;
    }

    private async Task<JsonNode> RunAsync(string tool, string command, string pathDirectory)
    {
        var node = ExecutableSearch.FindOnProcessPath("node")
                   ?? throw new InvalidOperationException("node is not on PATH");
        var psi = new ProcessStartInfo(node)
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            // Only the dtk under test is reachable, plus node's own directory and the POSIX tools a fake dtk script uses.
            // The apphost finds the .NET runtime through DOTNET_ROOT or the install location, never PATH, and the child
            // inherits this process's environment apart from PATH.
            Environment = { ["PATH"] = string.Join(Path.PathSeparator, pathDirectory, Path.GetDirectoryName(node), "/bin", "/usr/bin") }
        };
        psi.ArgumentList.Add("driver.mjs");
        psi.ArgumentList.Add(tool);
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0, "the plugin must never throw; stderr: {0}", stderr);
        return JsonNode.Parse(stdout)!;
    }
}
```

The JIT apphost `dtk` is copied into the test output directory beside `dtk.dll`, so `AppContext.BaseDirectory` holds
a runnable `dtk` (`dtk.exe` on Windows) — confirmed in `tests/DotnetTokenKiller.Cli.IntegrationTests/bin/*/net10.0/`.

- [ ] **Step 3: Run the tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~OpenCodePluginTests"`
Expected: 5 PASS on Linux with Node installed (`node --version` works here). If
`Bash_DotnetCommand_IsRewrittenInPlaceByTheRealDtkAsync` fails because the apphost cannot find the runtime, add
`DOTNET_ROOT` from the test process's environment to `psi.Environment`.

- [ ] **Step 4: Require Node in CI**

`.github/workflows/ci.yml`, the `Test` step:

```yaml
      - name: Test
        env:
          DTK_NODE_REQUIRED: '1'
        run: dotnet test --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage" --results-directory ./coverage
```

`.github/workflows/fallback-package.yml`, the "Run the integration suite against the installed binary" step: add
`env: { DTK_NODE_REQUIRED: '1' }` in block form, so on Windows the plugin starts the installed `dtk.exe`.

- [ ] **Step 5: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests .github/workflows/ci.yml .github/workflows/fallback-package.yml
git commit -m "test: run the generated OpenCode plugin under Node against real and fake dtk"
```

---

### Task 6: CLI surface and docs

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs`, `Commands/Settings/InitCommandSettings.cs`,
  `CliConfigurator.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`
- Modify: `docs/articles/ai-agent-setup.md`, `docs/articles/usage.md`, `docs/index.md`, `README.md`,
  `src/DotnetTokenKiller.Cli/README.md`, `CLAUDE.md`

- [ ] **Step 1: Extend the completion test**

Rename `ExecuteAsync_EveryShell_CompletesTheCodexProvider` to `ExecuteAsync_EveryShell_CompletesTheNewHarnessProviders`
and assert `writer.ToString().Should().Contain("codex").And.Contain("opencode");`.
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests"`
Expected: FAIL.

- [ ] **Step 2: Implement the CLI strings**

- bash `init_providers`: `claude copilot copilot-cli gemini codex opencode cursor windsurf aider jetbrains`
- zsh: `'opencode:Install dtk plugin and instructions for OpenCode'` after codex
- fish: `complete -c dtk -f -n '__fish_seen_subcommand_from init integrate' -a opencode  -d 'Install dtk plugin and instructions for OpenCode'`
- PowerShell `$providers`: add `'opencode'` after `'codex'`
- `InitCommandSettings`: add `opencode` after `codex` in both descriptions
- `CliConfigurator`: `.WithExample(InitCommand, "opencode")` after the codex examples

Run the completion and `CliConfiguratorTests`; accept the InitHelp `.received.txt` after checking its diff shows only
the new example and descriptions. Expected: PASS.

- [ ] **Step 3: Docs**

`docs/articles/ai-agent-setup.md` — *Installing globally*: add
`dtk init opencode    --global   # ~/.config/opencode, ~/.agents/skills` and name **opencode**. Insert after the
Codex CLI section:

````markdown
## OpenCode

OpenCode runs plugins rather than hook commands, so dtk installs a small plugin that rewrites
`dotnet build|test|restore|clean|format|list package` commands to use `dtk`. It targets OpenCode 1.x.

### Installation

From your project root, run:

```sh
dtk init opencode
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- `.opencode/plugins/dtk.js` — the plugin

`dtk init opencode --global` writes `~/.config/opencode/AGENTS.md` and `~/.config/opencode/plugins/dtk.js` (under
`$XDG_CONFIG_HOME` when it is set) and `~/.agents/skills/dotnet-token-killer/SKILL.md`. OpenCode also reads
`.claude/skills/`, so a project set up for Claude Code too logs a harmless duplicate-skill warning.

Re-running the command refreshes `dtk.js` if dtk wrote it and leaves an edited copy alone unless you pass `--force`.

### How It Works

Before OpenCode runs a `bash` command that mentions `dotnet`, the plugin passes it to `dtk hook opencode` and runs
the `dtk dotnet …` command it gets back. Other commands never start `dtk`. If `dtk` is missing, fails or takes longer
than five seconds, the original command runs unchanged. OpenCode checks its permission rules against the rewritten
command.

````

`docs/articles/usage.md`: add `dtk init opencode    # OpenCode plugin + AGENTS.md section + skill` and name
**opencode** in the `--global` sentence. `docs/index.md`: add `<span class="dtk-agent">OpenCode</span>` after Codex CLI.

`README.md` and `src/DotnetTokenKiller.Cli/README.md`: `10 AI agent integrations — …, Codex CLI, OpenCode, Cursor, …`;
`README.md` global block `dtk init opencode    --global   # ~/.config/opencode, ~/.agents/skills`, supported-list and
table row `| **OpenCode**           | `dtk init opencode`    | .opencode/plugins/dtk.js running dtk hook opencode, AGENTS.md section, skill |`;
packaged README names `opencode` in the `--global` list.

`CLAUDE.md`, after the codex paragraph:

```markdown
`dtk init opencode` writes the same `AGENTS.md` section and skill plus a generated, stamped `.opencode/plugins/dtk.js`
(`--global`: `$XDG_CONFIG_HOME/opencode` or `~/.config/opencode`). OpenCode has no hook commands: the plugin's
`tool.execute.before` spawns `dtk hook opencode` (no shell) for `bash` commands containing `dotnet` and mutates
`output.args.command` in place. `OpenCodePluginTests` run it under Node; CI sets `DTK_NODE_REQUIRED=1`.
```

- [ ] **Step 4: Run docs and CLI tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests|FullyQualifiedName~CompletionCommandTests|FullyQualifiedName~CliConfiguratorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli tests/DotnetTokenKiller.Cli.IntegrationTests docs/articles docs/index.md README.md src/DotnetTokenKiller.Cli/README.md CLAUDE.md
git commit -m "docs: document dtk init opencode and complete the provider"
```

---

### Task 7: Verify in a real OpenCode and measure the plugin's cost

Scratch work under `/tmp/opencode-gate/`; only the measurements are committed (to `CLAUDE.md`).

- [ ] **Step 1: Install OpenCode and a fake model**

```bash
mkdir -p /tmp/opencode-gate/{home,proj} && cd /tmp/opencode-gate
npm install --prefix /tmp/opencode-gate opencode-ai@1
/tmp/opencode-gate/node_modules/.bin/opencode --version
```

Record the version (1.x). Write `/tmp/opencode-gate/fake-chat.mjs`, an OpenAI-compatible chat-completions stream that
asks for one `bash` call when the last message is the user's and answers `done` otherwise (the shape the research run
at `/tmp/oc-research/e2e/server.py` used):

```js
import { createServer } from "node:http";

const chunk = (delta, finish = null) =>
  `data: ${JSON.stringify({ id: "c1", object: "chat.completion.chunk", created: 0, model: "m",
    choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`;

createServer((req, res) => {
  let body = "";
  req.on("data", (c) => (body += c));
  req.on("end", () => {
    const messages = JSON.parse(body || "{}").messages ?? [];
    const last = messages.at(-1) ?? {};
    res.writeHead(200, { "content-type": "text/event-stream" });
    if (last.role === "user") {
      const command = process.env.GATE_COMMAND ?? "dotnet --list-sdks";
      res.write(chunk({ role: "assistant", tool_calls: [{ index: 0, id: `call_${Date.now()}`, type: "function",
        function: { name: "bash", arguments: JSON.stringify({ command, description: "gate" }) } }] }));
      res.write(chunk({}, "tool_calls"));
    } else {
      res.write(chunk({ role: "assistant", content: "done" }));
      res.write(chunk({}, "stop"));
    }
    res.end("data: [DONE]\n\n");
  });
}).listen(18765, "127.0.0.1");
```

`/tmp/opencode-gate/proj/opencode.json`:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "provider": {
    "fake": {
      "npm": "@ai-sdk/openai-compatible",
      "name": "Fake",
      "options": { "baseURL": "http://127.0.0.1:18765/v1", "apiKey": "x" },
      "models": { "m": { "name": "m", "tool_call": true } }
    }
  },
  "model": "fake/m",
  "small_model": "fake/m",
  "permission": { "bash": "allow" },
  "autoupdate": false,
  "share": "disabled"
}
```

- [ ] **Step 2: Confirm the rewrite in OpenCode**

Build this branch (`dtk dotnet build src/DotnetTokenKiller.Cli -c Release`), put
`src/DotnetTokenKiller.Cli/bin/Release/net10.0` first on `PATH`, run
`dotnet src/DotnetTokenKiller.Cli/bin/Release/net10.0/dtk.dll init opencode --dir /tmp/opencode-gate/proj`, start
`node fake-chat.mjs &`, and with `GATE_COMMAND="dotnet build --help"`:

```bash
cd /tmp/opencode-gate/proj
HOME=/tmp/opencode-gate/home XDG_CONFIG_HOME=/tmp/opencode-gate/home/.config \
  /tmp/opencode-gate/node_modules/.bin/opencode run "go" --print-logs 2>&1 | tee run.out
```

Expected: the tool result in `run.out` shows the command as `dtk dotnet build --help`. Repeat with `dtk` removed from
`PATH`: the run completes and the command stays `dotnet build --help`. If the rewrite does not appear, stop and
investigate (plugin not loaded, `.js` treated as CommonJS, tool name changed) before continuing.

- [ ] **Step 3: Measure**

Time 21 runs each of `opencode run` with `GATE_COMMAND="echo hi"` (no `dotnet`) and `GATE_COMMAND="dotnet --version"`,
first with `dtk.js` removed and then installed, reading the plugin's own cost from OpenCode's `--print-logs` tool
timings if present, else wall clock. Record medians: added time for a non-dotnet command (expected: within noise) and
for a dotnet command (expected: about one `dtk` start, ~15 ms under AOT, more under the JIT build — say which build).

- [ ] **Step 4: Record in `CLAUDE.md`**

Under *Benchmarks*, after the `dtk hook` paragraph, add one paragraph: date, OpenCode version, dtk build (JIT or AOT),
the two medians, and that the plugin starts no process for commands without `dotnet`.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: record the OpenCode plugin's measured cost"
```

---

### Task 8: Full verification and pull request

- [ ] **Step 1: Format and build**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore` then `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: no format changes; 0 warnings, 0 errors.

- [ ] **Step 2: Test**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Hook|FullyQualifiedName~InitCommand|FullyQualifiedName~Doctor|FullyQualifiedName~Completion|FullyQualifiedName~CliConfigurator|FullyQualifiedName~DocsBinding|FullyQualifiedName~OpenCodePlugin"`
Expected: PASS.

- [ ] **Step 3: Push and open the PR — confirm with the user first**

```bash
git push -u origin feat/opencode-integration
gh pr create --base develop --title "feat: dtk init opencode and dtk hook opencode" --body-file <draft>
```

The body links the spec and this plan, reports Task 7's OpenCode version, rewrite confirmation and measurements, and
ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Expected: CI green on Linux and Windows,
with `OpenCodePluginTests` running (not skipped). Wait for the merge before starting
`docs/superpowers/plans/2026-09-15-antigravity-integration.md`.

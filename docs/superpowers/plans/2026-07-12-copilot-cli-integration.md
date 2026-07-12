# GitHub Copilot CLI Integration (`copilot-cli`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new dtk integration provider, `copilot-cli`, that installs a `preToolUse` rewrite hook (+ instructions doc) so GitHub Copilot CLI transparently runs `dtk dotnet …` instead of `dotnet …`.

**Architecture:** A new `CopilotCliIntegrator : IProviderIntegrator, IGlobalIntegrator` in the Application layer, following the `GeminiCliIntegrator` pattern. It reuses the existing `SharedCore` Python rewriter (via a new `HookScriptTemplates.CopilotCliHook` template with a Copilot-shaped `main()`), writes a **dedicated** `dtk-dotnet.json` hook file with `IntegratorHelpers.WriteFileAsync` (no settings merge — dtk owns the file), and reuses `WriteSectionBasedFileAsync` for the `.github/copilot-instructions.md` doc. Global install targets `~/.copilot/hooks/`.

**Tech Stack:** .NET 10 / C#, Spectre.Console.Cli, System.Text.Json, xunit + FluentAssertions. Hook script is Python 3.

## Global Constraints

- Target framework: `net10.0`. `TreatWarningsAsErrors` is enabled — all analyzer warnings are build errors.
- File-scoped namespaces; `var` preferred; private fields `_camelCase`; async methods end in `Async`; interfaces `IPascalCase`; type params `TPascalCase`.
- LF line endings, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Central package management: no versions in `.csproj`.
- Build/test/format via dtk: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`, `dtk dotnet format DotnetTokenKiller.slnx --no-restore`.
- The existing `copilot` provider (instruction-only, GitHub Copilot IDE) is **unchanged**. The new provider name is exactly `copilot-cli`.
- Copilot CLI `preToolUse` contract: input stdin JSON `{ toolName, toolArgs, … }` where `toolArgs` may be a JSON **string** (double-encoded) `{"command":"…"}`; to rewrite, print `{"permissionDecision":"allow","modifiedArgs":{…}}` (object, preserving other arg fields); **fail-closed** (non-zero exit denies the tool), so the script must exit 0 and print nothing on the no-rewrite path.

## File Structure

- Create: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs` — the provider.
- Modify: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs` — add `CopilotCliHook`.
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs` — add `CopilotHooksDir`.
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — register the provider.
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs` — provider list + `--global` text.
- Modify: `src/DotnetTokenKiller.Cli/Program.cs` — `.WithExample(...)` lines.
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs`.
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs` — assert `CopilotHooksDir`.
- Modify: `CLAUDE.md` (and `README.md` if it lists providers) — document `copilot-cli`.

---

## Task 1: `HomePaths.CopilotHooksDir`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`

**Interfaces:**
- Consumes: `HomePaths.Home` (existing).
- Produces: `internal string CopilotHooksDir` → `<Home>/.copilot/hooks`.

- [ ] **Step 1: Add the failing assertion to the existing HomePaths test**

In `HomePathsTests.cs`, inside `ResolvesProviderDirectoriesUnderTheGivenHome`, add after the `AiderInstructionsPath` assertion:

```csharp
        sut.CopilotHooksDir.Should().Be(Path.Combine(home, ".copilot", "hooks"));
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HomePathsTests.ResolvesProviderDirectoriesUnderTheGivenHome"`
Expected: FAIL to compile — `CopilotHooksDir` does not exist.

- [ ] **Step 3: Add the property**

In `HomePaths.cs`, after the `AiderInstructionsPath` property:

```csharp
    /// <summary>Gets the user-level GitHub Copilot CLI hooks directory (<c>~/.copilot/hooks</c>).</summary>
    internal string CopilotHooksDir => Path.Combine(Home, ".copilot", "hooks");
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HomePathsTests.ResolvesProviderDirectoriesUnderTheGivenHome"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HomePaths.cs tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs
git commit -m "feat: add HomePaths.CopilotHooksDir for Copilot CLI global install"
```

---

## Task 2: `HookScriptTemplates.CopilotCliHook`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs` (create)

**Interfaces:**
- Consumes: existing `HookScriptTemplates.SharedCore` (private const, `def rewrite`).
- Produces: `internal static string HookScriptTemplates.CopilotCliHook { get; }` — full Python script text.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class HookScriptTemplatesTests
{
    [Fact]
    public void CopilotCliHook_ReusesSharedRewriteCore()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().Contain("def rewrite");
        script.Should().Contain("def main");
    }

    [Fact]
    public void CopilotCliHook_EmitsCopilotDecisionSchema()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().Contain("\"toolName\"");
        script.Should().Contain("\"toolArgs\"");
        script.Should().Contain("permissionDecision");
        script.Should().Contain("modifiedArgs");
    }

    [Fact]
    public void CopilotCliHook_DoesNotUseClaudeOrGeminiSchema()
    {
        var script = HookScriptTemplates.CopilotCliHook;

        script.Should().NotContain("updatedInput");
        script.Should().NotContain("hookSpecificOutput");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HookScriptTemplatesTests"`
Expected: FAIL to compile — `CopilotCliHook` does not exist.

- [ ] **Step 3: Add the header, main, and property**

In `HookScriptTemplates.cs`, after the `GeminiMain` const (before the `ClaudeHook` property), add:

```csharp
    private const string CopilotCliHeader = """"
        #!/usr/bin/env python3
        """GitHub Copilot CLI preToolUse hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

        Reads the preToolUse event from stdin (JSON with "toolName" and "toolArgs";
        for the CLI's file-based hooks "toolArgs" is a JSON string holding {"command": ...}).
        When the tool is `bash` and a qualifying dotnet command is found, prints a
        preToolUse decision that allows the call with `modifiedArgs` carrying the
        rewritten command. Prints nothing when no rewrite is needed (allow, no change).
        Always exits 0: Copilot CLI treats a non-zero exit as a denial.
        """


        """";

    private const string CopilotCliMain = """
        def main() -> None:
            try:
                payload = json.load(sys.stdin)
            except (json.JSONDecodeError, EOFError):
                return

            if payload.get("toolName") != "bash":
                return

            tool_args = payload.get("toolArgs", {})
            if isinstance(tool_args, str):
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
                modified = dict(tool_args)
                modified["command"] = rewritten
                print(json.dumps({
                    "permissionDecision": "allow",
                    "modifiedArgs": modified,
                }))
            # No output on the no-change path: Copilot CLI proceeds normally.


        if __name__ == "__main__":
            main()

        """;
```

Then, alongside the existing `ClaudeHook`/`GeminiHook` properties at the bottom, add:

```csharp
    /// <summary>GitHub Copilot CLI preToolUse hook, emitting the CLI's <c>permissionDecision</c>/<c>modifiedArgs</c> schema.</summary>
    internal static string CopilotCliHook { get; } = CopilotCliHeader + SharedCore + CopilotCliMain;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HookScriptTemplatesTests"`
Expected: PASS (all 3).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs
git commit -m "feat: add Copilot CLI preToolUse hook script template"
```

---

## Task 3: `CopilotCliIntegrator` — repository install

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs` (create)

**Interfaces:**
- Consumes: `HomePaths` (ctor), `HookScriptTemplates.CopilotCliHook`, `IntegratorHelpers.WriteFileAsync`, `IntegratorHelpers.WriteSectionBasedFileAsync`, `IntegrationContext`, `IntegrationInstructions.Markdown`.
- Produces: `internal sealed class CopilotCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator` with `ProviderName => "copilot-cli"` and `IntegrateAsync(directory, force, ct)`. Repo artifacts: `.github/hooks/dotnet-to-dtk.py`, `.github/hooks/dtk-dotnet.json`, `.github/copilot-instructions.md`. (`IntegrateGlobalAsync` is stubbed in this task and implemented in Task 4.)

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs`:

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CopilotCliIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-copilotcli-test-{Guid.NewGuid()}");
    private readonly string _isolatedHome;
    private readonly CopilotCliIntegrator _sut;

    public CopilotCliIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        _sut = new CopilotCliIntegrator(new HomePaths(_isolatedHome));
    }

    private string HookScriptPath => Path.Combine(_tempDir, ".github", "hooks", "dotnet-to-dtk.py");
    private string HookJsonPath => Path.Combine(_tempDir, ".github", "hooks", "dtk-dotnet.json");
    private string InstructionsPath => Path.Combine(_tempDir, ".github", "copilot-instructions.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void ProviderName_ReturnsCopilotCli()
    {
        _sut.ProviderName.Should().Be("copilot-cli");
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesAllThreeFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(HookScriptPath).Should().BeTrue();
        File.Exists(HookJsonPath).Should().BeTrue();
        File.Exists(InstructionsPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_HookJson_HasCopilotPreToolUseShape()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(HookJsonPath)) as JsonObject;

        root.Should().NotBeNull();
        root!["version"]!.GetValue<int>().Should().Be(1);
        var entry = root["hooks"]!["preToolUse"]!.AsArray()[0]!;
        entry["type"]!.GetValue<string>().Should().Be("command");
        entry["matcher"]!.GetValue<string>().Should().Be("bash");
        entry["bash"]!.GetValue<string>().Should().Contain("dotnet-to-dtk.py");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_ContainsCopilotDecisionSchema()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(HookScriptPath);

        content.Should().Contain("def rewrite");
        content.Should().Contain("permissionDecision");
        content.Should().Contain("modifiedArgs");
        content.Should().NotContain("updatedInput");
    }

    [Fact]
    public async Task IntegrateAsync_Instructions_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsAllFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_OverwritesFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().HaveCount(3);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_ExistingInstructionsWithoutMarker_NoForce_SkipsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        const string original = "# Copilot rules\n\nBe nice.";
        await File.WriteAllTextAsync(InstructionsPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(InstructionsPath);
        (await File.ReadAllTextAsync(InstructionsPath)).Should().Be(original);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliIntegratorTests"`
Expected: FAIL to compile — `CopilotCliIntegrator` does not exist.

- [ ] **Step 3: Create the integrator**

Create `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot CLI.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates a hook-based integration modeled on <see cref="GeminiCliIntegrator"/>:
/// <list type="bullet">
///   <item><description><c>.github/hooks/dotnet-to-dtk.py</c> — the preToolUse rewrite script.</description></item>
///   <item><description><c>.github/hooks/dtk-dotnet.json</c> — a dedicated, dtk-owned hook registration (no merge).</description></item>
///   <item><description><c>.github/copilot-instructions.md</c> — section-merged dtk instructions.</description></item>
/// </list>
/// Distinct from <see cref="GitHubCopilotIntegrator"/> (the instruction-only IDE <c>copilot</c> provider).
/// Declared <see langword="internal"/> because its primary constructor takes the internal
/// <see cref="HomePaths"/>; reached polymorphically via <see cref="IProviderIntegrator"/> through DI.
/// </remarks>
internal sealed class CopilotCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";
    private const string HookScriptName = "dotnet-to-dtk.py";
    private const string HookJsonName = "dtk-dotnet.json";

    private const string CopilotSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}

        A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` rewrites `dotnet build|test|restore|clean|format`
        to `dtk dotnet ...` automatically. The hook shells out to `python3`; on Windows (where the launcher is
        usually `python`, not `python3`), edit the `bash` command in that file if it doesn't fire.
        {SectionEndMarker}
        """;

    /// <inheritdoc/>
    public string ProviderName => "copilot-cli";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);
        var hooksDir = Path.Combine(directory, ".github", "hooks");

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, ".github", "copilot-instructions.md"),
            SectionMarker, SectionEndMarker, CopilotSection,
            context, cancellationToken).ConfigureAwait(false);

        await WriteHookArtifactsAsync(hooksDir, HookCwdRelative, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => throw new NotImplementedException("Implemented in Task 4.");

    /// <summary>Relative <c>cwd</c> for the repository hook (resolved by Copilot CLI against the repo root).</summary>
    private const string HookCwdRelative = ".github/hooks";

    private static async Task WriteHookArtifactsAsync(
        string hooksDir,
        string cwd,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(hooksDir, HookScriptName),
            HookScriptTemplates.CopilotCliHook,
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(hooksDir, HookJsonName),
            BuildHookJson(cwd),
            context, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildHookJson(string cwd)
    {
        var root = new JsonObject
        {
            ["version"] = 1,
            ["hooks"] = new JsonObject
            {
                ["preToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = $"python3 {HookScriptName}",
                        ["cwd"] = cwd,
                        ["timeoutSec"] = 10
                    }
                }
            }
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliIntegratorTests"`
Expected: PASS for every test except any global test (there are none yet in this task). All listed tests PASS.

- [ ] **Step 5: Build to confirm no analyzer/warning errors**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs
git commit -m "feat: add copilot-cli integrator (repository install)"
```

---

## Task 4: `CopilotCliIntegrator` — global install

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs`

**Interfaces:**
- Consumes: `HomePaths.CopilotHooksDir` (Task 1).
- Produces: implemented `IntegrateGlobalAsync(force, ct)` — writes `~/.copilot/hooks/dotnet-to-dtk.py` and `~/.copilot/hooks/dtk-dotnet.json` (hook only, no instructions doc), plus one advisory `Note`.

- [ ] **Step 1: Write the failing global tests**

Append to `CopilotCliIntegratorTests.cs` (inside the class):

```csharp
    private string GlobalHookScriptPath => Path.Combine(_isolatedHome, ".copilot", "hooks", "dotnet-to-dtk.py");
    private string GlobalHookJsonPath => Path.Combine(_isolatedHome, ".copilot", "hooks", "dtk-dotnet.json");

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesHookArtifactsOnly()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        File.Exists(GlobalHookScriptPath).Should().BeTrue();
        File.Exists(GlobalHookJsonPath).Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);
        result.Notes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HookJson_UsesAbsoluteHooksDirCwd()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(GlobalHookJsonPath)) as JsonObject;
        var cwd = root!["hooks"]!["preToolUse"]!.AsArray()[0]!["cwd"]!.GetValue<string>();

        cwd.Should().Be(Path.Combine(_isolatedHome, ".copilot", "hooks"));
    }

    [Fact]
    public async Task IntegrateGlobalAsync_DoesNotWriteRepoInstructions()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        File.Exists(InstructionsPath).Should().BeFalse();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliIntegratorTests.IntegrateGlobalAsync"`
Expected: FAIL — `IntegrateGlobalAsync` throws `NotImplementedException`.

- [ ] **Step 3: Implement `IntegrateGlobalAsync`**

In `CopilotCliIntegrator.cs`, replace the stubbed `IntegrateGlobalAsync` with:

```csharp
    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        // Global (~/.copilot/hooks) is not a git-tracked location, so an absolute cwd is portable and
        // unambiguous here (unlike the repo variant, which uses a relative cwd for a committed file).
        await WriteHookArtifactsAsync(home.CopilotHooksDir, home.CopilotHooksDir, context, cancellationToken)
            .ConfigureAwait(false);

        context.Notes.Add(
            "Copilot CLI instructions are repository-scoped; the global install adds the rewrite hook only. "
            + "Run 'dtk integrate copilot-cli' inside a project to also write .github/copilot-instructions.md.");

        return context.ToResult();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~CopilotCliIntegratorTests"`
Expected: PASS (all repo + global tests).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs tests/DotnetTokenKiller.Application.Tests/Integration/CopilotCliIntegratorTests.cs
git commit -m "feat: support 'dtk integrate copilot-cli --global'"
```

---

## Task 5: Register provider + CLI wiring

**Files:**
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs`
- Modify: `src/DotnetTokenKiller.Cli/Program.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs`

**Interfaces:**
- Consumes: `CopilotCliIntegrator`, `IntegrateUseCase.RunAsync`/`RunGlobalAsync`.
- Produces: `copilot-cli` resolvable as a provider through `IntegrateUseCase`; supports `--global`.

- [ ] **Step 1: Write the failing use-case tests**

Open `tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs`. Add two tests (adapt to the file's existing construction of `IntegrateUseCase` — it builds the use case from a list of `IProviderIntegrator`s; include a `new CopilotCliIntegrator(new HomePaths(<isolated home>))` in that list, matching how the existing tests instantiate integrators):

```csharp
    [Fact]
    public async Task RunAsync_CopilotCli_WritesRepositoryArtifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dtk-usecase-copilotcli-{Guid.NewGuid()}");
        try
        {
            var sut = new IntegrateUseCase(new IProviderIntegrator[]
            {
                new CopilotCliIntegrator(new HomePaths(Path.Combine(tempDir, "home"))),
            });

            var result = await sut.RunAsync("copilot-cli", tempDir, false, CancellationToken.None);

            result.CreatedFiles.Should().NotBeEmpty();
            File.Exists(Path.Combine(tempDir, ".github", "hooks", "dtk-dotnet.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RunGlobalAsync_CopilotCli_DoesNotThrow()
    {
        var home = Path.Combine(Path.GetTempPath(), $"dtk-usecase-copilotcli-global-{Guid.NewGuid()}");
        try
        {
            var sut = new IntegrateUseCase(new IProviderIntegrator[]
            {
                new CopilotCliIntegrator(new HomePaths(home)),
            });

            var act = () => sut.RunGlobalAsync("copilot-cli", false, CancellationToken.None);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }
```

If `IntegrateUseCaseTests.cs` lacks the `DotnetTokenKiller.Application.Integration` using or FluentAssertions, add them at the top:

```csharp
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~IntegrateUseCaseTests"`
Expected: The two new tests FAIL (`copilot-cli` unknown → `RunAsync`/`RunGlobalAsync` throws the "unknown provider" exception).

- [ ] **Step 3: Register the provider in DI**

In `src/DotnetTokenKiller.Application/DependencyInjection.cs`, after the `GitHubCopilotIntegrator` registration:

```csharp
        services.AddTransient<IProviderIntegrator, CopilotCliIntegrator>();
```

- [ ] **Step 4: Update CLI provider list and examples**

In `IntegrateCommandSettings.cs`, update the `[Description(...)]` on `Provider`:

```csharp
    [Description(
        "AI assistant provider to integrate (claude, copilot, copilot-cli, gemini, cursor, windsurf, aider, jetbrains)")]
```

and the `--global` `[Description(...)]`:

```csharp
    [Description("Install into the user's home config (claude, copilot-cli, gemini, aider) instead of a project")]
```

In `Program.cs`, add after the `copilot` example in the `IntegrateCommand` block:

```csharp
            .WithExample(integrateCmd, "copilot-cli")
            .WithExample(integrateCmd, "copilot-cli", "--global")
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~IntegrateUseCaseTests"`
Expected: PASS.

- [ ] **Step 6: Build the full solution**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Application/DependencyInjection.cs src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs src/DotnetTokenKiller.Cli/Program.cs tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs
git commit -m "feat: register copilot-cli provider and CLI examples"
```

---

## Task 6: Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md` (only if it enumerates providers — check first)

**Interfaces:** none (docs only).

- [ ] **Step 1: Check whether README lists providers**

Run: `grep -n "jetbrains\|integrate" README.md`
Expected: shows whether a provider list/table exists to update.

- [ ] **Step 2: Update the docs**

In `CLAUDE.md` (and `README.md` if it lists providers), wherever the integration providers are enumerated, add `copilot-cli` and a one-line description, e.g.:

```
- `copilot-cli` — GitHub Copilot CLI: installs a preToolUse hook (.github/hooks/) that rewrites `dotnet …` to `dtk dotnet …`. Supports `--global` (~/.copilot/hooks/). Distinct from `copilot` (instruction-only, Copilot IDE).
```

If neither file enumerates providers, add a short note under the Commands section of `CLAUDE.md` documenting `dtk integrate copilot-cli`.

- [ ] **Step 3: Verify formatting**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
Expected: no formatting changes required (docs are Markdown, but run to confirm nothing else drifted).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: document copilot-cli integration provider"
```

---

## Task 7: Full verification + manual Copilot CLI smoke test

**Files:** none (verification only).

- [ ] **Step 1: Run the entire test suite**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: all tests pass.

- [ ] **Step 2: Verify formatting across the solution**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
Expected: exit 0, no changes.

- [ ] **Step 3: Exercise the command against a scratch directory**

```bash
tmp=$(mktemp -d)
dtk integrate copilot-cli --dir "$tmp"
cat "$tmp/.github/hooks/dtk-dotnet.json"
cat "$tmp/.github/hooks/dotnet-to-dtk.py" | head -5
```
Expected: three files reported created; the JSON shows `preToolUse`/`matcher: bash`; the script starts with the Copilot CLI docstring.

- [ ] **Step 4: Verify the rewrite locally by piping a sample payload through the script**

```bash
printf '%s' '{"toolName":"bash","toolArgs":"{\"command\":\"dotnet build\"}"}' \
  | python3 "$tmp/.github/hooks/dotnet-to-dtk.py"
```
Expected: `{"permissionDecision": "allow", "modifiedArgs": {"command": "dtk dotnet build"}}`.

Also verify a passthrough case emits nothing:

```bash
printf '%s' '{"toolName":"bash","toolArgs":"{\"command\":\"git status\"}"}' \
  | python3 "$tmp/.github/hooks/dotnet-to-dtk.py"; echo "exit=$?"
```
Expected: no stdout, `exit=0`.

- [ ] **Step 5: (Manual, if Copilot CLI is installed) confirm end-to-end + the `cwd` detail**

Install the hook globally and drive Copilot CLI to run a `dotnet build`:

```bash
dtk integrate copilot-cli --global
# then, in a Copilot CLI session, ask it to build the solution and confirm it runs `dtk dotnet build`.
```
If the hook does not fire because the script path fails to resolve, adjust `cwd`/`bash` in `BuildHookJson` (this is the one runtime-unverified detail from the spec — the relative `.github/hooks` cwd for repo installs and the absolute cwd for global). Re-run the affected tests, and commit any correction:

```bash
git commit -am "fix: correct Copilot CLI hook cwd/script resolution"
```

- [ ] **Step 6: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to open a PR against `develop`.

---

## Self-Review Notes

- **Spec coverage:** hook script (Task 2) ✓, dedicated hook JSON via `WriteFileAsync` (Task 3) ✓, instructions doc reuse (Task 3) ✓, repo + `--global` (Tasks 3–4) ✓, `HomePaths` addition (Task 1) ✓, DI + CLI wiring (Task 5) ✓, tests (Tasks 1–5) ✓, docs (Task 6) ✓, manual `cwd` verification (Task 7) ✓.
- **Deviation from spec (permitted):** `COPILOT_HOME` env handling dropped (YAGNI; sibling `ClaudeDir`/`GeminiDir` ignore per-tool env overrides, and reading env in `HomePaths` would break deterministic parallel tests). The spec's HomePaths section explicitly allowed this simplification.
- **Type consistency:** `CopilotCliHook` (Task 2) used identically in Task 3; `CopilotHooksDir` (Task 1) used in Task 4; `WriteHookArtifactsAsync(hooksDir, cwd, …)` defined in Task 3 and reused in Task 4; `ProviderName == "copilot-cli"` consistent across integrator, DI, settings, and use-case tests.
- **No placeholders:** every code and command step shows concrete content.

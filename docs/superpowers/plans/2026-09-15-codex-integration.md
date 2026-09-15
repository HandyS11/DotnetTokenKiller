# Codex CLI integration (with the shared groundwork) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `dtk init codex` and `dtk hook codex`, plus the pieces the OpenCode and Antigravity plans build on: a
shared `AGENTS.md` section and skill, a doctor warning state, and file-based rtk reconciliation.

**Architecture:** A new `CodexIntegrator` writes the shared instruction artifacts and merges a `PreToolUse` hook
into `.codex/hooks.json` through the existing `MergeJsonSettingsAsync` (Codex's schema is Claude's). `dtk hook codex`
is a fourth payload handler in `HookPayloads`. Codex runs a hook only after the user approves it, so `doctor` reads
`config.toml` and warns until it sees an approval, through a small `IHookApprovalInspector` seam.

**Tech Stack:** .NET 10, System.Text.Json `JsonNode`, Tomlyn (source-generated context), xunit 2, FluentAssertions,
NSubstitute, Verify (CLI help snapshots), POSIX sh (`eng/hooks/check-hook-shells.sh`).

**Spec:** `docs/superpowers/specs/2026-09-15-codex-opencode-antigravity-design.md` (sections 1, 2, 4 gate C, 5, 6,
7, 8 PR 1).

## Global Constraints

- This is PR 1 of 3. Work on branch `feat/codex-opencode-antigravity` (it already holds the spec and these plans).
- The registered Codex command is exactly `dtk hook codex` with `"timeout": 10`, and must never change after it
  ships: Codex hashes the definition to remember the user's approval.
- Codex CLI 0.131 or later is the documented floor (first release accepting `updatedInput`).
- `dtk hook <provider>` always exits 0 and prints nothing on any failure; it never builds the DI container.
- dtk never writes Codex's `trusted_hash`.
- `TreatWarningsAsErrors` is on with Roslynator, SonarAnalyzer and NetAnalyzers; file-scoped namespaces; `var`;
  `_camelCase` private fields; async methods end in `Async`; LF line endings, no trailing whitespace, no BOM;
  4-space indent in `.cs`, 2-space in JSON/YAML.
- No new NuGet packages. The three libraries are `IsAotCompatible`: no reflection-based JSON or TOML. Use `JsonNode`
  and the source-generated Tomlyn context.
- Run dotnet through dtk: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test <project> --filter …`,
  `dtk dotnet format DotnetTokenKiller.slnx --no-restore`.
- The build-spawning CLI integration tests (`Dotnet*IntegrationTests`, `TeeDurabilityTests`, parity) cannot pass on
  this machine; run the filters named in each task and let CI gate the rest.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

### Task 1: Section writes report `unchanged` when the section is already current

`WriteSectionBasedFileAsync` reports an existing file `skipped` without `--force` and `updated` with it, even when the
dtk section in it is already byte-identical. With `AGENTS.md` shared by three providers, a second `dtk init` would
tell the user to pass `--force` for a file `--force` would not change.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs:229-275`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`
- Modify (expectations): `tests/DotnetTokenKiller.Application.Tests/Integration/GeminiCliIntegratorTests.cs`,
  `CopilotCliIntegratorTests.cs`, `JetBrainsAiIntegratorTests.cs`, `AiderIntegratorTests.cs`

**Interfaces:**
- Produces: `IntegratorHelpers.WriteSectionBasedFileAsync` adds `path` to `IntegrationContext.Unchanged` and writes
  nothing when the file's content (line endings normalized to `\n`) already contains `section`, whatever `Force` is.

- [ ] **Step 1: Write the failing tests**

Add to `IntegratorHelpersTests`, next to `WriteSectionBasedFileAsync_ExistingWithMarker_NoForce_SkipsFile`:

```csharp
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteSectionBasedFileAsync_SectionAlreadyCurrent_ReportsUnchangedWithoutWriting(bool force)
    {
        var context = new IntegrationContext(force);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        const string section = "<!-- dtk -->\nNEW\n<!-- /dtk -->";
        var original = $"# Header\n{section}\n# Footer";
        await File.WriteAllTextAsync(path, original);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be(original);
        context.Unchanged.Should().Equal(path);
        context.Skipped.Should().BeEmpty("--force would change nothing, so advising it would be false");
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_CrlfCopyOfTheCurrentSection_ReportsUnchanged()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "# Header\r\n<!-- dtk -->\r\nNEW\r\n<!-- /dtk -->\r\n");

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", "<!-- dtk -->\nNEW\n<!-- /dtk -->", context, CancellationToken.None);

        context.Unchanged.Should().Equal(path);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegratorHelpersTests.WriteSectionBasedFileAsync"`
Expected: the three new cases FAIL (`Unchanged` empty; `Skipped` or `Updated` holds the path).

- [ ] **Step 3: Implement**

In `IntegratorHelpers.WriteSectionBasedFileAsync`, replace the start of the method body up to (not including)
`if (!exists)` with:

```csharp
        var exists = File.Exists(path);

        if (exists
            && await TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false) is { } existing
            && existing.Contains(section.ReplaceLineEndings("\n"), StringComparison.Ordinal))
        {
            // Nothing to write, with or without --force; reporting it skipped would advise a --force that changes nothing.
            context.Unchanged.Add(path);
            return;
        }

        if (ShouldSkipWrite(exists, context.Force))
        {
            context.Skipped.Add(path);
            return;
        }
```

Update the method's `<summary>` to add, before "If the file does not exist":
`If the file already contains exactly <paramref name="section"/> (line endings normalized): reports it unchanged and
writes nothing, force or not.`

- [ ] **Step 4: Run the helper tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegratorHelpersTests"`
Expected: PASS.

- [ ] **Step 5: Update the integrator tests that pinned the old reporting**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~Integration"`
Expected: the second-run tests below FAIL. Update each to the new rule — a second run with the section already
current reports that file unchanged, never skipped or updated — and fix their comments to match:

`GeminiCliIntegratorTests`:

```csharp
    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_ReportsEverythingUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // GEMINI.md already holds the current section and settings.json already registers the hook: dtk can prove
        // there is nothing to write in either, so neither is reported skipped.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(GeminiMdPath, SettingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_ReportsEverythingUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(GeminiMdPath, SettingsPath);
    }
```

(Delete the two old tests these replace: `IntegrateAsync_SecondRun_NoForce_SkipsGeminiMdOnly` and
`IntegrateAsync_SecondRun_WithForce_UpdatesGeminiMdOnly`.)

`CopilotCliIntegratorTests.IntegrateAsync_SecondRun_NoForce_SkipsInstructionsAndReportsRestUnchanged` → rename to
`IntegrateAsync_SecondRun_NoForce_ReportsEverythingUnchanged`, assertions:

```csharp
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().HaveCount(2).And.Contain(HookJsonPath);
```

`CopilotCliIntegratorTests.IntegrateAsync_SecondRun_WithForce_UpdatesInstructionsAndReportsRestUnchanged` → rename to
`IntegrateAsync_SecondRun_WithForce_ReportsEverythingUnchanged`, assertions:

```csharp
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().HaveCount(2).And.Contain(HookJsonPath);
```

`JetBrainsAiIntegratorTests.IntegrateAsync_SecondRun_NoForce_SkipsFile` → `IntegrateAsync_SecondRun_NoForce_ReportsUnchanged`
with `result.UnchangedFiles.Should().ContainSingle(); result.SkippedFiles.Should().BeEmpty(); result.CreatedFiles.Should().BeEmpty();`.
`JetBrainsAiIntegratorTests.IntegrateAsync_SecondRun_WithForce_UpdatesFile` → `IntegrateAsync_SecondRun_WithForce_ReportsUnchanged`
with `result.UnchangedFiles.Should().ContainSingle(); result.UpdatedFiles.Should().BeEmpty(); result.SkippedFiles.Should().BeEmpty();`.

`AiderIntegratorTests.IntegrateAsync_SecondRun_NoForce_SkipsConfAndReportsInstructionsUnchanged` and
`IntegrateAsync_SecondRun_WithForce_UpdatesConfAndReportsInstructionsUnchanged`: only if they now fail, rename to
`…_ReportsEverythingUnchanged` and assert `UnchangedFiles.Should().HaveCount(2)` with `SkippedFiles`/`UpdatedFiles`
empty. If they still pass, the conf file takes Aider's own merge path: leave them alone.

Any other failure in this run is not caused by this rule: stop and investigate instead of editing it to pass.

- [ ] **Step 6: Run the Application tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs tests/DotnetTokenKiller.Application.Tests/Integration
git commit -m "fix: report a section file unchanged when its dtk section is already current"
```

---

### Task 2: Shared instruction artifacts

Move Gemini's section and Claude's skill into one class every provider writes from.

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/SharedInstructionArtifacts.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs:34-104,146-152`
- Modify: `src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs:27-37,86-89`
- Modify: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs` (doc comment naming `ClaudeCodeIntegrator.SkillMarkdown`)
- Modify: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs:145,158`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/SharedInstructionArtifactsTests.cs`

**Interfaces:**
- Produces (all `internal`, class `SharedInstructionArtifacts` in `DotnetTokenKiller.Application.Integration`):
  - `const string SectionMarker = "<!-- dtk -->"`, `const string SectionEndMarker = "<!-- /dtk -->"`
  - `static readonly string Section` — byte-identical to today's `GeminiCliIntegrator.GeminiSection`
  - `static readonly string SkillMarkdown` — byte-identical to today's `ClaudeCodeIntegrator.SkillMarkdown`
  - `const string SkillLegacySignature = "name: dotnet-token-killer"`
  - `static string SkillPath(string skillsDirectory)` → `<skillsDirectory>/dotnet-token-killer/SKILL.md`
  - `static Task WriteAgentsFilesAsync(string instructionsPath, string skillsDirectory, IntegrationContext context, CancellationToken cancellationToken)`
    — section-merges `Section` into `instructionsPath`, then writes the stamped skill.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class SharedInstructionArtifactsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-shared-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Section_IsExactlyWhatGeminiWrites()
    {
        // Antigravity CLI reads the ~/.gemini/GEMINI.md that `dtk init gemini --global` writes; one constant keeps
        // the two providers from rewriting each other's section.
        await new GeminiCliIntegrator(new HomePaths(Path.Combine(_tempDir, "home"))).IntegrateAsync(_tempDir, false, default);

        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "GEMINI.md"))).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public void SkillMarkdown_SatisfiesEveryHarnessSkillLoader()
    {
        var frontmatter = SharedInstructionArtifacts.SkillMarkdown.Split("---")[1];

        // OpenCode requires a lowercase-hyphenated name equal to the folder name (at most 64 characters for Codex);
        // Codex, OpenCode and Antigravity all require a description.
        var folder = Path.GetFileName(Path.GetDirectoryName(SharedInstructionArtifacts.SkillPath("root")))!;
        folder.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        folder.Length.Should().BeLessThanOrEqualTo(64);
        frontmatter.Should().Contain($"name: {folder}\n");
        frontmatter.Should().Contain("description: '");
    }

    [Fact]
    public async Task WriteAgentsFilesAsync_FreshThenRepeated_CreatesBothThenReportsBothUnchanged()
    {
        var agents = Path.Combine(_tempDir, "AGENTS.md");
        var skills = Path.Combine(_tempDir, ".agents", "skills");

        var first = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, first, default);
        var second = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, second, default);

        first.Created.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        second.Unchanged.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        (await File.ReadAllTextAsync(agents)).Should().Be(SharedInstructionArtifacts.Section);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(SharedInstructionArtifacts.SkillPath(skills))).Should().BeTrue();
    }
}
```

(Remove the unused `using System.Text.RegularExpressions;` if the analyzer flags it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `SharedInstructionArtifacts` does not exist.

- [ ] **Step 3: Implement**

Create `SharedInstructionArtifacts.cs`. Move, verbatim, `SkillDescription` (keep it `private static readonly` and
declared **before** `SkillMarkdown`, since static initializers run in textual order), `SkillMarkdown` and
`SkillLegacySignature` from `ClaudeCodeIntegrator`, with their XML docs; move `GeminiSection` from
`GeminiCliIntegrator` as `Section`, and its two marker constants:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// The instruction artifacts more than one harness reads: the <c>&lt;!-- dtk --&gt;</c> section merged into
/// <c>AGENTS.md</c> and <c>GEMINI.md</c>, and the <c>dotnet-token-killer</c> skill.
/// </summary>
/// <remarks>
/// Codex CLI, OpenCode and Antigravity CLI all read <c>AGENTS.md</c> and <c>.agents/skills/</c>, and Antigravity CLI
/// also reads the <c>~/.gemini/GEMINI.md</c> that <see cref="GeminiCliIntegrator"/> writes. Every provider writing
/// one of those files writes these exact strings, so a file two providers share never flips between versions.
/// </remarks>
internal static class SharedInstructionArtifacts
{
    /// <summary>Opens the dtk-managed section.</summary>
    internal const string SectionMarker = "<!-- dtk -->";

    /// <summary>Closes the dtk-managed section.</summary>
    internal const string SectionEndMarker = "<!-- /dtk -->";

    /// <summary>The skill's folder name, which OpenCode requires to equal its frontmatter <c>name</c>.</summary>
    private const string SkillName = "dotnet-token-killer";

    // SkillDescription, SkillMarkdown and SkillLegacySignature moved here verbatim from ClaudeCodeIntegrator.

    /// <summary>The dtk section merged into <c>AGENTS.md</c> and <c>GEMINI.md</c>.</summary>
    internal static readonly string Section =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}
        {SectionEndMarker}
        """;

    /// <summary>Where the skill lives under a harness's skills directory.</summary>
    /// <param name="skillsDirectory">A skills root such as <c>.agents/skills</c>.</param>
    internal static string SkillPath(string skillsDirectory) => Path.Combine(skillsDirectory, SkillName, "SKILL.md");

    /// <summary>Merges <see cref="Section"/> into an instructions file and writes the stamped skill.</summary>
    /// <param name="instructionsPath">The <c>AGENTS.md</c> (or <c>GEMINI.md</c>) to merge into.</param>
    /// <param name="skillsDirectory">The skills root the skill folder goes under.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteAgentsFilesAsync(
        string instructionsPath, string skillsDirectory, IntegrationContext context, CancellationToken cancellationToken)
    {
        await IntegratorHelpers.WriteSectionBasedFileAsync(
            instructionsPath, SectionMarker, SectionEndMarker, Section, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteGeneratedFileAsync(
            new GeneratedArtifact(SkillPath(skillsDirectory), SkillMarkdown, StampStyle.HtmlComment, SkillLegacySignature),
            context, cancellationToken).ConfigureAwait(false);
    }
}
```

In `ClaudeCodeIntegrator.IntegrateCoreAsync`, the skill write becomes:

```csharp
        await IntegratorHelpers.WriteGeneratedFileAsync(
            new GeneratedArtifact(
                SharedInstructionArtifacts.SkillPath(Path.Combine(baseDirectory, "skills")),
                SharedInstructionArtifacts.SkillMarkdown,
                StampStyle.HtmlComment,
                SharedInstructionArtifacts.SkillLegacySignature),
            context, cancellationToken).ConfigureAwait(false);
```

In `GeminiCliIntegrator`, delete the two marker constants and `GeminiSection`, and write:

```csharp
        await IntegratorHelpers.WriteSectionBasedFileAsync(
            contextFilePath,
            SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker, SharedInstructionArtifacts.Section,
            context, cancellationToken).ConfigureAwait(false);
```

Replace `ClaudeCodeIntegrator.SkillMarkdown` with `SharedInstructionArtifacts.SkillMarkdown` in
`SubcommandBindingTests` (two places) and in the `DotnetSubcommands.cs` doc comment. Fix any `<see cref>` in the
moved docs that pointed at their old class.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: PASS, including the unchanged `ClaudeCodeIntegratorTests`, `GeminiCliIntegratorTests` and `SubcommandBindingTests`.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application src/DotnetTokenKiller.Domain/DotnetSubcommands.cs tests/DotnetTokenKiller.Application.Tests
git commit -m "refactor: share the dtk instructions section and skill across providers"
```

---

### Task 3: `HomePaths` gains an environment seam, `CodexDir` and `AgentsSkillsDir`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`

**Interfaces:**
- Produces: `internal HomePaths(string home, Func<string, string?> environment)`; `internal HomePaths(string home)`
  now reads no environment (`_ => null`); `internal string CodexDir` (`$CODEX_HOME` when rooted, else `~/.codex`);
  `internal string AgentsSkillsDir` (`~/.agents/skills`); private helper
  `string RootedOrDefault(string variable, string fallback)`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void CodexDir_DefaultsToDotCodexUnderHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home).CodexDir.Should().Be(Path.Combine(home, ".codex"));
        new HomePaths(home).AgentsSkillsDir.Should().Be(Path.Combine(home, ".agents", "skills"));
    }

    [Fact]
    public void CodexDir_HonorsAnAbsoluteCodexHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var codexHome = Path.Combine(Path.GetTempPath(), "custom-codex");

        new HomePaths(home, name => name == "CODEX_HOME" ? codexHome : null).CodexDir.Should().Be(codexHome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/codex")]
    public void CodexDir_IgnoresAnEmptyOrRelativeCodexHome(string value)
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home, _ => value).CodexDir.Should().Be(Path.Combine(home, ".codex"));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `CodexDir`, `AgentsSkillsDir` and the two-argument constructor do not exist.

- [ ] **Step 3: Implement**

```csharp
internal sealed class HomePaths
{
    private readonly Func<string, string?> _environment;

    /// <summary>Creates an instance rooted at the real user profile directory, reading the real environment.</summary>
    public HomePaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>Test seam: inject an isolated home directory, with no environment overrides.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    internal HomePaths(string home)
        : this(home, static _ => null)
    {
    }

    /// <summary>Test seam: inject an isolated home directory and environment.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    /// <param name="environment">Reads an environment variable; returns <see langword="null"/> when unset.</param>
    internal HomePaths(string home, Func<string, string?> environment)
    {
        Home = home;
        _environment = environment;
    }

    // … existing properties unchanged …

    /// <summary>Gets Codex CLI's home directory: <c>$CODEX_HOME</c> when it is an absolute path, else <c>~/.codex</c>.</summary>
    internal string CodexDir => RootedOrDefault("CODEX_HOME", Path.Combine(Home, ".codex"));

    /// <summary>Gets the user-level skills directory Codex CLI and OpenCode both read (<c>~/.agents/skills</c>).</summary>
    internal string AgentsSkillsDir => Path.Combine(Home, ".agents", "skills");

    /// <summary>An environment variable's value when it is an absolute path, otherwise <paramref name="fallback"/>.</summary>
    /// <param name="variable">The variable to read.</param>
    /// <param name="fallback">The path to use when the variable is unset, empty or relative.</param>
    private string RootedOrDefault(string variable, string fallback)
    {
        var value = _environment(variable);
        return string.IsNullOrEmpty(value) || !Path.IsPathRooted(value) ? fallback : value;
    }
}
```

`AddSingleton<HomePaths>()` keeps working: the container only considers the public parameterless constructor.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HomePathsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HomePaths.cs tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs
git commit -m "feat: resolve Codex CLI's home and the shared skills directory"
```

---

### Task 4: Registration timeout, and hooks with no legacy script

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs:7-12,305-322`
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs:26-42`
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs:73`
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`, `GeminiCliIntegrator.cs`,
  `CopilotCliIntegrator.cs` (pass `hook.LegacyScriptPath!`)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Produces: `HookRegistrationSpec(string SettingsPath, string EventKey, string Matcher, string Command, int? TimeoutSeconds = null)`;
  `HookInstallation(..., string? LegacyScriptPath, HookPayloadKind PayloadKind)`.

- [ ] **Step 1: Write the failing tests**

In `IntegratorHelpersTests`, near the `WriteHookRegistrationAsync` tests (line ~1502):

```csharp
    [Fact]
    public async Task WriteHookRegistrationAsync_WithTimeout_WritesItOnTheHandler()
    {
        var path = Path.Combine(_tempDir, "hooks.json");
        var context = new IntegrationContext(false);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook codex", TimeoutSeconds: 10), context, CancellationToken.None);

        var handler = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!;
        handler["command"]!.GetValue<string>().Should().Be("dtk hook codex");
        handler["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task WriteHookRegistrationAsync_WithoutTimeout_WritesNoTimeoutKey()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var context = new IntegrationContext(false);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        var handler = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!.AsObject();
        handler.ContainsKey("timeout").Should().BeFalse("existing providers' registrations must stay byte-identical");
    }
```

(Add `using System.Text.Json.Nodes;` if the file lacks it.)

In `HookHealthCheckerTests`:

```csharp
    [Fact]
    public async Task RunAsync_InstallationWithoutALegacyScriptAndNoRegistration_IsNotReported()
    {
        var integrator = new FixedHooks(new HookInstallation(
            "codex", HookScope.Project, Path.Combine(_tempDir, ".codex", "hooks.json"), "dtk hook codex", null, HookPayloadKind.ClaudeCode));

        var checks = await _sut.RunAsync([integrator], _tempDir, default);

        checks.Should().ContainSingle().Which.Name.Should().Be("hook integration");
    }

    /// <summary>
    /// An integrator describing fixed installations. A hand-written fake, because NSubstitute cannot proxy the internal
    /// <see cref="IHookIntegrator"/> (the Application assembly grants no internals to DynamicProxyGenAssembly2).
    /// </summary>
    /// <param name="installations">The installations to describe, filtered by scope.</param>
    private sealed class FixedHooks(params HookInstallation[] installations) : IHookIntegrator
    {
        public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
            [.. installations.Where(installation => installation.Scope == scope)];
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — no `TimeoutSeconds` parameter; `null` not accepted for `LegacyScriptPath`.

- [ ] **Step 3: Implement**

`IntegratorHelpers.cs` — the record and its doc:

```csharp
/// <param name="TimeoutSeconds">A <c>timeout</c> written on the handler, or <see langword="null"/> to write none.</param>
internal sealed record HookRegistrationSpec(
    string SettingsPath, string EventKey, string Matcher, string Command, int? TimeoutSeconds = null);
```

`WriteHookRegistrationAsync` becomes a block body:

```csharp
    internal static Task<bool> WriteHookRegistrationAsync(
        HookRegistrationSpec spec, IntegrationContext context, CancellationToken cancellationToken)
    {
        var handler = new JsonObject { ["type"] = "command", [CommandKey] = spec.Command };
        if (spec.TimeoutSeconds is { } timeout)
        {
            handler["timeout"] = timeout;
        }

        return MergeJsonSettingsAsync(
            spec.SettingsPath,
            spec.EventKey,
            new JsonObject { ["matcher"] = spec.Matcher, [HooksKey] = new JsonArray(handler) },
            spec.Command,
            context,
            cancellationToken);
    }
```

`IHookIntegrator.cs`: `string? LegacyScriptPath,` with doc
`Where dtk installed the Python hook this registration replaces, or <see langword="null"/> for a provider that never had one.`

`HookHealthChecker.cs:73`:

```csharp
                    if (registration.Kind == RegistrationKind.Absent
                        && (installation.LegacyScriptPath is null || !File.Exists(installation.LegacyScriptPath)))
```

In the three existing integrators, pass `hook.LegacyScriptPath!` to `RetireLegacyHookScriptAsync` (their
`DescribeHooks` always sets it).

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: register hook timeouts and describe hooks that never had a Python script"
```

---

### Task 5: `dtk hook codex`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs:14-24` (enum)
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Modify: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs:18-19`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs`

**Interfaces:**
- Consumes: `DotnetCommandRewriter.Rewrite`, `HookPayloads.TryRewrite` (private, existing).
- Produces: `HookPayloadKind.CodexCli = 3`; `HookPayloads.TryGetKind("codex", out HookPayloadKind.CodexCli)`; usage string
  `usage: dtk hook <claude|gemini|copilot-cli|codex> (run by an AI agent's pre-tool hook; reads the payload on stdin)`.

- [ ] **Step 1: Write the failing tests**

`HookPayloadsTests` — add `[InlineData("codex", HookPayloadKind.CodexCli)]` to `TryGetKind_KnownProvider_Resolves`, then:

```csharp
    [Fact]
    public void Codex_Rewrite_AllowsWithTheWholeToolInputAsUpdatedInput()
    {
        var reply = Reply(HookPayloadKind.CodexCli,
            """{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet build","extra":1}}""");

        var output = JsonNode.Parse(reply!)!["hookSpecificOutput"]!;
        output["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        output["permissionDecision"]!.GetValue<string>().Should().Be("allow", "Codex rejects updatedInput without it");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        output["updatedInput"]!["extra"]!.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_name":"apply_patch","tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":42,"tool_input":{"command":"dotnet build"}}""")]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":""}}""")]
    [InlineData("""{"tool_name":"Bash"}""")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Codex_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.CodexCli, payload).Should().BeNull();
    }

    [Fact]
    public void Codex_PayloadWithoutAToolName_StillRewrites()
    {
        // doctor's probe sends only tool_input.
        Reply(HookPayloadKind.CodexCli, """{"tool_input":{"command":"dotnet test"}}""").Should().Contain("dtk dotnet test");
    }

    [Fact]
    public void Codex_DuplicateJsonKey_PrintsNothing()
    {
        Reply(HookPayloadKind.CodexCli, """{"tool_name":"Bash","tool_input":{"command":"a","command":"b"}}""").Should().BeNull();
    }
```

`HookEntryPointTests` and `HookIntegrationTests`: change every expected usage fragment from
`dtk hook <claude|gemini|copilot-cli>` to `dtk hook <claude|gemini|copilot-cli|codex>`. Add to `HookIntegrationTests`:

```csharp
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Codex_RewritesWithTheMandatoryAllowAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet test"}}""", "hook", "codex");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        var output = JsonNode.Parse(stdout)!["hookSpecificOutput"]!;
        output["permissionDecision"]!.GetValue<string>().Should().Be("allow");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `HookPayloadKind.CodexCli` does not exist.

- [ ] **Step 3: Implement**

`IHookIntegrator.cs`, in `HookPayloadKind`:

```csharp
    /// <summary>OpenAI Codex CLI's <c>PreToolUse</c> payload.</summary>
    CodexCli = 3
```

`HookPayloads.cs`:
- `TryGetKind`: add `"codex" => (true, HookPayloadKind.CodexCli),` and name it in the summary.
- `Reply` switch: add `HookPayloadKind.CodexCli => ReplyToCodex(root),`.
- New members:

```csharp
    private static string? ReplyToCodex(JsonNode? root)
    {
        if (root is not JsonObject payload
            || NamesAnotherTool(payload, "tool_name", "Bash")
            || payload["tool_input"] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput["command"] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                // Codex rejects updatedInput unless the reply also allows; it still applies its approval policy and
                // sandbox to the rewritten command.
                ["permissionDecision"] = "allow",
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    /// <summary>
    /// Whether <paramref name="payload"/> names a tool other than <paramref name="expected"/> under <paramref name="key"/>.
    /// A payload without the key names none, which is what doctor's probe sends.
    /// </summary>
    /// <param name="payload">The hook payload.</param>
    /// <param name="key">The property holding the tool name.</param>
    /// <param name="expected">The shell tool's name.</param>
    private static bool NamesAnotherTool(JsonObject payload, string key, string expected) =>
        payload[key] is not null
        && (payload[key] is not JsonValue value || !value.TryGetValue<string>(out var name) || name != expected);
```

`HookEntryPoint.cs`: update `Usage` to list `codex`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application src/DotnetTokenKiller.Cli/HookEntryPoint.cs tests
git commit -m "feat: answer Codex CLI's PreToolUse hook with dtk hook codex"
```

---

### Task 6: rtk reconciliation from any harness's files

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs:168-179`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs`

**Interfaces:**
- Produces:
  - `internal static bool RtkHookCoexistence.IsRtkRewriteReferencedIn(IEnumerable<string> harnessFiles)`
  - `public Task<RtkReconcileOutcome> RtkHookCoexistence.ReconcileFilesAsync(IReadOnlyList<string> harnessFiles, CancellationToken cancellationToken)`
  - `internal void RtkReconcileOutcome.ApplyTo(IntegrationContext context)`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Theory]
    [InlineData("""{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]}]}}""")]
    [InlineData("""{"rtk-rewrite":{"PreToolUse":[{"matcher":"run_command","hooks":[{"command":"rtk hook antigravity"}]}]}}""")]
    [InlineData("const result = await $`rtk rewrite ${command}`.quiet().nothrow()")]
    public async Task ReconcileFilesAsync_HarnessFileRunsAnRtkRewrite_ExcludesDotnet(string content)
    {
        var file = Path.Combine(ProjectDir, "harness", "hooks.json");
        await WriteAsync(file, content);

        var outcome = await CreateSut().ReconcileFilesAsync([Path.Combine(ProjectDir, "missing.json"), file], CancellationToken.None);

        outcome.CreatedConfigPath.Should().Be(RtkConfigPath);
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Theory]
    [InlineData("""{"hooks":{"PreToolUse":[{"hooks":[{"command":"dtk hook codex"}]}]}}""")]
    [InlineData("# we used to run rtk here")]
    [InlineData("trtk hook codex")]
    public async Task ReconcileFilesAsync_NoRtkRewrite_ChangesNothing(string content)
    {
        var file = Path.Combine(ProjectDir, "harness", "hooks.json");
        await WriteAsync(file, content);

        var outcome = await CreateSut().ReconcileFilesAsync([file], CancellationToken.None);

        outcome.Should().Be(RtkReconcileOutcome.None);
        File.Exists(RtkConfigPath).Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_CopiesPathsAndNotesIntoTheContext()
    {
        var context = new IntegrationContext(false);

        new RtkReconcileOutcome("created.toml", "updated.toml", ["note"]).ApplyTo(context);

        context.Created.Should().Equal("created.toml");
        context.Updated.Should().Equal("updated.toml");
        context.Notes.Should().Equal("note");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `ReconcileFilesAsync` and `ApplyTo` do not exist.

- [ ] **Step 3: Implement**

In `RtkHookCoexistence`:

```csharp
    /// <summary>
    /// Detect an rtk rewrite in any of a harness's hook or plugin files and, if one is present, reconcile rtk's config.
    /// </summary>
    /// <param name="harnessFiles">The files where rtk registers itself for the harness being integrated.</param>
    /// <param name="cancellationToken">Token used to cancel the config read/write.</param>
    public async Task<RtkReconcileOutcome> ReconcileFilesAsync(
        IReadOnlyList<string> harnessFiles, CancellationToken cancellationToken)
    {
        return IsRtkRewriteReferencedIn(harnessFiles)
            ? await ReconcileRtkConfigAsync(cancellationToken).ConfigureAwait(false)
            : RtkReconcileOutcome.None;
    }

    /// <summary>
    /// True when any file runs an rtk rewrite: <c>rtk hook …</c> in a hook registration, or <c>rtk rewrite …</c> in a
    /// plugin that shells out to it. A text search, because the files are JSON, TOML or JavaScript depending on the
    /// harness; rtk routes every one of those entry points through the decision that honors <c>exclude_commands</c>.
    /// </summary>
    /// <param name="harnessFiles">The files to search; missing or unreadable ones count as no rtk.</param>
    internal static bool IsRtkRewriteReferencedIn(IEnumerable<string> harnessFiles) =>
        harnessFiles.Any(FileMentionsRtkRewrite);

    private static bool FileMentionsRtkRewrite(string path)
    {
        try
        {
            return File.Exists(path) && RtkRewriteInvocationRegex().IsMatch(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another tool's file we cannot read: we cannot tell, so assume no rtk.
            return false;
        }
    }

    [GeneratedRegex(@"(?:^|[\s/\\""'`])rtk(?:\.exe)?\s+(?:hook|rewrite)\b")]
    private static partial Regex RtkRewriteInvocationRegex();
```

In `RtkReconcileOutcome`:

```csharp
    /// <summary>Records what the reconciliation changed in an integration run's result.</summary>
    /// <param name="context">The run's context.</param>
    internal void ApplyTo(IntegrationContext context)
    {
        if (CreatedConfigPath is not null)
        {
            context.Created.Add(CreatedConfigPath);
        }

        if (UpdatedConfigPath is not null)
        {
            context.Updated.Add(UpdatedConfigPath);
        }

        context.Notes.AddRange(Notes);
    }
```

In `ClaudeCodeIntegrator.IntegrateCoreAsync`, replace the three blocks after `ReconcileAsync` with
`rtkOutcome.ApplyTo(context);`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~RtkHookCoexistenceTests|FullyQualifiedName~ClaudeCodeIntegratorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs
git commit -m "feat: reconcile rtk from any harness's hook or plugin files"
```

---

### Task 7: `CodexIntegrator`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/CodexIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs:44-51`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CodexIntegratorTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs:360-367`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Aot/ParityCases.cs:70`

**Interfaces:**
- Consumes: `SharedInstructionArtifacts.WriteAgentsFilesAsync` (Task 2), `HomePaths.CodexDir`/`AgentsSkillsDir`
  (Task 3), `HookRegistrationSpec(..., TimeoutSeconds)` (Task 4), `HookPayloadKind.CodexCli` (Task 5),
  `RtkHookCoexistence.ReconcileFilesAsync`/`RtkReconcileOutcome.ApplyTo` (Task 6).
- Produces: `internal sealed class CodexIntegrator(RtkHookCoexistence rtk, HomePaths home) : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator`
  with `ProviderName == "codex"`, `internal const string ApprovalNote`, `internal const string ProjectTrustNote`.
  Task 9 adds `IHookApprovalInspector` to it.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CodexIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-codex-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string HooksPath => Path.Combine(ProjectDir, ".codex", "hooks.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CodexIntegrator CreateSut()
    {
        var home = new HomePaths(HomeDir, name => _environment.GetValueOrDefault(name));
        return new CodexIntegrator(new RtkHookCoexistence(home.ClaudeDir, RtkConfigPath), home);
    }

    [Fact]
    public void ProviderName_IsCodex() => CreateSut().ProviderName.Should().Be("codex");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndHookWithBothNotes()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().Equal(CodexIntegrator.ApprovalNote, CodexIntegrator.ProjectTrustNote);
        (await File.ReadAllTextAsync(AgentsPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public async Task IntegrateAsync_HooksJson_HoldsOnlyTheDtkPreToolUseHookForBash()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!.AsObject();
        root.Should().ContainSingle().Which.Key.Should().Be("hooks", "Codex rejects unknown top-level keys in hooks.json");
        var group = root["hooks"]!["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("Bash");
        var handler = group["hooks"]!.AsArray().Should().ContainSingle().Subject!;
        handler["type"]!.GetValue<string>().Should().Be("command");
        handler["command"]!.GetValue<string>().Should().Be("dtk hook codex");
        handler["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutRepeatingTheNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().BeEmpty("an unchanged hook keeps whatever approval the user already gave");
    }

    [Fact]
    public async Task IntegrateAsync_HooksJsonWithAForeignHook_KeepsItBesideDtks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"audit.sh"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(HooksPath);
        var json = await File.ReadAllTextAsync(HooksPath);
        json.Should().Contain("audit.sh").And.Contain("dtk hook codex");
    }

    [Fact]
    public async Task IntegrateAsync_MalformedHooksJson_Throws()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath, "{ not json");

        var act = () => CreateSut().IntegrateAsync(ProjectDir, false, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderCodexHomeAndTheSharedSkillsDirectory()
    {
        var codexHome = Path.Combine(_tempDir, "codex-home");
        _environment["CODEX_HOME"] = codexHome;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(codexHome, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(codexHome, "hooks.json"));
        result.Notes.Should().Equal(CodexIntegrator.ApprovalNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WithoutCodexHome_WritesUnderDotCodex()
    {
        await CreateSut().IntegrateGlobalAsync(false, default);

        File.Exists(Path.Combine(HomeDir, ".codex", "hooks.json")).Should().BeTrue();
        File.Exists(Path.Combine(HomeDir, ".codex", "AGENTS.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_RtkHookInCodexHooks_ExcludesDotnetInRtkConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `CodexIntegrator` does not exist.

- [ ] **Step 3: Implement**

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for OpenAI Codex CLI.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory and <c>$CODEX_HOME</c> for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description>
///     <c>.codex/hooks.json</c> registering <c>dtk hook codex</c> under <c>PreToolUse</c> (merged, never overwritten)
///   </description></item>
/// </list>
/// Codex runs a hook only once the user has approved it, and remembers the approval as a hash of the definition, so
/// the command and timeout registered here must never change. dtk does not approve the hook itself: the approval is
/// the user's record of reviewing what runs before every shell command.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CodexIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <summary>Printed when this run wrote the hook, which Codex will not run until the user approves it.</summary>
    internal const string ApprovalNote =
        "Codex runs this hook only after you approve it: open Codex and review it under /hooks "
        + "('codex exec' skips unapproved hooks silently). Requires Codex 0.131 or later.";

    /// <summary>Printed when this run wrote a project hook, which Codex reads only in trusted projects.</summary>
    internal const string ProjectTrustNote = "Codex reads .codex/ only in projects you have trusted.";

    /// <summary>Seconds Codex waits for the hook. Part of the approval hash: never change it.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <inheritdoc/>
    public string ProviderName => "codex";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var codexDir = scope == HookScope.Global ? home.CodexDir : Path.Combine(directory, ".codex");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(codexDir, "hooks.json"),
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.CodexCli)
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "AGENTS.md"),
            Path.Combine(directory, ".agents", "skills"),
            directory,
            HookScope.Project,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.CodexDir, "AGENTS.md"),
            home.AgentsSkillsDir,
            home.Home,
            HookScope.Global,
            force,
            cancellationToken);

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
            new HookRegistrationSpec(hook.RegistrationPath, "PreToolUse", "Bash", hook.Command, HookTimeoutSeconds),
            context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(ApprovalNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(ProjectTrustNote);
            }
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(
            [
                DescribeHooks(hookDirectory, HookScope.Project)[0].RegistrationPath,
                DescribeHooks(hookDirectory, HookScope.Global)[0].RegistrationPath
            ],
            cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }
}
```

Register it in `DependencyInjection.cs` after `GeminiCliIntegrator`:
`services.AddTransient<IProviderIntegrator, CodexIntegrator>();`

`HookDescriptionTests`: add `new CodexIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home)` to both
integrator arrays, `["codex"] = "dtk hook codex"` to `expected`, and guard the legacy-name assertion:

```csharp
            if (installation.LegacyScriptPath is not null)
            {
                Path.GetFileName(installation.LegacyScriptPath).Should().Be(IntegratorHelpers.LegacyHookScriptName);
            }
```

`InitCommandTests.ExecuteAsync_EveryProviderName_RoutesToMatchingIntegrator`: add `[InlineData("copilot-cli")]` and
`[InlineData("codex")]`. `ParityCases.cs:70`: add `"codex"` after `"gemini"`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application tests
git commit -m "feat: dtk init codex installs the AGENTS.md section, skill and PreToolUse hook"
```

---

### Task 8: A warning state for doctor

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs:8-12`
- Modify: `src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs:37-57`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/DoctorCommandTests.cs`

**Interfaces:**
- Produces: `public sealed record DiagnosticCheck(string Name, bool Passed, string Message, bool IsWarning = false)`
  with `public static DiagnosticCheck Warning(string name, string message)`;
  `internal static int DoctorCommand.Render(IAnsiConsole console, IReadOnlyList<DiagnosticCheck> checks)`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Render_WarningsOnly_ExitsZeroAndSaysSo()
    {
        var console = new TestConsole();

        var exitCode = DoctorCommand.Render(console,
        [
            new DiagnosticCheck("codex hook (project)", true, "registered"),
            DiagnosticCheck.Warning("codex hook approval (project)", "not yet approved")
        ]);

        exitCode.Should().Be(0, "a warning never fails doctor");
        console.Output.Should().Contain("!  codex hook approval (project): not yet approved");
        console.Output.Should().Contain("All checks passed, with 1 warning(s).");
    }

    [Fact]
    public void Render_AFailureAndAWarning_ExitsOne()
    {
        var console = new TestConsole();

        var exitCode = DoctorCommand.Render(console,
        [
            new DiagnosticCheck("dotnet SDK", false, "missing"),
            DiagnosticCheck.Warning("codex hook approval (project)", "not yet approved")
        ]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Some checks failed");
    }

    [Fact]
    public void Warning_PassesAndIsMarked()
    {
        var check = DiagnosticCheck.Warning("n", "m");

        check.Passed.Should().BeTrue();
        check.IsWarning.Should().BeTrue();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `Render`, `Warning` and `IsWarning` do not exist.

- [ ] **Step 3: Implement**

`DoctorUseCase.cs`:

```csharp
/// <summary>Result of a single doctor diagnostic check.</summary>
/// <param name="Name">Short display name for the check.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Message">Human-readable detail message.</param>
/// <param name="IsWarning">Whether a passing check still deserves the user's attention; a warning never fails doctor.</param>
public sealed record DiagnosticCheck(string Name, bool Passed, string Message, bool IsWarning = false)
{
    /// <summary>Creates a passing check marked as a warning.</summary>
    /// <param name="name">Short display name for the check.</param>
    /// <param name="message">Human-readable detail message.</param>
    public static DiagnosticCheck Warning(string name, string message) => new(name, true, message, IsWarning: true);
}
```

`DoctorCommand.cs` — `RunAsync` ends with `return Render(console, checks);` after computing `checks`, and:

```csharp
    /// <summary>Prints one line per check and a summary, and returns doctor's exit code.</summary>
    /// <param name="console">The output sink.</param>
    /// <param name="checks">The checks to print.</param>
    /// <returns>1 when any check failed, otherwise 0; warnings do not count as failures.</returns>
    internal static int Render(IAnsiConsole console, IReadOnlyList<DiagnosticCheck> checks)
    {
        foreach (var check in checks)
        {
            var icon = (check.Passed, check.IsWarning) switch
            {
                (false, _) => "[red]✘[/]",
                (true, true) => "[yellow]![/]",
                _ => "[green]✔[/]"
            };
            console.MarkupLine($"  {icon}  [bold]{Markup.Escape(check.Name)}[/]: {Markup.Escape(check.Message)}");
        }

        var failed = checks.Count(check => !check.Passed);
        var warnings = checks.Count(check => check.Passed && check.IsWarning);

        console.WriteLine();
        console.MarkupLine((failed, warnings) switch
        {
            (> 0, _) => "[red]Some checks failed. Review the output above.[/]",
            (_, > 0) => $"[green]All checks passed[/][yellow], with {warnings} warning(s).[/]",
            _ => "[green]All checks passed.[/]"
        });

        return failed > 0 ? 1 : 0;
    }
```

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DoctorCommandTests"`
Expected: PASS (existing tests unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/DoctorCommandTests.cs
git commit -m "feat: doctor reports warnings that do not fail the run"
```

---

### Task 9: doctor checks Codex's hook approval and project trust

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/IHookApprovalInspector.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/CodexConfig.cs`
- Rename: `src/DotnetTokenKiller.Application/Integration/RtkTomlContext.cs` → `TomlTableContext.cs` (class
  `TomlTableContext`; update both uses in `RtkHookCoexistence.cs`)
- Modify: `src/DotnetTokenKiller.Application/Integration/CodexIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs:57-117`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/CodexConfigTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Consumes: `DiagnosticCheck.Warning` (Task 8), `CodexIntegrator` (Task 7).
- Produces:
  - `internal sealed record HookApprovalFinding(string Label, bool Satisfied, string Message)`
  - `internal interface IHookApprovalInspector { IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory); }`
  - `internal sealed class CodexConfig` with `static CodexConfig Load(string path)`, `bool IsReadable`,
    `bool HasHookApproval(string hooksJsonPath)`, `bool TrustsProject(string projectDirectory)`.
  - `HookHealthChecker` appends one check per finding after a current registration's probe: satisfied → pass,
    unsatisfied → `DiagnosticCheck.Warning`, named `"<provider> <label> (<scope>)"`.

Codex's approval key is `"<hooks.json path>:pre_tool_use:<group>:<handler>"` under `[hooks.state]`
(`codex-rs/hooks/src/engine/discovery.rs`, `format!("{}:pre_tool_use:0:0", source_path.display())`); project trust is
`[projects."<absolute path>"] trust_level = "trusted"` (`codex-rs/config/src/config_toml.rs`). Gate C3 (Task 12)
confirms both against a real install.

- [ ] **Step 1: Write the failing tests**

`CodexConfigTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CodexConfigTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-codexcfg-{Guid.NewGuid()}");

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");
    private string HooksPath => Path.Combine(_tempDir, "project", ".codex", "hooks.json");
    private string ProjectDir => Path.Combine(_tempDir, "project");

    public CodexConfigTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public void Load_MissingFile_IsReadableWithNoApprovalOrTrust()
    {
        var config = CodexConfig.Load(ConfigPath);

        config.IsReadable.Should().BeTrue();
        config.HasHookApproval(HooksPath).Should().BeFalse();
        config.TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task HasHookApproval_StateKeyForThatFile_IsTrue()
    {
        // Literal TOML strings, so a Windows path's backslashes need no escaping.
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var config = CodexConfig.Load(ConfigPath);

        config.HasHookApproval(HooksPath).Should().BeTrue();
        config.HasHookApproval(Path.Combine(_tempDir, "other", "hooks.json")).Should().BeFalse();
    }

    [Fact]
    public async Task TrustsProject_TrustedAncestor_IsTrue()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task TrustsProject_UntrustedEntry_IsFalse()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{ProjectDir}']
            trust_level = "untrusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task Load_InvalidToml_IsNotReadable()
    {
        await File.WriteAllTextAsync(ConfigPath, "[hooks\nnot toml");

        CodexConfig.Load(ConfigPath).IsReadable.Should().BeFalse();
    }
}
```

`HookHealthCheckerTests.cs`:

```csharp
    private CodexIntegrator Codex => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);

    private string CodexConfigPath => Path.Combine(Home.CodexDir, "config.toml");

    [Fact]
    public async Task RunAsync_CodexHookNotApprovedInAnUntrustedProject_WarnsTwiceWithoutFailing()
    {
        await Codex.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal(
            "codex hook (project)", "codex hook probe (project)", "codex hook approval (project)", "codex project trust (project)");
        checks.Should().OnlyContain(c => c.Passed);
        checks.Skip(2).Should().OnlyContain(c => c.IsWarning);
        checks[2].Message.Should().Contain("/hooks");
    }

    [Fact]
    public async Task RunAsync_CodexHookApprovedInATrustedProject_HasNoWarnings()
    {
        await Codex.IntegrateAsync(_tempDir, force: false, default);
        var hooksPath = Codex.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(CodexConfigPath)!);
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"

            [hooks.state.'{hooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Should().HaveCount(4).And.OnlyContain(c => c.Passed && !c.IsWarning);
    }

    [Fact]
    public async Task RunAsync_CodexGlobalHook_ChecksApprovalButNotProjectTrust()
    {
        await Codex.IntegrateGlobalAsync(force: false, default);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal(
            "codex hook (global)", "codex hook probe (global)", "codex hook approval (global)");
    }

    [Fact]
    public async Task RunAsync_CodexConfigUnreadable_WarnsThatApprovalIsUnknown()
    {
        await Codex.IntegrateGlobalAsync(force: false, default);
        await File.WriteAllTextAsync(CodexConfigPath, "[hooks\nnot toml");

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        var approval = checks.Single(c => c.Name == "codex hook approval (global)");
        approval.IsWarning.Should().BeTrue();
        approval.Message.Should().Contain(CodexConfigPath).And.Contain("could not be read");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `CodexConfig` does not exist.

- [ ] **Step 3: Implement**

`IHookApprovalInspector.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>One thing a harness requires before it runs an installed hook, and whether it holds.</summary>
/// <param name="Label">The check label, e.g. <c>hook approval</c>.</param>
/// <param name="Satisfied">Whether the requirement holds; doctor warns when it does not.</param>
/// <param name="Message">What was found, and what to do when it is not satisfied.</param>
internal sealed record HookApprovalFinding(string Label, bool Satisfied, string Message);

/// <summary>Implemented by integrators whose harness runs a registered hook only once the user has approved it.</summary>
internal interface IHookApprovalInspector
{
    /// <summary>Reports the harness's approval state for one registered installation.</summary>
    /// <param name="installation">A registration doctor found current.</param>
    /// <param name="projectDirectory">The directory doctor treats as the project root.</param>
    IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory);
}
```

`CodexConfig.cs`:

```csharp
using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>The parts of Codex CLI's <c>config.toml</c> doctor reads: hook approvals and project trust.</summary>
/// <remarks>
/// Codex records an approved hook under <c>[hooks.state."&lt;hooks.json path&gt;:pre_tool_use:&lt;group&gt;:&lt;handler&gt;"]</c>
/// with a <c>trusted_hash</c> over the hook's definition. The hash is internal to Codex, so this reads only whether an
/// approval exists for a file, never whether it still matches.
/// </remarks>
internal sealed class CodexConfig
{
    private readonly TomlTable? _root;

    private CodexConfig(TomlTable? root) => _root = root;

    /// <summary>Gets a value indicating whether the file was absent or parsed; <see langword="false"/> when it could not be read.</summary>
    internal bool IsReadable => _root is not null;

    /// <summary>Reads a <c>config.toml</c>; a missing file reads as empty.</summary>
    /// <param name="path">The file to read.</param>
    internal static CodexConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new CodexConfig(new TomlTable());
            }

            return TomlSerializer.TryDeserialize(File.ReadAllText(path), TomlTableContext.Default, out TomlTable? model)
                ? new CodexConfig(model)
                : new CodexConfig(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CodexConfig(null);
        }
    }

    /// <summary>Whether any <c>PreToolUse</c> handler in <paramref name="hooksJsonPath"/> has a recorded approval.</summary>
    /// <param name="hooksJsonPath">The registration file, as Codex discovers it.</param>
    internal bool HasHookApproval(string hooksJsonPath)
    {
        var prefix = hooksJsonPath + ":pre_tool_use:";
        return Table(Table(_root, "hooks"), "state") is { } state
               && state.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="projectDirectory"/> or one of its ancestors is marked trusted.</summary>
    /// <param name="projectDirectory">The project root.</param>
    internal bool TrustsProject(string projectDirectory)
    {
        if (Table(_root, "projects") is not { } projects)
        {
            return false;
        }

        for (var directory = new DirectoryInfo(Path.GetFullPath(projectDirectory)); directory is not null; directory = directory.Parent)
        {
            if (Table(projects, directory.FullName) is { } project
                && project.TryGetValue("trust_level", out var level)
                && level is "trusted")
            {
                return true;
            }
        }

        return false;
    }

    private static TomlTable? Table(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as TomlTable : null;
}
```

Rename `RtkTomlContext` to `TomlTableContext` (file and class; doc: "Source-generated Tomlyn metadata for reading a
TOML file as an untyped <see cref="TomlTable"/> …"), updating `RtkHookCoexistence.cs`.

`CodexIntegrator` — add `IHookApprovalInspector` to its base list and:

```csharp
    /// <inheritdoc/>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory)
    {
        var configPath = Path.Combine(home.CodexDir, "config.toml");
        var config = CodexConfig.Load(configPath);

        if (!config.IsReadable)
        {
            return
            [
                new HookApprovalFinding("hook approval", false,
                    $"{configPath} could not be read, so dtk cannot tell whether Codex will run this hook")
            ];
        }

        var findings = new List<HookApprovalFinding>
        {
            config.HasHookApproval(installation.RegistrationPath)
                ? new HookApprovalFinding("hook approval", true,
                    $"approval recorded in {configPath} (dtk cannot tell whether it matches the current definition)")
                : new HookApprovalFinding("hook approval", false,
                    "not yet approved — Codex skips this hook until you review it under /hooks")
        };

        if (installation.Scope == HookScope.Project && !config.TrustsProject(projectDirectory))
        {
            findings.Add(new HookApprovalFinding("project trust", false,
                $"Codex reads {Path.GetDirectoryName(installation.RegistrationPath)} only in trusted projects — trust this project when Codex asks"));
        }

        return findings;
    }
```

`HookHealthChecker.cs` — in `RunAsync`, call `CheckAsync(integrator, installation, registration, projectDirectory, cancellationToken)`;
change `CheckAsync`'s signature to take `IHookIntegrator integrator` first and `string projectDirectory` after
`registration`, and its `Current` arm to:

```csharp
            RegistrationKind.Current =>
            [
                new DiagnosticCheck(name, true, "registered"),
                await ProbeAsync(installation, cancellationToken).ConfigureAwait(false),
                .. ApprovalChecks(integrator, installation, projectDirectory)
            ],
```

and add:

```csharp
    /// <summary>The harness's approval requirements for a current registration, as checks; empty for most harnesses.</summary>
    /// <param name="integrator">The provider that described the installation.</param>
    /// <param name="installation">The registered installation.</param>
    /// <param name="projectDirectory">The directory to treat as the project root.</param>
    private static IReadOnlyList<DiagnosticCheck> ApprovalChecks(
        IHookIntegrator integrator, HookInstallation installation, string projectDirectory)
    {
        if (integrator is not IHookApprovalInspector inspector)
        {
            return [];
        }

        return
        [
            .. inspector.InspectApproval(installation, projectDirectory).Select(finding => finding.Satisfied
                ? new DiagnosticCheck(CheckName(installation, finding.Label), true, finding.Message)
                : DiagnosticCheck.Warning(CheckName(installation, finding.Label), finding.Message))
        ];
    }
```

`CheckName` produces `"codex hook approval (project)"` from label `"hook approval"`.

- [ ] **Step 4: Run tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~CodexConfigTests|FullyQualifiedName~HookHealthCheckerTests|FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: doctor warns until Codex has approved dtk's hook and trusts the project"
```

---

### Task 10: CLI surface — completions, help, and the per-shell hook check

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs:37,103-112,163-170,196`
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/InitCommandSettings.cs:11-12,26-27`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs:88-100`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt`
- Modify: `eng/hooks/check-hook-shells.sh`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`

**Interfaces:** none beyond strings.

- [ ] **Step 1: Write the failing test**

In `CompletionCommandTests`:

```csharp
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("powershell")]
    public async Task ExecuteAsync_EveryShell_CompletesTheCodexProvider(string shell)
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings { Shell = shell }, CancellationToken.None);

        writer.ToString().Should().Contain("codex");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests"`
Expected: FAIL for all four shells.

- [ ] **Step 3: Implement**

`CompletionCommand.cs`:
- bash: `local init_providers="claude copilot copilot-cli gemini codex cursor windsurf aider jetbrains"`
- zsh, after the gemini line: `'codex:Install dtk hook and instructions for Codex CLI'`
- fish, after the gemini line:
  `complete -c dtk -f -n '__fish_seen_subcommand_from init integrate' -a codex     -d 'Install dtk hook and instructions for Codex CLI'`
- PowerShell: `$providers = @('claude', 'copilot', 'copilot-cli', 'gemini', 'codex', 'cursor', 'windsurf', 'aider', 'jetbrains')`

`InitCommandSettings.cs`: provider description
`"AI assistant provider to set up (claude, copilot, copilot-cli, gemini, codex, cursor, windsurf, aider, jetbrains)"`;
global description `"Install into the user's home config (claude, copilot-cli, gemini, codex, aider) instead of a project"`.

`CliConfigurator.cs`, after `.WithExample(InitCommand, "gemini")`:

```csharp
            .WithExample(InitCommand, "codex")
            .WithExample(InitCommand, "codex", "--global")
```

`eng/hooks/check-hook-shells.sh` — after the `copilot.json` payload line:

```sh
printf '%s' '{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet build"}}' > "$work/codex.json"
```

and before the `failures` summary:

```sh
# Codex CLI: the session's shell without a login (sh/bash -c on Unix, powershell -NoProfile -Command on Windows); the
# bare command, because Codex runs the original command when a hook fails.
check "codex, bash" "$work/codex.json" rewrite "$with_dtk" "$bash_cmd" -c 'dtk hook codex'
for ps in "$pwsh_cmd" "$powershell_cmd"; do
    [ -n "$ps" ] || continue
    check "codex, $(basename "$ps")" "$work/codex.json" rewrite "$with_dtk" "$ps" -NoProfile -Command 'dtk hook codex'
done
```

- [ ] **Step 4: Run tests and accept the help snapshot**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests|FullyQualifiedName~CliConfiguratorTests"`
Expected: completion tests PASS; `Configure_InitHelp_MatchesSnapshot` FAILS and writes a `.received.txt` beside the
`.verified.txt`. Check its diff shows only the two new examples and the two description changes, then move the
`.received.txt` over the `.verified.txt` and re-run: PASS.

Run: `dtk dotnet build src/DotnetTokenKiller.Cli -c Release` then
`sh eng/hooks/check-hook-shells.sh src/DotnetTokenKiller.Cli/bin/Release/net10.0`
Expected: every line `ok`, including `codex, bash` (and `codex, pwsh` if pwsh is installed).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli eng/hooks/check-hook-shells.sh tests/DotnetTokenKiller.Cli.IntegrationTests
git commit -m "feat: complete, document and shell-check the codex provider"
```

---

### Task 11: Documentation

**Files:**
- Modify: `docs/articles/ai-agent-setup.md` (global block at lines 13-20; new section before `## Cursor`)
- Modify: `docs/articles/usage.md:155-164`
- Modify: `docs/index.md:89-97`
- Modify: `README.md:88,203-233`
- Modify: `src/DotnetTokenKiller.Cli/README.md:70,84-86`
- Modify: `CLAUDE.md:82-84`

- [ ] **Step 1: `docs/articles/ai-agent-setup.md`**

In *Installing globally*, add `dtk init codex       --global   # ~/.codex (or $CODEX_HOME), ~/.agents/skills` after
the gemini line, and change the sentence to name **claude**, **gemini**, **codex**, **aider**, and **copilot-cli**.

Insert before `## Cursor`:

````markdown
## Codex CLI

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`. It needs
Codex CLI 0.131 or later, the first release whose hooks can change a command.

### Installation

From your project root, run:

```sh
dtk init codex
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet; an existing `AGENTS.md`
  gets the section only when you pass `--force`, and keeps the rest of its content
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill, which Codex loads when it is relevant
- `.codex/hooks.json` — registers `dtk hook codex` under `PreToolUse` (merges with any existing hooks)

`dtk init codex --global` writes `~/.codex/AGENTS.md` and `~/.codex/hooks.json` (under `$CODEX_HOME` when it is
set) and `~/.agents/skills/dotnet-token-killer/SKILL.md`. The `AGENTS.md` section and the skill are the same ones
`dtk init opencode` and `dtk init antigravity` write, so running several of them leaves one copy of each.

### Approving the hook

Codex runs a hook only after you approve that exact definition. Open Codex after `dtk init codex`: it lists the new
hook for review at startup, and `/hooks` shows it at any time. Until you approve it, `codex exec` skips the hook
without saying so and commands run unrewritten. Codex also reads a project's `.codex/` folder only once you trust the
project. `dtk doctor` warns while either is missing.

dtk does not approve the hook for you: the approval is Codex's record that you reviewed what runs before every shell
command.

### How It Works

Before each shell command, Codex sends it to `dtk hook codex`, which replies with `dtk dotnet …` for a matching
`dotnet …` command. Codex still applies its approval policy and sandbox to the rewritten command.

### Manual Installation

Add the following to `.codex/hooks.json` (or `~/.codex/hooks.json`). Codex runs the original command when a hook
fails, so the command needs no guard:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook codex",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

Then approve it in Codex under `/hooks`.

````

- [ ] **Step 2: `docs/articles/usage.md`**

Add `dtk init copilot-cli # GitHub Copilot CLI hook + instructions section` after the copilot line and
`dtk init codex       # Codex CLI hook + AGENTS.md section + skill` after the gemini line; name **codex** in the
`--global` sentence.

- [ ] **Step 3: `docs/index.md`**

Add `<span class="dtk-agent">Codex CLI</span>` after the Gemini CLI span.

- [ ] **Step 4: `README.md` and `src/DotnetTokenKiller.Cli/README.md`**

- Feature line (both files): `9 AI agent integrations — Claude Code, GitHub Copilot, GitHub Copilot CLI, Gemini CLI, Codex CLI, Cursor, Windsurf, Aider, JetBrains AI`
  (keep each file's bold/plain style).
- `README.md` global block: add `dtk init codex       --global   # ~/.codex, ~/.agents/skills`; name **codex** in the
  supported list.
- `README.md` table, after Gemini CLI:
  `| **Codex CLI**          | `dtk init codex`       | PreToolUse hook in .codex/hooks.json running dtk hook codex, AGENTS.md section, skill (approve it under /hooks) |`
- `src/DotnetTokenKiller.Cli/README.md`: `--global` is supported for `claude`, `gemini`, `codex`, `aider`, and `copilot-cli`.

- [ ] **Step 5: `CLAUDE.md`**

After the `dtk init copilot-cli` paragraph add:

```markdown
`dtk init codex` writes a shared `AGENTS.md` section, the `.agents/skills/dotnet-token-killer` skill and a
`PreToolUse` hook in `.codex/hooks.json` (`--global`: `$CODEX_HOME` or `~/.codex`, and `~/.agents/skills`). Codex runs
the hook only after the user approves it under `/hooks`, keyed by a hash of the definition, so `dtk hook codex` and its
`timeout` must never change; `dtk doctor` warns until `config.toml` records an approval.
```

- [ ] **Step 6: Run the docs binding tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add docs/articles docs/index.md README.md src/DotnetTokenKiller.Cli/README.md CLAUDE.md
git commit -m "docs: document dtk init codex and the hook approval step"
```

---

### Task 12: Gate C — verify against a real Codex CLI

Nothing here is committed except the results, which go in the PR description. If C1 fails, **stop and report to the
user** before anything else.

**Files:** scratch only, under `/tmp/codex-gate/`.

- [ ] **Step 1: Install Codex and a local dtk**

```bash
mkdir -p /tmp/codex-gate/home && cd /tmp/codex-gate
npm install --prefix /tmp/codex-gate @openai/codex
/tmp/codex-gate/node_modules/.bin/codex --version
```

Expected: a version of 0.131.0 or later; record it. Put this branch's dtk first on `PATH`:
`dtk dotnet build src/DotnetTokenKiller.Cli -c Release` and
`export PATH="$PWD/src/DotnetTokenKiller.Cli/bin/Release/net10.0:$PATH"` (from the repo root), then `dtk hook codex </dev/null` prints the usage line.

- [ ] **Step 2: Start a fake Responses endpoint**

Write `/tmp/codex-gate/fake-responses.mjs`. It answers a request with no `function_call_output` by calling the shell
tool once, and any later request with a final message. The event shapes follow
`codex-rs/core/tests/common/responses.rs` (`ev_response_created`, `ev_function_call`, `ev_completed`, `sse`):

```js
import { createServer } from "node:http";
import { appendFileSync } from "node:fs";

const sse = (events) => events.map((e) => `event: ${e.type}\ndata: ${JSON.stringify(e)}\n\n`).join("");
const usage = { input_tokens: 0, input_tokens_details: null, output_tokens: 0, output_tokens_details: null, total_tokens: 0 };
let counter = 0;

createServer((req, res) => {
  let body = "";
  req.on("data", (chunk) => (body += chunk));
  req.on("end", () => {
    appendFileSync("/tmp/codex-gate/requests.jsonl", `${req.method} ${req.url} ${body}\n`);
    if (!req.url.endsWith("/responses")) {
      res.writeHead(404).end();
      return;
    }
    const id = `resp_${++counter}`;
    const item = body.includes("function_call_output")
      ? { type: "message", role: "assistant", id: `msg_${counter}`, content: [{ type: "output_text", text: "done" }] }
      : { type: "function_call", call_id: "call_1", name: process.env.GATE_TOOL ?? "exec_command",
          arguments: process.env.GATE_ARGS ?? JSON.stringify({ cmd: "dotnet build --help" }) };
    res.writeHead(200, { "content-type": "text/event-stream" });
    res.end(sse([
      { type: "response.created", response: { id } },
      { type: "response.output_item.done", item },
      { type: "response.completed", response: { id, usage } },
    ]));
  });
}).listen(18080, "127.0.0.1");
```

Run it in the background: `node /tmp/codex-gate/fake-responses.mjs &`.

Write `/tmp/codex-gate/home/config.toml`:

```toml
model = "gpt-5-codex"
model_provider = "fake"
approval_policy = "untrusted"

[model_providers.fake]
name = "fake"
base_url = "http://127.0.0.1:18080/v1"
wire_api = "responses"
```

If the first logged request's `tools` array offers a different shell tool than `exec_command`, or its parameters are
not `cmd`, restart the server with `GATE_TOOL`/`GATE_ARGS` set to match (e.g. `shell` with
`{"command":["bash","-lc","dotnet build --help"]}`).

- [ ] **Step 3: C2 — the hook receives the documented payload and the rewrite runs**

Create a scratch project `/tmp/codex-gate/project` (`git init`), run `dtk init codex --dir /tmp/codex-gate/project`,
then for this check only change the command in its `.codex/hooks.json` to
`tee -a /tmp/codex-gate/payloads.jsonl | dtk hook codex`. Run:

```bash
cd /tmp/codex-gate/project
CODEX_HOME=/tmp/codex-gate/home /tmp/codex-gate/node_modules/.bin/codex exec \
  --dangerously-bypass-hook-trust -c approval_policy='"never"' "build it"
```

Expected: `payloads.jsonl` holds `"tool_name":"Bash"` and a string `"tool_input":{"command":"dotnet build --help"…}`;
the second logged request's `function_call_output` is `dtk`'s filtered help output, not the raw SDK banner. Restore
the registered command afterwards (`dtk init codex --dir … --force` or edit it back). If the payload shape differs,
fix `ReplyToCodex` and its tests before continuing.

- [ ] **Step 4: C1 — the rewrite does not bypass the approval policy**

```bash
CODEX_HOME=/tmp/codex-gate/home /tmp/codex-gate/node_modules/.bin/codex exec \
  --dangerously-bypass-hook-trust -c approval_policy='"untrusted"' "build it"
```

Expected: `dtk dotnet build --help` is **not** executed without approval — the second logged request's
`function_call_output` reports the command was rejected or needs approval, exactly as the same run does with the
hook removed from `hooks.json`. Also run the interactive TUI once (`CODEX_HOME=… codex "build it"` in a terminal) and
confirm the approval prompt shows `dtk dotnet build --help`.
**If the rewritten command runs without approval, stop: report to the user that `permissionDecision: "allow"`
escalates, and do not open the PR.**

- [ ] **Step 5: C3 — approval key and doctor**

In the interactive TUI, approve the hook under `/hooks`, and trust the project when asked. Then inspect
`/tmp/codex-gate/home/config.toml`: record the exact `[hooks.state."…"]` key and `[projects."…"]` key. Run
`CODEX_HOME=/tmp/codex-gate/home dtk doctor` from the project.
Expected: `codex hook approval (project)` and `codex project trust (project)` pass with no warnings. If a key's path
form differs from `CodexConfig` (e.g. a canonicalized `/private/tmp` on macOS, or a group/handler suffix that is not
`:pre_tool_use:`), adjust `CodexConfig` and `CodexConfigTests` to the recorded form and commit that fix.

- [ ] **Step 6: Record the results**

Write the Codex version, the payload recorded in C2, the C1 outcome (both `exec` and TUI), and the two keys from C3
into a `## Gate C` section of the PR description draft.

---

### Task 13: Full verification and pull request

- [ ] **Step 1: Format and build**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore`, then
`dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
Expected: no changes reported.

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Test**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests`
Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Hook|FullyQualifiedName~InitCommand|FullyQualifiedName~Doctor|FullyQualifiedName~Completion|FullyQualifiedName~CliConfigurator|FullyQualifiedName~DocsBinding"`
Expected: PASS. Build-spawning suites are left to CI (Global Constraints).

- [ ] **Step 3: Smoke-test end to end**

```bash
tmp=$(mktemp -d) && dotnet src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll init codex --dir "$tmp"
dotnet src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll init codex --dir "$tmp"
```

Expected: the first run prints three `created` lines and both notes; the second prints three `unchanged` lines and
"Already integrated. Nothing to do." (Use `dotnet …/dtk.dll`, not `dtk`, and never wrap a `dotnet build` in it: the
global hook rewrites nested dotnet commands.)

- [ ] **Step 4: Push and open the PR — confirm with the user first**

Ask the user before pushing. Then:

```bash
git push -u origin feat/codex-opencode-antigravity
gh pr create --base develop --title "feat: dtk init codex and dtk hook codex" --body-file <draft>
```

The body summarizes the spec's PR 1 scope, links the spec and this plan, includes the `## Gate C` section from Task
12, and ends with:

```
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Expected: CI (`ci.yml`, including `fallback-package.yml`'s `check-hook-shells.sh` on Windows) goes green. Wait for the
merge before starting `docs/superpowers/plans/2026-09-15-opencode-integration.md`.

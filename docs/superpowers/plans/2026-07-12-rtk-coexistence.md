# rtk-aware `dtk integrate claude` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When `dtk integrate claude` runs on a machine that also runs the rtk PreToolUse hook, detect that hook and auto-exclude `dotnet` from rtk's config so dtk becomes the sole owner of `dotnet build/test/restore/clean/format`.

**Architecture:** A new `RtkHookCoexistence` collaborator (Application layer) detects the rtk hook across the four Claude settings files, then merges `"dotnet"` into `[hooks].exclude_commands` in `~/.config/rtk/config.toml` (Tomlyn for parse/classify, targeted text edits for the write so foreign comments survive). `ClaudeCodeIntegrator` invokes it and funnels the outcome (created/updated config path + advisory notes) into the existing `IntegrationContext`. A new `Notes` channel on `IntegrationResult` carries the "why" to the CLI, which renders it.

**Tech Stack:** .NET 10, C#, Tomlyn 2.10.1, System.Text.Json.Nodes, xunit + FluentAssertions, Spectre.Console (CLI rendering).

## Global Constraints

- Target framework: `net10.0`.
- `TreatWarningsAsErrors` is enabled — all Roslynator / SonarAnalyzer / NetAnalyzers warnings must be resolved. Prefer `[GeneratedRegex]` partial methods over inline `new Regex(...)`.
- File-scoped namespaces (`namespace Foo;`); `var` preferred; private fields `_camelCase`; async methods end in `Async`; interfaces `IPascalCase`; type parameters `TPascalCase`.
- Central package versions only: version numbers live in `Directory.Packages.props`; `.csproj` `PackageReference` entries omit `Version`.
- Line endings LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`.
- Build/test via dtk: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`, single test `dtk dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.
- rtk config target path: `~/.config/rtk/config.toml` (honor `XDG_CONFIG_HOME`). User Claude settings dir: `~/.claude`.
- Design doc: `docs/superpowers/specs/2026-07-12-rtk-coexistence-design.md` (gitignored — local only).
- Branch: `feature/rtk-coexistence` (already created off `develop`).

---

## File Structure

**New:**
- `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs` — detection + rtk config reconcile. One responsibility: rtk coexistence.
- `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs` — unit tests for detection + reconcile.

**Modified:**
- `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs` — add `Notes` (4th positional, with 3-arg chaining ctor).
- `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs` — add `Notes` accumulator; pass to `ToResult()`.
- `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` — primary-constructor injection of `RtkHookCoexistence`; invoke after writing artifacts.
- `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs` — render `result.Notes`; make `RelativePath` return an absolute path for files outside the project.
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` — register `RtkHookCoexistence`.
- `Directory.Packages.props` — add Tomlyn 2.10.1.
- `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj` — reference Tomlyn.
- `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs` — construct SUT with isolated temp rtk/user paths; add an rtk-present integration case.

---

## Task 1: `Notes` channel end-to-end (no rtk yet)

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs`

**Interfaces:**
- Produces: `IntegrationResult(IReadOnlyList<string> CreatedFiles, IReadOnlyList<string> UpdatedFiles, IReadOnlyList<string> SkippedFiles, IReadOnlyList<string> Notes)` plus a 3-arg chaining ctor defaulting `Notes` to `[]`. `IntegrationContext.Notes` (`List<string>`).

- [ ] **Step 1: Write the failing test** (append to `IntegrateCommandTests.cs`)

```csharp
[Fact]
public async Task RunAsync_ResultWithNotes_RendersNoteLine()
{
    var console = new TestConsole();
    var result = new IntegrationResult([], [], [], ["excluded dotnet in rtk config"]);
    var command = new IntegrateCommand(FakeUseCaseReturning(result), console);

    await command.RunAsync(new IntegrateCommandSettings { Provider = "claude" }, CancellationToken.None);

    console.Output.Should().Contain("excluded dotnet in rtk config");
}
```

If the test file has no `FakeUseCaseReturning` helper, reuse whatever construction the existing tests in this file use to build an `IntegrateCommand` with a stubbed `IntegrateUseCase` returning `result`; mirror the nearest existing test (e.g. the one around line 17) exactly rather than inventing a new pattern.

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test --filter "FullyQualifiedName~IntegrateCommandTests.RunAsync_ResultWithNotes_RendersNoteLine"`
Expected: FAIL — `IntegrationResult` has no 4-arg constructor (compile error) or the note is not in output.

- [ ] **Step 3: Add `Notes` to `IntegrationResult`**

Replace the record body in `IntegrationResult.cs` with:

```csharp
namespace DotnetTokenKiller.Domain.Integration;

/// <summary>Describes which files were affected during an integration.</summary>
/// <param name="CreatedFiles">Files written for the first time.</param>
/// <param name="UpdatedFiles">Existing files that were modified.</param>
/// <param name="SkippedFiles">Existing files that were left unchanged (use --force to overwrite).</param>
/// <param name="Notes">Advisory messages explaining cross-tool actions (e.g. an rtk config edit).</param>
public sealed record IntegrationResult(
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> UpdatedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<string> Notes)
{
    /// <summary>Creates a result with no advisory notes.</summary>
    public IntegrationResult(
        IReadOnlyList<string> createdFiles,
        IReadOnlyList<string> updatedFiles,
        IReadOnlyList<string> skippedFiles)
        : this(createdFiles, updatedFiles, skippedFiles, [])
    {
    }
}
```

- [ ] **Step 4: Add `Notes` to `IntegrationContext`**

In `IntegrationContext.cs`, add the property and thread it through `ToResult()`:

```csharp
/// <summary>Gets advisory messages accumulated during this integration run.</summary>
internal List<string> Notes { get; } = [];
```

```csharp
internal IntegrationResult ToResult()
{
    return new IntegrationResult(Created, Updated, Skipped, Notes);
}
```

- [ ] **Step 5: Render notes in `IntegrateCommand`**

In `IntegrateCommand.RunAsync`, immediately **after** the `foreach (var file in result.SkippedFiles)` loop and **before** `PrintSummary(...)`, add:

```csharp
foreach (var note in result.Notes)
{
    console.MarkupLine($"[cyan]note[/]     {Markup.Escape(note)}");
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dtk dotnet test --filter "FullyQualifiedName~IntegrateCommandTests.RunAsync_ResultWithNotes_RendersNoteLine"`
Expected: PASS.

- [ ] **Step 7: Build to confirm no analyzer regressions**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs \
        src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs \
        src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs \
        tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs
git commit -m "feat: add advisory Notes channel to integration result"
```

---

## Task 2: rtk hook detection

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs`

**Interfaces:**
- Produces:
  - `public RtkHookCoexistence()` — real paths from `Environment`.
  - `internal RtkHookCoexistence(string userClaudeDir, string rtkConfigPath)` — test seam (Application.Tests already has `InternalsVisibleTo`).
  - `internal bool IsRtkHookPresent(string projectDirectory)` — scans user + project settings.

- [ ] **Step 1: Write the failing tests** (`RtkHookCoexistenceTests.cs`)

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class RtkHookCoexistenceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dtk-rtk-{Guid.NewGuid()}");
    private string UserClaudeDir => Path.Combine(_tempRoot, "home", ".claude");
    private string ProjectDir => Path.Combine(_tempRoot, "project");
    private string RtkConfigPath => Path.Combine(_tempRoot, "config", "rtk", "config.toml");

    private RtkHookCoexistence CreateSut() => new(UserClaudeDir, RtkConfigPath);

    private async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Fact]
    public void IsRtkHookPresent_NoSettingsFiles_ReturnsFalse()
    {
        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task IsRtkHookPresent_RtkHookInUserSettings_ReturnsTrue()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "rtk hook claude" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task IsRtkHookPresent_RtkHookInProjectLocalSettings_ReturnsTrue()
    {
        await WriteAsync(Path.Combine(ProjectDir, ".claude", "settings.local.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "/usr/local/bin/rtk hook claude" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task IsRtkHookPresent_OnlyDtkHook_ReturnsFalse()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task IsRtkHookPresent_MalformedSettings_IsIgnored()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), "NOT JSON {{{");

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: FAIL — `RtkHookCoexistence` does not exist (compile error).

- [ ] **Step 3: Create `RtkHookCoexistence.cs` with detection only**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Detects an rtk PreToolUse hook and reconciles rtk's config so that dtk — not rtk — owns
/// <c>dotnet build/test/restore/clean/format</c> once dtk is integrated with Claude Code.
/// </summary>
internal sealed partial class RtkHookCoexistence
{
    private readonly string _userClaudeDir;
    private readonly string _rtkConfigPath;

    /// <summary>Creates an instance rooted at the real user Claude dir and rtk config path.</summary>
    public RtkHookCoexistence()
        : this(DefaultUserClaudeDir(), DefaultRtkConfigPath())
    {
    }

    /// <summary>Test seam: inject isolated paths so tests never touch the real machine.</summary>
    internal RtkHookCoexistence(string userClaudeDir, string rtkConfigPath)
    {
        _userClaudeDir = userClaudeDir;
        _rtkConfigPath = rtkConfigPath;
    }

    /// <summary>
    /// True when any of the user or project Claude settings files registers a PreToolUse hook whose
    /// command invokes <c>rtk … hook</c>.
    /// </summary>
    internal bool IsRtkHookPresent(string projectDirectory)
    {
        string[] candidates =
        [
            Path.Combine(_userClaudeDir, "settings.json"),
            Path.Combine(_userClaudeDir, "settings.local.json"),
            Path.Combine(projectDirectory, ".claude", "settings.json"),
            Path.Combine(projectDirectory, ".claude", "settings.local.json"),
        ];

        return candidates.Any(FileRegistersRtkHook);
    }

    private static bool FileRegistersRtkHook(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            var preToolUse = (root?["hooks"] as JsonObject)?["PreToolUse"] as JsonArray;
            if (preToolUse is null)
            {
                return false;
            }

            foreach (var entry in preToolUse)
            {
                if (entry?["hooks"] is not JsonArray hooks)
                {
                    continue;
                }

                foreach (var hook in hooks)
                {
                    if (hook?["command"] is JsonValue value
                        && value.TryGetValue<string>(out var command)
                        && RtkHookCommandRegex().IsMatch(command))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Tolerate malformed settings — never fail integration over another tool's file.
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string DefaultUserClaudeDir() =>
        Path.Combine(Home(), ".claude");

    private static string DefaultRtkConfigPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".config") : xdg;
        return Path.Combine(configHome, "rtk", "config.toml");
    }

    [GeneratedRegex(@"(?:^|[\s/\\""'])rtk(?:\.exe)?\s+hook\b")]
    private static partial Regex RtkHookCommandRegex();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: PASS (all 5 detection tests).

- [ ] **Step 5: Build**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs
git commit -m "feat: detect rtk PreToolUse hook across user and project settings"
```

---

## Task 3: rtk config reconcile (Tomlyn)

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj`
- Modify: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed record RtkReconcileOutcome(string? CreatedConfigPath, string? UpdatedConfigPath, IReadOnlyList<string> Notes)` with `static RtkReconcileOutcome None`.
  - `internal Task<RtkReconcileOutcome> ReconcileRtkConfigAsync(CancellationToken cancellationToken)` — reconcile the config file only (assumes rtk hook already detected).
  - `public Task<RtkReconcileOutcome> ReconcileAsync(string projectDirectory, CancellationToken cancellationToken)` — detect + reconcile; returns `None` if no rtk hook.

- [ ] **Step 1: Add Tomlyn to central package management**

In `Directory.Packages.props`, add inside the `<ItemGroup>` (alphabetical-ish, near the other packages):

```xml
    <PackageVersion Include="Tomlyn" Version="2.10.1"/>
```

- [ ] **Step 2: Reference Tomlyn from the Application project**

In `src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj`, add to the first `<ItemGroup>` of `PackageReference`s:

```xml
    <PackageReference Include="Tomlyn"/>
```

- [ ] **Step 3: Write the failing reconcile tests** (append to `RtkHookCoexistenceTests.cs`)

```csharp
[Fact]
public async Task ReconcileRtkConfig_MissingFile_CreatesWithDotnetExcluded()
{
    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.CreatedConfigPath.Should().Be(RtkConfigPath);
    outcome.Notes.Should().ContainSingle();
    var toml = await File.ReadAllTextAsync(RtkConfigPath);
    toml.Should().Contain("[hooks]");
    toml.Should().Contain("exclude_commands = [\"dotnet\"]");
}

[Fact]
public async Task ReconcileRtkConfig_HooksTableWithoutKey_AddsKey()
{
    await WriteAsync(RtkConfigPath, "# my rtk config\n[hooks]\ntransparent_prefixes = []\n");

    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
    var toml = await File.ReadAllTextAsync(RtkConfigPath);
    toml.Should().Contain("# my rtk config");           // comment preserved
    toml.Should().Contain("transparent_prefixes = []"); // sibling key preserved
    toml.Should().Contain("exclude_commands = [\"dotnet\"]");
}

[Fact]
public async Task ReconcileRtkConfig_ExistingArrayWithoutDotnet_AppendsDotnet()
{
    await WriteAsync(RtkConfigPath, "[hooks]\nexclude_commands = [\"git\", \"npm\"]\n");

    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
    var toml = await File.ReadAllTextAsync(RtkConfigPath);
    toml.Should().Contain("\"git\"");
    toml.Should().Contain("\"npm\"");
    toml.Should().Contain("\"dotnet\"");
}

[Fact]
public async Task ReconcileRtkConfig_DotnetAlreadyExcluded_IsNoOp()
{
    const string original = "[hooks]\nexclude_commands = [\"dotnet\", \"git\"]\n";
    await WriteAsync(RtkConfigPath, original);

    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.CreatedConfigPath.Should().BeNull();
    outcome.UpdatedConfigPath.Should().BeNull();
    outcome.Notes.Should().BeEmpty();
    (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
}

[Fact]
public async Task ReconcileRtkConfig_NoHooksTable_AppendsSection()
{
    await WriteAsync(RtkConfigPath, "[filters]\nenabled = true\n");

    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
    var toml = await File.ReadAllTextAsync(RtkConfigPath);
    toml.Should().Contain("[filters]");
    toml.Should().Contain("[hooks]");
    toml.Should().Contain("exclude_commands = [\"dotnet\"]");
}

[Fact]
public async Task ReconcileRtkConfig_MalformedToml_ReturnsAdviceWithoutThrowing()
{
    const string broken = "[hooks\nexclude_commands = [";
    await WriteAsync(RtkConfigPath, broken);

    var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

    outcome.CreatedConfigPath.Should().BeNull();
    outcome.UpdatedConfigPath.Should().BeNull();
    outcome.Notes.Should().ContainSingle(n => n.Contains("exclude_commands"));
    (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(broken); // never clobbered
}

[Fact]
public async Task ReconcileAsync_NoRtkHook_ReturnsNone()
{
    // No settings files → no rtk hook → reconcile short-circuits, config never created.
    var outcome = await CreateSut().ReconcileAsync(ProjectDir, CancellationToken.None);

    outcome.Should().Be(RtkReconcileOutcome.None);
    File.Exists(RtkConfigPath).Should().BeFalse();
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests.ReconcileRtkConfig"`
Expected: FAIL — `ReconcileRtkConfigAsync` / `RtkReconcileOutcome` do not exist.

- [ ] **Step 5: Add the outcome record and reconcile logic**

Add the `using` directives at the top of `RtkHookCoexistence.cs` (with the existing ones):

```csharp
using Tomlyn;
using Tomlyn.Model;
```

Add this record in the same file, below the class (still inside the namespace):

```csharp
/// <summary>What <see cref="RtkHookCoexistence"/> did to rtk's config, for the integration result.</summary>
internal sealed record RtkReconcileOutcome(
    string? CreatedConfigPath,
    string? UpdatedConfigPath,
    IReadOnlyList<string> Notes)
{
    /// <summary>Nothing was detected or changed.</summary>
    internal static RtkReconcileOutcome None { get; } = new(null, null, []);
}
```

Add these members inside `RtkHookCoexistence`:

```csharp
private const string ExcludedNote =
    "Detected an rtk hook: excluded `dotnet` in rtk's config so dtk owns dotnet commands. " +
    "`rtk dotnet …` still works for manual use.";

/// <summary>Detect the rtk hook and, if present, reconcile rtk's config.</summary>
public async Task<RtkReconcileOutcome> ReconcileAsync(
    string projectDirectory,
    CancellationToken cancellationToken)
{
    return IsRtkHookPresent(projectDirectory)
        ? await ReconcileRtkConfigAsync(cancellationToken).ConfigureAwait(false)
        : RtkReconcileOutcome.None;
}

/// <summary>Ensure rtk's config excludes <c>dotnet</c>, preserving all other content.</summary>
internal async Task<RtkReconcileOutcome> ReconcileRtkConfigAsync(CancellationToken cancellationToken)
{
    try
    {
        if (!File.Exists(_rtkConfigPath))
        {
            await WriteConfigAsync("[hooks]\nexclude_commands = [\"dotnet\"]\n", cancellationToken)
                .ConfigureAwait(false);
            return new RtkReconcileOutcome(_rtkConfigPath, null, [ExcludedNote]);
        }

        var text = await File.ReadAllTextAsync(_rtkConfigPath, cancellationToken).ConfigureAwait(false);
        var document = Toml.Parse(text, _rtkConfigPath);
        if (document.HasErrors)
        {
            return new RtkReconcileOutcome(null, null, [AdviceNote()]);
        }

        var model = document.ToModel();
        var hooks = model.TryGetValue("hooks", out var hooksNode) ? hooksNode as TomlTable : null;
        var excludes = hooks is not null && hooks.TryGetValue("exclude_commands", out var arrayNode)
            ? arrayNode as TomlArray
            : null;

        string updated;
        if (excludes is not null)
        {
            if (excludes.OfType<string>().Contains("dotnet"))
            {
                return RtkReconcileOutcome.None; // already excluded — stay silent
            }

            updated = ReplaceExcludeArray(text, [.. excludes.OfType<string>(), "dotnet"]);
        }
        else if (hooks is not null)
        {
            updated = InsertKeyUnderHooksHeader(text);
        }
        else
        {
            updated = text.TrimEnd('\n') + "\n\n[hooks]\nexclude_commands = [\"dotnet\"]\n";
        }

        await WriteConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        return new RtkReconcileOutcome(null, _rtkConfigPath, [ExcludedNote]);
    }
    catch (IOException)
    {
        return new RtkReconcileOutcome(null, null, [AdviceNote()]);
    }
    catch (UnauthorizedAccessException)
    {
        return new RtkReconcileOutcome(null, null, [AdviceNote()]);
    }
}

private async Task WriteConfigAsync(string content, CancellationToken cancellationToken)
{
    Directory.CreateDirectory(Path.GetDirectoryName(_rtkConfigPath)!);
    await File.WriteAllTextAsync(_rtkConfigPath, content, cancellationToken).ConfigureAwait(false);
}

private string AdviceNote() =>
    $"Could not update rtk's config at {_rtkConfigPath}. To let dtk own dotnet commands, add " +
    "under [hooks]: exclude_commands = [\"dotnet\"].";

private static string FormatStringArray(IEnumerable<string> items) =>
    "[" + string.Join(", ", items.Select(item => $"\"{item}\"")) + "]";

private static string ReplaceExcludeArray(string text, IEnumerable<string> items)
{
    var array = FormatStringArray(items);
    return ExcludeArrayRegex().Replace(
        text,
        match => match.Groups["prefix"].Value + array,
        1);
}

private static string InsertKeyUnderHooksHeader(string text) =>
    HooksHeaderRegex().Replace(text, match => match.Value + "\nexclude_commands = [\"dotnet\"]", 1);

[GeneratedRegex(@"(?m)^(?<prefix>\s*exclude_commands\s*=\s*)\[[^\]]*\]")]
private static partial Regex ExcludeArrayRegex();

[GeneratedRegex(@"(?m)^\[hooks\][^\S\n]*$")]
private static partial Regex HooksHeaderRegex();
```

- [ ] **Step 6: Run reconcile + no-hook tests**

Run: `dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: PASS (all detection + reconcile tests).

- [ ] **Step 7: Build**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props \
        src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj \
        src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs
git commit -m "feat: reconcile rtk config to exclude dotnet (Tomlyn, format-preserving)"
```

---

## Task 4: Wire into `ClaudeCodeIntegrator`, DI, and CLI path rendering

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs`

**Interfaces:**
- Consumes: `RtkHookCoexistence.ReconcileAsync(string, CancellationToken)`, `RtkReconcileOutcome`, `IntegrationContext.Notes`.
- Produces: `ClaudeCodeIntegrator(RtkHookCoexistence rtk)` primary constructor.

- [ ] **Step 1: Isolate existing tests + add rtk-present test**

In `ClaudeCodeIntegratorTests.cs`, replace the `_sut` field and add rtk temp paths so no test touches the real machine:

```csharp
private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-claude-test-{Guid.NewGuid()}");
private readonly ClaudeCodeIntegrator _sut;

public ClaudeCodeIntegratorTests()
{
    var userClaudeDir = Path.Combine(_tempDir, "isolated-home", ".claude");
    var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
    _sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath));
}
```

Add a new test at the end of the class (uses a dedicated rtk config path so it can assert the write):

```csharp
[Fact]
public async Task IntegrateAsync_RtkHookInProjectSettings_ExcludesDotnetAndAddsNote()
{
    var userClaudeDir = Path.Combine(_tempDir, "rtk-home", ".claude");
    var rtkConfigPath = Path.Combine(_tempDir, "rtk-config", "rtk", "config.toml");
    var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
    await File.WriteAllTextAsync(settingsPath, """
        { "hooks": { "PreToolUse": [ { "matcher": "Bash",
          "hooks": [ { "type": "command", "command": "rtk hook claude" } ] } ] } }
        """);
    var sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath));

    var result = await sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

    result.Notes.Should().ContainSingle(n => n.Contains("dotnet"));
    result.CreatedFiles.Should().Contain(rtkConfigPath);
    (await File.ReadAllTextAsync(rtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test --filter "FullyQualifiedName~ClaudeCodeIntegratorTests.IntegrateAsync_RtkHookInProjectSettings_ExcludesDotnetAndAddsNote"`
Expected: FAIL — `ClaudeCodeIntegrator` has no constructor taking `RtkHookCoexistence` (compile error).

- [ ] **Step 3: Inject and invoke `RtkHookCoexistence` in `ClaudeCodeIntegrator`**

Change the class declaration to a primary constructor:

```csharp
public sealed class ClaudeCodeIntegrator(RtkHookCoexistence rtk) : IProviderIntegrator
```

At the end of `IntegrateAsync`, replace `return context.ToResult();` with:

```csharp
        var rtkOutcome = await rtk.ReconcileAsync(directory, cancellationToken).ConfigureAwait(false);
        if (rtkOutcome.CreatedConfigPath is not null)
        {
            context.Created.Add(rtkOutcome.CreatedConfigPath);
        }

        if (rtkOutcome.UpdatedConfigPath is not null)
        {
            context.Updated.Add(rtkOutcome.UpdatedConfigPath);
        }

        context.Notes.AddRange(rtkOutcome.Notes);

        return context.ToResult();
```

- [ ] **Step 4: Register `RtkHookCoexistence` in DI**

In `DependencyInjection.cs`, immediately before `services.AddTransient<IProviderIntegrator, ClaudeCodeIntegrator>();` add:

```csharp
        services.AddTransient<RtkHookCoexistence>();
```

- [ ] **Step 5: Make `RelativePath` show absolute paths for out-of-project files**

In `IntegrateCommand.RelativePath`, after computing `relative` and before `return`, insert the outside-project guard:

```csharp
            var relative = Path.GetRelativePath(normalizedBaseDir, normalizedFullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                // Files outside the project (e.g. the global rtk config) read better as an
                // absolute path than as a "../../.." relative walk.
                return normalizedFullPath.Replace(Path.DirectorySeparatorChar, '/');
            }

            return relative.Replace(Path.DirectorySeparatorChar, '/');
```

- [ ] **Step 6: Run the full integrator + coexistence + command test suites**

Run: `dtk dotnet test --filter "FullyQualifiedName~ClaudeCodeIntegratorTests|FullyQualifiedName~RtkHookCoexistenceTests|FullyQualifiedName~IntegrateCommandTests"`
Expected: PASS (all).

- [ ] **Step 7: Full build + full test run**

Run: `dtk dotnet build DotnetTokenKiller.slnx` then `dtk dotnet test DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs \
        src/DotnetTokenKiller.Application/DependencyInjection.cs \
        src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs
git commit -m "feat: exclude dotnet from rtk when integrating dtk with Claude Code"
```

---

## Task 5: Documentation

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (the `<remarks>` list of created artifacts / behavior)
- Modify: `README.md` (if it documents `dtk integrate claude` — verify first)

- [ ] **Step 1: Update the `ClaudeCodeIntegrator` XML `<remarks>`**

Add a bullet after the `.claude/settings.json` item noting the rtk coexistence behavior:

```csharp
///   <item><description>
///     When an rtk PreToolUse hook is detected, merges <c>exclude_commands = ["dotnet"]</c> into
///     <c>~/.config/rtk/config.toml</c> so dtk (not rtk) owns dotnet commands. Silent if already excluded.
///   </description></item>
```

- [ ] **Step 2: Check whether README documents `integrate`**

Run: `grep -n "integrate claude" README.md docs/*.md 2>/dev/null`
Expected: either matches (edit them to mention the rtk auto-exclusion in one sentence) or no matches (skip the README edit).

If matched, add one sentence near the `dtk integrate claude` description:
> On machines that also run the rtk hook, `dtk integrate claude` automatically excludes `dotnet` from rtk so the two proxies don't both rewrite `dotnet` commands.

- [ ] **Step 3: Build + test (docs-only, sanity)**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: document rtk auto-exclusion in dtk integrate claude"
```

---

## Self-Review

**Spec coverage:**
- Auto-write rtk exclusion → Task 3 (`ReconcileRtkConfigAsync`).
- Skip if already excluded → Task 3 (`ReconcileRtkConfig_DotnetAlreadyExcluded_IsNoOp`).
- Scan user + project settings → Task 2 (`IsRtkHookPresent`, 4 candidate files).
- Tomlyn format-preserving edit → Task 3 (Tomlyn parse/classify + regex text edits; comment-preservation test).
- Notes channel + rendering → Task 1; absolute path for global file → Task 4 Step 5.
- Graceful degradation (malformed/unwritable) → Task 3 (`AdviceNote`, malformed test; IO/Unauthorized catches).
- Isolate existing tests → Task 4 Step 1.
- Wiring + DI → Task 4 Steps 3–4.

**Placeholder scan:** none — every code step contains complete code.

**Type consistency:** `RtkHookCoexistence`, `IsRtkHookPresent`, `ReconcileAsync`, `ReconcileRtkConfigAsync`, `RtkReconcileOutcome(CreatedConfigPath, UpdatedConfigPath, Notes)`, `IntegrationResult(..., Notes)`, `IntegrationContext.Notes` are used identically across Tasks 1–5.

**Note on regex analyzers:** all regexes use `[GeneratedRegex]` partial methods (class is `partial`) to satisfy SonarAnalyzer/Roslynator under `TreatWarningsAsErrors`.

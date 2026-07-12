# Global Integration + SonarQube Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in `dtk integrate <provider> --global` flag that installs integration artifacts into the user's home config (claude, gemini, aider), and resolve the 5 open SonarQube code smells so the quality gate returns to green.

**Architecture:** Global support is added as a capability interface `IGlobalIntegrator` implemented only by the three home-config-capable integrators; `IProviderIntegrator` is unchanged. A small injectable `HomePaths` seam resolves home directories (real home in production, temp dir in tests). Local (per-project) integration stays the default and is untouched.

**Tech Stack:** .NET 10, C#, Spectre.Console.Cli, xunit + FluentAssertions, `System.Text.Json.Nodes`.

## Global Constraints

- Target framework: `net10.0`.
- `TreatWarningsAsErrors` is enabled — all analyzer warnings are build errors; new code must be warning-clean.
- File-scoped namespaces; `var` preferred; private fields `_camelCase`; async methods end in `Async`; interfaces `IPascalCase`.
- Line endings LF only; no trailing whitespace; no BOM; 4-space indent for `.cs`.
- Central package versions in `Directory.Packages.props`; `.csproj` omits versions.
- Build: `dtk dotnet build DotnetTokenKiller.slnx`. Test: `dtk dotnet test DotnetTokenKiller.slnx`. Single test: `dtk dotnet test --filter "FullyQualifiedName~<Class>.<Method>"`.
- SonarQube gate thresholds (must stay green): `new_violations = 0`, `new_coverage ≥ 80%`, `new_duplicated_lines_density ≤ 3%`.
- Local integration behavior and output must remain byte-for-byte unchanged.

---

## Track A — SonarQube cleanup

These 5 tasks are independent of the feature and can land first to green the gate. Tasks A1–A3 are behavior-preserving literal extractions covered by existing tests; A4–A5 are behavior-preserving method extractions covered by existing tests.

### Task A1: S1192 — extract `"dotnet"` literal constant (ArgumentPreprocessor)

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs`
- Test (existing, must stay green): `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new (private constant, no public surface change).

- [ ] **Step 1: Baseline — run existing tests to confirm green**

Run: `dtk dotnet test --filter "FullyQualifiedName~ArgumentPreprocessorTests"`
Expected: PASS (all existing cases).

- [ ] **Step 2: Add the constant**

In `ArgumentPreprocessor.cs`, add a private constant next to the existing subcommand constants (after line 29, before the `KnownSubcommandsOrdered` block):

```csharp
    /// <summary>The <c>dotnet</c> driver command that dtk-handled invocations begin with.</summary>
    private const string DotnetCommand = "dotnet";
```

- [ ] **Step 3: Replace the 5 string literals**

Replace every `"dotnet"` literal in this file with `DotnetCommand`. The occurrences are:
- line 72: `!string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||`
- line 81: `if (string.Equals(args[0], DotnetCommand, StringComparison.Ordinal) &&`
- line 88: `normalized[0] = DotnetCommand;`
- line 101: `string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) &&`
- line 118: `!string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||`

- [ ] **Step 4: Build + run tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~ArgumentPreprocessorTests"`
Expected: PASS, no new warnings.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs
git commit -m "refactor: extract 'dotnet' literal constant (sonar S1192)"
```

---

### Task A2: S1192 — extract `"duration"` group-name constant (DotnetTestFilter)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs`
- Test (existing, must stay green): `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new (private constant).

- [ ] **Step 1: Baseline tests**

Run: `dtk dotnet test --filter "FullyQualifiedName~DotnetTestFilterTests"`
Expected: PASS.

- [ ] **Step 2: Add the constant**

Add a private constant near the top of the `DotnetTestFilter` class body (with the other private members):

```csharp
    /// <summary>Name of the regex capture group holding a test/summary duration.</summary>
    private const string DurationGroup = "duration";
```

- [ ] **Step 3: Replace the 4 literals**

Replace the `"duration"` regex-group lookups (do NOT touch the group *definitions* inside `[GeneratedRegex]` pattern strings — those are regex syntax, not the flagged literal):
- line 87: `var duration = failedHeaderMatch.Groups[DurationGroup].Value;`
- line 115: `var duration = failedMatch.Groups[DurationGroup].Value.Trim();`
- line 215: `state.TotalDurationMs += ParseDurationToMs(summaryMatch.Groups[DurationGroup].Value);`
- line 232: `var durationGroup = summaryMatch.Groups[DurationGroup];`

- [ ] **Step 4: Build + tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~DotnetTestFilterTests"`
Expected: PASS, no new warnings.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs
git commit -m "refactor: extract 'duration' regex group-name constant (sonar S1192)"
```

---

### Task A3: S1192 — extract `"hooks"` literal constant (RtkHookCoexistence)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs`
- Test (existing, must stay green): `tests/DotnetTokenKiller.Application.Tests/Integration/RtkHookCoexistenceTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new (private constant).

- [ ] **Step 1: Baseline tests**

Run: `dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: PASS.

- [ ] **Step 2: Add the constant**

Add near the existing private constants (after `ExcludedNote`, around line 17):

```csharp
    /// <summary>The <c>hooks</c> key/table name used in both rtk's TOML config and Claude's JSON settings.</summary>
    private const string HooksKey = "hooks";
```

- [ ] **Step 3: Replace the 4 literals**

- line 91: `var hooks = model.TryGetValue(HooksKey, out var hooksNode) ? hooksNode as TomlTable : null;`
- line 144: `var hooks = model.TryGetValue(HooksKey, out var hooksNode) ? hooksNode as TomlTable : null;`
- line 188: `if (root?[HooksKey] is not JsonObject hooksObject || hooksObject["PreToolUse"] is not JsonArray preToolUse)`
- line 195: `if (entry?[HooksKey] is not JsonArray hooks)`

- [ ] **Step 4: Build + tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~RtkHookCoexistenceTests"`
Expected: PASS, no new warnings.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/RtkHookCoexistence.cs
git commit -m "refactor: extract 'hooks' literal constant (sonar S1192)"
```

---

### Task A4: S3776 — reduce `FilteredRunUseCase.RunAsync` cognitive complexity (17 → ≤15)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs:34-121`
- Test (existing, must stay green): `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: two new private methods on `FilteredRunUseCase`:
  - `private async Task<string> ApplyFilterSafelyAsync(IOutputFilter filter, string stripped, int exitCode, int verbosityLevel)`
  - `private static string NormalizeGlyphs(string filtered, DtkConfig config)`

- [ ] **Step 1: Baseline tests**

Run: `dtk dotnet test --filter "FullyQualifiedName~FilteredRunUseCaseTests"`
Expected: PASS.

- [ ] **Step 2: Extract the filter try/catch (lines 66-80) into a method**

Replace lines 66-80 (the `string filtered; try { ... } catch { ... }` block) with a single call:

```csharp
        var filtered = await ApplyFilterSafelyAsync(filter, stripped, result.ExitCode, verbosityLevel)
            .ConfigureAwait(false);
```

Add this method (place it just below `RunAsync`, above `BuildRawTailFallback`):

```csharp
    /// <summary>Applies the filter, falling back to raw output if the filter throws (never breaks the workflow).</summary>
    private async Task<string> ApplyFilterSafelyAsync(
        IOutputFilter filter,
        string stripped,
        int exitCode,
        int verbosityLevel)
    {
        try
        {
            return filter.Apply(stripped, exitCode);
        }
        catch
        {
            // Intentional: filter errors must not break the user's workflow
            if (verbosityLevel >= 2)
            {
                await output.WriteLineAsync("[filter error — using raw output]").ConfigureAwait(false);
            }

            return stripped;
        }
    }
```

- [ ] **Step 3: Extract the glyph-normalization block (lines 93-98) into a method**

Replace lines 93-98 (the `if (!config.Display.Emoji || ...) { filtered = filtered.Replace(...) }` block) with:

```csharp
        filtered = NormalizeGlyphs(filtered, config);
```

Add this method (below `ApplyFilterSafelyAsync`):

```csharp
    /// <summary>Replaces ✓/✗ glyphs with ASCII equivalents when emoji are disabled or NO_COLOR is set.</summary>
    private static string NormalizeGlyphs(string filtered, DtkConfig config)
    {
        if (config.Display.Emoji && Environment.GetEnvironmentVariable("NO_COLOR") is null)
        {
            return filtered;
        }

        return filtered
            .Replace("✓", "ok:", StringComparison.Ordinal)
            .Replace("✗", "FAIL:", StringComparison.Ordinal);
    }
```

- [ ] **Step 4: Build + tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~FilteredRunUseCaseTests"`
Expected: PASS, no new warnings. Behavior is identical (pure extraction).

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs
git commit -m "refactor: reduce FilteredRunUseCase.RunAsync cognitive complexity (sonar S3776)"
```

---

### Task A5: S3776 — reduce `IntegratorHelpers.MergeJsonSettingsAsync` cognitive complexity (16 → ≤15)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs:174-273`
- Test (existing, must stay green): all integrator tests exercise this path, e.g. `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`, `ClaudeCodeIntegratorTests.cs`.

**Interfaces:**
- Consumes: nothing new.
- Produces: one new private helper in `IntegratorHelpers`:
  - `private static async Task<JsonObject> ReadRootObjectAsync(string path, bool exists, CancellationToken cancellationToken)`

- [ ] **Step 1: Baseline tests**

Run: `dtk dotnet test --filter "FullyQualifiedName~IntegratorHelpersTests|FullyQualifiedName~ClaudeCodeIntegratorTests"`
Expected: PASS.

- [ ] **Step 2: Extract the root read/parse/validate block (lines 182-214) into a helper**

In `MergeJsonSettingsAsync`, replace lines 182-214 (from `var exists = File.Exists(path);` through the `else { root = []; }` block) with:

```csharp
        var exists = File.Exists(path);
        var root = await ReadRootObjectAsync(path, exists, cancellationToken).ConfigureAwait(false);
```

Add this helper method (place it directly below `MergeJsonSettingsAsync`, above `FindRegisteredCommandEntry`):

```csharp
    /// <summary>
    /// Reads and parses the settings file into its root <see cref="JsonObject"/>, or returns an empty
    /// object when the file does not exist. Throws <see cref="InvalidOperationException"/> when the file
    /// contains invalid JSON or a non-object root.
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="exists">Whether the file already exists on disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<JsonObject> ReadRootObjectAsync(
        string path,
        bool exists,
        CancellationToken cancellationToken)
    {
        if (!exists)
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse JSON settings file '{path}'. The file must contain a valid JSON object at the root.",
                ex);
        }

        if (parsed is JsonObject obj)
        {
            return obj;
        }

        var actualType = parsed?.GetType().Name ?? "null";
        throw new InvalidOperationException(
            $"The settings file '{path}' must contain a JSON object at the root, but found '{actualType}'.");
    }
```

- [ ] **Step 3: Build + full test run**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS, no new warnings. The two remaining `switch` expressions (hooks node, event node) keep their existing behavior and error messages; only the read/parse/validate branch moved out.

- [ ] **Step 4: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs
git commit -m "refactor: reduce MergeJsonSettingsAsync cognitive complexity (sonar S3776)"
```

---

## Track B — Global integration

### Task B1: Domain — add `IGlobalIntegrator` capability interface

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Integration/IGlobalIntegrator.cs`

**Interfaces:**
- Consumes: `IntegrationResult` (existing domain type).
- Produces: `public interface IGlobalIntegrator { Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken); }` — consumed by Tasks B3–B6.

- [ ] **Step 1: Create the interface**

```csharp
namespace DotnetTokenKiller.Domain.Integration;

/// <summary>
/// Optional capability implemented by provider integrators that support installing their artifacts
/// into the user's home configuration (so the integration applies across all projects), in addition
/// to the per-project install described by <see cref="IProviderIntegrator"/>.
/// </summary>
public interface IGlobalIntegrator
{
    /// <summary>Installs all integration artifacts into the user's home configuration.</summary>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Build**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: PASS (interface has no implementers yet — compiles clean).

- [ ] **Step 3: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Integration/IGlobalIntegrator.cs
git commit -m "feat: add IGlobalIntegrator capability interface"
```

---

### Task B2: `HomePaths` seam + DI registration

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs:30`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `internal sealed class HomePaths` with:
  - `public HomePaths()` (resolves real home),
  - `internal HomePaths(string home)` (test seam),
  - `internal string Home`, `internal string ClaudeDir`, `internal string GeminiDir`, `internal string AiderConfPath`, `internal string AiderInstructionsPath`.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class HomePathsTests
{
    [Fact]
    public void ResolvesProviderDirectoriesUnderTheGivenHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var sut = new HomePaths(home);

        sut.Home.Should().Be(home);
        sut.ClaudeDir.Should().Be(Path.Combine(home, ".claude"));
        sut.GeminiDir.Should().Be(Path.Combine(home, ".gemini"));
        sut.AiderConfPath.Should().Be(Path.Combine(home, ".aider.conf.yml"));
        sut.AiderInstructionsPath.Should().Be(Path.Combine(home, ".aider-dtk-instructions.md"));
    }

    [Fact]
    public void ParameterlessCtorResolvesTheRealUserProfile()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        new HomePaths().Home.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test --filter "FullyQualifiedName~HomePathsTests"`
Expected: FAIL — `HomePaths` does not exist (compile error).

- [ ] **Step 3: Create `HomePaths`**

Create `src/DotnetTokenKiller.Application/Integration/HomePaths.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Resolves the user's home directory and the per-provider config paths used by global integration.
/// Mirrors the <see cref="RtkHookCoexistence"/> test seam: the public ctor resolves the real home;
/// the internal ctor injects an isolated home so tests never touch the real machine.
/// </summary>
internal sealed class HomePaths
{
    /// <summary>Creates an instance rooted at the real user profile directory.</summary>
    public HomePaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <summary>Test seam: inject an isolated home directory.</summary>
    /// <param name="home">The home directory to resolve provider paths against.</param>
    internal HomePaths(string home)
    {
        Home = home;
    }

    /// <summary>Gets the user's home directory.</summary>
    internal string Home { get; }

    /// <summary>Gets the user-level Claude Code config directory (<c>~/.claude</c>).</summary>
    internal string ClaudeDir => Path.Combine(Home, ".claude");

    /// <summary>Gets the user-level Gemini CLI config directory (<c>~/.gemini</c>).</summary>
    internal string GeminiDir => Path.Combine(Home, ".gemini");

    /// <summary>Gets the user-level Aider config file path (<c>~/.aider.conf.yml</c>).</summary>
    internal string AiderConfPath => Path.Combine(Home, ".aider.conf.yml");

    /// <summary>Gets the user-level dtk instructions file path (<c>~/.aider-dtk-instructions.md</c>).</summary>
    internal string AiderInstructionsPath => Path.Combine(Home, ".aider-dtk-instructions.md");
}
```

- [ ] **Step 4: Register in DI**

In `src/DotnetTokenKiller.Application/DependencyInjection.cs`, add after line 30 (`services.AddTransient<RtkHookCoexistence>();`):

```csharp
        services.AddSingleton<HomePaths>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~HomePathsTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HomePaths.cs \
        src/DotnetTokenKiller.Application/DependencyInjection.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/HomePathsTests.cs
git commit -m "feat: add HomePaths seam for global config path resolution"
```

---

### Task B3: `ClaudeCodeIntegrator` implements `IGlobalIntegrator`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`
- Modify (test ctor + new tests): `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs`

**Interfaces:**
- Consumes: `IGlobalIntegrator` (B1), `HomePaths` (B2), `RtkHookCoexistence` (existing).
- Produces: `ClaudeCodeIntegrator : IProviderIntegrator, IGlobalIntegrator`; ctor becomes `ClaudeCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)`.

- [ ] **Step 1: Write the failing tests**

In `ClaudeCodeIntegratorTests.cs`, update the ctor to also build a `HomePaths` rooted at the isolated home, and add global tests. Change the constructor field setup (lines 14-19) to:

```csharp
    private readonly string _isolatedHome;

    public ClaudeCodeIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        var userClaudeDir = Path.Combine(_isolatedHome, ".claude");
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        _sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath), new HomePaths(_isolatedHome));
    }
```

(Also update the one-off `sut` built at line ~318 in `IntegrateAsync_RtkHookInProjectSettings_...` to pass `new HomePaths(_isolatedHome)` as the second argument.)

Add these new tests:

```csharp
    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesAllThreeFilesUnderHomeClaudeDir()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        File.Exists(Path.Combine(_isolatedHome, ".claude", "skills", "dotnet-token-killer", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "hooks", "dotnet-to-dtk.py")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RegistersHomeRootedHookCommand()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("""python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py""");
    }

    [Fact]
    public void ImplementsIGlobalIntegrator()
    {
        _sut.Should().BeAssignableTo<DotnetTokenKiller.Domain.Integration.IGlobalIntegrator>();
    }
```

> Note: only the `$HOME` env var is quoted (`"$HOME"/.claude/...`), mirroring the local
> `"$CLAUDE_PROJECT_DIR"/.claude/...` convention. This is shell-correct, keeps `DeriveLegacyCommand`
> recognizing the `"$VAR"/…` shape, and avoids a trailing quote inside the C# raw-string literal.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dtk dotnet test --filter "FullyQualifiedName~ClaudeCodeIntegratorTests"`
Expected: FAIL — ctor arity mismatch / `IntegrateGlobalAsync` not defined.

- [ ] **Step 3: Implement global support in `ClaudeCodeIntegrator`**

Edit `ClaudeCodeIntegrator.cs`:

1. Change the class declaration and primary ctor:

```csharp
internal sealed class ClaudeCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator
```

2. Add the global hook command constant next to `HookCommand` (line 34):

```csharp
    /// <summary>
    /// Global variant of <see cref="HookCommand"/>: rooted at <c>$HOME</c> because the hook script is
    /// installed under <c>~/.claude/hooks</c> (Claude's <c>$CLAUDE_PROJECT_DIR</c> points at the
    /// current project, not the home-installed script).
    /// </summary>
    private const string GlobalHookCommand = """python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py""";
```

3. Refactor the existing `IntegrateAsync` body to delegate to a shared core, and add `IntegrateGlobalAsync`. Replace the whole `IntegrateAsync` method (lines 99-134) with:

```csharp
    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
        => IntegrateCoreAsync(directory, HookCommand, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.ClaudeDir, GlobalHookCommand, force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string baseDirectory,
        string hookCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(baseDirectory, "skills", "dotnet-token-killer", "SKILL.md"),
            SkillMarkdown, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteHookAndSettingsAsync(
            new HookSpec(
                Path.Combine(baseDirectory, "hooks", "dotnet-to-dtk.py"),
                HookScriptTemplates.ClaudeHook,
                Path.Combine(baseDirectory, "settings.json"),
                "PreToolUse",
                "Bash",
                hookCommand),
            context, cancellationToken).ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileAsync(home.Home, cancellationToken).ConfigureAwait(false);
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
    }
```

> Important: `baseDirectory` is now the `.claude` directory itself (local: `Path.Combine(directory, ".claude")`; global: `home.ClaudeDir`). Because the old local code combined `directory` with `".claude"` inside each path, the local `IntegrateAsync` must pass `Path.Combine(directory, ".claude")` as the base. Update the local delegation to:

```csharp
    public Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
        => IntegrateCoreAsync(Path.Combine(directory, ".claude"), HookCommand, force, cancellationToken);
```

Also change the rtk reconcile: local previously passed `directory`; to preserve local behavior exactly, thread the original project directory into the reconcile. Since `IntegrateCoreAsync` no longer has the project dir for local, pass a `reconcileDir` param:

```csharp
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(Path.Combine(directory, ".claude"), directory, HookCommand, force, cancellationToken);

    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.ClaudeDir, home.Home, GlobalHookCommand, force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string baseDirectory,
        string reconcileDir,
        string hookCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        // ... body identical to above, except:
        var rtkOutcome = await rtk.ReconcileAsync(reconcileDir, cancellationToken).ConfigureAwait(false);
    }
```

Use this final three-arg-base form (with `reconcileDir`) as the implementation.

- [ ] **Step 4: Update DI registration so the same instance satisfies both interfaces**

In `src/DotnetTokenKiller.Application/DependencyInjection.cs`, the existing line
`services.AddTransient<IProviderIntegrator, ClaudeCodeIntegrator>();` keeps working. No global
resolution is needed via DI (the use case downcasts `IProviderIntegrator` to `IGlobalIntegrator`),
so no DI change is required here. Confirm `ClaudeCodeIntegrator`'s new `HomePaths` dependency resolves
(registered in B2).

- [ ] **Step 5: Build + run tests (new + existing local ones)**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~ClaudeCodeIntegratorTests"`
Expected: PASS — all existing local tests (unchanged behavior) and the 3 new global tests.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs
git commit -m "feat: global integration for Claude Code (dtk integrate claude --global)"
```

---

### Task B4: `GeminiCliIntegrator` implements `IGlobalIntegrator`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs`
- Modify (test ctor + new tests): `tests/DotnetTokenKiller.Application.Tests/Integration/GeminiCliIntegratorTests.cs`

**Interfaces:**
- Consumes: `IGlobalIntegrator` (B1), `HomePaths` (B2).
- Produces: `GeminiCliIntegrator : IProviderIntegrator, IGlobalIntegrator`; ctor becomes `GeminiCliIntegrator(HomePaths home)`.

- [ ] **Step 1: Write the failing tests**

In `GeminiCliIntegratorTests.cs`, update the `_sut` initialization to construct an isolated home and pass it. Add a field `private readonly string _isolatedHome = Path.Combine(<tempDir>, "isolated-home");` and build `_sut = new GeminiCliIntegrator(new HomePaths(_isolatedHome));`. Then add:

```csharp
    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesArtifactsUnderHomeGeminiDir()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().NotBeEmpty();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "hooks", "dotnet-to-dtk.py")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "GEMINI.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RegistersHomeRootedHookCommand()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".gemini", "settings.json"));
        json.Should().Contain("""python3 "$HOME"/.gemini/hooks/dotnet-to-dtk.py""");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dtk dotnet test --filter "FullyQualifiedName~GeminiCliIntegratorTests"`
Expected: FAIL — ctor arity / `IntegrateGlobalAsync` not defined.

- [ ] **Step 3: Implement global support in `GeminiCliIntegrator`**

Edit `GeminiCliIntegrator.cs`:

1. Class + ctor:

```csharp
public sealed class GeminiCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator
```

2. Add the global hook command constant next to `HookCommand` (line 24):

```csharp
    private const string GlobalHookCommand = """python3 "$HOME"/.gemini/hooks/dotnet-to-dtk.py""";
```

3. Replace `IntegrateAsync` (lines 42-65) with a shared core plus both entry points. The current
   local paths combine `directory` with `GEMINI.md` (project root) and `.gemini/...`. For global,
   the context file lives at `~/.gemini/GEMINI.md`. So parameterize both the gemini dir and the
   context-file path:

```csharp
    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "GEMINI.md"),
            Path.Combine(directory, ".gemini"),
            HookCommand,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.GeminiDir, "GEMINI.md"),
            home.GeminiDir,
            GlobalHookCommand,
            force,
            cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string contextFilePath,
        string geminiDir,
        string hookCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            contextFilePath,
            SectionMarker, SectionEndMarker, GeminiSection,
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteHookAndSettingsAsync(
            new HookSpec(
                Path.Combine(geminiDir, "hooks", "dotnet-to-dtk.py"),
                HookScriptTemplates.GeminiHook,
                Path.Combine(geminiDir, "settings.json"),
                "BeforeTool",
                "run_shell_command",
                hookCommand),
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
```

- [ ] **Step 4: Build + run tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~GeminiCliIntegratorTests"`
Expected: PASS — existing local tests unchanged + 2 new global tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/GeminiCliIntegratorTests.cs
git commit -m "feat: global integration for Gemini CLI"
```

---

### Task B5: `AiderIntegrator` implements `IGlobalIntegrator` (absolute `read:` path)

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/AiderIntegrator.cs`
- Modify (test ctor + new tests): `tests/DotnetTokenKiller.Application.Tests/Integration/AiderIntegratorTests.cs`

**Interfaces:**
- Consumes: `IGlobalIntegrator` (B1), `HomePaths` (B2).
- Produces: `AiderIntegrator : IProviderIntegrator, IGlobalIntegrator`; ctor becomes `AiderIntegrator(HomePaths home)`.

**Design note:** Local aider writes `.aider-dtk-instructions.md` + `.aider.conf.yml` in the project and the `read:` entry is the relative filename. Global writes `~/.aider.conf.yml` + `~/.aider-dtk-instructions.md`, and the `read:` entry must be the **absolute** path to `~/.aider-dtk-instructions.md` (a home-level conf cannot rely on a cwd-relative filename resolving). The instructions filename used inside the conf section therefore differs per scope.

- [ ] **Step 1: Write the failing tests**

In `AiderIntegratorTests.cs`, update `_sut` to `new AiderIntegrator(new HomePaths(_isolatedHome))` with an isolated home field. Add:

```csharp
    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesConfAndInstructionsUnderHome()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().NotBeEmpty();
        File.Exists(Path.Combine(_isolatedHome, ".aider.conf.yml")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".aider-dtk-instructions.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_ConfReadsAbsoluteInstructionsPath()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var conf = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".aider.conf.yml"));
        conf.Should().Contain(Path.Combine(_isolatedHome, ".aider-dtk-instructions.md"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dtk dotnet test --filter "FullyQualifiedName~AiderIntegratorTests"`
Expected: FAIL — ctor arity / `IntegrateGlobalAsync` not defined.

- [ ] **Step 3: Implement global support in `AiderIntegrator`**

Edit `AiderIntegrator.cs`:

1. Class + ctor:

```csharp
public sealed class AiderIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator
```

2. Build the conf section from a `readTarget` (relative filename for local, absolute path for global)
   instead of hardcoding `.aider-dtk-instructions.md`. Add a helper that produces the section text
   for a given read target, replacing the two `const` sections with builders:

```csharp
    private static string BuildConfSection(string readTarget) =>
        $"""
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        read:
          - {readTarget}
        # /dtk
        """;
```

   Keep `AiderConfSectionWithoutReadKey` as-is (it contributes no `read:` key). For the external-key
   merge path (`TryMergeExistingReadKey` / `MergeFlowStyle` / `MergeBlockStyle`), pass the same
   `readTarget` through so the merged entry uses the correct (relative or absolute) value. Thread a
   `string readTarget` parameter into `PrepareConfSectionAsync`, `TryMergeExistingReadKey`,
   `MergeFlowStyle`, and `MergeBlockStyle`, replacing every `InstructionsFileName` used as the value
   to write/detect with `readTarget`. (The instructions *file* written on disk stays keyed off the
   scope-specific path; see below.)

3. Replace `IntegrateAsync` (lines 60-79) with a shared core + both entry points:

```csharp
    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, InstructionsFileName),
            Path.Combine(directory, ".aider.conf.yml"),
            InstructionsFileName,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            home.AiderInstructionsPath,
            home.AiderConfPath,
            home.AiderInstructionsPath,
            force,
            cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string instructionsPath,
        string confPath,
        string readTarget,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            instructionsPath, InstructionsMarkdown, context, cancellationToken).ConfigureAwait(false);

        var confSection = await PrepareConfSectionAsync(confPath, readTarget, force, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            confPath, SectionMarker, SectionEndMarker, confSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
```

   Update `PrepareConfSectionAsync` to accept `readTarget` and, where it currently returns
   `AiderConfSection`, return `BuildConfSection(readTarget)`; where it merges an existing external
   `read:` key, pass `readTarget` into `TryMergeExistingReadKey`.

> Keep `InstructionsFileName` as the local relative value. `readTarget` equals `InstructionsFileName`
> for local (unchanged behavior) and the absolute `home.AiderInstructionsPath` for global.

- [ ] **Step 4: Build + run tests**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~AiderIntegratorTests"`
Expected: PASS — existing local tests unchanged + 2 new global tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/AiderIntegrator.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/AiderIntegratorTests.cs
git commit -m "feat: global integration for Aider (absolute read: path)"
```

---

### Task B6: `IntegrateUseCase.RunGlobalAsync` + CLI `--global` flag

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrateUseCase.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs`

**Interfaces:**
- Consumes: `IGlobalIntegrator` (B1), existing `IntegrateUseCase` internals.
- Produces:
  - `IntegrateUseCase.RunGlobalAsync(string providerName, bool force, CancellationToken)` → `Task<IntegrationResult>`; throws `InvalidOperationException` when the provider is unknown or not an `IGlobalIntegrator`.
  - `IntegrateCommandSettings.Global` (bool, `-g|--global`).

- [ ] **Step 1: Write the failing use-case tests**

In `IntegrateUseCaseTests.cs`, add:

```csharp
    [Fact]
    public async Task RunGlobalAsync_RepoOnlyProvider_ThrowsWithFriendlyMessage()
    {
        var act = () => _sut.RunGlobalAsync("copilot", false, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("copilot").And.Contain("repository-scoped");
    }

    [Fact]
    public async Task RunGlobalAsync_GlobalCapableProvider_ReturnsResult()
    {
        var result = await _sut.RunGlobalAsync("claude", false, CancellationToken.None);

        result.Should().NotBeNull();
    }
```

> Check how `_sut` (the `IntegrateUseCase`) is constructed in this test file. If it builds real
> integrators, ensure the claude integrator is constructed with an isolated `HomePaths`/`RtkHookCoexistence`
> (temp home) so the "global capable" test does not touch the real machine. If the test uses fakes,
> add a fake implementing `IProviderIntegrator, IGlobalIntegrator` for the claude case and a
> repo-only fake for copilot.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dtk dotnet test --filter "FullyQualifiedName~IntegrateUseCaseTests"`
Expected: FAIL — `RunGlobalAsync` not defined.

- [ ] **Step 3: Add `RunGlobalAsync` to `IntegrateUseCase`**

Add to `IntegrateUseCase` (after `RunAsync`), importing nothing new (both types already in scope):

```csharp
    /// <summary>Runs the named provider's global (home config) integration.</summary>
    /// <param name="providerName">Provider identifier (e.g. "claude").</param>
    /// <param name="force">When <see langword="true"/>, overwrite existing files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The integration result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="providerName"/> is unknown, or when the provider does not support
    /// global integration (it is repository-scoped).
    /// </exception>
    public Task<IntegrationResult> RunGlobalAsync(
        string providerName,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!_integrators.TryGetValue(providerName, out var integrator))
        {
            throw new InvalidOperationException(
                $"Unknown provider '{providerName}'. Available: {string.Join(", ", _integrators.Keys)}");
        }

        if (integrator is not IGlobalIntegrator globalIntegrator)
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' is repository-scoped and has no global config. " +
                $"Run 'dtk integrate {providerName}' inside a project.");
        }

        return globalIntegrator.IntegrateGlobalAsync(force, cancellationToken);
    }
```

- [ ] **Step 4: Run use-case tests to verify they pass**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test --filter "FullyQualifiedName~IntegrateUseCaseTests"`
Expected: PASS.

- [ ] **Step 5: Add the `--global` flag to settings**

In `IntegrateCommandSettings.cs`, add after the `Force` property:

```csharp
    /// <summary>Gets a value indicating whether to install into the user's home config instead of the project.</summary>
    [CommandOption("-g|--global")]
    [Description("Install into the user's home config (claude, gemini, aider) instead of a project")]
    public bool Global { get; init; }
```

- [ ] **Step 6: Wire `--global` into `IntegrateCommand`**

In `IntegrateCommand.RunAsync`, after the `canonicalProvider is null` guard (line 38) and before
`var directory = ...` (line 40), add the mutual-exclusion guard:

```csharp
        if (settings.Global && settings.Directory is not null)
        {
            console.MarkupLine(
                "[red]Error:[/] --global installs into your home config and cannot be combined with --dir.");
            return 1;
        }
```

Then branch the integration call. Replace the `try { result = await integrateUseCase.RunAsync(...); }`
block (lines 42-56) with:

```csharp
        var directory = settings.Global ? Environment.CurrentDirectory : (settings.Directory ?? Environment.CurrentDirectory);

        IntegrationResult result;
        try
        {
            result = settings.Global
                ? await integrateUseCase
                    .RunGlobalAsync(canonicalProvider, settings.Force, cancellationToken)
                    .ConfigureAwait(false)
                : await integrateUseCase
                    .RunAsync(canonicalProvider, directory, settings.Force, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
```

> The existing `var directory = settings.Directory ?? Environment.CurrentDirectory;` line (40) is
> replaced by the above. In global mode `directory` is only used to compute display-relative paths
> in the created/updated/skipped output; home-absolute artifact paths will render as absolute
> (acceptable — they are outside the cwd, matching the existing `IsOutsideProject` behavior).

- [ ] **Step 7: Add a CLI integration test for the flag**

Add to the CLI integration tests (e.g. a new fact in an existing integrate-related test class under
`tests/DotnetTokenKiller.Cli.IntegrationTests/`, or extend the use-case test coverage) asserting that
`dtk integrate copilot --global` exits non-zero with the repository-scoped message. If no CLI harness
for `integrate` exists, cover this at the `IntegrateCommand` level with a unit test that constructs the
command with a fake `IAnsiConsole` (Spectre.Console.Testing `TestConsole`) and asserts exit code 1 and
the message. Example using `TestConsole`:

```csharp
    [Fact]
    public async Task Integrate_GlobalWithDir_ReturnsError()
    {
        var console = new Spectre.Console.Testing.TestConsole();
        var command = new IntegrateCommand(BuildUseCase(), console);
        var settings = new IntegrateCommandSettings { Provider = "claude", Global = true, Directory = "/tmp/x" };

        var exit = await command.RunAsync(settings, CancellationToken.None);

        exit.Should().Be(1);
        console.Output.Should().Contain("cannot be combined with --dir");
    }
```

> `BuildUseCase()` returns a minimal `IntegrateUseCase` (real integrators with isolated `HomePaths`,
> or a small set of fakes). Follow whatever construction the existing `IntegrateCommand` tests use;
> if none exist, prefer fakes so no filesystem is touched for the `--global` + `--dir` case (it
> returns before any integrator runs).

- [ ] **Step 8: Build + full test run**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS (whole suite).

- [ ] **Step 9: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/IntegrateUseCase.cs \
        src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs \
        src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs \
        tests/DotnetTokenKiller.Application.Tests/Integration/IntegrateUseCaseTests.cs \
        tests/DotnetTokenKiller.Cli.IntegrationTests/
git commit -m "feat: dtk integrate --global flag with repo-only provider guard"
```

---

### Task B7: Documentation

**Files:**
- Modify: `README.md` (AI Agent Setup section)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing (docs only).

- [ ] **Step 1: Document `--global` in the README**

In `README.md`, under **AI Agent Setup**, after the provider table, add:

```markdown
### Global vs per-project integration

By default, `dtk integrate <provider>` installs artifacts into the current project. To install into
your home config so the integration applies across all projects, add `--global` (`-g`):

```sh
dtk integrate claude --global   # ~/.claude
dtk integrate gemini --global   # ~/.gemini
dtk integrate aider  --global   # ~/.aider.conf.yml
```

`--global` is supported for **claude**, **gemini**, and **aider** (the providers with a home config).
The other providers are repository-scoped; run them without `--global` inside a project. `--global`
cannot be combined with `--dir`.
```

- [ ] **Step 2: Verify markdown lints**

Run: `npx markdownlint-cli2 README.md` (or the repo's configured markdown lint) if available; otherwise visually confirm fenced-code nesting is correct.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document dtk integrate --global"
```

---

## Final verification

- [ ] **Full build + test**

Run: `dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS, zero warnings.

- [ ] **Format check**

Run: `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
Expected: no changes needed.

- [ ] **ReSharper cleanup (per CLAUDE.md)**

Run: `jb cleanupcode DotnetTokenKiller.slnx --profile="Built-in: Reformat & Apply Syntax Style"`
Then re-run build + tests; commit any formatting-only changes.

- [ ] **Manual smoke test (global claude, isolated home)**

```bash
HOME=$(mktemp -d) dtk integrate claude --global
# confirm files created under $HOME/.claude and settings.json command uses $HOME-rooted path
```

Note: the SonarQube gate re-evaluates on the next `develop` analysis (push/merge); Track A brings
`new_violations` to 0, and the feature's new code must stay violation-free (small extracted methods,
named constants, ≥80% new-line coverage from the added tests).
```

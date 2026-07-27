# Subcommand Single Source of Truth — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the set of dtk-handled dotnet subcommands exist in exactly one place, so adding a subcommand can never silently miss the agent hook.

**Architecture:** A new `DotnetSubcommands` type in the Domain layer becomes canonical. `FilterKeys` aliases its `const`s (so DI keys cannot diverge by construction), the Cli layer's private copies are deleted, and the Python hook's subcommand tuple and docstrings are generated from it. Four binding tests — each living in the layer that owns the binding — cover the seams that generation cannot close.

**Tech Stack:** .NET 10, C# raw string literals, Spectre.Console.Cli, `Microsoft.Extensions.DependencyInjection` keyed services, xunit 2.9.3 + FluentAssertions 8.10.

**Spec:** [docs/superpowers/specs/2026-07-27-subcommand-single-source-of-truth-design.md](../specs/2026-07-27-subcommand-single-source-of-truth-design.md)

## Global Constraints

- `TreatWarningsAsErrors` is on. Every analyzer warning (Roslynator, SonarAnalyzer, .NET analyzers) fails the build.
- Documentation generation is on for `src/` projects: every **public** type and member needs an XML `<summary>`. Missing docs are CS1591 errors.
- File-scoped namespaces. `var` preferred. LF line endings, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Central package management: no version numbers in `.csproj`.
- Build/test/format via `dtk`: `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test DotnetTokenKiller.slnx`, `dtk dotnet format DotnetTokenKiller.slnx --no-restore`.
- **The user-facing help surface must not change.** `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/*.verified.txt` must all still pass unmodified. A snapshot diff means the refactor changed behaviour and is wrong.
- **The generated hook bytes must not change.** `.claude/hooks/dotnet-to-dtk.py` must remain byte-identical after Task 4. An empty `git diff` on that file is the proof the generator is correct.
- The supported set stays exactly `build`, `test`, `restore`, `clean`, `format`.

---

## File Structure

**Create:**
- `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs` — the canonical list. Top-level in Domain (namespace `DotnetTokenKiller.Domain`) because the concept spans filters, CLI routing, and hook generation; it is not a filter-only concern.
- `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs` — shape of the canonical type + `FilterKeys` binding.
- `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` — DI keyed-filter binding + generated-hook bindings + repo hook parity.
- `tests/DotnetTokenKiller.Cli.IntegrationTests/SubcommandRegistrationTests.cs` — Spectre `dotnet` branch binding.

**Modify:**
- `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs` — consts alias `DotnetSubcommands`.
- `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs:16-50` — delete the five `*Subcommand` consts and both `KnownSubcommands*` collections; use `DotnetSubcommands` directly.
- `src/DotnetTokenKiller.Cli/CliConfigurator.cs:46-60` — reference `DotnetSubcommands.*`.
- `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs:10,204-218` — reference `DotnetSubcommands.Ordered`.
- `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs:38,48` — reference `DotnetSubcommands.All`.
- `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs:20-56,171-186` — generate the tuple and the docstring alternation.

**Deviation from the spec, and why:** the spec put all binding tests in `Cli.IntegrationTests` on the grounds that it is the only project seeing both Cli and Application internals. On inspection, three of the four bindings do not need Cli at all — `FilterKeys` vs canonical is Domain-only, and both the DI keys and the hook template are reachable from `Application.Tests`. Each test now lives in the layer that owns the binding it guards. Only the Spectre registration test needs `Cli.IntegrationTests`.

**Second deviation:** the spec said the repo-hook parity test should *skip* when no repo root is found. xunit 2.9.3 has no runtime skip (`Assert.Skip` is xunit v3). The test instead **fails** with an explicit message if it cannot locate `DotnetTokenKiller.slnx` by walking up from the test assembly. This is stricter and simpler; the test project only ever runs from inside the repo.

---

### Task 1: Canonical list in Domain, with `FilterKeys` bound to it

**Files:**
- Create: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`
- Modify: `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs`
- Test: `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DotnetTokenKiller.Domain.DotnetSubcommands` with `const string Build/Test/Restore/Clean/Format`, `IReadOnlyList<string> Ordered`, `IReadOnlySet<string> All`, `IReadOnlyList<string> Sorted`. Every later task consumes these exact names.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`:

```csharp
using System.Reflection;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

/// <summary>
/// Binds every Domain-level restatement of the supported dotnet subcommands back to
/// <see cref="DotnetSubcommands"/>. Without this, a new filter key can be added without a
/// canonical entry and nothing fails until adoption is silently zero.
/// </summary>
public class DotnetSubcommandsTests
{
    [Fact]
    public void Ordered_IsTheDisplayOrderUsedByTheCli()
    {
        DotnetSubcommands.Ordered.Should().Equal("build", "test", "restore", "clean", "format");
    }

    [Fact]
    public void Sorted_IsOrdinalAlphabetical_WithTheSameMembersAsOrdered()
    {
        DotnetSubcommands.Sorted.Should().BeInAscendingOrder(StringComparer.Ordinal);
        DotnetSubcommands.Sorted.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
    }

    [Fact]
    public void All_ContainsEveryOrderedEntry_CaseInsensitively()
    {
        DotnetSubcommands.All.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
        DotnetSubcommands.All.Contains("BUILD").Should().BeTrue();
    }

    [Fact]
    public void FilterKeys_ExposesExactlyTheCanonicalSubcommands()
    {
        var keys = typeof(FilterKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        keys.Should().BeEquivalentTo(
            DotnetSubcommands.Ordered,
            "every keyed IOutputFilter must correspond to a canonical subcommand and vice versa");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~DotnetSubcommandsTests"`
Expected: compile failure — `DotnetSubcommands` does not exist.

- [ ] **Step 3: Create the canonical type**

Create `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`:

```csharp
namespace DotnetTokenKiller.Domain;

/// <summary>
/// The dotnet subcommands dtk filters. This is the single source of truth: CLI routing, shell
/// completion, the keyed filter registrations, and the generated agent hook scripts all derive
/// from it, so a new subcommand cannot be half-added.
/// </summary>
public static class DotnetSubcommands
{
    /// <summary>The <c>dotnet build</c> subcommand.</summary>
    public const string Build = "build";

    /// <summary>The <c>dotnet test</c> subcommand.</summary>
    public const string Test = "test";

    /// <summary>The <c>dotnet restore</c> subcommand.</summary>
    public const string Restore = "restore";

    /// <summary>The <c>dotnet clean</c> subcommand.</summary>
    public const string Clean = "clean";

    /// <summary>The <c>dotnet format</c> subcommand.</summary>
    public const string Format = "format";

    /// <summary>
    /// Canonical display order, used for CLI routing, help, and completion. Changing this order
    /// changes the order commands are listed in <c>dtk dotnet --help</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean, Format];

    /// <summary>Case-insensitive membership test, used to tell a dtk-handled invocation from a passthrough.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinal alphabetical order. Generated artifacts (the Python hook's subcommand tuple) use
    /// this so their bytes stay stable regardless of how <see cref="Ordered"/> is rearranged.
    /// </summary>
    public static readonly IReadOnlyList<string> Sorted = [.. Ordered.Order(StringComparer.Ordinal)];
}
```

- [ ] **Step 4: Bind `FilterKeys` to it**

Replace the body of `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Filters;

/// <summary>
/// Keyed service keys for <see cref="IOutputFilter"/> registrations. Each key aliases the
/// corresponding <see cref="DotnetSubcommands"/> constant, so a key and its subcommand cannot
/// hold different values.
/// </summary>
public static class FilterKeys
{
    /// <summary>Key for the <c>dotnet build</c> output filter.</summary>
    public const string Build = DotnetSubcommands.Build;

    /// <summary>Key for the <c>dotnet test</c> output filter.</summary>
    public const string Test = DotnetSubcommands.Test;

    /// <summary>Key for the <c>dotnet restore</c> output filter.</summary>
    public const string Restore = DotnetSubcommands.Restore;

    /// <summary>Key for the <c>dotnet clean</c> output filter.</summary>
    public const string Clean = DotnetSubcommands.Clean;

    /// <summary>Key for the <c>dotnet format</c> output filter.</summary>
    public const string Format = DotnetSubcommands.Format;
}
```

Note: `FilterKeys` is in namespace `DotnetTokenKiller.Domain.Filters`, which is nested under
`DotnetTokenKiller.Domain`, so `DotnetSubcommands` resolves without a `using`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~DotnetSubcommandsTests"`
Expected: 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Domain/DotnetSubcommands.cs \
        src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs \
        tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs
git commit -m "refactor: add canonical DotnetSubcommands and bind FilterKeys to it"
```

---

### Task 2: Delete the Cli copy

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs:16-50,76,81,105,122`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs:46-60`
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs:10,204-218`
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs:38,48`

**Interfaces:**
- Consumes: `DotnetSubcommands.Ordered`, `.All`, and the `const`s from Task 1.
- Produces: nothing new. `ArgumentPreprocessor.KnownSubcommandsOrdered`, `.KnownSubcommands`, and the five `*Subcommand` consts **cease to exist** — later tasks must not reference them.

This task has no new test of its own: it is covered by the existing `ArgumentPreprocessorTests`,
`CompletionCommandTests`, and the `CliConfiguratorTests` snapshots. Those passing unchanged *is*
the assertion that the refactor preserved behaviour.

- [ ] **Step 1: Rewrite the head of `ArgumentPreprocessor`**

In `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs`, add `using DotnetTokenKiller.Domain;` at the
top, then delete lines 16-50 (the five `*Subcommand` consts, `KnownSubcommandsOrdered`, and
`KnownSubcommands`), keeping `DotnetCommand`. The head becomes:

```csharp
using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Cli;

// ... existing <summary> and <remarks> unchanged ...
internal static class ArgumentPreprocessor
{
    /// <summary>The <c>dotnet</c> driver command that dtk-handled invocations begin with.</summary>
    private const string DotnetCommand = "dotnet";

    private static readonly HashSet<string> DtkOptions =
        // ... unchanged ...
```

- [ ] **Step 2: Repoint the four use sites inside `ArgumentPreprocessor`**

Four replacements in the same file:

- line 76 (`Normalize`): `!KnownSubcommands.Contains(args[1]))` → `!DotnetSubcommands.All.Contains(args[1]))`
- line 81 (`Normalize`): `var canonicalSub = KnownSubcommandsOrdered.First(` → `var canonicalSub = DotnetSubcommands.Ordered.First(`
- line 105 (`IsPassthrough`): `!KnownSubcommands.Contains(args[1]);` → `!DotnetSubcommands.All.Contains(args[1]);`
- line 122 (`InsertSeparator`): `!KnownSubcommands.Contains(args[1]))` → `!DotnetSubcommands.All.Contains(args[1]))`

- [ ] **Step 3: Repoint `CliConfigurator`**

In `src/DotnetTokenKiller.Cli/CliConfigurator.cs`, add `using DotnetTokenKiller.Domain;` and replace
the five registration arguments (lines 46, 50, 54, 57, 60) — descriptions and examples stay exactly
as they are:

```csharp
dotnet.AddCommand<DotnetBuildCommand>(DotnetSubcommands.Build)
dotnet.AddCommand<DotnetTestCommand>(DotnetSubcommands.Test)
dotnet.AddCommand<DotnetRestoreCommand>(DotnetSubcommands.Restore)
dotnet.AddCommand<DotnetCleanCommand>(DotnetSubcommands.Clean)
dotnet.AddCommand<DotnetFormatCommand>(DotnetSubcommands.Format)
```

- [ ] **Step 4: Repoint `CompletionCommand`**

In `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs`, add `using DotnetTokenKiller.Domain;`,
change the four `ArgumentPreprocessor.KnownSubcommandsOrdered` references (lines 204, 207, 212, 218)
to `DotnetSubcommands.Ordered`, and update the `<see cref="..."/>` on line 10 to
`<see cref="DotnetSubcommands.Ordered"/>` — a stale cref is a CS1574 error under
`TreatWarningsAsErrors`.

- [ ] **Step 5: Repoint the completion test**

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`, add
`using DotnetTokenKiller.Domain;`, change line 48 to `foreach (var subcommand in DotnetSubcommands.All)`
and the comment on line 38 to read `generated from DotnetSubcommands.Ordered`.

- [ ] **Step 6: Build and run the full suite**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: no errors. Any remaining reference to a deleted member surfaces here.

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: all PASS, **with no `.verified.txt` snapshot changes**. If Verify writes a `.received.txt`
file, the refactor changed the help surface — revert and find out why rather than accepting it.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Cli tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs
git commit -m "refactor: drop the Cli copy of the subcommand list"
```

---

### Task 3: Bind DI keys and Spectre registrations by test

**Files:**
- Create: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/SubcommandRegistrationTests.cs`

**Interfaces:**
- Consumes: `DotnetSubcommands.Ordered`, `ServiceCollectionExtensions.AddApplication()`, `CliConfigurator.Configure(IConfigurator, string)`.
- Produces: nothing consumed by later tasks. Task 4 adds more `[Fact]`s to `SubcommandBindingTests`.

- [ ] **Step 1: Write the failing DI test**

Create `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`:

```csharp
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotnetTokenKiller.Application.Tests;

/// <summary>
/// Binds the Application layer's restatements of the supported subcommands back to
/// <see cref="DotnetSubcommands"/>. These are the seams generation cannot close: a registration
/// that is simply absent produces no compile error and no runtime error until a user hits it.
/// </summary>
public sealed class SubcommandBindingTests
{
    [Fact]
    public void EverySubcommand_ResolvesAKeyedOutputFilter()
    {
        var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            var filter = provider.GetKeyedService<IOutputFilter>(subcommand);

            filter.Should().NotBeNull(
                "'{0}' is a canonical subcommand, so AddApplication must register a filter keyed to it",
                subcommand);
        }
    }

    [Fact]
    public void NoFilter_IsRegisteredUnderANonCanonicalKey()
    {
        var services = new ServiceCollection().AddApplication();

        var keys = services
            .Where(descriptor => descriptor.ServiceType == typeof(IOutputFilter))
            .Select(descriptor => descriptor.ServiceKey)
            .OfType<string>();

        keys.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
    }
}
```

- [ ] **Step 2: Run it to verify it passes for the right reason**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~SubcommandBindingTests"`
Expected: 2 tests PASS. These are regression locks over already-correct code, so passing now is
correct — Task 6 proves they are not vacuous.

If `GetKeyedService` returns null, the filter's constructor could not be satisfied by
`AddApplication` alone. In that case add `services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);`
before `BuildServiceProvider()` and note it here.

- [ ] **Step 3: Write the Spectre registration test**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/SubcommandRegistrationTests.cs`:

```csharp
using DotnetTokenKiller.Domain;
using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Binds the Spectre command tree to <see cref="DotnetSubcommands"/>. The help snapshots lock the
/// wording of what is registered; this locks the <em>set</em>, so adding a canonical subcommand
/// without registering a command fails here rather than at a user's terminal.
/// </summary>
public sealed class SubcommandRegistrationTests
{
    [Fact]
    public void DotnetBranch_RegistersExactlyTheCanonicalSubcommands()
    {
        var registered = ParseCommandsSection(RunHelp("dotnet", "--help"));

        registered.Should().Equal(
            DotnetSubcommands.Ordered,
            "the dotnet branch must register every canonical subcommand, in canonical order, and nothing else");
    }

    /// <summary>
    /// Extracts the command names from the COMMANDS section of a Spectre help page. The section is
    /// the last block on the page; its entries are indented four spaces and start with the name.
    /// </summary>
    /// <param name="help">Rendered help output.</param>
    private static List<string> ParseCommandsSection(string help)
    {
        var lines = help.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "COMMANDS:");

        start.Should().BeGreaterThan(-1, "the dotnet branch help must have a COMMANDS section");

        return lines
            .Skip(start + 1)
            .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim().Split(' ')[0])
            .ToList();
    }

    private static string RunHelp(params string[] args)
    {
        var console = new TestConsole().Width(100);
        var app = new CommandApp();
        app.Configure(config =>
        {
            CliConfigurator.Configure(config, "1.2.3-test");
            config.Settings.Console = console;
        });

        app.Run(args);
        return console.Output;
    }
}
```

- [ ] **Step 4: Run it**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~SubcommandRegistrationTests"`
Expected: PASS.

If the parse returns an empty list, print `RunHelp("dotnet", "--help")` and adjust
`ParseCommandsSection` to the actual rendering — the reference output is
`tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_DotnetBranchHelp_MatchesSnapshot.verified.txt`.

- [ ] **Step 5: Commit**

```bash
git add tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs \
        tests/DotnetTokenKiller.Cli.IntegrationTests/SubcommandRegistrationTests.cs
git commit -m "test: bind DI filter keys and Spectre registrations to the canonical subcommands"
```

---

### Task 4: Generate the hook's subcommand tuple and docstrings

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs:20-56,171-186`
- Test: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` (add facts)

**Interfaces:**
- Consumes: `DotnetSubcommands.Ordered`, `DotnetSubcommands.Sorted`.
- Produces: `HookScriptTemplates.ClaudeHook`, `.GeminiHook`, `.CopilotCliHook` — unchanged names and types (`static string { get; }`), now derived from the canonical list.

**Critical detail — the two orders are different and both must be preserved:**

- the docstring prose reads `dotnet build|test|restore|clean|format` → **display order** (`Ordered`);
- the Python tuple reads `("build", "clean", "format", "restore", "test")` → **alphabetical** (`Sorted`).

Getting these backwards changes the generated bytes and fails Task 5.

**Critical detail — raw string interpolation.** The templates must become `$$""""` (two `$`, four
quotes). Under `$$`, a hole is `{{Name}}` and a *single* `{` is literal. This matters because the
Python body contains single braces — `_BOUNDARY_CHARS = " \t;&|({\`\n"` and
`f"dtk dotnet {match.group(1)}"` — which must survive untouched. Do not use `$` (one sigil): it
would treat those braces as holes and fail to compile.

**Critical detail — declaration order.** C# initializes static fields in textual order. The two
generated fragments must be declared **above** the literals that interpolate them, or those literals
capture `null`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` (inside the class; the
`using DotnetTokenKiller.Application.Integration;` goes at the top of the file):

```csharp
    [Fact]
    public void GeneratedHooks_DeclareExactlyTheCanonicalSubcommands()
    {
        foreach (var hook in new[]
                 {
                     HookScriptTemplates.ClaudeHook,
                     HookScriptTemplates.GeminiHook,
                     HookScriptTemplates.CopilotCliHook
                 })
        {
            var expectedTuple =
                $"_DTK_SUBCOMMANDS = ({string.Join(", ", DotnetSubcommands.Sorted.Select(s => $"\"{s}\""))})";

            hook.Should().Contain(expectedTuple);
        }
    }

    [Fact]
    public void GeneratedHooks_DocumentEveryCanonicalSubcommand()
    {
        var alternation = string.Join("|", DotnetSubcommands.Ordered);

        HookScriptTemplates.ClaudeHook.Should().Contain($"rewrites `dotnet {alternation}`");
        HookScriptTemplates.GeminiHook.Should().Contain($"rewrites `dotnet {alternation}`");
        HookScriptTemplates.CopilotCliHook.Should().Contain($"rewrites `dotnet {alternation}`");
    }
```

- [ ] **Step 2: Run them to verify they pass against the *current* hardcoded text**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~SubcommandBindingTests"`
Expected: 4 tests PASS.

This is the point of doing it in this order: these two tests assert that the generated form equals
the hardcoded form. Passing *before* the change proves the expected strings are exactly what is
committed today, so any diff after Step 3 is a real regression rather than a moved goalpost.

- [ ] **Step 3: Add the generated fragments**

At the top of the `HookScriptTemplates` class body in
`src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs`, above `ClaudeHeader`, add
(and add `using DotnetTokenKiller.Domain;` at the top of the file):

```csharp
    /// <summary>
    /// The subcommands as a Python tuple body, alphabetically ordered so the generated bytes are
    /// stable no matter how <see cref="DotnetSubcommands.Ordered"/> is arranged.
    /// </summary>
    private static readonly string SubcommandTuple =
        string.Join(", ", DotnetSubcommands.Sorted.Select(subcommand => $"\"{subcommand}\""));

    /// <summary>The subcommands as a <c>build|test|…</c> alternation for the hook docstrings, in display order.</summary>
    private static readonly string SubcommandAlternation = string.Join("|", DotnetSubcommands.Ordered);
```

- [ ] **Step 4: Convert the four literals**

`ClaudeHeader`, `GeminiHeader`, and `CopilotCliHeader` change from `private const string` to
`private static readonly string`, and from `""""` to `$$""""`. In each, the hardcoded
`build|test|restore|clean|format` becomes `{{SubcommandAlternation}}`. For example `ClaudeHeader`:

```csharp
    /// <summary>4-quote raw string literals so embedded Python triple-quoted docstrings need no escaping.</summary>
    private static readonly string ClaudeHeader = $$""""
        #!/usr/bin/env python3
        """Claude Code PreToolUse hook: rewrites `dotnet {{SubcommandAlternation}}` to `dtk dotnet ...`.

        Reads the Bash tool input from stdin (JSON with a "tool_input" object whose
        "command" field holds the shell command) and, when a qualifying dotnet command
        is found, emits the PreToolUse `updatedInput` payload so Claude Code executes
        the rewritten command. Prints nothing when no rewrite is needed.
        """


        """";
```

Apply the same two changes to `GeminiHeader` (line 33) and `CopilotCliHeader` (line 171), touching
only the `build|test|restore|clean|format` run in their first docstring line.

`SharedCore` likewise becomes `private static readonly string SharedCore = $$""""`, with line 54
changing to:

```python
        _DTK_SUBCOMMANDS = ({{SubcommandTuple}})
```

Every other line of `SharedCore` — including `_BOUNDARY_CHARS`, `_inside_quotes`, and `rewrite` —
stays byte-for-byte as it is.

`ClaudeMain`, `GeminiMain`, and `CopilotCliMain` are **not** converted: they contain
`json.dumps({...})` and need no generation. Leave them as `const`.

Note: `SubcommandTuple` and `SubcommandAlternation` must appear textually above `ClaudeHeader`, per
the declaration-order detail above.

- [ ] **Step 5: Run the tests and confirm the generated bytes are unchanged**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all PASS — including the existing `ClaudeCodeIntegratorTests`, `GeminiCliIntegratorTests`,
and `CopilotCliIntegratorTests`, which write the hooks to temp dirs and assert on their content.
Those passing is the strongest available signal that generation reproduced the originals exactly.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs \
        tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs
git commit -m "refactor: generate the hook subcommand tuple from the canonical list"
```

---

### Task 5: Lock this repo's own committed hook to the generated one

**Files:**
- Modify: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` (add a fact)
- Possibly modify: `.claude/hooks/dotnet-to-dtk.py` (only if it is already stale)

**Interfaces:**
- Consumes: `HookScriptTemplates.ClaudeHook`.
- Produces: nothing.

This closes the fourth copy. `HookScriptTemplates`' own doc comment already claims the repo file is
"verbatim identical to `.claude/hooks/dotnet-to-dtk.py`" — nothing enforces that claim today.

- [ ] **Step 1: Write the test**

Add to `SubcommandBindingTests`:

```csharp
    [Fact]
    public void RepoClaudeHook_IsByteIdenticalToTheGeneratedHook()
    {
        var repoRoot = FindRepoRoot();
        var hookPath = Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py");

        File.Exists(hookPath).Should().BeTrue("this repo ships its own copy of the Claude hook at {0}", hookPath);

        var committed = File.ReadAllText(hookPath);

        committed.Should().Be(
            HookScriptTemplates.ClaudeHook,
            "the committed hook must be regenerated whenever the template changes, or this repo's own "
            + "agent sessions silently stop rewriting the newest subcommand");
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>DotnetTokenKiller.slnx</c>.
    /// xunit 2.x has no runtime skip, and the test project only ever runs from inside the repo,
    /// so not finding it is a failure rather than a skip.
    /// </summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
```

- [ ] **Step 2: Run it**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~RepoClaudeHook"`
Expected: PASS.

**If it fails**, the committed hook was already out of sync before this work — a real finding, not a
test bug. Read the FluentAssertions diff, confirm the only differences are ones the generator did
not introduce, then overwrite `.claude/hooks/dotnet-to-dtk.py` with the generated content and
mention the drift in the commit message. Do **not** weaken the assertion to `Contain` to make it
pass.

- [ ] **Step 3: Confirm the file on disk is untouched**

Run: `git status --short .claude/hooks/dotnet-to-dtk.py`
Expected: empty output. A non-empty result means Task 4's generation changed the bytes and needs
investigating before this is committed.

- [ ] **Step 4: Commit**

```bash
git add tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs
git commit -m "test: lock the repo's committed Claude hook to the generated template"
```

---

### Task 6: Prove the bindings are not vacuous, then finish

**Files:**
- Temporarily modify: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`
- Modify: `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md:84-99`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

Five of the seven new tests were green the moment they were written. A binding test that cannot
fail is worse than no test, because it reads as coverage. This task establishes that each one bites.

- [ ] **Step 1: Break the canonical list**

In `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`, temporarily change `Ordered` to omit
`Format`:

```csharp
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean];
```

- [ ] **Step 2: Run the whole suite and record which tests fail**

Run: `dtk dotnet test DotnetTokenKiller.slnx`

Expected FAILURES — all of these, by name:

| Test | Why it must fail |
|---|---|
| `DotnetSubcommandsTests.Ordered_IsTheDisplayOrderUsedByTheCli` | literal list no longer matches |
| `DotnetSubcommandsTests.FilterKeys_ExposesExactlyTheCanonicalSubcommands` | `FilterKeys.Format` has no canonical entry |
| `SubcommandBindingTests.NoFilter_IsRegisteredUnderANonCanonicalKey` | a filter is keyed `"format"` with no canonical entry |
| `SubcommandBindingTests.GeneratedHooks_DeclareExactlyTheCanonicalSubcommands` | generated tuple drops `"format"` |
| `SubcommandBindingTests.GeneratedHooks_DocumentEveryCanonicalSubcommand` | docstring alternation drops `format` |
| `SubcommandBindingTests.RepoClaudeHook_IsByteIdenticalToTheGeneratedHook` | committed file still has `format` |
| `SubcommandRegistrationTests.DotnetBranch_RegistersExactlyTheCanonicalSubcommands` | help still lists `format` |

`EverySubcommand_ResolvesAKeyedOutputFilter` is expected to still **pass** — it only checks that
each canonical subcommand has a filter, and a shorter list trivially satisfies it. That is why
`NoFilter_IsRegisteredUnderANonCanonicalKey` exists as its converse; both are needed.

If any other test in the table passes, that binding is vacuous. Fix the test before continuing.

- [ ] **Step 3: Restore the canonical list**

Revert `Ordered` to `[Build, Test, Restore, Clean, Format]`.

Run: `git diff src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`
Expected: empty. Nothing from this experiment may be committed.

- [ ] **Step 4: Full verification**

Run each and confirm before claiming completion:

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
jb inspectcode DotnetTokenKiller.slnx --output=artifacts/inspectcode.xml --format=Xml
git status --short
```

Expected: build clean (no analyzer errors), all tests pass, format reports no changes,
`inspectcode` reports no new issues against the touched files, and
`git status` shows no unexpected modifications — in particular `.claude/hooks/dotnet-to-dtk.py`
and every `Snapshots/*.verified.txt` must be untouched.

- [ ] **Step 5: Mark §7 done in the gap analysis**

In `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`, replace the §7 table's "Bound to
canonical?" column values with the new state and add one line under the table:

```markdown
> **Resolved 2026-07-27.** The canonical list now lives in `DotnetTokenKiller.Domain.DotnetSubcommands`.
> `FilterKeys` aliases its constants, the Cli copy is gone, the hook's tuple and docstrings are
> generated from it, and binding tests cover the DI keys, the Spectre registrations, the generated
> hook, and this repo's committed `.claude/hooks/dotnet-to-dtk.py`.
> See [the design](2026-07-27-subcommand-single-source-of-truth-design.md).
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md
git commit -m "docs: mark gap-analysis §7 resolved"
```

---

## Out of Scope

- **Multi-word subcommands** (`list package`, `ef migrations`). The hook's `_PATTERN` joins the list
  into a regex alternation, where a token containing a space, and the ordering of a prefix against
  its extension, both need care. §1 of the gap analysis will hit this.
- Changing which subcommands dtk supports.
- Generating `.claude/hooks/dotnet-to-dtk.py` at build time. The Task 5 parity test catches drift
  without build machinery.

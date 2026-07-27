# `dotnet list package` Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Filter all four `dotnet list package` output variants, and generalize dtk's subcommand
routing from single tokens to token sequences so multi-token subcommands are possible at all.

**Architecture:** A subcommand becomes an ordered token sequence matched longest-first, exposed as
`DotnetSubcommands.TryMatch`. `ArgumentPreprocessor`, `FilteredRunUseCase`'s tracking slug, the
generated hooks, and shell completion all move onto it. The filter itself is a conventional
`IOutputFilter` that detects its variant from marker phrases in the output and parses package tables
by mapping row fields onto the preceding header row's column names.

**Tech Stack:** net10.0, Spectre.Console.Cli, xunit, FluentAssertions, Verify.Xunit (snapshots),
`GeneratedRegex` source-generated patterns.

**Spec:** [2026-07-27-list-package-filter-design.md](../specs/2026-07-27-list-package-filter-design.md)

## Global Constraints

- Target framework `net10.0`; `TreatWarningsAsErrors` is on — every analyzer warning is a build error.
- Central package management: all versions live in `Directory.Packages.props`, never in `.csproj`.
- File-scoped namespaces; `var` throughout; private fields `_camelCase`; async methods end `Async`.
- LF line endings, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Filter output is normalized to `\n` (`StringBuilder.AppendLine` emits `\r\n` on Windows).
- A filter returns `string.Empty` for a failed run it could not parse, so `FilteredRunUseCase`'s
  raw-tail fallback surfaces the real output.
- Density cap: **30** package groups, then an explicit `… and N more` line.
- "Shared" means present in **every** project at the **same version** — strict, not majority.
- `✓` is emitted only when `exitCode == 0` **and** there are no findings.
- Console output is the parsing substrate. Do **not** add `--format json` to any invocation.

## Deviations from the spec (read before starting)

Three gaps were found while planning. All three are additions the spec does not mention, and are
included here because the spec's own success criterion cannot be met without the first.

1. **`FilteredRunUseCase` records the wrong tracking slug.** `FilteredRunUseCase.cs:64` is
   `var commandSlug = args.Count > 0 ? args[0] : command;`. For `list package` that records
   `"list"`, so the filtered runs would never line up with the `list package` rows the coverage
   report already holds — the spec's before/after comparison would compare two different keys.
   Fixed in Task 10.
2. **Shell completion generates broken script.** `CompletionCommand.cs:216-220` emits
   `complete -c dtk ... -a {sub} -d '...'` unquoted; with `sub` = `list package`, fish receives
   `-a list package` and the generated script is malformed. Bash's `-W` word list would also offer
   `package` as a standalone candidate. Fixed in Task 8 by completing on first tokens.
3. **Multi-targeted projects emit one table per TFM.** Entries are deduplicated by
   (project, package, version…) and the TFM is discarded. When a project resolves a package to
   different versions per TFM, both rows survive but which TFM is which is lost. Accepted
   simplification; noted in Task 3.

### Corrections applied after Task 1 recon (2026-07-27)

The first implementer's recon found two consumers of `DotnetSubcommands.All` that the original
draft of this plan missed, both in test code. Three corrections follow, and they make the plan
strictly better rather than merely fixing it:

- **`All` is kept, not deleted.** `DotnetSubcommandsTests.All_ContainsEveryOrderedEntry_CaseInsensitively`
  and `CompletionCommandTests.ExecuteAsync_Script_OffersEveryDotnetSubcommand` both use it as a
  canonical-name *set*, which stays coherent when a name contains a space — that is a legitimate
  non-routing use. Deleting it would mean rewriting those tests for no gain. The hazard it posed
  (a future caller routing with it) is handled by a doc comment pointing at `TryMatch` instead.
  Consequence: **Task 1 becomes purely additive, so the tree builds between Tasks 1 and 2 and each
  commits independently.** Task 1's Step 5/6 and Task 2's Step 6 are amended accordingly.
- **`tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs` already exists** with four
  tests. Task 1 **appends** to it; it must not be created from scratch, which would silently delete
  them.
- **Two further pinned literals exist that Task 10 must update**, beyond the three already listed
  there. They are enumerated in Task 10 Step 1.

### Correction applied after Task 9 recon (2026-07-28)

Task 9's implementer found that `IntegrationInstructions.Intro` and `CopilotCliIntegrator` are **not**
the only places hardcoding the subcommand list in user-facing prose. Three more exist, in three
distinct shapes:

| Site | Shape |
|---|---|
| `CursorIntegrator.cs:17` | `build, test, restore, clean, and format` — byte-identical to `SubcommandProse` |
| `AiderIntegrator.cs:38,54` | `build/test/restore/clean/format` — slash-joined |
| `ClaudeCodeIntegrator.cs:64` | `` `dotnet build`, `test`, `restore`, `clean`, and `format` `` — backticked Oxford list |

All three are folded into Task 9, because leaving them uncovered means Task 10 makes `list package`
canonical while dtk's own instructions still advertise only five subcommands — exactly the silent
failure §7 of the gap analysis describes. Each new generated form takes its own pinned literal in
`SubcommandBindingTests`, preserving the tripwire property.

Also from that recon: `UsageBody` must stay `const`. CA1802 forces it, because its own text
interpolates no computed value. Task 9's original text saying to convert it was wrong.

## File Structure

| File | Responsibility |
|---|---|
| `src/DotnetTokenKiller.Domain/SubcommandMatch.cs` | **new** — value type: canonical name + token count |
| `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs` | canonical list; gains `TryMatch`, keeps `All` |
| `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs` | gains `ListPackage` |
| `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs` | **new** — the filter |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | tracking slug via `TryMatch` |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | keyed filter registration |
| `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs` | `\s+`-joined hook regex |
| `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` | prose derived from canonical list |
| `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs` | prose alternation |
| `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs` | routing via `TryMatch` |
| `src/DotnetTokenKiller.Cli/CliConfigurator.cs` | nested `dotnet list` → `package` branch |
| `src/DotnetTokenKiller.Cli/Commands/DotnetListPackageCommand.cs` | **new** — Spectre command |
| `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs` | first-token completion candidates |
| `.claude/hooks/dotnet-to-dtk.py` | regenerated twice (Tasks 7 and 10) |

The filter is one file. It stays manageable by keeping parsing (header-driven field mapping) and
formatting (per-variant renderers) as separate private types inside it, mirroring
`DotnetRestoreFilter`'s `ParseState` + `FormatOutput` split.

---

## Task 1: `SubcommandMatch` and `DotnetSubcommands.TryMatch`

Longest-match-first token-sequence matching, with no change yet to the canonical list — so the whole
suite stays green.

**Files:**
- Create: `src/DotnetTokenKiller.Domain/SubcommandMatch.cs`
- Modify: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`
- Test: **append to the existing** `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`
  (it already holds four tests — do not recreate the file)

**Interfaces:**
- Produces: `SubcommandMatch(string Name, int TokenCount)` (readonly record struct);
  `DotnetSubcommands.TryMatch(IReadOnlyList<string> args, out SubcommandMatch match) → bool`;
  `internal DotnetSubcommands.TryMatch(IReadOnlyList<string> args, IReadOnlyList<string> candidateNames, out SubcommandMatch match) → bool`.

- [ ] **Step 1: Write the failing test**

**Append** these tests to the existing `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`,
inside its existing `DotnetSubcommandsTests` class, leaving its four current tests untouched. The
internal overload is the seam that lets multi-token matching be tested before any multi-token
subcommand exists:

```csharp
    [Fact]
    public void TryMatch_SingleTokenSubcommand_ReturnsNameAndOneToken()
    {
        DotnetSubcommands.TryMatch(["build", "MyApp.slnx"], out var match).Should().BeTrue();

        match.Name.Should().Be("build");
        match.TokenCount.Should().Be(1);
    }

    [Fact]
    public void TryMatch_IsCaseInsensitive_ButReturnsCanonicalCasing()
    {
        DotnetSubcommands.TryMatch(["BUILD"], out var match).Should().BeTrue();

        match.Name.Should().Be("build");
    }

    [Fact]
    public void TryMatch_UnknownSubcommand_ReturnsFalse()
    {
        DotnetSubcommands.TryMatch(["publish"], out var match).Should().BeFalse();

        match.Should().Be(default(SubcommandMatch));
    }

    [Fact]
    public void TryMatch_EmptyArgs_ReturnsFalse()
    {
        DotnetSubcommands.TryMatch([], out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatch_MultiTokenSubcommand_ConsumesBothTokens()
    {
        DotnetSubcommands.TryMatch(["list", "package", "--outdated"], ["list package"], out var match)
            .Should().BeTrue();

        match.Name.Should().Be("list package");
        match.TokenCount.Should().Be(2);
    }

    [Fact]
    public void TryMatch_PrefersTheLongestCandidate_RegardlessOfDeclarationOrder()
    {
        // "list" is declared first, but "list package" must win: a shorter candidate that is a
        // prefix of a longer one would otherwise shadow it and silently route to the wrong filter.
        DotnetSubcommands.TryMatch(["list", "package"], ["list", "list package"], out var match)
            .Should().BeTrue();

        match.Name.Should().Be("list package");
        match.TokenCount.Should().Be(2);
    }

    [Fact]
    public void TryMatch_MultiTokenCandidate_DoesNotMatchOnFirstTokenAlone()
    {
        DotnetSubcommands.TryMatch(["list", "reference"], ["list package"], out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatch_MultiTokenCandidate_DoesNotMatchATruncatedArgList()
    {
        DotnetSubcommands.TryMatch(["list"], ["list package"], out _).Should().BeFalse();
    }
```

The existing file already has the `using` directives, namespace, and class declaration these tests
need — append inside the class and add nothing else.

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~DotnetSubcommandsTests"
```

Expected: FAIL to **compile** — `TryMatch` and `SubcommandMatch` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Domain/SubcommandMatch.cs`:

```csharp
namespace DotnetTokenKiller.Domain;

/// <summary>A matched dtk-handled subcommand and how many argv tokens it consumed.</summary>
/// <param name="Name">The canonical, space-joined subcommand name, e.g. <c>list package</c>.</param>
/// <param name="TokenCount">
/// How many leading argv tokens the match consumed. Callers that need to look past the subcommand
/// — inserting a <c>--</c> separator, say — must use this rather than assuming 1.
/// </param>
public readonly record struct SubcommandMatch(string Name, int TokenCount);
```

In `DotnetSubcommands.cs`, **keep** `All` and add the matching API alongside it. `All` remains a
correct set of canonical *names* even when a name contains a space, and two existing tests use it
that way. What it cannot do is route an argv list, so amend its doc comment to say so and point at
`TryMatch`:

```csharp
    /// <summary>
    /// The canonical names as a case-insensitive set, for asserting membership and binding other
    /// restatements of the list back to it.
    /// </summary>
    /// <remarks>
    /// Not usable for routing an argument list: a multi-token name such as <c>list package</c> is one
    /// entry here, so <c>All.Contains(args[1])</c> would never match it. Use
    /// <see cref="TryMatch(IReadOnlyList{string}, out SubcommandMatch)"/> for that.
    /// </remarks>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);
```

Then add the matching API:

```csharp
    /// <summary>
    /// Candidate token sequences, longest first. Precomputed for the public <see cref="TryMatch"/>
    /// overload so routing every invocation does not re-split the canonical list.
    /// </summary>
    private static readonly IReadOnlyList<string[]> OrderedMatchers = BuildMatchers(Ordered);

    /// <summary>
    /// Matches the leading tokens of <paramref name="args"/> against the canonical subcommands.
    /// </summary>
    /// <param name="args">Arguments starting at the subcommand, e.g. <c>["list", "package", "--outdated"]</c>.</param>
    /// <param name="match">The canonical name and consumed token count, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when a canonical subcommand matched.</returns>
    public static bool TryMatch(IReadOnlyList<string> args, out SubcommandMatch match) =>
        TryMatchCore(args, OrderedMatchers, out match);

    /// <summary>
    /// Matches against an explicit candidate list. Exists so multi-token matching is testable
    /// independently of whichever subcommands happen to be canonical today.
    /// </summary>
    /// <param name="args">Arguments starting at the subcommand.</param>
    /// <param name="candidateNames">Canonical names to match against, space-separated for multi-token.</param>
    /// <param name="match">The canonical name and consumed token count, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when a candidate matched.</returns>
    internal static bool TryMatch(
        IReadOnlyList<string> args,
        IReadOnlyList<string> candidateNames,
        out SubcommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(candidateNames);
        return TryMatchCore(args, BuildMatchers(candidateNames), out match);
    }

    private static IReadOnlyList<string[]> BuildMatchers(IReadOnlyList<string> names) =>
        [.. names.Select(name => name.Split(' ')).OrderByDescending(tokens => tokens.Length)];

    private static bool TryMatchCore(
        IReadOnlyList<string> args,
        IReadOnlyList<string[]> matchers,
        out SubcommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var tokens in matchers)
        {
            if (StartsWith(args, tokens))
            {
                match = new SubcommandMatch(string.Join(' ', tokens), tokens.Length);
                return true;
            }
        }

        match = default;
        return false;
    }

    private static bool StartsWith(IReadOnlyList<string> args, string[] tokens)
    {
        if (args.Count < tokens.Length)
        {
            return false;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!string.Equals(args[i], tokens[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
```

The test project needs access to the internal overload. Check
`src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj` for an `InternalsVisibleTo` entry for
`DotnetTokenKiller.Domain.Tests`; if absent, add:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="DotnetTokenKiller.Domain.Tests"/>
  </ItemGroup>
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~DotnetSubcommandsTests"
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Verify nothing else broke**

```bash
dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx
```

Expected: build succeeds and the full suite passes. This task is purely additive — `All` is retained
and nothing yet calls `TryMatch` — so a failure here means the new members broke something
unexpected, not that a later task will fix it.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Domain tests/DotnetTokenKiller.Domain.Tests
git commit -m "feat: match dotnet subcommands as token sequences"
```

---

## Task 2: Route through `TryMatch` in `ArgumentPreprocessor`

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`

**Interfaces:**
- Consumes: `DotnetSubcommands.TryMatch(args, out SubcommandMatch)` from Task 1.

Note the index shift: `TryMatch` takes arguments **starting at the subcommand**, but
`ArgumentPreprocessor` receives them starting at the `dotnet` driver token. Every call therefore
passes a slice from index 1, and `InsertSeparator` splits at `1 + match.TokenCount`.

- [ ] **Step 1: Write the failing test**

Append to `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`. Read the file
first and match its existing naming style.

```csharp
    [Fact]
    public void IsPassthrough_ListReference_IsPassthrough()
    {
        // `list package` will be filtered but `list reference` must never be: it is the case that
        // makes first-token matching wrong. The guarantee is dispatch order — IsPassthrough runs
        // before Spectre, so this never reaches the `dotnet list` branch.
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list", "reference"]).Should().BeTrue();
    }

    [Fact]
    public void IsPassthrough_BareList_IsPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list"]).Should().BeTrue();
    }

    [Fact]
    public void IsPassthrough_KnownSingleTokenSubcommand_IsNotPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "build"]).Should().BeFalse();
    }

    [Fact]
    public void InsertSeparator_SingleTokenSubcommand_SeparatesAfterOneToken()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "build", "MyApp.slnx"])
            .Should().Equal("dotnet", "build", "--", "MyApp.slnx");
    }

    [Fact]
    public void Normalize_UppercaseSubcommand_IsCanonicalized()
    {
        ArgumentPreprocessor.Normalize(["DOTNET", "BUILD"]).Should().Equal("dotnet", "build");
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~ArgumentPreprocessorTests"
```

Expected: the three new `IsPassthrough`/`InsertSeparator`/`Normalize` assertions PASS already,
because the existing single-token behaviour satisfies them — `list reference` and bare `list` are
passthrough today for the same reason they must stay passthrough afterwards. They are the regression
guards that prove this refactor changes nothing for the existing five. Confirm they pass **before**
you touch `ArgumentPreprocessor`, so that their passing afterwards means something.

- [ ] **Step 3: Write minimal implementation**

Replace the three membership tests in `ArgumentPreprocessor.cs`. `Normalize`:

```csharp
    internal static string[] Normalize(string[] args)
    {
        if (args.Length < 2 ||
            !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||
            !DotnetSubcommands.TryMatch(args[1..], out var match))
        {
            return args;
        }

        var canonicalTokens = match.Name.Split(' ');
        var alreadyCanonical =
            string.Equals(args[0], DotnetCommand, StringComparison.Ordinal) &&
            !canonicalTokens.Where((token, i) => !string.Equals(args[1 + i], token, StringComparison.Ordinal)).Any();

        if (alreadyCanonical)
        {
            return args;
        }

        var normalized = (string[])args.Clone();
        normalized[0] = DotnetCommand;
        for (var i = 0; i < canonicalTokens.Length; i++)
        {
            normalized[1 + i] = canonicalTokens[i];
        }

        return normalized;
    }
```

`IsPassthrough`:

```csharp
    internal static bool IsPassthrough(string[] args)
    {
        return args.Length >= 2 &&
               string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) &&
               !DotnetSubcommands.TryMatch(args[1..], out _);
    }
```

`InsertSeparator` — the loop start moves from the hardcoded `2` to `1 + match.TokenCount`, and the
preserved prefix copies that many tokens:

```csharp
    internal static string[] InsertSeparator(string[] args)
    {
        if (args.Length <= 2 ||
            !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||
            !DotnetSubcommands.TryMatch(args[1..], out var match))
        {
            return args;
        }

        var firstArgIndex = 1 + match.TokenCount;
        if (args.Length <= firstArgIndex)
        {
            return args;
        }

        var dtkFlags = new List<string>();
        var dotnetArgs = new List<string>();
        var sawUserSeparator = false;
        for (var i = firstArgIndex; i < args.Length; i++)
        {
            // Once the user's own "--" is seen, it and everything after it is forwarded
            // verbatim — never reinterpreted as a dtk flag.
            if (!sawUserSeparator && string.Equals(args[i], "--", StringComparison.Ordinal))
            {
                sawUserSeparator = true;
            }

            if (!sawUserSeparator && DtkOptions.Contains(args[i]))
            {
                dtkFlags.Add(args[i]);
            }
            else
            {
                dotnetArgs.Add(args[i]);
            }
        }

        if (dotnetArgs.Count == 0)
        {
            return args;
        }

        var updated = new List<string>(args.Length + 1);
        updated.AddRange(args[..firstArgIndex]);
        updated.AddRange(dtkFlags);
        updated.Add("--");
        updated.AddRange(dotnetArgs);
        return [.. updated];
    }
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~ArgumentPreprocessorTests"
```

Expected: PASS, including every pre-existing test in the file. Those pre-existing tests are the real
deliverable here — this is a refactor, and their passing unchanged is what proves the five existing
subcommands still route identically.

- [ ] **Step 5: Verify nothing else broke**

```bash
dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx
```

Expected: build succeeds; full suite passes (1067 pre-existing tests plus the 13 added in Tasks 1–2).

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs tests/DotnetTokenKiller.Cli.IntegrationTests
git commit -m "refactor: route dotnet subcommands through token-sequence matching"
```

---

## Task 3: `DotnetListPackageFilter` — plain variant

The filter is built variant by variant across Tasks 3–6, registered nowhere until Task 10. Keeping
registration out until the end is deliberate:
`SubcommandBindingTests.NoFilter_IsRegisteredUnderANonCanonicalKey` fails the moment a filter is
registered under a key that is not yet canonical.

**Files:**
- Create: `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_raw.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetListPackageFilterTests.cs`

**Interfaces:**
- Produces: `DotnetListPackageFilter` with a **parameterless** constructor, implementing
  `IOutputFilter.Apply(string rawOutput, int exitCode) → string`. The parameterless constructor
  matters: Task 10 registers it with `AddKeyedTransient<IOutputFilter, DotnetListPackageFilter>`,
  which cannot supply arguments.

- [ ] **Step 1: Capture the fixture**

Run from the repo root. The absolute SDK path bypasses the rewrite hook, which would otherwise
rewrite the inner `dotnet` and fail:

```bash
/usr/share/dotnet/dotnet dotnet list package \
  > tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_raw.txt 2>&1
wc -lc tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_raw.txt
```

Expected: roughly 107 lines / 7892 bytes. `Fixtures/**/*.txt` is already globbed as an
`EmbeddedResource`, so no `.csproj` edit is needed.

- [ ] **Step 2: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetListPackageFilterTests.cs`:

```csharp
using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetListPackageFilterTests
{
    private readonly DotnetListPackageFilter _sut = new();

    [Fact]
    public Task Apply_PlainFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_PlainFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_list_package_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "list package filter should achieve ≥70% savings");
    }

    [Fact]
    public void Apply_PlainFixture_HoistsPackagesSharedByEveryProject()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0);

        result.Should().Contain("all projects:")
            .And.Contain("SonarAnalyzer.CSharp 10.29.0.143774");
    }

    [Theory]
    [InlineData("Determining projects to restore")]
    [InlineData("All projects are up-to-date for restore")]
    [InlineData("Top-level Package")]
    [InlineData("has the following package references")]
    public void Apply_PlainFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        _sut.Apply(LoadFixture("dotnet_list_package_raw.txt"), exitCode: 0)
            .Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_PlainOutput_OmitsProjectsThatAddNothingBeyondTheSharedSet()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0
                               > OnlyAlpha            2.0.0       2.0.0

                            Project 'Beta' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Shared               1.0.0       1.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("all projects: Shared 1.0.0")
            .And.Contain("Alpha: OnlyAlpha 2.0.0");
        result.Should().NotContain("Beta:", "Beta adds nothing beyond the shared set");
    }

    [Fact]
    public void Apply_NoSharedPackages_DegradesToPerProjectListing()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > OnlyAlpha            2.0.0       2.0.0

                            Project 'Beta' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > OnlyBeta             3.0.0       3.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().NotContain("all projects:");
        result.Should().Contain("Alpha: OnlyAlpha 2.0.0").And.Contain("Beta: OnlyBeta 3.0.0");
    }

    [Fact]
    public void Apply_EmptyOutput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetListPackageFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: FAIL to compile — `DotnetListPackageFilter` does not exist.

- [ ] **Step 4: Write minimal implementation**

Create `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs`. This implements parsing
plus the plain renderer only; Tasks 4–6 add the other renderers.

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet list package</c> output, in all four of its variants.</summary>
/// <remarks>
/// The variant is detected from marker phrases in the output rather than from arguments, because
/// <see cref="IOutputFilter.Apply"/> does not receive them. Table rows are mapped onto the column
/// names of the header row above them, which is what makes multi-word cell values
/// (<c>Critical Bugs</c>) and the <c>Transitive Package</c> sub-table's missing <c>Requested</c>
/// column parse correctly.
/// </remarks>
public sealed partial class DotnetListPackageFilter : IOutputFilter
{
    private const int MaxGroups = 30;

    /// <summary>Applies the filter to raw <c>dotnet list package</c> output.</summary>
    /// <param name="rawOutput">The raw output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var state = Parse(AnsiStrip.Strip(rawOutput).Split(["\r\n", "\n"], StringSplitOptions.None));

        // Nothing recognizable was parsed. Returning empty lets FilteredRunUseCase's raw-tail
        // fallback surface the real output on a failure, and is honest on success too.
        return state.Variant switch
        {
            Variant.Plain => FormatPlain(state, exitCode),
            _ => string.Empty
        };
    }

    private static ParseState Parse(string[] lines)
    {
        var state = new ParseState();
        string[] columns = [];
        var project = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            var header = ProjectHeaderPattern().Match(line);
            if (header.Success)
            {
                project = header.Groups["proj"].Value;
                state.Projects.Add(project);
                ApplyProjectHeader(state, header.Groups["what"].Value);
                columns = [];
                continue;
            }

            var trimmed = line.TrimStart();

            // A table header, e.g. "Top-level Package   Requested   Resolved".
            if (!trimmed.StartsWith('>') && trimmed.Contains("Package", StringComparison.Ordinal))
            {
                columns = SplitCells(line);
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) && columns.Length > 0)
            {
                AddEntry(state, project, columns, SplitCells(trimmed[2..]));
            }
        }

        return state;
    }

    private static void ApplyProjectHeader(ParseState state, string what)
    {
        if (what.Contains("package references", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Plain;
        }
        else if (what.Contains("updates to its packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Outdated;
        }
        else if (what.Contains("deprecated packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Deprecated;
            TrackCleanProject(state, what);
        }
        else if (what.Contains("vulnerable packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Vulnerable;
            TrackCleanProject(state, what);
        }
    }

    private static void TrackCleanProject(ParseState state, string what)
    {
        if (what.StartsWith("no ", StringComparison.OrdinalIgnoreCase))
        {
            state.CleanProjects++;
        }
    }

    private static void AddEntry(ParseState state, string project, string[] columns, string[] cells)
    {
        if (cells.Length == 0)
        {
            return;
        }

        state.Entries.Add(new Entry(
            project,
            cells[0],
            Cell(columns, cells, "Requested"),
            Cell(columns, cells, "Resolved"),
            Cell(columns, cells, "Latest"),
            Cell(columns, cells, "Reason(s)"),
            Cell(columns, cells, "Alternative"),
            Cell(columns, cells, "Severity"),
            Cell(columns, cells, "Advisory URL")));
    }

    /// <summary>Reads the cell under <paramref name="column"/>, or empty when that column is absent.</summary>
    /// <param name="columns">Header cells for the table this row belongs to.</param>
    /// <param name="cells">The row's cells, index-aligned with <paramref name="columns"/>.</param>
    /// <param name="column">The header name to look up.</param>
    private static string Cell(string[] columns, string[] cells, string column)
    {
        var index = Array.FindIndex(columns, c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < cells.Length ? cells[index] : string.Empty;
    }

    /// <summary>Splits a padded table line on runs of two or more spaces.</summary>
    /// <param name="line">The line to split.</param>
    private static string[] SplitCells(string line) =>
        [.. CellSeparatorPattern().Split(line.Trim()).Select(cell => cell.Trim()).Where(cell => cell.Length > 0)];

    /// <summary>Renders <c>Resolved</c>, or <c>requested→resolved</c> when a floating version moved.</summary>
    /// <param name="entry">The parsed row.</param>
    private static string Version(Entry entry) =>
        entry.Requested.Length > 0 &&
        !string.Equals(entry.Requested, entry.Resolved, StringComparison.OrdinalIgnoreCase)
            ? $"{entry.Requested}→{entry.Resolved}"
            : entry.Resolved;

    private static string FormatPlain(ParseState state, int exitCode)
    {
        if (state.Entries.Count == 0)
        {
            return exitCode == 0
                ? $"✓ dotnet list package ({state.Projects.Count} project{Plural(state.Projects.Count)}, 0 packages)\n"
                : string.Empty;
        }

        var byProject = state.Entries
            .GroupBy(e => e.Project, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => $"{e.Id} {Version(e)}").ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        // Strict all-or-nothing: present in every project at the same version. A near-universal
        // package stays on its projects' own lines rather than becoming an "except X" special case.
        var shared = byProject.Count > 1
            ? byProject.Values.Skip(1).Aggregate(
                new HashSet<string>(byProject.Values.First(), StringComparer.Ordinal),
                (acc, next) =>
                {
                    acc.IntersectWith(next);
                    return acc;
                })
            : [];

        var distinct = byProject.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count();

        var sb = new StringBuilder();
        var glyph = exitCode == 0 ? "✓ " : string.Empty;
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{glyph}dotnet list package ({state.Projects.Count} project{Plural(state.Projects.Count)}, {distinct} package{Plural(distinct)})");

        var groups = 0;
        var omitted = 0;

        if (shared.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  all projects: {string.Join(", ", shared.Order(StringComparer.Ordinal))}");
            groups++;
        }

        foreach (var (project, packages) in byProject.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var own = packages.Except(shared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (own.Count == 0)
            {
                continue;
            }

            if (groups >= MaxGroups)
            {
                omitted += own.Count;
                continue;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"  {project}: {string.Join(", ", own)}");
            groups++;
        }

        AppendTruncation(sb, omitted);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Appends the explicit truncation line. Truncation is always stated, never silent.</summary>
    /// <param name="sb">The buffer being built.</param>
    /// <param name="omitted">How many packages were dropped; nothing is appended when zero.</param>
    private static void AppendTruncation(StringBuilder sb, int omitted)
    {
        if (omitted > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  … and {omitted} more package{Plural(omitted)} (use --show-log for full output)");
        }
    }

    private static string Plural(int count) => count == 1 ? "" : "s";

    // "Project 'Name' has the following package references"  (plain uses single quotes)
    // "Project `Name` has the following updates to its packages"  (audit variants use backticks)
    // "The given project `Name` has no deprecated packages given the current sources."
    [GeneratedRegex(@"^(?:The given project|Project)\s+['`](?<proj>[^'`]+)['`]\s+has\s+(?<what>.+?)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProjectHeaderPattern();

    // Padded table columns are separated by two or more spaces.
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CellSeparatorPattern();

    private enum Variant
    {
        Unknown,
        Plain,
        Outdated,
        Deprecated,
        Vulnerable
    }

    private sealed record Entry(
        string Project,
        string Id,
        string Requested,
        string Resolved,
        string Latest,
        string Reason,
        string Alternative,
        string Severity,
        string Advisory);

    private sealed class ParseState
    {
        public Variant Variant { get; set; }
        public HashSet<string> Projects { get; } = new(StringComparer.Ordinal);
        public List<Entry> Entries { get; } = [];
        public int CleanProjects { get; set; }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: the snapshot test FAILS on first run — Verify writes a `.received.txt` and no
`.verified.txt` exists yet. **Read the received file** and confirm it matches the spec's target shape,
then accept it:

```bash
cd tests/DotnetTokenKiller.Application.Tests/Filters
mv DotnetListPackageFilterTests.Apply_PlainFixture_MatchesSnapshot.received.txt \
   DotnetListPackageFilterTests.Apply_PlainFixture_MatchesSnapshot.verified.txt
```

Re-run: all tests PASS. Never accept a snapshot without reading it — an approved snapshot of wrong
output is a silently wrong test.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs \
        tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: add dotnet list package filter (plain variant)"
```

---

## Task 4: `--outdated` variant

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_outdated_raw.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetListPackageFilterTests.cs`

**Interfaces:**
- Consumes: `Entry`, `ParseState`, `Variant`, `Cell`, `Version`, `Plural`, `AppendTruncation`,
  `MaxGroups` from Task 3.
- Produces: `FormatAudit(ParseState state, int exitCode, string flag, (string Singular, string Plural) noun, Func<Entry, string> detail) → string`
  — the shared renderer Tasks 4 and 5 both use.

- [ ] **Step 1: Capture the fixture**

```bash
/usr/share/dotnet/dotnet dotnet list package --outdated \
  > tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_outdated_raw.txt 2>&1
```

Expected: roughly 44 lines / 2231 bytes. Requires network access to nuget.org.

- [ ] **Step 2: Write the failing test**

Append to `DotnetListPackageFilterTests`:

```csharp
    [Fact]
    public Task Apply_OutdatedFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_outdated_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_Outdated_GroupsOnePackageAcrossProjects()
    {
        const string input = """
                            Project `Alpha` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0

                            Project `Beta` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("1 package with updates (all 2 projects)")
            .And.Contain("Analyzer 1.0.0 → 2.0.0 (2 projects)");
    }

    [Fact]
    public void Apply_Outdated_SplitsDistinctTransitionsForTheSamePackage()
    {
        const string input = """
                            Project `Alpha` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.0.0       1.0.0      2.0.0

                            Project `Beta` has the following updates to its packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Latest
                               > Analyzer             1.5.0       1.5.0      2.0.0
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("Analyzer 1.0.0 → 2.0.0 (Alpha)")
            .And.Contain("Analyzer 1.5.0 → 2.0.0 (Beta)");
    }

    [Fact]
    public void Apply_Outdated_NoUpdates_CollapsesToOneLine()
    {
        const string input = """
                              Determining projects to restore...
                              All projects are up-to-date for restore.

                            The following sources were used:
                               https://api.nuget.org/v3/index.json

                            The given project `Alpha` has no updates given the current sources.
                            """;

        _sut.Apply(input, exitCode: 0)
            .Should().Be("✓ dotnet list package --outdated (all 1 project up to date)\n");
    }
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: the four new tests FAIL — `Apply` returns empty for `Variant.Outdated`.

- [ ] **Step 4: Write minimal implementation**

First, `ApplyProjectHeader` must recognize the no-updates phrasing, which says "no updates" rather
than "updates to its packages". Add this branch **before** the `updates to its packages` branch, so
the more specific phrase wins:

```csharp
        else if (what.Contains("no updates", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Outdated;
            state.CleanProjects++;
        }
```

Add the shared audit renderer:

```csharp
    /// <summary>Renders an audit variant: findings grouped by package, else a single clean line.</summary>
    /// <param name="state">The parsed output.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="flag">The variant's flag, e.g. <c>--outdated</c>.</param>
    /// <param name="noun">Singular and plural forms of the finding noun.</param>
    /// <param name="detail">Renders the variant-specific detail for one entry.</param>
    private static string FormatAudit(
        ParseState state,
        int exitCode,
        string flag,
        (string Singular, string Plural) noun,
        Func<Entry, string> detail)
    {
        var projects = state.Projects.Count;

        if (state.Entries.Count == 0)
        {
            if (exitCode != 0)
            {
                return string.Empty;
            }

            var clean = string.Equals(flag, "--outdated", StringComparison.Ordinal)
                ? $"all {projects} project{Plural(projects)} up to date"
                : $"no {noun.Plural}, {projects} project{Plural(projects)}";
            return $"✓ dotnet list package {flag} ({clean})\n";
        }

        // Group by package plus its rendered detail, so rows that say different things never merge.
        var groups = state.Entries
            .GroupBy(e => (e.Id, Detail: detail(e)))
            .Select(g => (
                g.Key.Id,
                g.Key.Detail,
                Projects: g.Select(e => e.Project).Distinct(StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Id, StringComparer.Ordinal)
            .ThenBy(g => g.Detail, StringComparer.Ordinal)
            .ToList();

        var affected = state.Entries.Select(e => e.Project).Distinct(StringComparer.Ordinal).Count();
        var scope = affected == projects
            ? $"all {projects} project{Plural(projects)}"
            : $"{affected} of {projects} projects";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet list package {flag}: {groups.Count} {(groups.Count == 1 ? noun.Singular : noun.Plural)} ({scope})");

        var omitted = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            if (i >= MaxGroups)
            {
                omitted = groups.Count - MaxGroups;
                break;
            }

            var (id, det, projectNames) = groups[i];

            // A count alone would be useless for a single project, so name it.
            var where = projectNames.Count == 1 ? projectNames[0] : $"{projectNames.Count} projects";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {id} {det} ({where})");
        }

        AppendTruncation(sb, omitted);
        return sb.ToString().ReplaceLineEndings("\n");
    }
```

Wire it into the `Apply` switch:

```csharp
            Variant.Outdated => FormatAudit(
                state, exitCode, "--outdated", ("package with updates", "packages with updates"),
                entry => $"{entry.Resolved} → {entry.Latest}"),
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: read and accept the new snapshot, then all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: filter dotnet list package --outdated"
```

---

## Task 5: `--deprecated` and `--vulnerable` variants

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_deprecated_raw.txt`
- Create: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_vulnerable_raw.txt`
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetListPackageFilterTests.cs`

**Interfaces:**
- Consumes: `FormatAudit` from Task 4, `Version` from Task 3.
- Produces: `Arrow(string alternative) → string`.

- [ ] **Step 1: Capture the deprecated fixture and hand-write the vulnerable one**

```bash
/usr/share/dotnet/dotnet dotnet list package --deprecated \
  > tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_list_package_deprecated_raw.txt 2>&1
```

Expected: roughly 30 lines / 1785 bytes, with four `has no deprecated packages` lines and four
projects carrying findings (`xunit` in four, `Verify.Xunit` in two).

This repository has no vulnerable packages, so `dotnet_list_package_vulnerable_raw.txt` must be
written by hand. This is the spec's recorded known risk — the only variant shipping unverified
against real SDK output. Write exactly:

```
  Determining projects to restore...
  All projects are up-to-date for restore.

The following sources were used:
   https://api.nuget.org/v3/index.json

The given project `Alpha` has no vulnerable packages given the current sources.
Project `Beta` has the following vulnerable packages
   [net10.0]:
   Top-level Package        Requested   Resolved   Severity   Advisory URL
   > Fragile.Parser         1.0.0       1.0.0      High       https://github.com/advisories/GHSA-aaaa-bbbb-cccc
   > Legacy.Crypto          2.1.0       2.1.0      Critical   https://github.com/advisories/GHSA-dddd-eeee-ffff
```

- [ ] **Step 2: Write the failing test**

```csharp
    [Fact]
    public Task Apply_DeprecatedFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_VulnerableFixture_MatchesSnapshot()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_vulnerable_raw.txt"), exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_DeprecatedFixture_DropsThePerProjectCleanLines()
    {
        _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0)
            .Should().NotContain("has no deprecated packages");
    }

    [Fact]
    public void Apply_DeprecatedFixture_KeepsReasonAndAlternative()
    {
        _sut.Apply(LoadFixture("dotnet_list_package_deprecated_raw.txt"), exitCode: 0)
            .Should().Contain("xunit 2.9.3 — Legacy → xunit.v3 >= 0.0.0");
    }

    [Fact]
    public void Apply_VulnerableFixture_KeepsSeverityAndAdvisory()
    {
        var result = _sut.Apply(LoadFixture("dotnet_list_package_vulnerable_raw.txt"), exitCode: 0);

        result.Should().Contain("2 vulnerable packages (1 of 2 projects)")
            .And.Contain("Legacy.Crypto 2.1.0 — Critical https://github.com/advisories/GHSA-dddd-eeee-ffff");
    }

    [Fact]
    public void Apply_NoVulnerablePackages_CollapsesToOneLine()
    {
        const string input = """
                            The given project `Alpha` has no vulnerable packages given the current sources.
                            The given project `Beta` has no vulnerable packages given the current sources.
                            """;

        _sut.Apply(input, exitCode: 0)
            .Should().Be("✓ dotnet list package --vulnerable (no vulnerable packages, 2 projects)\n");
    }

    [Fact]
    public void Apply_MultiWordDeprecationReason_IsNotSplit()
    {
        const string input = """
                            Project `Alpha` has the following deprecated packages
                               [net10.0]:
                               Top-level Package      Requested   Resolved   Reason(s)       Alternative
                               > Risky                1.0.0       1.0.0      Critical Bugs   Safe >= 2.0.0
                            """;

        _sut.Apply(input, exitCode: 0).Should().Contain("Risky 1.0.0 — Critical Bugs → Safe >= 2.0.0");
    }
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: the new tests FAIL — both variants still return empty.

- [ ] **Step 4: Write minimal implementation**

Add the alternative-arrow helper:

```csharp
    /// <summary>Renders <c> → alternative</c>, or empty when the package suggests no replacement.</summary>
    /// <param name="alternative">The <c>Alternative</c> cell, which is often absent.</param>
    private static string Arrow(string alternative) =>
        alternative.Length > 0 ? $" → {alternative}" : string.Empty;
```

Wire both renderers into the `Apply` switch. Note `--deprecated` has no `Latest` column, and the arrow
separates reason from alternative:

```csharp
            Variant.Deprecated => FormatAudit(
                state, exitCode, "--deprecated", ("deprecated package", "deprecated packages"),
                entry => $"{Version(entry)} — {entry.Reason}{Arrow(entry.Alternative)}"),
            Variant.Vulnerable => FormatAudit(
                state, exitCode, "--vulnerable", ("vulnerable package", "vulnerable packages"),
                entry => $"{Version(entry)} — {entry.Severity} {entry.Advisory}".TrimEnd()),
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: read and accept both new snapshots, then all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs tests/DotnetTokenKiller.Application.Tests
git commit -m "feat: filter dotnet list package --deprecated and --vulnerable"
```

---

## Task 6: Transitive tables, floating versions, cap, and failure degradation

The edge cases that make the filter safe to turn on.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs` (only if a test fails)
- Test: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetListPackageFilterTests.cs`

- [ ] **Step 1: Write the failing test**

Add `using System.Text;` to the test file for the cap test's `StringBuilder`.

```csharp
    [Fact]
    public void Apply_TransitiveTable_ParsesRowsWithoutARequestedColumn()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Direct               1.0.0       1.0.0

                               Transitive Package                 Resolved
                               > Indirect                         3.2.1
                            """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("Direct 1.0.0").And.Contain("Indirect 3.2.1");
    }

    [Fact]
    public void Apply_FloatingVersion_ShowsRequestedAndResolved()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Floating             1.0.*       1.0.7
                            """;

        _sut.Apply(input, exitCode: 0).Should().Contain("Floating 1.0.*→1.0.7");
    }

    [Fact]
    public void Apply_MoreGroupsThanTheCap_StatesWhatWasOmitted()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 40; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Project 'P{i:D2}' has the following package references");
            sb.AppendLine("   [net10.0]:");
            sb.AppendLine("   Top-level Package      Requested   Resolved");
            sb.AppendLine(CultureInfo.InvariantCulture, $"   > Only{i:D2}                1.0.0       1.0.0");
            sb.AppendLine();
        }

        var result = _sut.Apply(sb.ToString(), exitCode: 0);

        result.Should().Contain("… and ")
            .And.Contain("more package")
            .And.Contain("use --show-log for full output");
    }

    [Fact]
    public void Apply_FailedRunWithUnparseableOutput_ReturnsEmptyForRawTailFallback()
    {
        const string input = "MSBUILD : error MSB1003: Specify a project or solution file.";

        _sut.Apply(input, exitCode: 1).Should().BeEmpty(
            "an empty result is what makes FilteredRunUseCase fall back to the raw tail");
    }

    [Fact]
    public void Apply_FailedRunWithParseableOutput_OmitsTheSuccessGlyph()
    {
        const string input = """
                            Project 'Alpha' has the following package references
                               [net10.0]:
                               Top-level Package      Requested   Resolved
                               > Direct               1.0.0       1.0.0
                            """;

        _sut.Apply(input, exitCode: 1).Should().NotContain("✓");
    }
```

Add `using System.Globalization;` to the test file as well, for the interpolated `AppendLine` calls.

- [ ] **Step 2: Run test to verify it fails or passes for the wrong reason**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: several of these already PASS against Task 3's implementation — the header-driven parser
already handles transitive tables, and `Version` already renders the arrow. That is fine; they are
regression guards for behaviour the spec requires. Any that FAIL indicate a real gap to close.

The cap test is the one genuinely expected to fail or to pass for the wrong reason: `FormatPlain`
counts the `all projects:` line as a group and then omits *packages*, so verify the emitted number is
the count of packages actually dropped, not of projects.

- [ ] **Step 3: Fix whatever failed**

Make only the changes the failing assertions demand. Do not restructure passing behaviour.

- [ ] **Step 4: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetListPackageFilterTests"
```

Expected: PASS.

- [ ] **Step 5: Verify nothing else broke**

```bash
dtk dotnet build DotnetTokenKiller.slnx && dtk dotnet test DotnetTokenKiller.slnx
```

Expected: full suite green. The filter is still registered nowhere, so nothing else can be affected.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Filters/DotnetListPackageFilter.cs tests/DotnetTokenKiller.Application.Tests
git commit -m "test: cover list package transitive, floating, cap, and failure paths"
```

---

## Task 7: Multi-token hook regex

Independent of activation: with only single-token subcommands canonical, the generated pattern's
*behaviour* is unchanged, but its bytes change — so the committed hook must be regenerated now.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs:68`
- Modify: `.claude/hooks/dotnet-to-dtk.py`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs`

- [ ] **Step 1: Write the failing test**

Read `tests/DotnetTokenKiller.Application.Tests/Integration/` first — if a test file for these
templates already exists under another name, append to it rather than creating a second one.

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public class HookScriptTemplatesTests
{
    [Fact]
    public void SharedCore_MatchesAnyWhitespaceBetweenSubcommandTokens()
    {
        // A tuple entry containing a literal space would match exactly one space, so
        // `dotnet list   package` would silently not be rewritten.
        HookScriptTemplates.ClaudeHook.Should().Contain("""s.replace(" ", r"\s+")""");
    }

    [Fact]
    public void SharedCore_OrdersTheAlternationLongestFirst()
    {
        // Python's alternation is first-match-wins, so a subcommand that prefixes a longer one
        // would shadow it.
        HookScriptTemplates.ClaudeHook.Should().Contain("key=len, reverse=True");
    }

    [Fact]
    public void AllThreeHooks_ShareTheIdenticalPatternConstruction()
    {
        foreach (var hook in new[]
                 {
                     HookScriptTemplates.ClaudeHook,
                     HookScriptTemplates.GeminiHook,
                     HookScriptTemplates.CopilotCliHook
                 })
        {
            hook.Should().Contain("""s.replace(" ", r"\s+")""");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookScriptTemplatesTests"
```

Expected: FAIL — the current line is a plain `"|".join(_DTK_SUBCOMMANDS)`.

- [ ] **Step 3: Write minimal implementation**

In `HookScriptTemplates.cs`, replace the `_PATTERN` line (line 68) inside `SharedCore`:

```python
        _PATTERN = re.compile(r"\bdotnet\s+(" + "|".join(_DTK_SUBCOMMANDS) + r")\b")
```

with:

```python
        # Multi-token subcommands are declared with spaces ("list package") but must match any run
        # of whitespace between their tokens. Longest-first ordering matters because Python's
        # alternation is first-match-wins: a subcommand that prefixes a longer one would shadow it.
        _PATTERN = re.compile(
            r"\bdotnet\s+("
            + "|".join(
                s.replace(" ", r"\s+")
                for s in sorted(_DTK_SUBCOMMANDS, key=len, reverse=True)
            )
            + r")\b"
        )
```

- [ ] **Step 4: Regenerate the committed hook**

`RepoClaudeHook_MatchesTheGeneratedHook` pins `.claude/hooks/dotnet-to-dtk.py` byte-for-byte to the
template, so it must be rewritten from the new template:

```bash
dtk dotnet build DotnetTokenKiller.slnx
scratch=$(mktemp -d)
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir "$scratch"
cp "$scratch/.claude/hooks/dotnet-to-dtk.py" .claude/hooks/dotnet-to-dtk.py
rm -rf "$scratch"
git diff --stat .claude/hooks/dotnet-to-dtk.py
```

Expected: only the `_PATTERN` block differs.

- [ ] **Step 5: Verify the hook still rewrites correctly**

The pattern is the most fragile artifact here, so assert behaviour rather than only bytes:

```bash
echo '{"tool_input":{"command":"dotnet build MyApp.slnx"}}' | python3 .claude/hooks/dotnet-to-dtk.py
echo '{"tool_input":{"command":"dotnet list reference"}}' | python3 .claude/hooks/dotnet-to-dtk.py
```

Expected: the first prints JSON containing `dtk dotnet build`; the second prints **nothing**
(`list package` is not canonical yet, and `list reference` must never be rewritten).

- [ ] **Step 6: Run the suite**

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: PASS. The pinned `_DTK_SUBCOMMANDS` tuple literal is untouched, so
`GeneratedHooks_DeclareExactlyTheCanonicalSubcommands` still holds.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs \
        .claude/hooks/dotnet-to-dtk.py tests/DotnetTokenKiller.Application.Tests
git commit -m "fix: match whitespace between multi-token subcommands in generated hooks"
```

---

## Task 8: Shell completion for multi-token subcommands

Fixes deviation 2. Without this, activation emits a malformed fish completion script.

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs:203-220`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`

**Interfaces:**
- Produces: `internal static IReadOnlyList<string> CompletionCommand.CompletionCandidates(IReadOnlyList<string> names)`
  — first tokens, de-duplicated, order preserved.

- [ ] **Step 1: Write the failing test**

Read the existing completion test file first: it already has a helper that renders a shell script, and
this test must reuse it rather than inventing a second one. Substitute its real name for
`RenderCompletionAsync` below.

```csharp
    [Fact]
    public void CompletionCandidates_MultiTokenSubcommand_YieldsItsFirstTokenOnly()
    {
        CompletionCommand.CompletionCandidates(["build", "list package"])
            .Should().Equal("build", "list");
    }

    [Fact]
    public void CompletionCandidates_TwoSubcommandsSharingAFirstToken_AreDeduplicated()
    {
        CompletionCommand.CompletionCandidates(["list package", "list reference"])
            .Should().Equal("list");
    }

    [Fact]
    public async Task RunAsync_FishScript_EmitsNoCandidateContainingASpace()
    {
        var output = await RenderCompletionAsync("fish");

        // `complete ... -a list package -d '...'` is malformed: fish reads `package` as another
        // argument to `complete`, so the whole completion silently stops working.
        foreach (var line in output.Split('\n').Where(l => l.Contains(" -a ", StringComparison.Ordinal)))
        {
            var candidate = line.Split(" -a ")[1].Split(" -d ")[0];
            candidate.Should().NotContain(" ", "candidate '{0}' would break the generated script", candidate);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests"
```

Expected: FAIL to compile — `CompletionCandidates` does not exist.

- [ ] **Step 3: Write minimal implementation**

Replace lines 203-220 of `CompletionCommand.cs`:

```csharp
    /// <summary>
    /// Completion candidates at the <c>dtk dotnet &lt;TAB&gt;</c> position: the first token of every
    /// canonical subcommand, de-duplicated.
    /// </summary>
    /// <param name="names">Canonical subcommand names, space-separated for multi-token ones.</param>
    /// <remarks>
    /// Multi-token subcommands contribute only their first token. Emitting the full
    /// <c>"list package"</c> would break the generated scripts — fish reads the second token as
    /// another argument to <c>complete</c>, and bash's <c>-W</c> word list would offer
    /// <c>package</c> as a standalone candidate. Completing the second token is not implemented;
    /// the rewrite hook, not completion, is the primary path for these.
    /// </remarks>
    internal static IReadOnlyList<string> CompletionCandidates(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return [.. names.Select(name => name.Split(' ')[0]).Distinct(StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> DotnetCandidates { get; } =
        CompletionCandidates(DotnetSubcommands.Ordered);

    /// <summary>Space-separated dotnet subcommand list for the bash script.</summary>
    private static readonly string BashDotnetCommands = string.Join(' ', DotnetCandidates);

    private static readonly string PowerShellDotnetCommands =
        string.Join(", ", DotnetCandidates.Select(sub => $"'{sub}'"));

    private static readonly string ZshDotnetCommands =
        string.Join(
            "\n        ",
            DotnetCandidates.Select(sub => $"'{sub}:Run dotnet {sub} with filtered output'"));

    private static readonly string FishDotnetCommands =
        string.Join(
            '\n',
            DotnetCandidates.Select(
                sub => $"complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a {sub} -d 'Run dotnet {sub} with filtered output'"));
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CompletionCommandTests"
```

Expected: PASS. Existing completion snapshots must be **unchanged** — with only single-token
subcommands canonical, `CompletionCandidates` is the identity function. A changed snapshot here means
the refactor altered output it should not have.

- [ ] **Step 5: Verify nothing else broke**

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: PASS, with no completion snapshot updates.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs tests/DotnetTokenKiller.Cli.IntegrationTests
git commit -m "fix: complete on first tokens so multi-token subcommands cannot break shell scripts"
```

---

## Task 9: Derive integration prose from the canonical list

Closes the unguarded gap that step 6 of `DotnetSubcommands`' remarks warns about. Follows the existing
tripwire pattern: generate the prose, then **pin the expected literal** in a test, so the test cannot
drift in lockstep with the source.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs:34`
- Test: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`

**Interfaces:**
- Produces: `IntegrationInstructions.SubcommandProse` and
  `IntegrationInstructions.SubcommandAlternation`, both `internal static readonly string`.

- [ ] **Step 1: Write the failing test**

Append to `SubcommandBindingTests`:

```csharp
    [Fact]
    public void IntegrationProse_ListsEveryCanonicalSubcommand()
    {
        // Pinned literals, for the same reason as the hook assertions above: deriving these from
        // DotnetSubcommands.Ordered would make the expectation move in lockstep with the source, and
        // the test could never fail. Adding a subcommand means editing these by hand.
        const string expectedProse = "build, test, restore, clean, and format";
        const string expectedAlternation = "build|test|restore|clean|format";

        IntegrationInstructions.SubcommandProse.Should().Be(
            expectedProse,
            "the shared instructions must name every dtk-handled subcommand, or users are told to "
            + "keep using raw dotnet for the newest one");
        IntegrationInstructions.SubcommandAlternation.Should().Be(expectedAlternation);
    }

    [Fact]
    public void SharedInstructions_MentionEverySubcommandInTheIntro()
    {
        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            IntegrationInstructions.Intro.Should().Contain(
                subcommand,
                "'{0}' is canonical, so the instructions embedded by every provider must mention it",
                subcommand);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~SubcommandBindingTests"
```

Expected: FAIL to compile — `SubcommandProse` and `SubcommandAlternation` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `IntegrationInstructions.cs`, add the `using` and the two derived members, and make `Intro` consume
the prose one. `Intro` must change from `const` to `static readonly` because it now interpolates a
computed value; `UsageBody` and `Markdown` must follow for the same reason.

```csharp
using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Application.Integration;
```

```csharp
    /// <summary>
    /// The canonical subcommands as an Oxford-comma prose list, e.g.
    /// <c>build, test, restore, clean, and format</c>.
    /// </summary>
    internal static readonly string SubcommandProse = BuildProse(DotnetSubcommands.Ordered);

    /// <summary>The canonical subcommands as a <c>build|test|…</c> alternation, in display order.</summary>
    internal static readonly string SubcommandAlternation = string.Join("|", DotnetSubcommands.Ordered);

    private static string BuildProse(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
        };

    internal static readonly string Intro =
        $"""
        Use `dtk` instead of raw `dotnet` for {SubcommandProse} commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.
        """;
```

Change `UsageBody` and `Markdown` from `const` to `static readonly string`, leaving their text as-is
for now — Task 10 adds the `list package` example.

In `CopilotCliIntegrator.cs`, change `CopilotSection` from `const` to `static readonly string` and
replace the hardcoded alternation on line 34:

```csharp
    private static readonly string CopilotSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}

        A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` rewrites `dotnet {IntegrationInstructions.SubcommandAlternation}`
        to `dtk dotnet ...` automatically. The hook shells out to `python3`; on Windows (where the launcher is
        usually `python`, not `python3`), edit the `bash` command in that file if it doesn't fire.
        {SectionEndMarker}
        """;
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~SubcommandBindingTests"
```

Expected: PASS.

- [ ] **Step 5: Verify nothing else broke**

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: PASS with **no** snapshot changes — the generated prose is byte-identical to what was
previously hardcoded. If an integration snapshot changes, the prose generation is wrong; fix the
generator rather than accepting the snapshot.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration tests/DotnetTokenKiller.Application.Tests
git commit -m "refactor: derive integration prose from the canonical subcommand list"
```

---

## Task 10: Activation

The atomic flip. `SubcommandBindingTests` is designed so partial activation cannot pass: registering a
filter under a non-canonical key fails one test, adding a canonical subcommand without a filter fails
another, and the pinned hook literals fail a third. Everything here lands in one commit.

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`
- Modify: `src/DotnetTokenKiller.Domain/Filters/FilterKeys.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs:64`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs`
- Create: `src/DotnetTokenKiller.Cli/Commands/DotnetListPackageCommand.cs`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs`
- Modify: `.claude/hooks/dotnet-to-dtk.py`
- Test: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/ArgumentPreprocessorTests.cs`,
  `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`,
  `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`

**Interfaces:**
- Consumes: `DotnetListPackageFilter` (Tasks 3–6), `DotnetSubcommands.TryMatch` (Task 1),
  `IntegrationInstructions.SubcommandProse`/`.SubcommandAlternation` (Task 9),
  `CompletionCommand.CompletionCandidates` (Task 8).

- [ ] **Step 1: Write the failing test**

There are **five** pinned literals to update, not three. Two live outside `SubcommandBindingTests`
and were found during Task 1's recon:

In `tests/DotnetTokenKiller.Domain.Tests/DotnetSubcommandsTests.cs`:

```csharp
        DotnetSubcommands.Ordered.Should().Equal("build", "test", "restore", "clean", "format", "list package");
```

In `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/CompletionCommandTests.cs`,
`ExecuteAsync_Script_OffersEveryDotnetSubcommand` iterates `DotnetSubcommands.All` and asserts each
name appears verbatim in the script. That is no longer the contract: Task 8 deliberately emits only
first tokens, so `"list package"` will never appear. Preserve the test's anti-drift *intent* by
iterating the candidates the scripts are actually generated from:

```csharp
        foreach (var subcommand in CompletionCommand.CompletionCandidates(DotnetSubcommands.Ordered))
```

Leave its comment accurate by extending it: the completion lists are generated from
`CompletionCandidates(DotnetSubcommands.Ordered)`, so every candidate must appear in every shell
script, and a multi-token subcommand contributes only its first token.

Then, in `SubcommandBindingTests`, change the three constants:

```csharp
        const string expectedTuple =
            "_DTK_SUBCOMMANDS = (\"build\", \"clean\", \"format\", \"list package\", \"restore\", \"test\")";
```

```csharp
        const string expected = "rewrites `dotnet build|test|restore|clean|format|list package`";
```

```csharp
        const string expectedProse = "build, test, restore, clean, format, and list package";
        const string expectedAlternation = "build|test|restore|clean|format|list package";
```

In `ArgumentPreprocessorTests`, add:

```csharp
    [Fact]
    public void IsPassthrough_ListPackage_IsNotPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list", "package"]).Should().BeFalse();
    }

    [Fact]
    public void InsertSeparator_ListPackage_SeparatesAfterBothTokens()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "list", "package", "--outdated"])
            .Should().Equal("dotnet", "list", "package", "--", "--outdated");
    }

    [Fact]
    public void Normalize_UppercaseListPackage_CanonicalizesBothTokens()
    {
        ArgumentPreprocessor.Normalize(["DOTNET", "LIST", "PACKAGE"])
            .Should().Equal("dotnet", "list", "package");
    }
```

In `FilteredRunUseCaseTests`, add the slug regression test (deviation 1). Read the file first and reuse
its existing arrangement helper for building the use case with substituted
`ICommandRunner`/`ITracker`; substitute its real name for `CreateSut` below.

```csharp
    [Fact]
    public async Task RunAsync_MultiTokenSubcommand_TracksTheFullCanonicalName()
    {
        // args[0] alone would record "list", which would never line up with the "list package" rows
        // the coverage report already holds from the passthrough path — making the before/after
        // savings comparison compare two different keys.
        var tracker = Substitute.For<ITracker>();
        var sut = CreateSut(tracker: tracker, stdOut: "Project 'A' has the following package references");

        await sut.RunAsync(new DotnetListPackageFilter(), "dotnet", ["list", "package"], verbosityLevel: 0);

        await tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "list package"),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: multiple FAILURES — the pinned literals no longer match the generated artifacts, the new
routing tests fail, and the slug test records `"list"`.

- [ ] **Step 3: Write minimal implementation**

`DotnetSubcommands.cs` — add the const and extend `Ordered`:

```csharp
    /// <summary>The <c>dotnet list package</c> subcommand.</summary>
    /// <remarks>
    /// Two tokens, unlike every other entry. <see cref="TryMatch"/> exists for this: matching only
    /// <c>args[1]</c> would capture <c>dotnet list reference</c>, which dtk must forward untouched.
    /// </remarks>
    public const string ListPackage = "list package";
```

```csharp
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean, Format, ListPackage];
```

Update the `<remarks>` checklist on that class, which is now out of date in two ways. Add a step after
the current step 3:

```
///   <item><description>
///   Check <c>CompletionCommand.CompletionCandidates</c> — a multi-token subcommand contributes only
///   its first token, and completing the remaining tokens is not implemented.
///   </description></item>
```

and amend the final step so it no longer claims the integration prose is unguarded: it is now covered
by `SubcommandBindingTests.IntegrationProse_ListsEveryCanonicalSubcommand`.

`FilterKeys.cs`:

```csharp
    /// <summary>Key for the <c>dotnet list package</c> output filter.</summary>
    public const string ListPackage = DotnetSubcommands.ListPackage;
```

`DependencyInjection.cs`, after the Format registration:

```csharp
        services.AddKeyedTransient<IOutputFilter, DotnetListPackageFilter>(FilterKeys.ListPackage);
```

`FilteredRunUseCase.cs` — replace line 64 and add `using DotnetTokenKiller.Domain;`:

```csharp
        // The canonical name, not args[0]: a multi-token subcommand would otherwise be recorded
        // under its first token ("list"), which no report or coverage row would ever match.
        var commandSlug = DotnetSubcommands.TryMatch(args, out var subcommand)
            ? subcommand.Name
            : args.Count > 0 ? args[0] : command;
```

Create `src/DotnetTokenKiller.Cli/Commands/DotnetListPackageCommand.cs`:

```csharp
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet list package with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The list package output filter.</param>
internal sealed class DotnetListPackageCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.ListPackage)]
    IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        // Both tokens are prepended: the child process must receive `dotnet list package ...`, and
        // FilteredRunUseCase derives the tracking slug by matching this same argument list.
        var args = settings.PositionalArgs
            .Prepend("package")
            .Prepend("list")
            .Concat(context.Remaining.Raw)
            .ToArray();

        return await filteredRun.RunAsync(filter, "dotnet", args, settings.VerbosityLevel, settings.ShowLog,
            settings.Quiet, cancellationToken).ConfigureAwait(false);
    }
}
```

`CliConfigurator.cs` — add the nested branch inside the existing `dotnet` branch, after the Format
registration:

```csharp
            dotnet.AddBranch("list", list =>
            {
                list.SetDescription("Run dotnet list commands with filtered output");
                list.AddCommand<DotnetListPackageCommand>("package")
                    .WithDescription("Run dotnet list package with filtered output")
                    .WithExample(DotnetCommand, "list", "package")
                    .WithExample(DotnetCommand, "list", "package", "--outdated");
            });
```

`IntegrationInstructions.cs` — add an example to `UsageBody`, after the format lines:

```
        dtk dotnet list package --outdated
```

- [ ] **Step 4: Regenerate the committed hook**

```bash
dtk dotnet build DotnetTokenKiller.slnx
scratch=$(mktemp -d)
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir "$scratch"
cp "$scratch/.claude/hooks/dotnet-to-dtk.py" .claude/hooks/dotnet-to-dtk.py
rm -rf "$scratch"
git diff .claude/hooks/dotnet-to-dtk.py
```

Expected: the tuple gains `"list package"` and the docstring gains `|list package`.

- [ ] **Step 5: Verify the hook rewrites the new subcommand and still ignores `list reference`**

```bash
echo '{"tool_input":{"command":"dotnet list package --outdated"}}' | python3 .claude/hooks/dotnet-to-dtk.py
echo '{"tool_input":{"command":"dotnet list reference"}}' | python3 .claude/hooks/dotnet-to-dtk.py
echo '{"tool_input":{"command":"dotnet list   package"}}' | python3 .claude/hooks/dotnet-to-dtk.py
```

Expected: the first prints JSON containing `dtk dotnet list package`; the second prints nothing; the
third prints JSON, proving the `\s+` join from Task 7 does its job.

- [ ] **Step 6: Run the full suite**

```bash
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: PASS. The CLI help snapshots will change because the `dotnet` branch gained a subcommand —
read each diff and confirm it shows only the new `list` entry before accepting.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: filter dotnet list package"
```

---

## Task 11: End-to-end verification and the measured result

Proves the wiring against a real process and produces the spec's success-criterion numbers.

**Files:**
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/ListPackageIntegrationTests.cs`

- [ ] **Step 1: Write the failing test**

Read `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs` first for the
DB-isolating `RunDtkWithDbAsync` overload added by the coverage-tracking work, and
`PassthroughIntegrationTests.cs` for the invocation style. Match their real signatures.

```csharp
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public class ListPackageIntegrationTests
{
    [Fact]
    public async Task ListPackage_IsFiltered_AndTrackedUnderItsFullName()
    {
        var (output, exitCode, dbPath) = await IntegrationTestHelper.RunDtkWithDbAsync(
            "dotnet", "list", "package");

        exitCode.Should().Be(0);
        output.Should().NotContain("Top-level Package", "the raw table must not survive filtering");
        output.Should().Contain("dotnet list package");

        var commands = await IntegrationTestHelper.ReadTrackedCommandsAsync(dbPath);
        commands.Should().Contain("list package");
    }

    [Fact]
    public async Task ListReference_StillPassesThroughUnfiltered()
    {
        var (output, _, _) = await IntegrationTestHelper.RunDtkWithDbAsync("dotnet", "list", "reference");

        // Passthrough forwards dotnet's own output verbatim, whatever it is — it must never be
        // rendered by the list package filter.
        output.Should().NotContain("✓ dotnet list package");
    }
}
```

If `ReadTrackedCommandsAsync` does not exist on the helper, add it: a small read of
`SELECT command FROM commands` against the isolated database, returning the values.

- [ ] **Step 2: Run test to verify it passes**

```bash
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~ListPackageIntegrationTests"
```

Expected: PASS if Task 10 is correct. These are wiring guards rather than new behaviour — if
`ListPackage_IsFiltered_AndTrackedUnderItsFullName` fails on the tracked name, deviation 1's fix did
not take.

- [ ] **Step 3: Measure the actual saving**

This is the spec's success criterion. Re-run the same bootstrap the design used:

```bash
DTK=src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll
export DTK_DB_PATH=/tmp/dtk-list-package-after.db
rm -f "$DTK_DB_PATH"
for v in "" "--outdated" "--vulnerable" "--deprecated"; do
  /usr/share/dotnet/dotnet "$DTK" dotnet list package $v > /dev/null 2>&1
done
/usr/share/dotnet/dotnet "$DTK" gain --coverage
/usr/share/dotnet/dotnet "$DTK" gain
unset DTK_DB_PATH
```

Expected: `list package` now appears with outcome `Filtered` rather than `PassthroughMeasured`, and
`dtk gain` reports its savings. Record the percentage — the baseline to beat is plain 7892 B,
`--outdated` 2231 B, `--deprecated` 1785 B, `--vulnerable` 1000 B.

- [ ] **Step 4: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests
git commit -m "test: end-to-end coverage for the dotnet list package filter"
```

---

## Task 12: Documentation

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-07-27-list-package-filter-design.md`
- Modify: `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`

- [ ] **Step 1: Update the README**

Read `README.md` and add `list package` wherever the five subcommands are enumerated — the feature
list, any command table, and the savings table, using the percentage measured in Task 11 rather than
an estimate.

- [ ] **Step 2: Update CLAUDE.md**

Add to the Commands section:

```bash
# Inspect package references with filtered output
dtk dotnet list package --outdated
```

- [ ] **Step 3: Mark the design implemented**

```markdown
**Status:** Implemented — see [the plan](../plans/2026-07-27-list-package-filter.md)
```

- [ ] **Step 4: Close the loop on the gap analysis**

In `2026-07-27-rtk-gap-analysis.md`:

- Update "Suggested sequencing" item 3 to strike the first-new-filter clause, noting `list package` is
  done and that §2 pipe mode and §5 `dtk log` are next.
- Add a resolution note under §1's table stating `list package` is now filtered, and that the
  remaining candidates in that table are measured rather than guessed.
- Correct §9's density-knob bullet: `DotnetListPackageFilter` caps groups at 30 with an explicit
  truncation line, so that complaint now applies only to the build filter.

- [ ] **Step 5: Verify and commit**

```bash
dtk dotnet test DotnetTokenKiller.slnx
git add -A
git commit -m "docs: record the list package filter and its measured savings"
```

---

## Self-Review

**Spec coverage.** Every section of the design maps to a task. Multi-token routing → Tasks 1, 2, 10.
Marker-phrase variant detection and header-driven column parsing → Task 3. Plain shared-set/delta →
Task 3. `--outdated` → Task 4. `--deprecated`/`--vulnerable`, including the hand-written fixture and
its recorded risk → Task 5. Restore-preamble stripping, `Requested ≠ Resolved`, the 30-group cap, the
`✓` rule, transitive tables, and empty-on-unparsed-failure → Tasks 3 and 6. The console-parsing
decision is honoured throughout: no task adds `--format json`. Components table → Tasks 7–10. Data
flow → Task 10's command plus Task 11's end-to-end test. Testing section → Tasks 3–6 and 8–11. Prose
binding test → Task 9. Success criterion → Task 11 Step 3. Out-of-scope items are respected: no other
filter is added, no project-name shortening, and the `PassthroughSubcommands` `sln` bug is untouched.

**Three additions beyond the spec**, all documented under "Deviations": the `FilteredRunUseCase` slug
fix (without which the success criterion cannot be evaluated at all), the shell-completion fix
(without which activation ships a malformed fish script), and the TFM-deduplication simplification.

**Type consistency.** `SubcommandMatch(Name, TokenCount)` is defined in Task 1 and consumed under
those exact property names in Tasks 2 and 10. `TryMatch`'s two overloads keep the `(args, out match)`
and `(args, candidateNames, out match)` shapes across Tasks 1, 2, and 10. `FilterKeys.ListPackage`
aliases `DotnetSubcommands.ListPackage` and is used by `DotnetListPackageCommand`'s
`[FromKeyedServices]` and the DI registration, all in Task 10. `DotnetListPackageFilter`'s private
members — `Entry` with its nine positional properties, `ParseState`, `Variant`, `Cell`, `SplitCells`,
`Version`, `Plural`, `AppendTruncation`, `MaxGroups` — are introduced in Task 3 and reused under those
names in Tasks 4, 5, and 6. `FormatAudit(state, exitCode, flag, noun, detail)` is introduced in Task 4
with `noun` as a `(string Singular, string Plural)` tuple and called with that arity and shape in
Task 5. `Arrow` is introduced and used in Task 5. `CompletionCommand.CompletionCandidates` (Task 8) is
referenced by `DotnetSubcommands`' updated remarks in Task 10.
`IntegrationInstructions.SubcommandProse`/`.SubcommandAlternation` (Task 9) are consumed by
`CopilotCliIntegrator` in Task 9 and re-pinned in Task 10.

**Known soft spots.**

1. Task 6 expects several tests to pass immediately against Task 3's parser. That is intentional —
   they are regression guards for spec requirements — but an implementer following TDD strictly may
   find it unsatisfying. The cap test is the one genuinely expected to fail.
2. Tasks 8 and 11 reference helpers (`RenderCompletionAsync`, `RunDtkWithDbAsync`,
   `ReadTrackedCommandsAsync`) by descriptive names because the existing test files' arrangements were
   not read while planning. Each of those steps says to read the file first and substitute the real
   name; do that rather than adding a duplicate helper.
3. Task 10 is large because the repo's tripwire tests make partial activation impossible by design.
   It cannot be split without leaving the suite red between commits.

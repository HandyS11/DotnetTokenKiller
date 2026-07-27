# Coverage Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make dtk able to measure the dotnet commands it does *not* filter, and the filtered runs where its filter degraded, so the next filter is chosen from data rather than guessed.

**Architecture:** A single `RunOutcome` dimension is added to the tracking record. Three of its five values mean "a filter ran" and keep feeding today's `dtk gain` numbers unchanged; two mean "no filter ran" and feed a new `dtk gain --coverage` report. Allowlisted passthrough subcommands are run through a new streaming runner that echoes output live while accumulating it for token counting; everything else keeps today's inherited-stdio behaviour.

**Tech Stack:** net10.0 · Spectre.Console.Cli · Microsoft.Data.Sqlite · Microsoft.ML.Tokenizers · xunit + FluentAssertions + NSubstitute

**Spec:** [2026-07-27-coverage-tracking-design.md](../specs/2026-07-27-coverage-tracking-design.md)

## Global Constraints

- `TreatWarningsAsErrors` is enabled. Roslynator, SonarAnalyzer, and Microsoft.CodeAnalysis.NetAnalyzers all run at build time. A build with any analyzer warning fails.
- Every public type and member needs an XML doc comment, including every record positional parameter (`/// <param name="X">`). Missing docs are build errors.
- File-scoped namespaces. `var` throughout. Private fields `_camelCase`. Async methods end in `Async`. Interfaces `IPascalCase`.
- LF line endings, no trailing whitespace, no BOM, 4-space indent for `.cs`.
- Package versions live only in `Directory.Packages.props`; `.csproj` files never carry a version.
- `await` on a non-test path needs `.ConfigureAwait(false)` (CA2007). For `await using` this analyzer cannot be satisfied — follow the existing `#pragma warning disable CA2007` pattern already used throughout `SqliteTracker`.
- All string comparisons need an explicit `StringComparison` (CA1307/CA1310).
- Culture-sensitive formatting needs an explicit `CultureInfo.InvariantCulture` (CA1305).

**Build and test commands:**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test DotnetTokenKiller.slnx
```

Run a single test against its **project**, not the solution — `--filter` against the `.slnx` can report "0 tests found" when other assemblies in the solution contain no match:

```bash
dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~TestName"
```

Before every commit:

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/DotnetTokenKiller.Domain/Tracking/RunOutcome.cs` | The five-value enum plus the set defining which values count toward savings |
| `src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs` | Allowlists: which subcommands are safe to measure, and how an argv is reduced to a safe command name |
| `src/DotnetTokenKiller.Domain/Tracking/CoverageSummary.cs` | `CoverageSummary` + `CoverageDetail` result shapes |
| `src/DotnetTokenKiller.Infrastructure/Tracking/TrackerFactory.cs` | Single place that turns a `DtkConfig` into a `SqliteTracker` |
| `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs` | Decides measured vs unmeasured, runs, records |
| `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs` | Minimal non-DI wiring for the passthrough branch |
| `tests/DotnetTokenKiller.Domain.Tests/RunOutcomeTests.cs` | |
| `tests/DotnetTokenKiller.Domain.Tests/PassthroughSubcommandsTests.cs` | |
| `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs` | |

**Modified:**

| File | Change |
|---|---|
| `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs` | `Outcome` property |
| `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` | `GetCoverageAsync` |
| `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs` | `RunStreamedAsync` |
| `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs` | `outcome` column, migration helper, savings exclusion, coverage query |
| `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs` | `RunStreamedAsync` |
| `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs` | Delegate tracker construction to `TrackerFactory` |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Determine and record `Outcome` |
| `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs` | `GetCoverageAsync` passthrough |
| `src/DotnetTokenKiller.Cli/Program.cs` | Passthrough branch calls `PassthroughEntryPoint` |
| `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs` | `--coverage` |
| `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs` | Coverage branch, `outcome` CSV column |
| `src/DotnetTokenKiller.Cli/Formatting/GainDashboardRenderer.cs` | `RenderCoverage` |
| `src/DotnetTokenKiller.Cli/Serialization/GainSummaryJsonContext.cs` | Coverage types, string enums |

---

## Deviation from the spec (read before Task 2)

The spec's leak guarantee covers the **second** argv token: `dotnet list ./src/App.csproj` must record as `list`, never as the path. Writing Task 2 surfaced a case the spec's rule does not cover — **the first token can also be a path**. `dotnet ./bin/App.dll` is a valid invocation (run an assembly), and `IsPassthrough` routes it here because `./bin/App.dll` is not a canonical subcommand.

Task 2 therefore extends the same guarantee to token 1: the first token is used only if it appears in a hardcoded allowlist of real dotnet verbs or options, and anything else records as the literal `(other)`. This is strictly stronger than the spec and changes no other decision in it.

---

## Task 1: `RunOutcome` and `CommandRecord.Outcome`

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Tracking/RunOutcome.cs`
- Modify: `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs`
- Test: `tests/DotnetTokenKiller.Domain.Tests/RunOutcomeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum RunOutcome { Filtered, RawTailFallback, FilterFaulted, PassthroughMeasured, PassthroughUnmeasured }`; `static class RunOutcomes` with `IReadOnlySet<RunOutcome> CountedInSavings` and `bool IsPassthrough(RunOutcome)`; `CommandRecord.Outcome` (`RunOutcome`, defaults to `RunOutcome.Filtered`) and a new final optional constructor parameter `RunOutcome outcome = RunOutcome.Filtered`.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Domain.Tests/RunOutcomeTests.cs`:

```csharp
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class RunOutcomeTests
{
    [Theory]
    [InlineData(RunOutcome.Filtered)]
    [InlineData(RunOutcome.RawTailFallback)]
    [InlineData(RunOutcome.FilterFaulted)]
    public void CountedInSavings_ContainsEveryOutcomeWhereAFilterRan(RunOutcome outcome)
    {
        RunOutcomes.CountedInSavings.Should().Contain(outcome);
        RunOutcomes.IsPassthrough(outcome).Should().BeFalse();
    }

    [Theory]
    [InlineData(RunOutcome.PassthroughMeasured)]
    [InlineData(RunOutcome.PassthroughUnmeasured)]
    public void CountedInSavings_ExcludesEveryOutcomeWhereNoFilterRan(RunOutcome outcome)
    {
        RunOutcomes.CountedInSavings.Should().NotContain(outcome);
        RunOutcomes.IsPassthrough(outcome).Should().BeTrue();
    }

    [Fact]
    public void CountedInSavings_PartitionsEveryDeclaredOutcome()
    {
        // A new outcome added to the enum without a deliberate decision about whether it counts
        // toward savings would silently land on the passthrough side. Force the choice.
        var all = Enum.GetValues<RunOutcome>();

        all.Should().HaveCount(5);
        all.Count(RunOutcomes.IsPassthrough).Should().Be(2);
    }

    [Fact]
    public void CommandRecord_DefaultsToFiltered_WhenOutcomeNotSupplied()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow,
            "build",
            "/proj",
            new TokenStatistics(100, 10, 90, 90.0),
            TimeSpan.FromMilliseconds(5));

        record.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public void CommandRecord_RoundTripsSuppliedOutcome()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow,
            "publish",
            "/proj",
            new TokenStatistics(100, 100, 0, 0.0),
            TimeSpan.FromMilliseconds(5),
            success: true,
            outcome: RunOutcome.PassthroughMeasured);

        record.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~RunOutcomeTests"`
Expected: FAIL — build error, `RunOutcome` and `RunOutcomes` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Domain/Tracking/RunOutcome.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>How a tracked run produced the output the user saw.</summary>
public enum RunOutcome
{
    /// <summary>A filter ran and produced the output.</summary>
    Filtered = 0,

    /// <summary>The command failed and its filter produced nothing, so the raw tail was emitted.</summary>
    RawTailFallback = 1,

    /// <summary>The filter threw, so the raw output was emitted unchanged.</summary>
    FilterFaulted = 2,

    /// <summary>No filter exists; output was streamed through and its tokens counted.</summary>
    PassthroughMeasured = 3,

    /// <summary>No filter exists and the output was not captured, so its size is unknown.</summary>
    PassthroughUnmeasured = 4
}

/// <summary>Classification helpers for <see cref="RunOutcome"/>.</summary>
public static class RunOutcomes
{
    /// <summary>
    /// The outcomes where a filter actually ran. Only these contribute to <c>dtk gain</c>'s
    /// savings totals — passthrough runs saved nothing and would drag the average toward zero.
    /// </summary>
    public static readonly IReadOnlySet<RunOutcome> CountedInSavings =
        new HashSet<RunOutcome>
        {
            RunOutcome.Filtered,
            RunOutcome.RawTailFallback,
            RunOutcome.FilterFaulted
        };

    /// <summary>Returns <see langword="true"/> when no filter ran for this outcome.</summary>
    /// <param name="outcome">The outcome to classify.</param>
    public static bool IsPassthrough(RunOutcome outcome) => !CountedInSavings.Contains(outcome);
}
```

Modify `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs` — add the parameter to the constructor after `success`, assign it, and add the property. The constructor becomes:

```csharp
    /// <param name="success">Whether the command exited with code 0.</param>
    /// <param name="outcome">How the run produced its output.</param>
    public CommandRecord(
        DateTimeOffset timestamp,
        string command,
        string projectPath,
        TokenStatistics tokens,
        TimeSpan executionTime,
        bool success = true,
        RunOutcome outcome = RunOutcome.Filtered)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(tokens);

        Timestamp = timestamp;
        Command = command;
        ProjectPath = projectPath;
        InputTokens = tokens.Input;
        OutputTokens = tokens.Output;
        SavedTokens = tokens.Saved;
        SavingsPercentage = tokens.SavingsPercentage;
        ExecutionTime = executionTime;
        Success = success;
        Outcome = outcome;
    }
```

And add the property alongside `Success`:

```csharp
    /// <summary>How the run produced its output.</summary>
    public RunOutcome Outcome { get; init; } = RunOutcome.Filtered;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~RunOutcomeTests"`
Expected: PASS — 9 tests.

- [ ] **Step 5: Verify nothing else broke**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. The new constructor parameter is optional and last, so every existing call site still compiles.

- [ ] **Step 6: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Domain/Tracking/RunOutcome.cs \
        src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs \
        tests/DotnetTokenKiller.Domain.Tests/RunOutcomeTests.cs
git commit -m "feat(domain): add RunOutcome dimension to CommandRecord"
```

---

## Task 2: `PassthroughSubcommands` — the leak guarantee

**Files:**
- Create: `src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs`
- Test: `tests/DotnetTokenKiller.Domain.Tests/PassthroughSubcommandsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `PassthroughSubcommands.CommandName(IReadOnlyList<string> dotnetArgs) -> string` and `PassthroughSubcommands.IsMeasurable(IReadOnlyList<string> dotnetArgs) -> bool`. `dotnetArgs` is the argument list passed to `dotnet`, starting at the subcommand — i.e. `Program.cs`'s `args[1..]`. Also `PassthroughSubcommands.Unknown` (the literal `"(other)"`).

Read the "Deviation from the spec" section above before starting this task.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Domain.Tests/PassthroughSubcommandsTests.cs`:

```csharp
using DotnetTokenKiller.Domain;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class PassthroughSubcommandsTests
{
    [Theory]
    [InlineData(new[] { "list", "package" }, "list package")]
    [InlineData(new[] { "list", "package", "--outdated" }, "list package")]
    [InlineData(new[] { "list", "reference" }, "list reference")]
    [InlineData(new[] { "ef", "migrations", "add", "Init" }, "ef migrations")]
    [InlineData(new[] { "ef", "database", "update" }, "ef database")]
    [InlineData(new[] { "tool", "restore" }, "tool restore")]
    [InlineData(new[] { "sln", "list" }, "sln list")]
    [InlineData(new[] { "workload", "list" }, "workload list")]
    [InlineData(new[] { "nuget", "locals", "all", "--clear" }, "nuget locals")]
    public void CommandName_QualifiesWithTheSecondVerb_WhenItIsRecognised(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "publish" }, "publish")]
    [InlineData(new[] { "publish", "-c", "Release" }, "publish")]
    [InlineData(new[] { "pack" }, "pack")]
    [InlineData(new[] { "msbuild", "/t:Rebuild" }, "msbuild")]
    public void CommandName_UsesTheSubcommandAlone_WhenItTakesNoQualifyingVerb(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "list", "./src/App.csproj" })]
    [InlineData(new[] { "list", "/home/someone/secret/Thing.csproj" })]
    [InlineData(new[] { "ef", "--connection", "Server=db;Password=hunter2" })]
    [InlineData(new[] { "tool", "MyCompany.Internal.Tool" })]
    public void CommandName_DropsAnUnrecognisedSecondToken(string[] args)
    {
        // The leak guarantee: an argv fragment must never become a database value.
        var name = PassthroughSubcommands.CommandName(args);

        name.Should().Be(args[0]);
        name.Should().NotContain(args[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "./bin/Release/App.dll" })]
    [InlineData(new[] { "/opt/secret/Payload.dll" })]
    [InlineData(new[] { "totally-made-up-verb" })]
    public void CommandName_ReturnsUnknown_WhenTheFirstTokenIsNotARecognisedVerb(string[] args)
    {
        // `dotnet ./bin/App.dll` runs an assembly — token 1 is a path, not a subcommand.
        var name = PassthroughSubcommands.CommandName(args);

        name.Should().Be(PassthroughSubcommands.Unknown);
        name.Should().NotContain(args[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "--info" }, "--info")]
    [InlineData(new[] { "--version" }, "--version")]
    [InlineData(new[] { "--list-sdks" }, "--list-sdks")]
    public void CommandName_KeepsRecognisedTopLevelOptions(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Fact]
    public void CommandName_IsCaseInsensitive()
    {
        PassthroughSubcommands.CommandName(["LIST", "PACKAGE"]).Should().Be("list package");
    }

    [Fact]
    public void CommandName_ReturnsUnknown_ForAnEmptyArgumentList()
    {
        PassthroughSubcommands.CommandName([]).Should().Be(PassthroughSubcommands.Unknown);
    }

    [Theory]
    [InlineData(new[] { "publish" })]
    [InlineData(new[] { "pack" })]
    [InlineData(new[] { "list", "package" })]
    [InlineData(new[] { "ef", "migrations" })]
    [InlineData(new[] { "msbuild" })]
    public void IsMeasurable_IsTrue_ForBatchSubcommands(string[] args)
    {
        PassthroughSubcommands.IsMeasurable(args).Should().BeTrue();
    }

    [Theory]
    [InlineData(new[] { "run" })]
    [InlineData(new[] { "watch" })]
    [InlineData(new[] { "new", "console" })]
    [InlineData(new[] { "--info" })]
    [InlineData(new[] { "./bin/App.dll" })]
    [InlineData(new string[0])]
    public void IsMeasurable_IsFalse_ForInteractiveOrUnrecognisedInvocations(string[] args)
    {
        // Capturing these would change behaviour the user depends on, or measure nothing useful.
        PassthroughSubcommands.IsMeasurable(args).Should().BeFalse();
    }

    [Fact]
    public void EveryMeasurableSubcommand_IsAlsoARecognisedVerb()
    {
        // Otherwise a subcommand could be captured and then recorded as "(other)", making the
        // measurement unattributable.
        foreach (var sub in PassthroughSubcommands.Measurable)
        {
            PassthroughSubcommands.CommandName([sub]).Should().Be(sub);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~PassthroughSubcommandsTests"`
Expected: FAIL — build error, `PassthroughSubcommands` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs`:

```csharp
namespace DotnetTokenKiller.Domain;

/// <summary>
/// Classifies <c>dotnet</c> invocations that dtk does not filter, for coverage tracking.
/// </summary>
/// <remarks>
/// Every value this type returns comes from a hardcoded allowlist. No fragment of the user's
/// argument list is ever passed through, because those arguments routinely contain file paths,
/// package names, and connection strings, and this value is written to the tracking database
/// and displayed in reports.
/// </remarks>
public static class PassthroughSubcommands
{
    /// <summary>The command name recorded when the invocation matches no known verb or option.</summary>
    public const string Unknown = "(other)";

    /// <summary>
    /// Subcommands whose output is batch rather than interactive, so capturing it to measure its
    /// size does not change behaviour the user depends on.
    /// </summary>
    public static readonly IReadOnlySet<string> Measurable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "publish", "pack", "list", "tool", "workload", "sln", "msbuild", "ef"
        };

    /// <summary>
    /// Real <c>dotnet</c> subcommands. A first token outside this set is not a subcommand — most
    /// importantly it may be an assembly path, as in <c>dotnet ./bin/App.dll</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownVerbs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "build", "build-server", "clean", "dev-certs", "ef", "format", "fsi", "help",
            "list", "msbuild", "new", "nuget", "pack", "package", "publish", "reference", "remove",
            "restore", "run", "sdk", "sln", "solution", "store", "test", "tool", "user-jwts",
            "user-secrets", "vstest", "watch", "workload"
        };

    /// <summary>Top-level <c>dotnet</c> options that are worth recording under their own name.</summary>
    private static readonly IReadOnlySet<string> KnownOptions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--info", "--version", "--list-sdks", "--list-runtimes", "--help", "-h", "--diagnostics"
        };

    /// <summary>
    /// The second token, per subcommand, that meaningfully changes what the command does. Without
    /// this, <c>list package</c> (worth filtering) and <c>list reference</c> (not) collapse into
    /// one row.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> QualifyingVerbs =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["list"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "package", "reference" },
            ["ef"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "migrations", "database", "dbcontext" },
            ["tool"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "restore", "install", "update" },
            ["sln"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "add", "remove" },
            ["workload"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "search" },
            ["nuget"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "locals", "push", "verify" }
        };

    /// <summary>Reduces a passthrough invocation to a safe, low-cardinality command name.</summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand (that is,
    /// <c>args[1..]</c> at the passthrough branch in <c>Program.cs</c>).
    /// </param>
    /// <returns>
    /// A name drawn entirely from this type's allowlists, such as <c>"list package"</c>,
    /// <c>"publish"</c>, <c>"--info"</c>, or <see cref="Unknown"/>.
    /// </returns>
    public static string CommandName(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count == 0)
        {
            return Unknown;
        }

        var head = dotnetArgs[0];

        if (KnownOptions.Contains(head))
        {
            return Canonical(KnownOptions, head);
        }

        if (!KnownVerbs.Contains(head))
        {
            return Unknown;
        }

        var verb = Canonical(KnownVerbs, head);

        if (dotnetArgs.Count < 2 ||
            !QualifyingVerbs.TryGetValue(verb, out var allowed) ||
            !allowed.Contains(dotnetArgs[1]))
        {
            return verb;
        }

        return $"{verb} {Canonical(allowed, dotnetArgs[1])}";
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation's output can be captured and measured
    /// without changing behaviour the user depends on.
    /// </summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand.
    /// </param>
    public static bool IsMeasurable(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        return dotnetArgs.Count > 0 && Measurable.Contains(dotnetArgs[0]);
    }

    /// <summary>
    /// Returns the allowlist's own spelling of <paramref name="value"/>, so a name recorded from
    /// <c>DOTNET LIST PACKAGE</c> is identical to one recorded from <c>dotnet list package</c> and
    /// the two do not become separate rows.
    /// </summary>
    /// <param name="allowlist">The set that matched, searched case-insensitively.</param>
    /// <param name="value">The user-supplied token that matched.</param>
    private static string Canonical(IReadOnlySet<string> allowlist, string value)
    {
        foreach (var candidate in allowlist)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return value;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests --filter "FullyQualifiedName~PassthroughSubcommandsTests"`
Expected: PASS — 38 tests.

- [ ] **Step 5: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs \
        tests/DotnetTokenKiller.Domain.Tests/PassthroughSubcommandsTests.cs
git commit -m "feat(domain): classify passthrough invocations without leaking argv"
```

---

## Task 3: Persist `outcome` and exclude passthrough from savings

**Files:**
- Modify: `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs`

**Interfaces:**
- Consumes: `RunOutcome`, `RunOutcomes.CountedInSavings`, `CommandRecord.Outcome` (Task 1).
- Produces: an `outcome TEXT NOT NULL DEFAULT 'Filtered'` column; `GetHistoryAsync` returns records with `Outcome` populated; `GetSummaryAsync` counts only `RunOutcomes.CountedInSavings`.

- [ ] **Step 1: Write the failing test**

Append to `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs` (inside the existing class). Note the existing `MakeRecord` helper needs an `outcome` parameter — add `RunOutcome outcome = RunOutcome.Filtered` as its final parameter and pass it through to the `CommandRecord` constructor.

```csharp
    [Fact]
    public async Task RecordAsync_RoundTripsOutcome()
    {
        await _sut.RecordAsync(MakeRecord(outcome: RunOutcome.PassthroughMeasured));

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.PassthroughMeasured);
    }

    [Fact]
    public async Task RecordAsync_DefaultsToFiltered_WhenOutcomeNotSupplied()
    {
        await _sut.RecordAsync(MakeRecord());

        var history = await _sut.GetHistoryAsync(1, null);

        history.Should().ContainSingle().Which.Outcome.Should().Be(RunOutcome.Filtered);
    }

    [Fact]
    public async Task GetSummaryAsync_IsUnchanged_WhenPassthroughRowsAreAdded()
    {
        // The guarantee that shipping coverage tracking does not move anybody's gain numbers.
        // If the outcome exclusion is ever dropped, this fails loudly.
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 1000, outputTokens: 100, savedTokens: 900));
        var before = await _sut.GetSummaryAsync(1, null);

        await _sut.RecordAsync(MakeRecord(
            "publish",
            inputTokens: 50_000,
            outputTokens: 50_000,
            savedTokens: 0,
            savingsPct: 0.0,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("run", inputTokens: 0, outputTokens: 0, savedTokens: 0,
            savingsPct: 0.0, outcome: RunOutcome.PassthroughUnmeasured));

        var after = await _sut.GetSummaryAsync(1, null);

        after.TotalCommands.Should().Be(before.TotalCommands);
        after.TotalInputTokens.Should().Be(before.TotalInputTokens);
        after.TotalOutputTokens.Should().Be(before.TotalOutputTokens);
        after.TotalSavedTokens.Should().Be(before.TotalSavedTokens);
        after.AverageSavingsPercentage.Should().BeApproximately(before.AverageSavingsPercentage, 0.001);
        after.CommandDetails.Should().NotContainKey("publish");
        after.CommandDetails.Should().NotContainKey("run");
    }

    [Theory]
    [InlineData(RunOutcome.RawTailFallback)]
    [InlineData(RunOutcome.FilterFaulted)]
    public async Task GetSummaryAsync_IncludesDegradedFilteredRuns(RunOutcome outcome)
    {
        // These ran a filter — badly — so they still represent real savings and stay in the math.
        await _sut.RecordAsync(MakeRecord("build", outcome: outcome));

        var summary = await _sut.GetSummaryAsync(1, null);

        summary.TotalCommands.Should().Be(1);
        summary.CommandDetails.Should().ContainKey("build");
    }

    [Fact]
    public async Task Schema_AddsOutcomeColumnToLegacyDatabase_DefaultingExistingRowsToFiltered()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dtk-legacy-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        try
        {
            // Build the pre-outcome schema by hand and seed a row, as an older dtk would have.
            await using (var legacy = new SqliteConnection(connectionString))
            {
                await legacy.OpenAsync();
                await using var cmd = legacy.CreateCommand();
                cmd.CommandText = """
                                  CREATE TABLE commands (
                                      id INTEGER PRIMARY KEY AUTOINCREMENT,
                                      timestamp TEXT NOT NULL,
                                      command TEXT NOT NULL,
                                      project_path TEXT NOT NULL,
                                      input_tokens INTEGER NOT NULL,
                                      output_tokens INTEGER NOT NULL,
                                      saved_tokens INTEGER NOT NULL,
                                      savings_percentage REAL NOT NULL,
                                      execution_time_ms REAL NOT NULL,
                                      success INTEGER NOT NULL DEFAULT 1
                                  );
                                  INSERT INTO commands
                                      (timestamp, command, project_path, input_tokens, output_tokens,
                                       saved_tokens, savings_percentage, execution_time_ms, success)
                                  VALUES (@ts, 'build', '/legacy', 900, 90, 810, 90.0, 12.0, 1);
                                  """;
                cmd.Parameters.AddWithValue("@ts",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await cmd.ExecuteNonQueryAsync();
            }

            await using var tracker = new SqliteTracker(connectionString);
            var history = await tracker.GetHistoryAsync(1, null);

            var stored = history.Should().ContainSingle().Subject;
            stored.Command.Should().Be("build");
            stored.Outcome.Should().Be(RunOutcome.Filtered);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
```

Add `using System.Globalization;` to the file's usings if it is not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~SqliteTrackerTests"`
Expected: FAIL — build error, `MakeRecord` has no `outcome` parameter and `CommandRecord.Outcome` is not read back.

- [ ] **Step 3: Write minimal implementation**

In `SqliteTracker.cs`:

**3a.** Add the shared outcome-list literal near `HistoryLimit`:

```csharp
    private const int HistoryLimit = 500;

    /// <summary>
    /// The SQL literal list of outcomes that count toward savings, derived from
    /// <see cref="RunOutcomes.CountedInSavings"/> so the two can never disagree.
    /// </summary>
    private static readonly string CountedInSavingsSqlList =
        string.Join(", ", RunOutcomes.CountedInSavings.Select(o => $"'{o}'"));
```

Add `using System.Linq;` if the file does not already have it implicitly via global usings.

**3b.** Add `outcome` to the `CREATE TABLE` in `InitializeSchemaAsync`, immediately after the `success` column:

```csharp
                                    success INTEGER NOT NULL DEFAULT 1,
                                    outcome TEXT NOT NULL DEFAULT 'Filtered'
```

**3c.** Replace the inline `success` migration block in `InitializeSchemaAsync` with two calls to a shared helper, and add that helper. The existing `success` block (from `await using var checkCmd` to the end of the method) becomes:

```csharp
        // Migrations for databases created before these columns existed. Fresh databases already
        // have them (see CREATE TABLE above) so these only run on legacy files.
        await EnsureColumnAsync("success", "INTEGER NOT NULL DEFAULT 1", ct).ConfigureAwait(false);
        await EnsureColumnAsync("outcome", "TEXT NOT NULL DEFAULT 'Filtered'", ct).ConfigureAwait(false);
    }

    /// <summary>Adds a column to the commands table if it is not already present.</summary>
    /// <param name="columnName">The column to ensure exists.</param>
    /// <param name="columnDefinition">The SQL type and constraints for the column.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task EnsureColumnAsync(string columnName, string columnDefinition, CancellationToken ct)
    {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var checkCmd = _connection.CreateCommand();
#pragma warning restore CA2007
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('commands') WHERE name = @name";
        checkCmd.Parameters.AddWithValue("@name", columnName);
        var columnExists = (long)(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
        if (columnExists)
        {
            return;
        }

        try
        {
#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
            await using var alterCmd = _connection.CreateCommand();
#pragma warning restore CA2007
#pragma warning disable CA2100 // columnName and columnDefinition are caller-supplied constant literals
            alterCmd.CommandText = $"ALTER TABLE commands ADD COLUMN {columnName} {columnDefinition}";
#pragma warning restore CA2100
            await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // A concurrent initializer added the column between our pragma check and this ALTER.
            // The column now exists, which is all we required — the losing racer is fine.
        }
    }
```

**3d.** Write the column in `RecordAsync` — extend the INSERT column list, values list, and parameters:

```csharp
            cmd.CommandText = """
                              INSERT INTO commands (timestamp, command, project_path, input_tokens, output_tokens,
                                  saved_tokens, savings_percentage, execution_time_ms, success, outcome)
                              VALUES (@ts, @cmd, @path, @in, @out, @saved, @pct, @ms, @success, @outcome)
                              """;
```

and after the `@success` parameter:

```csharp
            cmd.Parameters.AddWithValue("@outcome", record.Outcome.ToString());
```

**3e.** Exclude passthrough rows from `GetSummaryAsync`. Its `sql` is currently a `const string`; because it now interpolates `CountedInSavingsSqlList` it must become a `static readonly string`. Replace the `const string sql = ...` declaration with:

```csharp
        var sql = $"""
                   SELECT command, success,
                          COUNT(*) as run_count,
                          SUM(input_tokens) as total_input,
                          SUM(output_tokens) as total_output,
                          SUM(saved_tokens) as total_saved,
                          AVG(savings_percentage) as avg_pct,
                          SUM(execution_time_ms) as total_ms
                   FROM commands
                   WHERE timestamp >= @since
                     AND (@path IS NULL OR project_path = @path)
                     AND (@cmd IS NULL OR command = @cmd)
                     AND outcome IN ({CountedInSavingsSqlList})
                   GROUP BY command, success
                   ORDER BY command, success DESC
                   """;
```

**3f.** Return the column from `GetHistoryAsync`. Add `outcome` to its SELECT list (after `success`), then in `ReadHistoryAsync` parse it as the tenth column:

```csharp
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // An outcome written by a newer dtk that this build does not know is treated as
            // Filtered rather than crashing the report.
            var outcome = Enum.TryParse<RunOutcome>(reader.GetString(9), out var parsed)
                ? parsed
                : RunOutcome.Filtered;

            results.Add(new CommandRecord(
                DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetString(1),
                reader.GetString(2),
                new TokenStatistics(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetDouble(6)),
                TimeSpan.FromMilliseconds(reader.GetDouble(7)),
                reader.GetInt32(8) != 0,
                outcome));
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~SqliteTrackerTests"`
Expected: PASS, including the six new tests.

- [ ] **Step 5: Verify nothing else broke**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs \
        tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs
git commit -m "feat(infra): persist run outcome and keep passthrough out of savings math"
```

---

## Task 4: The coverage query

**Files:**
- Create: `src/DotnetTokenKiller.Domain/Tracking/CoverageSummary.cs`
- Modify: `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs`, `src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs`, `src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs`

**Interfaces:**
- Consumes: `RunOutcome`, `RunOutcomes.IsPassthrough` (Task 1); the `outcome` column (Task 3).
- Produces:
  - `sealed record CoverageDetail(string Command, RunOutcome Outcome, int RunCount, long TotalInputTokens, TimeSpan TotalExecutionTime)`
  - `sealed record CoverageSummary(IReadOnlyList<CoverageDetail> Entries, int TotalRuns, long TotalUnfilteredInputTokens)`
  - `ITracker.GetCoverageAsync(int days, string? projectPath, string? commandFilter = null, CancellationToken cancellationToken = default) -> Task<CoverageSummary>`
  - `GainReportUseCase.GetCoverageAsync(...)` with the same signature.

- [ ] **Step 1: Write the failing test**

Append to `SqliteTrackerTests`:

```csharp
    [Fact]
    public async Task GetCoverageAsync_RanksByInputTokensDescending()
    {
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 500, outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 9000, outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("list package", inputTokens: 3000,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Select(e => e.Command)
            .Should().ContainInOrder("publish", "list package", "pack");
    }

    [Fact]
    public async Task GetCoverageAsync_BreaksTokenTiesByRunCount()
    {
        // Every PassthroughUnmeasured row has zero tokens. Without the tiebreak the most-run
        // unmeasured command would sort arbitrarily and stay invisible.
        for (var i = 0; i < 5; i++)
        {
            await _sut.RecordAsync(MakeRecord("watch", inputTokens: 0,
                outcome: RunOutcome.PassthroughUnmeasured));
        }

        await _sut.RecordAsync(MakeRecord("run", inputTokens: 0, outcome: RunOutcome.PassthroughUnmeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Select(e => e.Command).Should().ContainInOrder("watch", "run");
    }

    [Fact]
    public async Task GetCoverageAsync_GroupsByCommandAndOutcome()
    {
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 100,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 200,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 0,
            outcome: RunOutcome.PassthroughUnmeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        var measured = coverage.Entries.Should()
            .ContainSingle(e => e.Command == "publish" && e.Outcome == RunOutcome.PassthroughMeasured).Subject;
        measured.RunCount.Should().Be(2);
        measured.TotalInputTokens.Should().Be(300);

        coverage.Entries.Should()
            .ContainSingle(e => e.Command == "publish" && e.Outcome == RunOutcome.PassthroughUnmeasured);
    }

    [Fact]
    public async Task GetCoverageAsync_IncludesFilteredRuns_SoCoveredAndUncoveredCanBeCompared()
    {
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 7000));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 100,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Should().Contain(e => e.Command == "build" && e.Outcome == RunOutcome.Filtered);
        coverage.TotalRuns.Should().Be(2);
    }

    [Fact]
    public async Task GetCoverageAsync_TotalUnfilteredInputTokens_CountsOnlyPassthroughRows()
    {
        await _sut.RecordAsync(MakeRecord("build", inputTokens: 7000));
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.TotalUnfilteredInputTokens.Should().Be(1000);
    }

    [Fact]
    public async Task GetCoverageAsync_HonoursTheCommandFilter()
    {
        await _sut.RecordAsync(MakeRecord("publish", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("pack", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, null, "publish");

        coverage.Entries.Should().ContainSingle().Which.Command.Should().Be("publish");
    }

    [Fact]
    public async Task GetCoverageAsync_HonoursTheProjectFilter()
    {
        await _sut.RecordAsync(MakeRecord("publish", "/a", inputTokens: 400,
            outcome: RunOutcome.PassthroughMeasured));
        await _sut.RecordAsync(MakeRecord("publish", "/b", inputTokens: 600,
            outcome: RunOutcome.PassthroughMeasured));

        var coverage = await _sut.GetCoverageAsync(1, "/a");

        coverage.Entries.Should().ContainSingle().Which.TotalInputTokens.Should().Be(400);
    }

    [Fact]
    public async Task GetCoverageAsync_ReturnsEmpty_WhenNothingRecorded()
    {
        var coverage = await _sut.GetCoverageAsync(1, null);

        coverage.Entries.Should().BeEmpty();
        coverage.TotalRuns.Should().Be(0);
        coverage.TotalUnfilteredInputTokens.Should().Be(0);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~SqliteTrackerTests"`
Expected: FAIL — build error, `GetCoverageAsync` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Domain/Tracking/CoverageSummary.cs`:

```csharp
namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>One command-and-outcome group in a coverage report.</summary>
/// <param name="Command">The recorded command name.</param>
/// <param name="Outcome">How runs in this group produced their output.</param>
/// <param name="RunCount">Number of runs in this group.</param>
/// <param name="TotalInputTokens">Total raw output tokens across these runs; zero when unmeasured.</param>
/// <param name="TotalExecutionTime">Total wall-clock execution time across these runs.</param>
public sealed record CoverageDetail(
    string Command,
    RunOutcome Outcome,
    int RunCount,
    long TotalInputTokens,
    TimeSpan TotalExecutionTime);

/// <summary>Where dtk is and is not filtering, ranked by how much output is at stake.</summary>
/// <param name="Entries">Groups ordered by total input tokens descending, then run count descending.</param>
/// <param name="TotalRuns">Total runs across every group, filtered and not.</param>
/// <param name="TotalUnfilteredInputTokens">
/// Total raw tokens that reached the caller without passing through any filter. This is the size
/// of the prize available to the next filter.
/// </param>
public sealed record CoverageSummary(
    IReadOnlyList<CoverageDetail> Entries,
    int TotalRuns,
    long TotalUnfilteredInputTokens);
```

Add to `ITracker.cs`:

```csharp
    /// <summary>Returns the filtering-coverage breakdown for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="commandFilter">Optional command name filter (e.g. "publish").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CoverageSummary> GetCoverageAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default);
```

Add to `SqliteTracker.cs`, next to `GetHistoryAsync`:

```csharp
    /// <inheritdoc/>
    public Task<CoverageSummary> GetCoverageAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT command, outcome,
                                  COUNT(*) as run_count,
                                  SUM(input_tokens) as total_input,
                                  SUM(execution_time_ms) as total_ms
                           FROM commands
                           WHERE timestamp >= @since
                             AND (@path IS NULL OR project_path = @path)
                             AND (@cmd IS NULL OR command = @cmd)
                           GROUP BY command, outcome
                           ORDER BY total_input DESC, run_count DESC
                           """;

        return ExecuteWithFilterAsync(days, projectPath, commandFilter, sql, ReadCoverageAsync, null,
            cancellationToken);
    }

    private static async Task<CoverageSummary> ReadCoverageAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var entries = new List<CoverageDetail>();
        var totalRuns = 0;
        long totalUnfiltered = 0;

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2007
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var outcome = Enum.TryParse<RunOutcome>(reader.GetString(1), out var parsed)
                ? parsed
                : RunOutcome.Filtered;
            var runCount = reader.GetInt32(2);
            var inputTokens = reader.GetInt64(3);

            entries.Add(new CoverageDetail(
                reader.GetString(0),
                outcome,
                runCount,
                inputTokens,
                TimeSpan.FromMilliseconds(reader.GetDouble(4))));

            totalRuns += runCount;
            if (RunOutcomes.IsPassthrough(outcome))
            {
                totalUnfiltered += inputTokens;
            }
        }

        return new CoverageSummary(entries, totalRuns, totalUnfiltered);
    }
```

Add to `GainReportUseCase.cs`:

```csharp
    /// <summary>Returns the filtering-coverage breakdown for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="commandFilter">Optional command name filter (e.g. "publish").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CoverageSummary> GetCoverageAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default)
    {
        return tracker.GetCoverageAsync(days, projectPath, commandFilter, cancellationToken);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~SqliteTrackerTests"`
Expected: PASS, including the eight new tests.

- [ ] **Step 5: Verify nothing else broke**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. Any test double implementing `ITracker` now needs `GetCoverageAsync`; NSubstitute substitutes generate it automatically, so no test changes should be needed. If a hand-written fake exists, add the method returning `new CoverageSummary([], 0, 0)`.

- [ ] **Step 6: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Domain/Tracking/CoverageSummary.cs \
        src/DotnetTokenKiller.Domain/Tracking/ITracker.cs \
        src/DotnetTokenKiller.Infrastructure/Tracking/SqliteTracker.cs \
        src/DotnetTokenKiller.Application/UseCases/GainReportUseCase.cs \
        tests/DotnetTokenKiller.Infrastructure.Tests/Tracking/SqliteTrackerTests.cs
git commit -m "feat: add coverage query ranking commands by unfiltered token volume"
```

---

## Task 5: `RunStreamedAsync`

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs`, `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ICommandRunner.RunStreamedAsync(string command, IReadOnlyList<string> args, TextWriter stdOutSink, TextWriter stdErrSink, CancellationToken cancellationToken = default) -> Task<CommandResult>`. The returned `CommandResult` carries the same text that was written to the sinks.

- [ ] **Step 1: Write the failing test**

Append to `ProcessCommandRunnerTests`. Match the existing file's conventions for locating a shell — read the file first and reuse whatever helper it already uses to run a trivial cross-platform command. If it has none, use the pattern below.

```csharp
    [Fact]
    public async Task RunStreamedAsync_EchoesStdoutToTheSink_AndReturnsTheSameText()
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--version"], stdOut, stdErr);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().NotBeEmpty();
        stdOut.ToString().Should().Be(result.StdOut);
    }

    [Fact]
    public async Task RunStreamedAsync_ReturnsNonZeroExitCode_ForAFailingCommand()
    {
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--this-option-does-not-exist"], stdOut, stdErr);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task RunStreamedAsync_Throws_WhenTheCommandCannotBeStarted()
    {
        var sut = new ProcessCommandRunner();

        var act = async () => await sut.RunStreamedAsync(
            "dtk-no-such-binary-exists", [], TextWriter.Null, TextWriter.Null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunStreamedAsync_HandlesOutputLargerThanThePipeBuffer()
    {
        // Both streams must be pumped concurrently; a sequential reader deadlocks here.
        var stdOut = new StringWriter();
        var sut = new ProcessCommandRunner();

        var result = await sut.RunStreamedAsync(
            "dotnet", ["--info"], stdOut, TextWriter.Null);

        result.ExitCode.Should().Be(0);
        stdOut.ToString().Should().Contain(".NET SDK");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~RunStreamedAsync"`
Expected: FAIL — build error, `RunStreamedAsync` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `ICommandRunner.cs`:

```csharp
    /// <summary>
    /// Runs the command, writing each line of output to the given sinks as it arrives while also
    /// accumulating it, so the output can be measured without withholding it from the user.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="stdOutSink">Receives standard output, line by line, as it is produced.</param>
    /// <param name="stdErrSink">Receives standard error, line by line, as it is produced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommandResult> RunStreamedAsync(
        string command,
        IReadOnlyList<string> args,
        TextWriter stdOutSink,
        TextWriter stdErrSink,
        CancellationToken cancellationToken = default);
```

Add to `ProcessCommandRunner.cs`, after `RunCapturedAsync`:

```csharp
    /// <inheritdoc/>
    public async Task<CommandResult> RunStreamedAsync(
        string command,
        IReadOnlyList<string> args,
        TextWriter stdOutSink,
        TextWriter stdErrSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdOutSink);
        ArgumentNullException.ThrowIfNull(stdErrSink);

        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = StartProcess(psi, command);

        // Close stdin immediately so a child that reads it sees EOF and exits instead of hanging
        // forever waiting for input this non-interactive capture will never provide.
        process.StandardInput.Close();

#pragma warning disable CA2016 // CancellationToken is handled via registration below
        var registration = cancellationToken.Register(static state => KillProcess((Process)state!), process);
#pragma warning restore CA2016
        try
        {
            // CRITICAL: pump both streams concurrently — sequential reads deadlock on large output
            var stdOutTask = PumpAsync(process.StandardOutput, stdOutSink, cancellationToken);
            var stdErrTask = PumpAsync(process.StandardError, stdErrSink, cancellationToken);
            await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return new CommandResult(await stdOutTask.ConfigureAwait(false),
                await stdErrTask.ConfigureAwait(false), process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            await KillAndReapAsync(process).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Copies a stream to a sink line by line, returning everything it copied.</summary>
    /// <param name="reader">The child process stream to read.</param>
    /// <param name="sink">The destination to echo each line to as it arrives.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<string> PumpAsync(
        StreamReader reader,
        TextWriter sink,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // Flush per line: the whole point of streaming is that the user sees progress, and a
            // buffered sink would defeat that on a long-running publish.
            await sink.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await sink.FlushAsync(cancellationToken).ConfigureAwait(false);
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests --filter "FullyQualifiedName~RunStreamedAsync"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Verify nothing else broke**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. `ICommandRunner` gained a member; NSubstitute substitutes handle it automatically.

- [ ] **Step 6: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs \
        src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs \
        tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs
git commit -m "feat(infra): add streaming runner that echoes output while measuring it"
```

---

## Task 6: `FilteredRunUseCase` records its outcome

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs`

**Interfaces:**
- Consumes: `RunOutcome` (Task 1).
- Produces: no new public surface. `FilteredRunUseCase` now records `RunOutcome.Filtered`, `RunOutcome.RawTailFallback`, or `RunOutcome.FilterFaulted`.

- [ ] **Step 1: Write the failing test**

Append to `FilteredRunUseCaseTests`:

```csharp
    [Fact]
    public async Task RunAsync_RecordsFiltered_WhenTheFilterSucceeds()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.Filtered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsFilterFaulted_WhenTheFilterThrows()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsRawTailFallback_WhenAFailedCommandFiltersToNothing()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("line one\nline two\n", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("   ");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.RawTailFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PrefersFilterFaulted_WhenTheFilterThrowsOnAFailedCommand()
    {
        // A throwing filter yields the raw text, which is non-empty, so the raw-tail branch cannot
        // also fire. The two outcomes are mutually exclusive by construction, not by ordering.
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("some raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FilteredRunUseCaseTests"`
Expected: FAIL — the four new tests fail because every record carries the default `RunOutcome.Filtered`.

- [ ] **Step 3: Write minimal implementation**

In `FilteredRunUseCase.cs`:

**3a.** Change `ApplyFilterSafelyAsync` to report whether it faulted. Its signature and return become:

```csharp
    /// <summary>Applies the filter, falling back to raw output if the filter throws (never breaks the workflow).</summary>
    /// <param name="filter">The output filter to apply.</param>
    /// <param name="stripped">The ANSI-stripped command output.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="verbosityLevel">Verbosity level controlling diagnostic output.</param>
    /// <returns>
    /// The filtered output and whether the filter threw. A faulted filter yields the stripped
    /// output unchanged.
    /// </returns>
    private async Task<(string Output, bool Faulted)> ApplyFilterSafelyAsync(
        IOutputFilter filter,
        string stripped,
        int exitCode,
        int verbosityLevel)
    {
        try
        {
            return (filter.Apply(stripped, exitCode), false);
        }
        catch
        {
            // Intentional: filter errors must not break the user's workflow
            if (verbosityLevel >= 2)
            {
                await output.WriteLineAsync("[filter error — using raw output]").ConfigureAwait(false);
            }

            return (stripped, true);
        }
    }
```

**3b.** In `RunAsync`, destructure the result and derive the outcome. Replace the `var filtered = await ApplyFilterSafelyAsync(...)` line with:

```csharp
        var (filtered, filterFaulted) = await ApplyFilterSafelyAsync(filter, stripped, result.ExitCode, verbosityLevel)
            .ConfigureAwait(false);
```

Then after the `usedRawTailFallback` block, add:

```csharp
        // A faulted filter yields the raw text, which is non-empty, so the raw-tail branch above
        // cannot also have fired. Checking faulted first documents that rather than relying on it.
        var outcome = filterFaulted
            ? RunOutcome.FilterFaulted
            : usedRawTailFallback
                ? RunOutcome.RawTailFallback
                : RunOutcome.Filtered;
```

**3c.** Pass it through. Change the `TrackIfEnabledAsync` call to:

```csharp
        await TrackIfEnabledAsync(config, commandSlug, stripped, filtered, stopwatch.Elapsed, result.ExitCode,
                outcome, cancellationToken)
            .ConfigureAwait(false);
```

and the method to accept and use it:

```csharp
    private async Task TrackIfEnabledAsync(
        DtkConfig config,
        string commandSlug,
        string stripped,
        string filtered,
        TimeSpan elapsed,
        int exitCode,
        RunOutcome outcome,
        CancellationToken cancellationToken)
```

with the `CommandRecord` construction gaining the outcome:

```csharp
            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                commandSlug,
                Environment.CurrentDirectory,
                new TokenStatistics(inputTokens, outputTokens, savedTokens, savingsPct),
                elapsed,
                exitCode == 0,
                outcome);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~FilteredRunUseCaseTests"`
Expected: PASS, including the four new tests.

- [ ] **Step 5: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs \
        tests/DotnetTokenKiller.Application.Tests/UseCases/FilteredRunUseCaseTests.cs
git commit -m "feat(app): record when a filtered run degraded to fallback or faulted"
```

---

## Task 7: `PassthroughRunUseCase`

**Files:**
- Create: `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`

**Interfaces:**
- Consumes: `RunOutcome` (Task 1); `PassthroughSubcommands.CommandName` / `.IsMeasurable` (Task 2); `ICommandRunner.RunStreamedAsync` (Task 5).
- Produces: `sealed class PassthroughRunUseCase(ICommandRunner commandRunner, ITracker tracker, TextWriter stdOut, TextWriter stdErr)` with `Task<int> RunAsync(DtkConfig config, string command, IReadOnlyList<string> dotnetArgs, CancellationToken cancellationToken = default)`.

The config is passed in rather than resolved from an `IConfigProvider` so the caller loads it exactly once — `JsonConfigProvider` does not cache, and the entry point in Task 8 needs the same config to build the tracker.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`:

```csharp
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class PassthroughRunUseCaseTests
{
    private static readonly string[] PublishArgs = ["publish", "-c", "Release"];
    private static readonly string[] RunArgs = ["run"];
    private readonly StringWriter _stdErr = new();
    private readonly StringWriter _stdOut = new();

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly PassthroughRunUseCase _sut;
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public PassthroughRunUseCaseTests()
    {
        _sut = new PassthroughRunUseCase(_runner, _tracker, _stdOut, _stdErr);
    }

    [Fact]
    public async Task RunAsync_MeasurableSubcommand_StreamsAndRecordsMeasured()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("a good deal of publish output", "", 0));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(0);
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.Outcome == RunOutcome.PassthroughMeasured &&
                r.Command == "publish" &&
                r.InputTokens > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MeasurableSubcommand_RecordsZeroSavings()
    {
        // Nothing was filtered, so every input token also reached the caller.
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("some output", "", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.SavedTokens == 0 &&
                r.SavingsPercentage == 0.0 &&
                r.InputTokens == r.OutputTokens),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonMeasurableSubcommand_UsesPassthroughAndRecordsUnmeasured()
    {
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        await _runner.DidNotReceive().RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.Outcome == RunOutcome.PassthroughUnmeasured &&
                r.Command == "run" &&
                r.InputTokens == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingDisabled_NeverCapturesAndNeverRecords()
    {
        // tracking.enabled = false is the escape hatch back to today's inherited-stdio behaviour.
        var config = DtkConfig.Default with { Tracking = new TrackingConfig(Enabled: false) };
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync(config, "dotnet", PublishArgs);

        await _runner.DidNotReceive().RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>());
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ReturnsTheChildExitCode_WhenMeasured()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "error text", 42));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheChildExitCode_WhenUnmeasured()
    {
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(7);

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        exitCode.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceExceptionAndKeepsTheExitCode()
    {
        // A broken tracking database must never change what a dotnet publish returns.
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 3));
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_RecordsQualifiedNameForListPackage()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["list", "package", "--outdated"]);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "list package"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CountsStderrTowardTheMeasuredTotal()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "a warning printed to standard error", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens > 0),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PassthroughRunUseCaseTests"`
Expected: FAIL — build error, `PassthroughRunUseCase` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`:

```csharp
using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Runs a dotnet subcommand dtk has no filter for, recording how much output it produced so the
/// next filter can be chosen from data rather than guessed.
/// </summary>
/// <param name="commandRunner">The command runner.</param>
/// <param name="tracker">The tracking store.</param>
/// <param name="stdOut">Receives the child's standard output when the run is measured.</param>
/// <param name="stdErr">Receives the child's standard error when the run is measured.</param>
public sealed class PassthroughRunUseCase(
    ICommandRunner commandRunner,
    ITracker tracker,
    TextWriter stdOut,
    TextWriter stdErr)
{
    /// <summary>Runs the command and records the run.</summary>
    /// <param name="config">
    /// The already-loaded configuration. Passed in rather than resolved so the caller reads the
    /// config file exactly once.
    /// </param>
    /// <param name="command">The executable to run.</param>
    /// <param name="dotnetArgs">The arguments to pass to it, starting at the subcommand.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The child process exit code.</returns>
    public async Task<int> RunAsync(
        DtkConfig config,
        string command,
        IReadOnlyList<string> dotnetArgs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        // With tracking off there is nothing to measure, so take the cheapest path and leave the
        // child's stdio attached to the terminal exactly as it is today — colour included.
        if (!config.Tracking.Enabled)
        {
            return await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandName = PassthroughSubcommands.CommandName(dotnetArgs);
        var stopwatch = Stopwatch.StartNew();

        if (!PassthroughSubcommands.IsMeasurable(dotnetArgs))
        {
            var passthroughExit = await commandRunner.RunPassthroughAsync(command, dotnetArgs, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            await TrackAsync(config, commandName, 0, stopwatch.Elapsed, passthroughExit,
                    RunOutcome.PassthroughUnmeasured, cancellationToken)
                .ConfigureAwait(false);
            return passthroughExit;
        }

        var result = await commandRunner
            .RunStreamedAsync(command, dotnetArgs, stdOut, stdErr, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var stripped = AnsiStrip.Strip(result.StdOut + result.StdErr);
        var tokens = TokenEstimator.Estimate(stripped, config.Tracking.Tokenizer);
        await TrackAsync(config, commandName, tokens, stopwatch.Elapsed, result.ExitCode,
                RunOutcome.PassthroughMeasured, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
    }

    /// <summary>Records the run, swallowing any failure so tracking cannot break the workflow.</summary>
    /// <param name="config">The loaded configuration.</param>
    /// <param name="commandName">The allowlisted command name.</param>
    /// <param name="tokens">Raw output tokens, or zero when the run was not measured.</param>
    /// <param name="elapsed">Wall-clock time for the run.</param>
    /// <param name="exitCode">The child process exit code.</param>
    /// <param name="outcome">Which passthrough outcome this run had.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task TrackAsync(
        DtkConfig config,
        string commandName,
        int tokens,
        TimeSpan elapsed,
        int exitCode,
        RunOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            // No filter ran, so every input token also reached the caller: output equals input and
            // savings are zero. Recording a negative or synthesized saving here would corrupt the
            // very ranking this exists to produce.
            var record = new CommandRecord(
                DateTimeOffset.UtcNow,
                commandName,
                Environment.CurrentDirectory,
                new TokenStatistics(tokens, tokens, 0, 0.0),
                elapsed,
                exitCode == 0,
                outcome);

            await tracker.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tracking errors must not surface to the user
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~PassthroughRunUseCaseTests"`
Expected: PASS — 9 tests.

- [ ] **Step 5: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs \
        tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs
git commit -m "feat(app): measure and record unfiltered passthrough runs"
```

---

## Task 8: Wire the passthrough branch

**Files:**
- Create: `src/DotnetTokenKiller.Infrastructure/Tracking/TrackerFactory.cs`, `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs`, `src/DotnetTokenKiller.Cli/Program.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/PassthroughIntegrationTests.cs`

**Interfaces:**
- Consumes: `PassthroughRunUseCase` (Task 7).
- Produces: `TrackerFactory.Create(DtkConfig config) -> SqliteTracker`; `PassthroughEntryPoint.RunAsync(string command, IReadOnlyList<string> dotnetArgs) -> Task<int>`.

`TrackerFactory` exists so the connection-string and retention logic lives in one place. Today it is duplicated between the DI registration and this new entry point; leaving it duplicated would let the two drift.

- [ ] **Step 1: Write the failing test**

The existing tests in `PassthroughIntegrationTests.cs` already assert behaviour that must not regress — leave them. Read `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs` first to see how it isolates `DTK_DB_PATH` and `DTK_CONFIG_PATH`; if it has no per-test DB isolation helper, set `DTK_DB_PATH` explicitly as below. Append:

```csharp
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_MeasurableSubcommand_StillPrintsOutputAndReturnsExitCode()
    {
        // The streaming path must not swallow output. `dotnet list package` needs a project, so
        // run it somewhere it will fail — the failure output still has to reach the caller.
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync("dotnet", "list", "package");

        exitCode.Should().NotBe(0);
        output.Should().NotBeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Passthrough_UnmeasurableSubcommand_MatchesRawDotnetExitCode()
    {
        var (_, dtkExit) = await IntegrationTestHelper.RunDtkAsync("dotnet", "--version");
        var (_, dotnetExit) = await IntegrationTestHelper.RunDotnetAsync("--version");

        dtkExit.Should().Be(dotnetExit);
    }
```

- [ ] **Step 2: Run test to verify it fails or passes for the wrong reason**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~PassthroughIntegrationTests"`
Expected: these two may already PASS against the current inherited-stdio path. That is fine — they are regression guards for the rewiring in Step 3, and Step 5 is what proves the new path is actually taken.

- [ ] **Step 3: Write minimal implementation**

Create `src/DotnetTokenKiller.Infrastructure/Tracking/TrackerFactory.cs`:

```csharp
using DotnetTokenKiller.Domain.Configuration;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>Builds a configured <see cref="SqliteTracker"/>.</summary>
/// <remarks>
/// The DI registration and the non-DI passthrough entry point both need a tracker built the same
/// way. Resolving the database path in one place keeps them from drifting apart.
/// </remarks>
public static class TrackerFactory
{
    /// <summary>Creates a tracker using the configured database path and retention period.</summary>
    /// <param name="config">The loaded configuration.</param>
    /// <returns>A tracker the caller owns and must dispose.</returns>
    public static SqliteTracker Create(DtkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var dbPath = EnvironmentOverride.Read("DTK_DB_PATH")
                     ?? config.Tracking.DbPath
                     ?? SqliteTracker.GetDefaultDbPath();
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        return new SqliteTracker(connectionString, config.Tracking.RetentionDays);
    }
}
```

Replace the tracker registration in `DependencyInjection.cs` with:

```csharp
        services.AddSingleton<ITracker>(sp =>
        {
            var configProvider = sp.GetRequiredService<IConfigProvider>();
            return TrackerFactory.Create(configProvider.Load());
        });
```

and drop the now-unused `using Microsoft.Data.Sqlite;` if nothing else in the file needs it.

Create `src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs`:

```csharp
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Runs a dotnet subcommand dtk does not filter, without building the DI container.
/// </summary>
/// <remarks>
/// This path deliberately skips Spectre and the service container, which together account for
/// most of dtk's startup cost. It pays only for one config file read and, when tracking is on,
/// one SQLite connection opened after the child has already exited.
/// </remarks>
internal static class PassthroughEntryPoint
{
    /// <summary>Runs the command and records the run.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="dotnetArgs">The arguments to pass to it, starting at the subcommand.</param>
    /// <returns>The child process exit code.</returns>
    internal static async Task<int> RunAsync(string command, IReadOnlyList<string> dotnetArgs)
    {
        var config = new JsonConfigProvider().Load();
        var runner = new ProcessCommandRunner();

        if (!config.Tracking.Enabled)
        {
            // Nothing to record, so never open the database at all.
            return await runner.RunPassthroughAsync(command, dotnetArgs).ConfigureAwait(false);
        }

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var tracker = TrackerFactory.Create(config);
#pragma warning restore CA2007
        var useCase = new PassthroughRunUseCase(runner, tracker, Console.Out, Console.Error);
        return await useCase.RunAsync(config, command, dotnetArgs).ConfigureAwait(false);
    }
}
```

Replace the passthrough branch in `Program.cs` (lines 23-28) with:

```csharp
// Passthrough: run any unsupported dotnet subcommand directly, recording what it cost so the
// coverage report can rank which subcommand is worth filtering next.
if (ArgumentPreprocessor.IsPassthrough(args))
{
    return await PassthroughEntryPoint.RunAsync(dotnetCmd, args[1..]).ConfigureAwait(false);
}
```

The `using DotnetTokenKiller.Infrastructure.Execution;` at the top of `Program.cs` may now be unused — remove it if the build warns.

- [ ] **Step 4: Run the integration tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~PassthroughIntegrationTests"`
Expected: PASS — output still reaches the caller and exit codes still match raw dotnet.

- [ ] **Step 5: Verify the row is actually written**

This is the step that proves the wiring, not just that nothing broke. Run against a throwaway database:

```bash
dtk dotnet build DotnetTokenKiller.slnx
export DTK_DB_PATH=/tmp/dtk-passthrough-check.db
rm -f "$DTK_DB_PATH"
dotnet run --project src/DotnetTokenKiller.Cli -- dotnet --version
dotnet run --project src/DotnetTokenKiller.Cli -- dotnet list package
sqlite3 "$DTK_DB_PATH" "SELECT command, outcome, input_tokens FROM commands;"
unset DTK_DB_PATH
```

Expected: two rows — `--version|PassthroughUnmeasured|0` and `list package|PassthroughMeasured|<non-zero>`.

If `sqlite3` is unavailable, run `dotnet run --project src/DotnetTokenKiller.Cli -- gain --coverage` after Task 9 instead and read the rows from the report.

- [ ] **Step 6: Verify nothing else broke**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Infrastructure/Tracking/TrackerFactory.cs \
        src/DotnetTokenKiller.Infrastructure/DependencyInjection.cs \
        src/DotnetTokenKiller.Cli/PassthroughEntryPoint.cs \
        src/DotnetTokenKiller.Cli/Program.cs \
        tests/DotnetTokenKiller.Cli.IntegrationTests/PassthroughIntegrationTests.cs
git commit -m "feat(cli): track passthrough runs without building the DI container"
```

---

## Task 9: `dtk gain --coverage`

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/Settings/GainCommandSettings.cs`, `src/DotnetTokenKiller.Cli/Commands/GainCommand.cs`, `src/DotnetTokenKiller.Cli/Formatting/GainDashboardRenderer.cs`, `src/DotnetTokenKiller.Cli/Serialization/GainSummaryJsonContext.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/GainCommandTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/Formatting/GainDashboardRendererTests.cs`

**Interfaces:**
- Consumes: `CoverageSummary`, `CoverageDetail`, `GainReportUseCase.GetCoverageAsync` (Task 4).
- Produces: `GainCommandSettings.Coverage` (`--coverage`); `GainDashboardRenderer.RenderCoverage(IAnsiConsole console, CoverageSummary coverage, string scope)`; `GainCommand.CsvHeader` gains a trailing `,outcome`.

- [ ] **Step 1: Write the failing test**

Read both test files first to match their existing setup (how they build a `GainCommand` and stub the tracker). Append to `GainCommandTests`:

```csharp
    [Fact]
    public async Task RunAsync_Coverage_ReportsUnfilteredCommands()
    {
        // Substitute the tracker so the report has known contents; follow the arrangement the
        // other tests in this file already use to construct the command under test.
        var coverage = new CoverageSummary(
            [
                new CoverageDetail("publish", RunOutcome.PassthroughMeasured, 4, 48_000, TimeSpan.FromSeconds(12)),
                new CoverageDetail("build", RunOutcome.Filtered, 30, 300_000, TimeSpan.FromSeconds(90))
            ],
            34,
            48_000);

        // Arrange the tracker substitute to return `coverage` from GetCoverageAsync, then:
        var exitCode = await sut.RunAsync(new GainCommandSettings { Coverage = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        var text = console.Output;
        text.Should().Contain("publish");
        text.Should().Contain("PassthroughMeasured");
    }

    [Fact]
    public async Task RunAsync_Coverage_WithNoData_PrintsAFriendlyMessage()
    {
        // Arrange GetCoverageAsync to return an empty summary, then:
        var exitCode = await sut.RunAsync(new GainCommandSettings { Coverage = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("No data yet");
    }

    [Fact]
    public async Task CsvHeader_EndsWithOutcome()
    {
        // The export must carry the new dimension or the CSV silently loses it.
        GainCommand.CsvHeader.Should().EndWith(",outcome");
    }
```

Append to `GainDashboardRendererTests`:

```csharp
    [Fact]
    public void RenderCoverage_ShowsUnfilteredTotalAndRanksEntriesInOrder()
    {
        var console = new TestConsole();
        var coverage = new CoverageSummary(
            [
                new CoverageDetail("publish", RunOutcome.PassthroughMeasured, 4, 48_000, TimeSpan.FromSeconds(12)),
                new CoverageDetail("pack", RunOutcome.PassthroughMeasured, 2, 6_000, TimeSpan.FromSeconds(3)),
                new CoverageDetail("watch", RunOutcome.PassthroughUnmeasured, 9, 0, TimeSpan.FromSeconds(400))
            ],
            15,
            54_000);

        GainDashboardRenderer.RenderCoverage(console, coverage, "Global Scope");

        var output = console.Output;
        output.Should().Contain("publish");
        output.Should().Contain("pack");
        output.Should().Contain("watch");
        output.IndexOf("publish", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("pack", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderCoverage_MarksUnmeasuredRowsDistinctlyFromZeroTokenMeasuredRows()
    {
        // "0 tokens because we did not look" must not read as "0 tokens because there were none".
        var console = new TestConsole();
        var coverage = new CoverageSummary(
            [new CoverageDetail("watch", RunOutcome.PassthroughUnmeasured, 3, 0, TimeSpan.FromSeconds(30))],
            3,
            0);

        GainDashboardRenderer.RenderCoverage(console, coverage, "Global Scope");

        console.Output.Should().Contain("not measured");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~GainCommandTests|FullyQualifiedName~GainDashboardRendererTests"`
Expected: FAIL — build error, `Coverage` and `RenderCoverage` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `GainCommandSettings.cs`:

```csharp
    /// <summary>Gets a value indicating whether to report filtering coverage instead of savings.</summary>
    [CommandOption("--coverage")]
    [Description("Report which commands run unfiltered, ranked by tokens at stake")]
    public bool Coverage { get; init; }
```

Add to `GainSummaryJsonContext.cs` — `UseStringEnumConverter` makes `RunOutcome` serialize as its name rather than an integer, which is what makes the JSON readable. `GainSummary` contains no enums, so existing `--json` output is unaffected:

```csharp
using System.Text.Json.Serialization;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(GainSummary))]
[JsonSerializable(typeof(CommandGainDetail))]
[JsonSerializable(typeof(CoverageSummary))]
[JsonSerializable(typeof(CoverageDetail))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext;
```

Add `RenderCoverage` to `GainDashboardRenderer.cs`:

```csharp
    /// <summary>Writes the coverage report: which commands ran unfiltered, and what that cost.</summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="coverage">The coverage summary to render.</param>
    /// <param name="scope">Human-readable scope description (e.g. "Global Scope, last 7 days").</param>
    public static void RenderCoverage(IAnsiConsole console, CoverageSummary coverage, string scope)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(coverage);

        console.MarkupLine($"[bold cyan]DTK Filter Coverage ({scope.EscapeMarkup()})[/]");
        console.MarkupLine($"[grey]{new string('═', RuleWidth)}[/]");
        console.WriteLine();
        console.MarkupLine($"Total runs:        {coverage.TotalRuns.ToString(CultureInfo.InvariantCulture)}");
        console.MarkupLine(
            $"[yellow]Unfiltered tokens: {TokenFormat.Tokens(coverage.TotalUnfilteredInputTokens)}[/]");
        console.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Command");
        table.AddColumn("Outcome");
        table.AddColumn(new TableColumn("Runs").RightAligned());
        table.AddColumn(new TableColumn("Raw tokens").RightAligned());

        foreach (var entry in coverage.Entries)
        {
            var tokens = entry.Outcome == RunOutcome.PassthroughUnmeasured
                ? "[grey]not measured[/]"
                : TokenFormat.Tokens(entry.TotalInputTokens);

            table.AddRow(
                entry.Command.EscapeMarkup(),
                $"[{OutcomeColor(entry.Outcome)}]{entry.Outcome}[/]",
                entry.RunCount.ToString(CultureInfo.InvariantCulture),
                tokens);
        }

        console.Write(table);
    }

    /// <summary>Maps an outcome to the colour that conveys how much attention it deserves.</summary>
    /// <param name="outcome">The outcome to colour.</param>
    private static string OutcomeColor(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Filtered => "green",
        RunOutcome.RawTailFallback or RunOutcome.FilterFaulted => "yellow",
        _ => "red"
    };
```

In `GainCommand.cs`:

**3a.** Extend the CSV header and each exported row:

```csharp
    internal const string CsvHeader =
        "timestamp,command,project_path,input_tokens,output_tokens,saved_tokens,savings_pct,execution_time_ms,success,outcome";
```

and append `,{r.Outcome}` to the end of the interpolated row string inside the export loop.

**3b.** Add the coverage branch in `RunAsync`, immediately after the `settings.Export` block and before `GetSummaryAsync` is called:

```csharp
        if (settings.Coverage)
        {
            var coverage = await gainReport.GetCoverageAsync(settings.Days, projectPath, commandFilter,
                    cancellationToken)
                .ConfigureAwait(false);

            if (settings.Json)
            {
                var coverageJson = JsonSerializer.Serialize(coverage,
                    GainSummaryJsonContext.Default.CoverageSummary);
                await output.WriteLineAsync(coverageJson).ConfigureAwait(false);
                return 0;
            }

            if (coverage.TotalRuns == 0)
            {
                console.MarkupLine(
                    "[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking coverage.[/]");
                return 0;
            }

            GainDashboardRenderer.RenderCoverage(console, coverage, BuildScope(settings));
            return 0;
        }
```

Add `using DotnetTokenKiller.Domain.Tracking;` to `GainDashboardRenderer.cs` if it is not already present (it is — the file already uses `GainSummary`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~GainCommandTests|FullyQualifiedName~GainDashboardRendererTests"`
Expected: PASS.

- [ ] **Step 5: Verify the whole suite and the real report**

```bash
dtk dotnet test DotnetTokenKiller.slnx
dotnet run --project src/DotnetTokenKiller.Cli -- gain --coverage
dotnet run --project src/DotnetTokenKiller.Cli -- gain
```

Expected: suite PASS; `gain --coverage` renders a table; plain `gain` shows the same savings numbers it did before this branch.

- [ ] **Step 6: Update the README**

`README.md` documents the `gain` flags. Add `--coverage` to that list with a one-line description matching the `[Description]` attribute text, and add a short subsection showing example output. Keep the existing flag descriptions unchanged.

- [ ] **Step 7: Commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
git add src/DotnetTokenKiller.Cli/ README.md tests/DotnetTokenKiller.Cli.IntegrationTests/
git commit -m "feat(cli): add dtk gain --coverage report"
```

---

## Task 10: Close the loop on the gap analysis

**Files:**
- Modify: `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`

- [ ] **Step 1: Mark §1 and §3 resolved**

Add a resolution note under §1, in the same style as the one §7 already carries:

```markdown
> **Resolved 2026-07-27.** Passthrough runs are now tracked. Allowlisted batch subcommands are
> streamed through and measured; interactive ones are counted but not captured. `dtk gain
> --coverage` ranks every command by unfiltered tokens at stake.
> See [the design](2026-07-27-coverage-tracking-design.md).
```

And under §3's second bullet (the fallback-visibility one):

```markdown
> **Resolved 2026-07-27.** Filter exceptions and raw-tail fallbacks are recorded as
> `FilterFaulted` and `RawTailFallback` respectively, and surface in `dtk gain --coverage`.
> Project-local extensible filters (the first bullet) remain open.
```

Update the "Suggested sequencing" list to strike item 2 and note that item 3 — the first new filter — is now the next step, to be chosen from what `--coverage` reports.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md
git commit -m "docs: mark gap-analysis §1 and §3 resolved"
```

---

## Self-Review

**Spec coverage.** Every section of the design maps to a task: the outcome table → Task 1; `PassthroughSubcommands` and the leak guarantee → Task 2; the `outcome` column, migration, and savings exclusion → Task 3; coverage query and sort tiebreak → Task 4; `RunStreamedAsync` → Task 5; degraded-outcome recording → Task 6; measured/unmeasured decision and `tracking.enabled` escape hatch → Task 7; non-DI wiring and `TrackerFactory` → Task 8; `--coverage`, JSON composition, CSV `outcome` column → Task 9. The spec's "Out of scope" (writing an actual new filter) is respected — no task adds one.

**One deliberate extension.** Task 2 extends the leak guarantee to the first argv token, covering `dotnet ./bin/App.dll`. Documented above the tasks.

**Type consistency.** `RunOutcome` / `RunOutcomes.CountedInSavings` / `RunOutcomes.IsPassthrough` (Task 1) are used under those exact names in Tasks 3, 4, 6, 7, 9. `PassthroughSubcommands.CommandName` / `.IsMeasurable` / `.Measurable` / `.Unknown` (Task 2) are used under those names in Tasks 2 and 7. `RunStreamedAsync(command, args, stdOutSink, stdErrSink, ct)` (Task 5) is called with that argument order in Task 7. `CoverageSummary(Entries, TotalRuns, TotalUnfilteredInputTokens)` and `CoverageDetail(Command, Outcome, RunCount, TotalInputTokens, TotalExecutionTime)` (Task 4) are constructed positionally in that order in Task 9's tests. `TrackerFactory.Create(DtkConfig)` (Task 8) is called from both the DI registration and `PassthroughEntryPoint`.

**Known soft spots for the implementer.** Task 8 Step 1 and Task 9 Step 1 both say to read the existing test file first and match its arrangement, because the exact substitute-wiring in `GainCommandTests` and the env-var isolation in `IntegrationTestHelper` were not read while writing this plan. Those are the only two places where the plan defers to what is already on disk; everywhere else the code is given in full.

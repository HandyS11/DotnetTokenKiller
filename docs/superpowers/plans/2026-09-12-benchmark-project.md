# Benchmark Project Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Measure both dimensions of dtk's performance — the wall-clock and allocation overhead it
adds, and the percentage of tokens it removes — so improvements are targetable and regressions
cannot land silently.

**Architecture:** Two new projects under a `/benchmarks/` solution folder.
`DotnetTokenKiller.Benchmarks.Corpus` is a plain library over Application holding the fixture
corpus, a seeded log generator, the savings engine, and the committed savings baseline.
`DotnetTokenKiller.Benchmarks` is the BenchmarkDotNet executable holding the timing benchmarks, a
cold-start process harness, and the baseline regeneration verb. The savings dimension is
hard-gated by a new test class in Application.Tests; the timing dimension is never gated.

**Tech Stack:** net10.0, BenchmarkDotNet 0.15.8, xunit + FluentAssertions, System.Text.Json
source generation, Microsoft.ML.Tokenizers (tiktoken), Microsoft.Data.Sqlite.

**Spec:** [docs/superpowers/specs/2026-09-12-benchmark-project-design.md](../specs/2026-09-12-benchmark-project-design.md)

## Global Constraints

- **Target framework:** `net10.0`; `LangVersion` 14; `ImplicitUsings` and `Nullable` both enabled.
  All inherited from `Directory.Build.props` — do not restate them in the new csproj files.
- **`TreatWarningsAsErrors` is `true`** with `AnalysisLevel=latest-all`. Every analyzer warning is
  a build error. Resolve them properly; reach for a suppression only where this plan says to, and
  always with a `Justification`.
- **All package versions live in `Directory.Packages.props`.** The new `.csproj` files carry
  `<PackageReference Include="..."/>` with no `Version` attribute.
- **BenchmarkDotNet version:** `0.15.8` exactly.
- **Code style:** file-scoped namespaces; `var` preferred; private fields `_camelCase`; async
  methods suffixed `Async`; interfaces `IPascalCase`.
- **Formatting:** LF line endings, no trailing whitespace, no BOM, 4-space indent for `.cs`,
  2-space for XML/JSON/YAML. Run `dtk dotnet format DotnetTokenKiller.slnx --no-restore` before
  every commit.
- **Use `dtk`, not raw `dotnet`**, for build/test/format/restore. A `PreToolUse` hook rewrites
  these anyway.
- **Pinned filter root:** every construction of a filter in Corpus or the benchmarks MUST pass the
  literal root `/repo`. Five of the six filters resolve `rootPath ?? Environment.CurrentDirectory`
  (`DotnetBuildFilter.cs:14`), so an unpinned filter produces working-directory-dependent output
  and the exact savings baseline could never be stable across machines or CI runners.
- **Hermetic state:** anything touching config, the tracking database, or tee logs must set
  `DTK_CONFIG_PATH`, `DTK_DB_PATH` and `DTK_TEE_DIR` into a per-run temp directory. Never let a
  benchmark write to the developer's real home-directory state.
- **Single-test runs:** prefer `dtk dotnet test <project>.csproj --filter "..."` over passing the
  `.slnx`. Per project history, `dtk dotnet test <slnx> --filter` can falsely report
  "0 tests found" on installed dtk builds. If you see that, re-run against the `.csproj`.

---

### Task 1: Scaffold the runner and prove BenchmarkDotNet works on net10.0

BenchmarkDotNet's default toolchain generates and compiles a throwaway project targeting the
benchmark project's framework. A release predating a given TFM cannot generate a valid project for
it, and that failure appears only at run time — never at build time. So this is verified with one
trivial benchmark before any real benchmark code exists. Everything else in this plan either does
not depend on BenchmarkDotNet at all (Tasks 2-6, 9) or builds on a proven toolchain (Tasks 7-8).

**Files:**
- Modify: `Directory.Packages.props`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/ToolchainSmokeBenchmark.cs`
- Modify: `DotnetTokenKiller.slnx`
- Modify: `.editorconfig`

**Interfaces:**
- Consumes: nothing.
- Produces: the `DotnetTokenKiller.Benchmarks` executable and its verb dispatch. Later tasks add
  benchmark classes (auto-discovered by `BenchmarkSwitcher`) and two verbs.

- [ ] **Step 1: Add the BenchmarkDotNet package version**

In `Directory.Packages.props`, add to the existing `<ItemGroup>`:

```xml
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.8"/>
```

- [ ] **Step 2: Create the benchmark project file**

`benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>DotnetTokenKiller.Benchmarks</RootNamespace>
    <IsPackable>false</IsPackable>
    <!--
      BenchmarkDotNet requires public benchmark classes and public benchmark methods, none of
      which document anything a reader needs. Requiring XML comments on all of them would add
      noise without information, so CS1591 is switched off here only. The Benchmarks.Corpus
      library keeps documentation on, because it has a real public API that other projects call.
    -->
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet"/>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the verb dispatch entry point**

`benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`. The three entry points stay separable from
the start so later tasks only fill in branches:

```csharp
using BenchmarkDotNet.Running;

namespace DotnetTokenKiller.Benchmarks;

internal static class Program
{
    private const string Usage = """
        Usage:
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks
              Runs the BenchmarkDotNet suite (add -- --filter '*Filter*' to narrow).
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
              Times the published dtk binary end to end, out of process.
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
              Regenerates the committed savings baseline.
        """;

    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case ["cold-start", ..]:
                Console.Error.WriteLine("cold-start is not implemented yet.");
                return 1;

            case ["update-baseline", ..]:
                Console.Error.WriteLine("update-baseline is not implemented yet.");
                return 1;

            default:
                // Anything else, including no arguments, belongs to BenchmarkDotNet: it owns
                // --filter, --list, --job and the rest of its own command line.
                if (args is [var first, ..] && !first.StartsWith('-'))
                {
                    Console.Error.WriteLine($"Unknown verb '{first}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
                }

                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
                await Task.CompletedTask.ConfigureAwait(false);
                return 0;
        }
    }
}
```

- [ ] **Step 4: Write the smoke benchmark**

`benchmarks/DotnetTokenKiller.Benchmarks/ToolchainSmokeBenchmark.cs`. Deliberately trivial — it
exists to exercise the toolchain, not to measure anything. It is deleted in Task 7.

```csharp
using BenchmarkDotNet.Attributes;

namespace DotnetTokenKiller.Benchmarks;

[MemoryDiagnoser]
public class ToolchainSmokeBenchmark
{
    [Params(16, 256)]
    public int Length { get; set; }

    [Benchmark]
    public string AllocateString() => new('x', Length);
}
```

- [ ] **Step 5: Register the project in the solution**

In `DotnetTokenKiller.slnx`, add a new folder after the `/src/` folder block:

```xml
  <Folder Name="/benchmarks/">
    <Project Path="benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj"/>
  </Folder>
```

- [ ] **Step 6: Add the scoped analyzer relaxation**

Append to `.editorconfig`. The scope is the runner only — Benchmarks.Corpus has no reason to need
this:

```ini
# BenchmarkDotNet requires benchmark methods to be instance methods so it can bind them to a
# per-instance [Params] / [GlobalSetup] lifecycle. Methods that happen not to read instance state
# still cannot be made static, so the "could be static" rules are off for the runner only.
[benchmarks/DotnetTokenKiller.Benchmarks/**/*.cs]
dotnet_diagnostic.CA1822.severity = none
dotnet_diagnostic.S2325.severity = none
```

- [ ] **Step 7: Verify it builds clean under TreatWarningsAsErrors**

Run: `dtk dotnet build DotnetTokenKiller.slnx`

Expected: build succeeds, 0 warnings. If analyzers flag the new files, fix them rather than
widening the `.editorconfig` block.

- [ ] **Step 8: Verify the BenchmarkDotNet toolchain actually runs on net10.0**

Run:

```bash
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*ToolchainSmoke*'
```

Expected: BenchmarkDotNet builds its generated project, runs both `Length` cases, and prints a
results table with `Mean` and `Allocated` columns.

**If it fails to generate or build its throwaway project**, that is the risk this task exists to
find. Apply the fallback: add `[SimpleJob(RuntimeMoniker.Net80)]` to the benchmark class (with
`using BenchmarkDotNet.Jobs;`), or configure an in-process toolchain with
`ManualConfig.CreateEmpty().AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance))`.
Record which fallback was needed as a comment in the csproj. Do not proceed to Step 9 until a
results table appears.

- [ ] **Step 9: Confirm artifacts are gitignored**

Run: `git status --short`

Expected: no `BenchmarkDotNet.Artifacts/` entries appear (`.gitignore:88` already covers it). If
any show up, do not add new ignore rules until you have checked why the existing one missed.

- [ ] **Step 10: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add Directory.Packages.props DotnetTokenKiller.slnx .editorconfig benchmarks/
git commit -m "build: scaffold the benchmark runner

Adds BenchmarkDotNet 0.15.8 and a runner project with one trivial
benchmark, to establish that its generated-project toolchain works on
net10.0 before any real benchmark code depends on it. That failure mode
only surfaces at run time, so it is worth isolating first."
```

---

### Task 2: The corpus library and its fixture loader

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/DotnetTokenKiller.Benchmarks.Corpus.csproj`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/FixtureCorpus.cs`
- Modify: `DotnetTokenKiller.slnx`
- Modify: `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`
- Test: `tests/DotnetTokenKiller.Application.Tests/Benchmarks/FixtureCorpusTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `static ImmutableArray<string> FixtureCorpus.Names` — fixture file names, ordinal-sorted.
  - `static string FixtureCorpus.Load(string name)` — fixture text; throws
    `InvalidOperationException` naming the known fixtures when `name` is not found.

- [ ] **Step 1: Create the corpus project file**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/DotnetTokenKiller.Benchmarks.Corpus.csproj`. The
fixtures are already `EmbeddedResource` items in Application.Tests; this embeds the same files from
their original location so exactly one copy exists in git:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DotnetTokenKiller.Benchmarks.Corpus</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <!--
      Embedded from the test project's directory rather than copied: these are the real captured
      dotnet outputs the filter tests already assert against, and a second copy in git would be
      free to drift from the first. LinkBase puts them under a Fixtures.* resource prefix here.
    -->
    <EmbeddedResource Include="../../tests/DotnetTokenKiller.Application.Tests/Fixtures/*.txt"
                      LinkBase="Fixtures"/>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj"/>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Register it in the solution and wire the project references**

In `DotnetTokenKiller.slnx`, add to the `/benchmarks/` folder created in Task 1, before the runner
entry:

```xml
    <Project Path="benchmarks/DotnetTokenKiller.Benchmarks.Corpus/DotnetTokenKiller.Benchmarks.Corpus.csproj"/>
```

In `tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj`, add to
the existing `ProjectReference` item group:

```xml
    <ProjectReference Include="../../benchmarks/DotnetTokenKiller.Benchmarks.Corpus/DotnetTokenKiller.Benchmarks.Corpus.csproj"/>
```

In `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`, add a new
`ProjectReference` item group:

```xml
  <ItemGroup>
    <ProjectReference Include="../DotnetTokenKiller.Benchmarks.Corpus/DotnetTokenKiller.Benchmarks.Corpus.csproj"/>
  </ItemGroup>
```

- [ ] **Step 3: Write the failing test**

`tests/DotnetTokenKiller.Application.Tests/Benchmarks/FixtureCorpusTests.cs`:

```csharp
using DotnetTokenKiller.Benchmarks.Corpus;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class FixtureCorpusTests
{
    [Fact]
    public void Names_ExposesEveryEmbeddedFixture()
    {
        // Bound to a count, not a subset: a glob that silently stopped matching would otherwise
        // leave the corpus quietly smaller than the fixtures directory it mirrors.
        FixtureCorpus.Names.Should().HaveCount(16);
        FixtureCorpus.Names.Should().Contain("dotnet_build_errors.txt");
        FixtureCorpus.Names.Should().Contain("dotnet_list_package_raw.txt");
    }

    [Fact]
    public void EveryFixture_LoadsNonEmptyText()
    {
        foreach (var name in FixtureCorpus.Names)
        {
            FixtureCorpus.Load(name).Should().NotBeNullOrWhiteSpace("fixture {0} must load", name);
        }
    }

    [Fact]
    public void Load_UnknownFixture_ThrowsListingKnownNames()
    {
        var act = () => FixtureCorpus.Load("does_not_exist.txt");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does_not_exist.txt*dotnet_build_errors.txt*");
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~FixtureCorpusTests"
```

Expected: compile error — `FixtureCorpus` does not exist.

- [ ] **Step 5: Implement the fixture loader**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/FixtureCorpus.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;

namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>
/// Reads the real captured <c>dotnet</c> outputs that the Application filter tests assert against.
/// These drive the savings baseline: the published savings percentages have to come from output
/// dotnet actually produced, not from generated approximations of it.
/// </summary>
public static class FixtureCorpus
{
    private const string ResourceMarker = ".Fixtures.";

    private static readonly Assembly OwningAssembly = typeof(FixtureCorpus).Assembly;

    private static readonly ImmutableDictionary<string, string> ResourcesByName =
        OwningAssembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
            .ToImmutableDictionary(
                name => name[(name.IndexOf(ResourceMarker, StringComparison.Ordinal)
                              + ResourceMarker.Length)..],
                name => name,
                StringComparer.Ordinal);

    /// <summary>Every embedded fixture's file name, ordinal-sorted for stable iteration.</summary>
    public static ImmutableArray<string> Names { get; } =
        [.. ResourcesByName.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Returns the full text of the named fixture.</summary>
    /// <param name="name">The fixture file name, e.g. <c>dotnet_build_errors.txt</c>.</param>
    /// <exception cref="InvalidOperationException">No fixture with that name is embedded.</exception>
    public static string Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ResourcesByName.TryGetValue(name, out var resource))
        {
            throw new InvalidOperationException(
                $"No embedded fixture named '{name}'. Known fixtures: {string.Join(", ", Names)}.");
        }

        using var stream = OwningAssembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException(
                               $"Embedded fixture '{resource}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~FixtureCorpusTests"
```

Expected: 3 tests pass.

- [ ] **Step 7: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add DotnetTokenKiller.slnx benchmarks/ tests/
git commit -m "feat: add the benchmark corpus library and fixture loader

Embeds the 16 captured dotnet outputs from the Application test project
rather than copying them, so the savings baseline is computed from the
same bytes the filter tests assert against and the two cannot drift.

The library references only Application, which is what keeps
Application.Tests' one-to-one layering intact when it picks this up."
```

---

### Task 3: The seeded log generator

The existing fixtures run 289 B to 7.7 KB. Real build output on a large solution is 100 KB to
several MB, and every filter runs regexes per line across the whole log — an accidental quadratic
is invisible at 2 KB and crippling at 1 MB. This generates that range deterministically instead of
committing megabytes of captured logs.

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/CorpusTier.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/LogCorpusGenerator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Benchmarks/LogCorpusGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing — the generator is independent of `FixtureCorpus`.
- Produces:
  - `enum CorpusTier { Small, Medium, Large }`
  - `static int LogCorpusGenerator.TargetBytes(CorpusTier tier)`
  - `static string LogCorpusGenerator.Generate(string filterKey, CorpusTier tier)` — `filterKey` is
    a `DotnetTokenKiller.Domain.Filters.FilterKeys` constant.

- [ ] **Step 1: Write the failing test**

`tests/DotnetTokenKiller.Application.Tests/Benchmarks/LogCorpusGeneratorTests.cs`:

```csharp
using System.Text;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class LogCorpusGeneratorTests
{
    public static TheoryData<string> FilterKeysUnderTest() => new(
        FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore,
        FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage);

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_IsDeterministicForAGivenSeed(string filterKey)
    {
        var first = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);
        var second = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);

        // Byte-identical, not merely equal in length: a benchmark whose input shifts between runs
        // reports noise as change, and a savings number computed from it means nothing.
        second.Should().Be(first);
    }

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_LandsWithinTenPercentOfTheTierTarget(string filterKey)
    {
        foreach (var tier in Enum.GetValues<CorpusTier>())
        {
            var target = LogCorpusGenerator.TargetBytes(tier);
            var actual = Encoding.UTF8.GetByteCount(LogCorpusGenerator.Generate(filterKey, tier));

            actual.Should().BeInRange((int)(target * 0.9), (int)(target * 1.1),
                "{0}/{1} should be near its {2}-byte target", filterKey, tier, target);
        }
    }

    [Theory]
    [MemberData(nameof(FilterKeysUnderTest))]
    public void Generate_ProducesOutputItsFilterMeaningfullyCondenses(string filterKey)
    {
        var raw = LogCorpusGenerator.Generate(filterKey, CorpusTier.Medium);

        var filtered = FilterFor(filterKey).Apply(raw, exitCode: 0);

        // The load-bearing assertion of this task. A generator that drifted into line shapes no
        // filter recognises would still be deterministic and still hit its size target, while
        // making every throughput number meaningless. Requiring real compression pins the
        // generated logs to shapes the filters actually parse.
        filtered.Should().NotBeNullOrWhiteSpace();
        Encoding.UTF8.GetByteCount(filtered).Should().BeLessThan(
            Encoding.UTF8.GetByteCount(raw) / 2,
            "{0} should condense its generated log by at least half", filterKey);
    }

    private static IOutputFilter FilterFor(string filterKey) => filterKey switch
    {
        FilterKeys.Build => new DotnetBuildFilter("/repo"),
        FilterKeys.Test => new DotnetTestFilter("/repo"),
        FilterKeys.Restore => new DotnetRestoreFilter("/repo"),
        FilterKeys.Clean => new DotnetCleanFilter("/repo"),
        FilterKeys.Format => new DotnetFormatFilter("/repo"),
        FilterKeys.ListPackage => new DotnetListPackageFilter(),
        _ => throw new ArgumentOutOfRangeException(nameof(filterKey), filterKey, "No filter."),
    };
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~LogCorpusGeneratorTests"
```

Expected: compile error — `LogCorpusGenerator` and `CorpusTier` do not exist.

- [ ] **Step 3: Define the tier enum**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/CorpusTier.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>Generated-log size tiers. Three points are the minimum that can show a curve.</summary>
public enum CorpusTier
{
    /// <summary>About 2 KB — the size of today's fixtures, and of an interactive single-project run.</summary>
    Small = 0,

    /// <summary>About 50 KB — a realistic multi-project solution build.</summary>
    Medium = 1,

    /// <summary>About 1 MB — the scaling probe, where non-linear behaviour becomes visible.</summary>
    Large = 2
}
```

- [ ] **Step 4: Implement the generator**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/LogCorpusGenerator.cs`. Every line template is a
real shape taken from the captured fixtures, so the filters recognise them. Read the matching
fixture in `tests/DotnetTokenKiller.Application.Tests/Fixtures/` alongside this and correct any
template that does not match what its filter parses:

```csharp
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>
/// Produces deterministic, realistically shaped <c>dotnet</c> output at three size tiers.
/// </summary>
/// <remarks>
/// The captured fixtures are real but small — none exceeds 8 KB, while a large solution's build
/// output runs to megabytes. Every filter applies regexes per line across the whole log, so a
/// quadratic cannot be observed at fixture scale. Generating the large tier rather than committing
/// a captured multi-megabyte log keeps the repository small and lets the tier be re-scaled later.
/// </remarks>
public static class LogCorpusGenerator
{
    /// <summary>
    /// Fixed so that a tier is byte-identical on every machine and every run. A benchmark whose
    /// input varies between runs reports its own noise as a change.
    /// </summary>
    private const int Seed = 20260912;

    private const string Root = "/repo";

    private static readonly FrozenDictionary<string, string[]> Templates =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [FilterKeys.Build] =
            [
                "  Determining projects to restore...",
                "  All projects are up-to-date for restore.",
                Root + "/src/Project{0}/Service{1}.cs({2},{3}): warning CA1822: Member 'Handle{1}' does not access instance data and can be marked as static [" + Root + "/src/Project{0}/Project{0}.csproj]",
                Root + "/src/Project{0}/Model{1}.cs({2},{3}): warning CS8618: Non-nullable property 'Name{1}' must contain a non-null value when exiting constructor [" + Root + "/src/Project{0}/Project{0}.csproj]",
                Root + "/src/Project{0}/Repository{1}.cs({2},{3}): warning S1481: Remove the unused local variable 'temp{1}' [" + Root + "/src/Project{0}/Project{0}.csproj]",
                "    Project{0} -> " + Root + "/src/Project{0}/bin/Release/net10.0/Project{0}.dll",
            ],
            [FilterKeys.Test] =
            [
                "  Determining projects to restore...",
                "    Project{0}.Tests -> " + Root + "/tests/Project{0}.Tests/bin/Release/net10.0/Project{0}.Tests.dll",
                "  Passed Project{0}.Tests.Service{1}Tests.Returns_TheExpectedValue [{2} ms]",
                "  Passed Project{0}.Tests.Service{1}Tests.Throws_WhenInputIsNull [{2} ms]",
                "  Skipped Project{0}.Tests.Service{1}Tests.Handles_TheLegacyShape",
            ],
            [FilterKeys.Restore] =
            [
                "  Determining projects to restore...",
                "  Restored " + Root + "/src/Project{0}/Project{0}.csproj (in {2} ms).",
                "  All projects are up-to-date for restore.",
            ],
            [FilterKeys.Clean] =
            [
                "  Determining projects to restore...",
                "  Project{0} -> " + Root + "/src/Project{0}/bin/Release/net10.0/Project{0}.dll",
                "  Cleaning " + Root + "/src/Project{0}/obj/Release/net10.0/Project{0}.assets.cache",
            ],
            [FilterKeys.Format] =
            [
                "  Loading workspace " + Root + "/DotnetTokenKiller.slnx.",
                Root + "/src/Project{0}/Service{1}.cs({2},{3}): info WHITESPACE: Fix whitespace formatting.",
                "  Formatted code file 'src/Project{0}/Service{1}.cs'.",
            ],
            [FilterKeys.ListPackage] =
            [
                "Project 'Project{0}' has the following package references",
                "   [net10.0]: ",
                "   Top-level Package                        Requested   Resolved",
                "   > Package.Number{1}                      1.{2}.0     1.{2}.0",
            ],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns the approximate byte size the given tier aims for.</summary>
    /// <param name="tier">The tier to size.</param>
    public static int TargetBytes(CorpusTier tier) => tier switch
    {
        CorpusTier.Small => 2 * 1024,
        CorpusTier.Medium => 50 * 1024,
        CorpusTier.Large => 1024 * 1024,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };

    /// <summary>Generates a log of roughly <see cref="TargetBytes"/> bytes for the given filter.</summary>
    /// <param name="filterKey">A <see cref="FilterKeys"/> constant naming the output shape.</param>
    /// <param name="tier">The size tier to generate.</param>
    /// <exception cref="ArgumentOutOfRangeException">The filter key has no line templates.</exception>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Reproducibility is the requirement. A seeded Random makes each tier "
                        + "byte-identical across machines; a cryptographic source would make "
                        + "every benchmark run measure a different input.")]
    [SuppressMessage("Major Code Smell", "S2245:Using pseudorandom number generators is security-sensitive",
        Justification = "See CA5394: the seeded sequence is the point, and no security decision "
                        + "depends on it.")]
    public static string Generate(string filterKey, CorpusTier tier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterKey);

        if (!Templates.TryGetValue(filterKey, out var templates))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filterKey), filterKey, "No line templates for this filter key.");
        }

        var target = TargetBytes(tier);
        var random = new Random(Seed);
        var builder = new StringBuilder(target + 512);
        var index = 0;

        while (builder.Length < target)
        {
            var template = templates[index % templates.Length];
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                template,
                random.Next(1, 40),
                random.Next(1, 400),
                random.Next(1, 900),
                random.Next(1, 80)));
            index++;
        }

        builder.Append(Summary(filterKey));
        return builder.ToString();
    }

    /// <summary>
    /// The trailing summary block. Filters read the tail for their verdict, so a generated log
    /// without one is not a shape they would ever be handed in practice.
    /// </summary>
    private static string Summary(string filterKey) => filterKey switch
    {
        FilterKeys.Build =>
            "\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n\nTime Elapsed 00:00:12.3456789\n",
        FilterKeys.Test =>
            "\nPassed!  - Failed:     0, Passed:  1200, Skipped:    40, Total:  1240, Duration: 8 s\n",
        FilterKeys.Restore => "\nRestore succeeded.\n",
        FilterKeys.Clean => "\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
        FilterKeys.Format => "\nFormat complete.\n",
        _ => "\n",
    };
}
```

- [ ] **Step 5: Run the tests and iterate on the templates**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~LogCorpusGeneratorTests"
```

Expected: 18 tests pass.

`Generate_ProducesOutputItsFilterMeaningfullyCondenses` is the one likely to fail first, and it is
doing its job when it does. If a filter fails to condense its generated log by half, the templates
for that key do not match what the filter parses — read the corresponding fixture and the filter's
regexes, then correct the template. **Do not relax the assertion to make it pass.**

Note the two assertions interact: `string.Format` placeholders change line length, so adjusting a
template moves the generated size. Re-run both after any template edit.

- [ ] **Step 6: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/ tests/
git commit -m "feat: add the seeded log corpus generator

The captured fixtures top out at 7.7 KB, and every filter runs regexes
per line across the whole log, so a quadratic is invisible at fixture
scale. This generates realistically shaped output at 2 KB, 50 KB and
1 MB from a fixed seed, giving a scaling curve without committing
megabytes of captured logs.

Tests pin the three properties that make the generator worth having:
byte-identical output per seed, size within 10% of each tier's target,
and output its filter actually condenses by half or better. The last is
the load-bearing one -- templates drifting into shapes no filter parses
would leave every throughput number meaningless with all else green."
```

---

### Task 4: The savings engine and baseline model

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenario.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenarios.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/ScenarioSavings.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaseline.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaselineJsonContext.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsEngine.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaselineFile.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Benchmarks/SavingsEngineTests.cs`

**Interfaces:**
- Consumes: `FixtureCorpus.Load` (Task 2).
- Produces:
  - `record SavingsScenario(string Id, string Fixture, string FilterKey, int ExitCode)`
  - `static ImmutableArray<SavingsScenario> SavingsScenarios.All` — 16 entries
  - `const string SavingsScenarios.PinnedRoot` — `"/repo"`
  - `static IOutputFilter SavingsScenarios.FilterFor(string filterKey)` — pinned to `PinnedRoot`
  - `record ScenarioSavings(string Id, string Fixture, string FilterKey, int ExitCode, int RawBytes, int RawTokens, int FilteredTokens, int SavedTokens, double SavingsPercent)`
  - `record SavingsBaseline(string Tokenizer, int TokenizerFingerprint, IReadOnlyList<ScenarioSavings> Scenarios)`
  - `static ScenarioSavings SavingsEngine.Measure(SavingsScenario scenario)`
  - `static SavingsBaseline SavingsEngine.MeasureAll()`
  - `static int SavingsEngine.TokenizerFingerprint()`, `const string SavingsEngine.TokenizerName`
  - `SavingsBaselineFile.Path()`, `.Read()`, `.Write(SavingsBaseline)`, `.Serialize(SavingsBaseline)`,
    `.Deserialize(string)`, `const string RegenerateCommand`

**Spec deviation to apply here:** the spec specifies a `tokenizerDataVersion` string read from the
`Microsoft.ML.Tokenizers.Data.Cl100kBase` package. Implement `tokenizerFingerprint` instead — the
token count of a fixed probe string. It measures the thing actually cared about (did the
vocabulary change) rather than a proxy for it, needs no assembly introspection of a package that
ships data rather than types, and cannot report "unchanged" when a repackaged version alters
tokenization. Same purpose, strictly more reliable.

- [ ] **Step 1: Write the failing test**

`tests/DotnetTokenKiller.Application.Tests/Benchmarks/SavingsEngineTests.cs`:

```csharp
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class SavingsEngineTests
{
    [Fact]
    public void All_CoversEveryFixtureExactlyOnce()
    {
        // Every captured fixture is a scenario. Without this, adding a fixture would silently
        // leave it outside the gate.
        SavingsScenarios.All.Select(s => s.Fixture).Should().BeEquivalentTo(FixtureCorpus.Names);
        SavingsScenarios.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Measure_IsDeterministic()
    {
        foreach (var scenario in SavingsScenarios.All)
        {
            SavingsEngine.Measure(scenario).Should().Be(SavingsEngine.Measure(scenario));
        }
    }

    [Fact]
    public void Measure_DoesNotDependOnTheWorkingDirectory()
    {
        // Five of the six filters resolve `rootPath ?? Environment.CurrentDirectory`, so an
        // unpinned filter shortens diagnostic paths differently per machine and the exact
        // baseline could never hold. This is the assertion that pins the pinning.
        var scenario = SavingsScenarios.All.First(s => s.Fixture == "dotnet_build_errors.txt");
        var original = Environment.CurrentDirectory;
        var elsewhere = Directory.CreateTempSubdirectory("dtk-savings-cwd");

        try
        {
            var before = SavingsEngine.Measure(scenario);
            Environment.CurrentDirectory = elsewhere.FullName;
            var after = SavingsEngine.Measure(scenario);

            after.Should().Be(before);
        }
        finally
        {
            Environment.CurrentDirectory = original;
            elsewhere.Delete(recursive: true);
        }
    }

    [Fact]
    public void Measure_ReportsRealCompression()
    {
        var scenario = SavingsScenarios.All.First(s => s.Fixture == "dotnet_build_errors.txt");

        var savings = SavingsEngine.Measure(scenario);

        savings.RawTokens.Should().BeGreaterThan(0);
        savings.FilteredTokens.Should().BeGreaterThan(0).And.BeLessThan(savings.RawTokens);
        savings.SavedTokens.Should().Be(savings.RawTokens - savings.FilteredTokens);
        savings.SavingsPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var baseline = SavingsEngine.MeasureAll();

        var restored = SavingsBaselineFile.Deserialize(SavingsBaselineFile.Serialize(baseline));

        restored.Should().BeEquivalentTo(baseline);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~SavingsEngineTests"
```

Expected: compile error — the `Savings` namespace does not exist.

- [ ] **Step 3: Define the scenario types**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenario.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>One measurable case: a captured fixture, the filter that would receive it, and the
/// exit code the producing command returned.</summary>
/// <param name="Id">Stable identifier used in the baseline and in drift reports.</param>
/// <param name="Fixture">The fixture file name, as embedded by <see cref="FixtureCorpus"/>.</param>
/// <param name="FilterKey">A <see cref="Domain.Filters.FilterKeys"/> constant.</param>
/// <param name="ExitCode">
/// The producing command's exit code. Every filter treats this as the sole source of the
/// success/failure verdict, so it changes the output and therefore the savings.
/// </param>
public sealed record SavingsScenario(string Id, string Fixture, string FilterKey, int ExitCode);
```

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsScenarios.cs`. The exit codes are
not invented — each is the code that fixture is already asserted with in the filter tests:

```csharp
using System.Collections.Immutable;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>The fixed set of savings scenarios, one per captured fixture.</summary>
public static class SavingsScenarios
{
    /// <summary>
    /// The root every filter is pinned to. Five of the six resolve
    /// <c>rootPath ?? Environment.CurrentDirectory</c> and use it to shorten diagnostic paths, so
    /// leaving it unpinned would make the filtered output — and every token count derived from it —
    /// depend on where the process happened to run. This is the same literal
    /// <c>ExamplesBindingTests</c> pins the documented examples to.
    /// </summary>
    public const string PinnedRoot = "/repo";

    /// <summary>Every scenario. One per fixture; the exit code is the one that fixture's own
    /// filter tests exercise it with.</summary>
    public static ImmutableArray<SavingsScenario> All { get; } =
    [
        new("build/errors", "dotnet_build_errors.txt", FilterKeys.Build, 1),
        new("build/success", "dotnet_build_success.txt", FilterKeys.Build, 0),
        new("build/warnings", "dotnet_build_warnings.txt", FilterKeys.Build, 0),
        new("clean/raw", "dotnet_clean_raw.txt", FilterKeys.Clean, 0),
        new("format/files", "dotnet_format_files_raw.txt", FilterKeys.Format, 0),
        new("format/verbose-nothing", "dotnet_format_verbose_nothing_raw.txt", FilterKeys.Format, 0),
        new("format/violations", "dotnet_format_violations_raw.txt", FilterKeys.Format, 1),
        new("list-package/all", "dotnet_list_package_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/deprecated", "dotnet_list_package_deprecated_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/outdated", "dotnet_list_package_outdated_raw.txt", FilterKeys.ListPackage, 0),
        new("list-package/vulnerable", "dotnet_list_package_vulnerable_raw.txt", FilterKeys.ListPackage, 0),
        new("restore/raw", "dotnet_restore_raw.txt", FilterKeys.Restore, 0),
        new("test/all-pass", "dotnet_test_all_pass.txt", FilterKeys.Test, 0),
        new("test/failures", "dotnet_test_failures.txt", FilterKeys.Test, 1),
        new("test/multiproject-partial", "dotnet_test_multiproject_partial_match.txt", FilterKeys.Test, 0),
        new("test/zero", "dotnet_test_zero.txt", FilterKeys.Test, 0),
    ];

    /// <summary>Builds the filter for a key, pinned to <see cref="PinnedRoot"/>.</summary>
    /// <param name="filterKey">A <see cref="FilterKeys"/> constant.</param>
    /// <exception cref="ArgumentOutOfRangeException">The key names no filter.</exception>
    public static IOutputFilter FilterFor(string filterKey) => filterKey switch
    {
        FilterKeys.Build => new DotnetBuildFilter(PinnedRoot),
        FilterKeys.Test => new DotnetTestFilter(PinnedRoot),
        FilterKeys.Restore => new DotnetRestoreFilter(PinnedRoot),
        FilterKeys.Clean => new DotnetCleanFilter(PinnedRoot),
        FilterKeys.Format => new DotnetFormatFilter(PinnedRoot),

        // Takes no root: it reports package names, not file paths.
        FilterKeys.ListPackage => new DotnetListPackageFilter(),
        _ => throw new ArgumentOutOfRangeException(nameof(filterKey), filterKey, "No such filter."),
    };
}
```

- [ ] **Step 4: Define the result and baseline records**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/ScenarioSavings.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>What one scenario costs before and after filtering.</summary>
/// <param name="Id">The scenario's stable identifier.</param>
/// <param name="Fixture">The fixture measured.</param>
/// <param name="FilterKey">The filter applied.</param>
/// <param name="ExitCode">The exit code the filter was given.</param>
/// <param name="RawBytes">UTF-8 byte count of the ANSI-stripped raw output.</param>
/// <param name="RawTokens">Token count of the ANSI-stripped raw output.</param>
/// <param name="FilteredTokens">Token count of the filtered output.</param>
/// <param name="SavedTokens">
/// <paramref name="RawTokens"/> minus <paramref name="FilteredTokens"/>. Stored rather than
/// computed so the committed baseline reads as a report, not as a puzzle.
/// </param>
/// <param name="SavingsPercent">Percentage saved, rounded to one decimal place.</param>
public sealed record ScenarioSavings(
    string Id,
    string Fixture,
    string FilterKey,
    int ExitCode,
    int RawBytes,
    int RawTokens,
    int FilteredTokens,
    int SavedTokens,
    double SavingsPercent);
```

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaseline.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>The committed record of what every scenario saves.</summary>
/// <param name="Tokenizer">The tiktoken encoding the counts were produced with.</param>
/// <param name="TokenizerFingerprint">
/// The token count of a fixed probe string. When the tokenizer's vocabulary data changes, every
/// scenario's counts shift at once; this distinguishes that from a filter regression, which is
/// otherwise indistinguishable in the diff.
/// </param>
/// <param name="Scenarios">Per-scenario measurements, ordered by <see cref="ScenarioSavings.Id"/>.</param>
public sealed record SavingsBaseline(
    string Tokenizer,
    int TokenizerFingerprint,
    IReadOnlyList<ScenarioSavings> Scenarios);
```

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaselineJsonContext.cs`, mirroring
the existing `DtkConfigJsonContext` and `GainSummaryJsonContext` pattern:

```csharp
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavingsBaseline))]
internal sealed partial class SavingsBaselineJsonContext : JsonSerializerContext;
```

- [ ] **Step 5: Implement the engine**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsEngine.cs`. The order of operations
mirrors `FilteredOutputPipeline.ProcessAsync` — strip, then filter, then count both sides — so the
measured savings are the savings users actually get:

```csharp
using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>Measures what filtering removes, for every scenario.</summary>
public static class SavingsEngine
{
    /// <summary>
    /// The encoding the gate is expressed in: what <see cref="TrackingConfig"/> ships as its
    /// default, so the baseline describes what users actually get. o200k is covered on the timing
    /// side instead.
    /// </summary>
    public const TokenizerModel Tokenizer = TokenizerModel.Cl100kBase;

    /// <summary>Name recorded in the baseline for <see cref="Tokenizer"/>.</summary>
    public const string TokenizerName = "cl100k_base";

    /// <summary>
    /// Fixed text whose token count fingerprints the tokenizer's vocabulary. Deliberately varied —
    /// prose, punctuation, digits, a path, a non-ASCII glyph — so a vocabulary change is likely to
    /// move the count.
    /// </summary>
    private const string FingerprintProbe =
        "dtk: build succeeded — 3 warnings, 0 errors (net10.0) ✓\n"
        + "/repo/src/Project/Service.cs(42,17): warning CA1822: Member 'Handle' …\n"
        + "Passed!  - Failed: 0, Passed: 1200, Skipped: 40, Total: 1240, Duration: 8 s\n";

    /// <summary>Returns the current tokenizer fingerprint.</summary>
    public static int TokenizerFingerprint() => TokenEstimator.Estimate(FingerprintProbe, Tokenizer);

    /// <summary>Measures one scenario.</summary>
    /// <param name="scenario">The scenario to measure.</param>
    public static ScenarioSavings Measure(SavingsScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        // Same order as FilteredOutputPipeline.ProcessAsync: strip first, filter the stripped
        // text, and count the stripped text as the "before". Counting the un-stripped raw output
        // instead would credit dtk for removing escape sequences no model ever sees.
        var stripped = AnsiStrip.Strip(FixtureCorpus.Load(scenario.Fixture));
        var filtered = SavingsScenarios.FilterFor(scenario.FilterKey)
            .Apply(stripped, scenario.ExitCode);

        var rawTokens = TokenEstimator.Estimate(stripped, Tokenizer);
        var filteredTokens = TokenEstimator.Estimate(filtered, Tokenizer);
        var saved = rawTokens - filteredTokens;

        return new ScenarioSavings(
            scenario.Id,
            scenario.Fixture,
            scenario.FilterKey,
            scenario.ExitCode,
            Encoding.UTF8.GetByteCount(stripped),
            rawTokens,
            filteredTokens,
            saved,
            rawTokens > 0 ? Math.Round((double)saved / rawTokens * 100.0, 1) : 0.0);
    }

    /// <summary>Measures every scenario, ordered by identifier for a stable baseline diff.</summary>
    public static SavingsBaseline MeasureAll() => new(
        TokenizerName,
        TokenizerFingerprint(),
        [.. SavingsScenarios.All
            .OrderBy(scenario => scenario.Id, StringComparer.Ordinal)
            .Select(Measure)]);
}
```

- [ ] **Step 6: Implement baseline file access**

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Savings/SavingsBaselineFile.cs`:

```csharp
using System.Text.Json;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>Reads and writes the committed savings baseline.</summary>
public static class SavingsBaselineFile
{
    /// <summary>The command that regenerates the baseline, quoted in drift reports.</summary>
    public const string RegenerateCommand =
        "dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline";

    private const string RelativePath =
        "benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json";

    /// <summary>Resolves the baseline's absolute path by walking up to the repository root.</summary>
    /// <remarks>
    /// The same walk <c>ExamplesBindingTests.ExamplesPath</c> uses. The file is read from the
    /// working tree rather than from an embedded copy, so a regenerated baseline is reviewable as a
    /// diff and the reader and the writer cannot disagree about which copy is authoritative.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The repository root was not found.</exception>
    public static string Path()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return System.IO.Path.Combine(
                    dir.FullName, System.IO.Path.Combine(RelativePath.Split('/')));
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>Serializes a baseline to its committed JSON form.</summary>
    /// <param name="baseline">The baseline to serialize.</param>
    public static string Serialize(SavingsBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return JsonSerializer.Serialize(baseline, SavingsBaselineJsonContext.Default.SavingsBaseline);
    }

    /// <summary>Deserializes a baseline from its committed JSON form.</summary>
    /// <param name="json">The JSON to read.</param>
    /// <exception cref="InvalidOperationException">The JSON did not describe a baseline.</exception>
    public static SavingsBaseline Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, SavingsBaselineJsonContext.Default.SavingsBaseline)
               ?? throw new InvalidOperationException("The savings baseline JSON was null.");
    }

    /// <summary>Reads the committed baseline.</summary>
    /// <exception cref="InvalidOperationException">The baseline file does not exist.</exception>
    public static SavingsBaseline Read()
    {
        var path = Path();

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"No savings baseline at {path}. Generate one with: {RegenerateCommand}");
        }

        return Deserialize(File.ReadAllText(path));
    }

    /// <summary>Writes the baseline, creating its directory if needed.</summary>
    /// <param name="baseline">The baseline to write.</param>
    public static void Write(SavingsBaseline baseline)
    {
        var path = Path();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Trailing newline, LF endings: .gitattributes and .editorconfig require both, and a file
        // written without them fails the formatting check rather than the test.
        File.WriteAllText(path, Serialize(baseline).ReplaceLineEndings("\n") + "\n");
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~SavingsEngineTests"
```

Expected: 5 tests pass.

If `Measure_DoesNotDependOnTheWorkingDirectory` fails, a filter is being constructed without
`PinnedRoot` somewhere — fix the construction, never the assertion.

- [ ] **Step 8: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/ tests/
git commit -m "feat: add the savings engine and baseline model

Measures what filtering removes per scenario, in the same order the real
pipeline does it: strip ANSI, filter the stripped text, count both sides.
Counting the un-stripped output as the 'before' would credit dtk for
removing escape sequences no model ever sees.

Every filter is pinned to the root '/repo'. Five of the six resolve
rootPath ?? Environment.CurrentDirectory to shorten diagnostic paths, so
an unpinned filter makes the output -- and every token count drawn from
it -- depend on where the process ran, and an exact baseline could never
hold across machines. A test moves the working directory to prove it.

Records a tokenizer fingerprint (the token count of a fixed probe) rather
than the package version the spec called for: it detects the thing that
actually matters, a vocabulary change, instead of a proxy that can bump
without one or change without bumping."
```

---

### Task 5: Generate and commit the initial baseline

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/UpdateBaselineCommand.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` (generated)

**Interfaces:**
- Consumes: `SavingsEngine.MeasureAll`, `SavingsBaselineFile.Write`, `SavingsBaselineFile.Path` (Task 4).
- Produces: `static int UpdateBaselineCommand.Run()` — writes the baseline, prints a summary
  table, returns 0.

- [ ] **Step 1: Implement the regeneration verb**

`benchmarks/DotnetTokenKiller.Benchmarks/UpdateBaselineCommand.cs`:

```csharp
using System.Globalization;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;

namespace DotnetTokenKiller.Benchmarks;

internal static class UpdateBaselineCommand
{
    internal static int Run()
    {
        var baseline = SavingsEngine.MeasureAll();
        SavingsBaselineFile.Write(baseline);

        Console.WriteLine($"Wrote {SavingsBaselineFile.Path()}");
        Console.WriteLine(
            $"Tokenizer {baseline.Tokenizer} (fingerprint {baseline.TokenizerFingerprint})");
        Console.WriteLine();
        Console.WriteLine($"{"scenario",-28} {"raw",8} {"filtered",9} {"saved",8}");

        foreach (var scenario in baseline.Scenarios)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario.Id,-28} {scenario.RawTokens,8} {scenario.FilteredTokens,9} {scenario.SavingsPercent,7:F1}%"));
        }

        var totalRaw = baseline.Scenarios.Sum(s => s.RawTokens);
        var totalFiltered = baseline.Scenarios.Sum(s => s.FilteredTokens);
        var overall = totalRaw > 0 ? (double)(totalRaw - totalFiltered) / totalRaw * 100.0 : 0.0;

        Console.WriteLine();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Overall: {totalRaw} -> {totalFiltered} tokens ({overall:F1}% saved)"));

        return 0;
    }
}
```

- [ ] **Step 2: Wire the verb into the entry point**

In `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`, replace the `update-baseline` case:

```csharp
            case ["update-baseline", ..]:
                return UpdateBaselineCommand.Run();
```

- [ ] **Step 3: Build and run the generator**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
```

Expected: a 16-row table, an overall savings line, and a new
`benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json`.

- [ ] **Step 4: Sanity-check the generated baseline before trusting it**

Run:

```bash
git status --short
head -30 benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json
```

Check by eye, because this file becomes the reference every future run is judged against:

- 16 scenario entries.
- `rawTokens` greater than `filteredTokens` in every entry.
- `savingsPercent` plausible per scenario. Large captured logs condensed to a summary should show
  high percentages; `format/files` and `format/violations` are tiny fixtures and may legitimately
  show low or even negative savings, since a short raw output can get longer once a summary line is
  added. A negative number is a real finding to note in the commit message, not a bug to hide.
- Keys are camelCase and the file ends with a newline.

- [ ] **Step 5: Commit the baseline**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/
git commit -m "feat: generate the initial savings baseline

Records what each of the 16 captured scenarios saves today, as the
reference every later run is measured against. Regenerate deliberately
with 'update-baseline' when a filter changes on purpose; the diff is
then the record of how the savings moved."
```

---

### Task 6: The savings gate

**Files:**
- Test: `tests/DotnetTokenKiller.Application.Tests/Benchmarks/SavingsBaselineTests.cs`

**Interfaces:**
- Consumes: `SavingsEngine.MeasureAll`, `SavingsEngine.TokenizerName`, `SavingsBaselineFile.Read`,
  `SavingsBaselineFile.RegenerateCommand` (Task 4); the committed baseline (Task 5).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the gate test**

This one is written against an already-passing baseline, so it will pass on first run. Step 3 then
breaks a filter on purpose to confirm it actually reddens — a gate never observed failing is not
known to work.

`tests/DotnetTokenKiller.Application.Tests/Benchmarks/SavingsBaselineTests.cs`:

```csharp
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

/// <summary>
/// Gates the product metric. <c>ExamplesBindingTests</c> asserts that documented output matches
/// what the filters emit; nothing asserted that the filters still <em>compress</em>. A change that
/// added a line of summary to every filter would cost real tokens on every user's every command
/// and leave this suite entirely green. This is the assertion that notices.
/// </summary>
public sealed class SavingsBaselineTests
{
    [Fact]
    public void EveryScenario_MatchesTheCommittedBaseline()
    {
        var baseline = SavingsBaselineFile.Read();
        var actual = SavingsEngine.MeasureAll();

        actual.Scenarios.Select(s => s.Id).Should().Equal(
            baseline.Scenarios.Select(s => s.Id),
            "the baseline must cover exactly the current scenario set — regenerate with: {0}",
            SavingsBaselineFile.RegenerateCommand);

        var drifted = actual.Scenarios
            .Zip(baseline.Scenarios, (now, then) => (Now: now, Then: then))
            .Where(pair => pair.Now.RawTokens != pair.Then.RawTokens
                           || pair.Now.FilteredTokens != pair.Then.FilteredTokens)
            .ToList();

        drifted.Should().BeEmpty(
            "{0}",
            DriftReport(drifted, baseline.TokenizerFingerprint, actual.TokenizerFingerprint));
    }

    [Fact]
    public void Baseline_RecordsTheTokenizerItWasMeasuredWith()
    {
        var baseline = SavingsBaselineFile.Read();

        baseline.Tokenizer.Should().Be(SavingsEngine.TokenizerName);
    }

    /// <summary>
    /// Explains a drift the way a reviewer needs to read it: which scenarios moved, by how much,
    /// and whether the tokenizer or the filters are the likelier cause. Without the fingerprint
    /// line, a dependency bump that changes the vocabulary data looks exactly like a filter
    /// regression.
    /// </summary>
    private static string DriftReport(
        IReadOnlyCollection<(ScenarioSavings Now, ScenarioSavings Then)> drifted,
        int baselineFingerprint,
        int actualFingerprint)
    {
        if (drifted.Count == 0)
        {
            return "no drift";
        }

        var report = new StringBuilder();
        report.AppendLine("savings drifted from the committed baseline:").AppendLine();

        foreach (var (now, then) in drifted)
        {
            report.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {now.Id,-28} {then.SavingsPercent,6:F1}% -> {now.SavingsPercent,6:F1}% (raw {then.RawTokens}->{now.RawTokens}, filtered {then.FilteredTokens}->{now.FilteredTokens})"));
        }

        report.AppendLine();

        report.AppendLine(actualFingerprint != baselineFingerprint
            ? $"  The tokenizer changed (fingerprint {baselineFingerprint} -> {actualFingerprint}). "
              + "Every count shifts when the vocabulary data does; this is expected after a "
              + "Microsoft.ML.Tokenizers.Data bump, and regenerating is the correct response."
            : $"  The tokenizer is unchanged (fingerprint {actualFingerprint}), so {drifted.Count} "
              + "scenario(s) moved because filtering changed. If that was intended, regenerate. "
              + "If not, this is the regression.");

        report.AppendLine().AppendLine($"  Regenerate with: {SavingsBaselineFile.RegenerateCommand}");
        return report.ToString();
    }
}
```

- [ ] **Step 2: Run the gate and verify it passes**

Run:

```bash
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~SavingsBaselineTests"
```

Expected: 2 tests pass.

- [ ] **Step 3: Prove the gate actually gates**

Temporarily make a filter more verbose. In
`src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs`, append a junk line to what
`Apply` returns — for example `return result + "benchmark gate probe line\n";`.

Run:

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~SavingsBaselineTests"
```

Expected: `EveryScenario_MatchesTheCommittedBaseline` FAILS, showing the `clean/raw` row with a
lower percentage, stating the tokenizer is unchanged, and printing the regenerate command.

Read the failure output and judge whether it is genuinely useful to a reviewer. If it is not,
improve `DriftReport` now — this message is the entire user interface of the gate.

- [ ] **Step 4: Revert the probe**

```bash
git checkout -- src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj --filter "FullyQualifiedName~SavingsBaselineTests"
```

Expected: 2 tests pass. Confirm with `git status --short` that no `src/` change remains.

- [ ] **Step 5: Run the whole suite to check nothing else moved**

Run: `dtk dotnet test DotnetTokenKiller.slnx`

Expected: all tests pass. The CLI integration tests may not run reliably locally — per project
history they cannot pass cold or warm on a developer machine, so let CI gate those and confirm
only that no *new* failures appeared elsewhere.

- [ ] **Step 6: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add tests/
git commit -m "test: gate the savings baseline

ExamplesBindingTests checks that documented output matches what the
filters emit. Nothing checked that the filters still compress, so a
change adding a summary line to every filter would cost tokens on every
user's every command with the suite fully green.

The failure message is the feature: it names the drifted scenarios with
their before and after percentages, and uses the recorded tokenizer
fingerprint to say whether the cause was a vocabulary change or a
filtering change -- otherwise indistinguishable, and a dependency bump
would read as a regression. Verified by breaking a filter on purpose and
reading the output."
```

---

### Task 7: Filter, ANSI-strip and token-estimator benchmarks

**Files:**
- Delete: `benchmarks/DotnetTokenKiller.Benchmarks/ToolchainSmokeBenchmark.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/FilterBenchmarks.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/AnsiStripBenchmarks.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/TokenEstimatorBenchmarks.cs`

**Interfaces:**
- Consumes: `LogCorpusGenerator.Generate`, `CorpusTier` (Task 3); `SavingsScenarios.FilterFor`,
  `SavingsScenarios.PinnedRoot` (Task 4).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Remove the smoke benchmark**

Run: `git rm benchmarks/DotnetTokenKiller.Benchmarks/ToolchainSmokeBenchmark.cs`

It proved the toolchain in Task 1 and measures nothing worth keeping.

- [ ] **Step 2: Write the filter benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/FilterBenchmarks.cs`. `[Params]` are properties, not
fields, so CA1051 and S1104 never fire:

```csharp
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Filtering cost per filter across the three size tiers. Read this as a curve, not as three
/// numbers: a filter whose cost grows faster than its input is the defect worth catching, and only
/// the ratio between tiers shows it.
/// </summary>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    private string _raw = string.Empty;
    private IOutputFilter _filter = new DotnetBuildFilter(SavingsScenarios.PinnedRoot);

    [Params(FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore,
        FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage)]
    public string FilterKey { get; set; } = FilterKeys.Build;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        _raw = LogCorpusGenerator.Generate(FilterKey, Tier);
        _filter = SavingsScenarios.FilterFor(FilterKey);
    }

    [Benchmark]
    public string Apply() => _filter.Apply(_raw, exitCode: 0);
}
```

- [ ] **Step 3: Write the ANSI-strip benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/AnsiStripBenchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// ANSI stripping, with and without escape sequences present. The clean case is the interesting
/// one: piped dotnet output usually has no escapes at all, and it runs through three regex
/// replacements regardless. If the clean case costs the same as the decorated one, a fast path is
/// available for free on the common input.
/// </summary>
[MemoryDiagnoser]
public class AnsiStripBenchmarks
{
    private const char Escape = '';

    private string _clean = string.Empty;
    private string _decorated = string.Empty;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        _clean = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);
        _decorated = Decorate(_clean);
    }

    [Benchmark(Baseline = true)]
    public string StripCleanInput() => AnsiStrip.Strip(_clean);

    [Benchmark]
    public string StripDecoratedInput() => AnsiStrip.Strip(_decorated);

    /// <summary>Wraps every line in a colour sequence, as a TTY-attached dotnet run would.</summary>
    private static string Decorate(string text) => string.Join(
        '\n',
        text.Split('\n').Select(line => $"{Escape}[32m{line}{Escape}[0m"));
}
```

- [ ] **Step 4: Write the token-estimator benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/TokenEstimatorBenchmarks.cs`. This is the class most
likely to produce an actionable finding:

```csharp
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.ML.Tokenizers;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Token counting cost. Every dtk invocation counts the full raw output and the filtered output,
/// purely to record a savings statistic, so this runs twice per command over the raw log's full
/// length. <see cref="EstimateRawThenFiltered"/> measures that pair as the pipeline actually pays
/// for it.
/// </summary>
[MemoryDiagnoser]
public class TokenEstimatorBenchmarks
{
    private string _raw = string.Empty;
    private string _filtered = string.Empty;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [Params(TokenizerModel.Cl100kBase, TokenizerModel.O200kBase)]
    public TokenizerModel Tokenizer { get; set; } = TokenizerModel.Cl100kBase;

    [GlobalSetup]
    public void Setup()
    {
        _raw = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);
        _filtered = SavingsScenarios.FilterFor(FilterKeys.Build).Apply(_raw, exitCode: 0);

        // Warm the tokenizer cache so the vocabulary load is not charged to the first iteration.
        // Cold load is measured separately by TokenizerLoadBenchmarks.
        TokenEstimator.Estimate("warmup", Tokenizer);
    }

    [Benchmark]
    public int EstimateRaw() => TokenEstimator.Estimate(_raw, Tokenizer);

    [Benchmark]
    public int EstimateFiltered() => TokenEstimator.Estimate(_filtered, Tokenizer);

    /// <summary>Both counts, as one dtk invocation pays for them.</summary>
    [Benchmark]
    public int EstimateRawThenFiltered() =>
        TokenEstimator.Estimate(_raw, Tokenizer) + TokenEstimator.Estimate(_filtered, Tokenizer);
}

/// <summary>
/// The one-time vocabulary load. <see cref="TokenEstimator"/> caches tokenizers in a private
/// static dictionary that cannot be cleared from outside the type, so this constructs the
/// tokenizer directly — the same underlying work the first <c>Estimate</c> call of a process pays
/// for, and a plausible dominant cost for a short log.
/// </summary>
[MemoryDiagnoser]
public class TokenizerLoadBenchmarks
{
    [Params("cl100k_base", "o200k_base")]
    public string Encoding { get; set; } = "cl100k_base";

    [Benchmark]
    public TiktokenTokenizer LoadTokenizer() => TiktokenTokenizer.CreateForEncoding(Encoding);
}
```

Note: `TokenizerLoadBenchmarks` needs `Microsoft.ML.Tokenizers` at compile time. It arrives
transitively through Application, which references it as an ordinary `PackageReference`. If the
compiler cannot see `TiktokenTokenizer`, add an explicit
`<PackageReference Include="Microsoft.ML.Tokenizers"/>` to the runner csproj — the version is
already centrally managed.

- [ ] **Step 5: Build and verify the benchmarks are discovered**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --list flat
```

Expected: every benchmark case is listed. `FilterBenchmarks.Apply` should appear 18 times
(6 filters × 3 tiers).

- [ ] **Step 6: Run a fast subset to confirm they produce real numbers**

```bash
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*AnsiStripBenchmarks*'
```

Expected: a results table with `Mean`, `Ratio` and `Allocated`.

Note what it shows: if `StripCleanInput` and `StripDecoratedInput` cost about the same and both
allocate proportionally to input size, that is the free fast path the spec predicted. Record the
observation in the commit message — finding these is the point of the project.

Be aware the full suite is slow. `TokenEstimatorBenchmarks` at the Large tier tokenizes about a
megabyte per operation, so expect the complete run to take tens of minutes. Use `--filter` while
developing.

- [ ] **Step 7: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/
git commit -m "feat: benchmark the filters, ANSI stripping and token counting

Each filter across three tiers, so the cost can be read as a curve --
growth faster than input size is the defect worth catching, and no single
measurement shows it. ANSI stripping is measured on clean input as well
as decorated, because piped output usually has no escapes yet runs three
regex replacements regardless.

Token counting gets the most attention: the pipeline counts the full raw
log and the filtered output on every invocation to record one statistic,
so both are measured separately and as the pair the user actually pays
for, with the one-time vocabulary load measured on its own.

Drops the toolchain smoke benchmark, which had served its purpose."
```

---

### Task 8: Pipeline, tracker and startup benchmarks

These touch config, the tracking database and tee logs, so hermeticity is the main correctness
requirement: nothing here may write to the developer's real home-directory state.

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/Support/NullTracker.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/PipelineBenchmarks.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/TrackerBenchmarks.cs`
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/StartupBenchmarks.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`

**Interfaces:**
- Consumes: `LogCorpusGenerator.Generate`, `CorpusTier` (Task 3); `SavingsScenarios.FilterFor` (Task 4).
- Produces:
  - `sealed class HermeticState : IDisposable` with `static HermeticState Enter()` and
    `string ConfigPath { get; }`, `string DbPath { get; }`, `string TeeDir { get; }`
  - `sealed class NullTracker : ITracker`

- [ ] **Step 1: Add the Infrastructure reference**

In `benchmarks/DotnetTokenKiller.Benchmarks/DotnetTokenKiller.Benchmarks.csproj`, add to the
`ProjectReference` group. Note that Cli is deliberately *not* referenced: `CliConfigurator` is
`internal`, with `InternalsVisibleTo` for `Cli.IntegrationTests` only, so the command tree cannot
be benchmarked in process. Cold start covers it in Task 9 instead.

```xml
    <ProjectReference Include="../../src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj"/>
```

- [ ] **Step 2: Write the hermetic state helper**

`benchmarks/DotnetTokenKiller.Benchmarks/Support/HermeticState.cs`:

```csharp
namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// Redirects dtk's three on-disk state locations into a temporary directory for the life of a
/// benchmark, and deletes them afterwards.
/// </summary>
/// <remarks>
/// Without this, running the benchmarks would write thousands of synthetic records into the
/// developer's real tracking database and skew their own <c>dtk gain</c> report — and the benchmark
/// numbers would themselves depend on how much history happened to be there.
/// </remarks>
internal sealed class HermeticState : IDisposable
{
    private readonly DirectoryInfo _root;
    private readonly string? _previousConfigPath;
    private readonly string? _previousDbPath;
    private readonly string? _previousTeeDir;

    private HermeticState()
    {
        _root = Directory.CreateTempSubdirectory("dtk-bench-");
        _previousConfigPath = Environment.GetEnvironmentVariable("DTK_CONFIG_PATH");
        _previousDbPath = Environment.GetEnvironmentVariable("DTK_DB_PATH");
        _previousTeeDir = Environment.GetEnvironmentVariable("DTK_TEE_DIR");

        ConfigPath = Path.Combine(_root.FullName, "config.json");
        DbPath = Path.Combine(_root.FullName, "tracking.db");
        TeeDir = Path.Combine(_root.FullName, "logs");
        Directory.CreateDirectory(TeeDir);

        Environment.SetEnvironmentVariable("DTK_CONFIG_PATH", ConfigPath);
        Environment.SetEnvironmentVariable("DTK_DB_PATH", DbPath);
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", TeeDir);
    }

    internal string ConfigPath { get; }

    internal string DbPath { get; }

    internal string TeeDir { get; }

    internal static HermeticState Enter() => new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DTK_CONFIG_PATH", _previousConfigPath);
        Environment.SetEnvironmentVariable("DTK_DB_PATH", _previousDbPath);
        Environment.SetEnvironmentVariable("DTK_TEE_DIR", _previousTeeDir);

        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a benchmark run over.
        }
    }
}
```

- [ ] **Step 3: Write the no-op tracker**

`benchmarks/DotnetTokenKiller.Benchmarks/Support/NullTracker.cs`:

```csharp
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// A tracker that records nothing, so the pipeline benchmark can separate filtering and token
/// counting from database cost. SQLite is measured on its own in TrackerBenchmarks.
/// </summary>
internal sealed class NullTracker : ITracker
{
    public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<GainSummary> GetSummaryAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task<CoverageSummary> GetCoverageAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 4: Write the pipeline benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/PipelineBenchmarks.cs`:

```csharp
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Configuration;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// The combined per-invocation cost: ANSI strip, filter, both token counts, glyph normalisation and
/// the write. This is the closest in-process figure to what a user pays for one wrapped command.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private HermeticState? _state;
    private FilteredOutputPipeline? _pipeline;
    private FilteredOutputRequest? _request;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        _state = HermeticState.Enter();

        var raw = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);

        _pipeline = new FilteredOutputPipeline(
            new NullTracker(),
            TextWriter.Null,
            new JsonConfigProvider(_state.ConfigPath));

        _request = new FilteredOutputRequest(
            SavingsScenarios.FilterFor(FilterKeys.Build),
            raw,
            ExitCode: 0,
            CommandSlug: FilterKeys.Build,
            DisplayCommandLine: "dotnet build",
            Source: RunSource.Run,
            Options: new OutputOptions(),
            StartTimestamp: Stopwatch.GetTimestamp());
    }

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    [Benchmark]
    public async Task<int> ProcessAsync() =>
        await _pipeline!.ProcessAsync(_request!, NullTeeSession.Instance).ConfigureAwait(false);
}
```

- [ ] **Step 5: Write the tracker benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/TrackerBenchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Tracking write cost, and aggregation cost as history accumulates. The <c>RowCount</c> parameter
/// is the point of the second benchmark: <c>dtk gain</c> reads every retained record, so its cost
/// grows with how long the user has had dtk installed — a regression nobody would ever reproduce on
/// a fresh database.
/// </summary>
[MemoryDiagnoser]
public class TrackerBenchmarks
{
    private HermeticState? _state;
    private SqliteTracker? _tracker;

    [Params(100, 10_000)]
    public int RowCount { get; set; } = 100;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _state = HermeticState.Enter();
        _tracker = new SqliteTracker($"Data Source={_state.DbPath}");

        for (var i = 0; i < RowCount; i++)
        {
            await _tracker.RecordAsync(NewRecord(i)).ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_tracker is not null)
        {
            await _tracker.DisposeAsync().ConfigureAwait(false);
        }

        _state?.Dispose();
    }

    [Benchmark]
    public async Task RecordAsync() => await _tracker!.RecordAsync(NewRecord(0)).ConfigureAwait(false);

    [Benchmark]
    public async Task<GainSummary> GetSummaryAsync() =>
        await _tracker!.GetSummaryAsync(days: 30, projectPath: null).ConfigureAwait(false);

    private static CommandRecord NewRecord(int index) => new(
        DateTimeOffset.UtcNow.AddMinutes(-index),
        "build",
        "/repo",
        new TokenStatistics(1000, 100, 900, 90.0),
        TimeSpan.FromMilliseconds(1200));
}
```

Both shapes above are verified against the current source:
`TokenStatistics(int Input, int Output, int Saved, double SavingsPercentage)` and
`SqliteTracker(string connectionString, int defaultRetentionDays = 90)` — the tracker takes a
connection string, not a path, which is why `Data Source=` is prefixed. `TrackerFactory.cs:24`
builds the same string via `SqliteConnectionStringBuilder`; either form works.

- [ ] **Step 6: Write the startup benchmarks**

`benchmarks/DotnetTokenKiller.Benchmarks/StartupBenchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Fixed per-invocation cost paid before any output is filtered: building the service graph and
/// reading the config file. Both grow quietly as registrations and settings are added, and neither
/// is attributable to any one feature.
/// </summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    private HermeticState? _state;

    [GlobalSetup]
    public void Setup() => _state = HermeticState.Enter();

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    [Benchmark]
    public ServiceProvider BuildServiceProvider() =>
        new ServiceCollection().AddApplication().AddInfrastructure().BuildServiceProvider();

    [Benchmark]
    public async Task<DtkConfig> LoadConfigAsync() =>
        await new JsonConfigProvider(_state!.ConfigPath).LoadAsync().ConfigureAwait(false);
}
```

- [ ] **Step 7: Build and run the new benchmarks**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*StartupBenchmarks*'
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*TrackerBenchmarks*'
```

Expected: results tables for both.

- [ ] **Step 8: Verify hermeticity — this step protects the developer's own data**

```bash
dtk gain
```

Expected: the same history as before the benchmark run — no thousands of synthetic `build` records
from `TrackerBenchmarks`. If synthetic records appear, `HermeticState` is not being entered before
the tracker is constructed. Fix that before committing, and clean up with `dtk reset`.

- [ ] **Step 9: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/
git commit -m "feat: benchmark the pipeline, tracker and startup path

Adds the combined per-invocation cost, the tracking write, gain
aggregation at 100 and 10,000 rows, the DI graph build and the config
load. The row-count parameter is the interesting one: gain reads every
retained record, so its cost grows with how long a user has had dtk
installed -- a regression nobody reproduces on a fresh database.

All of it runs against DTK_CONFIG_PATH, DTK_DB_PATH and DTK_TEE_DIR
redirected into a temp directory. Without that, a benchmark run would
write thousands of synthetic records into the developer's own tracking
database, skewing both their gain report and the benchmark numbers.

Cli is deliberately not referenced: CliConfigurator is internal to
Cli.IntegrationTests, so the command tree cannot be measured in process.
Cold start covers it instead."
```

---

### Task 9: The cold-start harness

The in-process benchmarks cannot see process start, JIT, or the first-touch vocabulary load — which
together may be most of what a user actually waits for on a small log. This measures the published
binary end to end, using `dtk pipe` so no `dotnet build` is involved.

**Files:**
- Create: `benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs`
- Modify: `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`

**Interfaces:**
- Consumes: `FixtureCorpus.Load` (Task 2); `HermeticState.Enter` (Task 8).
- Produces: `static Task<int> ColdStartCommand.RunAsync(string[] args)`.

- [ ] **Step 1: Implement the cold-start harness**

`benchmarks/DotnetTokenKiller.Benchmarks/ColdStartCommand.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Support;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Times the real <c>dtk</c> binary end to end, out of process.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>dtk pipe build</c> with a fixture on stdin. That routes through PipeFilterUseCase into
/// the same FilteredOutputPipeline as a wrapped command, exercising process start, JIT, the DI
/// graph, the config load, ANSI stripping, filtering, both token counts and the SQLite write —
/// without running a dotnet build, which would swamp the measurement and cannot be relied on to
/// run on a developer machine at all.
/// </para>
/// <para>
/// Hand-written rather than a BenchmarkDotNet job: BenchmarkDotNet would measure its own harness
/// wrapped around the spawn. Median and p95 of raw wall-clock is the honest figure for startup.
/// </para>
/// </remarks>
internal static class ColdStartCommand
{
    private const int WarmupRuns = 5;
    private const int MeasuredRuns = 50;
    private const string Fixture = "dotnet_build_errors.txt";

    internal static async Task<int> RunAsync(string[] args)
    {
        var binary = ResolveBinary(args);

        if (binary is null)
        {
            Console.Error.WriteLine(
                "Could not locate a dtk binary. Pass one explicitly:\n"
                + "  ... -- cold-start /path/to/dtk\n"
                + "or build one first:\n"
                + "  dotnet build src/DotnetTokenKiller.Cli -c Release");

            // Failing loudly matters more here than anywhere else in this project: a harness that
            // silently measured nothing would report an impressively fast startup time.
            return 1;
        }

        var input = FixtureCorpus.Load(Fixture);
        using var state = HermeticState.Enter();

        Console.WriteLine($"Cold start: {binary}");
        Console.WriteLine($"Input: {Fixture} ({input.Length} chars), exit code 1");
        Console.WriteLine($"Runs: {WarmupRuns} warmup + {MeasuredRuns} measured");
        Console.WriteLine();

        for (var i = 0; i < WarmupRuns; i++)
        {
            await TimeOnceAsync(binary, input).ConfigureAwait(false);
        }

        var samples = new List<double>(MeasuredRuns);
        for (var i = 0; i < MeasuredRuns; i++)
        {
            samples.Add(await TimeOnceAsync(binary, input).ConfigureAwait(false));
        }

        samples.Sort();
        Report("median", Percentile(samples, 0.50));
        Report("p95", Percentile(samples, 0.95));
        Report("min", samples[0]);
        Report("max", samples[^1]);
        return 0;
    }

    private static void Report(string label, double milliseconds) => Console.WriteLine(
        string.Create(CultureInfo.InvariantCulture, $"  {label,-8} {milliseconds,8:F1} ms"));

    /// <summary>Nearest-rank percentile over the sorted samples.</summary>
    private static double Percentile(List<double> sorted, double fraction)
    {
        var rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static async Task<double> TimeOnceAsync(string binary, string input)
    {
        var info = new ProcessStartInfo(binary)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("pipe");
        info.ArgumentList.Add("build");
        info.ArgumentList.Add("--exit-code");
        info.ArgumentList.Add("1");

        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {binary}.");

        await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
        process.StandardInput.Close();

        // Drain both pipes before waiting: a child that fills its stdout buffer blocks forever.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>
    /// Resolves the binary from an explicit argument, then the Release build output, then whatever
    /// <c>dtk</c> is installed on PATH.
    /// </summary>
    private static string? ResolveBinary(string[] args)
    {
        if (args is [_, var explicitPath, ..])
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var name = OperatingSystem.IsWindows() ? "dtk.exe" : "dtk";

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                continue;
            }

            var built = Path.Combine(
                dir.FullName, "src", "DotnetTokenKiller.Cli", "bin", "Release", "net10.0", name);

            return File.Exists(built) ? built : FromPath(name);
        }

        return FromPath(name);
    }

    private static string? FromPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator)
        .Where(dir => !string.IsNullOrWhiteSpace(dir))
        .Select(dir => Path.Combine(dir, name))
        .FirstOrDefault(File.Exists);
}
```

- [ ] **Step 2: Wire the verb in**

In `benchmarks/DotnetTokenKiller.Benchmarks/Program.cs`, replace the `cold-start` case:

```csharp
            case ["cold-start", ..]:
                return await ColdStartCommand.RunAsync(args).ConfigureAwait(false);
```

- [ ] **Step 3: Verify it fails loudly when no binary exists**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start /nonexistent/dtk
```

Expected: exit code 1 and the "Could not locate a dtk binary" message — not a timing table.

- [ ] **Step 4: Run it against a real binary**

```bash
dtk dotnet build src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj -c Release
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
```

Expected: median, p95, min and max in milliseconds.

This number is the headline result of the whole project — the tax dtk adds to a wrapped command.
Compare it against `PipelineBenchmarks.ProcessAsync` at the Small tier from Task 8. If cold start
is an order of magnitude larger, then startup, not filtering, is what users feel, and the tokenizer
vocabulary load is the first thing to investigate. Record the comparison in the commit message.

- [ ] **Step 5: Verify hermeticity again**

Run: `dtk gain`

Expected: no 55 new `build` records from the cold-start runs. The spawned processes inherit the
`DTK_*` variables `HermeticState` sets, which is what keeps them out of the real database. If they
leaked, the environment is being set after the child starts — fix it before committing.

- [ ] **Step 6: Format and commit**

```bash
dtk dotnet format DotnetTokenKiller.slnx --no-restore
git add benchmarks/
git commit -m "feat: add the cold-start harness

The in-process benchmarks cannot see process start, JIT or the
first-touch tokenizer vocabulary load, which together may be most of what
a user waits for on a small log. This spawns the real binary as
'dtk pipe build' with a fixture on stdin: the same pipeline a wrapped
command uses, with no dotnet build to swamp the measurement or to make
the harness unrunnable locally.

55 runs, first 5 discarded, median and p95 over the rest -- hand-timed
rather than a BenchmarkDotNet job, which would measure its own harness
around the spawn. Missing binaries fail loudly: a harness that silently
measured nothing would report a very impressive startup time."
```

---

### Task 10: CI wiring, reporting exclusions and documentation

**Files:**
- Create: `.github/workflows/benchmarks.yml`
- Modify: `.github/workflows/sonarqube.yml`
- Modify: `stryker-config.json`
- Modify: `CLAUDE.md`
- Modify: `CONTRIBUTING.md`

**Interfaces:**
- Consumes: every verb and benchmark from Tasks 1-9.
- Produces: nothing.

- [ ] **Step 1: Add the manual benchmark workflow**

`.github/workflows/benchmarks.yml`. `workflow_dispatch` only and deliberately never gating:
timings on shared runners vary 10-30%, and a threshold wide enough to survive that noise would not
catch anything worth catching. The savings gate already runs on every pull request inside
`dotnet test`.

```yaml
name: Benchmarks

permissions:
  contents: read

on:
  workflow_dispatch:
    inputs:
      filter:
        description: 'BenchmarkDotNet filter glob, e.g. *FilterBenchmarks*'
        required: false
        default: '*'

jobs:
  benchmark:
    runs-on: ubuntu-latest
    timeout-minutes: 90

    steps:
      - uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: Directory.Packages.props

      - name: Restore dependencies
        run: dotnet restore

      - name: Run benchmarks
        run: >
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks
          -- --filter '${{ inputs.filter }}' --artifacts artifacts/benchmarks

      - name: Measure cold start
        run: |
          dotnet build src/DotnetTokenKiller.Cli -c Release
          mkdir -p artifacts/benchmarks
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start \
            | tee artifacts/benchmarks/cold-start.txt

      - name: Upload results
        if: ${{ !cancelled() }}
        uses: actions/upload-artifact@v7
        with:
          name: benchmark-results
          path: artifacts/benchmarks/**
          retention-days: 30
```

- [ ] **Step 2: Exclude the benchmarks from Sonar**

In `.github/workflows/sonarqube.yml`, in the `dotnet-sonarscanner begin` line, add
`benchmarks/**/*` to both exclusion lists, which become:

```
/d:sonar.exclusions="_bmad/**/*,samples/**/*,.claude/**/*,benchmarks/**/*"
/d:sonar.coverage.exclusions=".claude/**/*,_bmad/**/*,samples/**/*,benchmarks/**/*,src/DotnetTokenKiller.Cli/Program.cs"
```

Without the coverage exclusion, adding projects whose benchmark classes no test covers would drag
the reported coverage percentage down for reasons unrelated to test quality.

- [ ] **Step 3: Exclude the benchmarks from mutation testing**

In `stryker-config.json`, add a `mutate` exclusion inside the `stryker-config` object:

```json
    "mutate": [
      "!benchmarks/**/*.cs"
    ],
```

Application.Tests references Benchmarks.Corpus, so without this Stryker would mutate the corpus
generator and the savings engine. Surviving mutants in a log generator are noise, not signal.

- [ ] **Step 4: Document the commands in CLAUDE.md**

In `CLAUDE.md`, add to the `## Commands` fenced block, after the `dtk dotnet list package` entry:

```
# Run the benchmark suite (Release only; the full run takes tens of minutes)
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --filter '*FilterBenchmarks*'

# Measure the end-to-end cold-start cost of the built binary
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start

# Regenerate the savings baseline after intentionally changing a filter
dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
```

Then add a section after `## Git Hooks`:

```markdown
## Benchmarks

`benchmarks/DotnetTokenKiller.Benchmarks.Corpus` holds the fixture corpus, a seeded log generator
and the savings engine; `benchmarks/DotnetTokenKiller.Benchmarks` holds the BenchmarkDotNet suite.

Performance here has two dimensions, gated differently:

- **Token savings** is deterministic and hard-gated. `SavingsBaselineTests` compares every scenario
  against `benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json` and runs
  as part of `dotnet test`. **Changing a filter's output changes its savings and fails this test.**
  That is intended: regenerate with `update-baseline` and let the diff show how the numbers moved.
- **Timings** are never gated. Shared CI runners vary too much for a threshold to mean anything, so
  the suite runs on demand via the `Benchmarks` workflow and uploads its results as artifacts.
```

- [ ] **Step 5: Document the regeneration workflow in CONTRIBUTING.md**

Add a section to `CONTRIBUTING.md`, covering the one failure a contributor will otherwise hit with
no warning:

```markdown
## Savings baseline

`dotnet test` gates the percentage of tokens each filter removes. If you change what a filter
emits, the savings change and `SavingsBaselineTests` fails with a table of the drifted scenarios.
That is the gate working, not a flaky test.

Regenerate the baseline and commit the result alongside your change:

    dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline

The diff then records how your change moved the savings, which is the point.

A dependency bump to `Microsoft.ML.Tokenizers.Data.*` can also shift every count at once. The test
message distinguishes the two cases using a recorded tokenizer fingerprint, and says which it saw.
```

- [ ] **Step 6: Validate the workflow YAML and JSON**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/benchmarks.yml')); print('benchmarks.yml ok')"
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/sonarqube.yml')); print('sonarqube.yml ok')"
python3 -c "import json; json.load(open('stryker-config.json')); print('stryker-config.json ok')"
```

Expected: three `ok` lines.

- [ ] **Step 7: Run the full verification pass**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
dtk dotnet test DotnetTokenKiller.slnx
```

Expected: build clean with no warnings, formatting verified with no changes, tests pass. The CLI
integration tests may fail locally for reasons predating this work — confirm only that no *new*
failures appeared, and that `FixtureCorpusTests`, `LogCorpusGeneratorTests`, `SavingsEngineTests`
and `SavingsBaselineTests` all pass.

- [ ] **Step 8: Commit**

```bash
git add .github/ stryker-config.json CLAUDE.md CONTRIBUTING.md
git commit -m "ci: wire up the benchmark workflow and document the gate

The savings gate needs no new CI surface -- it runs inside the existing
dotnet test step. Timings get a workflow_dispatch-only job that uploads
its results and never gates: shared runners vary 10-30%, and a threshold
wide enough to survive that would not catch anything worth catching.

Excludes benchmarks/ from Sonar coverage, which would otherwise report a
drop for adding projects no test covers, and from Stryker, which would
otherwise mutate the log generator through the Application.Tests
reference and report surviving mutants as findings.

Documents the one surprise a contributor will hit: changing a filter's
output changes its savings and fails the gate, and regenerating the
baseline is the intended response."
```

---

## Verification Checklist

Run before opening a pull request:

- [ ] `dtk dotnet build DotnetTokenKiller.slnx` — clean, zero warnings under `TreatWarningsAsErrors`
- [ ] `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` — no changes
- [ ] `dtk dotnet test DotnetTokenKiller.slnx` — no new failures versus the pre-change baseline
- [ ] `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- --list flat` — every benchmark discovered
- [ ] `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start` — reports a median
- [ ] `dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline` then `git diff --exit-code benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/` — the committed baseline is reproducible
- [ ] `dtk gain` — no synthetic benchmark records leaked into the real tracking database
- [ ] `git status --short` — no `BenchmarkDotNet.Artifacts/` or `artifacts/` entries appear

## Findings to report

The project exists to produce numbers worth acting on. When the suite first runs green, record in
the pull request description:

1. **Cold start median and p95**, against `PipelineBenchmarks.ProcessAsync` at the Small tier.
   Their ratio says whether users feel startup or filtering.
2. **`EstimateRawThenFiltered` versus `Apply`** at each tier. If token counting dominates
   filtering, the double count in `FilteredOutputPipeline.TrackIfEnabledAsync` is the
   highest-value optimization available — and it exists only to record a statistic.
3. **`TokenizerLoadBenchmarks`** absolute cost, against cold start. If the vocabulary load is most
   of startup, that is the first thing to attack.
4. **`StripCleanInput` versus `StripDecoratedInput`.** Equal cost means a fast path for
   escape-free input — the common case — is available for free.
5. **`GetSummaryAsync` at 100 versus 10 000 rows.** Growth worse than linear means `dtk gain` gets
   slower the longer someone uses dtk.
6. **Overall savings percentage** from the baseline, per filter. This is the first time the
   product's headline claim has a measured number behind it.

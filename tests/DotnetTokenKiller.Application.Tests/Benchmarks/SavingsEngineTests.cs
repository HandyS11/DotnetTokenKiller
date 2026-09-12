using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Benchmarks.Corpus.Savings;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class SavingsEngineTests
{
    /// <summary>
    /// One scenario per filter, paired with a filter this test constructs itself on
    /// <see cref="SavingsScenarios.PinnedRoot"/>. Stating the expected construction here rather
    /// than calling <c>SavingsScenarios.FilterFor</c> is the whole point: the production mapping
    /// has to agree with it, so un-pinning any branch of it moves the counts and fails.
    /// </summary>
    private static readonly (string ScenarioId, IOutputFilter Filter)[] PinnedFilters =
    [
        ("build/errors", new DotnetBuildFilter(SavingsScenarios.PinnedRoot)),
        ("clean/raw", new DotnetCleanFilter(SavingsScenarios.PinnedRoot)),
        ("format/violations", new DotnetFormatFilter(SavingsScenarios.PinnedRoot)),
        ("restore/raw", new DotnetRestoreFilter(SavingsScenarios.PinnedRoot)),
        ("test/failures", new DotnetTestFilter(SavingsScenarios.PinnedRoot)),

        // Takes no root at all; here so a root ever being added to it does not go unnoticed.
        ("list-package/all", new DotnetListPackageFilter()),
    ];

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
        // Five of the six filters resolve `rootPath ?? Environment.CurrentDirectory` and
        // relativise diagnostic paths against it, so an unpinned filter produces different text —
        // and a different token count — depending on where the process happened to run, and the
        // exact baseline could never hold. This is the assertion that pins the pinning.
        //
        // It deliberately does not move the process to prove that. xunit runs one collection per
        // test class in parallel and this assembly is full of classes that read
        // Environment.CurrentDirectory (every filter suite's Apply_DefaultRootPath_* test,
        // FilteredRunUseCaseTests, PipeFilterUseCaseTests), so setting it here — even briefly,
        // even restored in a finally — would be visible to all of them and would surface as
        // intermittent red on unrelated pull requests.
        foreach (var (scenarioId, pinnedFilter) in PinnedFilters)
        {
            var scenario = SavingsScenarios.All.Single(s => s.Id == scenarioId);
            var stripped = AnsiStrip.Strip(FixtureCorpus.Load(scenario.Fixture));
            var pinnedTokens = TokenEstimator.Estimate(
                pinnedFilter.Apply(stripped, scenario.ExitCode), SavingsEngine.Tokenizer);

            SavingsEngine.Measure(scenario).FilteredTokens.Should().Be(
                pinnedTokens,
                "scenario {0} must be measured through a filter pinned to {1}, not through one that "
                + "resolves the working directory",
                scenarioId,
                SavingsScenarios.PinnedRoot);
        }
    }

    [Fact]
    public void Measure_RelativisesPathsAgainstThePinnedRootAlone()
    {
        var scenario = SavingsScenarios.All.Single(s => s.Id == "build/errors");
        var stripped = AnsiStrip.Strip(FixtureCorpus.Load(scenario.Fixture));

        var filtered = SavingsScenarios.FilterFor(scenario.FilterKey).Apply(stripped, scenario.ExitCode);

        // The fixture's paths sit under /test/project/root, and Path.GetRelativePath always
        // returns something relative — so relativising them against "/repo", one segment deep,
        // climbs exactly one level. A filter that fell back to the working directory would climb
        // out of tests/.../bin/Debug/net10.0 with a whole ladder of them: machine-dependent text,
        // and a different token count on every developer's checkout.
        filtered.Should().Contain("../test/project/root/src/");
        filtered.Should().NotContain("../../");
        filtered.Should().NotContain(Environment.CurrentDirectory);
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

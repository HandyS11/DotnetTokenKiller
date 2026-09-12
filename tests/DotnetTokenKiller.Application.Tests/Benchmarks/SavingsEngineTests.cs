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

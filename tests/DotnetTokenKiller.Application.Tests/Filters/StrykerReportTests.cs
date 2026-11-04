using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public sealed class StrykerReportTests
{
    private const string Calculator = "/test/project/root/Calc/Calculator.cs";

    [Fact]
    public void ReadMutants_ParsesSurvivorsAndNoCoverage()
    {
        var mutants = StrykerReport.ReadMutants(LoadFixture("stryker_mutation_report.json"));

        mutants.Should().HaveCount(17);
        mutants.Where(m => m.Status == "Survived").Should().Equal(
            new StrykerMutant(Calculator, 6, 45, "Equality mutation", "a >= 0", "Survived"),
            new StrykerMutant(Calculator, 8, 58, "Equality mutation", "v <= lo", "Survived"),
            new StrykerMutant(Calculator, 8, 81, "Equality mutation", "v >= hi", "Survived"));
        mutants.Count(m => m.Status == "NoCoverage").Should().Be(7);
        mutants.Count(m => m.Status == "Killed").Should().Be(6);
    }

    [Fact]
    public void ReadMutants_MissingReplacement_IsNull_AndIncompleteMutantsAreSkipped()
    {
        const string json = """
                            {"files":{"/a/A.cs":{"mutants":[
                              {"mutatorName":"Block removal mutation","location":{"start":{"line":3,"column":9}},"status":"Survived"},
                              {"mutatorName":"No status","location":{"start":{"line":4,"column":1}}},
                              {"mutatorName":"No location","status":"Killed"}
                            ]}}}
                            """;

        StrykerReport.ReadMutants(json).Should().Equal(
            new StrykerMutant("/a/A.cs", 3, 9, "Block removal mutation", null, "Survived"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"files\":42}")]
    [InlineData("{\"files\":{\"/a/A.cs\":{\"mutants\":{}}}}")]
    [InlineData("{\"files\":{\"/a/A.cs\":{\"mutants\":[42]}}}")]
    public void ReadMutants_InvalidJson_Empty(string json)
    {
        StrykerReport.ReadMutants(json).Should().BeEmpty();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(StrykerReportTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

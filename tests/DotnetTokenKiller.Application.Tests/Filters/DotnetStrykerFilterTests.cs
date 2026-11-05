using System.Text;
using System.Text.Json;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public sealed class DotnetStrykerFilterTests
{
    private const string Root = "/test/project/root";
    private const string TestsDirectory = Root + "/Calc.Tests";
    private const string Counts = "6 killed, 3 survived, 0 timeout, 7 no coverage";
    private const string Hint = "survivors: enable the json reporter to list them here\n";

    private const string JsonReporterReport =
        Root + "/Calc.Tests/StrykerOutput/2026-11-04.23-39-35/reports/mutation-report.json";

    private const string Survivors =
        "Survived (3):\n" +
        "  Calc/Calculator.cs: (6,45) Equality → a >= 0 · (8,58) Equality → v <= lo · (8,81) Equality → v >= hi\n";

    private const string NoCoverage = "No coverage: 7 (Calc/Calculator.cs ×7)\n";

    private static readonly DateTimeOffset Started = new(2026, 11, 4, 23, 39, 35, TimeSpan.Zero);

    [Fact]
    public void Apply_Default_ScoreCountsAndHint()
    {
        var result = new DotnetStrykerFilter(null, Root)
            .Apply(LoadFixture("dotnet_stryker_default.txt"), 0, FilterContext.Unknown).Output;

        result.Should().Be(
            "Project Calc.Tests.csproj simulated build failed. Trying again with a nuget restore.\n" +
            "Analysis of project Calc.Tests.csproj succeeded.\n" +
            Hint +
            "No coverage: 7\n" +
            $"✓ dotnet stryker: mutation score 37.50% · {Counts} (11.27s) · " +
            "report Calc.Tests/StrykerOutput/2026-11-04.23-39-23/reports/mutation-report.html\n");
    }

    [Fact]
    public void Apply_JsonReporter_ListsSurvivors()
    {
        var files = new FakeFiles(new Dictionary<string, string> { [JsonReporterReport] = LoadFixture("stryker_mutation_report.json") });

        var result = new DotnetStrykerFilter(files, Root)
            .Apply(LoadFixture("dotnet_stryker_json_reporter.txt"), 0, FilterContext.Unknown).Output;

        result.Should().Be(
            Survivors +
            NoCoverage +
            $"✓ dotnet stryker: mutation score 37.50% · {Counts} (10.09s) · " +
            "report Calc.Tests/StrykerOutput/2026-11-04.23-39-35/reports/mutation-report.html\n");
    }

    [Fact]
    public void Apply_WrappedPath_Rejoined()
    {
        // The fixture prints the path over two lines, cut inside "mutation-report.json" at column 80.
        var files = new FakeFiles([]);

        new DotnetStrykerFilter(files, Root).Apply(LoadFixture("dotnet_stryker_json_reporter.txt"), 0, FilterContext.Unknown);

        files.Reads.Should().Equal(JsonReporterReport);
    }

    [Fact]
    public void Apply_WindowsWrappedPath_Rejoined()
    {
        // No Windows capture is possible here: this is the real json reporter fixture with its json report path
        // replaced by a Windows one, wrapped at column 80 as Stryker wraps it.
        const string windowsReport =
            "C:/Users/dev/source/repos/Calc/Calc.Tests/StrykerOutput/2026-11-04.23-39-35/reports/mutation-report.json";
        var fixture = LoadFixture("dotnet_stryker_json_reporter.txt");
        const string printed = "file://" + JsonReporterReport;
        var output = fixture.Replace(Wrap(printed), Wrap("file:///" + windowsReport), StringComparison.Ordinal);
        output.Should().NotBe(fixture);
        var files = new FakeFiles(new Dictionary<string, string> { [windowsReport] = LoadFixture("stryker_mutation_report.json") });

        var result = new DotnetStrykerFilter(files, Root).Apply(output, 0, FilterContext.Unknown).Output;

        files.Reads.Should().Equal(windowsReport);
        result.Should().StartWith(Survivors);
    }

    [Fact]
    public void Apply_WrappedPathWithSpace_Rejoined()
    {
        // A path with a space wraps at the space, which stays at the end of the first line (as a real run with
        // -O "…/out dir/stryker run" printed it); a word longer than a line is then cut at column 80.
        const string report = "/test/project/root/My Projects/Calc.Tests/StrykerOutput/2026-11-04.23-39-35/reports/mutation-report.json";
        const string output = "Your json report has been generated at:\n" +
                              "file:///test/project/root/My \n" +
                              "Projects/Calc.Tests/StrykerOutput/2026-11-04.23-39-35/reports/mutation-report.js\n" +
                              "on\n" +
                              "[23:39:45 INF] Time Elapsed 00:00:10.0852235\n" +
                              "[23:39:45 INF] The final mutation score is 37.50 %\n";
        var files = new FakeFiles([]);

        new DotnetStrykerFilter(files, Root).Apply(output, 0, FilterContext.Unknown);

        files.Reads.Should().Equal(report);
    }

    [Fact]
    public void Apply_UnreadableReport_SaysSo()
    {
        var result = new DotnetStrykerFilter(new FakeFiles([]), Root)
            .Apply(LoadFixture("dotnet_stryker_json_reporter.txt"), 0, FilterContext.Unknown).Output;

        result.Should().StartWith("survivors: json report not readable\nNo coverage: 7\n✓ dotnet stryker:");
    }

    [Theory]
    [InlineData("", "StrykerOutput")]
    [InlineData("-O out", "out")]
    [InlineData("--output out", "out")]
    [InlineData("--output=out", "out")]
    [InlineData("--reporter json -- -O out", "StrykerOutput")]
    public void Apply_FallsBackToNewestReport_WhenPathMissing(string arguments, string directory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        const string newest = "/elsewhere/reports/mutation-report.json";
        var files = new FakeFiles(new Dictionary<string, string> { [newest] = LoadFixture("stryker_mutation_report.json") }, newest);
        // A real run's working directory is fully qualified; TestsDirectory alone is not on Windows (no drive),
        // where Path.GetFullPath(path, basePath) rejects it.
        var workingDirectory = Path.GetFullPath(TestsDirectory);
        var context = new FilterContext
        {
            Source = Domain.Tracking.RunSource.Run,
            Arguments = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            WorkingDirectory = workingDirectory,
            StartedUtc = Started,
        };

        var result = new DotnetStrykerFilter(files, Root).Apply(LoadFixture("dotnet_stryker_default.txt"), 0, context).Output;

        files.Searches.Should().Equal((Path.GetFullPath(directory, workingDirectory), "mutation-report.json", Started));
        result.Should().Contain(Survivors + NoCoverage);
    }

    [Fact]
    public void Apply_Piped_DoesNotSearchForAReport()
    {
        var files = new FakeFiles([], "/elsewhere/mutation-report.json");
        var context = new FilterContext { WorkingDirectory = TestsDirectory };

        var result = new DotnetStrykerFilter(files, Root).Apply(LoadFixture("dotnet_stryker_default.txt"), 0, context).Output;

        files.Searches.Should().BeEmpty();
        result.Should().Contain(Hint);
    }

    [Fact]
    public void Apply_BreakAt()
    {
        var result = new DotnetStrykerFilter(null, Root)
            .Apply(LoadFixture("dotnet_stryker_break_at.txt"), 2, FilterContext.Unknown).Output;

        result.Should().Be(
            Hint +
            "No coverage: 7\n" +
            $"✗ dotnet stryker: mutation score 37.50% below break threshold 90% (exit 2) · {Counts} (10.08s) · " +
            "report Calc.Tests/StrykerOutput/2026-11-04.23-39-45/reports/mutation-report.html\n");
    }

    [Fact]
    public void Apply_BreakAt_CommaDecimals_AreNormalizedLikeTheScore()
    {
        // The real --break-at run, as a culture with a decimal comma prints it: both figures read with a dot.
        var fixture = LoadFixture("dotnet_stryker_break_at.txt")
            .Replace("The final mutation score is 37.50 %", "The final mutation score is 37,50 %", StringComparison.Ordinal)
            .Replace("configured break threshold of 90%.", "configured break threshold of 90,5%.", StringComparison.Ordinal);

        var result = new DotnetStrykerFilter(null, Root).Apply(fixture, 2, FilterContext.Unknown).Output;

        result.Should().Contain("✗ dotnet stryker: mutation score 37.50% below break threshold 90.5% (exit 2)");
    }

    [Fact]
    public void Apply_BuildFailed_GroupedErrors()
    {
        var result = new DotnetStrykerFilter(null, Root)
            .Apply(LoadFixture("dotnet_stryker_build_failed.txt"), 1, FilterContext.Unknown).Output;

        result.Should().Be(
            "Calc/Broken.cs(5,36) CS1525: Invalid expression term ';'\n" +
            "✗ dotnet stryker failed (exit 1): initial build failed · 1 error\n");
    }

    [Fact]
    public void Apply_Solution()
    {
        // The json report path is followed by the next log line, with no blank line in between.
        const string report = Root + "/StrykerOutput/2026-11-04.23-41-12/reports/mutation-report.json";
        var files = new FakeFiles(new Dictionary<string, string> { [report] = LoadFixture("stryker_mutation_report.json") });

        var result = new DotnetStrykerFilter(files, Root)
            .Apply(LoadFixture("dotnet_stryker_solution.txt"), 0, FilterContext.Unknown).Output;

        files.Reads.Should().Equal(report);
        result.Should().Be(
            Survivors +
            NoCoverage +
            $"✓ dotnet stryker: mutation score 37.50% · {Counts} (9.11s) · " +
            "report StrykerOutput/2026-11-04.23-41-12/reports/mutation-report.json\n");
    }

    [Fact]
    public void Apply_JsonOnly_CountsFromReport()
    {
        // Without the progress reporter Stryker prints no Killed/Survived block: the counts come from the report.
        const string report = Root + "/Calc.Tests/StrykerOutput/2026-11-04.23-42-53/reports/mutation-report.json";
        var files = new FakeFiles(new Dictionary<string, string> { [report] = LoadFixture("stryker_mutation_report.json") });

        var result = new DotnetStrykerFilter(files, Root)
            .Apply(LoadFixture("dotnet_stryker_json_only.txt"), 0, FilterContext.Unknown).Output;

        result.Should().Be(
            Survivors +
            NoCoverage +
            $"✓ dotnet stryker: mutation score 37.50% · {Counts} (10.13s) · " +
            "report Calc.Tests/StrykerOutput/2026-11-04.23-42-53/reports/mutation-report.json\n");
    }

    [Fact]
    public void Apply_JsonOnly_WithoutReport_OmitsCounts()
    {
        var result = new DotnetStrykerFilter(null, Root)
            .Apply(LoadFixture("dotnet_stryker_json_only.txt"), 0, FilterContext.Unknown).Output;

        result.Should().Be(
            "survivors: json report not readable\n" +
            "No coverage: 7\n" +
            "✓ dotnet stryker: mutation score 37.50% (10.13s) · " +
            "report Calc.Tests/StrykerOutput/2026-11-04.23-42-53/reports/mutation-report.json\n");
    }

    [Fact]
    public void Apply_CapsAt20Survivors()
    {
        var mutants = Enumerable.Range(1, 15).Select(i => ("A.cs", i, "Survived", (string?)"x"))
            .Concat(Enumerable.Range(1, 10).Select(i => ("B.cs", i, "Survived", (string?)"y")))
            .Concat(Enumerable.Range(1, 12).Select(i => ($"F{i:00}.cs", 1, "NoCoverage", (string?)"z")));
        var (output, files) = JsonOnlyRun(Report(mutants));

        var lines = new DotnetStrykerFilter(files, Root).Apply(output, 0, FilterContext.Unknown).Output
            .TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(6);
        lines[0].Should().Be("Survived (25):");
        lines[1].Should().Be("  A.cs: " + string.Join(" · ", Enumerable.Range(1, 15).Select(i => $"({i},5) Equality → x")));
        lines[2].Should().Be("  B.cs: " + string.Join(" · ", Enumerable.Range(1, 5).Select(i => $"({i},5) Equality → y")));
        lines[3].Should().Be("… 5 more (report)");
        lines[4].Should().Be(
            "No coverage: 12 (" + string.Join(", ", Enumerable.Range(1, 10).Select(i => $"F{i:00}.cs ×1")) +
            ", … 2 more files)");
        lines[5].Should().StartWith("✓ dotnet stryker: mutation score 37.50% · 0 killed, 25 survived, 0 timeout, 12 no coverage");
    }

    [Fact]
    public void Apply_NoCoverage_ListsTheMostUncoveredFileFirst()
    {
        var mutants = new[]
        {
            ("B.cs", 1, "NoCoverage", (string?)"z"), ("A.cs", 1, "NoCoverage", "z"), ("C.cs", 1, "NoCoverage", "z"),
            ("C.cs", 2, "NoCoverage", "z"), ("A.cs", 2, "Killed", "z"),
        };
        var (output, files) = JsonOnlyRun(Report(mutants));

        new DotnetStrykerFilter(files, Root).Apply(output, 0, FilterContext.Unknown).Output
            .Should().StartWith("No coverage: 4 (C.cs ×2, A.cs ×1, B.cs ×1)\n✓ dotnet stryker: mutation score 37.50% · 1 killed, 0 survived");
    }

    [Fact]
    public void Apply_SurvivorReplacement_OnOneLine_AndMutatorWithoutReplacement()
    {
        var mutants = new[] { ("A.cs", 3, "Survived", (string?)"{\n    return;\n}"), ("A.cs", 9, "Survived", null) };
        var (output, files) = JsonOnlyRun(Report(mutants));

        new DotnetStrykerFilter(files, Root).Apply(output, 0, FilterContext.Unknown).Output
            .Should().StartWith("Survived (2):\n  A.cs: (3,5) Equality → { return; } · (9,5) Equality\n");
    }

    [Fact]
    public void Apply_NoMutationScore()
    {
        // From a real run whose --mutate filter matched no file.
        const string output = """
                              [23:43:12 INF] 17    mutants got status Ignored.      Reason: Removed by mutate filter
                              [23:43:12 WRN] It looks like all mutants with tests were ignored. Try a re-run with less ignoring!

                              Killed:   0
                              Survived: 0
                              Timeout:  0
                              Errors:  0

                              [23:43:13 INF] Time Elapsed 00:00:08.7214110
                              [23:43:13 INF] Stryker was unable to calculate a mutation score
                              """;

        new DotnetStrykerFilter(null, Root).Apply(output, 0, FilterContext.Unknown).Output.Should().Be(
            "It looks like all mutants with tests were ignored. Try a re-run with less ignoring!\n" +
            "✓ dotnet stryker: no mutation score · 0 killed, 0 survived, 0 timeout, 0 no coverage (8.72s)\n");
    }

    [Fact]
    public void Apply_KeptLines_DistinctAndShortened()
    {
        const string output = "[10:00:00 WRN] Could not load /test/project/root/A/A.csproj.\n" +
                              "[10:00:01 WRN] Could not load /test/project/root/A/A.csproj.\n" +
                              "[10:00:02 ERR] Reporter failed for /test/project/root/out.\n" +
                              "[10:00:03 INF] The final mutation score is 50.00 %\n";

        new DotnetStrykerFilter(null, Root).Apply(output, 0, FilterContext.Unknown).Output.Should().Be(
            "Could not load A/A.csproj.\nReporter failed for out.\n✓ dotnet stryker: mutation score 50.00%\n");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unhandled exception. System.InvalidOperationException: boom\n   at Stryker.Core.Run()\n")]
    public void Apply_UnknownFailure_FallsBackToRawTail(string output)
    {
        new DotnetStrykerFilter(null, Root).Apply(output, 1, FilterContext.Unknown).Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dotnet_stryker_default.txt", 0)]
    [InlineData("dotnet_stryker_json_reporter.txt", 0)]
    [InlineData("dotnet_stryker_json_only.txt", 0)]
    [InlineData("dotnet_stryker_break_at.txt", 2)]
    [InlineData("dotnet_stryker_build_failed.txt", 1)]
    [InlineData("dotnet_stryker_solution.txt", 0)]
    public void Apply_Crlf(string name, int exitCode)
    {
        var fixture = LoadFixture(name);
        var json = LoadFixture("stryker_mutation_report.json");
        var files = new FakeFiles(new Dictionary<string, string>
        {
            [JsonReporterReport] = json,
            [Root + "/StrykerOutput/2026-11-04.23-41-12/reports/mutation-report.json"] = json,
            [Root + "/Calc.Tests/StrykerOutput/2026-11-04.23-42-53/reports/mutation-report.json"] = json,
        });
        var sut = new DotnetStrykerFilter(files, Root);

        var result = sut.Apply(fixture.ReplaceLineEndings("\r\n"), exitCode, FilterContext.Unknown).Output;

        result.Should().NotContain("\r");
        result.Should().NotBeEmpty();
        result.Should().Be(sut.Apply(fixture, exitCode, FilterContext.Unknown).Output);
    }

    /// <summary>Wraps one line at column 80 the way Stryker wraps a word longer than a line.</summary>
    /// <param name="line">The line.</param>
    private static string Wrap(string line) => string.Join('\n', line.Chunk(80).Select(c => new string(c)));

    /// <summary>The real json-only run, with its report replaced by <paramref name="report"/>.</summary>
    /// <param name="report">The report the run's printed json path serves.</param>
    private static (string Output, FakeFiles Files) JsonOnlyRun(string report) =>
        (LoadFixture("dotnet_stryker_json_only.txt"), new FakeFiles(new Dictionary<string, string>
        {
            [Root + "/Calc.Tests/StrykerOutput/2026-11-04.23-42-53/reports/mutation-report.json"] = report,
        }));

    /// <summary>A mutation report of Equality mutants at column 5 of files under the root.</summary>
    /// <param name="mutants">The mutants, by file relative to the root.</param>
    private static string Report(IEnumerable<(string File, int Line, string Status, string? Replacement)> mutants)
    {
        var sb = new StringBuilder("{\"schemaVersion\":\"2\",\"files\":{");
        var files = mutants.GroupBy(m => m.File).Select(file =>
            $"\"{Root}/{file.Key}\":{{\"mutants\":[" + string.Join(',', file.Select(m =>
                "{\"mutatorName\":\"Equality mutation\"," +
                (m.Replacement is null ? "" : $"\"replacement\":{JsonSerializer.Serialize(m.Replacement)},") +
                $"\"location\":{{\"start\":{{\"line\":{m.Line},\"column\":5}}}},\"status\":\"{m.Status}\"}}")) + "]}");
        return sb.AppendJoin(',', files).Append("}}").ToString();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetStrykerFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Serves fixed file contents and records what the filter asked for.</summary>
    /// <param name="texts">The content of each readable path.</param>
    /// <param name="newest">What every search for the newest report finds.</param>
    private sealed class FakeFiles(Dictionary<string, string> texts, string? newest = null) : IArtifactFileReader
    {
        public List<string> Reads { get; } = [];

        public List<(string Directory, string FileName, DateTimeOffset NotBeforeUtc)> Searches { get; } = [];

        public string? TryReadText(string path)
        {
            Reads.Add(path);
            return texts.GetValueOrDefault(path);
        }

        public string? FindNewest(string directory, string fileName, DateTimeOffset notBeforeUtc)
        {
            Searches.Add((directory, fileName, notBeforeUtc));
            return newest;
        }
    }
}

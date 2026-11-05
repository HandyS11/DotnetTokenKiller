using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Benchmarks.Corpus;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class LocalizedTestOutputTests
{
    [Fact]
    public void Detect_French_VsTestFail()
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load("dotnet_test_localized_fr_fail.txt")).Should().Be("fr");
    }

    [Fact]
    public void Detect_German()
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load("dotnet_test_localized_de_fail.txt")).Should().Be("de");
    }

    [Fact]
    public void Detect_Japanese()
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load("dotnet_test_localized_ja_fail.txt")).Should().Be("ja");
    }

    [Fact]
    public void Detect_Mtp_French()
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load("dotnet_test_mtp_localized_fr_fail.txt")).Should().Be("fr");
    }

    [Theory]
    [InlineData("dotnet_test_localized_fr_pass.txt", "fr")]
    [InlineData("dotnet_test_mtp_localized_de_pass.txt", "de")]
    [InlineData("dotnet_test_mtp_localized_ja_fail.txt", "ja")]
    public void Detect_OtherLocalizedRuns(string fixture, string culture)
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load(fixture)).Should().Be(culture);
    }

    [Fact]
    public void Detect_NormalLoggerVerdict()
    {
        // The verdict block of a real `--logger "console;verbosity=normal"` run under DOTNET_CLI_UI_LANGUAGE=fr.
        const string output = "\nÉchec de la série de tests.\nNombre total de tests\u00a0: 4\n     Réussi(s)\u00a0: 3\n";

        LocalizedTestOutput.Detect(output).Should().Be("fr");
    }

    [Fact]
    public void Detect_Crlf()
    {
        var fixture = FixtureCorpus.Load("dotnet_test_localized_ja_fail.txt");

        LocalizedTestOutput.Detect(fixture.ReplaceLineEndings("\r\n")).Should().Be("ja");
    }

    [Fact]
    public void Detect_NoBreakSpacesTypedAsSpaces()
    {
        // French puts a no-break space before a colon; a copy that turned it into a plain space still reads as French.
        var fixture = FixtureCorpus.Load("dotnet_test_localized_fr_pass.txt").Replace('\u00a0', ' ');

        LocalizedTestOutput.Detect(fixture).Should().Be("fr");
    }

    [Fact]
    public void Detect_English_Null()
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load("dotnet_test_failures.txt")).Should().BeNull();
    }

    /// <summary>Every captured output of every command except the localized runs: all English.</summary>
    public static TheoryData<string> EnglishFixtures { get; } =
        [.. FixtureCorpus.Names.Where(name => !name.Contains("localized", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(EnglishFixtures))]
    public void Detect_EveryEnglishFixture_Null(string fixture)
    {
        LocalizedTestOutput.Detect(FixtureCorpus.Load(fixture)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Série de tests pour /test/project/root/SampleApp.Tests.dll (.NETCoreApp,Version=v10.0)\n")]
    [InlineData("  Échoué SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [2 ms]\n")]
    [InlineData("log: Échec de la série de tests.\n")]
    public void Detect_NoSummaryLine_Null(string output)
    {
        LocalizedTestOutput.Detect(output).Should().BeNull();
    }
}

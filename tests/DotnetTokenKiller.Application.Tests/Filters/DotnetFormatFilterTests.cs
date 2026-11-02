using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Tests.Helpers;
using DotnetTokenKiller.Domain.Text;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetFormatFilterTests
{
    private readonly DotnetFormatFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_FilesFormattedFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_ViolationsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_VerboseNothingFixture_SavingsAtLeast90Percent()
    {
        // Represents `dotnet format --verbosity diagnostic` when nothing needs formatting.
        // Default-verbosity format produces 0 bytes (covered by Apply_EmptyInput_ReturnsNothingToFormat).
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "format filter should achieve ≥90% savings on verbose output");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_DoesNotContainLoadingConfiguration()
    {
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        _sut.Apply(fixture, exitCode: 0).Should().NotContain("Loading configuration");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_DoesNotContainFormatComplete()
    {
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        _sut.Apply(fixture, exitCode: 0).Should().NotContain("Format complete");
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNothingToFormat()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNothingToFormat()
    {
        _sut.Apply(null!, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_AlreadyStrippedAnsiOnlyInput_ReturnsNothingToFormat()
    {
        // The filter no longer strips ANSI itself — FilteredOutputPipeline strips once, before any
        // filter runs — so this pins that ANSI-only text, once stripped down to a blank line,
        // still hits the "nothing to format" heuristic rather than being parsed as a violation.
        var stripped = AnsiStrip.Strip("\x1b[32m\x1b[0m\n");

        _sut.Apply(stripped, exitCode: 0).Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_VerboseNothingFixture_ReturnsNothingToFormatWithElapsed()
    {
        // Verbose output contains "Format complete in 3000ms." — the filter extracts the timing.
        var fixture = LoadFixture("dotnet_format_verbose_nothing_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Be("✓ dotnet format (nothing to format, 3.00s)\n");
    }

    [Fact]
    public void Apply_FilesFormattedFixture_ReturnsFormattedCount()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Contain("3 files formatted");
    }

    [Fact]
    public void Apply_FilesFormattedFixture_IncludesElapsed()
    {
        var fixture = LoadFixture("dotnet_format_files_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        result.Should().Contain("2.00s");
    }

    [Fact]
    public void Apply_SingleFileFormatted_UsesSingular()
    {
        const string input = """
                               Formatted code file '/test/project/root/src/Foo.cs'.
                             Format complete in 1000ms.
                             """;
        _sut.Apply(input, exitCode: 0).Should().Be("✓ dotnet format (1 file formatted, 1.00s)\n");
    }

    [Fact]
    public void Apply_ViolationsFixture_ShowsViolationCount()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        result.Should().EndWith("✗ dotnet format: 2 violations in 2 files\n");
    }

    [Fact]
    public void Apply_ViolationsFixture_ShowsShortPaths()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        result.Should().NotContain("/test/project/root/");
        result.Should().Contain("src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs");
    }

    [Fact]
    public void Apply_ViolationsFixture_DoesNotContainFormatComplete()
    {
        var fixture = LoadFixture("dotnet_format_violations_raw.txt");
        _sut.Apply(fixture, exitCode: 1).Should().NotContain("Format complete");
    }

    [Fact]
    public void Apply_SingleViolation_UsesSingular()
    {
        const string input =
            "  /test/project/root/src/Foo.cs(1,1): error whitespace: Fix whitespace formatting.\nFormat complete in 500ms.";
        var result = _sut.Apply(input, exitCode: 1);
        result.Should().EndWith("✗ dotnet format: 1 violation in 1 file\n");
    }

    [Fact]
    public void Apply_ManyFilesWithOneViolationEach_ListsEveryFileAndNeverTruncates()
    {
        // Grouping bounds the size now, so the old cap of ten lines is gone: an agent told "... and 15 more"
        // had to rerun the command and read the raw output to find them.
        var lines = string.Join("\n",
            Enumerable.Range(1, 25).Select(i =>
                $"  /test/project/root/src/File{i:D2}.cs(1,1): error WHITESPACE: Fix whitespace formatting. Insert '\\n'."));

        var result = _sut.Apply(lines, exitCode: 2).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        result.Should().HaveCount(26);
        result.Should().NotContain(line => line.Contains("more violation", StringComparison.Ordinal));
        result[0].Should().Be("src/File01.cs: WHITESPACE (1:1 insert newline)");
        result[24].Should().Be("src/File25.cs: WHITESPACE (1:1 insert newline)");
        result[25].Should().Be("✗ dotnet format: 25 violations in 25 files");
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        var filter = new DotnetFormatFilter();
        var result = filter.Apply(string.Empty, exitCode: 0);
        result.Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_NoElapsedInOutput_OmitsElapsedFromSummary()
    {
        const string input = "  Formatted code file '/test/project/root/src/Foo.cs'.";
        var result = _sut.Apply(input, exitCode: 0);
        result.Should().Be("✓ dotnet format (1 file formatted)\n");
    }

    [Fact]
    public void Apply_NonZeroExitWithNoParsedViolations_ReturnsEmpty()
    {
        // Crashed process / unparseable output: no violation lines were parsed, but the exit code
        // is non-zero. Returning "0 violations" here would look like a clean success and would
        // prevent FilteredRunUseCase's raw-tail fallback from ever firing.
        const string input = "Segmentation fault (core dumped)";

        var result = _sut.Apply(input, exitCode: 139);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ViolationWithFormatCompleteLine_ReportsViolation()
    {
        // Invariant: "nothing to format" is only valid when no violation was parsed.
        const string input =
            "/test/project/root/src/App.cs(1,1): error WHITESPACE: Fix whitespace formatting.\n" +
            "Format complete in 2345ms.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be(
            "src/App.cs: WHITESPACE (1:1): Fix whitespace formatting.\n⚠ dotnet format: 1 violation in 1 file\n");
        result.Should().NotContain("nothing to format");
    }

    [Fact]
    public void Apply_UnparseableElapsedValue_OmitsElapsedRatherThanReportingGarbage()
    {
        // The pattern accepts any run of digits and dots, so a value it cannot parse as a number
        // reaches the summary builder. Dropping the elapsed part is the only honest option.
        const string input = "Format complete in 1.2.3ms.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet format (nothing to format)\n");
    }

    [Fact]
    public void Apply_FrenchCulture_RendersTheElapsedTimeWithADecimalPoint()
    {
        // "0,02s" cost a French-locale user about 670 summaries: the number followed the system culture.
        using var _ = new CultureScope("fr-FR");

        var result = _sut.Apply("Format complete in 20ms.", exitCode: 0);

        result.Should().Be("✓ dotnet format (nothing to format, 0.02s)\n");
    }

    [Fact]
    public void Apply_ElapsedWithNoFormattedFiles_ReportsNothingToFormatWithElapsed()
    {
        const string input = "Loading workspace.\nFormat complete in 2345ms.";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet format (nothing to format, 2.35s)\n");
    }

    [Fact]
    public void Apply_WhitespaceOnlyOutputOnFailure_ReturnsEmpty()
    {
        // A failing run that printed nothing must not be dressed up as a success — blank output
        // lets FilteredRunUseCase's raw-tail fallback show whatever really happened.
        var result = _sut.Apply("   \n\t\n", exitCode: 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_DefaultRootPath_ShortensViolationPathsAgainstTheWorkingDirectory()
    {
        // The no-argument constructor is what DI uses; it falls back to the current directory.
        var filter = new DotnetFormatFilter();
        var input = $"{Path.Combine(Environment.CurrentDirectory, "src", "App.cs")}"
                    + "(3,5): error WHITESPACE: Fix whitespace formatting.";

        var result = filter.Apply(input, exitCode: 1);

        result.Should().EndWith("✗ dotnet format: 1 violation in 1 file\n");
        result.Should().NotContain(Environment.CurrentDirectory);
    }

    [Fact]
    public Task Apply_WhitespaceManyFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_format_whitespace_many.txt");
        var result = _sut.Apply(fixture, exitCode: 2);
        return Verify(result);
    }

    [Fact]
    public void Apply_WhitespaceManyFixture_DropsTheProjectSuffixAndTheAbsolutePath()
    {
        var fixture = LoadFixture("dotnet_format_whitespace_many.txt");

        var result = _sut.Apply(fixture, exitCode: 2);

        result.Should().NotContain(".csproj]");
        result.Should().NotContain("/test/project/root");
        result.Should().NotContain("characters with");
    }

    [Fact]
    public void Apply_WhitespaceManyFixture_LeavesTheVerboseLinesAroundTheViolationsOut()
    {
        // Captured with --verbosity normal, which prints a header, a "Formatted code file" line per file (even
        // though --verify-no-changes changes none) and "Format complete" around the violations.
        var fixture = LoadFixture("dotnet_format_whitespace_many.txt");

        var result = _sut.Apply(fixture, exitCode: 2);

        result.Should().NotContain("Formatted code file");
        result.Should().NotContain("Format complete");
        result.Should().NotContain("Formatting code files");
    }

    [Fact]
    public void Apply_WhitespaceManyFixture_ReadsTheVerdictOnTheLastLineAlone()
    {
        var fixture = LoadFixture("dotnet_format_whitespace_many.txt");

        var lines = _sut.Apply(fixture, exitCode: 2).TrimEnd('\n').Split('\n');

        lines[^1].Should().Be("✗ dotnet format: 21 violations in 4 files");
        lines.Count(line => line.StartsWith("✗ ", StringComparison.Ordinal)).Should().Be(1);
        lines.Count(line => line.StartsWith("dotnet format", StringComparison.Ordinal)).Should().Be(0);
    }

    [Fact]
    public void Apply_WhitespaceManyFixture_PutsEachFileAndCodeOnOneLine()
    {
        var fixture = LoadFixture("dotnet_format_whitespace_many.txt");

        var lines = _sut.Apply(fixture, exitCode: 2).TrimEnd('\n').Split('\n');

        // 17 whitespace violations over 3 files, 4 other codes: one line per file and code, the status last.
        lines.Should().HaveCount(7 + 1);
        lines.Should().ContainSingle(line => line.StartsWith("Fmt/F.cs: WHITESPACE ×10 (", StringComparison.Ordinal));
        lines.Should().ContainSingle(line => line.StartsWith("Fmt/A.cs: WHITESPACE ×5 (", StringComparison.Ordinal));
        lines.Should().ContainSingle(line => line.StartsWith("Fmt/B.cs: WHITESPACE ×2 (", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_ThreeViolationsInOneFile_RenderOnOneLineWithTheirReplacements()
    {
        const string input =
            "/test/project/root/src/UserAccessStore.cs(21,45): error WHITESPACE: Fix whitespace formatting. " +
            "Replace 1 characters with '\\n\\s\\s\\s\\s'. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/UserAccessStore.cs(21,80): error WHITESPACE: Fix whitespace formatting. " +
            "Replace 1 characters with '\\n\\s\\s\\s\\s\\s\\s\\s\\s'. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/UserAccessStore.cs(21,103): error WHITESPACE: Fix whitespace formatting. " +
            "Replace 1 characters with '\\n'. [/test/project/root/src/App.csproj]\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "src/UserAccessStore.cs: WHITESPACE ×3 (21:45 newline + 4 spaces, 21:80 newline + 8 spaces, 21:103 newline)\n" +
            "✗ dotnet format: 3 violations in 1 file\n");
    }

    [Fact]
    public void Apply_SingleViolation_OmitsTheCountAndSaysWhatToDo()
    {
        const string input =
            "/test/project/root/src/App.cs(3,5): error WHITESPACE: Fix whitespace formatting. Insert '\\s'. " +
            "[/test/project/root/src/App.csproj]";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be("src/App.cs: WHITESPACE (3:5 insert 1 space)\n✗ dotnet format: 1 violation in 1 file\n");
    }

    [Fact]
    public void Apply_PositionsOfAGroup_AreSortedByLineThenColumnNumerically()
    {
        // Compared as text, "100" sorts before "20" and "9" after both.
        const string input =
            "/test/project/root/src/App.cs(100,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/App.cs(9,30): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/App.cs(20,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/App.cs(9,4): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().StartWith(
            "src/App.cs: WHITESPACE ×4 (9:4 insert 1 space, 9:30 insert 1 space, 20:1 insert 1 space, 100:1 insert 1 space)\n");
    }

    [Fact]
    public void Apply_TheSameViolationReportedTwice_CountsItOnce()
    {
        // `dotnet format` really does report some analyzer diagnostics twice (see samples/examples/FORMAT.md).
        const string line =
            "/test/project/root/src/App.cs(10,9): warning IDE0007: use 'var' instead of explicit type " +
            "[/test/project/root/src/App.csproj]\n";

        var result = _sut.Apply(line + line, exitCode: 2);

        result.Should().Be(
            "src/App.cs: IDE0007 (10:9): use 'var' instead of explicit type\n✗ dotnet format: 1 violation in 1 file\n");
    }

    [Fact]
    public void Apply_ViolationsWithoutAReplacement_PrintTheirMessageOnceAfterThePositions()
    {
        const string input =
            "/test/project/root/src/App.cs(2,1): warning IDE0005: Using directive is unnecessary. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/App.cs(1,1): warning IDE0005: Using directive is unnecessary. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/App.cs(1,1): error IMPORTS: Fix imports ordering. [/test/project/root/src/App.csproj]\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "src/App.cs: IDE0005 ×2 (1:1, 2:1): Using directive is unnecessary.\n" +
            "src/App.cs: IMPORTS (1:1): Fix imports ordering.\n" +
            "✗ dotnet format: 3 violations in 1 file\n");
    }

    [Fact]
    public void Apply_SameCodeWithDifferentMessagesInOneFile_KeepsEachMessageOnItsOwnLine()
    {
        // S1481 names the variable: folding these into one line would drop which variable each position holds.
        const string input =
            "/test/project/root/src/App.cs(9,13): warning S1481: Remove the unused local variable 'x'. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/App.cs(12,13): warning S1481: Remove the unused local variable 'y'. [/test/project/root/src/App.csproj]\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "src/App.cs: S1481 (9:13): Remove the unused local variable 'x'.\n" +
            "src/App.cs: S1481 (12:13): Remove the unused local variable 'y'.\n" +
            "✗ dotnet format: 2 violations in 1 file\n");
    }

    [Fact]
    public void Apply_FilesAndCodes_AreOrderedByCountThenByNameWhateverOrderTheyArrivedIn()
    {
        const string input =
            "/test/project/root/src/Zeta.cs(1,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/Beta.cs(1,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/Alpha.cs(1,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/Alpha.cs(2,1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'.\n" +
            "/test/project/root/src/Beta.cs(5,1): warning IDE0161: Convert to file-scoped namespace\n" +
            "/test/project/root/src/Beta.cs(6,1): warning CA1822: Mark members as static\n" +
            "/test/project/root/src/Beta.cs(7,1): warning CA1822: Mark members as static\n";

        var result = _sut.Apply(input, exitCode: 2);

        // Beta has 4 (most), Alpha 2, Zeta 1; within Beta the code seen twice leads, the single codes follow by name.
        result.Should().Be(
            "src/Beta.cs: CA1822 ×2 (6:1, 7:1): Mark members as static\n" +
            "src/Beta.cs: IDE0161 (5:1): Convert to file-scoped namespace\n" +
            "src/Beta.cs: WHITESPACE (1:1 insert 1 space)\n" +
            "src/Alpha.cs: WHITESPACE ×2 (1:1 insert 1 space, 2:1 insert 1 space)\n" +
            "src/Zeta.cs: WHITESPACE (1:1 insert 1 space)\n" +
            "✗ dotnet format: 7 violations in 3 files\n");
    }

    [Fact]
    public void Apply_CrlfLineEndings_ReadTheSameAsLf()
    {
        // Windows prints each violation line with a carriage return, which must not end up in a message or a path.
        const string lf =
            "/test/project/root/src/App.cs(3,5): error WHITESPACE: Fix whitespace formatting. Insert '\\s'. [/test/project/root/src/App.csproj]\n" +
            "/test/project/root/src/App.cs(4,1): warning IDE0005: Using directive is unnecessary. [/test/project/root/src/App.csproj]\n";

        var result = _sut.Apply(lf.ReplaceLineEndings("\r\n"), exitCode: 2);

        result.Should().Be(_sut.Apply(lf, exitCode: 2));
        result.Should().NotContain("\r");
    }

    [Fact]
    public void Apply_InsertOfNothing_HasNoChangeToNameSoItsMessageStays()
    {
        const string input =
            "/test/project/root/src/App.cs(3,5): error WHITESPACE: Fix whitespace formatting. Insert ''. " +
            "[/test/project/root/src/App.csproj]";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "src/App.cs: WHITESPACE (3:5): Fix whitespace formatting.\n✗ dotnet format: 1 violation in 1 file\n");
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void Apply_PositionsOfAGroup_AreCappedAtTwentyWithTheRestCounted(int violationCount, bool capped)
    {
        // Reported in reverse, so "the first twenty" must be the first twenty in line order, not in report order.
        var input = string.Join("\n",
            Enumerable.Range(1, violationCount).Reverse().Select(i =>
                $"/test/project/root/src/App.cs({i},1): error WHITESPACE: Fix whitespace formatting. Insert '\\s'."));

        var result = _sut.Apply(input, exitCode: 2);

        var line = result.Split('\n')[0];
        var shown = string.Join(", ", Enumerable.Range(1, 20).Select(i => $"{i}:1 insert 1 space"));
        line.Should().Be(capped
            ? $"src/App.cs: WHITESPACE ×{violationCount} ({shown} … +{violationCount - 20} more)"
            : $"src/App.cs: WHITESPACE ×{violationCount} ({shown})");
        result.Should().EndWith($"✗ dotnet format: {violationCount} violations in 1 file\n");
    }

    [Fact]
    public void Apply_FiveThousandViolationsOverTwentyFiles_StaysSmall()
    {
        // The first --verify-no-changes on an unformatted repository: 250 positions in each of 20 files.
        var input = string.Join("\n",
            Enumerable.Range(1, 20).SelectMany(file => Enumerable.Range(1, 250).Select(i =>
                $"/test/project/root/src/File{file:D2}.cs({i},{(i % 7) + 1}): error WHITESPACE: " +
                "Fix whitespace formatting. Replace 1 characters with '\\n\\s\\s\\s\\s'. " +
                "[/test/project/root/src/App.csproj]")));

        var result = _sut.Apply(input, exitCode: 2);

        var lines = result.TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(21);
        lines[^1].Should().Be("✗ dotnet format: 5000 violations in 20 files");
        lines[0].Should().Contain("WHITESPACE ×250 (").And.EndWith(" … +230 more)");
        result.Length.Should().BeLessThan(12_000);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(41)]
    public void Apply_GroupLines_AreCappedAtFortyWithTheRestCounted(int fileCount)
    {
        var input = string.Join("\n",
            Enumerable.Range(1, fileCount).Select(i =>
                $"/test/project/root/src/File{i:D2}.cs(1,1): error WHITESPACE: Fix whitespace formatting. Insert '\\n'."));

        var lines = _sut.Apply(input, exitCode: 2).TrimEnd('\n').Split('\n');

        var status = $"✗ dotnet format: {fileCount} violations in {fileCount} files";
        if (fileCount == 40)
        {
            lines.Should().HaveCount(41);
            lines[^1].Should().Be(status);
            lines.Should().NotContain(line => line.StartsWith('…'));
        }
        else
        {
            lines.Should().HaveCount(42);
            lines[39].Should().Be("src/File40.cs: WHITESPACE (1:1 insert newline)");
            lines[40].Should().Be("… 1 more group in 1 file (dtk log)");
            lines[41].Should().Be(status);
        }
    }

    [Fact]
    public void Apply_HiddenGroups_AreCountedWithTheFilesTheyBelongTo()
    {
        // 45 codes in one file (shown first, being the largest) and 1 in another: 6 groups hide, in 2 files.
        var codes = Enumerable.Range(1, 45).Select(i =>
            $"/test/project/root/src/Big.cs({i},1): warning IDE{i:D4}: Message {i}\n");
        var input = string.Concat(codes) +
                    "/test/project/root/src/Small.cs(1,1): warning IDE0001: Message 1\n";

        var lines = _sut.Apply(input, exitCode: 2).TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(42);
        lines[40].Should().Be("… 6 more groups in 2 files (dtk log)");
        lines[41].Should().Be("✗ dotnet format: 46 violations in 2 files");
    }

    [Fact]
    public void Apply_BracketsInsideTheMessage_SurviveWhileTheProjectSuffixGoes()
    {
        const string input =
            "/test/project/root/src/App.cs(4,9): warning CA1861: Prefer 'static readonly' fields over constant array arguments " +
            "for 'int[]' [/test/project/root/src/App.csproj]";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "src/App.cs: CA1861 (4:9): Prefer 'static readonly' fields over constant array arguments for 'int[]'\n" +
            "✗ dotnet format: 1 violation in 1 file\n");
    }

    [Fact]
    public void Apply_TabsAndCrlf_AreSummarisedLikeSpaces()
    {
        // Real lines from a tab-indented, CRLF project.
        const string input =
            "/test/project/root/Fmt/T.cs(4,2): error WHITESPACE: Fix whitespace formatting. Replace 5 characters with '\\r\\n\\t'. [/test/project/root/Fmt/Fmt.csproj]\n" +
            "/test/project/root/Fmt/T.cs(6,6): error WHITESPACE: Fix whitespace formatting. Replace 9 characters with '\\r\\n\\t\\t'. [/test/project/root/Fmt/Fmt.csproj]\n" +
            "/test/project/root/Fmt/T.cs(1,15): error ENDOFLINE: Fix end of line marker. Replace 1 characters with '\\r\\n'. [/test/project/root/Fmt/Fmt.csproj]\n" +
            "/test/project/root/Fmt/U.cs(1,1): error CHARSET: Fix file encoding. [/test/project/root/Fmt/Fmt.csproj]\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().Be(
            "Fmt/T.cs: WHITESPACE ×2 (4:2 newline + tab, 6:6 newline + 2 tabs)\n" +
            "Fmt/T.cs: ENDOFLINE (1:15 newline)\n" +
            "Fmt/U.cs: CHARSET (1:1): Fix file encoding.\n" +
            "✗ dotnet format: 4 violations in 2 files\n");
    }

    [Fact]
    public void Apply_DeletedCharacters_AreCountedInTheirOwnWords()
    {
        const string input =
            "/test/project/root/src/App.cs(11,9): error WHITESPACE: Fix whitespace formatting. Delete 2 characters.\n" +
            "/test/project/root/src/App.cs(12,18): error WHITESPACE: Fix whitespace formatting. Delete 1 characters.\n";

        var result = _sut.Apply(input, exitCode: 2);

        result.Should().StartWith("src/App.cs: WHITESPACE ×2 (11:9 delete 2 chars, 12:18 delete 1 char)\n");
    }

    [Theory]
    [InlineData("\\n\\s\\s\\s\\s", "newline + 4 spaces")]
    [InlineData("\\n", "newline")]
    [InlineData("\\r\\n", "newline")]
    [InlineData("\\r\\n\\t\\t", "newline + 2 tabs")]
    [InlineData("\\t", "tab")]
    [InlineData("\\t\\t\\t", "3 tabs")]
    [InlineData("\\s", "1 space")]
    [InlineData("\\s\\s\\s", "3 spaces")]
    [InlineData("\\s\\n", "1 space + newline")]
    [InlineData("\\n\\n\\s\\s", "2 newlines + 2 spaces")]
    [InlineData("\\n\\s\\s;", "newline + 2 spaces + ';'")]
    [InlineData("\\r", "'\\r'")]
    [InlineData("", "")]
    [InlineData("\\", "'\\'")]
    [InlineData("\\\\", "'\\\\'")]
    [InlineData("\\\\s", "'\\' + 1 space")]
    [InlineData("'", "'''")]
    [InlineData("\\n'", "newline + '''")]
    [InlineData("é😀", "'é😀'")]
    [InlineData("\\néx😀\\s", "newline + 'éx😀' + 1 space")]
    [InlineData("\\n\\r\\n\\t\\s", "2 newlines + tab + 1 space")]
    public void SummarizeReplacement_NamesWhatTheEscapedTextIs(string escaped, string expected)
    {
        DotnetFormatFilter.SummarizeReplacement(escaped).Should().Be(expected);
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetFormatFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

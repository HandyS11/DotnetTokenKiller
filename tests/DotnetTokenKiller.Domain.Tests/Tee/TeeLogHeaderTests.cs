using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests.Tee;

public sealed class TeeLogHeaderTests
{
    private static TeeLogHeader Sample() => new(
        "dotnet build MyApp.slnx",
        "/home/user/projects/MyApp",
        1,
        RunSource.Run,
        new DateTimeOffset(2026, 7, 28, 9, 14, 2, TimeSpan.Zero));

    [Fact]
    public void Render_ThenTryParse_RoundTrips()
    {
        var original = Sample();

        var parsed = TeeLogHeader.TryParse(original.Render(), out var header);

        parsed.Should().BeTrue();
        header.CommandLine.Should().Be(original.CommandLine);
        header.ProjectPath.Should().Be(original.ProjectPath);
        header.ExitCode.Should().Be(original.ExitCode);
        header.Source.Should().Be(original.Source);
        header.TimestampUtc.Should().Be(original.TimestampUtc);
    }

    [Fact]
    public void Render_StartsWithVersionLine_AndEndsWithDelimiter()
    {
        var rendered = Sample().Render();

        rendered.Should().StartWith("# dtk-log v2\n");
        // The delimiter must be the last line so the body starts immediately after it.
        rendered.Should().EndWith("---\n");
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForLegacyFileWithNoHeader()
    {
        // A file written by dtk <= 0.6.0 is raw output with no header at all.
        var parsed = TeeLogHeader.TryParse("Build succeeded.\n  0 Warning(s)\n", out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenBodyMerelyContainsADelimiterLine()
    {
        // Guards the reverse mistake: raw output containing "---" must not be read as a header.
        var parsed = TeeLogHeader.TryParse("some output\n---\nmore output\n", out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenAFieldIsMissing()
    {
        const string text = "# dtk-log v1\n# command: dotnet build\n# cwd: /tmp\n# exit: 0\n---\n";

        var parsed = TeeLogHeader.TryParse(text, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenTheDelimiterIsMissing()
    {
        // This is what a truncated head-read looks like; it must not yield a half-built header.
        const string text = "# dtk-log v1\n# command: dotnet build\n# cwd: /tmp\n# exit: 0\n# source: Run\n";

        var parsed = TeeLogHeader.TryParse(text, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenExitCodeIsNotAnInteger()
    {
        const string text = "# dtk-log v1\n# command: dotnet build\n# cwd: /tmp\n# exit: banana\n"
                            + "# source: Run\n# utc: 2026-07-28T09:14:02.0000000+00:00\n---\n";

        var parsed = TeeLogHeader.TryParse(text, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryParse_HandlesCrLfLineEndings()
    {
        var text = Sample().Render().Replace("\n", "\r\n", StringComparison.Ordinal);

        var parsed = TeeLogHeader.TryParse(text, out var header);

        parsed.Should().BeTrue();
        header.ProjectPath.Should().Be("/home/user/projects/MyApp");
    }

    [Fact]
    public void TryParse_HandlesNonAsciiPaths()
    {
        var original = Sample() with { ProjectPath = "/home/user/projets/Café/Ünicode" };

        TeeLogHeader.TryParse(original.Render(), out var header).Should().BeTrue();

        header.ProjectPath.Should().Be("/home/user/projets/Café/Ünicode");
    }

    [Fact]
    public void Render_ReplacesNewlinesInValues_SoTheGrammarStaysLineBased()
    {
        // POSIX permits a newline in a path. Left as-is it would split the header mid-field and
        // corrupt every following line, so it is flattened to a space on write.
        var header = Sample() with { ProjectPath = "/home/user/we\nird" };

        var rendered = header.Render();

        rendered.Should().Contain("# cwd: /home/user/we ird\n");
        TeeLogHeader.TryParse(rendered, out _).Should().BeTrue();
    }

    [Fact]
    public void StripHeader_ReturnsBodyOnly_WhenHeaderPresent()
    {
        var text = Sample().Render() + "line one\nline two\n";

        TeeLogHeader.StripHeader(text).Should().Be("line one\nline two\n");
    }

    [Fact]
    public void StripHeader_ReturnsWholeText_WhenNoHeaderPresent()
    {
        const string legacy = "line one\nline two\n";

        TeeLogHeader.StripHeader(legacy).Should().Be(legacy);
    }

    [Fact]
    public void StripHeader_ReturnsEmpty_WhenHeaderIsAllThereIs()
    {
        TeeLogHeader.StripHeader(Sample().Render()).Should().BeEmpty();
    }

    [Fact]
    public void RenderStatusAndExit_ProducesTheSameLength_ForRunningAndAnyExitCode()
    {
        // The finalize path overwrites this region in place at a fixed byte offset. If the
        // running and complete forms differ in length, that overwrite corrupts the delimiter
        // and every log from a finished run becomes unparseable.
        var running = TeeLogHeader.RenderStatusAndExit(null);
        var zero = TeeLogHeader.RenderStatusAndExit(0);
        var widest = TeeLogHeader.RenderStatusAndExit(int.MinValue);

        zero.Length.Should().Be(running.Length);
        widest.Length.Should().Be(running.Length);
    }

    [Fact]
    public void Render_ThenTryParse_RoundTripsACompletedRun()
    {
        var original = new TeeLogHeader(
            "dotnet build MyApp.slnx",
            "/home/user/projects/MyApp",
            1,
            RunSource.Run,
            new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

        TeeLogHeader.TryParse(original.Render(), out var parsed).Should().BeTrue();

        parsed.Should().Be(original);
        parsed.Status.Should().Be(TeeLogStatus.Complete);
    }

    [Fact]
    public void Render_ThenTryParse_RoundTripsARunningRun()
    {
        var original = new TeeLogHeader(
            "dotnet build MyApp.slnx",
            "/home/user/projects/MyApp",
            null,
            RunSource.Run,
            new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

        TeeLogHeader.TryParse(original.Render(), out var parsed).Should().BeTrue();

        parsed.ExitCode.Should().BeNull();
        parsed.Status.Should().Be(TeeLogStatus.Running);
    }

    [Fact]
    public void TryParse_ReadsAV1Header_AsComplete()
    {
        // Logs written before this change must keep listing; they are complete by definition,
        // because v1 could only be written after the process exited.
        const string v1 =
            "# dtk-log v1\n"
            + "# command: dotnet build MyApp.slnx\n"
            + "# cwd: /home/user/projects/MyApp\n"
            + "# exit: 1\n"
            + "# source: Run\n"
            + "# utc: 2026-07-28T09:14:02.0000000+00:00\n"
            + "---\n"
            + "body\n";

        TeeLogHeader.TryParse(v1, out var parsed).Should().BeTrue();

        parsed.ExitCode.Should().Be(1);
        parsed.Status.Should().Be(TeeLogStatus.Complete);
        parsed.CommandLine.Should().Be("dotnet build MyApp.slnx");
    }

    [Fact]
    public void TryParse_RejectsAHeaderWhoseStatusAndExitDisagree()
    {
        // status and exit are two spellings of one fact. A file where they disagree is corrupt,
        // not merely unfinished, and must not be read as either.
        const string contradictory =
            "# dtk-log v2\n"
            + "# command: dotnet build MyApp.slnx\n"
            + "# cwd: /home/user/projects/MyApp\n"
            + "# source: Run\n"
            + "# utc: 2026-07-28T09:14:02.0000000+00:00\n"
            + "# status: running \n"
            + "# exit:   0          \n"
            + "---\n";

        TeeLogHeader.TryParse(contradictory, out _).Should().BeFalse();
    }

    [Fact]
    public void StripHeader_RemovesAV2Header()
    {
        var header = new TeeLogHeader(
            "dotnet build MyApp.slnx",
            "/home/user/projects/MyApp",
            0,
            RunSource.Run,
            new DateTimeOffset(2026, 7, 29, 9, 14, 2, TimeSpan.Zero));

        TeeLogHeader.StripHeader(header.Render() + "line one\nline two\n")
            .Should().Be("line one\nline two\n");
    }
}

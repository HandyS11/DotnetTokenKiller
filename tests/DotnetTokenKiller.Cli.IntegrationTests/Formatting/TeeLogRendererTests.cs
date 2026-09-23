using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Formatting;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Formatting;

public sealed class TeeLogRendererTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 28, 9, 14, 0, TimeSpan.Zero);

    private static TeeLogEntry Entry(int? exitCode = 0) =>
        new("/tee/build.log",
            new TeeLogHeader("dotnet build MyApp.slnx", "/home/user/proj", exitCode, RunSource.Run, Timestamp),
            2048,
            Timestamp,
            "build");

    private static async Task<string> RenderAsync(LogView view)
    {
        var writer = new StringWriter();
        await TeeLogRenderer.RenderViewAsync(writer, view, CancellationToken.None);
        return writer.ToString();
    }

    [Fact]
    public async Task RenderViewAsync_WarnsAboutTruncation_WhenTheBodysLastLineIsTheMarker()
    {
        // Detection is body-based, not a header field (see TeeTruncationMarker's remarks): a plain
        // Entry() with an ordinary header is enough — the marker being the body's true last line is
        // the only signal. LogView.Body never carries a trailing line feed (LogViewUseCase.ViewAsync
        // builds it with string.Join('\n', window)), so there is none after the marker here either.
        var view = new LogView(Entry(), "body\n[dtk: output truncated at 100 bytes]", 2, 2);

        var output = await RenderAsync(view);

        output.Should().Contain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_WhenTheBodyNeverHitTheCap()
    {
        var view = new LogView(Entry(), "body\nmore body", 2, 2);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_ForAnEmptyBody()
    {
        var view = new LogView(Entry(), string.Empty, 0, 0);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_WhenTheMarkerTextIsNotTheLastLine()
    {
        // The marker only means anything as the literal last line FileTeeSession ever appends; the
        // same text appearing mid-body (echoed by the command itself, say) is not truncation.
        var view = new LogView(Entry(), "[dtk: output truncated at 100 bytes]\nmore output after it", 2, 2);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_WhenTheHeaderIsMissingAndTheBodyIsNotTruncated()
    {
        // A legacy log (no header) must not falsely claim truncation just because there is no header
        // to say otherwise — detection is body-based, so a plain body still correctly reports "not
        // truncated" with no header at all. The positive counterpart lives in the test below.
        var entry = Entry() with { Header = null };
        var view = new LogView(entry, "body", 1, 1);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_WarnsAboutTruncation_WhenTheHeaderIsMissing_ButTheMarkerIsPresent()
    {
        // The marker's whole point is that detection needs no header support at all, old or new: a
        // legacy log (no header) whose body still ends with the marker line must warn exactly like
        // one with an ordinary header does.
        var entry = Entry() with { Header = null };
        var view = new LogView(entry, "body\n[dtk: output truncated at 100 bytes]", 2, 2);

        var output = await RenderAsync(view);

        output.Should().Contain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_ShowsBothWarnings_WhenARunWasKilledAfterItsOutputWasAlreadyTruncated()
    {
        var entry = Entry(exitCode: null);
        var view = new LogView(entry, "body\n[dtk: output truncated at 100 bytes]", 2, 2);

        var output = await RenderAsync(view);

        output.Should().Contain("run did not finish");
        output.Should().Contain("output was truncated");
    }
}

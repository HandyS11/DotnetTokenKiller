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

    private static TeeLogEntry Entry(bool truncated) =>
        new("/tee/build.log",
            new TeeLogHeader("dotnet build MyApp.slnx", "/home/user/proj", 0, RunSource.Run, Timestamp, truncated),
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
    public async Task RenderViewAsync_WarnsAboutTruncation_WhenTheHeaderSaysSo()
    {
        var view = new LogView(Entry(truncated: true), "body\n[dtk: output truncated at 100 bytes]\n", 2, 2);

        var output = await RenderAsync(view);

        output.Should().Contain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_WhenTheHeaderIsNotTruncated()
    {
        var view = new LogView(Entry(truncated: false), "body\n", 1, 1);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_OmitsTheTruncationWarning_WhenTheHeaderIsMissing()
    {
        // A legacy log (no header) cannot say one way or the other, so it must not claim truncation.
        var entry = Entry(truncated: false) with { Header = null };
        var view = new LogView(entry, "body\n", 1, 1);

        var output = await RenderAsync(view);

        output.Should().NotContain("output was truncated");
    }

    [Fact]
    public async Task RenderViewAsync_ShowsBothWarnings_WhenARunWasKilledAfterItsOutputWasAlreadyTruncated()
    {
        var entry = Entry(truncated: true) with
        {
            Header = new TeeLogHeader("dotnet build MyApp.slnx", "/home/user/proj", null, RunSource.Run, Timestamp,
                Truncated: true)
        };
        var view = new LogView(entry, "body\n[dtk: output truncated at 100 bytes]\n", 2, 2);

        var output = await RenderAsync(view);

        output.Should().Contain("run did not finish");
        output.Should().Contain("output was truncated");
    }
}

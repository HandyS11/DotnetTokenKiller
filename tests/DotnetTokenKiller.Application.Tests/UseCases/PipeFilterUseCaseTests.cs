using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class PipeFilterUseCaseTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public PipeFilterUseCaseTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
    }

    private PipeFilterUseCase Create(string stdin, TextWriter? output = null)
    {
        var pipeline = new FilteredOutputPipeline(_tracker, output ?? TextWriter.Null, _configProvider);
        return new PipeFilterUseCase(pipeline, _teeService, new StringReader(stdin));
    }

    [Fact]
    public async Task RunAsync_PassesStdinToTheFilter()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("piped input").RunAsync(_filter, "build", 0, new OutputOptions());

        _filter.Received(1).Apply("piped input", 0);
    }

    [Fact]
    public async Task RunAsync_PassesExitCodeToTheFilterAndReturnsIt()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var exitCode = await Create("boom").RunAsync(_filter, "build", 1, new OutputOptions());

        _filter.Received(1).Apply("boom", 1);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_RecordsPipeSourceUnderTheCanonicalSlug()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("raw").RunAsync(_filter, "list package", 0, new OutputOptions());

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Source == RunSource.Pipe && r.Command == "list package"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WritesFilteredOutput()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("condensed");
        var output = new StringWriter();

        await Create("raw", output).RunAsync(_filter, "build", 0, new OutputOptions());

        output.ToString().Should().Be("condensed");
    }

    [Fact]
    public async Task RunAsync_EmptyStdinOnFailure_EmitsRawTailFallbackHeader()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        var output = new StringWriter();

        await Create("", output).RunAsync(_filter, "build", 7, new OutputOptions());

        output.ToString().Should().Contain("dotnet build failed (exit 7)");
    }

    [Fact]
    public async Task RunAsync_OpensATeeSessionUnderTheCanonicalSlug()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("raw").RunAsync(_filter, "list package", 0, new OutputOptions());

        // CommandLine and ProjectPath are asserted here because they are exactly what `dtk log`
        // filters on, and a wrong value there would make it silently return nothing with no test
        // noticing.
        await _teeService.Received(1).BeginAsync(
            "list package",
            Arg.Is<TeeLogHeader>(h =>
                h.ExitCode == null &&
                h.Source == RunSource.Pipe &&
                h.CommandLine == "dotnet list package" &&
                h.ProjectPath == Environment.CurrentDirectory),
            Arg.Any<CancellationToken>());
    }
}

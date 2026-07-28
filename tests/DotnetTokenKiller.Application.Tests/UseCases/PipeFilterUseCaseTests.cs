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
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TeeLogHeader>(),
            Arg.Any<CancellationToken>()).Returns((string?)null);
    }

    private PipeFilterUseCase Create(string stdin, TextWriter? output = null) =>
        new(new FilteredOutputPipeline(_tracker, _teeService, output ?? TextWriter.Null, _configProvider),
            new StringReader(stdin));

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
            Arg.Is<CommandRecord>(r => r!.Source == RunSource.Pipe && r.Command == "list package"),
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
}

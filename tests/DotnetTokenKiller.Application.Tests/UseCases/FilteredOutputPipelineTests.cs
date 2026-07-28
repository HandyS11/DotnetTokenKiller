using System.Diagnostics;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class FilteredOutputPipelineTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly FilteredOutputPipeline _sut;
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public FilteredOutputPipelineTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>()).Returns((string?)null);
        _sut = new FilteredOutputPipeline(_tracker, _teeService, TextWriter.Null, _configProvider);
    }

    private FilteredOutputRequest Request(
        string raw = "raw output",
        int exitCode = 0,
        RunSource source = RunSource.Run) =>
        new(_filter, raw, exitCode, "build", "dotnet build", source,
            new OutputOptions(), Stopwatch.GetTimestamp());

    [Fact]
    public async Task ProcessAsync_ReturnsSuppliedExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var exitCode = await _sut.ProcessAsync(Request(exitCode: 42));

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task ProcessAsync_RecordsSuppliedSource()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.ProcessAsync(Request(source: RunSource.Pipe));

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.Source == RunSource.Pipe && r.Command == "build"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FilterThrows_RecordsFilterFaulted()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.ProcessAsync(Request());

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyFilterOutputOnFailure_RecordsRawTailFallback()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");

        await _sut.ProcessAsync(Request(exitCode: 1));

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.Outcome == RunOutcome.RawTailFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PrefersFilterFaulted_WhenBothConditionsOverlap()
    {
        // A faulted filter yields the raw text unchanged, so when that raw text is itself
        // whitespace on a failed command BOTH conditions hold. Faulted must win — this ordering
        // is deliberate and is the one rule in the pipeline that is not incidental.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.ProcessAsync(Request(raw: "   ", exitCode: 1));

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var act = async () => await _sut.ProcessAsync(Request());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessAsync_TeeThrows_DoesNotSurfaceException()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("io error"));

        var act = async () => await _sut.ProcessAsync(Request());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessAsync_TrackingDisabled_RecordsNothing()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(DtkConfig.Default with { Tracking = DtkConfig.Default.Tracking with { Enabled = false } });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.ProcessAsync(Request());

        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }
}

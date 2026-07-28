using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class CommandRecordTests
{
    [Fact]
    public void CommandRecord_stores_all_fields()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var executionTime = TimeSpan.FromSeconds(1.5);

        var record = new CommandRecord(
            timestamp,
            "build",
            "/home/user/MyApp",
            new TokenStatistics(1000, 150, 850, 85.0),
            executionTime,
            false);

        record.Timestamp.Should().Be(timestamp);
        record.Command.Should().Be("build");
        record.ProjectPath.Should().Be("/home/user/MyApp");
        record.InputTokens.Should().Be(1000);
        record.OutputTokens.Should().Be(150);
        record.SavedTokens.Should().Be(850);
        record.SavingsPercentage.Should().Be(85.0);
        record.ExecutionTime.Should().Be(executionTime);
        record.Success.Should().BeFalse();
    }

    [Fact]
    public void CommandRecord_DefaultSuccess_IsTrue()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/app", new TokenStatistics(100, 10, 90, 90.0), TimeSpan.Zero);

        record.Success.Should().BeTrue();
    }

    [Fact]
    public void CommandRecord_record_equality_by_value()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMilliseconds(500);

        var a = new CommandRecord(timestamp, "build", "/app", new TokenStatistics(100, 10, 90, 90.0), duration);
        var b = new CommandRecord(timestamp, "build", "/app", new TokenStatistics(100, 10, 90, 90.0), duration);

        a.Should().Be(b);
    }

    [Fact]
    public void CommandRecord_supports_with_expression()
    {
        var original = new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/app", new TokenStatistics(100, 10, 90, 90.0), TimeSpan.Zero);

        var updated = original with
        {
            Command = "test"
        };

        updated.Command.Should().Be("test");
        original.Command.Should().Be("build");
    }

    [Fact]
    public void CommandRecord_NullCommand_ThrowsArgumentException()
    {
        var act = () => new CommandRecord(
            DateTimeOffset.UtcNow, null!, "/app", new TokenStatistics(100, 10, 90, 90.0), TimeSpan.Zero);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CommandRecord_WhitespaceCommand_ThrowsArgumentException()
    {
        var act = () => new CommandRecord(
            DateTimeOffset.UtcNow, "  ", "/app", new TokenStatistics(100, 10, 90, 90.0), TimeSpan.Zero);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CommandRecord_NullTokens_ThrowsArgumentNullException()
    {
        var act = () => new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/app", null!, TimeSpan.Zero);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_DefaultsSourceToRun()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/proj",
            new TokenStatistics(1000, 150, 850, 85.0), TimeSpan.FromSeconds(1));

        record.Source.Should().Be(RunSource.Run);
    }

    [Fact]
    public void Constructor_PreservesExplicitPipeSource()
    {
        var record = new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/proj",
            new TokenStatistics(1000, 150, 850, 85.0), TimeSpan.FromSeconds(1),
            success: true, outcome: RunOutcome.Filtered, source: RunSource.Pipe);

        record.Source.Should().Be(RunSource.Pipe);
    }
}

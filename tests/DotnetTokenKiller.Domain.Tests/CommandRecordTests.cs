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
            Timestamp: timestamp,
            Command: "build",
            ProjectPath: "/home/user/MyApp",
            InputTokens: 1000,
            OutputTokens: 150,
            SavedTokens: 850,
            SavingsPercentage: 85.0,
            ExecutionTime: executionTime);

        record.Timestamp.Should().Be(timestamp);
        record.Command.Should().Be("build");
        record.ProjectPath.Should().Be("/home/user/MyApp");
        record.InputTokens.Should().Be(1000);
        record.OutputTokens.Should().Be(150);
        record.SavedTokens.Should().Be(850);
        record.SavingsPercentage.Should().Be(85.0);
        record.ExecutionTime.Should().Be(executionTime);
    }

    [Fact]
    public void CommandRecord_record_equality_by_value()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMilliseconds(500);

        var a = new CommandRecord(timestamp, "build", "/app", 100, 10, 90, 90.0, duration);
        var b = new CommandRecord(timestamp, "build", "/app", 100, 10, 90, 90.0, duration);

        a.Should().Be(b);
    }

    [Fact]
    public void CommandRecord_supports_with_expression()
    {
        var original = new CommandRecord(
            DateTimeOffset.UtcNow, "build", "/app", 100, 10, 90, 90.0, TimeSpan.Zero);

        var updated = original with { Command = "test" };

        updated.Command.Should().Be("test");
        original.Command.Should().Be("build");
    }
}

using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class CommandResultTests
{
    [Fact]
    public void CommandResult_stores_all_fields()
    {
        var result = new CommandResult("out", "err", 42);

        result.StdOut.Should().Be("out");
        result.StdErr.Should().Be("err");
        result.ExitCode.Should().Be(42);
    }

    [Fact]
    public void CommandResult_record_equality_by_value()
    {
        var a = new CommandResult("x", "y", 0);
        var b = new CommandResult("x", "y", 0);

        a.Should().Be(b);
    }

    [Fact]
    public void CommandResult_records_with_different_values_are_not_equal()
    {
        var a = new CommandResult("x", "y", 0);
        var b = new CommandResult("x", "y", 1);

        a.Should().NotBe(b);
    }

    [Fact]
    public void CommandResult_supports_deconstruction()
    {
        var (stdOut, stdErr, exitCode) = new CommandResult("stdout", "stderr", 0);

        stdOut.Should().Be("stdout");
        stdErr.Should().Be("stderr");
        exitCode.Should().Be(0);
    }
}

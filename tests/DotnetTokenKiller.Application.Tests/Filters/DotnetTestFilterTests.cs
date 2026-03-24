using DotnetTokenKiller.Application.Filters;
using FluentAssertions;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetTestFilterTests
{
    private readonly DotnetTestFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_AllPassFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_FailuresFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_AllPassFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "test all-pass filter should achieve ≥90% savings");
    }

    [Fact]
    public void Apply_FailuresFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "test failures filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("MSBuild version 17.11")]
    [InlineData("Microsoft (R) Test Execution")]
    [InlineData("Copyright (c) Microsoft")]
    [InlineData("Starting test execution, please wait...")]
    [InlineData("A total of 1 test files matched")]
    [InlineData("Determining projects to restore")]
    public void Apply_AllPassFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public Task Apply_ZeroTestsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_ZeroTestsFixture_ReturnsZeroTestsMessage()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        _sut.Apply(fixture).Should().Be("✓ dotnet test: 0 tests found\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    [Fact]
    public void Apply_FailureWithSubMillisecondDuration_IsIncluded()
    {
        // xUnit reports durations < 1ms as "[< 1 ms]" — ensure these are captured
        const string input = """
                               A total of 1 test files matched the specified pattern.

                               Failed MyTests.Throws_InvalidOperation [< 1 ms]
                               Error Message:
                                System.InvalidOperationException : Simulated failure
                               Stack Trace:
                                  at MyTests.Throws_InvalidOperation() in /path/to/Test.cs:line 10

                             Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 ms - Tests.dll
                             """;

        var result = new DotnetTestFilter().Apply(input);
        result.Should().Contain("Throws_InvalidOperation");
        result.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void Apply_ContentWithNoSummaryLine_ReturnsEmpty()
    {
        // Covers FormatOutput ProjectCount==0 path (lines 168-169)
        const string input = "Some test runner output without a summary line";

        var result = _sut.Apply(input);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_SummaryWithDurationInSeconds_ParsedCorrectly()
    {
        // Covers NormalizeDurationToMs "s" case (line 248)
        const string input = "Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 2.5 s - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("passed");
    }

    [Fact]
    public void Apply_SummaryWithDurationInMinutes_ParsedCorrectly()
    {
        // Covers NormalizeDurationToMs "m" case (line 249)
        const string input = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 m - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("passed");
    }

    [Fact]
    public void Apply_SummaryWithDurationInHours_ParsedCorrectly()
    {
        // Covers NormalizeDurationToMs "h" case (line 250)
        const string input = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 h - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("passed");
    }

    [Fact]
    public void Apply_SummaryWithSkippedTests_IncludesSkippedCountInOutput()
    {
        // Covers TotalSkipped > 0 true branch (condition at line 176)
        const string input = "Passed!  - Failed: 0, Passed: 5, Skipped: 2, Total: 7, Duration: 10 ms - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("2 skipped");
    }

    [Fact]
    public void Apply_SingleProject_UsesSingularProjectForm()
    {
        // Covers (ProjectCount == 1 ? "" : "s") true branch (condition at line 179)
        const string input = "Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 100 ms - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Contain("1 project");
        result.Should().NotContain("projects");
    }

    [Fact]
    public void Apply_MoreThanMaxFailures_ShowsPlusMoreLine()
    {
        // Covers Failures.Count > MaxFailures path (lines 202-204)
        var sb = new StringBuilder();
        for (var i = 0; i < 16; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Failed MyTests.TestMethod{i} [1 ms]")
                .AppendLine("  Error Message:")
                .AppendLine(CultureInfo.InvariantCulture, $"    Assertion failed for test {i}")
                .AppendLine("  Stack Trace:")
                .AppendLine(CultureInfo.InvariantCulture,
                    $"    at MyTests.TestMethod{i}() in /path/Test.cs:line {i + 1}");
        }

        sb.AppendLine("Failed!  - Failed: 16, Passed: 0, Skipped: 0, Total: 16, Duration: 100 ms - Tests.dll");

        var result = _sut.Apply(sb.ToString());

        result.Should().Contain("+1 more failures");
    }

    [Fact]
    public void Apply_FailureWithNoErrorMessage_HandlesEmptyMessageGracefully()
    {
        // Covers CompactMessage with empty lines list (lines 228-229)
        // The failure header is immediately followed by the Stack Trace label (no error message content)
        const string input = """
                               Failed MyTests.EmptyMessageTest [1 ms]
                               Error Message:
                               Stack Trace:
                                  at MyTests.EmptyMessageTest() in /path/Test.cs:line 42

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input);

        result.Should().Contain("EmptyMessageTest");
    }

    [Fact]
    public void Apply_FailureHeaderAsLastLine_CovershortCircuitCondition()
    {
        // Covers i >= lines.Length short-circuit in ParseFailure (conditions at lines 75, 84)
        const string input = "  Failed MyTests.LastLineTest [1 ms]";

        var result = _sut.Apply(input);

        // No summary → empty output
        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_FailuresWithSkippedTests_IncludesSkippedInSummaryLine()
    {
        // Covers TotalSkipped > 0 true branch inside FormatFailures (condition at line 206)
        const string input = """
                               Failed MyTests.FailingTest [1 ms]
                               Error Message:
                                 Assert failed
                               Stack Trace:
                                  at MyTests.FailingTest() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 3, Total: 4, Duration: 50 ms - Tests.dll
                             """;

        var result = _sut.Apply(input);

        result.Should().Contain("3 skipped");
    }

    [Fact]
    public void Apply_SingleProjectWithFailures_UsesSingularProjectInSummary()
    {
        // Covers (ProjectCount == 1 ? "" : "s") true branch in FormatFailures summary (condition at line 206)
        const string input = """
                               Failed MyTests.FailingTest [1 ms]
                               Error Message:
                                 Assert failed
                               Stack Trace:
                                  at MyTests.FailingTest() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 50 ms - Tests.dll
                             """;

        var result = _sut.Apply(input);

        result.Should().Contain("1 project");
        result.Should().NotContain("projects");
    }

    [Fact]
    public void Apply_FailuresWithMultipleProjects_UsesPluralProjectInSummary()
    {
        // Covers (ProjectCount == 1 ? "" : "s") false branch in FormatFailures summary (line 206)
        const string input = """
                               Failed MyTests.FailingTest [1 ms]
                               Error Message:
                                 Assert failed
                               Stack Trace:
                                  at MyTests.FailingTest() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 2, Skipped: 0, Total: 3, Duration: 100 ms - Tests1.dll
                             Failed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 50 ms - Tests2.dll
                             """;

        var result = _sut.Apply(input);

        result.Should().Contain("2 projects");
    }

    [Fact]
    public void NormalizeDurationToMs_UnknownUnit_ReturnsValueUnchanged()
    {
        // Covers default arm (line 251) of NormalizeDurationToMs switch via reflection
        var method = typeof(DotnetTestFilter)
            .GetMethod("NormalizeDurationToMs", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (double)method.Invoke(null, [42.0, "x"])!;

        result.Should().Be(42.0);
    }

    [Fact]
    public void Apply_ZeroTestsFromAllSummariesZero_ReturnsZeroTestsMessage()
    {
        // Covers state is { ProjectCount: > 0, TotalPassed: 0, TotalFailed: 0 } branch (line 162)
        const string input = "Passed!  - Failed: 0, Passed: 0, Skipped: 0, Total: 0, Duration: 5 ms - Tests.dll";

        var result = _sut.Apply(input);

        result.Should().Be("✓ dotnet test: 0 tests found\n");
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetTestFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

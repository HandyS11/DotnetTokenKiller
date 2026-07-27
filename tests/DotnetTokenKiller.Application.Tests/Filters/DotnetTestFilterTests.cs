using System.Globalization;
using System.Reflection;
using System.Text;
using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetTestFilterTests
{
    private readonly DotnetTestFilter _sut = new("/test/project/root");

    [Fact]
    public Task Apply_AllPassFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public Task Apply_FailuresFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
        return Verify(result);
    }

    [Fact]
    public void Apply_AllPassFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "test all-pass filter should achieve ≥90% savings");
    }

    [Fact]
    public void Apply_FailuresFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture, exitCode: 1);
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
        _sut.Apply(fixture, exitCode: 0).Should().NotContain(noiseLine);
    }

    [Fact]
    public Task Apply_ZeroTestsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_ZeroTestsFixture_ReturnsZeroTestsMessage()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        _sut.Apply(fixture, exitCode: 0).Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsEmpty()
    {
        _sut.Apply(null!, exitCode: 0).Should().BeEmpty();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        _sut.Apply(string.Empty, exitCode: 0).Should().BeEmpty();
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

        var result = new DotnetTestFilter().Apply(input, exitCode: 1);
        result.Should().Contain("Throws_InvalidOperation");
        result.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void Apply_ContentWithNoSummaryLine_ReturnsEmpty()
    {
        // Covers FormatOutput ProjectCount==0 path (lines 168-169)
        const string input = "Some test runner output without a summary line";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_SummaryWithDurationInSeconds_ParsedCorrectly()
    {
        // Asserts the whole line, not just "passed": the rendered elapsed time is the only place
        // the "s" arm of NormalizeDurationToMs is observable, so a loose assertion would let any
        // multiplier mutation (or deletion of the arm) survive.
        const string input = "Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 2.5 s - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 3 passed (1 project, 2.50s)\n");
    }

    [Fact]
    public void Apply_SummaryWithDurationInMinutes_ParsedCorrectly()
    {
        const string input = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 m - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 1 passed (1 project, 60.00s)\n");
    }

    [Fact]
    public void Apply_SummaryWithDurationInHours_ParsedCorrectly()
    {
        const string input = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 h - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 1 passed (1 project, 3600.00s)\n");
    }

    [Fact]
    public void Apply_SummaryWithSkippedTests_IncludesSkippedCountInOutput()
    {
        const string input = "Passed!  - Failed: 0, Passed: 5, Skipped: 2, Total: 7, Duration: 10 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 5 passed, 2 skipped (1 project, 0.01s)\n");
    }

    [Fact]
    public void Apply_SingleProject_UsesSingularProjectForm()
    {
        const string input = "Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 100 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 4 passed (1 project, 0.10s)\n");
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

        var result = _sut.Apply(sb.ToString(), exitCode: 1);

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

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("EmptyMessageTest");
    }

    [Fact]
    public void Apply_FailureHeaderAsLastLine_CovershortCircuitCondition()
    {
        // Covers i >= lines.Length short-circuit in ParseFailure (conditions at lines 75, 84).
        // A parsed failure now renders even without a trailing summary (crashed-host scenario),
        // so the run reports the failure instead of collapsing to empty.
        const string input = "  Failed MyTests.LastLineTest [1 ms]";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("LastLineTest").And.Contain("FAILURES (1)");
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

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be(
            """
            FAILURES (1):
              MyTests.FailingTest [1 ms]
                Assert failed
                at ../../../path/Test.cs:line 1
            dotnet test: 1 failed, 0 passed, 3 skipped (1 project, 0.05s)

            """);
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

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be(
            """
            FAILURES (1):
              MyTests.FailingTest [1 ms]
                Assert failed
                at ../../../path/Test.cs:line 1
            dotnet test: 1 failed, 0 passed (1 project, 0.05s)

            """);
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

        var result = _sut.Apply(input, exitCode: 1);

        // Pins the accumulator arithmetic across both summaries: passed 2+3, duration 100+50ms,
        // project count 1+1. A '+=' flipped to '-=' on any of them changes this string.
        result.Should().Be(
            """
            FAILURES (1):
              MyTests.FailingTest [1 ms]
                Assert failed
                at ../../../path/Test.cs:line 1
            dotnet test: 1 failed, 5 passed (2 projects, 0.15s)

            """);
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

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("⚠ dotnet test: 0 tests found\n");
    }

    [Fact]
    public void Apply_DefaultRootPath_UsesEnvironmentCurrentDirectory()
    {
        // Kills null coalescing mutation (line 16): rootPath ?? Environment.CurrentDirectory
        var filter = new DotnetTestFilter();
        const string input = "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 10 ms - T.dll";

        var result = filter.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 1 passed (1 project, 0.01s)\n");
    }

    [Fact]
    public void Apply_FailureParseLoop_IncrementMutationKilled()
    {
        // Kills i-- mutation (line 86) and loop condition mutation (line 84):
        // Stack trace with multiple frames — ensures loop increments forward
        const string input = """
                               Failed MyTests.MultiStackTest [5 ms]
                               Error Message:
                                 Assert.Equal() Failure
                               Stack Trace:
                                  at Internal.Method() in /path/to/Internal.cs:line 5
                                  at MyTests.MultiStackTest() in /path/to/Test.cs:line 42
                                  at MoreStack() in /path/to/Other.cs:line 99

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 5 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        // Verifies the first stack frame file reference is captured (Internal.cs:line 5)
        result.Should().Contain("line 5");
    }

    [Fact]
    public void Apply_FailureWithSourceRef_IncludesAtPrefix()
    {
        // Kills string mutation on sourceRef format (line 132)
        const string input = """
                               Failed MyTests.SourceRefTest [1 ms]
                               Error Message:
                                 Something failed
                               Stack Trace:
                                  at MyTests.SourceRefTest() in /test/project/root/Tests/Test.cs:line 42

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("at Tests/Test.cs:line 42");
    }

    [Fact]
    public void Apply_FailureDurationDisplayedInBrackets()
    {
        // Kills string mutation on duration format (line 155)
        const string input = """
                               Failed MyTests.DurationTest [42 ms]
                               Error Message:
                                 Boom
                               Stack Trace:
                                  at MyTests.DurationTest() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 42 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("[42 ms]");
    }

    [Fact]
    public void Apply_FailureHeaderLine_ContainsFailuresCount()
    {
        // Kills string mutation on "FAILURES ({count})" (line 216)
        const string input = """
                               Failed MyTests.Test1 [1 ms]
                               Error Message:
                                 boom

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().StartWith("FAILURES (1):");
    }

    [Fact]
    public void Apply_CompactMessage_OnlyExpected_NoCompaction()
    {
        // Kills logical mutation: expectedLine != null || actualLine != null (line 222)
        // When only Expected: exists but no Actual:, falls through to join path
        const string input = """
                               Failed MyTests.OnlyExpected [1 ms]
                               Error Message:
                                 Expected: 42
                               Stack Trace:
                                  at MyTests.OnlyExpected() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        // Should just join the message, not the compacted Expected/Actual form
        result.Should().Contain("Expected: 42");
        result.Should().NotContain("Actual:");
    }

    [Fact]
    public void Apply_CompactMessage_BothExpectedAndActual_CompactedFormat()
    {
        // Kills logical mutation on expectedLine && actualLine (line 222)
        const string input = """
                               Failed MyTests.AssertEqual [1 ms]
                               Error Message:
                                 Expected: "hello"
                                 Actual:   "world"
                               Stack Trace:
                                  at MyTests.AssertEqual() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("Expected:");
        result.Should().Contain("Actual:");
    }

    [Fact]
    public void Apply_ExactlyMaxFailures_NoMoreFailuresLine()
    {
        // Kills equality mutation: Failures.Count >= MaxFailures (line 201)
        var sb = new StringBuilder();
        for (var i = 0; i < 15; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Failed MyTests.TestMethod{i} [1 ms]")
                .AppendLine("  Error Message:")
                .AppendLine(CultureInfo.InvariantCulture, $"    Assertion {i}")
                .AppendLine("  Stack Trace:")
                .AppendLine(CultureInfo.InvariantCulture,
                    $"    at MyTests.TestMethod{i}() in /path/Test.cs:line {i + 1}");
        }

        sb.AppendLine("Failed!  - Failed: 15, Passed: 0, Skipped: 0, Total: 15, Duration: 100 ms - Tests.dll");

        var result = _sut.Apply(sb.ToString(), exitCode: 1);

        result.Should().NotContain("more failures");
    }

    [Fact]
    public void Apply_NormalizeDuration_Seconds_CorrectConversion()
    {
        // Kills arithmetic mutation: value * 1_000 → value / 1_000 (line 235)
        const string input =
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 2 s - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("2.00s");
    }

    [Fact]
    public void Apply_NormalizeDuration_Minutes_CorrectConversion()
    {
        // Kills arithmetic mutation: value * 60_000 → value / 60_000 (line 236)
        const string input =
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 m - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("60.00s");
    }

    [Fact]
    public void Apply_NormalizeDuration_Hours_CorrectConversion()
    {
        // Kills arithmetic mutation: value * 3_600_000 → value / 3_600_000 (line 237)
        const string input =
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 h - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("3600.00s");
    }

    [Fact]
    public void Apply_NormalizeDuration_Ms_ReturnsUnchanged()
    {
        // Kills string mutation on "ms" unit (line 234)
        const string input =
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 500 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("0.50s");
    }

    [Fact]
    public void Apply_FailureSummaryLine_ContainsExactFormat()
    {
        // Kills string mutations in FormatFailures summary line (line 227)
        const string input = """
                               Failed MyTests.Fail [1 ms]
                               Error Message:
                                 boom

                             Failed!  - Failed: 1, Passed: 2, Skipped: 0, Total: 3, Duration: 100 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("dotnet test: 1 failed, 2 passed");
    }

    [Fact]
    public void Apply_PassOutput_ContainsExactFormat()
    {
        // Kills string mutation on passed output format (line 180)
        const string input =
            "Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 200 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("dotnet test: 10 passed");
    }

    [Fact]
    public void Apply_FailedTestWithNoStackTraceLabel_StillParsesNextFailure()
    {
        // Kills statement mutation on loop index increment after missing StackTraceLabel (line 86)
        const string input = """
                               Failed MyTests.NoStack1 [1 ms]
                               Error Message:
                                 First error

                               Failed MyTests.NoStack2 [2 ms]
                               Error Message:
                                 Second error

                             Failed!  - Failed: 2, Passed: 0, Skipped: 0, Total: 2, Duration: 10 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("NoStack1");
        result.Should().Contain("NoStack2");
    }

    [Fact]
    public void Apply_NoTestsPattern_SetsZeroTestsFlag()
    {
        // Kills statement mutation on state.ZeroTestsFound = true (line 52)
        // "No test matches" pattern triggers the zero-tests path
        const string input = "No test matches the given testcase filter";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
    }

    [Fact]
    public void Apply_StackTraceLoopProcessesAllFrames()
    {
        // Kills equality mutation: i > lines.Length (line 84) and i-- mutation (line 86)
        // Multiple stack frames followed by a second failure — ensures loop exits correctly
        const string input = """
                               Failed MyTests.First [1 ms]
                               Error Message:
                                 First error
                               Stack Trace:
                                  at Layer1() in /path/L1.cs:line 10
                                  at Layer2() in /path/L2.cs:line 20
                                  at Layer3() in /path/L3.cs:line 30

                               Failed MyTests.Second [2 ms]
                               Error Message:
                                 Second error
                               Stack Trace:
                                  at MyTests.Second() in /path/T.cs:line 5

                             Failed!  - Failed: 2, Passed: 0, Skipped: 0, Total: 2, Duration: 10 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("First");
        result.Should().Contain("Second");
    }

    [Fact]
    public void Apply_PassedSingleProject_ContainsCheckmark()
    {
        // Kills string mutation on pass format (line 180) — verify ✓ prefix
        const string input =
            "Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 100 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().StartWith("\u2713");
    }

    [Fact]
    public void Apply_CompactMessage_OnlyActual_UsesJoinedFormat()
    {
        // Kills logical mutation: expectedLine != null || actualLine != null (line 222)
        // Only "Actual:" present without "Expected:" — should use joined format
        const string input = """
                               Failed MyTests.OnlyActual [1 ms]
                               Error Message:
                                 Actual: 99
                               Stack Trace:
                                  at MyTests.OnlyActual() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("Actual: 99");
    }

    [Fact]
    public void Apply_FailuresSummaryLine_ContainsDotnetTestPrefix()
    {
        // Kills string mutation on summary format (line 227)
        const string input = """
                               Failed MyTests.Fail [1 ms]
                               Error Message:
                                 boom

                             Failed!  - Failed: 1, Passed: 3, Skipped: 0, Total: 4, Duration: 50 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("dotnet test:");
    }

    [Fact]
    public void Apply_TwoProjectsPass_UsesPluralProjectForm()
    {
        // Kills line 180 conditional (state.ProjectCount == 1 ? "" : "s") and string mutations
        // — always returning "" produces "2 project" without the "s"
        const string input = """
                             Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 100 ms - Tests1.dll
                             Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 50 ms - Tests2.dll
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("2 projects");
        result.Should().NotContain("1 project");
    }

    [Fact]
    public void Apply_FailureWithNoMessageContent_MessageIsEmpty()
    {
        // Kills string mutation on CompactMessage early return (line 216)
        // — mutation returns "Stryker was here!" instead of string.Empty
        const string input = """
                               Failed MyTests.NoContent [1 ms]
                               Error Message:
                               Stack Trace:
                                  at MyTests.NoContent() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("NoContent");
        result.Should().NotContain("Stryker was here!");
    }

    [Fact]
    public void Apply_CompactMessage_BothExpectedAndActual_BothAppearInOutput()
    {
        // Kills string mutation on line 227: $"{expectedLine}, {actualLine}" → ""
        // — mutation returns empty string instead of the combined message
        const string input = """
                               Failed MyTests.EqualityFail [1 ms]
                               Error Message:
                                 Expected: 42
                                 Actual:   99
                               Stack Trace:
                                  at MyTests.EqualityFail() in /path/Test.cs:line 10

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 5 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("Expected: 42");
        result.Should().Contain("Actual:   99");
    }

    [Fact]
    public void Apply_CompactMessage_OnlyActual_NoLeadingComma()
    {
        // Kills line 222 logical mutation: && → ||
        // With || mutation: $"{null}, Actual: 99" = ", Actual: 99" — test verifies no leading comma
        const string input = """
                               Failed MyTests.OnlyActualTest [1 ms]
                               Error Message:
                                 Actual: 99
                               Stack Trace:
                                  at MyTests.OnlyActualTest() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("Actual: 99");
        result.Should().NotContain(", Actual: 99");
    }

    [Fact]
    public void Apply_FailureRegularMessage_ContentAppearsInOutput()
    {
        // Kills string mutation on CompactMessage fallback join (line 234)
        // — mutation returns "" from Truncate("") instead of the joined message lines
        const string input = """
                               Failed MyTests.RegularFail [1 ms]
                               Error Message:
                                 The quick brown fox assertion failed
                               Stack Trace:
                                  at MyTests.RegularFail() in /path/Test.cs:line 5

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 2 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Contain("quick brown fox assertion failed");
    }

    [Fact]
    public void Apply_OneProjectPass_UsesSingularProjectForm()
    {
        // Kills line 180 string mutation "" on the "s" branch — ensures "1 project" (not "1 projects")
        const string input =
            "Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 200 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("1 project,");
        result.Should().NotContain("1 projects");
    }

    [Fact]
    public void Apply_TwoProjectsPassWithElapsed_ContainsElapsedTime()
    {
        // Kills string mutation on elapsed format inside FormatOutput (line 180 area)
        const string input = """
                             Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1000 ms - Tests1.dll
                             Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 500 ms - Tests2.dll
                             """;

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("1.50s");
        result.Should().Contain("2 projects");
    }

    [Fact]
    public void Apply_PartialSummaryThenCrash_NonZeroExitZeroFailed_ReturnsEmpty()
    {
        // Host crashed after printing a partial pass summary: ProjectCount > 0 and TotalFailed == 0,
        // but the exit code is non-zero, so the run did not actually succeed. "FAILURES (0):" would
        // look like a clean report and would prevent FilteredRunUseCase's raw-tail fallback from
        // ever firing.
        const string input = "Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 89 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 134);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_MultiPartSummaryDuration_SumsAllParts()
    {
        // VSTest renders long runs as multi-part durations ("1 m 2 s"); the single-part parse
        // dropped every trailing part, so a 62 s run was reported as 60 s.
        const string input =
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 m 2 s - Tests.dll";
        var result = _sut.Apply(input, exitCode: 0);
        result.Should().Contain("62.00s").And.NotContain("60.00s");
    }

    [Fact]
    public void Apply_SlowFailingTest_KeepsFailureDetail()
    {
        // Slow tests report second-scale durations ("[1 s]") that the ms-only header regex missed.
        const string raw =
            "  Failed MyTests.SlowTest [1 s]\n  Error Message:\n   Expected 1 but was 2.\nFailed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - MyTests.dll";
        var result = new DotnetTestFilter().Apply(raw, exitCode: 1);
        result.Should().Contain("SlowTest").And.Contain("Expected 1 but was 2");
    }

    [Fact]
    public void Apply_DotNet9MtpSummary_IsParsed()
    {
        // .NET 9 Microsoft.Testing.Platform output: lowercase "failed" lines and a "Test summary:" line.
        const string raw =
            "failed MyTests.T1 (12ms)\nTest summary: total: 10, failed: 1, succeeded: 9, skipped: 0, duration: 2.3s";
        var result = _sut.Apply(raw, exitCode: 1);

        // A bare MTP failure line carries no message, so the message row renders as indent-only.
        // Spelled with \n rather than a raw literal because that trailing indent is significant.
        result.Should().Be(
            "FAILURES (1):\n  MyTests.T1 [12ms]\n    \ndotnet test: 1 failed, 9 passed (1 project, 2.30s)\n");
    }

    [Fact]
    public void Apply_MtpFailureWithStackFrame_ExtractsSourceReferenceAndMessage()
    {
        // MTP emits no "Error Message:"/"Stack Trace:" labels — the message and the stack frame
        // arrive as bare indented continuation lines. Nothing previously exercised the branch that
        // pulls a source ref out of those, so every mutant in it went unreached.
        const string raw = """
                           failed MyTests.T1 (12ms)
                             Assert.Equal() Failure
                             Expected: 1
                             Actual: 2
                             at MyTests.T1() in /path/to/Test.cs:line 42
                           Test summary: total: 2, failed: 1, succeeded: 1, skipped: 0, duration: 2.3s
                           """;

        var result = _sut.Apply(raw, exitCode: 1);

        result.Should().Be(
            """
            FAILURES (1):
              MyTests.T1 [12ms]
                Expected: 1, Actual: 2
                at ../../../path/to/Test.cs:line 42
            dotnet test: 1 failed, 1 passed (1 project, 2.30s)

            """);
    }

    [Fact]
    public void Apply_MtpFailureWithMultipleStackFrames_KeepsOnlyTheFirstSourceReference()
    {
        // Guards the string.IsNullOrEmpty(sourceRef) check: without it, the last frame would win.
        const string raw = """
                           failed MyTests.T2 (5ms)
                             boom
                             at Inner.Frame() in /path/to/First.cs:line 7
                             at MyTests.T2() in /path/to/Second.cs:line 99
                           Test summary: total: 1, failed: 1, succeeded: 0, skipped: 0, duration: 1s
                           """;

        var result = _sut.Apply(raw, exitCode: 1);

        result.Should().Contain("at ../../../path/to/First.cs:line 7")
            .And.NotContain("Second.cs");
    }

    [Fact]
    public void Apply_FailuresParsedButNoSummary_StillReportsFailures()
    {
        // Test host crashed before printing a summary — old code returned "" and lost the failures.
        // With no summary there is no project/elapsed context, so it must not fabricate "0 projects".
        // The exact assertion pins the empty trailing context: a mutant that seeds `context` with a
        // non-empty string instead of string.Empty would append it to this summary line.
        const string raw = "  Failed MyTests.T1 [15 ms]\n  Error Message:\n   boom";
        var result = new DotnetTestFilter().Apply(raw, exitCode: 1);
        result.Should().Be(
            "FAILURES (1):\n  MyTests.T1 [15 ms]\n    boom\ndotnet test: 1 failed, 0 passed\n");
    }

    [Fact]
    public void Apply_MultiLineMessageWithoutExpectedActual_JoinsLinesWithSpaces()
    {
        // CompactMessage falls to string.Join(" ", lines) when neither Expected: nor Actual: is
        // present. A single-line message can't tell " " from "": this needs two message lines so
        // the separator is observable.
        const string input = """
                               Failed MyTests.Multi [1 ms]
                               Error Message:
                                 first part
                                 second part
                               Stack Trace:
                                  at MyTests.Multi() in /path/Test.cs:line 1

                             Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be(
            """
            FAILURES (1):
              MyTests.Multi [1 ms]
                first part second part
                at ../../../path/Test.cs:line 1
            dotnet test: 1 failed, 0 passed (1 project, 0.00s)

            """);
    }

    [Fact]
    public void Apply_SummaryReportsMoreFailuresThanParsedHeaders_UsesSummaryCount()
    {
        // failedCount = Math.Max(TotalFailed, Failures.Count). When the summary's failed count
        // exceeds the parsed headers, TotalFailed drives the number — so flipping its '+=' to '-='
        // (which Math.Max would otherwise mask when the two are equal) now changes the output.
        const string input = """
                               Failed MyTests.OnlyOneShown [1 ms]
                               Error Message:
                                 boom
                               Stack Trace:
                                  at MyTests.OnlyOneShown() in /path/Test.cs:line 1

                             Failed!  - Failed: 5, Passed: 0, Skipped: 0, Total: 5, Duration: 10 ms - Tests.dll
                             """;

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be(
            """
            FAILURES (5):
              MyTests.OnlyOneShown [1 ms]
                boom
                at ../../../path/Test.cs:line 1
            dotnet test: 5 failed, 0 passed (1 project, 0.01s)

            """);
    }

    [Fact]
    public void Apply_SummaryOnlyFailures_NoParsedHeaders_StillRendersFailureReport()
    {
        // A summary reporting failures with no per-test failure lines (Failures.Count == 0) must
        // still render. This pins the `TotalFailed > 0` half of the verdict condition, which the
        // `|| Failures.Count > 0` half masks whenever headers were parsed.
        const string input =
            "Failed!  - Failed: 2, Passed: 1, Skipped: 0, Total: 3, Duration: 20 ms - Tests.dll";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be("FAILURES (2):\ndotnet test: 2 failed, 1 passed (1 project, 0.02s)\n");
    }

    [Fact]
    public void Apply_MtpSummaryReportsMoreFailuresThanHeaders_UsesSummaryCounts()
    {
        // MTP accumulator counterpart: pins AccumulateMtpSummary's failed/skipped '+=' arithmetic
        // by making the summary counts exceed the single parsed MTP failure line.
        const string input =
            "failed MyTests.T1 (5ms)\nTest summary: total: 8, failed: 3, succeeded: 3, skipped: 2, duration: 1s";

        var result = _sut.Apply(input, exitCode: 1);

        result.Should().Be(
            "FAILURES (3):\n  MyTests.T1 [5ms]\n    \ndotnet test: 3 failed, 3 passed, 2 skipped (1 project, 1.00s)\n");
    }

    [Fact]
    public void Apply_ZeroExit_StrayFailureShapedLine_IsNotReportedAsFailure()
    {
        // A passing run (exit 0) whose output coincidentally contains a failure-shaped line must
        // never be rendered as failed — the exit code is the sole verdict.
        const string raw =
            "  Failed to connect [500 ms]\nPassed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 10 ms - Tests.dll";
        var result = new DotnetTestFilter().Apply(raw, exitCode: 0);
        result.Should().StartWith("✓").And.Contain("3 passed");
        result.Should().NotContain("FAILURES");
    }

    [Fact]
    public void Apply_AllSkipped_ReportsSkippedNotZeroFound()
    {
        // A run where every test is skipped is not "0 tests found" — tests existed, none executed.
        const string raw =
            "Passed!  - Failed:     0, Passed:     0, Skipped:     5, Total:     5, Duration: 10 ms - T.dll";
        var result = new DotnetTestFilter().Apply(raw, exitCode: 0);
        result.Should().Contain("5 skipped").And.NotContain("0 tests found");
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

    [Fact]
    public void Apply_MultiProjectPartialMatch_ReportsPassedCount()
    {
        // Regression: in a multi-project run, a per-assembly "No test matches" line must not
        // discard another assembly's real results. Ground truth for this fixture is 1 passed.
        var fixture = LoadFixture("dotnet_test_multiproject_partial_match.txt");

        var result = _sut.Apply(fixture, exitCode: 0);

        result.Should().Be("✓ dotnet test: 1 passed (1 project, 0.05s)\n");
    }

    [Fact]
    public Task Apply_MultiProjectPartialMatch_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_multiproject_partial_match.txt");
        var result = _sut.Apply(fixture, exitCode: 0);
        return Verify(result);
    }

    [Fact]
    public void Apply_NoTestsLineWithPassedSummary_PrefersPassedCount()
    {
        // Kills the mutant that drops `noTestEvidence` from the guard: a passing summary alongside a
        // no-match line must report the pass.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll\n" +
            "Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 10 ms - A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("✓ dotnet test: 3 passed (1 project, 0.01s)\n");
    }

    [Fact]
    public void Apply_NoTestsLineWithSkippedSummary_ReportsSkipped()
    {
        // Kills the mutant that drops the TotalSkipped clause: skipped tests are evidence that tests
        // exist, so "0 tests found" must not win.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll\n" +
            "Passed!  - Failed:     0, Passed:     0, Skipped:     4, Total:     4, Duration: 10 ms - A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Contain("4 skipped").And.NotContain("0 tests found");
    }

    [Fact]
    public void Apply_NoTestsLineOnly_ReportsZeroWithNoAssemblyMatched()
    {
        // Kills the mutant that drops `state.ZeroTestsFound` from the disjunction: with no summary at
        // all, ProjectCount is 0, so only ZeroTestsFound can produce the verdict.
        const string input =
            "No test matches the given testcase filter `X` in /test/project/root/tests/A.Tests.dll";

        var result = _sut.Apply(input, exitCode: 0);

        result.Should().Be("⚠ dotnet test: 0 tests found (no assembly matched)\n");
    }
}

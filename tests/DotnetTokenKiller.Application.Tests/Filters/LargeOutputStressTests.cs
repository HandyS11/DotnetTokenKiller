using System.Globalization;
using System.Text;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class LargeOutputStressTests
{
    private const int TargetSizeBytes = 10 * 1024 * 1024;

    private const int BuildIterations = 50_000;
    private const int TestIterations = 120_000;
    private const int RestoreIterations = 115_000;
    private const int CleanIterations = 110_000;
    private const int AnsiIterations = 450_000;

    private static string BuildLargeBuildOutput()
    {
        var sb = new StringBuilder(TargetSizeBytes + 4096);
        sb.AppendLine("MSBuild version 17.8.0+6cdef4241 for .NET")
            .AppendLine("Build started 1/1/2025 12:00:00 AM.");
        for (var n = 0; n < BuildIterations; n++)
        {
            var proj = (n % 500).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine("  Determining projects to restore...")
                .AppendLine("  All projects are up-to-date for restore.")
                .AppendLine(CultureInfo.InvariantCulture,
                    $"  Project{proj} -> /repo/src/Project{proj}/bin/Debug/net10.0/Project{proj}.dll")
                .AppendLine(CultureInfo.InvariantCulture,
                    $"  Restored /repo/src/Project{proj}/Project{proj}.csproj (in 42 ms).");
        }

        sb.AppendLine("Build succeeded.")
            .AppendLine("    0 Warning(s)")
            .AppendLine("    0 Error(s)")
            .AppendLine("Time Elapsed 00:00:45.12");
        return sb.ToString();
    }

    private static string BuildLargeTestOutput()
    {
        // Each iteration emits a project header + summary line (~100 bytes) that the filter aggregates.
        var sb = new StringBuilder(TargetSizeBytes + 4096);
        sb.AppendLine("MSBuild version 17.11.9 for .NET")
            .AppendLine("  Determining projects to restore...")
            .AppendLine("  All projects are up-to-date for restore.");
        for (var n = 0; n < TestIterations; n++)
        {
            var proj = (n % 1000).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Passed!  - Failed:     0, Passed:   100, Skipped:     0, Total:   100, Duration: 89 ms - TestProject{proj}.dll (net10.0)");
        }

        return sb.ToString();
    }

    private static string BuildLargeRestoreOutput()
    {
        var sb = new StringBuilder(TargetSizeBytes + 4096);
        for (var n = 0; n < RestoreIterations; n++)
        {
            var proj = (n % 500).ToString(CultureInfo.InvariantCulture);
            var ms = (n % 200).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine("  Determining projects to restore...")
                .AppendLine(CultureInfo.InvariantCulture,
                    $"  Restored /repo/src/Project{proj}/Project{proj}.csproj (in {ms} ms).");
        }

        sb.AppendLine("Build succeeded.")
            .AppendLine("    0 Warning(s)")
            .AppendLine("    0 Error(s)");
        return sb.ToString();
    }

    private static string BuildLargeCleanOutput()
    {
        var sb = new StringBuilder(TargetSizeBytes + 4096);
        sb.AppendLine("Build started 1/1/2025 12:00:00 AM.");
        for (var n = 0; n < CleanIterations; n++)
        {
            var proj = (n % 500).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Cleaning /repo/src/Project{proj}/bin/Debug/net10.0/")
                .AppendLine(CultureInfo.InvariantCulture, $"  Cleaning /repo/src/Project{proj}/obj/Debug/net10.0/");
        }

        sb.AppendLine("Build succeeded.")
            .AppendLine("    0 Warning(s)")
            .AppendLine("    0 Error(s)");
        return sb.ToString();
    }

    private static string BuildLargeAnsiOutput()
    {
        var sb = new StringBuilder(TargetSizeBytes + 4096);
        for (var n = 0; n < AnsiIterations; n++)
        {
            var line = (n % 100000).ToString(CultureInfo.InvariantCulture);
            sb.Append("\e[32m")
                .AppendLine(CultureInfo.InvariantCulture, $"Build line {line}")
                .Append("\e[0m");
            if (n % 1000 == 0)
            {
                sb.Append("\e]0;Terminal Title\a");
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void DotnetBuildFilter_LargeSuccessOutput_DoesNotThrow_AndReducesSize()
    {
        var input = BuildLargeBuildOutput();
        input.Length.Should().BeGreaterThan(TargetSizeBytes);

        var sut = new DotnetBuildFilter();
        var result = sut.Apply(input, exitCode: 0);

        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public void DotnetTestFilter_LargePassingOutput_DoesNotThrow_AndReducesSize()
    {
        var input = BuildLargeTestOutput();
        input.Length.Should().BeGreaterThan(TargetSizeBytes);

        var sut = new DotnetTestFilter();
        var result = sut.Apply(input, exitCode: 0);

        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public void DotnetRestoreFilter_LargeOutput_DoesNotThrow_AndReducesSize()
    {
        var input = BuildLargeRestoreOutput();
        input.Length.Should().BeGreaterThan(TargetSizeBytes);

        var sut = new DotnetRestoreFilter();
        var result = sut.Apply(input, exitCode: 0);

        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public void DotnetCleanFilter_LargeOutput_DoesNotThrow_AndReducesSize()
    {
        var input = BuildLargeCleanOutput();
        input.Length.Should().BeGreaterThan(TargetSizeBytes);

        var sut = new DotnetCleanFilter();
        var result = sut.Apply(input, exitCode: 0);

        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public void AnsiStrip_LargeOutputWithMixedSequences_DoesNotThrow_AndRemovesEscapes()
    {
        var input = BuildLargeAnsiOutput();
        input.Length.Should().BeGreaterThan(TargetSizeBytes);

        var result = AnsiStrip.Strip(input);

        result.Should().NotBeNullOrEmpty();
        result.Should().NotContain("\e");
        result.Length.Should().BeLessThan(input.Length);
    }
}

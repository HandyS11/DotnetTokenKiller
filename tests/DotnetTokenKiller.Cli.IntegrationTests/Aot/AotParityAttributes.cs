using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>Why a parity test is skipped in this environment, or <see langword="null"/> to run it.</summary>
internal static class AotParitySkip
{
    /// <summary>The environment variable naming the dtk binary compared against the JIT build.</summary>
    internal const string AotBinaryVariable = "DTK_AOT_BINARY";

    /// <summary>Returns the skip reason, or <see langword="null"/> when the test should run.</summary>
    /// <param name="unixOnly">Whether the test cannot run on Windows.</param>
    /// <returns>The reason to skip, or <see langword="null"/>.</returns>
    internal static string? Reason(bool unixOnly)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AotBinaryVariable)))
        {
            return $"Set {AotBinaryVariable} to a dtk binary to compare it with the JIT build.";
        }

        return unixOnly && OperatingSystem.IsWindows()
            ? "Unix only: needs a POSIX fake dotnet, or a home directory that USERPROFILE can move."
            : null;
    }
}

/// <summary>A theory that runs only when <c>DTK_AOT_BINARY</c> is set.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityTheoryAttribute : TheoryAttribute
{
    public AotParityTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: false);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>A theory that runs only when <c>DTK_AOT_BINARY</c> is set and the OS is not Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityUnixTheoryAttribute : TheoryAttribute
{
    public AotParityUnixTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: true);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>A fact that runs only when <c>DTK_AOT_PACK_LOG</c> names the log of a Native AOT pack.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotPackLogFactAttribute : FactAttribute
{
    /// <summary>The environment variable naming the pack log to check.</summary>
    internal const string PackLogVariable = "DTK_AOT_PACK_LOG";

    public AotPackLogFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PackLogVariable)))
        {
            Skip = $"Set {PackLogVariable} to the log of a 'dotnet pack -r <rid>' run to check its warnings.";
        }
    }
}

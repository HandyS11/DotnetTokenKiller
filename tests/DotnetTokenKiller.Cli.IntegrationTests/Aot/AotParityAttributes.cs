using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>Why a parity test is skipped in this environment, or <see langword="null"/> to run it.</summary>
internal static class AotParitySkip
{
    /// <summary>The environment variable naming the dtk binary compared against the JIT build.</summary>
    internal const string AotBinaryVariable = "DTK_AOT_BINARY";

    /// <summary>
    /// The environment variable that, set to <c>1</c>, turns a missing <c>DTK_AOT_BINARY</c> or
    /// <c>DTK_AOT_PACK_LOG</c> into a failure instead of a skip. CI sets it, so the gates cannot pass by
    /// skipping.
    /// </summary>
    internal const string RequiredVariable = "DTK_AOT_REQUIRED";

    /// <summary>Returns the skip reason, or <see langword="null"/> when the test should run.</summary>
    /// <param name="unixOnly">Whether the test cannot run on Windows.</param>
    /// <returns>The reason to skip, or <see langword="null"/>.</returns>
    internal static string? Reason(bool unixOnly) =>
        Reason(
            unixOnly,
            Environment.GetEnvironmentVariable(AotBinaryVariable),
            Environment.GetEnvironmentVariable(RequiredVariable),
            OperatingSystem.IsWindows());

    /// <summary>Returns the skip reason, or <see langword="null"/> when the test should run.</summary>
    /// <param name="unixOnly">Whether the test cannot run on Windows.</param>
    /// <param name="aotBinary">The value of <c>DTK_AOT_BINARY</c>.</param>
    /// <param name="required">The value of <c>DTK_AOT_REQUIRED</c>.</param>
    /// <param name="isWindows">Whether the tests run on Windows.</param>
    /// <returns>The reason to skip, or <see langword="null"/>.</returns>
    internal static string? Reason(bool unixOnly, string? aotBinary, string? required, bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(aotBinary) && !IsRequired(required))
        {
            return $"Set {AotBinaryVariable} to a dtk binary to compare it with the JIT build.";
        }

        return unixOnly && isWindows
            ? "Unix only: needs a POSIX fake dotnet, or a home directory that USERPROFILE can move."
            : null;
    }

    /// <summary>Whether <c>DTK_AOT_REQUIRED</c> demands the AOT inputs.</summary>
    /// <param name="required">The value of <c>DTK_AOT_REQUIRED</c>.</param>
    /// <returns><see langword="true"/> when the value is <c>1</c>.</returns>
    internal static bool IsRequired(string? required) => string.Equals(required, "1", StringComparison.Ordinal);

    /// <summary>Reads a required AOT input, failing clearly when it is blank.</summary>
    /// <param name="variable">The environment variable to read.</param>
    /// <returns>The trimmed value.</returns>
    /// <exception cref="InvalidOperationException">The variable is unset or blank.</exception>
    internal static string ReadRequired(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{variable} is not set, and {RequiredVariable}=1 demands it: set it, or unset {RequiredVariable} to skip.")
            : value.Trim();
    }
}

/// <summary>A theory that runs only when <c>DTK_AOT_BINARY</c> is set or <c>DTK_AOT_REQUIRED</c> is <c>1</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityTheoryAttribute : TheoryAttribute
{
    public AotParityTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: false);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>
/// A theory that runs only when <c>DTK_AOT_BINARY</c> is set or <c>DTK_AOT_REQUIRED</c> is <c>1</c>, and
/// the OS is not Windows.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotParityUnixTheoryAttribute : TheoryAttribute
{
    public AotParityUnixTheoryAttribute()
    {
        Skip = AotParitySkip.Reason(unixOnly: true);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>
/// A fact that runs only when <c>DTK_AOT_PACK_LOG</c> names the log of a Native AOT pack or
/// <c>DTK_AOT_REQUIRED</c> is <c>1</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotPackLogFactAttribute : FactAttribute
{
    /// <summary>The environment variable naming the pack log to check.</summary>
    internal const string PackLogVariable = "DTK_AOT_PACK_LOG";

    public AotPackLogFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PackLogVariable))
            && !AotParitySkip.IsRequired(Environment.GetEnvironmentVariable(AotParitySkip.RequiredVariable)))
        {
            Skip = $"Set {PackLogVariable} to the log of a 'dotnet pack -r <rid>' run to check its warnings.";
        }
    }
}

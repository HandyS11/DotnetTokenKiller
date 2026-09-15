using DotnetTokenKiller.Application.Helpers;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>Why a Node-dependent test is skipped in this environment, or <see langword="null"/> to run it.</summary>
internal static class NodeSkip
{
    /// <summary>The variable that turns a missing Node.js into a failure instead of a skip.</summary>
    internal const string RequiredVariable = "DTK_NODE_REQUIRED";

    /// <summary>Returns the skip reason, or <see langword="null"/> when the test should run.</summary>
    /// <param name="unixOnly">Whether the test also needs a POSIX shell script on <c>PATH</c>.</param>
    internal static string? Reason(bool unixOnly)
    {
        if (unixOnly && OperatingSystem.IsWindows())
        {
            return "Unix only: uses a shell-script dtk.";
        }

        return ExecutableSearch.FindOnProcessPath("node") is null
            && Environment.GetEnvironmentVariable(RequiredVariable) != "1"
            ? $"Node.js is not on PATH. Set {RequiredVariable}=1 to fail instead."
            : null;
    }
}

/// <summary>A fact that needs Node.js on <c>PATH</c>; CI sets <c>DTK_NODE_REQUIRED=1</c> so it fails instead of skipping.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NodeFactAttribute : FactAttribute
{
    public NodeFactAttribute()
    {
        Skip = NodeSkip.Reason(unixOnly: false);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

/// <summary>
/// A fact that needs Node.js on <c>PATH</c> and a POSIX shell-script <c>dtk</c>; CI sets
/// <c>DTK_NODE_REQUIRED=1</c> so a missing Node.js fails instead of skipping, and it is always skipped on
/// Windows.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NodeUnixFactAttribute : FactAttribute
{
    public NodeUnixFactAttribute()
    {
        Skip = NodeSkip.Reason(unixOnly: true);
        Timeout = IntegrationTestHelper.DefaultTimeoutMs;
    }
}

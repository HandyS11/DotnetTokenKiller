using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>A theory that is skipped on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix only.";
        }
    }
}

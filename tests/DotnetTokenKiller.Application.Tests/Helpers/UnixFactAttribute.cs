using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

/// <summary>A fact that is skipped on Windows, for tests that run a POSIX script.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "POSIX scripts only.";
        }
    }
}

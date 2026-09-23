using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Helpers;

/// <summary>A fact that is skipped everywhere except Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only.";
        }
    }
}

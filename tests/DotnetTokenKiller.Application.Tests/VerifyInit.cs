using System.Runtime.CompilerServices;
using VerifyXunit;

namespace DotnetTokenKiller.Application.Tests;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Verifier.UseProjectRelativeDirectory("Snapshots");
        VerifierSettings.IgnoreStackTrace();
        VerifierSettings.ScrubLinesWithReplace(line => line.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}

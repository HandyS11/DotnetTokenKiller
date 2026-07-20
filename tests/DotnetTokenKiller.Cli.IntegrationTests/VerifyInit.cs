using System.Runtime.CompilerServices;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        UseProjectRelativeDirectory("Snapshots");
        VerifierSettings.IgnoreStackTrace();
        VerifierSettings.ScrubLinesWithReplace(line => line.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}

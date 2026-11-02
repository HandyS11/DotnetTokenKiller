using System.Runtime.CompilerServices;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>Keeps a raw-output request made for the test run itself away from the dtk runs the tests spawn.</summary>
public static class RawOutputIsolation
{
    /// <summary>
    /// Clears <c>DTK_RAW</c> from this process, whose environment every spawned dtk inherits: a suite run under
    /// <c>DTK_RAW=1</c> (an agent asking for the raw test output) would otherwise turn every filtered run under test
    /// into a passthrough. A test that wants raw output sets the variable on its own child.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize() => Environment.SetEnvironmentVariable("DTK_RAW", null);
}

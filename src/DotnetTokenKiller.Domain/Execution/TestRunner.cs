namespace DotnetTokenKiller.Domain.Execution;

/// <summary>The test platform a <c>dotnet test</c> run uses, which decides the arguments it accepts.</summary>
public enum TestRunner
{
    /// <summary>The runner could not be determined; dtk must add no runner-specific argument.</summary>
    Unknown = 0,

    /// <summary>VSTest, <c>dotnet test</c>'s default.</summary>
    VsTest = 1,

    /// <summary>Microsoft.Testing.Platform, opted into through <c>global.json</c>.</summary>
    MicrosoftTestingPlatform = 2
}

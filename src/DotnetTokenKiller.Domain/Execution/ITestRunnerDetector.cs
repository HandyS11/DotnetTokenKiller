namespace DotnetTokenKiller.Domain.Execution;

/// <summary>Tells which test platform a <c>dotnet test</c> run started in a directory uses.</summary>
public interface ITestRunnerDetector
{
    /// <summary>Detects the runner <c>dotnet test</c> uses when run from <paramref name="workingDirectory"/>.</summary>
    /// <param name="workingDirectory">The directory the run starts in.</param>
    /// <returns>The runner, or <see cref="TestRunner.Unknown"/> when it cannot be told.</returns>
    TestRunner Detect(string workingDirectory);
}

namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>A record of a single command execution and its token statistics.</summary>
/// <param name="Timestamp">When the command ran.</param>
/// <param name="Command">The dotnet subcommand name.</param>
/// <param name="ProjectPath">The working directory when the command ran.</param>
/// <param name="InputTokens">Estimated tokens in the raw output.</param>
/// <param name="OutputTokens">Estimated tokens in the filtered output.</param>
/// <param name="SavedTokens">Tokens saved by filtering.</param>
/// <param name="SavingsPercentage">Percentage of tokens saved.</param>
/// <param name="ExecutionTime">Total wall-clock time for the command.</param>
public sealed record CommandRecord(
    DateTimeOffset Timestamp,
    string Command,
    string ProjectPath,
    int InputTokens,
    int OutputTokens,
    int SavedTokens,
    double SavingsPercentage,
    TimeSpan ExecutionTime);

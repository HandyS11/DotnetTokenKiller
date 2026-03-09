namespace DotnetTokenKiller.Domain.Tracking;

public sealed record CommandRecord(
    DateTimeOffset Timestamp,
    string Command,
    string ProjectPath,
    int InputTokens,
    int OutputTokens,
    int SavedTokens,
    double SavingsPercentage,
    TimeSpan ExecutionTime);

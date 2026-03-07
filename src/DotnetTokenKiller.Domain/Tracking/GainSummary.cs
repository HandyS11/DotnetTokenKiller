namespace DotnetTokenKiller.Domain.Tracking;

public sealed record GainSummary(
    int TotalCommands,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage,
    IReadOnlyDictionary<string, int> SavedByCommand);

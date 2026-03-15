namespace DotnetTokenKiller.Domain.Tracking;

public sealed record CommandGainDetail(
    int RunCount,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage);

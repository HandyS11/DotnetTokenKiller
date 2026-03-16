namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Token-savings detail for a single command type.</summary>
/// <param name="RunCount">Number of times this command was run.</param>
/// <param name="TotalInputTokens">Total raw output tokens.</param>
/// <param name="TotalOutputTokens">Total filtered output tokens.</param>
/// <param name="TotalSavedTokens">Total tokens saved.</param>
/// <param name="AverageSavingsPercentage">Average savings percentage.</param>
public sealed record CommandGainDetail(
    int RunCount,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage);

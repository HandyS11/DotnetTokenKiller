namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Aggregated token-savings summary across one or more commands.</summary>
/// <param name="TotalCommands">Total number of commands run.</param>
/// <param name="TotalInputTokens">Sum of raw output tokens across all commands.</param>
/// <param name="TotalOutputTokens">Sum of filtered output tokens across all commands.</param>
/// <param name="TotalSavedTokens">Total tokens saved across all commands.</param>
/// <param name="AverageSavingsPercentage">Average savings percentage across command types.</param>
/// <param name="CommandDetails">Per-command breakdown.</param>
public sealed record GainSummary(
    int TotalCommands,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage,
    IReadOnlyDictionary<string, CommandGainDetail> CommandDetails);

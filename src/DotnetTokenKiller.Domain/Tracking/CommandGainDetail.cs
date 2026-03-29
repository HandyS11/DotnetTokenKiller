namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Token-savings detail for a single command type.</summary>
/// <param name="RunCount">Number of times this command was run.</param>
/// <param name="TotalInputTokens">Total raw output tokens.</param>
/// <param name="TotalOutputTokens">Total filtered output tokens.</param>
/// <param name="TotalSavedTokens">Total tokens saved.</param>
/// <param name="AverageSavingsPercentage">Average savings percentage.</param>
/// <param name="SuccessDetail">Detail for runs that exited with code 0, or <see langword="null"/> if not available.</param>
/// <param name="FailureDetail">Detail for runs that exited with a non-zero code, or <see langword="null"/> if none.</param>
public sealed record CommandGainDetail(
    int RunCount,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage,
    CommandGainDetail? SuccessDetail = null,
    CommandGainDetail? FailureDetail = null);

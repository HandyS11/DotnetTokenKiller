namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Token usage statistics for a single command run.</summary>
/// <param name="Input">Estimated tokens in the raw output.</param>
/// <param name="Output">Estimated tokens in the filtered output.</param>
/// <param name="Saved">Tokens saved by filtering.</param>
/// <param name="SavingsPercentage">Percentage of tokens saved.</param>
public sealed record TokenStatistics(int Input, int Output, int Saved, double SavingsPercentage);

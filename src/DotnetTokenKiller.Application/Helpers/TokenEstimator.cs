using Microsoft.ML.Tokenizers;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Estimates token counts using the cl100k_base tokenizer.</summary>
public static class TokenEstimator
{
    private static readonly Lazy<TiktokenTokenizer> Tokenizer =
        new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));

    /// <summary>Returns the estimated token count for the given text.</summary>
    /// <param name="text">The text to estimate tokens for.</param>
    public static int Estimate(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : Tokenizer.Value.CountTokens(text);
    }
}
